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
    string Url);

public sealed record RunnerInfo(string Name, bool Online, bool Busy, IReadOnlyList<string> Labels);

/// <summary>A runner container on this machine. Repo and Job are null while it waits for a job.</summary>
public sealed record LocalJob(string Container, string? Repo, string? Job, DateTimeOffset? Started)
{
    public bool Busy => Job is not null;
}

public sealed record Snapshot(
    string Owner,
    bool OwnerIsOrg,
    IReadOnlyList<RunInfo> Runs,
    IReadOnlyList<RunnerInfo> Runners,
    string? Error,
    DateTimeOffset At);

public interface IDataSource
{
    /// <summary>Workflow runs and self-hosted runners from GitHub.</summary>
    Task<Snapshot> FetchAsync(CancellationToken ct);

    /// <summary>Runner containers on this machine (optional, fast to read).</summary>
    Task<IReadOnlyList<LocalJob>> FetchLocalAsync(CancellationToken ct);
}
