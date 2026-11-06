using System.Runtime.InteropServices;
using AutoHDRManager.Windows;
using Xunit;

namespace AutoHDRManager.Windows.Tests;

public sealed class VideoIconTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "VideoIcon-" + Guid.NewGuid().ToString("N"));
    [Fact] public void VlcIconDoesNotChangeTheManagerOpenCommand()
    {
        Directory.CreateDirectory(root);
        string vlc = Path.Combine(root, "VLC with spaces.exe"); File.WriteAllText(vlc, "fixture");
        var associations = new VideoAssociations(new GameShortcuts(root), () => vlc);
        string icon = associations.ResolveIcon();
        Assert.Equal('"' + vlc + "\",0", icon);
        var plan = VideoAssociations.Registration(@"C:\Manager\AutoHDRManager.exe", icon);
        Assert.Equal(icon, Assert.Single(plan, v => v.Key.EndsWith("DefaultIcon")).Value);
        Assert.Equal("\"C:\\Manager\\AutoHDRManager.exe\" --open-video \"%1\"", Assert.Single(plan, v => v.Key.EndsWith(@"shell\open\command")).Value);
        Assert.False(Directory.Exists(Path.Combine(root, "Launcher")));
    }
    [Theory][InlineData(null)][InlineData("missing-vlc.exe")]
    public void MissingVlcUsesDurableShellReadableVideoIcon(string? missing)
    {
        var associations = new VideoAssociations(new GameShortcuts(root), () => missing);
        string reference = associations.ResolveIcon();
        string path = Path.Combine(root, "Launcher", "Video.ico");
        Assert.Equal('"' + path + "\",0", reference);
        Assert.Equal(1u, ExtractIconEx(path, 0, out var large, out var small, 1));
        try { Assert.NotEqual(0, large); Assert.NotEqual(0, small); }
        finally { if (large != 0) DestroyIcon(large); if (small != 0) DestroyIcon(small); }
        var modified = File.GetLastWriteTimeUtc(path);
        Assert.Equal(reference, associations.ResolveIcon()); Assert.Equal(modified, File.GetLastWriteTimeUtc(path));
        File.WriteAllText(path, "corrupt"); associations.ResolveIcon();
        Assert.Equal((byte)0, File.ReadAllBytes(path)[0]);
    }
    [Fact] public void ResolverTracksVlcInstallationAndRemovalWithoutChangingStableLauncher()
    {
        Directory.CreateDirectory(root); string? vlc = null;
        var shortcuts = new GameShortcuts(root);
        var associations = new VideoAssociations(shortcuts, () => vlc);
        var fallback = associations.ResolveIcon();
        vlc = Path.Combine(root, "vlc.exe"); File.WriteAllText(vlc, "fixture");
        Assert.NotEqual(fallback, associations.ResolveIcon());
        File.Delete(vlc); Assert.Equal(fallback, associations.ResolveIcon());
        Assert.EndsWith(@"Launcher\AutoHDRManager.exe", shortcuts.Launcher);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, out nint large, out nint small, uint count);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
