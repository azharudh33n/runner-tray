using System.Drawing.Drawing2D;

namespace RunnerTray;

/// <summary>
/// One line in the popup: status badge, title, subtitle, and a right-aligned value.
/// Accessible: focusable with Tab, opens with Enter or Space, visible focus ring, and a
/// screen-reader name that includes the status in words (WCAG 2.1.1, 2.4.7, 4.1.2, 1.4.1).
/// </summary>
internal sealed class RowControl : Control
{
    private bool _hover;

    public Color Dot { get; init; } = Theme.Gray;
    public Glyph Glyph { get; init; } = Glyph.None;
    public bool Live { get; init; }
    public string StateWords { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public Func<string> RightText { get; init; } = () => "";
    public Func<string>? RightWords { get; init; }
    public string? Url { get; init; }
    public bool Compact { get; init; }

    public RowControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Bg;
        Margin = new Padding(0, 0, 0, 2);
    }

    /// <summary>The text a screen reader announces for this row.</summary>
    public string SpokenText
    {
        get
        {
            var parts = new[] { StateWords, Title, Subtitle, (RightWords ?? RightText)() };
            return string.Join(". ", parts.Where(p => !string.IsNullOrWhiteSpace(p))).Replace("  ·  ", ", ").Replace("   ·   ", ", ");
        }
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        TabStop = Url is not null;
        Cursor = Url is null ? Cursors.Default : Cursors.Hand;
        AccessibleName = SpokenText;
        AccessibleRole = Url is null ? AccessibleRole.ListItem : AccessibleRole.Link;
        AccessibleDefaultActionDescription = Url is null ? null : "Open on GitHub";
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new RowAccessible(this);

    protected override void OnMouseEnter(EventArgs e) { _hover = Url is not null; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        (Parent as ScrollableControl)?.ScrollControlIntoView(this);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Activate();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            Activate();
            e.Handled = true;
        }
    }

    public void Activate()
    {
        if (Url is not null) Proc.Open(Url);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float S(float v) => LogicalToDeviceUnits((int)Math.Round(v * 10 * Theme.TextScale)) / 10f;

        if (_hover || Focused)
        {
            using var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), S(6));
            if (_hover)
            {
                using var hover = new SolidBrush(Theme.Hover);
                g.FillPath(hover, path);
            }

            if (Focused)
            {
                using var ring = new Pen(Theme.Focus, Math.Max(2f, S(2)));
                g.DrawPath(ring, path);
            }
        }

        // Status badge: colour plus a shape, so the meaning does not depend on colour.
        var cy = Height / 2f;
        var size = S(16);
        var bx = S(10);
        if (Live)
        {
            using var halo = new SolidBrush(Color.FromArgb(70, Dot));
            if (!Theme.HighContrast) g.FillEllipse(halo, bx - S(3), cy - size / 2 - S(3), size + S(6), size + S(6));
        }

        Badge.Draw(g, new RectangleF(bx, cy - size / 2, size, size), Dot, Glyph);

        var fg = _hover && Theme.HighContrast ? SystemColors.HighlightText : Theme.Fg;
        var muted = _hover && Theme.HighContrast ? SystemColors.HighlightText : Theme.Muted;
        var right = RightText();
        var rightSize = TextRenderer.MeasureText(g, right, Theme.Small);
        var textX = (int)S(36);
        var textW = Width - textX - rightSize.Width - (int)S(18);
        const TextFormatFlags flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        if (Compact || string.IsNullOrEmpty(Subtitle))
        {
            var r = new Rectangle(textX, 0, textW, Height);
            TextRenderer.DrawText(g, Title, Theme.Body, r, fg, flags | TextFormatFlags.VerticalCenter);
            if (!string.IsNullOrEmpty(Subtitle))
            {
                var tw = Math.Min(TextRenderer.MeasureText(g, Title, Theme.Body).Width, textW);
                TextRenderer.DrawText(g, Subtitle, Theme.Small,
                    new Rectangle(textX + tw + (int)S(6), 0, Math.Max(0, textW - tw - (int)S(6)), Height), muted,
                    flags | TextFormatFlags.VerticalCenter);
            }
        }
        else
        {
            var lineH = TextRenderer.MeasureText(g, "Ag", Theme.Strong).Height;
            var subH = TextRenderer.MeasureText(g, "Ag", Theme.Small).Height;
            var top = (Height - lineH - subH) / 2;
            TextRenderer.DrawText(g, Title, Theme.Strong, new Rectangle(textX, top, textW, lineH), fg, flags);
            TextRenderer.DrawText(g, Subtitle, Theme.Small, new Rectangle(textX, top + lineH, textW, subH), muted, flags);
        }

        TextRenderer.DrawText(g, right, Theme.Small,
            new Rectangle(Width - rightSize.Width - (int)S(12), 0, rightSize.Width, Height),
            Live ? (Theme.HighContrast ? fg : Theme.Blue) : muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);
    }

    private sealed class RowAccessible : ControlAccessibleObject
    {
        private readonly RowControl _row;

        public RowAccessible(RowControl row) : base(row) => _row = row;

        public override string? Name => _row.SpokenText;
        public override AccessibleRole Role => _row.Url is null ? AccessibleRole.ListItem : AccessibleRole.Link;
        public override string? DefaultAction => _row.Url is null ? null : "Open on GitHub";
        public override void DoDefaultAction() => _row.Activate();
    }
}
