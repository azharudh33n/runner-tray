using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RunnerTray;

/// <summary>
/// The only way RunnerTray talks to the GitHub REST API. It follows GitHub's best practices:
/// - one request at a time (no parallel requests), with a short gap between requests;
/// - conditional requests with ETags, so unchanged data costs no rate limit;
/// - obeys retry-after and x-ratelimit-reset, and backs off exponentially on errors;
/// - refreshes the token once after a 401.
/// </summary>
public sealed partial class ApiClient : IDisposable
{
    private static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(100);
    private const int MaxAttempts = 4;

    private readonly HttpClient _http;
    private readonly ITokenSource _tokens;
    private readonly ResponseCache _cache;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public ApiClient(
        ITokenSource tokens,
        ResponseCache cache,
        HttpMessageHandler? handler = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? now = null)
    {
        _tokens = tokens;
        _cache = cache;
        _delay = delay ?? ((t, ct) => Task.Delay(t, ct));
        _now = now ?? (() => DateTimeOffset.UtcNow);
        handler ??= new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"RunnerTray/{typeof(ApiClient).Assembly.GetName().Version?.ToString(3)}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public ITokenSource Tokens => _tokens;

    /// <summary>Requests left in the current rate-limit window, as GitHub last reported.</summary>
    public int? RateRemaining { get; private set; }

    public int? RateLimit { get; private set; }

    public DateTimeOffset? RateReset { get; private set; }

    /// <summary>Do not poll more often than this (GitHub's x-poll-interval).</summary>
    public TimeSpan? MinPollInterval { get; private set; }

    /// <summary>Set while GitHub has asked us to wait.</summary>
    public DateTimeOffset? PausedUntil { get; private set; }

    public long RequestCount { get; private set; }

    public long NotModifiedCount { get; private set; }

    public DateTimeOffset? LastSuccess { get; private set; }

    /// <summary>Forget cached responses, for example after a different account signs in.</summary>
    public void ClearCache() => _cache.Clear();

    /// <summary>GET a path (relative to https://api.github.com/) and return the JSON body.</summary>
    public async Task<JsonDocument> GetJsonAsync(string path, CancellationToken ct)
    {
        var (body, _) = await GetAsync(path, ct);
        return JsonDocument.Parse(body);
    }

    /// <summary>
    /// GET every page of a list. <paramref name="keepGoing"/> sees each page and can stop early.
    /// </summary>
    public async Task<List<JsonDocument>> GetPagesAsync(
        string path, int maxPages, Func<JsonDocument, bool> keepGoing, CancellationToken ct)
    {
        var pages = new List<JsonDocument>();
        string? next = path;
        while (next is not null && pages.Count < maxPages)
        {
            var (body, nextPage) = await GetAsync(next, ct);
            var doc = JsonDocument.Parse(body);
            pages.Add(doc);
            if (!keepGoing(doc))
            {
                break;
            }

            next = nextPage;
        }

        return pages;
    }

    /// <summary>GET with all the rules above. Returns the body and the next page link, if any.</summary>
    public async Task<(string Body, string? NextPage)> GetAsync(string path, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = _now();
            if (PausedUntil is { } until && until > now)
            {
                throw new GitHubException(FailureKind.RateLimited, $"GitHub rate limit: paused until {until.ToLocalTime():HH:mm}.", until);
            }

            var refreshed = false;
            for (var attempt = 1; ; attempt++)
            {
                var wait = _lastRequest + Gap - _now();
                if (wait > TimeSpan.Zero)
                {
                    await _delay(wait, ct);
                }

                var token = await _tokens.GetTokenAsync(ct)
                            ?? throw new GitHubException(FailureKind.SignInRequired, "Not signed in to GitHub.");

                var cacheKey = $"{_tokens.Kind}|{path}";
                var cached = _cache.TryGet(cacheKey, out var c) ? c : null;

                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (cached is not null && EntityTagHeaderValue.TryParse(cached.ETag, out var etag))
                {
                    request.Headers.IfNoneMatch.Add(etag);
                }

                HttpResponseMessage response;
                try
                {
                    RequestCount++;
                    response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
                {
                    _lastRequest = _now();
                    if (attempt < MaxAttempts)
                    {
                        await _delay(Backoff(attempt), ct);
                        continue;
                    }

                    throw new GitHubException(FailureKind.Offline, "Cannot reach GitHub. Check your internet connection.", inner: ex);
                }

                using (response)
                {
                    _lastRequest = _now();
                    ReadRateHeaders(response);
                    var status = (int)response.StatusCode;

                    if (status == 304 && cached is not null)
                    {
                        NotModifiedCount++;
                        LastSuccess = _now();
                        return (cached.Body, cached.NextPage);
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync(ct);
                        var next = NextLink(response);
                        if (response.Headers.ETag is { } tag)
                        {
                            _cache.Put(cacheKey, new CachedResponse(tag.ToString(), body, next));
                        }

                        LastSuccess = _now();
                        return (body, next);
                    }

                    var message = await ErrorMessageAsync(response, ct);

                    if (status == 401)
                    {
                        if (!refreshed && await _tokens.TryRefreshAsync(ct))
                        {
                            refreshed = true;
                            Log.Info("GitHub answered 401; token refreshed, trying again.");
                            continue;
                        }

                        throw new GitHubException(FailureKind.SignInRequired, "Your GitHub sign-in has expired. Sign in again.");
                    }

                    if (IsRateLimited(response, status, message))
                    {
                        var retryAt = RetryTime(response, attempt);
                        if (retryAt - _now() <= TimeSpan.FromSeconds(10) && attempt < MaxAttempts)
                        {
                            await _delay(retryAt - _now(), ct);
                            continue;
                        }

                        PausedUntil = retryAt;
                        Log.Warn($"Rate limited until {retryAt:u}: {message}");
                        throw new GitHubException(FailureKind.RateLimited,
                            $"GitHub rate limit reached. Paused until {retryAt.ToLocalTime():HH:mm}.", retryAt);
                    }

                    if (status >= 500)
                    {
                        if (attempt < MaxAttempts)
                        {
                            await _delay(Backoff(attempt), ct);
                            continue;
                        }

                        throw new GitHubException(FailureKind.ServerError, $"GitHub is having problems ({status}). Trying again later.");
                    }

                    throw status switch
                    {
                        404 => new GitHubException(FailureKind.NotFound, $"Not found: {path}"),
                        403 => new GitHubException(FailureKind.Forbidden, $"No access: {message}"),
                        _ => new GitHubException(FailureKind.Unexpected, $"GitHub answered {status}: {message}"),
                    };
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>1 s, 2 s, 4 s, ... with up to 25% random jitter, at most 60 s.</summary>
    internal static TimeSpan Backoff(int attempt)
    {
        var seconds = Math.Min(60, Math.Pow(2, attempt - 1));
        return TimeSpan.FromSeconds(seconds * (1 + Random.Shared.NextDouble() * 0.25));
    }

    private void ReadRateHeaders(HttpResponseMessage r)
    {
        if (Header(r, "x-ratelimit-remaining") is { } rem && int.TryParse(rem, out var remaining)) RateRemaining = remaining;
        if (Header(r, "x-ratelimit-limit") is { } lim && int.TryParse(lim, out var limit)) RateLimit = limit;
        if (Header(r, "x-ratelimit-reset") is { } res && long.TryParse(res, out var reset)) RateReset = DateTimeOffset.FromUnixTimeSeconds(reset);
        if (Header(r, "x-poll-interval") is { } poll && int.TryParse(poll, out var seconds)) MinPollInterval = TimeSpan.FromSeconds(seconds);
        if (RateRemaining > 0 && PausedUntil is not null && PausedUntil <= _now()) PausedUntil = null;
    }

    private static bool IsRateLimited(HttpResponseMessage r, int status, string message) =>
        status == 429
        || (status == 403 && (Header(r, "retry-after") is not null
                              || Header(r, "x-ratelimit-remaining") == "0"
                              || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)));

    private DateTimeOffset RetryTime(HttpResponseMessage r, int attempt)
    {
        if (Header(r, "retry-after") is { } ra && int.TryParse(ra, out var seconds))
        {
            return _now().AddSeconds(seconds);
        }

        if (Header(r, "x-ratelimit-remaining") == "0" && RateReset is { } reset && reset > _now())
        {
            return reset.AddSeconds(1);
        }

        // GitHub: otherwise wait at least one minute, longer on repeated errors.
        return _now().AddSeconds(60 * Math.Pow(2, attempt - 1));
    }

    private static string? Header(HttpResponseMessage r, string name) =>
        r.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    [GeneratedRegex("<([^>]+)>;\\s*rel=\"next\"")]
    private static partial Regex NextPattern();

    /// <summary>The next page from the Link header, as a path relative to api.github.com.</summary>
    internal static string? NextLink(HttpResponseMessage r)
    {
        var link = Header(r, "link");
        if (link is null) return null;
        var m = NextPattern().Match(link);
        if (!m.Success) return null;
        var url = m.Groups[1].Value;
        const string root = "https://api.github.com/";
        return url.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? url[root.Length..] : url;
    }

    private static async Task<string> ErrorMessageAsync(HttpResponseMessage r, CancellationToken ct)
    {
        try
        {
            var text = await r.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
            return r.ReasonPhrase ?? "";
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
