using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AutoHDRManager.Windows;

public sealed record VideoAssociationValue(string Key, string Name, string Value);
public sealed class VideoAssociations(GameShortcuts shortcuts)
{
    public const string AppName = "AutoHDRManager.Video";
    public const string ProgId = "AutoHDRManager.VideoFile";
    private const string Capabilities = @"Software\AutoHDRManager\Video\Capabilities";
    public bool IsRegistered
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications"); return key?.GetValue(AppName) as string == Capabilities; }
    }
    public static IReadOnlyList<VideoAssociationValue> Registration(string launcher) =>
    [
        new(@"Software\Classes\" + ProgId, "", "Auto HDR Manager Video"),
        new(@"Software\Classes\" + ProgId + @"\DefaultIcon", "", '"' + launcher + "\",0"),
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
        foreach (var item in Registration(shortcuts.Launcher))
        { using var key = Registry.CurrentUser.CreateSubKey(item.Key); key.SetValue(item.Name, item.Value, RegistryValueKind.String); }
        SHChangeNotify(0x08000000, 0, 0, 0);
    }
    public void RefreshExisting() { if (IsRegistered) shortcuts.EnsureLauncher(); }
    public static void OpenDefaults(bool forManager)
    {
        string uri = forManager ? "ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(AppName) : "ms-settings:defaultapps";
        using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }
    // Disabling the feature deliberately keeps the handler as a VLC passthrough until the user chooses VLC in Settings.
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
