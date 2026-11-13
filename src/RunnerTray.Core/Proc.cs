using System.ComponentModel;
using System.Diagnostics;

namespace RunnerTray;

/// <summary>Runs command-line tools without showing a console window.</summary>
public static class Proc
{
    public static async Task<(int ExitCode, string Output)> RunAsync(
        string file, IEnumerable<string> args, int timeoutMs, CancellationToken ct, bool includeStdErr = true)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        Process? p;
        try
        {
            p = Process.Start(psi);
        }
        catch (Win32Exception)
        {
            return (-1, ""); // tool not installed
        }

        if (p is null)
        {
            return (-1, "");
        }

        using (p)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            var stdout = p.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = p.StandardError.ReadToEndAsync(cts.Token);
            try
            {
                await p.WaitForExitAsync(cts.Token);
                var output = await stdout;
                var errors = await stderr;
                return (p.ExitCode, includeStdErr ? output + errors : output);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    p.Kill(true);
                }
                catch (InvalidOperationException)
                {
                    // Already exited.
                }

                return (-1, "");
            }
        }
    }

    /// <summary>Opens a URL or file with its default program.</summary>
    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            Log.Warn($"Could not open {target}: {ex.Message}");
        }
    }
}
