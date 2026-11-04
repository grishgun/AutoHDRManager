using AutoHDRManager.Core;
using Xunit;

namespace AutoHDRManager.Tests;

public class EngineTests
{
    private sealed class Hdr : IHdrController
    {
        public bool Enabled, FailEnable, FailRestore, Missing;
        public List<bool> Writes = [];
        public Display Display => new("monitor", "Test", true, Enabled, Enabled, true, 0);
        public Task<IReadOnlyList<Display>> GetDisplaysAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<Display>>([Display]);
        public Task<Display> ReadAsync(string id, CancellationToken token = default) => Missing ? throw new IOException("Disconnected") : Task.FromResult(Display);
        public Task SetAsync(string id, bool enabled, CancellationToken token = default)
        {
            Writes.Add(enabled);
            if (enabled ? FailEnable : FailRestore) throw new IOException("API failed");
            Enabled = enabled; return Task.CompletedTask;
        }
    }
    private sealed class Journal : ISessionRepository
    {
        public Session? Saved;
        public Session? Load() => Saved;
        public void Save(Session session) => Saved = session;
        public void Clear() => Saved = null;
    }
    private sealed class Sink : IEventLog, INotificationService { public void Write(string name, object? details = null) { } public void Show(string title, string message) { } }
    private sealed class Rig
    {
        public readonly Hdr Hdr = new(); public readonly Journal Journal = new(); public readonly Settings Settings = new(); public readonly HdrEngine Engine;
        public readonly Game A = new() { Name = "A", ExecutablePath = Path.GetFullPath("a.exe") };
        public readonly Game B = new() { Name = "B", ExecutablePath = Path.GetFullPath("b.exe") };
        private readonly DateTimeOffset epoch = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        public Rig(bool original = false) { Hdr.Enabled = original; var sink = new Sink(); Engine = new(Hdr, Journal, sink, sink, Settings); }
        public GameProcess P(int pid = 1, bool second = false, long created = 10) => new(new(pid, created), second ? B.ExecutablePath : A.ExecutablePath, "test");
        public Task Tick(double seconds, params GameProcess[] processes) => Engine.TickAsync(processes, [A, B], epoch.AddSeconds(seconds));
    }
    [Fact] public async Task SingleGameWaitsEnablesAndRestores()
    {
        var r = new Rig(); await r.Tick(0, r.P()); Assert.Equal(HdrState.WaitingToEnable, r.Engine.State);
        await r.Tick(2, r.P()); Assert.Empty(r.Hdr.Writes); await r.Tick(3, r.P()); Assert.True(r.Hdr.Enabled);
        await r.Tick(4); Assert.True(r.Hdr.Enabled); await r.Tick(6); Assert.False(r.Hdr.Enabled); Assert.Equal(new[] { true, false }, r.Hdr.Writes); Assert.Null(r.Journal.Saved);
    }
    [Fact] public async Task OriginalHdrRemainsOn()
    {
        var r = new Rig(true); await r.Tick(0, r.P()); await r.Tick(4, r.P()); await r.Tick(5); await r.Tick(7); Assert.True(r.Hdr.Enabled); Assert.Empty(r.Hdr.Writes);
    }
    [Fact] public async Task TwoGamesShareOneSession()
    {
        var r = new Rig(); await r.Tick(0, r.P()); await r.Tick(3, r.P(), r.P(2, true)); await r.Tick(5, r.P(2, true)); Assert.True(r.Hdr.Enabled);
        await r.Tick(6); await r.Tick(8); Assert.Equal(new[] { true, false }, r.Hdr.Writes);
    }
    [Fact] public async Task MultipleInstancesMustAllExit()
    {
        var r = new Rig(); await r.Tick(0, r.P(), r.P(2)); await r.Tick(3, r.P(), r.P(2)); await r.Tick(5, r.P(2)); Assert.True(r.Hdr.Enabled);
        await r.Tick(6); await r.Tick(8); Assert.False(r.Hdr.Enabled);
    }
    [Fact] public async Task FastExitCancelsDelayedEnable()
    {
        var r = new Rig(); await r.Tick(0, r.P()); await r.Tick(1); await r.Tick(10); Assert.Empty(r.Hdr.Writes); Assert.False(r.Engine.HasSession);
    }
    [Fact] public async Task RestartDuringRestoreDelayDoesNotToggle()
    {
        var r = new Rig(); await r.Tick(0, r.P()); await r.Tick(3, r.P()); await r.Tick(4); await r.Tick(5, r.P(created: 20)); await r.Tick(10, r.P(created: 20)); Assert.Single(r.Hdr.Writes);
    }
    [Fact] public async Task EnableFailuresHaveBoundedRetries()
    {
        var r = new Rig(); r.Hdr.FailEnable = true; await r.Tick(0, r.P());
        for (int i = 3; i < 30; i++) await r.Tick(i, r.P());
        Assert.Equal(3, r.Hdr.Writes.Count); Assert.Equal(HdrState.Error, r.Engine.State);
    }
    [Fact] public async Task RestoreFailuresKeepJournalAndStopRetrying()
    {
        var r = new Rig(); await r.Tick(0, r.P()); await r.Tick(3, r.P()); r.Hdr.FailRestore = true;
        for (int i = 4; i < 30; i++) await r.Tick(i);
        Assert.Equal(4, r.Hdr.Writes.Count); Assert.NotNull(r.Journal.Saved); Assert.Equal(HdrState.Error, r.Engine.State);
    }
    [Fact] public async Task PauseRestoreFailuresAreAlsoBounded()
    {
        var r = new Rig(); await r.Tick(0, r.P()); await r.Tick(3, r.P()); r.Hdr.FailRestore = true; r.Settings.Monitoring = false;
        for (int i = 4; i < 30; i++) await r.Tick(i, r.P());
        Assert.Equal(4, r.Hdr.Writes.Count); Assert.NotNull(r.Journal.Saved);
    }
    [Fact] public async Task ManualChangeSuppressesAllLaterWrites()
    {
        var r = new Rig(); await r.Tick(0, r.P()); await r.Tick(3, r.P()); r.Hdr.Enabled = false; await r.Tick(4, r.P());
        r.Hdr.Enabled = true; await r.Tick(5, r.P()); await r.Tick(6); Assert.Single(r.Hdr.Writes); Assert.True(r.Hdr.Enabled);
    }
    [Fact] public async Task ManualChangeDuringEnableDelayIsPreserved()
    {
        var r = new Rig(); await r.Tick(0, r.P()); r.Hdr.Enabled = true; await r.Tick(3, r.P()); await r.Tick(4); Assert.Empty(r.Hdr.Writes); Assert.True(r.Hdr.Enabled);
    }
    [Fact] public async Task CrashRecoveryNeverOverwritesUnobservedManualChanges()
    {
        var r = new Rig(true); r.Journal.Saved = new() { DisplayId = "monitor", Original = false, LastConfirmed = true, ChangedByUs = true };
        await r.Tick(0, r.P()); Assert.True(r.Engine.ManualOverride); await r.Tick(5); Assert.Empty(r.Hdr.Writes); Assert.True(r.Hdr.Enabled);
    }
    [Fact] public async Task PauseRestoresThenResumeStartsNewSession()
    {
        var r = new Rig(); await r.Tick(0, r.P()); await r.Tick(3, r.P()); r.Settings.Monitoring = false; await r.Tick(4, r.P()); Assert.False(r.Hdr.Enabled); Assert.Equal(HdrState.Suspended, r.Engine.State);
        r.Settings.Monitoring = true; await r.Tick(5, r.P()); await r.Tick(8, r.P()); Assert.True(r.Hdr.Enabled);
    }
    [Fact] public async Task ShutdownRestoresActiveSession()
    {
        var r = new Rig(); await r.Tick(0, r.P()); await r.Tick(3, r.P()); await r.Engine.ShutdownAsync(); Assert.False(r.Hdr.Enabled); Assert.Null(r.Journal.Saved);
    }
    [Fact] public async Task ShutdownAfterManualChangePreservesIt()
    {
        var r = new Rig(); await r.Tick(0, r.P()); await r.Tick(3, r.P()); r.Hdr.Enabled = false; await r.Engine.ShutdownAsync(); Assert.Single(r.Hdr.Writes);
    }
    [Fact] public async Task PidReuseDoesNotKeepAnUnrelatedProcessActive()
    {
        var r = new Rig(); await r.Tick(0, r.P()); await r.Tick(3, r.P()); var other = new GameProcess(new(1, 99), Path.GetFullPath("other.exe"), "other");
        await r.Tick(4, other); await r.Tick(6, other); Assert.False(r.Hdr.Enabled);
    }
    [Fact] public async Task TemporarilyMissingPathKeepsKnownInstance()
    {
        var r = new Rig(); await r.Tick(0, r.P()); await r.Tick(3, r.P()); await r.Tick(4, r.P() with { Path = null }); Assert.Single(r.Engine.ActiveProcesses);
    }
    [Fact] public async Task MissingDisplayForfeitsOwnership()
    {
        var r = new Rig(); await r.Tick(0, r.P()); await r.Tick(3, r.P()); r.Hdr.Missing = true; await r.Tick(4, r.P()); r.Hdr.Missing = false; await r.Tick(5); Assert.Single(r.Hdr.Writes); Assert.True(r.Hdr.Enabled);
    }
    [Fact] public async Task PresentationPreferencesDoNotInterruptActiveSession()
    {
        var r = new Rig(); await r.Tick(0, r.P()); await r.Tick(3, r.P());
        var session = r.Journal.Saved;
        r.Settings.Language = "en-US"; r.Settings.Theme = "Light"; r.Settings.LibraryView = "List";
        await r.Tick(4, r.P());
        Assert.Same(session, r.Journal.Saved); Assert.True(r.Engine.HasSession); Assert.True(r.Hdr.Enabled); Assert.Single(r.Hdr.Writes);
        r.Settings.Language = "ru-RU"; r.Settings.Theme = "Dark";
        await r.Tick(5, r.P()); await r.Tick(6); await r.Tick(8);
        Assert.Equal(new[] { true, false }, r.Hdr.Writes); Assert.Null(r.Journal.Saved);
    }
    [Fact] public async Task MissingExecutableDoesNotChangeHdr()
    {
        var r = new Rig(); await Assert.ThrowsAsync<FileNotFoundException>(() => r.Engine.PrepareLaunchAsync(r.A, DateTimeOffset.UtcNow)); Assert.Empty(r.Hdr.Writes);
    }
    [Fact] public async Task LaunchPreparesHdrAndTimeoutRestores()
    {
        var r = new Rig(); string exe = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"); File.WriteAllText(exe, "test");
        try
        {
            await r.Tick(0); await r.Engine.PrepareLaunchAsync(new Game { ExecutablePath = exe }, DateTimeOffset.Parse("2026-01-01T00:00:00Z")); Assert.True(r.Hdr.Enabled);
            await r.Tick(30); Assert.True(r.Hdr.Enabled); await r.Tick(61); await r.Tick(63); Assert.False(r.Hdr.Enabled);
        }
        finally { File.Delete(exe); }
    }
    [Fact] public async Task CancelledLaunchRestoresImmediately()
    {
        var r = new Rig(); string exe = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"); File.WriteAllText(exe, "test");
        try { await r.Tick(0); await r.Engine.PrepareLaunchAsync(new Game { ExecutablePath = exe }, DateTimeOffset.UtcNow); await r.Engine.CancelLaunchAsync(); Assert.False(r.Hdr.Enabled); }
        finally { File.Delete(exe); }
    }
    [Fact] public async Task CancellingSecondLaunchKeepsFirstReservation()
    {
        var r = new Rig(); string first = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"), second = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
        File.WriteAllText(first, "test"); File.WriteAllText(second, "test");
        try
        {
            await r.Tick(0); var a = new Game { ExecutablePath = first }; var b = new Game { ExecutablePath = second };
            await r.Engine.PrepareLaunchAsync(a, DateTimeOffset.UtcNow); await r.Engine.PrepareLaunchAsync(b, DateTimeOffset.UtcNow);
            await r.Engine.CancelLaunchAsync(gameId: b.Id); Assert.True(r.Hdr.Enabled);
            await r.Engine.CancelLaunchAsync(gameId: a.Id); Assert.False(r.Hdr.Enabled);
        }
        finally { File.Delete(first); File.Delete(second); }
    }
}
