namespace RunnerTray;

/// <summary>
/// Made-up data for screenshots and for trying the app without a GitHub account.
/// All names are fictional.
/// </summary>
public sealed class DemoSource : IDataSource
{
    private readonly DateTimeOffset _now = DateTimeOffset.Now;

    public Task<Snapshot> FetchAsync(CancellationToken ct)
    {
        var runs = new List<RunInfo>
        {
            Run(1, "storefront-web", "CI", "main", "push", "priya-k", RunState.Running, 3, "Add saved carts to checkout"),
            Run(2, "payments-api", "Integration tests", "feat/refund-webhooks", "pull_request", "m-santos", RunState.Running, 1, "Send refund events to merchant webhooks"),
            Run(3, "search-service", "Build image", "main", "push", "jlee", RunState.Queued, 0, "Bump base image to Debian 13"),
            Run(4, "atlas-mobile", "Android release", "release/3.2", "push", "jlee", RunState.Failure, 12, "Release 3.2.0"),
            Run(5, "storefront-web", "Preview deploy", "feat/checkout-v2", "pull_request", "priya-k", RunState.Success, 26, "Checkout v2: new address form"),
            Run(6, "infra-terraform", "Plan", "main", "push", "release-bot", RunState.Success, 41, "Raise the build cache bucket size"),
            Run(7, "docs-portal", "Link check", "main", "schedule", "github-actions", RunState.Success, 75, "Nightly link check"),
            Run(8, "payments-api", "CodeQL", "main", "push", "m-santos", RunState.Cancelled, 130, "Fix rounding in currency conversion"),
            Run(9, "design-tokens", "Publish", "main", "workflow_dispatch", "a-okafor", RunState.Success, 190, "Publish tokens 4.1.0"),
            Run(10, "atlas-mobile", "Unit tests", "main", "push", "jlee", RunState.Success, 260, "Cache map tiles on first launch"),
        };

        var runners = new List<RunnerInfo>
        {
            new("build-01", true, true, new[] { "linux-large", "docker" }),
            new("build-02", true, true, new[] { "linux-large", "docker" }),
            new("build-03", true, false, new[] { "linux-large", "docker" }),
            new("build-04", true, false, new[] { "linux-large", "docker" }),
            new("gpu-01", true, false, new[] { "gpu", "cuda-12" }),
            new("mac-mini-01", false, false, new[] { "xcode-16" }),
        };

        return Task.FromResult(new Snapshot("northwind-labs", true, runs, runners, null, _now));
    }

    public Task<IReadOnlyList<LocalJob>> FetchLocalAsync(CancellationToken ct)
    {
        IReadOnlyList<LocalJob> jobs = new List<LocalJob>
        {
            new("build-01", "storefront-web", "Build and test (Node 22)", _now.AddMinutes(-3).AddSeconds(-12)),
            new("build-02", "payments-api", "Integration tests (Postgres 17)", _now.AddMinutes(-1).AddSeconds(-40)),
            new("build-03", null, null, null),
            new("build-04", null, null, null),
        };
        return Task.FromResult(jobs);
    }

    private RunInfo Run(long id, string repo, string wf, string branch, string ev, string actor, RunState state, int minutesAgo, string message)
    {
        var created = _now.AddMinutes(-minutesAgo).AddSeconds(-20);
        return new RunInfo(id, repo, wf, branch, ev, actor, state, created,
            state is RunState.Running or RunState.Queued ? _now : created.AddMinutes(4),
            $"https://github.com/northwind-labs/{repo}/actions/runs/{id}")
        {
            Message = message,
        };
    }
}
