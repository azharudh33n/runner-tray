namespace RunnerTray;

/// <summary>Diagnostics: what RunnerTray is doing, for troubleshooting and bug reports. Contains no tokens.</summary>
internal sealed class DiagnosticsForm : Form
{
    private readonly TextBox _text;
    private readonly Func<string> _report;

    public DiagnosticsForm(Func<string> report)
    {
        _report = report;
        Text = "Diagnostics - RunnerTray";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f * Theme.TextScale);
        ClientSize = new Size(Theme.Scaled(640), Theme.Scaled(480));
        MinimumSize = new Size(400, 300);
        Icon = AppIcon.Value;

        _text = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9.5f * Theme.TextScale), AccessibleName = "Diagnostics report",
        };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        var copy = new Button { Text = "&Copy", AutoSize = true };
        copy.Click += (_, _) => Clipboard.SetText(_text.Text);
        var refresh = new Button { Text = "&Refresh", AutoSize = true };
        refresh.Click += (_, _) => _text.Text = _report();
        var logs = new Button { Text = "Open &log folder", AutoSize = true };
        logs.Click += (_, _) =>
        {
            Directory.CreateDirectory(Log.Dir);
            Proc.Open(Log.Dir);
        };
        var issue = new Button { Text = "Report an &issue", AutoSize = true };
        issue.Click += (_, _) => Proc.Open(IssueUrl());
        buttons.Controls.AddRange(new Control[] { close, copy, refresh, logs, issue });
        CancelButton = close;

        Controls.Add(_text);
        Controls.Add(buttons);
        _text.Text = _report();
    }

    /// <summary>A new-issue link with version information only (no tokens, no repository names).</summary>
    private static string IssueUrl()
    {
        var body = $"**What happened**\n\n\n**What you expected**\n\n\n---\nRunnerTray {AppVersion.Text}, {Environment.OSVersion.VersionString}, .NET {Environment.Version}";
        return "https://github.com/azharudh33n/runner-tray/issues/new?body=" + Uri.EscapeDataString(body);
    }
}
