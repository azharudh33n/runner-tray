using System.Net;
using Xunit;

namespace RunnerTray.Tests;

public class ApiClientTests
{
    private static ApiClient Client(FakeHandler handler, ITokenSource? tokens = null, FakeClock? clock = null)
    {
        clock ??= new FakeClock();
        return new ApiClient(tokens ?? new FakeTokens("t1"), new ResponseCache(null), handler, clock.Delay, () => clock.Now);
    }

    [Fact]
    public async Task Second_request_sends_etag_and_reuses_body_on_304()
    {
        var handler = new FakeHandler((req, n) => n == 1
            ? FakeHandler.Json("{\"a\":1}", etag: "\"v1\"")
            : new HttpResponseMessage(HttpStatusCode.NotModified));
        using var api = Client(handler);

        var first = await api.GetAsync("x", default);
        var second = await api.GetAsync("x", default);

        Assert.Equal("{\"a\":1}", first.Body);
        Assert.Equal("{\"a\":1}", second.Body);
        Assert.Empty(handler.Requests[0].Headers.IfNoneMatch);
        Assert.Equal("\"v1\"", handler.Requests[1].Headers.IfNoneMatch.Single().Tag);
        Assert.Equal(1, api.NotModifiedCount);
    }

    [Fact]
    public async Task Requests_run_one_at_a_time()
    {
        var handler = new FakeHandler(async (req, n) =>
        {
            await Task.Delay(20);
            return FakeHandler.Json("{}");
        });
        using var api = new ApiClient(new FakeTokens("t"), new ResponseCache(null), handler);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(i => api.GetAsync($"p{i}", default)));

        Assert.Equal(6, handler.Requests.Count);
        Assert.Equal(1, handler.MaxConcurrent);
    }

    [Fact]
    public async Task Unauthorized_refreshes_token_once_and_retries()
    {
        var handler = new FakeHandler((req, n) => req.Headers.Authorization!.Parameter == "t2"
            ? FakeHandler.Json("{}")
            : FakeHandler.Status(HttpStatusCode.Unauthorized));
        var tokens = new FakeTokens("t1", "t2");
        using var api = Client(handler, tokens);

        await api.GetAsync("x", default);

        Assert.Equal(1, tokens.RefreshCalls);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Unauthorized_after_refresh_asks_for_sign_in()
    {
        var handler = new FakeHandler((req, n) => FakeHandler.Status(HttpStatusCode.Unauthorized));
        using var api = Client(handler, new FakeTokens("t1", "t2"));

        var ex = await Assert.ThrowsAsync<GitHubException>(() => api.GetAsync("x", default));

        Assert.Equal(FailureKind.SignInRequired, ex.Kind);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task No_token_asks_for_sign_in_without_calling_GitHub()
    {
        var handler = new FakeHandler((req, n) => FakeHandler.Json("{}"));
        using var api = Client(handler, new FakeTokens(null));

        var ex = await Assert.ThrowsAsync<GitHubException>(() => api.GetAsync("x", default));

        Assert.Equal(FailureKind.SignInRequired, ex.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Rate_limit_pauses_until_reset_and_blocks_further_requests()
    {
        var clock = new FakeClock();
        var reset = clock.Now.AddMinutes(20).ToUnixTimeSeconds();
        var handler = new FakeHandler((req, n) => FakeHandler.Status(HttpStatusCode.Forbidden, "API rate limit exceeded",
            new() { ["x-ratelimit-remaining"] = "0", ["x-ratelimit-reset"] = reset.ToString() }));
        using var api = Client(handler, clock: clock);

        var ex = await Assert.ThrowsAsync<GitHubException>(() => api.GetAsync("x", default));
        Assert.Equal(FailureKind.RateLimited, ex.Kind);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(reset).AddSeconds(1), ex.RetryAt);

        var again = await Assert.ThrowsAsync<GitHubException>(() => api.GetAsync("y", default));
        Assert.Equal(FailureKind.RateLimited, again.Kind);
        Assert.Single(handler.Requests); // no request while paused
    }

    [Fact]
    public async Task Short_retry_after_is_waited_then_retried()
    {
        var clock = new FakeClock();
        var handler = new FakeHandler((req, n) => n == 1
            ? FakeHandler.Status((HttpStatusCode)429, "slow down", new() { ["retry-after"] = "3" })
            : FakeHandler.Json("{\"ok\":true}"));
        using var api = Client(handler, clock: clock);

        var result = await api.GetAsync("x", default);

        Assert.Contains("ok", result.Body, StringComparison.Ordinal);
        Assert.Contains(clock.Delays, d => d >= TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Server_errors_are_retried_with_backoff()
    {
        var clock = new FakeClock();
        var handler = new FakeHandler((req, n) => n < 3 ? FakeHandler.Status(HttpStatusCode.BadGateway) : FakeHandler.Json("{}"));
        using var api = Client(handler, clock: clock);

        await api.GetAsync("x", default);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains(clock.Delays, d => d >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Persistent_server_errors_report_outage()
    {
        var handler = new FakeHandler((req, n) => FakeHandler.Status(HttpStatusCode.ServiceUnavailable));
        using var api = Client(handler);

        var ex = await Assert.ThrowsAsync<GitHubException>(() => api.GetAsync("x", default));

        Assert.Equal(FailureKind.ServerError, ex.Kind);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Network_failure_reports_offline()
    {
        var handler = new FakeHandler((Func<HttpRequestMessage, int, HttpResponseMessage>)((req, n) => throw new HttpRequestException("no network")));
        using var api = Client(handler);

        var ex = await Assert.ThrowsAsync<GitHubException>(() => api.GetAsync("x", default));

        Assert.Equal(FailureKind.Offline, ex.Kind);
    }

    [Fact]
    public async Task Not_found_and_forbidden_are_reported_without_retry()
    {
        var handler = new FakeHandler((req, n) => req.RequestUri!.AbsolutePath.EndsWith("missing", StringComparison.Ordinal)
            ? FakeHandler.Status(HttpStatusCode.NotFound)
            : FakeHandler.Status(HttpStatusCode.Forbidden, "Must have admin rights"));
        using var api = Client(handler);

        Assert.Equal(FailureKind.NotFound, (await Assert.ThrowsAsync<GitHubException>(() => api.GetAsync("missing", default))).Kind);
        Assert.Equal(FailureKind.Forbidden, (await Assert.ThrowsAsync<GitHubException>(() => api.GetAsync("secret", default))).Kind);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Pages_follow_link_header_and_stop_when_asked()
    {
        var handler = new FakeHandler((req, n) => FakeHandler.Json($"[{n}]", headers: new()
        {
            ["link"] = $"<https://api.github.com/items?page={n + 1}>; rel=\"next\", <https://api.github.com/items?page=9>; rel=\"last\"",
        }));
        using var api = Client(handler);

        var pages = await api.GetPagesAsync("items?page=1", 10, doc => doc.RootElement[0].GetInt32() < 3, default);

        Assert.Equal(3, pages.Count);
        Assert.Equal("/items", handler.Requests[2].RequestUri!.AbsolutePath);
        Assert.Equal("?page=3", handler.Requests[2].RequestUri!.Query);
    }

    [Fact]
    public async Task Rate_headers_are_tracked()
    {
        var handler = new FakeHandler((req, n) => FakeHandler.Json("{}", headers: new()
        {
            ["x-ratelimit-remaining"] = "4321", ["x-ratelimit-limit"] = "5000", ["x-poll-interval"] = "60",
        }));
        using var api = Client(handler);

        await api.GetAsync("x", default);

        Assert.Equal(4321, api.RateRemaining);
        Assert.Equal(5000, api.RateLimit);
        Assert.Equal(TimeSpan.FromSeconds(60), api.MinPollInterval);
    }

    [Fact]
    public async Task Requests_are_spaced_apart()
    {
        var clock = new FakeClock();
        var handler = new FakeHandler((req, n) => FakeHandler.Json("{}"));
        using var api = Client(handler, clock: clock);

        await api.GetAsync("a", default);
        await api.GetAsync("b", default);

        Assert.Contains(clock.Delays, d => d > TimeSpan.Zero && d <= TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Backoff_grows_and_is_capped()
    {
        Assert.InRange(ApiClient.Backoff(1).TotalSeconds, 1, 1.25);
        Assert.InRange(ApiClient.Backoff(3).TotalSeconds, 4, 5);
        Assert.InRange(ApiClient.Backoff(20).TotalSeconds, 60, 75);
    }
}
