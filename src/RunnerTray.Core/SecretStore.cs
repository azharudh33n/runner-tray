using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace RunnerTray;

/// <summary>Where the sign-in tokens are kept.</summary>
public interface ISecretStore
{
    string? Read(string key);

    void Write(string key, string value);

    void Delete(string key);
}

/// <summary>For tests.</summary>
public sealed class MemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = new();

    public string? Read(string key) => _values.TryGetValue(key, out var v) ? v : null;

    public void Write(string key, string value) => _values[key] = value;

    public void Delete(string key) => _values.Remove(key);
}

/// <summary>
/// Windows Credential Manager (generic credentials). Windows encrypts them for the current user (DPAPI).
/// Visible in Control Panel > Credential Manager > Windows Credentials as "RunnerTray:…".
/// </summary>
public sealed class CredentialManagerStore : ISecretStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string? Read(string key)
    {
        if (!CredReadW(Target(key), CredTypeGeneric, 0, out var ptr))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound) Log.Warn($"Credential Manager read failed: {new Win32Exception(error).Message}");
            return null;
        }

        try
        {
            var cred = Marshal.PtrToStructure<Credential>(ptr);
            return cred.CredentialBlobSize == 0 ? null : Marshal.PtrToStringUni(cred.CredentialBlob, (int)cred.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public void Write(string key, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var cred = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = Target(key),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = "RunnerTray",
            };
            if (!CredWriteW(ref cred, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not save the sign-in to Windows Credential Manager.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    public void Delete(string key)
    {
        if (!CredDeleteW(Target(key), CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound) Log.Warn($"Credential Manager delete failed: {new Win32Exception(error).Message}");
        }
    }

    private static string Target(string key) => "RunnerTray:" + key;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CredReadW(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CredWriteW(ref Credential credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CredDeleteW(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void CredFree(IntPtr buffer);
}
