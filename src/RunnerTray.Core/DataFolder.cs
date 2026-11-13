namespace RunnerTray;

/// <summary>
/// Local data (logs, cache, last data) lives in %LOCALAPPDATA%\RunnerTray-data, not in
/// %LOCALAPPDATA%\RunnerTray: that folder belongs to the installer, which replaces it on install
/// and deletes it on uninstall. Settings stay in %APPDATA%\RunnerTray.
/// </summary>
public static class DataFolder
{
    public const string Name = "RunnerTray-data";

    public static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);
}