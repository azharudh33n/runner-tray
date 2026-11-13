using System.Diagnostics;
using Axe.Windows.Automation;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Xunit;
using Xunit.Abstractions;

namespace RunnerTray.UiTests;

/// <summary>
/// Starts RunnerTray.exe with --test-ui (demo data, window opened at start) and checks it like a user
/// with a keyboard and a screen reader would, plus an Axe.Windows accessibility scan.
/// </summary>
public sealed class UiTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly UIA3Automation _automation = new();
    private Process? _process;

    public UiTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string ExePath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("RUNNERTRAY_EXE");
        if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;
        var dir = AppContext.BaseDirectory;
        var config = dir.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? "Debug" : "Release";
        var root = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", ".."));
        return Path.Combine(root, "src", "RunnerTray", "bin", config, "net8.0-windows", "RunnerTray.exe");
    }

    private Window Start(string window, string titleContains)
    {
        _process = Process.Start(new ProcessStartInfo(ExePath(), $"--test-ui {window}") { UseShellExecute = false })!;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var desktop = _automation.GetDesktop();
            var found = desktop.FindAllChildren(cf => cf.ByProcessId(_process.Id))
                .Select(e => e.AsWindow())
                .FirstOrDefault(w => w.Name.Contains(titleContains, StringComparison.Ordinal) && w.IsOffscreen == false);
            if (found is not null)
            {
                Thread.Sleep(800); // let the first layout finish
                return found;
            }

            Thread.Sleep(250);
        }

        throw new TimeoutException($"Window '{titleContains}' did not appear.");
    }

    [Fact]
    public void Panel_rows_have_names_with_status_in_words()
    {
        var panel = Start("panel", "RunnerTray");

        var rows = panel.FindAllDescendants(cf => cf.ByControlType(ControlType.Hyperlink).Or(cf.ByControlType(ControlType.Text)));
        var names = rows.Select(r => r.Name).Where(n => n.Contains(". ", StringComparison.Ordinal)).ToList();
        foreach (var n in names) _output.WriteLine(n);

        Assert.True(names.Count >= 10, $"Expected at least 10 rows, found {names.Count}.");
        Assert.Contains(names, n => n.StartsWith("Running", StringComparison.Ordinal));
        Assert.Contains(names, n => n.StartsWith("Failed. atlas-mobile", StringComparison.Ordinal));
        Assert.Contains(names, n => n.StartsWith("Passed", StringComparison.Ordinal));
        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.NotNull(panel.FindFirstDescendant(cf => cf.ByName("Refresh now").And(cf.ByControlType(ControlType.Button))));
    }

    [Fact]
    public void Keyboard_moves_focus_through_runs()
    {
        var panel = Start("panel", "RunnerTray");
        panel.Focus();
        Thread.Sleep(300);

        var first = _automation.FocusedElement();
        Keyboard.Press(VirtualKeyShort.DOWN);
        Thread.Sleep(300);
        var second = _automation.FocusedElement();

        _output.WriteLine($"{first.Name} -> {second.Name}");
        Assert.False(string.IsNullOrEmpty(second.Name));
        Assert.NotEqual(first.Name, second.Name);
    }

    [Theory]
    [InlineData("panel", "RunnerTray")]
    [InlineData("settings", "Settings - RunnerTray")]
    [InlineData("signin", "Sign in with GitHub")]
    [InlineData("diagnostics", "Diagnostics - RunnerTray")]
    public void Window_has_no_accessibility_errors(string window, string title)
    {
        Start(window, title);

        var scanner = ScannerFactory.CreateScanner(Config.Builder.ForProcessId(_process!.Id).Build());
        var output = scanner.Scan(null);
        var errors = output.WindowScanOutputs.SelectMany(w => w.Errors).ToList();
        foreach (var e in errors)
        {
            _output.WriteLine($"{e.Rule.ID}: {e.Rule.Description} | element: {e.Element.Properties?.GetValueOrDefault("Name")} ({e.Element.Properties?.GetValueOrDefault("ControlType")})");
        }

        Assert.Empty(errors);
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }

        _process?.Dispose();
        _automation.Dispose();
    }
}
