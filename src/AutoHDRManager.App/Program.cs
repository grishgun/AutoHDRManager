using AutoHDRManager.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using AutoHDRManager.Core;
using System.Runtime.InteropServices;

namespace AutoHDRManager.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // This worker never creates a manager, takes the single-instance lock, opens a window or touches HDR.
        if (args.Length == 2 && args[0] == "--video-probe")
        {
            VideoMetadata result;
            try { result = MediaInfoMetadata.Read(args[1]); }
            catch (Exception ex) { result = new(VideoKind.Unknown, "ProbeError:" + ex.GetType().Name); }
            using var output = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false));
            output.Write(System.Text.Json.JsonSerializer.Serialize(result));
            return;
        }
        try
        {
            var settings = new SettingsRepository(AppDataPath.Resolve(args)).Load();
            Localization.SetLanguage(settings.Language, global::Windows.System.UserProfile.GlobalizationPreferences.Languages.FirstOrDefault() ?? System.Globalization.CultureInfo.CurrentUICulture.Name);
        }
        catch (Exception ex) { ShowError(Localization.Message(ex.Message)); return; }
        if (args.Length == 2 && args[0] == "--taskbar-report")
        {
            try
            {
                WinRT.ComWrappersSupport.InitializeComWrappers();
                var report = TaskbarSupport.ProbeAsync().GetAwaiter().GetResult();
                string profileRoot = AppDataPath.Resolve(args);
                report["ProfileDataHashes"] = new[] { "settings.json", "games.json", "cover-key.bin" }.ToDictionary(name => name, name =>
                {
                    string path = Path.Combine(profileRoot, name);
                    if (!File.Exists(path)) return (string?)null;
                    using var file = File.OpenRead(path);
                    return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file));
                });
                report["Games"] = new GameRepository(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoHDRManager")).Load();
                var shortcuts = new GameShortcuts();
                report["Shortcuts"] = Directory.Exists(shortcuts.DirectoryPath) ? Directory.GetFiles(shortcuts.DirectoryPath, "*.lnk").Select(GameShortcuts.ReadLink).ToArray() : [];
                if (File.Exists(shortcuts.Launcher)) { using var file = File.OpenRead(shortcuts.Launcher); report["LauncherSHA256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)); }
                File.WriteAllText(Path.GetFullPath(args[1]), System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { ShowError(ex.Message); }
            return;
        }
        LaunchRequest request;
        try { request = LaunchRequest.Parse(args); GameShortcuts.SetManagerIdentity(); }
        catch (Exception ex) { ShowError(ex.Message); return; }
        string suffix = args.Contains("--ui-test-root") ? "-ui-test-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AppDataPath.Resolve(args))))[..16] : "";
        using var lifetime = new InstanceLifetime(suffix);
        if (!lifetime.Instance.IsPrimary)
        {
            try { lifetime.Instance.NotifyAsync(request).GetAwaiter().GetResult(); }
            catch { ShowError(Localization.Get("IpcFailure")); }
            return;
        }
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(lifetime.Instance, args);
        });
    }
    private static void ShowError(string message) => MessageBox(0, Localization.Message(message), "Auto HDR Manager", 0x10);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(nint hwnd, string message, string title, uint type);
    private sealed class InstanceLifetime(string suffix) : IDisposable
    {
        public SingleInstance Instance { get; } = new(suffix);
        public void Dispose() => Instance.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

