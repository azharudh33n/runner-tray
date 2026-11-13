using Microsoft.Win32;

namespace RunnerTray;

/// <summary>Tray icon, refresh timers, notifications and the right-click menu.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly AppConfig _config;
    private readonly IDataSource _source;
    private readonly NotifyIcon _icon;
    private readonly PopupForm _popup;
    private readonly System.Windows.Forms.Timer _githubTimer;
    private readonly System.Windows.Forms.Timer _localTimer;
    private readonly Dictionary<long, RunState> _seen = new();
    private Snapshot? _last;
    private IReadOnlyList<LocalJob> _local = Array.Empty<LocalJob>();
    private bool _fetching;
    private bool _firstLoad = true;
    private string? _balloonUrl;

    public TrayApp(AppConfig config, IDataSource source, bool demo)
    {
        _config = config;
        _source = source;

        _popup = new PopupForm();
        _popup.RefreshRequested += async (_, _) => await RefreshAsync();

        _icon = new NotifyIcon
        {
            Icon = Icons.Get(TrayState.Unknown),
            Text = demo ? "RunnerTray (demo)" : "RunnerTray: loading…",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) TogglePopup(); };
        _icon.BalloonTipClicked += (_, _) => { if (_balloonUrl is not null) Proc.Open(_balloonUrl); };

        _githubTimer = new System.Windows.Forms.Timer { Interval = Math.Max(15, config.RefreshSeconds) * 1000 };
        _githubTimer.Tick += async (_, _) => await RefreshAsync();
        _githubTimer.Start();

        _localTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _localTimer.Tick += async (_, _) => await RefreshLocalAsync();
        _localTimer.Start();

        _ = RefreshAsync();
        _ = RefreshLocalAsync();
    }

    private void TogglePopup()
    {
        if (_popup.Visible)
        {
            _popup.Hide();
            return;
        }

        // A click on the tray icon first hides the popup (it loses focus); do not reopen it at once.
        if ((DateTime.Now - _popup.LastHidden).TotalMilliseconds < 300)
        {
            return;
        }

        _popup.PlaceNearTray();
        _popup.Show();
        _popup.Activate();
        _popup.FocusFirst();
    }

    private async Task RefreshAsync()
    {
        if (_fetching) return;
        _fetching = true;
        try
        {
            var snapshot = await _source.FetchAsync(CancellationToken.None);
            if (snapshot.Error is null) Notify(snapshot);
            _last = snapshot;
            _popup.ShowSnapshot(snapshot);
            UpdateIcon();
        }
        finally
        {
            _fetching = false;
        }
    }

    private async Task RefreshLocalAsync()
    {
        _local = await _source.FetchLocalAsync(CancellationToken.None);
        _popup.ShowLocal(_local);
        UpdateIcon();
    }

    private void Notify(Snapshot snapshot)
    {
        foreach (var run in snapshot.Runs)
        {
            var known = _seen.TryGetValue(run.Id, out var before);
            _seen[run.Id] = run.State;
            if (_firstLoad || (known && before == run.State)) continue;

            if (run.State == RunState.Failure && _config.NotifyOnFailure)
            {
                Balloon($"✖ {run.Repo} failed", $"{run.Workflow} on {run.Branch} ({run.Actor})", ToolTipIcon.Error, run.Url);
            }
            else if (run.State == RunState.Success && _config.NotifyOnSuccess)
            {
                Balloon($"✔ {run.Repo} passed", $"{run.Workflow} on {run.Branch}", ToolTipIcon.Info, run.Url);
            }
        }

        _firstLoad = false;
    }

    private void Balloon(string title, string text, ToolTipIcon icon, string url)
    {
        _balloonUrl = url;
        _icon.ShowBalloonTip(8000, title, text, icon);
    }

    private void UpdateIcon()
    {
        var s = _last;
        TrayState state;
        string tip;
        if (s is null)
        {
            state = TrayState.Unknown; tip = "loading…";
        }
        else if (s.Error is not null)
        {
            state = TrayState.Error; tip = "cannot reach GitHub";
        }
        else
        {
            var failing = PopupForm.Failing(s.Runs).Count;
            var running = s.Runs.Count(r => r.State == RunState.Running) + _local.Count(j => j.Busy);
            state = failing > 0 ? TrayState.Failing : running > 0 ? TrayState.Busy : TrayState.Idle;
            tip = failing > 0 ? $"{failing} failing" : running > 0 ? $"{running} running" : "all passing";
            var busyLocal = _local.Count(j => j.Busy);
            if (_local.Count > 0) tip += $" · this PC {busyLocal}/{_local.Count} busy";
        }

        _icon.Icon = Icons.Get(state);
        var text = $"RunnerTray: {tip}";
        _icon.Text = text.Length > 63 ? text[..63] : text; // NotifyIcon text limit
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("Open", null, (_, _) => TogglePopup());
        menu.Items.Add("Refresh now", null, async (_, _) => await RefreshAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open in GitHub", null, (_, _) =>
        {
            if (_last is { } s)
            {
                Proc.Open(s.OwnerIsOrg ? $"https://github.com/organizations/{s.Owner}/settings/actions/runners" : $"https://github.com/{s.Owner}");
            }
        });
        menu.Items.Add("Settings (config.json)…", null, (_, _) =>
        {
            _config.Save();
            Proc.Open(AppConfig.FilePath);
            MessageBox.Show("Edit config.json, save it, then restart RunnerTray.", "RunnerTray",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        });

        var startup = new ToolStripMenuItem("Start with Windows") { Checked = IsStartupEnabled(), CheckOnClick = true };
        startup.CheckedChanged += (_, _) => SetStartup(startup.Checked);
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return menu;
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("RunnerTray") is string;
    }

    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;
        if (enabled) key.SetValue("RunnerTray", $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue("RunnerTray", throwOnMissingValue: false);
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _popup.Dispose();
        _githubTimer.Dispose();
        _localTimer.Dispose();
        base.ExitThreadCore();
    }
}
