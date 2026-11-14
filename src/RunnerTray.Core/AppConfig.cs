using System.Text.Json;
using System.Text.Json.Serialization;

namespace RunnerTray;

/// <summary>Settings in %APPDATA%\RunnerTray\config.json.</summary>
public sealed class AppConfig
{
    /// <summary>GitHub user or organization to watch. Empty: the signed-in user.</summary>
    public string Owner { get; set; } = "";

    /// <summary>Repositories to watch. Empty: every repository pushed in the last LookbackDays.</summary>
    public List<string> Repos { get; set; } = new();

    public int LookbackDays { get; set; } = 2;

    public int RunCount { get; set; } = 15;

    /// <summary>Leave passing runs out of the Recent runs list. The summary line still counts them.</summary>
    public bool HidePassingRuns { get; set; }

    /// <summary>Global shortcut that opens or closes the panel, for example "Win+Shift+R". Empty: off.</summary>
    public string Hotkey { get; set; } = GlobalHotkey.Default;

    /// <summary>Refresh interval while nothing is running. Runs in progress refresh every 15 seconds.</summary>
    public int RefreshSeconds { get; set; } = 60;

    /// <summary>Show the organization's self-hosted runners (needs organization admin access).</summary>
    public bool ShowSelfHostedRunners { get; set; } = true;

    /// <summary>Only show self-hosted runners whose name contains this text. Empty: all.</summary>
    public string RunnerNameFilter { get; set; } = "";

    /// <summary>
    /// Docker label of runner containers on this machine, for example "ci-job=build-box".
    /// When set, the popup shows which repository and job each local container is running.
    /// </summary>
    public string DockerJobLabel { get; set; } = "";

    /// <summary>Runner work folder inside those containers.</summary>
    public string DockerWorkDir { get; set; } = "/actions-runner/_work";

    public bool NotifyOnFailure { get; set; } = true;

    public bool NotifyOnSuccess { get; set; }

    /// <summary>Download and install new versions automatically (installed version only).</summary>
    public bool AutoUpdate { get; set; } = true;

    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RunnerTray");

    public static string FilePath => Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The problem found when the file was last loaded, or null.</summary>
    [JsonIgnore]
    public string? LoadError { get; private set; }

    /// <summary>Reads the file. A missing file is created with defaults; a broken file gives defaults and LoadError.</summary>
    public static AppConfig Load(string? path = null)
    {
        path ??= FilePath;
        try
        {
            if (File.Exists(path))
            {
                var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Options) ?? new AppConfig();
                config.Normalize();
                return config;
            }
        }
        catch (JsonException ex)
        {
            // Keep the broken file so the user can fix it; tell them where the problem is.
            var where = ex.LineNumber is { } line ? $" on line {line + 1}" : "";
            Log.Warn($"config.json has an error{where}: {ex.Message}");
            return new AppConfig { LoadError = $"The settings file has an error{where}. Default settings are used until it is fixed." };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not read config.json: {ex.Message}");
            return new AppConfig { LoadError = "The settings file could not be read. Default settings are used." };
        }

        var created = new AppConfig();
        created.Save(path);
        return created;
    }

    /// <summary>Writes the file atomically (temporary file, then rename).</summary>
    public void Save(string? path = null)
    {
        path ??= FilePath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not save config.json", ex);
            throw;
        }
    }

    /// <summary>Keeps every value in a safe range.</summary>
    public void Normalize()
    {
        Owner = (Owner ?? "").Trim();
        Repos = (Repos ?? new List<string>()).Select(r => r.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        LookbackDays = Math.Clamp(LookbackDays, 1, 90);
        RunCount = Math.Clamp(RunCount, 5, 50);
        RefreshSeconds = Math.Clamp(RefreshSeconds, 30, 900);
        RunnerNameFilter = (RunnerNameFilter ?? "").Trim();
        DockerJobLabel = (DockerJobLabel ?? "").Trim();
        DockerWorkDir = string.IsNullOrWhiteSpace(DockerWorkDir) ? "/actions-runner/_work" : DockerWorkDir.Trim();

        // A shortcut that cannot be read is kept as written, so the app can say which text is wrong.
        Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? ""
            : GlobalHotkey.TryParse(Hotkey, out var hotkey) ? hotkey.ToString()
            : Hotkey.Trim();
    }

    public AppConfig Clone() => JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, Options), Options)!;
}
