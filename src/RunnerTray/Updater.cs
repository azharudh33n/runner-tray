using Velopack;
using Velopack.Sources;

namespace RunnerTray;

/// <summary>
/// Updates from GitHub Releases with Velopack. Works only for the installed version
/// (Setup.exe); the portable zip never updates itself.
/// </summary>
internal sealed class Updater
{
    private const string RepoUrl = "https://github.com/azharudh33n/runner-tray";

    private readonly UpdateManager? _manager;
    private UpdateInfo? _pending;
    private bool _downloaded;

    public Updater()
    {
        try
        {
            // RUNNERTRAY_UPDATE_DIR: read updates from a local folder instead (for testing the update path).
            var localDir = Environment.GetEnvironmentVariable("RUNNERTRAY_UPDATE_DIR");
            IUpdateSource source = string.IsNullOrEmpty(localDir)
                ? new GithubSource(RepoUrl, null, false)
                : new SimpleFileSource(new DirectoryInfo(localDir));
            var manager = new UpdateManager(source);
            if (manager.IsInstalled)
            {
                _manager = manager;
            }
        }
        catch (Exception ex)
        {
            // Updates are optional; never stop the app for them.
            Log.Warn($"Updates unavailable: {ex.Message}");
        }
    }

    public bool Installed => _manager is not null;

    /// <summary>Version waiting to be installed, or null.</summary>
    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    public bool ReadyToRestart => _downloaded;

    /// <summary>Checks GitHub Releases. Downloads too when <paramref name="download"/> is true. Never throws.</summary>
    public async Task<bool> CheckAsync(bool download)
    {
        if (_manager is null)
        {
            return false;
        }

        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null)
            {
                return false;
            }

            _pending = info;
            if (download && !_downloaded)
            {
                await _manager.DownloadUpdatesAsync(info);
                _downloaded = true;
                Log.Info($"Update {PendingVersion} downloaded.");
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Update check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Downloads if needed, then restarts into the new version.</summary>
    public async Task ApplyAndRestartAsync()
    {
        if (_manager is null || _pending is null)
        {
            return;
        }

        try
        {
            if (!_downloaded)
            {
                await _manager.DownloadUpdatesAsync(_pending);
                _downloaded = true;
            }

            _manager.ApplyUpdatesAndRestart(_pending);
        }
        catch (Exception ex)
        {
            Problems.Report("The update could not be installed", ex);
        }
    }
}
