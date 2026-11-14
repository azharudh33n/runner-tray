namespace RunnerTray;

public enum RunState { Queued, Running, Success, Failure, Cancelled, Other }

public sealed record RunInfo(
    long Id,
    string Repo,
    string Workflow,
    string Branch,
    string Event,
    string Actor,
    RunState State,
    DateTimeOffset Created,
    DateTimeOffset Updated,
    string Url)
{
    /// <summary>The pull request title or commit message headline GitHub shows for the run. Empty if unknown.</summary>
    public string Message { get; init; } = "";
}

public sealed record RunnerInfo(string Name, bool Online, bool Busy, IReadOnlyList<string> Labels);

/// <summary>A runner container on this machine. Repo and Job are null while it waits for a job.</summary>
public sealed record LocalJob(string Container, string? Repo, string? Job, DateTimeOffset? Started)
{
    public bool Busy => Job is not null;
}

/// <summary>What the app knows at one moment. Always complete: on errors it carries the last good data.</summary>
public sealed record Snapshot(
    string Owner,
    bool OwnerIsOrg,
    IReadOnlyList<RunInfo> Runs,
    IReadOnlyList<RunnerInfo> Runners,
    string? Error,
    DateTimeOffset At)
{
    /// <summary>Why the last refresh failed, or None.</summary>
    public FailureKind Failure { get; init; }

    /// <summary>When the runs and runners were last read successfully.</summary>
    public DateTimeOffset? DataAt { get; init; }

    /// <summary>For rate limits: when GitHub allows the next request.</summary>
    public DateTimeOffset? RetryAt { get; init; }

    /// <summary>Short information lines, for example "Runner list needs organization admin access".</summary>
    public IReadOnlyList<string> Notices { get; init; } = Array.Empty<string>();

    /// <summary>Link to install the RunnerTray GitHub App, when repositories are missing.</summary>
    public string? InstallUrl { get; init; }

    public TokenKind TokenKind { get; init; }

    /// <summary>True when the data is not from the latest refresh.</summary>
    public bool Stale => Failure != FailureKind.None;

    public static Snapshot Empty(string owner) =>
        new(owner, false, Array.Empty<RunInfo>(), Array.Empty<RunnerInfo>(), null, DateTimeOffset.Now);
}

public interface IDataSource
{
    /// <summary>Workflow runs and self-hosted runners from GitHub. Never throws.</summary>
    Task<Snapshot> FetchAsync(CancellationToken ct);

    /// <summary>Runner containers on this machine (optional, fast to read). Never throws.</summary>
    Task<IReadOnlyList<LocalJob>> FetchLocalAsync(CancellationToken ct);
}

/// <summary>Logic shared by the tray icon and the panel.</summary>
public static class RunLogic
{
    /// <summary>Runs whose latest completed run for the same workflow and branch failed.</summary>
    public static List<RunInfo> Failing(IEnumerable<RunInfo> runs) => runs
        .Where(r => r.State is not RunState.Queued and not RunState.Running)
        .GroupBy(r => (r.Repo, r.Workflow, r.Branch))
        .Select(g => g.OrderByDescending(r => r.Created).First())
        .Where(r => r.State == RunState.Failure)
        .ToList();

    public static bool AnyActive(IEnumerable<RunInfo> runs) =>
        runs.Any(r => r.State is RunState.Running or RunState.Queued);

    /// <summary>The runs for the Recent runs list: all of them, or all but the passing ones.</summary>
    public static List<RunInfo> Visible(IEnumerable<RunInfo> runs, bool hidePassing) =>
        runs.Where(r => !hidePassing || r.State != RunState.Success).ToList();

    /// <summary>The first line of a commit message or title, shortened to at most <paramref name="max"/> characters.</summary>
    public static string Headline(string? text, int max = 80)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var line = text.AsSpan();
        var end = line.IndexOfAny('\r', '\n');
        if (end >= 0)
        {
            line = line[..end];
        }

        line = line.Trim();
        return line.Length <= max ? line.ToString() : string.Concat(line[..(max - 1)].TrimEnd(), "…");
    }
}
