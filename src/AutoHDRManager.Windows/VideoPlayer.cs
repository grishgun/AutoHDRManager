using System.Diagnostics;
using Microsoft.Win32;
using AutoHDRManager.Core;

namespace AutoHDRManager.Windows;

public sealed class VideoPlayer : IVideoPlayer
{
    public string? Find(string? configuredPath)
    {
        if (Valid(configuredPath)) return Path.GetFullPath(configuredPath!);
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var app = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\vlc.exe");
                string? path = (app?.GetValue("") as string)?.Trim('"');
                if (Valid(path)) return path;
                using var vlc = root.OpenSubKey(@"Software\VideoLAN\VLC");
                path = vlc?.GetValue("InstallDir") is string dir ? Path.Combine(dir, "vlc.exe") : null;
                if (Valid(path)) return path;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        foreach (var directory in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            string path = Path.Combine(directory, "VideoLAN", "VLC", "vlc.exe");
            if (Valid(path)) return path;
        }
        return null;
    }
    public static bool Valid(string? path) => path != null && Path.IsPathFullyQualified(path) &&
        string.Equals(Path.GetFileName(path), "vlc.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
    public static ProcessStartInfo CreateStartInfo(string executable, string file)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        start.ArgumentList.Add("--no-one-instance"); start.ArgumentList.Add("--no-one-instance-when-started-from-file");
        start.ArgumentList.Add("--"); start.ArgumentList.Add(VideoPath.Validate(file));
        return start;
    }
    public IVideoProcess Start(string executable, string path)
    {
        try { return new OwnedProcess(Process.Start(CreateStartInfo(executable, path)) ?? throw new IOException("No process")); }
        catch (Exception ex) { throw new VideoPlayerStartException(ex); }
    }
    private sealed class OwnedProcess : IVideoProcess
    {
        private readonly Process process;
        public ProcessIdentity Identity { get; }
        public OwnedProcess(Process value)
        {
            process = value;
            // The retained OS handle, not the executable name or a reused PID, establishes lifetime.
            _ = process.Handle;
            Identity = new(process.Id, process.StartTime.ToUniversalTime().ToFileTimeUtc());
        }
        public bool HasExited => process.HasExited;
        public void Dispose() => process.Dispose();
    }
}
