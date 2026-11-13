using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace RunnerTray;

/// <summary>
/// Colours and fonts.
/// Default: GitHub dark palette. Every text colour has a contrast ratio of at least 4.5:1
/// against the background (WCAG 2.2 AA, 1.4.3); status colours are at least 3:1 (1.4.11).
/// Windows high contrast mode: system colours are used instead (1.4.11, 1.4.8).
/// Windows "Text size" (Settings > Accessibility) scales all text (1.4.4).
/// </summary>
internal static class Theme
{
    public static readonly bool HighContrast = SystemInformation.HighContrast;

    /// <summary>Windows text scaling: 1.0 to 2.25.</summary>
    public static readonly float TextScale = ReadTextScale();

    // Contrast against Bg (#0D1117): Fg 16.0:1, Focus 9.7:1, Blue 7.5:1, Yellow 7.5:1, Green 7.4:1,
    // Muted 6.1:1 (4.9:1 on Hover), Red 5.6:1, Gray 4.1:1 (badges only). Badge glyphs (#0D1117) on each fill: at least 4.1:1.
    public static readonly Color Bg = HighContrast ? SystemColors.Window : Color.FromArgb(13, 17, 23);
    public static readonly Color Surface = HighContrast ? SystemColors.Window : Color.FromArgb(22, 27, 34);
    public static readonly Color Hover = HighContrast ? SystemColors.Highlight : Color.FromArgb(33, 38, 45);
    public static readonly Color Border = HighContrast ? SystemColors.WindowText : Color.FromArgb(48, 54, 61);
    public static readonly Color Fg = HighContrast ? SystemColors.WindowText : Color.FromArgb(230, 237, 243);
    public static readonly Color Muted = HighContrast ? SystemColors.WindowText : Color.FromArgb(139, 148, 158);
    public static readonly Color Focus = HighContrast ? SystemColors.Highlight : Color.FromArgb(121, 192, 255);
    public static readonly Color Green = HighContrast ? SystemColors.WindowText : Color.FromArgb(63, 185, 80);
    public static readonly Color Red = HighContrast ? SystemColors.WindowText : Color.FromArgb(248, 81, 73);
    public static readonly Color Blue = HighContrast ? SystemColors.WindowText : Color.FromArgb(88, 166, 255);
    public static readonly Color Yellow = HighContrast ? SystemColors.WindowText : Color.FromArgb(210, 153, 34);
    public static readonly Color Gray = HighContrast ? SystemColors.WindowText : Color.FromArgb(110, 118, 129);
    public static readonly Color ErrorBg = HighContrast ? SystemColors.Window : Color.FromArgb(73, 2, 2);

    /// <summary>Glyph colour drawn on top of a status badge.</summary>
    public static readonly Color OnBadge = HighContrast ? SystemColors.Window : Color.FromArgb(13, 17, 23);

    public static readonly Font Title = Make("Segoe UI Semibold", 12f);
    public static readonly Font Body = Make("Segoe UI", 9.5f);
    public static readonly Font Strong = Make("Segoe UI Semibold", 9.5f);
    public static readonly Font Small = Make("Segoe UI", 8.5f);
    public static readonly Font Section = Make("Segoe UI Semibold", 8f);

    public static int Scaled(int logical) => (int)Math.Round(logical * TextScale);

    private static Font Make(string family, float size) => new(family, size * TextScale);

    private static float ReadTextScale()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
        return key?.GetValue("TextScaleFactor") is int v && v is >= 100 and <= 225 ? v / 100f : 1f;
    }

    public static Color For(RunState s) => s switch
    {
        RunState.Success => Green,
        RunState.Failure => Red,
        RunState.Running => Blue,
        RunState.Queued => Yellow,
        _ => Gray,
    };

    /// <summary>Words for screen readers and tooltips, so state never depends on colour alone (1.4.1).</summary>
    public static string Words(RunState s) => s switch
    {
        RunState.Success => "Passed",
        RunState.Failure => "Failed",
        RunState.Running => "Running",
        RunState.Queued => "Queued",
        RunState.Cancelled => "Cancelled",
        _ => "Finished",
    };

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static string Age(DateTimeOffset t)
    {
        var s = (DateTimeOffset.Now - t).TotalSeconds;
        return s switch
        {
            < 60 => "now",
            < 3600 => $"{(int)(s / 60)}m ago",
            < 86400 => $"{(int)(s / 3600)}h ago",
            _ => $"{(int)(s / 86400)}d ago",
        };
    }

    public static string Elapsed(DateTimeOffset start)
    {
        var e = DateTimeOffset.Now - start;
        if (e < TimeSpan.Zero) e = TimeSpan.Zero;
        return e.TotalHours >= 1 ? $"{(int)e.TotalHours}:{e.Minutes:00}:{e.Seconds:00}" : $"{(int)e.TotalMinutes}:{e.Seconds:00}";
    }

    /// <summary>Spoken form of a duration, for screen readers.</summary>
    public static string ElapsedWords(DateTimeOffset start)
    {
        var e = DateTimeOffset.Now - start;
        if (e < TimeSpan.Zero) e = TimeSpan.Zero;
        return e.TotalHours >= 1 ? $"running for {(int)e.TotalHours} hours {e.Minutes} minutes"
            : $"running for {(int)e.TotalMinutes} minutes {e.Seconds} seconds";
    }
}
