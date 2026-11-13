using System.Globalization;
using System.Text.RegularExpressions;

namespace RunnerTray;

/// <summary>
/// Reads runner containers on this machine: which repository and job each one runs.
/// Works with containers that run the official GitHub Actions runner.
/// </summary>
internal static partial class LocalDocker
{
    // Runner log line: "2026-11-13 16:05:31Z: Running job: Build and test"
    [GeneratedRegex(@"^(\S+ \S+Z): Running job: (.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex RunningJob();

    public static async Task<IReadOnlyList<LocalJob>> GetJobsAsync(AppConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.DockerJobLabel))
        {
            return Array.Empty<LocalJob>();
        }

        var (code, output) = await Proc.RunAsync("docker",
            new[] { "ps", "--filter", $"label={config.DockerJobLabel}", "--format", "{{.Names}}" }, 8000, ct);
        if (code != 0)
        {
            return Array.Empty<LocalJob>();
        }

        var names = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        return await Task.WhenAll(names.Select(n => ReadContainerAsync(n, config.DockerWorkDir, ct)));
    }

    private static async Task<LocalJob> ReadContainerAsync(string name, string workDir, CancellationToken ct)
    {
        var (_, log) = await Proc.RunAsync("docker", new[] { "logs", "--tail", "5", name }, 5000, ct);
        var match = RunningJob().Matches(log).LastOrDefault();
        if (match is null)
        {
            return new LocalJob(name, null, null, null);
        }

        // The repository is the folder the runner checked out in its work folder.
        var (_, listing) = await Proc.RunAsync("docker", new[] { "exec", name, "ls", workDir }, 5000, ct);
        var repo = listing.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(s => !s.StartsWith('_') && !s.Contains(' '));

        DateTimeOffset? started = DateTimeOffset.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var t) ? t : null;
        return new LocalJob(name, repo, match.Groups[2].Value, started);
    }
}
