using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using AutoHDRManager.Core;

namespace AutoHDRManager.Windows;

public sealed record ShortcutInfo(string Path, string Target, string Arguments, string Icon, int IconIndex, string AppId);

/// <summary>Only owns our roaming profile directory. Never accesses Explorer's pinned-item store.</summary>
public sealed class GameShortcuts(string? root = null, string? executable = null)
{
    public string Root { get; } = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoHDRManager");
    private readonly string source = executable ?? Environment.ProcessPath!;
    public string Launcher => Path.Combine(Root, "Launcher", "AutoHDRManager.exe");
    public string DirectoryPath => Path.Combine(Root, "Shortcuts");
    public const string ManagerAppId = "AutoHDRManager.Manager";
    public static string GameAppId(Guid id) => "AutoHDRManager.Game." + id.ToString("N");
    private JsonFile<Dictionary<Guid, string>> Index => new(Path.Combine(Root, "shortcuts.json"),
        values => values.All(p => p.Key != Guid.Empty && p.Value == Path.GetFileName(p.Value) && p.Value.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)));

    public static void SetManagerIdentity() => Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(ManagerAppId));
    public string Create(Game game)
    {
        GamePaths.Validate(game);
        EnsureLauncher();
        Directory.CreateDirectory(DirectoryPath);
        var index = Index.Load() ?? [];
        if (!index.TryGetValue(game.Id, out var name))
        {
            string safeName = string.Concat(game.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
            safeName = safeName.Length > 70 ? safeName[..70] : safeName;
            name = $"{safeName} — HDR {game.Id.ToString("N")[..8]}.lnk";
        }
        string path = Path.Combine(DirectoryPath, name);
        WriteLink(path, Launcher, game);
        index[game.Id] = name; Index.Save(index);
        return path;
    }
    public void RefreshExisting(IReadOnlyList<Game> games)
    {
        var index = Index.Load(); if (index is not { Count: > 0 }) return;
        EnsureLauncher();
        foreach (var game in games)
            if (index.TryGetValue(game.Id, out var name) && File.Exists(game.ExecutablePath))
                WriteLink(Path.Combine(DirectoryPath, name), Launcher, game);
        // Removed games are rejected by GameId at launch, including copies already pinned by Windows.
    }
    public void EnsureLauncher()
    {
        if (root == null)
        {
            Directory.CreateDirectory(Root);
            using var directory = CreateFile(Root, 0, 7, 0, 3, 0x02000000, 0);
            if (directory.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var actual = new StringBuilder(32768);
            if (GetFinalPathNameByHandle(directory, actual, (uint)actual.Capacity, 0) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            string physical = actual.ToString(); if (physical.StartsWith(@"\\?\")) physical = physical[4..];
            if (!GamePaths.Equal(physical, Root) && physical.Contains(@"\AppData\Local\Packages\", StringComparison.OrdinalIgnoreCase) && physical.Contains(@"\LocalCache\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Windows перенаправила папку приложения в другой профиль или контейнер. Завершите Auto HDR Manager и запустите portable EXE из Проводника, затем создайте ярлык снова.");
        }
        if (GamePaths.Equal(source, Launcher)) return;
        if (!File.Exists(source)) throw new FileNotFoundException("Portable EXE не найден.", source);
        if (File.Exists(Path.ChangeExtension(source, ".runtimeconfig.json")))
            throw new InvalidOperationException("Создание ярлыков доступно из опубликованного portable EXE.");
        Directory.CreateDirectory(Path.GetDirectoryName(Launcher)!);
        if (File.Exists(Launcher) && new FileInfo(source).Length == new FileInfo(Launcher).Length)
        {
            using var a = File.OpenRead(source); using var b = File.OpenRead(Launcher);
            if (SHA256.HashData(a).SequenceEqual(SHA256.HashData(b))) return;
        }
        string temporary = Launcher + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary);
            File.Move(temporary, Launcher, true);
        }
        catch (IOException ex) { throw new IOException("Не удалось обновить служебный EXE. Завершите Auto HDR Manager через трей и откройте новую portable-версию ещё раз. Закреплённые значки сохранятся.", ex); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void OpenLocation(string path)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.Arguments = "/select,\"" + path + "\"";
        using var process = Process.Start(start);
    }
    public static void WriteLink(string path, string target, Game game)
    {
        object instance = new ShellLink();
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var link = (IShellLinkW)instance;
            link.SetPath(target); link.SetArguments("--launch-game " + game.Id.ToString("D"));
            link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
            link.SetDescription(game.Name + Localization.Get("ShortcutDescription")); link.SetShowCmd(1);
            link.SetIconLocation(IconSource(game.ExecutablePath, target), 0);
            var properties = (IPropertyStore)instance;
            var key = new PropertyKey(new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
            var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(GameAppId(game.Id)) };
            try { properties.SetValue(ref key, ref value); properties.Commit(); }
            finally { PropVariantClear(ref value); }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ((IPersistFile)instance).Save(temporary, true);
            File.Move(temporary, path, true);
            SHChangeNotify(0x2000, 0x5, path, 0); // SHCNE_UPDATEITEM, SHCNF_PATHW
        }
        finally { Marshal.FinalReleaseComObject(instance); if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string IconSource(string executable, string fallback) => File.Exists(executable) && ExtractIconEx(executable, -1, 0, 0, 0) is > 0 and < uint.MaxValue ? executable : fallback;
    // Diagnostic lookup only: no index repair, launcher refresh or shortcut writes.
    public IReadOnlyList<ShortcutInfo> ReadExisting(Guid gameId)
    {
        if (!Directory.Exists(DirectoryPath)) return [];
        return Directory.EnumerateFiles(DirectoryPath, "*.lnk").Select(ReadLink)
            .Where(link => string.Equals(link.Arguments, "--launch-game " + gameId.ToString("D"), StringComparison.OrdinalIgnoreCase)
                && GamePaths.Equal(link.Target, Launcher)).ToArray();
    }
    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, nint large, nint small, uint count);
    public static ShortcutInfo ReadLink(string path)
    {
        object instance = new ShellLink();
        try
        {
            ((IPersistFile)instance).Load(path, 0);
            var link = (IShellLinkW)instance;
            var target = new StringBuilder(32768); var args = new StringBuilder(2048); var icon = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, 0, 4); link.GetArguments(args, args.Capacity); link.GetIconLocation(icon, icon.Capacity, out var index);
            var key = new PropertyKey(new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
            ((IPropertyStore)instance).GetValue(ref key, out var value);
            string appId; try { appId = value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "" : ""; } finally { PropVariantClear(ref value); }
            return new(path, target.ToString(), args.ToString(), icon.ToString(), index, appId);
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern void SHChangeNotify(uint eventId, uint flags, string item1, nint item2);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey(Guid format, uint id) { public Guid Format = format; public uint Id = id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Pointer; }
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink { }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count); void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value); void SetValue(ref PropertyKey key, ref PropVariant value); void Commit();
    }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, nint findData, uint flags);
        void GetIDList(out nint pidl); void SetIDList(nint pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey); void SetHotkey(short hotkey); void GetShowCmd(out int command); void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved); void Resolve(nint hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}

