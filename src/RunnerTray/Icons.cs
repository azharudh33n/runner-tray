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

/// <summary>
/// Tray icons: the RunnerTray "Track" mark whose light shows the status with a colour and a shape.
/// Prebuilt PNGs (TrayIcons/*.png, from tools/make-icon.ps1) are embedded per status, taskbar theme
/// (dark or light) and size; the size closest to the system's small-icon size is used.
/// </summary>
internal static class Icons
{
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48 };
    private static readonly Dictionary<(TrayState, bool, int), Icon> Cache = new();

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
        _ => Glyph.Dash,
    };

    public static string Words(TrayState s) => s switch
    {
        TrayState.Idle => "All passing",
        TrayState.Busy => "Jobs running",
        TrayState.Failing => "A run failed",
        TrayState.Error => "Needs attention",
        _ => "Loading",
    };

    private static string FileState(TrayState s) => s switch
    {
        TrayState.Idle => "pass",
        TrayState.Busy => "run",
        TrayState.Failing => "fail",
        TrayState.Error => "error",
        _ => "idle",
    };

    /// <summary>True when Windows uses a light taskbar (Settings > Personalization > Colors).</summary>
    public static bool LightTaskbar()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
    }

    /// <summary>Forget cached icons, for example after the taskbar theme or display scale changed.</summary>
    public static void Reset()
    {
        foreach (var icon in Cache.Values) icon.Dispose();
        Cache.Clear();
    }

    public static Icon Get(TrayState s)
    {
        var light = LightTaskbar();
        var want = SystemInformation.SmallIconSize.Width;
        var size = Sizes.FirstOrDefault(x => x >= want, Sizes[^1]);
        if (Cache.TryGetValue((s, light, size), out var cached)) return cached;

        Icon icon;
        var name = $"RunnerTray.TrayIcons.{FileState(s)}-{(light ? "light" : "dark")}-{size}.png";
        using var stream = typeof(Icons).Assembly.GetManifestResourceStream(name);
        if (stream is not null && !Theme.HighContrast)
        {
            using var bmp = new Bitmap(stream);
            icon = Icon.FromHandle(bmp.GetHicon());
        }
        else
        {
            // High contrast mode, or a missing file: draw a status badge in system colours.
            using var bmp = Draw(s, size);
            icon = Icon.FromHandle(bmp.GetHicon());
        }

        Cache[(s, light, size)] = icon;
        return icon;
    }

    public static Bitmap Draw(TrayState s, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        var fill = Theme.HighContrast ? SystemColors.Highlight : ColorOf(s);
        var ink = Theme.HighContrast ? SystemColors.HighlightText : Color.FromArgb(14, 21, 37);
        Badge.Draw(g, new RectangleF(1, 1, size - 2.5f, size - 2.5f), fill, GlyphOf(s), ink);
        return bmp;
    }

    /// <summary>A tray icon image for screenshots.</summary>
    public static Bitmap Tray(TrayState s, bool light, int size)
    {
        using var stream = typeof(Icons).Assembly.GetManifestResourceStream(
            $"RunnerTray.TrayIcons.{FileState(s)}-{(light ? "light" : "dark")}-{Sizes.FirstOrDefault(x => x >= size, 48)}.png");
        return stream is null ? Draw(s, size) : new Bitmap(new Bitmap(stream), size, size);
    }
}