namespace RunnerTray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // RunnerTray.exe --screenshot <folder>: render README images from demo data and exit.
        var shot = Array.IndexOf(args, "--screenshot");
        if (shot >= 0)
        {
            var folder = shot + 1 < args.Length && !args[shot + 1].StartsWith("--") ? args[shot + 1] : ".";
            Screenshots.RenderAll(folder, live: args.Contains("--live"));
            return 0;
        }

        using var mutex = new Mutex(true, "RunnerTray.SingleInstance", out var firstInstance);
        if (!firstInstance)
        {
            return 0;
        }

        var demo = args.Contains("--demo");
        var config = AppConfig.Load();
        IDataSource source = demo ? new DemoSource() : new GitHubSource(config);
        Application.Run(new TrayApp(config, source, demo));
        return 0;
    }
}
