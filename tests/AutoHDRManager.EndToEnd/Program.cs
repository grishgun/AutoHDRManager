using AutoHDRManager.Core;
using AutoHDRManager.Windows;
using System.Diagnostics;
using System.Text.Json;

// Opt-in manual acceptance runner. Start only after the user approves two actual HDR cycles.
// Run from Explorer to use the ordinary user profile, not the launching tool's MSIX redirection.
if (args.Length == 2 && args[0] == "--cleanup-approved-test") { await TestCleanup.RunAsync(Path.GetFullPath(args[1])); return; }
if (args.Length != 3 || args[0] != "--approved-two-hdr-tests") return;
string fixture = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoHDRManager");
string starts = Path.Combine(output, "game-starts.jsonl"), shortcut = Path.Combine(output, "HDR lifecycle test.lnk");
string reportPath = Path.Combine(output, "result.json");
var report = new Dictionary<string, object?> { ["StartedUtc"] = DateTimeOffset.UtcNow, ["Status"] = "Preflight", ["Fixture"] = fixture };
var results = new List<object>(); report["Tests"] = results;
var saved = new Dictionary<string, byte[]?>(); bool modified = false;
var controller = new WindowsHdrController(new Sink()); Display? original = null;
Guid gameId = Guid.NewGuid();
void Save(string status) { report["Status"] = status; File.WriteAllText(reportPath + ".tmp", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); File.Move(reportPath + ".tmp", reportPath, true); }
int[] Managers()
{
    var found = new List<int>();
    foreach (var process in Process.GetProcessesByName("AutoHDRManager")) { using (process) { if (process.SessionId == Process.GetCurrentProcess().SessionId) found.Add(process.Id); } }
    return found.ToArray();
}
List<JsonElement> Events()
{
    var result = new List<JsonElement>();
    string file = Path.Combine(root, "Logs", "current.jsonl"); if (!File.Exists(file)) return result;
    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(stream);
    while (reader.ReadLine() is string line) { try { using var json = JsonDocument.Parse(line); result.Add(json.RootElement.Clone()); } catch (JsonException) { } }
    return result;
}
List<JsonElement> Starts() => File.Exists(starts) ? File.ReadAllLines(starts).Where(l => l.Length > 0).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList() : [];
try
{
    if (!File.Exists(fixture)) throw new FileNotFoundException("Fixture missing", fixture);
    if (File.Exists(starts)) throw new InvalidOperationException("Use a fresh result directory; do not mix test runs.");
    if (Managers().Length != 0) throw new InvalidOperationException("Close Auto HDR Manager before starting the acceptance test.");
    await using (var exclusive = new SingleInstance()) { if (!exclusive.IsPrimary) throw new InvalidOperationException("Another manager instance exists."); }
    if (File.Exists(Path.Combine(root, "session.json"))) throw new InvalidOperationException("An unfinished HDR session exists.");
    var repository = new GameRepository(root); var games = repository.Load(); var settings = new SettingsRepository(root).Load();
    var processes = await new ProcessMonitor().SnapshotAsync();
    if (processes.Any(p => games.Any(g => GamePaths.Equal(p.Path, g.ExecutablePath)))) throw new InvalidOperationException("A user game is running; test aborted before changes.");
    var displays = await controller.GetDisplaysAsync();
    original = settings.DisplayId is { Length: > 0 } id ? displays.Single(d => d.Id == id) : displays.First(d => d.Supported);
    if (original.Name != "MO34WQC2" || original.Enabled) throw new InvalidOperationException("Expected MO34WQC2 with HDR OFF; no changes performed.");
    report["Original"] = original; report["GameId"] = gameId;
    string launcher = new GameShortcuts().Launcher;
    if (!File.Exists(launcher)) throw new FileNotFoundException("Stable launcher missing", launcher);
    foreach (string name in new[] { "games.json", "games.json.bak", "settings.json", "settings.json.bak" })
    {
        string path = Path.Combine(root, name); saved[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
        if (saved[path] != null) File.WriteAllBytes(Path.Combine(output, name + ".original"), saved[path]!);
    }
    var game = new Game { Id = gameId, Name = "HDR acceptance fixture (temporary)", ExecutablePath = fixture, Arguments = $"5 \"{starts}\"" };
    modified = true; games.Add(game); repository.Save(games);
    new SettingsRepository(root).Save(settings with { Monitoring = true, Startup = false, PollSeconds = 1, RestoreDelaySeconds = 2, LaunchTimeoutSeconds = 15 });
    GameShortcuts.WriteLink(shortcut, launcher, game); report["Shortcut"] = GameShortcuts.ReadLink(shortcut);
    int primary = 0;
    for (int cycle = 1; cycle <= 2; cycle++)
    {
        var since = DateTimeOffset.UtcNow;
        Save(cycle == 1 ? "ColdRunning" : "WarmRunning");
        if (cycle == 2 && !Managers().SequenceEqual(new[] { primary })) throw new InvalidOperationException("Primary changed before warm launch.");
        using var launch = Process.Start(new ProcessStartInfo(shortcut) { UseShellExecute = true });
        JsonElement record = default; bool restored = false;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(250);
            var records = Starts(); if (records.Count < cycle) continue;
            record = records[cycle - 1];
            int pid = record.GetProperty("Pid").GetInt32();
            if (!Events().Any(e => e.GetProperty("Event").GetString() == "GameExited" && e.GetProperty("Time").GetDateTimeOffset() >= since && e.GetProperty("Details").GetProperty("Pid").GetInt32() == pid)) continue;
            if (File.Exists(Path.Combine(root, "session.json"))) continue;
            var actual = await controller.ReadAsync(original.Id);
            if (actual.Enabled == original.Enabled && actual.Active == original.Active) { restored = true; break; }
        }
        if (!restored) throw new TimeoutException($"Cycle {cycle}: process exit and restored original HDR were not confirmed.");
        var events = Events().Where(e => e.GetProperty("Time").GetDateTimeOffset() >= since).ToList();
        var confirmed = events.Single(e => e.GetProperty("Event").GetString() == "LaunchHdrConfirmed" && e.GetProperty("Details").GetProperty("GameId").GetGuid() == gameId);
        var dispatched = events.Single(e => e.GetProperty("Event").GetString() == "GameLaunchDispatched" && e.GetProperty("Details").GetProperty("GameId").GetGuid() == gameId);
        var processCreated = record.GetProperty("ProcessCreatedUtc").GetDateTimeOffset();
        double stabilizationMs = (processCreated - confirmed.GetProperty("Time").GetDateTimeOffset()).TotalMilliseconds;
        var fixtureDisplay = record.GetProperty("Displays").EnumerateArray().Single(d => d.GetProperty("Id").GetString() == original.Id);
        if (!fixtureDisplay.GetProperty("Enabled").GetBoolean() || !fixtureDisplay.GetProperty("Active").GetBoolean()) throw new InvalidOperationException("Fixture did not observe HDR ON/Active.");
        if (stabilizationMs < 1000) throw new InvalidOperationException("Game process started before the one-second stabilization completed.");
        var managers = Managers(); if (managers.Length != 1) throw new InvalidOperationException("Single instance check failed.");
        if (cycle == 1) primary = managers[0]; else if (managers[0] != primary) throw new InvalidOperationException("Warm IPC launched a different primary.");
        results.Add(new { Mode = cycle == 1 ? "Cold" : "WarmIPC", Result = "PASS", PrimaryPid = primary, StabilizationMs = stabilizationMs, Fixture = record, Restored = await controller.ReadAsync(original.Id), Events = events });
        Save(cycle == 1 ? "ColdPassed" : "TestsPassedAwaitingManagerExit");
    }
}
catch (Exception ex) { report["Error"] = ex.ToString(); Save("FailedAwaitingCleanup"); }
finally
{
    if (modified)
    {
        // The operator uses the normal Exit button; never kill the app during HDR management.
        report["CleanupInstruction"] = "Exit Auto HDR Manager normally to restore the original library/settings.";
        Save(report.ContainsKey("Error") ? "FailedAwaitingManagerExit" : "TestsPassedAwaitingManagerExit");
        var deadline = DateTimeOffset.UtcNow.AddMinutes(10);
        while (Managers().Length != 0 && DateTimeOffset.UtcNow < deadline) await Task.Delay(500);
        if (Managers().Length == 0)
        {
            try
            {
                foreach (var pair in saved) { if (pair.Value == null) { if (File.Exists(pair.Key)) File.Delete(pair.Key); } else File.WriteAllBytes(pair.Key, pair.Value); }
                if (File.Exists(shortcut)) File.Delete(shortcut);
                report["LibraryAndSettingsRestored"] = saved.All(p => p.Value == null ? !File.Exists(p.Key) : File.ReadAllBytes(p.Key).SequenceEqual(p.Value));
                report["FinalDisplay"] = original == null ? null : await controller.ReadAsync(original.Id);
                Save(report.ContainsKey("Error") ? "FAIL" : "PASS");
            }
            catch (Exception ex) { report["CleanupError"] = ex.ToString(); Save("CleanupFailed"); }
        }
        else Save("CleanupPendingManagerStillRunning");
    }
    else Save("AbortedBeforeChanges");
}
sealed class Sink : IEventLog { public void Write(string name, object? details = null) { } }
