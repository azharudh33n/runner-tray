using System.Runtime.InteropServices;

namespace RunnerTray;

/// <summary>
/// Holds one global shortcut (RegisterHotKey) on a hidden message-only window and raises Pressed on the
/// UI thread when the user presses it. Create it on the UI thread.
/// </summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr MessageOnlyParent = new(-3); // HWND_MESSAGE

    private bool _registered;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams { Parent = MessageOnlyParent });
    }

    public event EventHandler? Pressed;

    /// <summary>Registers the shortcut in place of the previous one. False when Windows refuses it, usually because another app uses it.</summary>
    public bool Register(GlobalHotkey hotkey)
    {
        Unregister();
        _registered = RegisterHotKey(Handle, HotkeyId, (uint)hotkey.Modifiers | ModNoRepeat, (uint)hotkey.VirtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            _ = UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
