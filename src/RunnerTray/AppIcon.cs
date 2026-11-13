namespace RunnerTray;

/// <summary>The RunnerTray logo (from the exe), for window title bars and the taskbar.</summary>
internal static class AppIcon
{
    private static readonly Lazy<Icon> Lazy = new(() =>
        (Environment.ProcessPath is { } exe ? Icon.ExtractAssociatedIcon(exe) : null) ?? Icons.Get(TrayState.Busy));

    public static Icon Value => Lazy.Value;
}