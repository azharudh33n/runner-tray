using System.Drawing.Drawing2D;

namespace RunnerTray;

public enum TrayState { Unknown, Idle, Busy, Failing, Error }

public enum Glyph { None, Check, Cross, Play, Clock, Dash, Bang, Dots }

/// <summary>
/// Status badges: a filled circle with a shape inside (check, cross, play, clock, ...).
/// The shape carries the meaning, so status is not shown by colour alone (WCAG 1.4.1).
/// </summary>
internal static class Badge
{
    public static Glyph For(RunState s) => s switch
    {
        RunState.Success => Glyph.Check,
        RunState.Failure => Glyph.Cross,
        RunState.Running => Glyph.Play,
        RunState.Queued => Glyph.Clock,
        RunState.Cancelled => Glyph.Dash,
        _ => Glyph.Dots,
    };

    public static void Draw(Graphics g, RectangleF r, Color fill, Glyph glyph, Color? ink = null)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(fill)) g.FillEllipse(b, r);
        if (Theme.HighContrast)
        {
            using var outline = new Pen(Theme.Fg, Math.Max(1f, r.Width / 12f));
            g.DrawEllipse(outline, r);
        }

        var c = ink ?? Theme.OnBadge;
        var w = Math.Max(1.4f, r.Width / 8f);
        using var pen = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(c);
        float X(float f) => r.X + r.Width * f;
        float Y(float f) => r.Y + r.Height * f;

        switch (glyph)
        {
            case Glyph.Check:
                g.DrawLines(pen, new[] { new PointF(X(.28f), Y(.52f)), new PointF(X(.44f), Y(.68f)), new PointF(X(.73f), Y(.35f)) });
                break;
            case Glyph.Cross:
                g.DrawLine(pen, X(.33f), Y(.33f), X(.67f), Y(.67f));
                g.DrawLine(pen, X(.67f), Y(.33f), X(.33f), Y(.67f));
                break;
            case Glyph.Play:
                g.FillPolygon(brush, new[] { new PointF(X(.38f), Y(.28f)), new PointF(X(.74f), Y(.5f)), new PointF(X(.38f), Y(.72f)) });
                break;
            case Glyph.Clock:
                g.DrawLines(pen, new[] { new PointF(X(.5f), Y(.27f)), new PointF(X(.5f), Y(.5f)), new PointF(X(.68f), Y(.6f)) });
                break;
            case Glyph.Dash:
                g.DrawLine(pen, X(.3f), Y(.5f), X(.7f), Y(.5f));
                break;
            case Glyph.Bang:
                g.DrawLine(pen, X(.5f), Y(.26f), X(.5f), Y(.56f));
                g.FillEllipse(brush, X(.5f) - w * .75f, Y(.72f) - w * .75f, w * 1.5f, w * 1.5f);
                break;
            case Glyph.Dots:
                foreach (var f in new[] { .3f, .5f, .7f })
                    g.FillEllipse(brush, X(f) - w * .6f, Y(.5f) - w * .6f, w * 1.2f, w * 1.2f);
                break;
        }

        g.SmoothingMode = old;
    }
}

/// <summary>Tray icons, drawn at runtime with the same badges.</summary>
internal static class Icons
{
    private static readonly Dictionary<TrayState, Icon> Cache = new();

    public static Color ColorOf(TrayState s) => s switch
    {
        TrayState.Idle => Theme.Green,
        TrayState.Busy => Theme.Blue,
        TrayState.Failing => Theme.Red,
        TrayState.Error => Theme.Yellow,
        _ => Theme.Gray,
    };

    public static Glyph GlyphOf(TrayState s) => s switch
    {
        TrayState.Idle => Glyph.Check,
        TrayState.Busy => Glyph.Play,
        TrayState.Failing => Glyph.Cross,
        TrayState.Error => Glyph.Bang,
        _ => Glyph.Dots,
    };

    public static string Words(TrayState s) => s switch
    {
        TrayState.Idle => "All passing",
        TrayState.Busy => "Jobs running",
        TrayState.Failing => "A run failed",
        TrayState.Error => "GitHub unreachable",
        _ => "Loading",
    };

    public static Icon Get(TrayState s)
    {
        if (!Cache.TryGetValue(s, out var icon))
        {
            using var bmp = Draw(s, 32);
            icon = Icon.FromHandle(bmp.GetHicon());
            Cache[s] = icon;
        }

        return icon;
    }

    public static Bitmap Draw(TrayState s, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        // Tray icons sit on the taskbar, not on the app background: always use the colour palette
        // with a dark glyph, which reads on light and dark taskbars.
        var fill = Theme.HighContrast ? SystemColors.Highlight : ColorOf(s);
        var ink = Theme.HighContrast ? SystemColors.HighlightText : Color.FromArgb(13, 17, 23);
        Badge.Draw(g, new RectangleF(1, 1, size - 2.5f, size - 2.5f), fill, GlyphOf(s), ink);
        return bmp;
    }
}
