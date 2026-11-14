namespace RunnerTray;

/// <summary>
/// Settings window. Standard controls with system colours: keyboard, Narrator and high contrast work as in
/// any Windows dialog. Every field has a visible label that is also its accessible name.
/// </summary>
internal sealed class SettingsForm : Form
{
    private const string HotkeyOff = "Off";

    private readonly AppConfig _config;
    private readonly TextBox _owner = new();
    private readonly TextBox _repos = new();
    private readonly NumericUpDown _lookback = new() { Minimum = 1, Maximum = 90 };
    private readonly NumericUpDown _runCount = new() { Minimum = 5, Maximum = 50 };
    private readonly CheckBox _hidePassing = new() { Text = "&Hide passing runs", AutoSize = true };
    private readonly NumericUpDown _refresh = new() { Minimum = 30, Maximum = 900, Increment = 30 };
    private readonly CheckBox _runners = new() { Text = "Show the organization's &self-hosted runners", AutoSize = true };
    private readonly TextBox _runnerFilter = new();
    private readonly TextBox _dockerLabel = new();
    private readonly TextBox _dockerDir = new();
    private readonly CheckBox _notifyFail = new() { Text = "Notify when a run &fails", AutoSize = true };
    private readonly CheckBox _notifyPass = new() { Text = "Notify when a run &passes", AutoSize = true };
    private readonly ComboBox _hotkey = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _startup = new() { Text = "Start with &Windows", AutoSize = true };
    private readonly CheckBox _autoUpdate = new() { Text = "Install &updates automatically", AutoSize = true };

    public SettingsForm(AppConfig config, string account, bool startWithWindows, bool updatesAvailable)
    {
        _config = config.Clone();

        Text = "Settings - RunnerTray";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 9.5f * Theme.TextScale);
        Padding = new Padding(16);
        Icon = AppIcon.Value;

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scaled(300)));

        void Heading(string text)
        {
            var l = new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 14, 0, 4), AccessibleRole = AccessibleRole.StaticText };
            grid.Controls.Add(l);
            grid.SetColumnSpan(l, 2);
        }

        void Row(string label, Control field, string? hint = null)
        {
            var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) };
            field.AccessibleName = label.Replace("&", "", StringComparison.Ordinal).TrimEnd(':');
            field.Dock = DockStyle.Fill;
            if (hint is not null) field.AccessibleDescription = hint;
            grid.Controls.Add(l);
            grid.Controls.Add(field);
            if (hint is not null)
            {
                grid.Controls.Add(new Label());
                grid.Controls.Add(new Label { Text = hint, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(Theme.Scaled(300), 0), Margin = new Padding(3, 0, 0, 6) });
            }
        }

        void Check(CheckBox box)
        {
            box.Margin = new Padding(0, 4, 0, 4);
            grid.Controls.Add(box);
            grid.SetColumnSpan(box, 2);
        }

        Heading("Account");
        var who = new Label { Text = account, AutoSize = true, MaximumSize = new Size(Theme.Scaled(460), 0), Margin = new Padding(0, 0, 0, 4) };
        grid.Controls.Add(who);
        grid.SetColumnSpan(who, 2);

        Heading("What to watch");
        Row("&Owner:", _owner, "User or organization. Empty: your own account.");
        Row("&Repositories:", _repos, "Comma-separated names. Empty: all with recent pushes.");
        Row("&Look back (days):", _lookback);
        Row("Runs &shown:", _runCount);
        Check(_hidePassing);
        Row("Refresh when &idle (seconds):", _refresh, "Running jobs always refresh every 15 seconds.");

        Heading("Runners");
        Check(_runners);
        Row("Runner name &contains:", _runnerFilter);
        Row("&Docker label on this PC:", _dockerLabel, "For example ci-job=build-box. Empty: off.");
        Row("Runner work &folder:", _dockerDir);

        Heading("Notifications, shortcut and start");
        Check(_notifyFail);
        Check(_notifyPass);
        Row("Shortcut &key:", _hotkey, "Opens or closes the panel from anywhere.");
        Check(_startup);
        Check(_autoUpdate);
        _autoUpdate.Enabled = updatesAvailable;
        if (!updatesAvailable)
        {
            _autoUpdate.Text += " (release version only)";
        }

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 0) };
        var save = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var file = new LinkLabel { Text = "Open settings file", AutoSize = true, Margin = new Padding(0, 8, 24, 0) };
        file.LinkClicked += (_, _) => Proc.Open(AppConfig.FilePath);
        buttons.Controls.AddRange(new Control[] { cancel, save, file });
        grid.Controls.Add(buttons);
        grid.SetColumnSpan(buttons, 2);
        AcceptButton = save;
        CancelButton = cancel;

        Controls.Add(grid);

        _owner.Text = _config.Owner;
        _repos.Text = string.Join(", ", _config.Repos);
        _lookback.Value = _config.LookbackDays;
        _runCount.Value = _config.RunCount;
        _hidePassing.Checked = _config.HidePassingRuns;
        _hotkey.Items.AddRange(new object[] { HotkeyOff, GlobalHotkey.Default, "Ctrl+Alt+R", "Ctrl+Alt+Shift+R" });
        var hotkey = string.IsNullOrEmpty(_config.Hotkey) ? HotkeyOff : _config.Hotkey;
        if (!_hotkey.Items.Contains(hotkey)) _hotkey.Items.Add(hotkey); // a shortcut written in the settings file
        _hotkey.SelectedItem = hotkey;
        _refresh.Value = _config.RefreshSeconds;
        _runners.Checked = _config.ShowSelfHostedRunners;
        _runnerFilter.Text = _config.RunnerNameFilter;
        _dockerLabel.Text = _config.DockerJobLabel;
        _dockerDir.Text = _config.DockerWorkDir;
        _notifyFail.Checked = _config.NotifyOnFailure;
        _notifyPass.Checked = _config.NotifyOnSuccess;
        _startup.Checked = startWithWindows;
        _autoUpdate.Checked = _config.AutoUpdate;
    }

    public bool StartWithWindows => _startup.Checked;

    /// <summary>The edited settings, already checked and kept in safe ranges.</summary>
    public AppConfig Result
    {
        get
        {
            _config.Owner = _owner.Text;
            _config.Repos = _repos.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            _config.LookbackDays = (int)_lookback.Value;
            _config.RunCount = (int)_runCount.Value;
            _config.HidePassingRuns = _hidePassing.Checked;
            _config.Hotkey = _hotkey.SelectedItem is string picked && picked != HotkeyOff ? picked : "";
            _config.RefreshSeconds = (int)_refresh.Value;
            _config.ShowSelfHostedRunners = _runners.Checked;
            _config.RunnerNameFilter = _runnerFilter.Text;
            _config.DockerJobLabel = _dockerLabel.Text;
            _config.DockerWorkDir = _dockerDir.Text;
            _config.NotifyOnFailure = _notifyFail.Checked;
            _config.NotifyOnSuccess = _notifyPass.Checked;
            _config.AutoUpdate = _autoUpdate.Checked;
            _config.Normalize();
            return _config;
        }
    }
}
