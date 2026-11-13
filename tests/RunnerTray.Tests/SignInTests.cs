using System.Text.Json;
using Xunit;

namespace RunnerTray.Tests;

public class DeviceFlowTests
{
    private static (DeviceFlowClient Flow, FakeHandler Handler, FakeClock Clock) Flow(params string[] answers)
    {
        var clock = new FakeClock();
        var handler = new FakeHandler((req, n) => FakeHandler.Json(answers[Math.Min(n, answers.Length) - 1]));
        return (new DeviceFlowClient("Iv1.test", handler, clock.Delay, () => clock.Now), handler, clock);
    }

    private static DeviceCode Code(int interval = 5, int expires = 900) =>
        new("dev", "ABCD-1234", "https://github.com/login/device", TimeSpan.FromSeconds(expires), TimeSpan.FromSeconds(interval));

    [Fact]
    public async Task Start_returns_the_user_code_and_sends_only_the_client_id()
    {
        var (flow, handler, _) = Flow("""{"device_code":"dev","user_code":"ABCD-1234","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}""");

        var code = await flow.StartAsync(default);

        Assert.Equal("ABCD-1234", code.UserCode);
        Assert.Equal(TimeSpan.FromSeconds(5), code.Interval);
        Assert.Equal("client_id=Iv1.test", handler.Bodies[0]);
        Assert.DoesNotContain("secret", handler.Bodies[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Waits_while_pending_slows_down_when_asked_then_returns_tokens()
    {
        var (flow, _, clock) = Flow(
            """{"error":"authorization_pending"}""",
            """{"error":"slow_down"}""",
            """{"access_token":"ghu_x","expires_in":28800,"refresh_token":"ghr_y","refresh_token_expires_in":15897600}""");

        var tokens = await flow.WaitForTokenAsync(Code(), default);

        Assert.Equal("ghu_x", tokens.AccessToken);
        Assert.Equal("ghr_y", tokens.RefreshToken);
        Assert.Equal(clock.Now.AddSeconds(28800), tokens.AccessExpires);
        Assert.Equal(new[] { 5d, 5d, 10d }, clock.Delays.Select(d => d.TotalSeconds));
    }

    [Theory]
    [InlineData("access_denied", DeviceFlowError.Denied)]
    [InlineData("expired_token", DeviceFlowError.Expired)]
    [InlineData("device_flow_disabled", DeviceFlowError.Disabled)]
    [InlineData("incorrect_client_credentials", DeviceFlowError.Other)]
    public async Task Errors_are_reported_with_their_kind(string error, DeviceFlowError expected)
    {
        var (flow, _, _) = Flow($$"""{"error":"{{error}}","error_description":"x"}""");

        var ex = await Assert.ThrowsAsync<DeviceFlowException>(() => flow.WaitForTokenAsync(Code(), default));

        Assert.Equal(expected, ex.Error);
    }

    [Fact]
    public async Task Stops_when_the_code_expires()
    {
        var (flow, _, _) = Flow("""{"error":"authorization_pending"}""");

        var ex = await Assert.ThrowsAsync<DeviceFlowException>(() => flow.WaitForTokenAsync(Code(interval: 5, expires: 12), default));

        Assert.Equal(DeviceFlowError.Expired, ex.Error);
    }

    [Fact]
    public async Task Refresh_sends_no_secret()
    {
        var (flow, handler, _) = Flow("""{"access_token":"ghu_2","expires_in":28800,"refresh_token":"ghr_2"}""");

        var tokens = await flow.RefreshAsync("ghr_1", default);

        Assert.Equal("ghu_2", tokens.AccessToken);
        Assert.Contains("grant_type=refresh_token", handler.Bodies[0], StringComparison.Ordinal);
        Assert.DoesNotContain("client_secret", handler.Bodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bad_refresh_token_is_reported()
    {
        var (flow, _, _) = Flow("""{"error":"bad_refresh_token"}""");

        var ex = await Assert.ThrowsAsync<DeviceFlowException>(() => flow.RefreshAsync("old", default));

        Assert.Equal(DeviceFlowError.BadRefreshToken, ex.Error);
    }

    [Fact]
    public async Task Network_failure_is_reported()
    {
        var clock = new FakeClock();
        var handler = new FakeHandler((Func<HttpRequestMessage, int, HttpResponseMessage>)((r, n) => throw new HttpRequestException("down")));
        using var flow = new DeviceFlowClient("id", handler, clock.Delay, () => clock.Now);

        var ex = await Assert.ThrowsAsync<DeviceFlowException>(() => flow.StartAsync(default));

        Assert.Equal(DeviceFlowError.Network, ex.Error);
    }
}

public class SignedInTokenSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static TokenSet Set(string access, int expiresInMinutes, string? refresh = "r1", int refreshDays = 180) =>
        new(access, refresh, Now.AddMinutes(expiresInMinutes), Now.AddDays(refreshDays));

    [Fact]
    public async Task Returns_a_valid_token_without_refreshing()
    {
        var store = new MemorySecretStore();
        var calls = 0;
        using var source = new SignedInTokenSource(store, (r, ct) => { calls++; return Task.FromResult(Set("new", 480)); }, () => Now);
        source.Save(Set("a1", 120));

        Assert.Equal("a1", await source.GetTokenAsync(default));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Refreshes_shortly_before_expiry_and_saves_the_new_pair()
    {
        var store = new MemorySecretStore();
        using var source = new SignedInTokenSource(store, (r, ct) => Task.FromResult(Set("a2", 480, "r2")), () => Now);
        source.Save(Set("a1", 5));

        Assert.Equal("a2", await source.GetTokenAsync(default));
        var saved = JsonSerializer.Deserialize<TokenSet>(store.Read("github")!)!;
        Assert.Equal("r2", saved.RefreshToken);
    }

    [Fact]
    public async Task Parallel_callers_refresh_only_once()
    {
        var store = new MemorySecretStore();
        var calls = 0;
        using var source = new SignedInTokenSource(store, async (r, ct) =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(30, ct);
            return Set("a2", 480, "r2");
        }, () => Now);
        source.Save(Set("a1", 1));

        var tokens = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => source.GetTokenAsync(default)));

        Assert.All(tokens, t => Assert.Equal("a2", t));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Rejected_refresh_token_signs_out()
    {
        var store = new MemorySecretStore();
        using var source = new SignedInTokenSource(store,
            (r, ct) => throw new DeviceFlowException(DeviceFlowError.BadRefreshToken, "bad"), () => Now);
        source.Save(Set("a1", 1));
        var changed = 0;
        source.Changed += (_, _) => changed++;

        Assert.Null(await source.GetTokenAsync(default));
        Assert.False(source.IsSignedIn);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Expired_refresh_token_signs_out_without_calling_GitHub()
    {
        var store = new MemorySecretStore();
        var calls = 0;
        using var source = new SignedInTokenSource(store, (r, ct) => { calls++; return Task.FromResult(Set("x", 480)); }, () => Now);
        source.Save(Set("a1", 1, refreshDays: -1));

        Assert.Null(await source.GetTokenAsync(default));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Offline_refresh_keeps_the_old_token()
    {
        var store = new MemorySecretStore();
        using var source = new SignedInTokenSource(store,
            (r, ct) => throw new DeviceFlowException(DeviceFlowError.Network, "down"), () => Now);
        source.Save(Set("a1", 1));

        Assert.Equal("a1", await source.GetTokenAsync(default));
        Assert.True(source.IsSignedIn);
    }

    [Fact]
    public async Task Damaged_saved_data_is_removed()
    {
        var store = new MemorySecretStore();
        store.Write("github", "{not json");
        using var source = new SignedInTokenSource(store, (r, ct) => Task.FromResult(Set("x", 480)), () => Now);

        Assert.Null(await source.GetTokenAsync(default));
        Assert.Null(store.Read("github"));
    }

    [Fact]
    public void Credential_manager_round_trip()
    {
        var store = new CredentialManagerStore();
        var key = "test-" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Null(store.Read(key));
            store.Write(key, "secret value ✓");
            Assert.Equal("secret value ✓", store.Read(key));
        }
        finally
        {
            store.Delete(key);
        }

        Assert.Null(store.Read(key));
    }
}

public class TokenChainTests
{
    [Fact]
    public async Task Uses_the_first_source_with_a_token()
    {
        var chain = new TokenChain(new FakeTokens(null) { Kind = TokenKind.SignedIn }, new FakeTokens("env") { Kind = TokenKind.Environment });

        Assert.Equal("env", await chain.GetTokenAsync(default));
        Assert.Equal(TokenKind.Environment, chain.Kind);
    }

    [Fact]
    public async Task No_source_means_no_token()
    {
        var chain = new TokenChain(new FakeTokens(null));

        Assert.Null(await chain.GetTokenAsync(default));
        Assert.Equal(TokenKind.None, chain.Kind);
    }

    [Fact]
    public async Task Environment_source_prefers_GITHUB_TOKEN()
    {
        var env = new EnvironmentTokenSource(n => n == "GITHUB_TOKEN" ? " abc " : "def");

        Assert.Equal("abc", await env.GetTokenAsync(default));
    }
}
