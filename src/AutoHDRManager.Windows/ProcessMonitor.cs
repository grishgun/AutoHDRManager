using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using AutoHDRManager.Core;

namespace AutoHDRManager.Windows;

public sealed class ProcessMonitor : IGameProcessMonitor
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    private readonly Dictionary<ProcessIdentity, string> cache = [];
    private readonly object sync = new();
    public Task<IReadOnlyList<GameProcess>> SnapshotAsync(CancellationToken token = default)
    {
        lock (sync)
        {
            var result = new List<GameProcess>();
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var handle = OpenProcess(0x1000, false, process.Id); // PROCESS_QUERY_LIMITED_INFORMATION, no admin rights.
                        if (handle.IsInvalid || !GetProcessTimes(handle, out var created, out _, out _, out _))
                        {
                            // Access loss is not proof of exit. Keep already-known instances provisionally
                            // while the PID exists; a later successful query checks creation time again.
                            foreach (var known in cache.Keys.Where(k => k.Pid == process.Id)) result.Add(new(known, null, "Недоступен"));
                            continue;
                        }
                        var identity = new ProcessIdentity(process.Id, created);
                        if (!cache.TryGetValue(identity, out var path))
                        {
                            uint length = 32768; var buffer = new StringBuilder((int)length);
                            if (QueryFullProcessImageName(handle, 0, buffer, ref length)) { path = GamePaths.Normalize(buffer.ToString()); cache[identity] = path; }
                        }
                        result.Add(new(identity, path, process.ProcessName));
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException) { }
                }
            }
            var live = result.Select(p => p.Identity).ToHashSet();
            foreach (var stale in cache.Keys.Where(k => !live.Contains(k)).ToArray()) cache.Remove(stale);
            return Task.FromResult<IReadOnlyList<GameProcess>>(result);
        }
    }
}
