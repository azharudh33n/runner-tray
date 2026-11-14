namespace RunnerTray;

/// <summary>
/// `RunnerTray.exe --selftest`: creates every window once with demo data, draws it, and exits.
/// Exit code 0 means the published app starts and renders; used by the release workflow.
/// </summary>
internal static class SelfTest
{
    public static int Run()
    {
        try
        {
            var demo = new DemoSource();
            var snapshot = demo.FetchAsync(CancellationToken.None).GetAwaiter().GetResult();
            var local = demo.FetchLocalAsync(CancellationToken.None).GetAwaiter().GetResult();

            using (var popup = new PopupForm { AutoHide = false, Opacity = 0, Location = new Point(-4000, -4000) })
            {
                popup.Show();
                popup.ShowSnapshot(snapshot);
                popup.ShowLocal(local);
                popup.ShowSnapshot(snapshot with { Failure = FailureKind.SignInRequired });
                popup.ShowSnapshot(snapshot with { Failure = FailureKind.RateLimited, RetryAt = DateTimeOffset.Now.AddMinutes(5) });
                Render(popup);
            }

            using (var settings = new SettingsForm(new AppConfig(), "Self-test", false, false)) Render(settings);
            using (var diagnostics = new DiagnosticsForm(() => "self-test")) Render(diagnostics);
            using (var uninstall = new UninstallForm(installed: true)) Render(uninstall);
            using (var signIn = new SignInForm(_ => Task.FromException<DeviceCode>(new OperationCanceledException()), (_, _) => Task.FromException<TokenSet>(new OperationCanceledException())))
            {
                signIn.CreateControl();
            }

            foreach (var state in Enum.GetValues<TrayState>()) _ = Icons.Get(state);
            Log.Info("Self-test passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Self-test failed", ex);
            return 1;
        }
    }

    private static void Render(Form form)
    {
        form.CreateControl();
        form.PerformLayout();
        using var bmp = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
        form.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
    }
}
