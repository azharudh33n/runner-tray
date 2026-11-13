using System.Text.Json;

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
    public int RefreshSeconds { get; set; } = 60;

    /// <summary>Show the organization's self-hosted runners (needs admin:org read access).</summary>
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
    public bool NotifyOnSuccess { get; set; } = false;

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

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Options) ?? new AppConfig();
            }
        }
        catch (JsonException)
        {
            // A broken file falls back to defaults; the file is left as is so the user can fix it.
            return new AppConfig();
        }

        var config = new AppConfig();
        config.Save();
        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
    }
}
