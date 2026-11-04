using Windows.Foundation.Metadata;
using Windows.UI.Shell;
using System.Runtime.InteropServices;

namespace AutoHDRManager.Windows;

/// <summary>Read-only probes. API presence is deliberately separate from permission and package identity.</summary>
public static class TaskbarSupport
{
    public static async Task<Dictionary<string, object?>> ProbeAsync()
    {
        var result = new Dictionary<string, object?> { ["OS"] = Environment.OSVersion.Version.ToString() };
        void Probe(string key, Func<object?> read)
        {
            try { result[key] = read(); }
            catch (Exception ex) { result[key] = $"0x{ex.HResult:X8}: {ex.Message}"; }
        }
        Probe("PackageIdentity", () => global::Windows.ApplicationModel.Package.Current.Id.FullName);
        Probe("TaskbarManagerPresent", () => ApiInformation.IsTypePresent("Windows.UI.Shell.TaskbarManager"));
        Probe("RequestPinSecondaryTileAsyncPresent", () => ApiInformation.IsMethodPresent("Windows.UI.Shell.TaskbarManager", "RequestPinSecondaryTileAsync"));
        Probe("DesktopAppSupportMarker", DesktopSupport);
        Probe("GetDefault", () => TaskbarManager.GetDefault() != null);
        Probe("IsSupported", () => TaskbarManager.GetDefault().IsSupported);
        Probe("IsPinningAllowed", () => TaskbarManager.GetDefault().IsPinningAllowed);
        try { result["SecondaryTileReadProbe"] = await TaskbarManager.GetDefault().IsSecondaryTilePinnedAsync("AutoHDRManager.CapabilityProbe"); }
        catch (Exception ex) { result["SecondaryTileReadProbe"] = $"0x{ex.HResult:X8}: {ex.Message}"; }
        result["Strategy"] = "Standard Shell Link; manual pin by user. No RequestPinCurrentAppAsync substitute.";
        return result;
    }
    private static object DesktopSupport()
    {
        // Microsoft windows-rs 0.62.0 Windows/UI/Shell/mod.rs, ITaskbarManagerDesktopAppSupportStatics.
        var iid = new Guid("CDFEFD63-E879-4134-B9A7-8283F05F9480");
        const string name = "Windows.UI.Shell.TaskbarManager";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var text));
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(text, ref iid, out var factory));
            Marshal.Release(factory); return true;
        }
        finally { WindowsDeleteString(text); }
    }
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string value, int length, out nint text);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint text);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint factory);
}
