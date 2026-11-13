using System.Text.Json;

namespace RunnerTray;

/// <summary>
/// Saves the last good data, so the panel shows something at once after a restart
/// (marked as old until the first refresh finishes).
/// </summary>
public static class SnapshotStore
{
    private sealed record Saved(string Owner, bool OwnerIsOrg, List<RunInfo> Runs, List<RunnerInfo> Runners, DateTimeOffset DataAt);

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolder.Name, "last.json");

    public static void Save(Snapshot s, string? path = null)
    {
        if (s.Stale || s.DataAt is null)
        {
            return;
        }

        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new Saved(s.Owner, s.OwnerIsOrg, s.Runs.ToList(), s.Runners.ToList(), s.DataAt.Value)));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not save last data: {ex.Message}");
        }
    }

    public static Snapshot? Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path));
            return saved is null
                ? null
                : new Snapshot(saved.Owner, saved.OwnerIsOrg, saved.Runs, saved.Runners, "Showing data from your last session.", DateTimeOffset.Now)
                {
                    DataAt = saved.DataAt,
                    Failure = FailureKind.Offline,
                };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public static void Delete(string? path = null)
    {
        try
        {
            File.Delete(path ?? DefaultPath);
        }
        catch (IOException)
        {
            // Not important.
        }
    }
}
