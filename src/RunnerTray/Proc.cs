using System.Diagnostics;

namespace RunnerTray;

/// <summary>Runs command-line tools without showing a console window.</summary>
internal static class Proc
{
    public static async Task<(int ExitCode, string Output)> RunAsync(
        string file, IEnumerable<string> args, int timeoutMs, CancellationToken ct)
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
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, ""); // tool not installed
        }

        if (p is null)
        {
            return (-1, "");
        }

        using (p)
        {
            var stdout = p.StandardOutput.ReadToEndAsync(ct);
            var stderr = p.StandardError.ReadToEndAsync(ct);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            try
            {
                await p.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch (InvalidOperationException) { }
                return (-1, "");
            }

            return (p.ExitCode, await stdout + await stderr);
        }
    }

    public static void Open(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
