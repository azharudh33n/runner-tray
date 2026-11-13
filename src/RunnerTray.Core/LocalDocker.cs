using System.Globalization;
using System.Text.RegularExpressions;

namespace RunnerTray;

/// <summary>
/// Reads runner containers on this machine: which repository and job each one runs.
/// Works with containers that run the official GitHub Actions runner. Never throws.
/// When Docker is missing or stopped, it checks again only every 30 seconds.
/// </summary>
public sealed partial class LocalDockerReader
{
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    private readonly Func<IReadOnlyList<string>, int, CancellationToken, Task<(int ExitCode, string Output)>> _docker;
    private readonly Func<DateTimeOffset> _now;
    private DateTimeOffset _nextTry = DateTimeOffset.MinValue;

    public LocalDockerReader(
        Func<IReadOnlyList<string>, int, CancellationToken, Task<(int ExitCode, string Output)>>? docker = null,
        Func<DateTimeOffset>? now = null)
    {
        _docker = docker ?? ((args, timeout, ct) => Proc.RunAsync("docker", args, timeout, ct));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>False after Docker could not be reached; shown in Diagnostics.</summary>
    public bool Available { get; private set; } = true;

    // Runner log line: "2026-11-13 16:05:31Z: Running job: Build and test"
    [GeneratedRegex(@"^(\S+ \S+Z): Running job: (.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex RunningJob();

    public async Task<IReadOnlyList<LocalJob>> GetJobsAsync(AppConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.DockerJobLabel) || _now() < _nextTry)
        {
            return Array.Empty<LocalJob>();
        }

        try
        {
            var (code, output) = await _docker(
                new[] { "ps", "--filter", $"label={config.DockerJobLabel}", "--format", "{{.Names}}" }, 8000, ct);
            if (code != 0)
            {
                if (Available) Log.Warn("Docker is not available; the This machine section is hidden.");
                Available = false;
                _nextTry = _now() + RetryAfterFailure;
                return Array.Empty<LocalJob>();
            }

            Available = true;
            var names = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var jobs = new List<LocalJob>(names.Count);
            foreach (var name in names)
            {
                jobs.Add(await ReadContainerAsync(name, config.DockerWorkDir, ct));
            }

            return jobs;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Reading local containers is optional and must never break the app.
            Log.Error("Could not read local runner containers", ex);
            _nextTry = _now() + RetryAfterFailure;
            return Array.Empty<LocalJob>();
        }
    }

    private async Task<LocalJob> ReadContainerAsync(string name, string workDir, CancellationToken ct)
    {
        var (_, log) = await _docker(new[] { "logs", "--tail", "5", name }, 5000, ct);
        var job = ParseRunningJob(log);
        if (job is null)
        {
            return new LocalJob(name, null, null, null);
        }

        // The repository is the folder the runner checked out in its work folder.
        var (_, listing) = await _docker(new[] { "exec", name, "ls", workDir }, 5000, ct);
        return new LocalJob(name, ParseRepoFolder(listing), job.Value.Job, job.Value.Started);
    }

    /// <summary>The job from the last "Running job:" line, unless the job has finished since.</summary>
    internal static (string Job, DateTimeOffset? Started)? ParseRunningJob(string log)
    {
        var match = RunningJob().Matches(log).LastOrDefault();
        if (match is null)
        {
            return null;
        }

        // "Job X completed with result: ..." after the start line means the runner is idle again.
        var after = log[(match.Index + match.Length)..];
        if (after.Contains("completed with result", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        DateTimeOffset? started = DateTimeOffset.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var t) ? t : null;
        return (match.Groups[2].Value, started);
    }

    internal static string? ParseRepoFolder(string listing) =>
        listing.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(s => !s.StartsWith('_') && !s.Contains(' ', StringComparison.Ordinal) && !s.Contains(':', StringComparison.Ordinal));
}
