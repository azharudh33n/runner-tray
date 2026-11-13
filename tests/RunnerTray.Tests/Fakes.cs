using System.Net;
using System.Text;

namespace RunnerTray.Tests;

/// <summary>An HTTP handler that answers from a function and records every request.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, Task<HttpResponseMessage>> _respond;
    private int _active;

    public FakeHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        : this((r, n) => Task.FromResult(respond(r, n)))
    {
    }

    public FakeHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    public List<string> Bodies { get; } = new();

    public int MaxConcurrent { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var now = Interlocked.Increment(ref _active);
        MaxConcurrent = Math.Max(MaxConcurrent, now);
        try
        {
            lock (Requests)
            {
                Requests.Add(request);
                Bodies.Add(request.Content is null ? "" : request.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult());
            }

            await Task.Yield();
            return await _respond(request, Requests.Count);
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK, string? etag = null,
        Dictionary<string, string>? headers = null)
    {
        var r = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (etag is not null) r.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        foreach (var (k, v) in headers ?? new Dictionary<string, string>()) r.Headers.TryAddWithoutValidation(k, v);
        return r;
    }

    public static HttpResponseMessage Status(HttpStatusCode status, string message = "error", Dictionary<string, string>? headers = null) =>
        Json($"{{\"message\":\"{message}\"}}", status, headers: headers);
}

/// <summary>A token source with a fixed token and a scripted refresh.</summary>
internal sealed class FakeTokens : ITokenSource
{
    private readonly Queue<string?> _refreshes;

    public FakeTokens(string? token, params string?[] refreshes)
    {
        Token = token;
        _refreshes = new Queue<string?>(refreshes);
    }

    public string? Token { get; private set; }

    public int RefreshCalls { get; private set; }

    public TokenKind Kind { get; set; } = TokenKind.GitHubCli;

    public Task<string?> GetTokenAsync(CancellationToken ct) => Task.FromResult(Token);

    public Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        RefreshCalls++;
        if (_refreshes.Count == 0) return Task.FromResult(false);
        Token = _refreshes.Dequeue();
        return Task.FromResult(Token is not null);
    }
}

/// <summary>A clock that moves only when a delay is awaited.</summary>
internal sealed class FakeClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    public List<TimeSpan> Delays { get; } = new();

    public Task Delay(TimeSpan t, CancellationToken ct)
    {
        Delays.Add(t);
        if (t > TimeSpan.Zero) Now += t;
        return Task.CompletedTask;
    }
}
