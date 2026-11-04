using L = AutoHDRManager.Core.Localization;
using System.ComponentModel;
using System.Runtime.InteropServices;
using AutoHDRManager.Core;

namespace AutoHDRManager.Windows;

public sealed class TrayService : ITrayService, INotificationService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyData
    {
        public uint Size; public nint Window; public uint Id, Flags, Message; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private delegate nint Subclass(nint window, uint message, nuint wparam, nint lparam, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint window, Subclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint window, Subclass callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wparam, nint lparam);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIconW(uint message, ref NotifyData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] private static extern nint LoadIconW(nint instance, nint name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconExW(string file, int index, out nint large, out nint small, uint count);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenuW(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
    public static void RestoreWindow(nint window) { ShowWindow(window, IsIconic(window) ? 9 : 5); SetForegroundWindow(window); }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessageW(nint window, uint message, nuint wparam, nint lparam);
    private readonly nint window;
    private readonly Subclass callback;
    private readonly uint taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    private NotifyData data;
    private string hdr = L.Get("Недоступен");
    private bool monitoring = true;
    private bool disposed;
    private nint ownedIcon;
    public event Action? OpenRequested;
    public event Action? PauseRequested;
    public event Action? ExitRequested;
    public TrayService(nint window)
    {
        this.window = window; callback = WindowProc;
        data = new NotifyData { Size = (uint)Marshal.SizeOf<NotifyData>(), Window = window, Id = 1, Message = 0x8001, Icon = LoadIconW(0, 32512), Tip = "Auto HDR Manager", Info = "", Title = "" };
        if (Environment.ProcessPath is string exe && ExtractIconExW(exe, 0, out var large, out var small, 1) > 0)
        {
            ownedIcon = small != 0 ? small : large; data.Icon = ownedIcon;
            if (large != 0 && large != ownedIcon) DestroyIcon(large);
        }
        if (!SetWindowSubclass(window, callback, 1, 0)) throw new Win32Exception("Не удалось создать обработчик трея.");
        Add();
    }
    private void Add()
    {
        data.Flags = 1 | 2 | 4 | 128;
        Shell_NotifyIconW(0, ref data); data.Version = 4; Shell_NotifyIconW(4, ref data);
    }
    public void Update(string hdr, bool monitoring)
    {
        if (disposed || (this.hdr == hdr && this.monitoring == monitoring)) return;
        this.hdr = hdr; this.monitoring = monitoring;
        data.Tip = L.Format("TrayTip", hdr, L.Get(monitoring ? "Активен" : "Приостановлен")); data.Flags = 4;
        Shell_NotifyIconW(1, ref data);
    }
    public void Show(string title, string message)
    {
        if (disposed) return;
        title = L.Message(title); message = L.Message(message);
        data.Title = title[..Math.Min(title.Length, 63)]; data.Info = message[..Math.Min(message.Length, 255)]; data.InfoFlags = 1; data.Flags = 16 | 64;
        Shell_NotifyIconW(1, ref data);
    }
    private nint WindowProc(nint hwnd, uint message, nuint wparam, nint lparam, nuint id, nuint reference)
    {
        if (message == taskbarCreated) Add();
        else if (message == 0x8001)
        {
            uint action = (uint)((long)lparam & 0xffff);
            if (action is 0x400 or 0x401 or 0x203) OpenRequested?.Invoke();
            if (action == 0x7b)
            {
                var menu = CreatePopupMenu();
                try
                {
                    AppendMenuW(menu, 0, 1, L.Get("Открыть Auto HDR Manager"));
                    AppendMenuW(menu, 2, 0, "HDR: " + hdr);
                    AppendMenuW(menu, 0, 2, monitoring ? L.Get("Приостановить мониторинг") : L.Get("Возобновить мониторинг"));
                    AppendMenuW(menu, 0x800, 0, null); AppendMenuW(menu, 0, 3, L.Get("Выход"));
                    GetCursorPos(out var point); SetForegroundWindow(window);
                    uint selected = TrackPopupMenu(menu, 0x100 | 2, point.X, point.Y, 0, window, 0);
                    PostMessageW(window, 0, 0, 0);
                    if (selected == 1) OpenRequested?.Invoke(); else if (selected == 2) PauseRequested?.Invoke(); else if (selected == 3) ExitRequested?.Invoke();
                }
                finally { DestroyMenu(menu); }
            }
        }
        return DefSubclassProc(hwnd, message, wparam, lparam);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        Shell_NotifyIconW(2, ref data); RemoveWindowSubclass(window, callback, 1);
        if (ownedIcon != 0) DestroyIcon(ownedIcon);
        GC.KeepAlive(callback); // The fallback LoadIcon handle is shared and is not destroyed.
    }
}


