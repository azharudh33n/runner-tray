using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RunnerTray;

/// <summary>
/// Renders images of the popup:
///   RunnerTray.exe --screenshot docs          README images from made-up demo data
///   RunnerTray.exe --screenshot out --live    popup.png from your real data (to check your settings)
/// </summary>
internal static class Screenshots
{
    public static void RenderAll(string folder, bool live = false)
    {
        Directory.CreateDirectory(folder);
        using var services = live ? Services.Create(AppConfig.Load()) : null;
        IDataSource source = services is not null ? services.Source : new DemoSource();
        var snapshot = Task.Run(() => source.FetchAsync(CancellationToken.None)).GetAwaiter().GetResult();
        var local = Task.Run(() => source.FetchLocalAsync(CancellationToken.None)).GetAwaiter().GetResult();

        using var popup = new PopupForm { AutoHide = false, Opacity = 0, Location = new Point(-4000, -4000) };
        popup.Show();
        popup.ShowSnapshot(snapshot);
        popup.ShowLocal(local);
        popup.PerformLayout();
        Application.DoEvents();

        using var panel = new Bitmap(popup.Width, popup.Height);
        popup.DrawToBitmap(panel, new Rectangle(Point.Empty, popup.Size));
        using var rounded = RoundCorners(panel, 10);
        rounded.Save(Path.Combine(folder, "popup.png"), ImageFormat.Png);
        popup.Close();
        if (live)
        {
            return;
        }

        using var hero = Hero(panel);
        hero.Save(Path.Combine(folder, "hero.png"), ImageFormat.Png);

        using (var social = Social(panel, Path.Combine(folder, "..", "assets", "logo-1024.png")))
        {
            social.Save(Path.Combine(folder, "social.png"), ImageFormat.Png);
        }

        using var states = States();
        states.Save(Path.Combine(folder, "tray-states.png"), ImageFormat.Png);

        using var toast = Toast();
        toast.Save(Path.Combine(folder, "notification.png"), ImageFormat.Png);

        using (var signIn = new SignInForm(
            _ => Task.FromResult(new DeviceCode("demo", "WDJB-MJHT", "https://github.com/login/device", TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(5))),
            (_, ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith<TokenSet>(_ => throw new OperationCanceledException(), TaskScheduler.Default)))
        {
            SaveWindow(signIn, Path.Combine(folder, "sign-in.png"));
        }

        using (var settings = new SettingsForm(new AppConfig { Owner = "northwind-labs" }, "Signed in with GitHub, watching northwind-labs.", true, true))
        {
            SaveWindow(settings, Path.Combine(folder, "settings.png"));
        }
    }

    /// <summary>The popup above a mock Windows desktop and taskbar, with a notification.</summary>
    private static Bitmap Hero(Bitmap panel)
    {
        const int w = 1280, h = 800, taskbar = 48;
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        // Desktop wallpaper: deep blue gradient with two soft glows.
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(18, 28, 58), Color.FromArgb(9, 12, 22), 55f))
        {
            g.FillRectangle(bg, 0, 0, w, h);
        }

        Glow(g, new RectangleF(-200, -260, 900, 700), Color.FromArgb(46, 88, 166, 255));
        Glow(g, new RectangleF(520, 300, 700, 600), Color.FromArgb(30, 63, 185, 80));

        // Taskbar with tray icon and clock.
        using (var tb = new SolidBrush(Color.FromArgb(240, 22, 25, 31))) g.FillRectangle(tb, 0, h - taskbar, w, taskbar);
        using var clockFont = new Font("Segoe UI", 9f);
        TextRenderer.DrawText(g, "12:45\n13-11-2026", clockFont, new Rectangle(w - 104, h - taskbar, 96, taskbar), Color.White,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        using (var tray = Icons.Draw(TrayState.Busy, 22)) g.DrawImage(tray, w - 140, h - taskbar + 13);
        using (var hl = new Pen(Color.FromArgb(120, 255, 255, 255))) g.DrawLine(hl, w - 146, h - 6, w - 112, h - 6);

        // Popup with a soft shadow, right above the tray icon.
        var x = w - panel.Width - 24;
        var y = h - taskbar - panel.Height - 12;
        Shadow(g, new RectangleF(x, y, panel.Width, panel.Height), 10);
        using (var clip = Theme.Rounded(new RectangleF(x, y, panel.Width, panel.Height), 10))
        {
            g.SetClip(clip);
            g.DrawImage(panel, x, y);
            g.ResetClip();
            using var border = new Pen(Theme.Border);
            g.DrawPath(border, clip);
        }

        // Notification card, left of the popup.
        using (var toast = Toast())
        {
            var tx = x - toast.Width - 28;
            var ty = h - taskbar - toast.Height - 24;
            Shadow(g, new RectangleF(tx, ty, toast.Width, toast.Height), 8);
            g.DrawImage(toast, tx, ty);
        }

        // Title and feature list.
        using var big = new Font("Segoe UI Semibold", 40f);
        using var mid = new Font("Segoe UI", 15f);
        using var small = new Font("Segoe UI", 11.5f);
        TextRenderer.DrawText(g, "RunnerTray", big, new Point(64, 70), Color.White);
        TextRenderer.DrawText(g, "GitHub Actions in your Windows tray", mid, new Point(70, 148), Color.FromArgb(201, 209, 217));
        var features = new (Glyph Glyph, Color Color, string Text)[]
        {
            (Glyph.Play, Theme.Blue, "Live jobs with timers"),
            (Glyph.Cross, Theme.Red, "Failure notifications"),
            (Glyph.Check, Theme.Green, "Self-hosted runner status"),
            (Glyph.Dots, Theme.Yellow, "Keyboard and screen reader ready"),
        };
        for (var i = 0; i < features.Length; i++)
        {
            var fy = 214 + i * 40;
            Badge.Draw(g, new RectangleF(72, fy + 3, 20, 20), features[i].Color, features[i].Glyph);
            TextRenderer.DrawText(g, features[i].Text, small, new Point(104, fy), Color.FromArgb(230, 237, 243));
        }

        return bmp;
    }

    /// <summary>A Windows-style notification for a failed run.</summary>
    private static Bitmap Toast()
    {
        const int w = 360, h = 104;
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using (var card = Theme.Rounded(new RectangleF(0, 0, w - 1, h - 1), 8))
        using (var fill = new SolidBrush(Color.FromArgb(32, 32, 36)))
        using (var edge = new Pen(Color.FromArgb(60, 60, 66)))
        {
            g.FillPath(fill, card);
            g.DrawPath(edge, card);
        }

        using var appFont = new Font("Segoe UI", 8.5f);
        using var titleFont = new Font("Segoe UI Semibold", 10.5f);
        using var bodyFont = new Font("Segoe UI", 9.5f);
        using (var icon = Icons.Draw(TrayState.Busy, 16)) g.DrawImage(icon, 14, 12);
        TextRenderer.DrawText(g, "RunnerTray", appFont, new Point(36, 11), Color.FromArgb(200, 200, 205));
        Badge.Draw(g, new RectangleF(16, 44, 34, 34), Theme.Red, Glyph.Cross);
        TextRenderer.DrawText(g, "atlas-mobile failed", titleFont, new Point(62, 40), Color.White);
        TextRenderer.DrawText(g, "Android release on release/3.2 (jlee)", bodyFont, new Point(62, 64), Color.FromArgb(210, 210, 215));
        return bmp;
    }

    /// <summary>The five tray icon states with captions.</summary>
    private static Bitmap States()
    {
        var items = new[] { TrayState.Idle, TrayState.Busy, TrayState.Failing, TrayState.Error, TrayState.Unknown };
        const int cell = 170;
        var bmp = new Bitmap(cell * items.Length, 104);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Bg);
        for (var i = 0; i < items.Length; i++)
        {
            using var icon = Icons.Draw(items[i], 40);
            g.DrawImage(icon, i * cell + (cell - 40) / 2, 14);
            TextRenderer.DrawText(g, Icons.Words(items[i]), Theme.Body, new Rectangle(i * cell, 64, cell, 26), Theme.Fg,
                TextFormatFlags.HorizontalCenter);
        }

        return bmp;
    }

    private static Bitmap RoundCorners(Bitmap src, int radius)
    {
        var bmp = new Bitmap(src.Width, src.Height);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var path = Theme.Rounded(new RectangleF(0, 0, src.Width - 1, src.Height - 1), radius);
        using var brush = new TextureBrush(src);
        g.FillPath(brush, path);
        using var border = new Pen(Theme.Border);
        g.DrawPath(border, path);
        return bmp;
    }

    private static void Glow(Graphics g, RectangleF r, Color c)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(r);
        using var brush = new PathGradientBrush(path) { CenterColor = c, SurroundColors = new[] { Color.Transparent } };
        g.FillEllipse(brush, r);
    }

    private static void Shadow(Graphics g, RectangleF r, float radius)
    {
        for (var i = 14; i > 0; i--)
        {
            using var shadow = new SolidBrush(Color.FromArgb(7, 0, 0, 0));
            using var path = Theme.Rounded(new RectangleF(r.X - i, r.Y - i + 8, r.Width + 2 * i, r.Height + 2 * i), radius + i);
            g.FillPath(shadow, path);
        }
    }

    /// <summary>Shows a window off screen, lets it finish loading, and saves an image of it.</summary>
    private static void SaveWindow(Form form, string path)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-4000, -4000);
        form.Show();
        for (var i = 0; i < 20; i++)
        {
            Application.DoEvents();
            Thread.Sleep(25);
        }

        using var bmp = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
        bmp.Save(path, ImageFormat.Png);
        form.Close();
    }

    /// <summary>1280 x 640 image for link previews (GitHub, Google, social sites).</summary>
    private static Bitmap Social(Bitmap panel, string logoPath)
    {
        const int w = 1280, h = 640;
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(16, 26, 54), Color.FromArgb(9, 12, 22), 30f))
        {
            g.FillRectangle(bg, 0, 0, w, h);
        }

        Glow(g, new RectangleF(600, -300, 900, 800), Color.FromArgb(60, 31, 111, 235));
        Glow(g, new RectangleF(-200, 300, 700, 600), Color.FromArgb(28, 63, 185, 80));

        if (File.Exists(logoPath))
        {
            using var logo = Image.FromFile(logoPath);
            g.DrawImage(logo, 72, 88, 128, 128);
        }

        using var big = new Font("Segoe UI Semibold", 52f);
        using var mid = new Font("Segoe UI", 22f);
        using var small = new Font("Segoe UI", 15f);
        TextRenderer.DrawText(g, "RunnerTray", big, new Point(64, 236), Color.White);
        TextRenderer.DrawText(g, "GitHub Actions status in your\nWindows system tray", mid,
            new Rectangle(70, 338, 620, 130), Color.FromArgb(201, 209, 217), TextFormatFlags.WordBreak);
        TextRenderer.DrawText(g, "Free · Open source · Windows 10 and 11", small, new Point(72, 470), Color.FromArgb(157, 167, 179));
        TextRenderer.DrawText(g, "github.com/azharudh33n/runner-tray", small, new Point(72, 548), Color.FromArgb(121, 192, 255));

        // Panel on the right, slightly cut off at the bottom, with a shadow.
        var scale = 560f / panel.Height;
        var pw = (int)(panel.Width * scale);
        var rect = new RectangleF(w - pw - 80, 70, pw, 560);
        Shadow(g, rect, 12);
        using (var clip = Theme.Rounded(rect, 12))
        {
            g.SetClip(clip);
            g.DrawImage(panel, rect);
            g.ResetClip();
            using var border = new Pen(Theme.Border);
            g.DrawPath(border, clip);
        }

        return bmp;
    }
}