using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AutoHDRManager.Windows;

public sealed record VideoAssociationValue(string Key, string Name, string Value);
public sealed class VideoAssociations(GameShortcuts shortcuts, Func<string?>? findVlc = null)
{
    public const string AppName = "AutoHDRManager.Video";
    public const string ProgId = "AutoHDRManager.VideoFile";
    private const string Capabilities = @"Software\AutoHDRManager\Video\Capabilities";
    public bool IsRegistered
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications"); return key?.GetValue(AppName) as string == Capabilities; }
    }
    public static IReadOnlyList<VideoAssociationValue> Registration(string launcher, string icon) =>
    [
        new(@"Software\Classes\" + ProgId, "", "Auto HDR Manager Video"),
        new(@"Software\Classes\" + ProgId + @"\DefaultIcon", "", icon),
        new(@"Software\Classes\" + ProgId + @"\shell\open\command", "", '"' + launcher + "\" --open-video \"%1\""),
        new(Capabilities, "ApplicationName", "Auto HDR Manager Video"),
        new(Capabilities, "ApplicationDescription", "Open HDR10/HLG videos in VLC with automatic Windows HDR management."),
        new(Capabilities + @"\FileAssociations", ".mkv", ProgId),
        new(Capabilities + @"\FileAssociations", ".mp4", ProgId),
        new(@"Software\RegisteredApplications", AppName, Capabilities),
        new(@"Software\Classes\.mkv\OpenWithProgids", ProgId, ""),
        new(@"Software\Classes\.mp4\OpenWithProgids", ProgId, "")
    ];
    public void Register()
    {
        shortcuts.EnsureLauncher();
        foreach (var item in Registration(shortcuts.Launcher, ResolveIcon()))
        { using var key = Registry.CurrentUser.CreateSubKey(item.Key); key.SetValue(item.Name, item.Value, RegistryValueKind.String); }
        SHChangeNotify(0x08000000, 0, 0, 0);
    }
    public void RefreshExisting()
    {
        if (!IsRegistered) return;
        shortcuts.EnsureLauncher();
        string icon = ResolveIcon();
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId + @"\DefaultIcon");
        if (key.GetValue("") as string == icon) return;
        key.SetValue("", icon, RegistryValueKind.String);
        SHChangeNotify(0x08000000, 0, 0, 0); // SHCNE_ASSOCCHANGED: invalidate Shell's association/icon cache.
    }
    public string ResolveIcon()
    {
        string? vlc = findVlc == null ? new VideoPlayer().Find(null) : findVlc();
        // VLC's installer registers vlc.exe,0 for both VLC.mkv and VLC.mp4.
        if (!string.IsNullOrEmpty(vlc) && File.Exists(vlc)) return '"' + Path.GetFullPath(vlc) + "\",0";
        string icon = Path.Combine(shortcuts.Root, "Launcher", "Video.ico");
        using var resource = typeof(VideoAssociations).Assembly.GetManifestResourceStream("AutoHDRManager.Windows.Video.ico")
            ?? throw new InvalidOperationException("Bundled video icon is missing.");
        using var buffer = new MemoryStream(); resource.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        if (!File.Exists(icon) || !File.ReadAllBytes(icon).AsSpan().SequenceEqual(bytes))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
            string temporary = icon + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, icon, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return '"' + icon + "\",0";
    }
    public static void OpenDefaults(bool forManager)
    {
        string uri = forManager ? "ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(AppName) : "ms-settings:defaultapps";
        using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }
    // Disabling the feature deliberately keeps the handler as a VLC passthrough until the user chooses VLC in Settings.
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
