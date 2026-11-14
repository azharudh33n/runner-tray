using System.Diagnostics;
using Microsoft.Win32;

namespace RunnerTray;

/// <summary>
/// Install location, Start with Windows, and removing RunnerTray completely.
///
/// Installed (Setup.exe, Velopack): %LOCALAPPDATA%\RunnerTray\
///   RunnerTray.exe   stable launcher (survives updates)  ← used for Start with Windows
///   Update.exe       installer, updater and uninstaller
///   current\         the running version
/// Portable: wherever the user extracted the zip.
///
/// Outside the install folder RunnerTray creates: the sign-in in Windows Credential Manager,
/// the Start with Windows registry value, %LOCALAPPDATA%\RunnerTray-data (cache, logs, last data)
/// and %APPDATA%\RunnerTray (settings). <see cref="CleanUp"/> removes them.
/// </summary>
internal static class Uninstall
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValue = "RunnerTray";

    /// <summary>Folder with Update.exe when installed with Setup.exe, otherwise null.</summary>
    public static string? InstallRoot
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            var root = current.Parent;
            return current.Name.Equals("current", StringComparison.OrdinalIgnoreCase)
                   && root is not null
                   && File.Exists(Path.Combine(root.FullName, "Update.exe"))
                ? root.FullName
                : null;
        }
    }

    public static bool Installed => InstallRoot is not null;

    /// <summary>The program Start with Windows should run: the stable launcher when installed.</summary>
    public static string LaunchPath =>
        InstallRoot is { } root && File.Exists(Path.Combine(root, "RunnerTray.exe"))
            ? Path.Combine(root, "RunnerTray.exe")
            : Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool StartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    public static void SetStartup(bool enabled)
    {
        try
        {
            if (enabled)
            {
                // The Run key can be missing on new or cleaned-up Windows profiles: create it.
                using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                key.SetValue(RunValue, $"\"{LaunchPath}\"");
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                key?.DeleteValue(RunValue, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warn($"Could not change Start with Windows: {ex.Message}");
        }
    }

    /// <summary>If Start with Windows points somewhere else (old copy, moved folder), point it at this copy.</summary>
    public static void RepairStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        if (key?.GetValue(RunValue) is string value && !value.Contains(LaunchPath, StringComparison.OrdinalIgnoreCase))
        {
            Log.Info($"Start with Windows pointed at {value}; updating it to this copy.");
            SetStartup(true);
        }
    }

    /// <summary>
    /// Removes everything RunnerTray created outside its install folder. Never throws.
    /// Called by the uninstaller (Settings > Apps) with keepSettings = true, and by the tray menu.
    /// </summary>
    public static void CleanUp(bool keepSettings)
    {
        Try(() =>
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            run?.DeleteValue(RunValue, throwOnMissingValue: false);
        });
        Try(() => new CredentialManagerStore().Delete("github"));
        Try(() =>
        {
            if (Directory.Exists(DataFolder.Path)) Directory.Delete(DataFolder.Path, recursive: true);
        });
        if (!keepSettings)
        {
            Try(() =>
            {
                if (Directory.Exists(AppConfig.Dir)) Directory.Delete(AppConfig.Dir, recursive: true);
            });
        }
    }

    /// <summary>Asks, cleans up, and starts the uninstaller (installed) or explains the last step (portable).</summary>
    public static void Run()
    {
        using var dialog = new UninstallForm(Installed);
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        Log.Info($"Uninstall requested from the menu (installed: {Installed}, keep settings: {dialog.KeepSettings}).");
        Log.Enabled = false; // the log folder is about to be deleted
        CleanUp(dialog.KeepSettings);

        if (InstallRoot is { } root)
        {
            // Update.exe stops RunnerTray, removes the app, its shortcuts and its Settings > Apps entry.
            Process.Start(new ProcessStartInfo(Path.Combine(root, "Update.exe"), "--uninstall --silent") { UseShellExecute = false })?.Dispose();
        }
        else
        {
            var folder = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            MessageBox.Show(
                $"RunnerTray removed its sign-in, startup entry and data{(dialog.KeepSettings ? "" : ", and your settings")}.\n\n" +
                $"To finish, delete this folder after RunnerTray closes:\n{folder}",
                "RunnerTray removed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose();
        }

        Application.Exit();
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            // Clean-up continues with the next item; leftovers are harmless.
            Log.Warn($"Clean-up step failed: {ex.Message}");
        }
    }
}

/// <summary>Confirms uninstalling, with a choice to keep settings. Standard controls: keyboard and screen reader friendly.</summary>
internal sealed class UninstallForm : Form
{
    private readonly CheckBox _keep = new() { Text = "&Keep my settings (for a later reinstall)", AutoSize = true, Checked = true };

    public UninstallForm(bool installed)
    {
        Text = installed ? "Uninstall RunnerTray" : "Remove RunnerTray";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 10f * Theme.TextScale);
        Padding = new Padding(20);
        Icon = AppIcon.Value;

        var text = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(Theme.Scaled(440), 0),
            Text = (installed
                       ? "RunnerTray will close and be removed from this PC."
                       : "RunnerTray will close and remove its data from this PC. You then delete its folder.")
                   + "\n\nThis also removes:\n• your RunnerTray sign-in (saved in Windows Credential Manager)\n• Start with Windows\n• the cache and logs",
        };
        var revoke = new LinkLabel { AutoSize = true, Text = "To also remove RunnerTray's access on GitHub, revoke it in GitHub settings.", MaximumSize = new Size(Theme.Scaled(440), 0), Margin = new Padding(0, 10, 0, 6) };
        revoke.LinkArea = new LinkArea(revoke.Text.IndexOf("revoke", StringComparison.Ordinal), "revoke it in GitHub settings".Length);
        revoke.LinkClicked += (_, _) => Proc.Open("https://github.com/settings/apps/authorizations");
        _keep.Margin = new Padding(0, 8, 0, 8);

        var ok = new Button { Text = installed ? "&Uninstall" : "&Remove", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        AcceptButton = ok;
        CancelButton = cancel;

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.AddRange(new Control[] { text, revoke, _keep, buttons });
        Controls.Add(layout);
        Shown += (_, _) => cancel.Focus(); // safe default for a destructive action
    }

    public bool KeepSettings => _keep.Checked;
}
