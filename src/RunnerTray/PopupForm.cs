using System.Runtime.InteropServices;

namespace RunnerTray;

/// <summary>The panel that opens above the tray icon.</summary>
internal sealed class PopupForm : Form
{
    private readonly Label _title;
    private readonly Label _status;
    private readonly Button _refresh;
    private readonly SummaryControl _summary;
    private readonly Banner _banner;
    private readonly Panel _viewport;
    private readonly FlowLayoutPanel _list;
    private readonly System.Windows.Forms.Timer _tick;
    private readonly ToolTip _tips = new() { ShowAlways = true, AutoPopDelay = 15000 };

    private readonly List<RowControl> _noticeRows = new();
    private Snapshot? _snapshot;
    private IReadOnlyList<LocalJob> _local = Array.Empty<LocalJob>();
    private bool _hidePassing;

    public event EventHandler? RefreshRequested;

    /// <summary>The user chose the action in the banner (sign in, install, retry, ...).</summary>
    public event EventHandler<BannerAction>? BannerActionRequested;

    /// <summary>Problem with config.json, shown in the banner until fixed.</summary>
    public string? ConfigError { get; set; }

    public DateTime LastHidden { get; private set; }

    /// <summary>Leave passing runs out of the Recent runs list. The summary line still counts them.</summary>
    public bool HidePassingRuns
    {
        get => _hidePassing;
        set
        {
            if (_hidePassing == value) return;
            _hidePassing = value;
            if (_snapshot is not null) Rebuild();
        }
    }

    /// <summary>Hide when the popup loses focus (off for screenshots).</summary>
    public bool AutoHide { get; set; } = true;

    public PopupForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Bg;
        ForeColor = Theme.Fg;
        Font = Theme.Body;
        ClientSize = new Size(Theme.Scaled(480), Theme.Scaled(640));
        Text = "RunnerTray";
        AccessibleName = "RunnerTray, GitHub Actions status";

        // Header: title and refresh button, then status line, then summary. Sizes follow the text.
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 3,
            BackColor = Theme.Bg, Padding = new Padding(14, 10, 10, 6),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _title = new Label { Text = "RunnerTray", Font = Theme.Title, AutoSize = true, ForeColor = Theme.Fg, Margin = new Padding(0, 2, 0, 0) };

        // The RunnerTray mark, then the title, side by side.
        var titleRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Bg };
        if (BrandMark(LogicalToDeviceUnits(Theme.Scaled(24))) is { } mark)
        {
            titleRow.Controls.Add(new PictureBox
            {
                Image = mark, SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(0, 4, 8, 0),
                AccessibleRole = AccessibleRole.Graphic, AccessibleName = "RunnerTray logo",
            });
        }

        titleRow.Controls.Add(_title);
        _refresh = new Button
        {
            Text = "⟳  Refresh", AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Theme.Fg, BackColor = Theme.Surface,
            Font = Theme.Small, Cursor = Cursors.Hand, AccessibleName = "Refresh now", Padding = new Padding(4, 1, 4, 1),
            MinimumSize = new Size(Theme.Scaled(28), Theme.Scaled(28)),
        };
        _refresh.FlatAppearance.BorderColor = Theme.Border;
        _refresh.FlatAppearance.MouseOverBackColor = Theme.Hover;
        _refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);

        _status = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(1, 2, 0, 4) };
        _summary = new SummaryControl { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Margin = new Padding(0, 2, 0, 0) };

        header.Controls.Add(titleRow, 0, 0);
        header.Controls.Add(_refresh, 1, 0);
        header.Controls.Add(_status, 0, 1);
        header.SetColumnSpan(_status, 2);
        header.Controls.Add(_summary, 0, 2);
        header.SetColumnSpan(_summary, 2);

        _banner = new Banner { Dock = DockStyle.Top, Visible = false };
        _banner.ActionClicked += (_, a) => BannerActionRequested?.Invoke(this, a);

        // The list is wider than its viewport, so the light Windows scrollbar is clipped away.
        // Scrolling works with the mouse wheel, and keyboard focus scrolls the focused row into view.
        _viewport = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        _list = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = Point.Empty,
            AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(8, 0, 8, 10),
            AccessibleRole = AccessibleRole.List, AccessibleName = "Runs and runners",
        };
        _viewport.Controls.Add(_list);
        _viewport.Resize += (_, _) => FitRows();

        // Docking runs in reverse order of adding: header first, then error, then the list fills the rest.
        Controls.Add(_viewport);
        Controls.Add(_banner);
        Controls.Add(header);

        _tick = new System.Windows.Forms.Timer { Interval = 1000 };
        _tick.Tick += (_, _) =>
        {
            foreach (Control c in _list.Controls)
            {
                if (c is RowControl { Live: true } row)
                {
                    row.Invalidate();
                }
            }
        };
    }

    public void ShowSnapshot(Snapshot snapshot)
    {
        _snapshot = snapshot;
        Rebuild();
    }

    public void ShowLocal(IReadOnlyList<LocalJob> local)
    {
        var changed = local.Count != _local.Count || local.Zip(_local).Any(p => p.First != p.Second);
        _local = local;
        if (changed) Rebuild();
    }

    /// <summary>
    /// Place the popup next to the tray, on the screen where the cursor is. Opened from the keyboard there is
    /// no click position, so it goes in the corner of the main screen where the notification area is.
    /// </summary>
    public void PlaceNearTray(bool fromKeyboard = false)
    {
        var cursor = Cursor.Position;
        var wa = Screen.FromPoint(cursor).WorkingArea;
        var margin = LogicalToDeviceUnits(12);
        if (fromKeyboard)
        {
            var screen = Screen.PrimaryScreen ?? Screen.FromPoint(cursor);
            wa = screen.WorkingArea;
            var left = wa.Left > screen.Bounds.Left;  // taskbar on the left
            var top = wa.Top > screen.Bounds.Top;     // taskbar at the top
            Location = new Point(
                left ? wa.Left + margin : wa.Right - Width - margin,
                top ? wa.Top + margin : Math.Max(wa.Top + margin, wa.Bottom - Height - margin));
            return;
        }

        var x = Math.Clamp(cursor.X - Width / 2, wa.Left + margin, wa.Right - Width - margin);
        int y;
        if (cursor.Y >= wa.Bottom) y = wa.Bottom - Height - margin;      // taskbar at the bottom
        else if (cursor.Y <= wa.Top) y = wa.Top + margin;                // taskbar at the top
        else y = wa.Bottom - Height - margin;                            // taskbar at a side
        Location = new Point(x, Math.Max(wa.Top + margin, y));
    }

    /// <summary>Give keyboard focus to the first item, so Tab and arrow keys work at once.</summary>
    public void FocusFirst()
    {
        if (_banner.Visible && _banner.ActionButton.Visible)
        {
            _banner.ActionButton.Focus();
            return;
        }

        var first = _list.Controls.OfType<RowControl>().FirstOrDefault(r => r.TabStop);
        if (first is not null) first.Focus();
        else _refresh.Focus();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 11: rounded corners and a border colour.
        var round = 2;
        _ = DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        var border = ColorTranslator.ToWin32(Theme.Border);
        _ = DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _tick.Enabled = Visible;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (AutoHide)
        {
            Hide();
            LastHidden = DateTime.Now;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                Hide();
                LastHidden = DateTime.Now;
                return true;
            case Keys.F5:
                RefreshRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case Keys.Down:
            case Keys.Up:
                SelectNextControl(ActiveControl, keyData == Keys.Down, true, true, true);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Rebuild()
    {
        var s = _snapshot;
        var hadFocus = _list.ContainsFocus;
        _list.SuspendLayout();
        _tips.RemoveAll();
        while (_list.Controls.Count > 0)
        {
            var c = _list.Controls[0];
            _list.Controls.RemoveAt(0);
            c.Dispose();
        }

        if (s is not null)
        {
            _title.Text = $"RunnerTray  ·  {s.Owner}";
            _status.Text = (s.Stale && s.DataAt is { } dataAt ? $"Showing data from {dataAt.ToLocalTime():HH:mm}" : $"Updated {s.At.ToLocalTime():HH:mm:ss}")
                           + $"   ·   {s.Runs.Count} recent runs   ·   F5 refresh, Esc close";

            var running = s.Runs.Count(r => r.State == RunState.Running);
            var queued = s.Runs.Count(r => r.State == RunState.Queued);
            var failing = Failing(s.Runs).Count;
            var parts = new List<SummaryPart>
            {
                running > 0 ? new($"{running} running", Theme.Blue, Glyph.Play) : new("nothing running", Theme.Gray, Glyph.Dash),
            };
            if (queued > 0) parts.Add(new($"{queued} queued", Theme.Yellow, Glyph.Clock));
            parts.Add(failing > 0 ? new($"{failing} failing", Theme.Red, Glyph.Cross) : new("no failures", Theme.Green, Glyph.Check));
            if (s.Runners.Count > 0)
            {
                var online = s.Runners.Count(r => r.Online);
                parts.Add(online == s.Runners.Count
                    ? new($"{online}/{s.Runners.Count} runners online", Theme.Green, Glyph.Check)
                    : new($"{online}/{s.Runners.Count} runners online", Theme.Yellow, Glyph.Bang));
            }

            _summary.Parts = parts;

            UpdateBanner(s);

            foreach (var notice in s.Notices)
            {
                _noticeRows.Add(new RowControl
                {
                    Compact = true, Dot = Theme.Yellow, Glyph = Glyph.Bang, StateWords = "Note", Title = notice,
                });
            }
        }

        foreach (var row in _noticeRows) AddRow(row);
        _noticeRows.Clear();

        // 1. Runner containers on this machine.
        if (_local.Count > 0)
        {
            var busy = _local.Where(j => j.Busy).ToList();
            AddSection($"THIS MACHINE   {busy.Count} of {_local.Count} busy");
            foreach (var j in busy)
            {
                var started = j.Started;
                AddRow(new RowControl
                {
                    Dot = Theme.Blue, Glyph = Glyph.Play, Live = true, StateWords = "Running on this machine",
                    Title = j.Repo ?? "(checking out)", Subtitle = j.Job ?? "",
                    RightText = () => started is null ? "" : Theme.Elapsed(started.Value),
                    RightWords = () => started is null ? "" : Theme.ElapsedWords(started.Value),
                    Url = j.Repo is null || s is null ? null : $"https://github.com/{s.Owner}/{j.Repo}/actions",
                });
            }

            var idle = _local.Count - busy.Count;
            if (idle > 0)
            {
                AddRow(new RowControl
                {
                    Dot = Theme.Green, Glyph = Glyph.Check, Compact = true, StateWords = "Idle",
                    Title = $"{idle} idle", Subtitle = "waiting for a job",
                });
            }
        }

        if (s is not null)
        {
            // 2. Recent runs.
            var shown = RunLogic.Visible(s.Runs, _hidePassing);
            var hidden = s.Runs.Count - shown.Count;
            AddSection(hidden > 0 ? $"RECENT RUNS   {hidden} passing hidden" : "RECENT RUNS");
            if (s.Runs.Count == 0)
            {
                AddRow(new RowControl { Compact = true, Title = "No runs yet", Subtitle = "in the watched repositories" });
            }
            else if (shown.Count == 0)
            {
                AddRow(new RowControl { Compact = true, Title = "All recent runs passed", Subtitle = "passing runs are hidden" });
            }

            foreach (var r in shown)
            {
                var run = r;
                AddRow(new RowControl
                {
                    Dot = Theme.For(run.State),
                    Glyph = Badge.For(run.State),
                    Live = run.State == RunState.Running,
                    StateWords = Theme.Words(run.State),
                    Title = $"{run.Repo}  ·  {run.Workflow}",
                    Subtitle = $"{run.Branch}   ·   {EventName(run.Event)}   ·   {run.Actor}",
                    Description = run.Message,
                    RightText = () => run.State == RunState.Running ? Theme.Elapsed(run.Created) : Theme.Age(run.Updated),
                    RightWords = () => run.State == RunState.Running ? Theme.ElapsedWords(run.Created) : Theme.Age(run.Updated),
                    Url = run.Url,
                });
            }

            // 3. Self-hosted runners.
            if (s.Runners.Count > 0)
            {
                AddSection($"SELF-HOSTED RUNNERS   {s.Runners.Count(r => r.Busy)} busy · {s.Runners.Count(r => r.Online)} online");
                foreach (var r in s.Runners)
                {
                    var words = !r.Online ? "offline" : r.Busy ? "busy" : "idle";
                    AddRow(new RowControl
                    {
                        Compact = true,
                        Dot = !r.Online ? Theme.Gray : r.Busy ? Theme.Blue : Theme.Green,
                        Glyph = !r.Online ? Glyph.Dash : r.Busy ? Glyph.Play : Glyph.Check,
                        StateWords = $"Runner {words}",
                        Title = r.Name,
                        Subtitle = string.Join(", ", r.Labels),
                        RightText = () => words,
                        Url = s.OwnerIsOrg ? $"https://github.com/organizations/{s.Owner}/settings/actions/runners" : null,
                    });
                }
            }
        }

        FitRows();
        _list.ResumeLayout();
        if (hadFocus && Visible) FocusFirst();
    }

    /// <summary>One banner at a time, most important first, each with a way out.</summary>
    private void UpdateBanner(Snapshot s)
    {
        string? text = null;
        BannerAction? action = null;
        var urgent = true;
        if (ConfigError is not null)
        {
            text = ConfigError;
            action = BannerAction.Settings;
        }
        else switch (s.Failure)
        {
            case FailureKind.SignInRequired:
                text = "Sign in to GitHub to see your runs.";
                action = BannerAction.SignIn;
                urgent = false;
                break;
            case FailureKind.RateLimited:
                text = s.RetryAt is { } retry
                    ? $"GitHub rate limit reached. RunnerTray waits until {retry.ToLocalTime():HH:mm}."
                    : "GitHub rate limit reached. RunnerTray waits and tries again.";
                break;
            case FailureKind.Offline:
                text = s.DataAt is { } at ? $"Offline. Showing data from {at.ToLocalTime():HH:mm}." : "Offline. No data yet.";
                action = BannerAction.Retry;
                break;
            case FailureKind.ServerError:
                text = "GitHub is having problems. RunnerTray keeps trying.";
                action = BannerAction.Status;
                break;
            case FailureKind.None:
                if (s.InstallUrl is not null)
                {
                    text = $"Install RunnerTray on {s.Owner} to see its repositories.";
                    action = BannerAction.Install;
                    urgent = false;
                }

                break;
            default:
                text = s.Error ?? "Something went wrong.";
                action = BannerAction.Diagnostics;
                break;
        }

        _banner.Show(text, action, urgent);
    }

    /// <summary>Runs whose latest run for the same workflow and branch failed.</summary>
    public static List<RunInfo> Failing(IEnumerable<RunInfo> runs) => RunLogic.Failing(runs);
    private void AddSection(string text)
    {
        _list.Controls.Add(new Label
        {
            Text = text, Font = Theme.Section, ForeColor = Theme.Muted, AutoSize = false,
            Height = LogicalToDeviceUnits(Theme.Scaled(30)), TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(LogicalToDeviceUnits(6), 0, 0, LogicalToDeviceUnits(4)), Margin = Padding.Empty,
            AccessibleRole = AccessibleRole.StaticText,
        });
    }

    private void AddRow(RowControl row)
    {
        row.Height = LogicalToDeviceUnits(Theme.Scaled(row.Compact ? 34 : 48));
        _list.Controls.Add(row);
        if (!string.IsNullOrEmpty(row.Description))
        {
            _tips.SetToolTip(row, $"{row.Title}\n{row.Description}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tips.Dispose();
        }

        base.Dispose(disposing);
    }

    private void FitRows()
    {
        _list.Size = new Size(_viewport.ClientSize.Width + SystemInformation.VerticalScrollBarWidth, _viewport.ClientSize.Height);
        var w = _viewport.ClientSize.Width - _list.Padding.Horizontal;
        foreach (Control c in _list.Controls) c.Width = w;
    }

    private static string EventName(string e) => e switch
    {
        "push" => "push",
        "pull_request" => "pull request",
        "workflow_dispatch" => "manual",
        "schedule" => "scheduled",
        _ => e.Replace('_', ' '),
    };

    /// <summary>The RunnerTray mark for the header, from the embedded TrayIcons/brand-*.png.</summary>
    private static Bitmap? BrandMark(int size)
    {
        var pick = new[] { 20, 24, 32, 40 }.FirstOrDefault(x => x >= size, 40);
        using var stream = typeof(PopupForm).Assembly.GetManifestResourceStream($"RunnerTray.TrayIcons.brand-{pick}.png");
        if (stream is null || Theme.HighContrast) return null;
        using var src = new Bitmap(stream);
        return new Bitmap(src, size, size);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

internal sealed record SummaryPart(string Text, Color Color, Glyph Glyph);

/// <summary>
/// Status parts in the header, each with a badge shape and words.
/// Parts wrap onto more lines when the panel is narrow or Windows text is large (WCAG 1.4.10).
/// </summary>
internal sealed class SummaryControl : Control
{
    private IReadOnlyList<SummaryPart> _parts = Array.Empty<SummaryPart>();

    public SummaryControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
        AccessibleRole = AccessibleRole.StaticText;
    }

    public IReadOnlyList<SummaryPart> Parts
    {
        get => _parts;
        set
        {
            _parts = value;
            AccessibleName = "Summary: " + string.Join(", ", value.Select(p => p.Text));
            Height = GetPreferredSize(new Size(Width, 0)).Height;
            Invalidate();
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        var places = Place(width);
        var lines = places.Count == 0 ? 1 : places.Max(p => p.Line) + 1;
        return new Size(width, lines * LineHeight);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        var h = GetPreferredSize(new Size(Width, 0)).Height;
        if (h != Height) Height = h;
    }

    private int LineHeight => TextRenderer.MeasureText("Ag", Theme.Body).Height + LogicalToDeviceUnits(6);
    private int BadgeSize => LogicalToDeviceUnits(Theme.Scaled(14));

    private List<(SummaryPart Part, int X, int Line)> Place(int width)
    {
        var result = new List<(SummaryPart, int, int)>();
        var x = 2;
        var line = 0;
        var gap = LogicalToDeviceUnits(14);
        foreach (var p in _parts)
        {
            var w = BadgeSize + LogicalToDeviceUnits(6) + TextRenderer.MeasureText(p.Text, Theme.Body).Width;
            if (x > 2 && x + w > width)
            {
                x = 2;
                line++;
            }

            result.Add((p, x, line));
            x += w + gap;
        }

        return result;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var lh = LineHeight;
        var badge = BadgeSize;
        foreach (var (p, x, line) in Place(Width))
        {
            var top = line * lh;
            Badge.Draw(g, new RectangleF(x, top + (lh - badge) / 2f, badge, badge), p.Color, p.Glyph);
            var tx = x + badge + LogicalToDeviceUnits(6);
            var size = TextRenderer.MeasureText(g, p.Text, Theme.Body);
            TextRenderer.DrawText(g, p.Text, Theme.Body, new Rectangle(tx, top, size.Width, lh), Theme.Fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}

public enum BannerAction { SignIn, Install, Retry, Status, Settings, Diagnostics }

/// <summary>A message line under the header with one action button. Announced to screen readers.</summary>
internal sealed class Banner : Panel
{
    private readonly Label _text;
    private readonly Button _button;
    private BannerAction? _action;

    public Banner()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14, 8, 10, 8);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _text = new Label { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Fg, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 8, 4) };
        _button = new Button
        {
            AutoSize = true, FlatStyle = FlatStyle.Flat, Font = Theme.Small, ForeColor = Theme.Fg, BackColor = Theme.Surface,
            Anchor = AnchorStyles.Right, MinimumSize = new Size(Theme.Scaled(28), Theme.Scaled(28)), Cursor = Cursors.Hand,
        };
        _button.FlatAppearance.BorderColor = Theme.Border;
        _button.Click += (_, _) => { if (_action is { } a) ActionClicked?.Invoke(this, a); };
        layout.Controls.Add(_text, 0, 0);
        layout.Controls.Add(_button, 1, 0);
        Controls.Add(layout);
        Resize += (_, _) => _text.MaximumSize = new Size(Math.Max(100, Width - _button.Width - Padding.Horizontal - 24), 0);
    }

    public event EventHandler<BannerAction>? ActionClicked;

    public void Show(string? text, BannerAction? action, bool urgent)
    {
        if (text is null)
        {
            Visible = false;
            return;
        }

        var changed = text != _text.Text;
        BackColor = urgent ? Theme.ErrorBg : Theme.Surface;
        _text.Text = text;
        _action = action;
        _button.Visible = action is not null;
        _button.Text = action switch
        {
            BannerAction.SignIn => "Sign in with GitHub",
            BannerAction.Install => "Install",
            BannerAction.Retry => "Try again",
            BannerAction.Status => "GitHub status",
            BannerAction.Settings => "Open settings",
            BannerAction.Diagnostics => "Details",
            _ => "",
        };
        AccessibleRole = AccessibleRole.Alert;
        AccessibleName = text;
        Visible = true;
        if (changed && IsHandleCreated)
        {
            // Screen readers announce the new message (WCAG 4.1.3 Status messages).
            AccessibilityObject.RaiseAutomationNotification(
                System.Windows.Forms.Automation.AutomationNotificationKind.Other,
                System.Windows.Forms.Automation.AutomationNotificationProcessing.ImportantMostRecent, text);
        }
    }

    public Button ActionButton => _button;
}