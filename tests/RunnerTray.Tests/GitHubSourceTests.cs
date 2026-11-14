using System.Net;
using System.Text.Json;
using Xunit;

namespace RunnerTray.Tests;

public class GitHubSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static string Run(long id, string status, string? conclusion, int minutesAgo) =>
        $$"""
        {"id":{{id}},"name":"CI","head_branch":"main","event":"push","actor":{"login":"dev"},
         "status":"{{status}}","conclusion":{{(conclusion is null ? "null" : $"\"{conclusion}\"")}},
         "created_at":"{{Now.AddMinutes(-minutesAgo):O}}","updated_at":"{{Now.AddMinutes(-minutesAgo + 2):O}}",
         "html_url":"https://github.com/acme/r/actions/runs/{{id}}"}
        """;

    private static string Repo(string name, int daysAgo, bool archived = false) =>
        $$"""{"name":"{{name}}","pushed_at":"{{Now.AddDays(-daysAgo):O}}","archived":{{(archived ? "true" : "false")}}}""";

    /// <summary>A fake GitHub for an organization "acme" seen by user "me" through a CLI token.</summary>
    private static FakeHandler OrgGitHub(Func<string, HttpResponseMessage?>? overrides = null) => new((req, n) =>
    {
        var path = req.RequestUri!.PathAndQuery;
        var custom = overrides?.Invoke(path);
        if (custom is not null) return custom;
        return path switch
        {
            "/user" => FakeHandler.Json("""{"login":"me"}"""),
            "/users/acme" => FakeHandler.Json("""{"login":"acme","type":"Organization"}"""),
            var p when p.StartsWith("/orgs/acme/repos", StringComparison.Ordinal) =>
                FakeHandler.Json($"[{Repo("web", 0)},{Repo("api", 1)},{Repo("archived", 0, true)},{Repo("old", 30)}]"),
            "/repos/acme/web/actions/runs?per_page=5" =>
                FakeHandler.Json($$"""{"workflow_runs":[{{Run(1, "in_progress", null, 1)}},{{Run(2, "completed", "success", 50)}}]}"""),
            "/repos/acme/api/actions/runs?per_page=5" =>
                FakeHandler.Json($$"""{"workflow_runs":[{{Run(3, "completed", "failure", 10)}}]}"""),
            var p when p.StartsWith("/orgs/acme/actions/runners", StringComparison.Ordinal) => FakeHandler.Json(
                """{"runners":[{"name":"r1","status":"online","busy":true,"labels":[{"name":"self-hosted"},{"name":"gpu"}]},{"name":"r2","status":"offline","busy":false,"labels":[]}]}"""),
            _ => FakeHandler.Status(HttpStatusCode.NotFound),
        };
    });

    private static GitHubSource Source(FakeHandler handler, AppConfig? config = null, FakeTokens? tokens = null)
    {
        var clock = new FakeClock { Now = Now };
        var api = new ApiClient(tokens ?? new FakeTokens("t"), new ResponseCache(null), handler, clock.Delay, () => clock.Now);
        return new GitHubSource(config ?? new AppConfig { Owner = "acme" }, api, () => Now);
    }

    [Fact]
    public void Run_message_is_the_display_title_or_the_commit_headline()
    {
        using var pullRequest = JsonDocument.Parse("""
            {"id":7,"name":"CI","event":"pull_request","display_title":"Send refund events to webhooks",
             "head_commit":{"message":"wip"},"created_at":"2026-11-13T12:00:00Z","updated_at":"2026-11-13T12:01:00Z"}
            """);
        using var push = JsonDocument.Parse("""
            {"id":8,"name":"CI","event":"push","head_commit":{"message":"Fix login on Safari\n\nThe cookie was too large."},
             "created_at":"2026-11-13T12:00:00Z","updated_at":"2026-11-13T12:01:00Z"}
            """);
        using var neither = JsonDocument.Parse("""{"id":9,"name":"CI","created_at":"2026-11-13T12:00:00Z","updated_at":"2026-11-13T12:01:00Z"}""");

        Assert.Equal("Send refund events to webhooks", GitHubSource.ParseRun("api", pullRequest.RootElement).Message);
        Assert.Equal("Fix login on Safari", GitHubSource.ParseRun("web", push.RootElement).Message);
        Assert.Equal("", GitHubSource.ParseRun("web", neither.RootElement).Message);
    }

    [Fact]
    public async Task Reads_recent_repos_runs_and_runners_for_an_organization()
    {
        var handler = OrgGitHub();
        var source = Source(handler);

        var s = await source.FetchAsync(default);

        Assert.Equal(FailureKind.None, s.Failure);
        Assert.Equal("acme", s.Owner);
        Assert.True(s.OwnerIsOrg);
        Assert.Equal(new[] { "web", "api" }, source.WatchedRepos);
        Assert.Equal(new long[] { 1, 3, 2 }, s.Runs.Select(r => r.Id));
        Assert.Equal(RunState.Running, s.Runs[0].State);
        Assert.Equal(RunState.Failure, s.Runs[1].State);
        Assert.Equal(2, s.Runners.Count);
        Assert.Equal(new[] { "gpu" }, s.Runners[0].Labels);
        Assert.Equal(1, handler.MaxConcurrent);
    }

    [Fact]
    public async Task Repository_without_actions_is_skipped_with_a_notice()
    {
        var source = Source(OrgGitHub(p => p.StartsWith("/repos/acme/api/", StringComparison.Ordinal) ? FakeHandler.Status(HttpStatusCode.NotFound) : null));

        var s = await source.FetchAsync(default);

        Assert.Equal(FailureKind.None, s.Failure);
        Assert.All(s.Runs, r => Assert.Equal("web", r.Repo));
        Assert.Contains(s.Notices, n => n.Contains("skipped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Runner_list_without_admin_access_gives_a_notice()
    {
        var source = Source(OrgGitHub(p => p.StartsWith("/orgs/acme/actions/runners", StringComparison.Ordinal)
            ? FakeHandler.Status(HttpStatusCode.Forbidden, "Must have admin rights to Repository.")
            : null));

        var s = await source.FetchAsync(default);

        Assert.Equal(FailureKind.None, s.Failure);
        Assert.Empty(s.Runners);
        Assert.Contains(s.Notices, n => n.Contains("admin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failure_returns_last_good_data_marked_stale()
    {
        var broken = false;
        var source = Source(OrgGitHub(p => broken ? FakeHandler.Status(HttpStatusCode.BadGateway) : null));
        var good = await source.FetchAsync(default);

        broken = true;
        var stale = await source.FetchAsync(default);

        Assert.Equal(FailureKind.ServerError, stale.Failure);
        Assert.True(stale.Stale);
        Assert.Equal(good.Runs, stale.Runs);
        Assert.Equal(good.DataAt, stale.DataAt);
        Assert.NotNull(stale.Error);
    }

    [Fact]
    public async Task Not_signed_in_reports_sign_in_required()
    {
        var source = Source(OrgGitHub(), tokens: new FakeTokens(null));

        var s = await source.FetchAsync(default);

        Assert.Equal(FailureKind.SignInRequired, s.Failure);
        Assert.Empty(s.Runs);
    }

    [Fact]
    public async Task Unexpected_json_does_not_throw()
    {
        var source = Source(OrgGitHub(p => p == "/user" ? FakeHandler.Json("""{"unexpected":true}""") : null));

        var s = await source.FetchAsync(default);

        Assert.Equal(FailureKind.Unexpected, s.Failure);
    }

    [Fact]
    public async Task Configured_repositories_are_used_without_listing()
    {
        var handler = OrgGitHub();
        var source = Source(handler, new AppConfig { Owner = "acme", Repos = new() { "api" } });

        var s = await source.FetchAsync(default);

        Assert.All(s.Runs, r => Assert.Equal("api", r.Repo));
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath == "/orgs/acme/repos");
    }

    [Fact]
    public async Task Signed_in_without_installation_offers_install_link()
    {
        var handler = OrgGitHub(p => p.StartsWith("/user/installations", StringComparison.Ordinal)
            ? FakeHandler.Json("""{"total_count":1,"installations":[{"id":7,"account":{"login":"someone-else"}}]}""")
            : null);
        var source = Source(handler, tokens: new FakeTokens("t") { Kind = TokenKind.SignedIn });

        var s = await source.FetchAsync(default);

        Assert.Equal(GitHubAppInfo.InstallUrl, s.InstallUrl);
        Assert.Empty(s.Runs);
    }

    [Fact]
    public async Task Signed_in_reads_repositories_from_installations()
    {
        var handler = OrgGitHub(p => p switch
        {
            "/user/installations?per_page=100" => FakeHandler.Json("""{"total_count":1,"installations":[{"id":7,"account":{"login":"acme"}}]}"""),
            "/user/installations/7/repositories?per_page=100" => FakeHandler.Json($$"""{"repositories":[{{Repo("api", 0)}}]}"""),
            _ => null,
        });
        var source = Source(handler, tokens: new FakeTokens("t") { Kind = TokenKind.SignedIn });

        var s = await source.FetchAsync(default);

        Assert.Null(s.InstallUrl);
        Assert.Equal(new[] { "api" }, source.WatchedRepos);
    }

    [Theory]
    [InlineData("completed", "success", RunState.Success)]
    [InlineData("completed", "failure", RunState.Failure)]
    [InlineData("completed", "timed_out", RunState.Failure)]
    [InlineData("completed", "cancelled", RunState.Cancelled)]
    [InlineData("completed", "skipped", RunState.Other)]
    [InlineData("in_progress", null, RunState.Running)]
    [InlineData("queued", null, RunState.Queued)]
    [InlineData("waiting", null, RunState.Queued)]
    public void Maps_run_status(string status, string? conclusion, RunState expected) =>
        Assert.Equal(expected, GitHubSource.ToState(status, conclusion));
}
