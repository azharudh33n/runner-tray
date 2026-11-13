using System.Net.Http.Headers;
using System.Text.Json;

namespace RunnerTray;

/// <summary>The code the user types at github.com/login/device.</summary>
public sealed record DeviceCode(string Code, string UserCode, string VerificationUri, TimeSpan ExpiresIn, TimeSpan Interval);

/// <summary>A user access token, with its refresh token when token expiration is on.</summary>
public sealed record TokenSet(string AccessToken, string? RefreshToken, DateTimeOffset? AccessExpires, DateTimeOffset? RefreshExpires);

public enum DeviceFlowError { Expired, Denied, BadRefreshToken, Disabled, Network, Other }

public sealed class DeviceFlowException : Exception
{
    public DeviceFlowException(DeviceFlowError error, string message, Exception? inner = null) : base(message, inner)
    {
        Error = error;
    }

    public DeviceFlowError Error { get; }
}

/// <summary>
/// GitHub App device flow. Needs only the public client ID: no client secret, for sign-in and for refresh.
/// https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app
/// </summary>
public sealed class DeviceFlowClient : IDisposable
{
    private readonly string _clientId;
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _now;

    public DeviceFlowClient(
        string clientId,
        HttpMessageHandler? handler = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? now = null)
    {
        _clientId = clientId;
        _delay = delay ?? ((t, ct) => Task.Delay(t, ct));
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _http = new HttpClient(handler ?? new SocketsHttpHandler())
        {
            BaseAddress = new Uri("https://github.com/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RunnerTray");
    }

    /// <summary>Step 1: get a user code to show.</summary>
    public async Task<DeviceCode> StartAsync(CancellationToken ct)
    {
        using var doc = await PostAsync("login/device/code", new Dictionary<string, string> { ["client_id"] = _clientId }, ct);
        var root = doc.RootElement;
        ThrowOnError(root);
        return new DeviceCode(
            root.GetProperty("device_code").GetString()!,
            root.GetProperty("user_code").GetString()!,
            root.GetProperty("verification_uri").GetString()!,
            TimeSpan.FromSeconds(root.GetProperty("expires_in").GetInt32()),
            TimeSpan.FromSeconds(root.TryGetProperty("interval", out var i) ? i.GetInt32() : 5));
    }

    /// <summary>Step 2: wait until the user approves, at the interval GitHub asks for.</summary>
    public async Task<TokenSet> WaitForTokenAsync(DeviceCode code, CancellationToken ct)
    {
        var interval = code.Interval;
        var deadline = _now() + code.ExpiresIn;
        while (true)
        {
            await _delay(interval, ct);
            if (_now() > deadline)
            {
                throw new DeviceFlowException(DeviceFlowError.Expired, "The code expired. Start again to get a new code.");
            }

            using var doc = await PostAsync("login/oauth/access_token", new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["device_code"] = code.Code,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            }, ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("access_token", out _))
            {
                return ParseTokens(root);
            }

            switch (Error(root))
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    // GitHub: add 5 seconds, or use the interval it sends.
                    interval = root.TryGetProperty("interval", out var ni) ? TimeSpan.FromSeconds(ni.GetInt32()) : interval + TimeSpan.FromSeconds(5);
                    continue;
                default:
                    ThrowOnError(root);
                    throw new DeviceFlowException(DeviceFlowError.Other, "GitHub returned no token.");
            }
        }
    }

    /// <summary>Get a new token pair. Refresh tokens work once: always save the new pair.</summary>
    public async Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        using var doc = await PostAsync("login/oauth/access_token", new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, ct);
        var root = doc.RootElement;
        if (root.TryGetProperty("access_token", out _))
        {
            return ParseTokens(root);
        }

        ThrowOnError(root);
        throw new DeviceFlowException(DeviceFlowError.Other, "GitHub returned no token.");
    }

    private TokenSet ParseTokens(JsonElement root)
    {
        var now = _now();
        return new TokenSet(
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
            root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var es) ? now.AddSeconds(es) : null,
            root.TryGetProperty("refresh_token_expires_in", out var re) && re.TryGetInt32(out var rs) ? now.AddSeconds(rs) : null);
    }

    private static string? Error(JsonElement root) =>
        root.TryGetProperty("error", out var e) ? e.GetString() : null;

    private static void ThrowOnError(JsonElement root)
    {
        var error = Error(root);
        if (error is null)
        {
            return;
        }

        var description = root.TryGetProperty("error_description", out var d) ? d.GetString() : error;
        throw error switch
        {
            "expired_token" => new DeviceFlowException(DeviceFlowError.Expired, "The code expired. Start again to get a new code."),
            "access_denied" => new DeviceFlowException(DeviceFlowError.Denied, "Sign-in was cancelled on GitHub."),
            "bad_refresh_token" => new DeviceFlowException(DeviceFlowError.BadRefreshToken, "Your sign-in expired. Sign in again."),
            "device_flow_disabled" => new DeviceFlowException(DeviceFlowError.Disabled, "Device sign-in is turned off for this app."),
            _ => new DeviceFlowException(DeviceFlowError.Other, $"GitHub sign-in failed: {description}"),
        };
    }

    private async Task<JsonDocument> PostAsync(string path, Dictionary<string, string> form, CancellationToken ct)
    {
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await _http.PostAsync(path, content, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode && !text.TrimStart().StartsWith('{'))
            {
                throw new DeviceFlowException(DeviceFlowError.Other, $"GitHub answered {(int)response.StatusCode}.");
            }

            return JsonDocument.Parse(text);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new DeviceFlowException(DeviceFlowError.Network, "Cannot reach GitHub. Check your internet connection.", ex);
        }
        catch (JsonException ex)
        {
            throw new DeviceFlowException(DeviceFlowError.Other, "GitHub sent an unexpected answer.", ex);
        }
    }

    public void Dispose() => _http.Dispose();
}
