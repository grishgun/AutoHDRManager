using AutoHDRManager.Core;
using AutoHDRManager.Windows;
using System.Diagnostics;
using System.Text.Json;

// Harmless lifecycle fixture. Reads HDR, never writes it; exits naturally after the requested duration.
var entered = DateTimeOffset.UtcNow;
if (args.Length > 1)
{
    var displays = await new WindowsHdrController(new Sink()).GetDisplaysAsync();
    using var own = Process.GetCurrentProcess();
    File.AppendAllText(Path.GetFullPath(args[1]), JsonSerializer.Serialize(new
    {
        Pid = Environment.ProcessId, ProcessCreatedUtc = own.StartTime.ToUniversalTime(), EnteredUtc = entered,
        ReadUtc = DateTimeOffset.UtcNow, Displays = displays
    }) + Environment.NewLine);
}
await Task.Delay(args.Length > 0 && int.TryParse(args[0], out int seconds) ? Math.Clamp(seconds, 1, 600) * 1000 : 120_000);
sealed class Sink : IEventLog { public void Write(string name, object? details = null) { } }
