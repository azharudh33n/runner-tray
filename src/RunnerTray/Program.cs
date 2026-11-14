using Velopack;

namespace RunnerTray;

/// <summary>
/// Command line:
///   (none)                      normal start
///   --demo                      made-up data, no GitHub account needed
///   --screenshot DIR [--live]   render README images and exit
///   --selftest                  create every window once and exit (exit code 0 = ok)
///   --test-ui [settings|signin] for UI tests: open a window at start and keep the panel open
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Velopack handles install, update and uninstall hooks first, then returns.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => Uninstall.CleanUp(keepSettings: true))
            .Run();

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Problems.Report("Unexpected error", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();

        var shot = Array.IndexOf(args, "--screenshot");
        if (shot >= 0)
        {
            var folder = shot + 1 < args.Length && !args[shot + 1].StartsWith("--", StringComparison.Ordinal) ? args[shot + 1] : ".";
            Screenshots.RenderAll(folder, live: args.Contains("--live"));
            return 0;
        }

        if (args.Contains("--selftest"))
        {
            return SelfTest.Run();
        }

        var testUi = Array.IndexOf(args, "--test-ui");
        var options = new StartOptions(
            Demo: args.Contains("--demo") || testUi >= 0,
            TestUi: testUi >= 0 ? (testUi + 1 < args.Length && !args[testUi + 1].StartsWith("--", StringComparison.Ordinal) ? args[testUi + 1] : "panel") : null);

        using var mutex = new Mutex(true, SingleInstance.MutexName, out var firstInstance);
        if (!firstInstance && options.TestUi is null)
        {
            SingleInstance.SignalFirst(); // the running copy opens its panel
            return 0;
        }

        Log.Info($"RunnerTray {AppVersion.Text} starting (demo: {options.Demo}).");
        Application.Run(new TrayApp(options));
        Log.Info("RunnerTray stopped.");
        return 0;
    }
}

internal sealed record StartOptions(bool Demo, string? TestUi);

internal static class AppVersion
{
    public static string Text => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}

/// <summary>Reports unexpected errors: logged, and shown once as a notification without crashing.</summary>
internal static class Problems
{
    public static event EventHandler<string>? Raised;

    public static void Report(string what, Exception ex)
    {
        Log.Error(what, ex);
        Raised?.Invoke(null, $"{what}. Details were saved in the log.");
    }
}

/// <summary>Starting RunnerTray again opens the panel of the running copy.</summary>
internal static class SingleInstance
{
    public const string MutexName = "RunnerTray.SingleInstance";
    private const string EventName = "RunnerTray.ShowPanel";

    public static void SignalFirst()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(EventName);
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The first copy is still starting; nothing to do.
        }
    }

    /// <summary>Calls <paramref name="onSignal"/> on the thread pool each time another copy starts.</summary>
    public static IDisposable Listen(Action onSignal)
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        var registration = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => onSignal(), null, Timeout.Infinite, executeOnlyOnce: false);
        return new Listener(signal, registration);
    }

    private sealed class Listener(EventWaitHandle signal, RegisteredWaitHandle registration) : IDisposable
    {
        public void Dispose()
        {
            registration.Unregister(null);
            signal.Dispose();
        }
    }
}
