using System.Net.NetworkInformation;
using System.Text;
using Microsoft.Win32;

namespace RunnerTray;

/// <summary>
/// Tray icon, refresh scheduling, notifications, sign-in, settings, updates and the right-click menu.
/// Everything runs on the UI thread; background events are posted to it.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly StartOptions _options;
    private readonly SynchronizationContext _ui;
    private readonly Services? _services;
    private readonly IDataSource _source;
    private readonly Updater _updater = new();
    private readonly NotifyIcon _icon;
    private readonly PopupForm _popup;
    private readonly System.Windows.Forms.Timer _pollTimer = new();
    private readonly System.Windows.Forms.Timer _localTimer = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 6 * 60 * 60 * 1000 };
    private readonly Dictionary<long, RunState> _seen = new();
    private readonly IDisposable _showSignal;
    private readonly FileSystemWatcher? _configWatcher;
    private readonly ToolStripMenuItem _signInItem = new();
    private readonly ToolStripMenuItem _updateItem = new() { Visible = false };
    private readonly ToolStripMenuItem _startupItem = new("Start with &Windows") { CheckOnClick = true };

    private AppConfig _config;
    private Snapshot? _last;
    private IReadOnlyList<LocalJob> _local = Array.Empty<LocalJob>();
    private bool _fetching;
    private bool _refreshAgain;
    private bool _firstLoad = true;
    private bool _locked;
    private int _failuresInRow;
    private string _runsSignature = "";
    private DateTimeOffset _lastChange = DateTimeOffset.Now;
    private string? _balloonUrl;
    private DateTime _lastProblemBalloon = DateTime.MinValue;
    private DateTime _ignoreConfigUntil = DateTime.MinValue;

    public TrayApp(StartOptions options)
    {
        _options = options;
        _config = options.Demo ? new AppConfig() : AppConfig.Load();

        _popup = new PopupForm { AutoHide = options.TestUi is null, ConfigError = _config.LoadError };
        _popup.RefreshRequested += (_, _) => RefreshNow();
        _popup.BannerActionRequested += (_, a) => OnBannerAction(a);
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        if (options.Demo)
        {
            _source = new DemoSource();
        }
        else
        {
            _services = Services.Create(_config);
            _source = _services.Source;
            _services.SignedIn.Changed += (_, _) => Post(OnSignInChanged);
            if (SnapshotStore.Load() is { } saved)
            {
                _services.Source.Seed(saved);
                _last = saved;
                _popup.ShowSnapshot(saved);
            }

            _configWatcher = WatchConfig();
        }

        _icon = new NotifyIcon
        {
            Icon = Icons.Get(TrayState.Unknown),
            Text = options.Demo ? "RunnerTray (demo)" : "RunnerTray: loading…",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) TogglePopup();
        };
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_balloonUrl is not null) Proc.Open(_balloonUrl);
        };

        _pollTimer.Tick += (_, _) => RefreshNow();
        _localTimer.Tick += async (_, _) => await RefreshLocalAsync();
        _localTimer.Start();
        _updateTimer.Tick += async (_, _) => await CheckUpdatesAsync();
        _updateTimer.Start();

        Problems.Raised += OnProblem;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        _showSignal = SingleInstance.Listen(() => Post(OpenPopup));

        RefreshNow();
        _ = RefreshLocalAsync();
        _ = DelayedUpdateCheckAsync();

        switch (options.TestUi)
        {
            case "settings": Post(OpenSettings); break;
            case "signin": Post(() => SignIn(demo: true)); break;
            case "diagnostics": Post(OpenDiagnostics); break;
            case not null: Post(OpenPopup); break;
        }
    }

    // ---------- panel ----------

    private void TogglePopup()
    {
        if (_popup.Visible)
        {
            _popup.Hide();
            return;
        }

        // Clicking the tray icon first takes focus from the panel (which hides it); do not reopen at once.
        if ((DateTime.Now - _popup.LastHidden).TotalMilliseconds < 300)
        {
            return;
        }

        OpenPopup();
    }

    private void OpenPopup()
    {
        _popup.PlaceNearTray();
        _popup.Show();
        _popup.Activate();
        _popup.FocusFirst();
    }

    private void OnBannerAction(BannerAction action)
    {
        switch (action)
        {
            case BannerAction.SignIn: SignIn(); break;
            case BannerAction.Install: Proc.Open(_last?.InstallUrl ?? GitHubAppInfo.InstallUrl); break;
            case BannerAction.Retry: RefreshNow(); break;
            case BannerAction.Status: Proc.Open("https://www.githubstatus.com/"); break;
            case BannerAction.Settings: OpenSettings(); break;
            case BannerAction.Diagnostics: OpenDiagnostics(); break;
        }
    }

    // ---------- refreshing ----------

    /// <summary>Refresh at once. If a refresh is running, one more follows it.</summary>
    private void RefreshNow()
    {
        if (_fetching)
        {
            _refreshAgain = true;
            return;
        }

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _fetching = true;
        _pollTimer.Stop();
        try
        {
            var snapshot = await _source.FetchAsync(CancellationToken.None);
            _failuresInRow = snapshot.Failure is FailureKind.Offline or FailureKind.ServerError or FailureKind.Unexpected
                ? _failuresInRow + 1
                : 0;

            if (snapshot.Failure == FailureKind.None)
            {
                var signature = string.Join(",", snapshot.Runs.Select(r => $"{r.Id}:{r.State}"));
                if (signature != _runsSignature)
                {
                    _runsSignature = signature;
                    _lastChange = DateTimeOffset.Now;
                }

                Notify(snapshot);
                SnapshotStore.Save(snapshot);
            }

            _last = snapshot;
            _popup.ShowSnapshot(snapshot);
            UpdateIcon();
        }
        catch (Exception ex)
        {
            Problems.Report("Refresh failed", ex);
            _failuresInRow++;
        }
        finally
        {
            _fetching = false;
            if (_refreshAgain)
            {
                _refreshAgain = false;
                RefreshNow();
            }
            else
            {
                Schedule();
            }
        }
    }

    private void Schedule()
    {
        if (_locked || _last is null)
        {
            return;
        }

        var next = PollPolicy.Next(_last, _config.RefreshSeconds, DateTimeOffset.Now - _lastChange, _failuresInRow,
            _services?.Api.MinPollInterval, DateTimeOffset.Now);
        _pollTimer.Interval = (int)Math.Clamp(next.TotalMilliseconds, 5000, int.MaxValue);
        _pollTimer.Start();
    }

    private async Task RefreshLocalAsync()
    {
        try
        {
            _local = await _source.FetchLocalAsync(CancellationToken.None);
            _popup.ShowLocal(_local);
            UpdateIcon();
        }
        catch (Exception ex)
        {
            Log.Error("Reading local runners failed", ex);
        }
    }

    // ---------- Windows events: lock, sleep, network ----------

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) => Post(() =>
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            _locked = true;
            _pollTimer.Stop();
            _localTimer.Stop();
            Log.Info("Screen locked: refresh paused.");
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
        {
            _locked = false;
            _localTimer.Start();
            Log.Info("Screen unlocked: refreshing.");
            RefreshNow();
        }
    });

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            // Give the network a few seconds to come back after sleep.
            Post(() => _ = DelayThenRefreshAsync(TimeSpan.FromSeconds(5)));
        }
    }

    private void OnNetworkChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
        {
            Post(() => _ = DelayThenRefreshAsync(TimeSpan.FromSeconds(2)));
        }
    }

    private async Task DelayThenRefreshAsync(TimeSpan delay)
    {
        await Task.Delay(delay);
        if (!_locked) RefreshNow();
    }

    private void Post(Action action) => _ui.Post(_ =>
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Problems.Report("Unexpected error", ex);
        }
    }, null);

    // ---------- notifications and icon ----------

    private void Notify(Snapshot snapshot)
    {
        foreach (var run in snapshot.Runs)
        {
            var known = _seen.TryGetValue(run.Id, out var before);
            _seen[run.Id] = run.State;
            if (_firstLoad || (known && before == run.State))
            {
                continue;
            }

            if (run.State == RunState.Failure && _config.NotifyOnFailure)
            {
                Balloon($"{run.Repo} failed", $"{run.Workflow} on {run.Branch} ({run.Actor})", ToolTipIcon.Error, run.Url);
            }
            else if (run.State == RunState.Success && _config.NotifyOnSuccess)
            {
                Balloon($"{run.Repo} passed", $"{run.Workflow} on {run.Branch}", ToolTipIcon.Info, run.Url);
            }
        }

        // Keep memory small: forget runs that are no longer listed.
        foreach (var id in _seen.Keys.Except(snapshot.Runs.Select(r => r.Id)).ToList())
        {
            _seen.Remove(id);
        }

        _firstLoad = false;
    }

    private void Balloon(string title, string text, ToolTipIcon icon, string? url)
    {
        _balloonUrl = url;
        _icon.ShowBalloonTip(8000, title, text, icon);
    }

    private void OnProblem(object? sender, string message) => Post(() =>
    {
        if ((DateTime.Now - _lastProblemBalloon).TotalMinutes < 10)
        {
            return;
        }

        _lastProblemBalloon = DateTime.Now;
        Balloon("RunnerTray", message, ToolTipIcon.Warning, null);
    });

    private void UpdateIcon()
    {
        var s = _last;
        TrayState state;
        string tip;
        if (s is null)
        {
            state = TrayState.Unknown;
            tip = "loading…";
        }
        else if (s.Failure == FailureKind.SignInRequired)
        {
            state = TrayState.Error;
            tip = "sign in needed";
        }
        else if (s.Failure is FailureKind.Offline)
        {
            state = TrayState.Unknown;
            tip = "offline";
        }
        else if (s.Failure != FailureKind.None)
        {
            state = TrayState.Error;
            tip = s.Failure == FailureKind.RateLimited ? "paused (rate limit)" : "GitHub problem";
        }
        else
        {
            var failing = RunLogic.Failing(s.Runs).Count;
            var running = s.Runs.Count(r => r.State == RunState.Running) + _local.Count(j => j.Busy);
            state = failing > 0 ? TrayState.Failing : running > 0 ? TrayState.Busy : TrayState.Idle;
            tip = failing > 0 ? $"{failing} failing" : running > 0 ? $"{running} running" : "all passing";
            if (_local.Count > 0)
            {
                tip += $" · this PC {_local.Count(j => j.Busy)}/{_local.Count} busy";
            }
        }

        _icon.Icon = Icons.Get(state);
        var text = $"RunnerTray: {tip}";
        _icon.Text = text.Length > 63 ? text[..63] : text; // NotifyIcon limit
    }

    // ---------- sign-in ----------

    private void SignIn(bool demo = false)
    {
        if (_services is null && !demo)
        {
            MessageBox.Show("Sign-in is not available in demo mode.", "RunnerTray", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _popup.Hide();
        using var form = demo || _services is null
            ? new SignInForm(
                _ => Task.FromResult(new DeviceCode("demo", "ABCD-1234", "https://github.com/login/device", TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(5))),
                (_, ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith<TokenSet>(_ => throw new OperationCanceledException(), TaskScheduler.Default))
            : new SignInForm(_services.Flow.StartAsync, _services.Flow.WaitForTokenAsync);

        if (form.ShowDialog() == DialogResult.OK && form.Result is { } tokens && _services is not null)
        {
            _services.SignedIn.Save(tokens); // raises Changed → OnSignInChanged
            Balloon("RunnerTray", "Signed in to GitHub.", ToolTipIcon.Info, null);
        }
    }

    private void SignOut()
    {
        if (_services is null)
        {
            return;
        }

        var answer = MessageBox.Show(
            "Sign out of GitHub in RunnerTray?\n\nTo remove RunnerTray's access completely, also revoke it on GitHub (Settings > Applications). Open that page now?",
            "Sign out - RunnerTray", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel)
        {
            return;
        }

        _services.SignedIn.SignOut();
        if (answer == DialogResult.Yes)
        {
            Proc.Open("https://github.com/settings/apps/authorizations");
        }
    }

    private void OnSignInChanged()
    {
        if (_services is null)
        {
            return;
        }

        _services.Api.ClearCache();
        _services.Source.ResetIdentity();
        SnapshotStore.Delete();
        _seen.Clear();
        _firstLoad = true;
        UpdateSignInItem();
        RefreshNow();
    }

    private void UpdateSignInItem()
    {
        var signedIn = _services?.SignedIn.IsSignedIn == true;
        _signInItem.Text = signedIn ? "Sign &out" : "&Sign in with GitHub…";
        _signInItem.Enabled = _services is not null;
    }

    // ---------- settings ----------

    private void OpenSettings()
    {
        _popup.Hide();
        var account = _services is null
            ? "Demo mode: no account."
            : _services.Tokens.Kind switch
            {
                TokenKind.SignedIn => $"Signed in with GitHub{(_last is { Stale: false } ? $", watching {_last.Owner}" : "")}.",
                TokenKind.Environment => "Using the GITHUB_TOKEN environment variable.",
                TokenKind.GitHubCli => "Using the GitHub CLI sign-in (gh). You can also sign in with GitHub from the menu.",
                _ => "Not signed in. Use Sign in with GitHub from the menu.",
            };
        using var form = new SettingsForm(_config, account, IsStartupEnabled(), _updater.Installed);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var updated = form.Result;
        SetStartup(form.StartWithWindows);
        _startupItem.Checked = form.StartWithWindows;
        if (_options.Demo)
        {
            _config = updated;
            return;
        }

        try
        {
            _ignoreConfigUntil = DateTime.Now.AddSeconds(2);
            updated.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"The settings could not be saved: {ex.Message}", "RunnerTray", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        ApplyConfig(updated);
    }

    private void ApplyConfig(AppConfig config)
    {
        _config = config;
        _popup.ConfigError = config.LoadError;
        _services?.Source.UpdateConfig(config);
        _services?.Api.ClearCache();
        _seen.Clear();
        _firstLoad = true;
        RefreshNow();
        _ = RefreshLocalAsync();
    }

    /// <summary>Reload settings when config.json is edited by hand.</summary>
    private FileSystemWatcher? WatchConfig()
    {
        try
        {
            Directory.CreateDirectory(AppConfig.Dir);
            var watcher = new FileSystemWatcher(AppConfig.Dir, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            var debounce = new System.Windows.Forms.Timer { Interval = 700 };
            debounce.Tick += (_, _) =>
            {
                debounce.Stop();
                if (DateTime.Now < _ignoreConfigUntil)
                {
                    return;
                }

                Log.Info("config.json changed; reloading.");
                ApplyConfig(AppConfig.Load());
            };
            void Changed(object s, FileSystemEventArgs e) => Post(() =>
            {
                debounce.Stop();
                debounce.Start();
            });
            watcher.Changed += Changed;
            watcher.Created += Changed;
            watcher.Renamed += (s, e) => Changed(s, e);
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Warn($"Cannot watch config.json: {ex.Message}");
            return null;
        }
    }

    // ---------- diagnostics ----------

    private void OpenDiagnostics()
    {
        _popup.Hide();
        using var form = new DiagnosticsForm(DiagnosticsReport);
        form.ShowDialog();
    }

    private string DiagnosticsReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"RunnerTray {AppVersion.Text} ({(_updater.Installed ? "installed" : "portable")}{(_options.Demo ? ", demo" : "")})");
        sb.AppendLine($"Windows {Environment.OSVersion.Version}, .NET {Environment.Version}, text scale {Theme.TextScale:P0}, high contrast {Theme.HighContrast}");
        sb.AppendLine();
        if (_services is { } s)
        {
            sb.AppendLine($"Token source:        {s.Tokens.Kind}");
            sb.AppendLine($"Rate limit left:     {s.Api.RateRemaining?.ToString() ?? "?"} of {s.Api.RateLimit?.ToString() ?? "?"}{(s.Api.RateReset is { } r ? $" (resets {r.ToLocalTime():HH:mm})" : "")}");
            sb.AppendLine($"Requests:            {s.Api.RequestCount} ({s.Api.NotModifiedCount} answered from cache with 304)");
            sb.AppendLine($"Last success:        {s.Api.LastSuccess?.ToLocalTime().ToString("T") ?? "never"}");
            sb.AppendLine($"Paused until:        {s.Api.PausedUntil?.ToLocalTime().ToString("T") ?? "-"}");
            sb.AppendLine($"Repositories:        {s.Source.WatchedRepos.Count} watched");
            sb.AppendLine($"Docker:              {(string.IsNullOrEmpty(_config.DockerJobLabel) ? "off" : s.Docker.Available ? "available" : "not available")}");
        }

        sb.AppendLine($"Next refresh in:     {(_pollTimer.Enabled ? $"{_pollTimer.Interval / 1000} s" : _locked ? "paused (screen locked)" : "-")}");
        sb.AppendLine($"Last result:         {_last?.Failure.ToString() ?? "-"}{(_last?.Error is { } e ? $": {e}" : "")}");
        sb.AppendLine($"Settings file:       {AppConfig.FilePath}{(_config.LoadError is null ? "" : " (has an error)")}");
        sb.AppendLine($"Log folder:          {Log.Dir}");
        sb.AppendLine();
        sb.AppendLine("Recent warnings and errors:");
        var recent = Log.Recent;
        if (recent.Count == 0) sb.AppendLine("  none");
        foreach (var line in recent.TakeLast(20)) sb.AppendLine("  " + line);
        return sb.ToString();
    }

    // ---------- updates ----------

    private async Task DelayedUpdateCheckAsync()
    {
        var testing = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RUNNERTRAY_UPDATE_DIR"));
        await Task.Delay(TimeSpan.FromSeconds(testing ? 3 : 30));
        await CheckUpdatesAsync();
    }

    private async Task CheckUpdatesAsync()
    {
        if (!_updater.Installed || !await _updater.CheckAsync(download: _config.AutoUpdate))
        {
            return;
        }

        _updateItem.Text = _updater.ReadyToRestart
            ? $"Restart to update to {_updater.PendingVersion}"
            : $"Update to {_updater.PendingVersion}…";
        _updateItem.Visible = true;
        Balloon("RunnerTray update", $"Version {_updater.PendingVersion} is ready. Choose it in the RunnerTray menu.", ToolTipIcon.Info, null);
    }

    // ---------- menu ----------

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("&Open", null, (_, _) => OpenPopup());
        menu.Items.Add("&Refresh now", null, (_, _) => RefreshNow());
        menu.Items.Add(new ToolStripSeparator());
        _signInItem.Click += (_, _) =>
        {
            if (_services?.SignedIn.IsSignedIn == true) SignOut();
            else SignIn();
        };
        UpdateSignInItem();
        menu.Items.Add(_signInItem);
        menu.Items.Add("Open in &GitHub", null, (_, _) =>
        {
            if (_last is { } s && s.Owner.Length > 0)
            {
                Proc.Open(s.OwnerIsOrg ? $"https://github.com/organizations/{s.Owner}/settings/actions/runners" : $"https://github.com/{s.Owner}");
            }
        });
        menu.Items.Add("S&ettings…", null, (_, _) => OpenSettings());
        menu.Items.Add("&Diagnostics…", null, (_, _) => OpenDiagnostics());
        _startupItem.Checked = IsStartupEnabled();
        _startupItem.CheckedChanged += (_, _) => SetStartup(_startupItem.Checked);
        menu.Items.Add(_startupItem);
        _updateItem.Click += async (_, _) => await _updater.ApplyAndRestartAsync();
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("E&xit", null, (_, _) => ExitThread());
        return menu;
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("RunnerTray") is string;
    }

    private static void SetStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;
            if (enabled) key.SetValue("RunnerTray", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("RunnerTray", throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warn($"Could not change Start with Windows: {ex.Message}");
        }
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        Problems.Raised -= OnProblem;
        _showSignal.Dispose();
        _configWatcher?.Dispose();
        _pollTimer.Dispose();
        _localTimer.Dispose();
        _updateTimer.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _popup.Dispose();
        _services?.Dispose();
        base.ExitThreadCore();
    }
}
