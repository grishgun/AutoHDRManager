using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AutoHDRManager.Windows;

public sealed record IdentityProperty(string Status, string? Value, string NativeResult, ushort? VariantType = null);
public sealed record WindowIdentityInfo(string Hwnd, int Pid, string? ExecutablePath, long? ProcessCreationTime,
    string Title, string ClassName, bool Visible, string OwnerHwnd, int? Cloaked,
    IdentityProperty WindowAppUserModelId, IdentityProperty ProcessApplicationIdentity);

/// <summary>Read-only, documented APIs. No process memory access, injection, window property writes or registry changes.</summary>
public static class WindowIdentityDiagnostics
{
    public const string ProcessShellIdLimitation = "GetCurrentProcessExplicitAppUserModelID reads only the calling process. An empty window property and absent AppModel identity do not prove that another process has no explicit Shell AppUserModelID. System-assigned IDs cannot be retrieved through the documented Shell API.";
    public static IReadOnlyList<WindowIdentityInfo> Capture(int? processId = null)
    {
        if (processId is <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        var windows = new List<WindowIdentityInfo>();
        var processes = new Dictionary<int, (string? Path, long? Created, IdentityProperty Identity)>();
        EnumWindowsCallback callback = (hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint rawPid);
            int pid = unchecked((int)rawPid);
            if (processId.HasValue && processId != pid) return true;
            // General picker only lists visible titled windows; a PID probe includes hidden top-level windows too.
            bool visible = IsWindowVisible(hwnd);
            var title = new StringBuilder(1024); GetWindowText(hwnd, title, title.Capacity);
            if (!processId.HasValue && (!visible || title.Length == 0)) return true;
            if (!processes.TryGetValue(pid, out var process)) processes[pid] = process = ReadProcess(pid);
            var className = new StringBuilder(256); GetClassName(hwnd, className, className.Capacity);
            int cloakedResult = DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int));
            var property = ReadWindowProperty(hwnd);
            // A window may disappear/recycle while enumerating. Do not attribute another process's property to this PID.
            GetWindowThreadProcessId(hwnd, out uint finalPid);
            if (finalPid != rawPid) return true;
            windows.Add(new(Hex(hwnd), pid, process.Path, process.Created, title.ToString(), className.ToString(), visible,
                Hex(GetWindow(hwnd, 4)), cloakedResult >= 0 ? cloaked : null, property, process.Identity));
            return true;
        };
        if (!EnumWindows(callback, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        GC.KeepAlive(callback);
        return windows;
    }

    public static IdentityProperty ReadWindowProperty(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd)) return new("Unavailable", null, "Invalid HWND");
        Guid iid = typeof(IPropertyStoreReadOnly).GUID;
        int hr = SHGetPropertyStoreForWindow(hwnd, ref iid, out var store);
        if (hr < 0 || store == null) return new("Unavailable", null, HResult(hr));
        try
        {
            var key = new PropertyKey { Format = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
            hr = store.GetValue(ref key, out var value);
            try
            {
                if (hr < 0) return new("Unavailable", null, HResult(hr));
                if (value.Type == 0) return new("Absent", null, HResult(hr), value.Type);
                if (value.Type != 31) return new("UnexpectedType", null, HResult(hr), value.Type);
                string? id = Marshal.PtrToStringUni(value.Pointer);
                return new(string.IsNullOrEmpty(id) ? "Absent" : "Present", id, HResult(hr), value.Type);
            }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.FinalReleaseComObject(store); }
    }

    private static (string? Path, long? Created, IdentityProperty Identity) ReadProcess(int pid)
    {
        using var handle = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION only
        if (handle.IsInvalid) return (null, null, new("Unavailable", null, "Win32=" + Marshal.GetLastWin32Error()));
        var path = new StringBuilder(32768); uint length = (uint)path.Capacity;
        string? executable = QueryFullProcessImageName(handle, 0, path, ref length) ? path.ToString() : null;
        long? created = GetProcessTimes(handle, out long creation, out _, out _, out _) ? creation : null;
        uint count = 0; int result = GetApplicationUserModelId(handle, ref count, null);
        if (result == 15703) return (executable, created, new("NoApplicationIdentity", null, "Win32=15703"));
        if (result != 122 || count == 0 || count > 65536) return (executable, created, new("Unavailable", null, "Win32=" + result));
        var id = new StringBuilder((int)count); result = GetApplicationUserModelId(handle, ref count, id);
        return (executable, created, result == 0 ? new("Present", id.ToString(), "Win32=0") : new("Unavailable", null, "Win32=" + result));
    }
    private static string Hex(nint value) => "0x" + unchecked((nuint)value).ToString("X");
    private static string HResult(int value) => "0x" + unchecked((uint)value).ToString("X8");
    private delegate bool EnumWindowsCallback(nint hwnd, nint parameter);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")] private static extern int GetWindowText(nint hwnd, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] private static extern int GetClassName(nint hwnd, StringBuilder text, int capacity);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(nint hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStoreReadOnly? store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(SafeProcessHandle process, ref uint length, StringBuilder? id);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Pointer; }
    // Only the first three COM slots are exposed. This probe cannot call SetValue or Commit.
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStoreReadOnly
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
}
