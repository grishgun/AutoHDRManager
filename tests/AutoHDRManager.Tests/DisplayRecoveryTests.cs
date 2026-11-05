using System.Text.Json;
using AutoHDRManager.Core;
using Xunit;

namespace AutoHDRManager.Tests;

public sealed class DisplayRecoveryTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-11-05T17:05:02Z");
    private sealed class Hdr : IHdrController
    {
        public List<Display> Displays = [new("path-A", "MO34WQC2", true, false, false, true, 0) { Fingerprint = "edid-A" }];
        public List<(string Id, bool Enabled)> Writes = [];
        public bool FailRead, FailQuery, FailWrite;
        public int Queries;
        public Task<IReadOnlyList<Display>> GetDisplaysAsync(CancellationToken token = default)
        {
            Queries++;
            if (FailQuery) throw new IOException("Topology unavailable");
            return Task.FromResult<IReadOnlyList<Display>>(Displays.ToArray());
        }
        public Task<Display> ReadAsync(string id, CancellationToken token = default)
        {
            if (FailRead) throw new IOException("Read failed");
            return Task.FromResult(Displays.SingleOrDefault(d => d.Id == id) ?? throw new IOException("Display disconnected"));
        }
        public Task SetAsync(string id, bool enabled, CancellationToken token = default)
        {
            Writes.Add((id, enabled));
            if (FailWrite) throw new IOException("Write failed");
            int index = Displays.FindIndex(d => d.Id == id);
            if (index < 0) throw new IOException("Disconnected before write");
            Displays[index] = Displays[index] with { Enabled = enabled, Active = enabled };
            return Task.CompletedTask;
        }
    }
    private sealed class Journal : ISessionRepository
    {
        private string? json;
        public Session? Load() => json == null ? null : JsonSerializer.Deserialize<Session>(json);
        public void Save(Session session) => json = JsonSerializer.Serialize(session);
        public void Clear() => json = null;
    }
    private sealed class Sink : IEventLog, INotificationService
    {
        public List<(string Name, string Details)> Events = [];
        public void Write(string name, object? details = null) => Events.Add((name, JsonSerializer.Serialize(details)));
        public void Show(string title, string message) { }
    }
    private sealed class Rig
    {
        public Hdr Hdr = new(); public Journal Journal = new(); public Sink Log = new();
        public Settings Settings = new() { EnableDelaySeconds = 0, RestoreDelaySeconds = 0, Notifications = false };
        public HdrEngine Engine;
        public Game Game = new() { ExecutablePath = Path.GetFullPath("recovery-game.exe") };
        public Display? Detached;
        public Rig() => Engine = new(Hdr, Journal, Log, Log, Settings);
        public Task Tick(double seconds, bool running = true) => Engine.TickAsync(running ? [new(new(21860, 10), Game.ExecutablePath, "game")] : [], [Game], Epoch.AddSeconds(seconds));
        public void Disconnect() { Detached = Hdr.Displays[0]; Hdr.Displays.Clear(); }
        public void Reconnect() => Hdr.Displays.Add(Detached!);
        public async Task StartAndDisconnect() { await Tick(0); Assert.True(Hdr.Displays[0].Enabled); Disconnect(); await Tick(1); }
        public void Restored() { Assert.Equal(new[] { ("path-A", true), ("path-A", false) }, Hdr.Writes); Assert.False(Hdr.Displays.Single(d => d.Id == "path-A").Enabled); Assert.Null(Journal.Load()); }
    }

    [Fact] public async Task DisconnectInSessionRetainsOriginalAndRestoresAfterReconnectAndExit()
    {
        var r = new Rig(); await r.StartAndDisconnect();
        Assert.False(r.Engine.ManualOverride); Assert.False(r.Journal.Load()!.Original);
        r.Reconnect(); await r.Tick(6); Assert.Single(r.Hdr.Writes); await r.Tick(7);
        Assert.Equal(HdrState.HDRActive, r.Engine.State); await r.Tick(8, false); r.Restored();
        Assert.Contains(r.Log.Events, e => e.Name == "DisplayReconnected");
    }
    [Fact] public async Task LongDisconnectionDuringGameDoesNotExpireOriginalState()
    {
        var r = new Rig(); await r.StartAndDisconnect(); await r.Tick(4 * 3600);
        Assert.False(r.Journal.Load()!.Original); Assert.Null(r.Journal.Load()!.RestoreDeadline);
        r.Reconnect(); await r.Tick(4 * 3600 + 5); await r.Tick(4 * 3600 + 6); await r.Tick(4 * 3600 + 7, false); r.Restored();
    }
    [Fact] public async Task ExitWhileDisconnectedPersistsPendingRestoreAndRestoresOnReturn()
    {
        var r = new Rig(); await r.StartAndDisconnect(); await r.Tick(2, false);
        var saved = r.Journal.Load()!; Assert.False(saved.Original); Assert.NotNull(saved.RestoreDeadline); Assert.Empty(saved.Processes);
        Assert.Contains(r.Log.Events, e => e.Name == "GameExited"); Assert.Contains(r.Log.Events, e => e.Name == "HdrRestorePending");
        r.Reconnect(); await r.Tick(6, false); Assert.Single(r.Hdr.Writes); await r.Tick(7, false); r.Restored();
    }
    [Fact] public async Task UserReportedTimelineRestoresOffInsteadOfStartingNextSessionWithOn()
    {
        var r = new Rig(); r.Settings.EnableDelaySeconds = 1; await r.Tick(0); await r.Tick(1); r.Disconnect(); await r.Tick(3348);
        r.Reconnect(); await r.Tick(3353); await r.Tick(3354);
        await r.Tick(14435, false); r.Restored(); await r.Tick(14493);
        Assert.False(r.Journal.Load()!.Original);
    }
    [Fact] public async Task DetachAfterExitDuringRestoreDelayKeepsPendingState()
    {
        var r = new Rig(); r.Settings.RestoreDelaySeconds = 3; await r.Tick(0); await r.Tick(1, false);
        r.Disconnect(); await r.Tick(2, false); r.Reconnect(); await r.Tick(7, false); await r.Tick(8, false); r.Restored();
    }
    [Fact] public async Task NewGameDuringPendingRestoreKeepsSameOriginal()
    {
        var r = new Rig(); await r.StartAndDisconnect(); await r.Tick(2, false); Guid id = r.Journal.Load()!.Id;
        await r.Tick(3); Assert.Equal(id, r.Journal.Load()!.Id); Assert.False(r.Journal.Load()!.Original);
        r.Reconnect(); await r.Tick(6); await r.Tick(7); Assert.Single(r.Hdr.Writes); await r.Tick(8, false); r.Restored();
    }
    [Fact] public async Task ObservedManualOffThenOnNeverReacquiresOwnership()
    {
        var r = new Rig(); await r.Tick(0); r.Hdr.Displays[0] = r.Hdr.Displays[0] with { Enabled = false, Active = false };
        await r.Tick(1); r.Hdr.Displays[0] = r.Hdr.Displays[0] with { Enabled = true, Active = true };
        r.Disconnect(); await r.Tick(2); r.Reconnect(); await r.Tick(10); await r.Tick(11, false);
        Assert.Single(r.Hdr.Writes); Assert.True(r.Hdr.Displays[0].Enabled); Assert.Null(r.Journal.Load());
    }
    [Fact] public async Task DifferentHdrOnReconnectIsPreservedAsExternalOverride()
    {
        var r = new Rig(); await r.StartAndDisconnect(); r.Detached = r.Detached! with { Enabled = false, Active = false }; r.Reconnect();
        await r.Tick(6); Assert.True(r.Engine.ManualOverride); r.Hdr.Displays[0] = r.Hdr.Displays[0] with { Enabled = true, Active = true };
        await r.Tick(7, false); Assert.Single(r.Hdr.Writes);
    }
    [Fact] public async Task ManualChangeBetweenReconnectConfirmationsPreventsRestore()
    {
        var r = new Rig(); await r.StartAndDisconnect(); await r.Tick(2, false); r.Reconnect(); await r.Tick(6, false);
        r.Hdr.Displays[0] = r.Hdr.Displays[0] with { Enabled = false, Active = false }; await r.Tick(7, false);
        Assert.Single(r.Hdr.Writes); Assert.Null(r.Journal.Load());
    }
    [Fact] public async Task ChangedPathWithUniqueHardwareIdentityCanResume()
    {
        var r = new Rig(); r.Hdr.Displays[0] = r.Hdr.Displays[0] with { HardwareId = "unique-serial" };
        await r.StartAndDisconnect(); r.Detached = r.Detached! with { Id = "path-B" }; r.Reconnect();
        await r.Tick(6); await r.Tick(7); await r.Tick(8, false);
        Assert.Equal(new[] { ("path-A", true), ("path-B", false) }, r.Hdr.Writes); Assert.Null(r.Journal.Load());
    }
    [Fact] public async Task SameModelAndEdidOnDifferentPathWithoutSerialNeverGetsWritten()
    {
        var r = new Rig(); await r.StartAndDisconnect(); r.Detached = r.Detached! with { Id = "path-B" }; r.Reconnect();
        await r.Tick(2, false); await r.Tick(6, false); await r.Tick(7, false);
        Assert.NotNull(r.Journal.Load()); Assert.Single(r.Hdr.Writes); await r.Tick(603, false);
        Assert.Null(r.Journal.Load()); Assert.Contains(r.Log.Events, e => e.Name == "HdrRestoreAbandoned");
    }
    [Theory] [InlineData(null)] [InlineData("another-edid")]
    public async Task SamePathWithMissingOrDifferentEdidDoesNotRestore(string? fingerprint)
    {
        var r = new Rig(); await r.StartAndDisconnect(); r.Detached = r.Detached! with { Fingerprint = fingerprint }; r.Reconnect();
        await r.Tick(6); await r.Tick(7, false); Assert.Single(r.Hdr.Writes); Assert.Null(r.Journal.Load());
    }
    [Fact] public async Task MissingInitialIdentityDoesNotBecomeTrustedOnReconnect()
    {
        var r = new Rig(); r.Hdr.Displays[0] = r.Hdr.Displays[0] with { Fingerprint = null }; await r.StartAndDisconnect();
        r.Detached = r.Detached! with { Fingerprint = "edid-A" }; r.Reconnect(); await r.Tick(6); await r.Tick(7, false); Assert.Single(r.Hdr.Writes);
    }
    [Fact] public async Task DuplicateHardwareIdentityIsAmbiguousEvenWithOriginalPath()
    {
        var r = new Rig(); r.Hdr.Displays[0] = r.Hdr.Displays[0] with { HardwareId = "duplicate" }; await r.StartAndDisconnect(); r.Reconnect();
        r.Hdr.Displays.Add(r.Detached! with { Id = "other-port" }); await r.Tick(6); await r.Tick(7, false); Assert.Single(r.Hdr.Writes);
    }
    [Fact] public async Task AnotherMonitorIsNeverFallbackTarget()
    {
        var r = new Rig(); await r.StartAndDisconnect(); r.Hdr.Displays.Add(new("other", "Other", true, true, true, true, 0) { Fingerprint = "different" });
        await r.Tick(2, false); await r.Tick(6, false); r.Reconnect(); await r.Tick(11, false); await r.Tick(12, false);
        Assert.Equal(new[] { ("path-A", true), ("path-A", false) }, r.Hdr.Writes); Assert.True(r.Hdr.Displays.Single(d => d.Id == "other").Enabled);
    }
    [Fact] public async Task RepeatedTopologyErrorsExpireWithoutWritesOrJournalLossBeforeDeadline()
    {
        var r = new Rig(); await r.StartAndDisconnect(); await r.Tick(2, false); r.Hdr.FailQuery = true;
        for (int t = 3; t < 600; t++) await r.Tick(t, false);
        Assert.NotNull(r.Journal.Load()); Assert.InRange(r.Hdr.Queries, 1, HdrEngine.MaxRestoreProbes + 1);
        await r.Tick(603, false); Assert.Null(r.Journal.Load()); Assert.Single(r.Hdr.Writes);
    }
    [Fact] public async Task ReadFailureWithSameVerifiedDisplayCanRecover()
    {
        var r = new Rig(); await r.Tick(0); r.Hdr.FailRead = true; await r.Tick(1); r.Hdr.FailRead = false;
        await r.Tick(6); await r.Tick(7); await r.Tick(8, false); r.Restored();
    }
    [Fact] public async Task RestoreRetriesRemainBoundedAfterReconnection()
    {
        var r = new Rig(); await r.StartAndDisconnect(); await r.Tick(2, false); r.Reconnect(); r.Hdr.FailWrite = true;
        for (int t = 6; t < 60; t++) await r.Tick(t, false);
        Assert.Equal(4, r.Hdr.Writes.Count); Assert.NotNull(r.Journal.Load());
        Assert.Contains(r.Log.Events, e => e.Name == "HdrRestoreRetriesExhausted");
    }
    [Fact] public async Task PauseWhileDisconnectedDefersRestore()
    {
        var r = new Rig(); await r.StartAndDisconnect(); r.Settings.Monitoring = false; await r.Tick(2); r.Reconnect();
        await r.Tick(6); await r.Tick(7); r.Restored(); Assert.Equal(HdrState.Suspended, r.Engine.State);
    }
    [Fact] public async Task ShutdownWhileDisconnectedKeepsJournalAndRestartDoesNotAssumeOwnership()
    {
        var r = new Rig(); await r.StartAndDisconnect(); await r.Engine.ShutdownAsync();
        Assert.NotNull(r.Journal.Load()); Assert.NotNull(r.Journal.Load()!.RestoreDeadline);
        r.Reconnect(); var restarted = new HdrEngine(r.Hdr, r.Journal, r.Log, r.Log, r.Settings);
        await restarted.TickAsync([], [r.Game], DateTimeOffset.UtcNow); Assert.Single(r.Hdr.Writes);
        Assert.Contains(r.Log.Events, e => e.Name == "CrashRecovery");
    }
    [Fact] public async Task OriginalOnIsNeverTurnedOffAfterReconnect()
    {
        var r = new Rig(); r.Hdr.Displays[0] = r.Hdr.Displays[0] with { Enabled = true, Active = true };
        await r.Tick(0); r.Disconnect(); await r.Tick(1); await r.Tick(2, false); r.Reconnect();
        await r.Tick(6, false); await r.Tick(7, false); Assert.Empty(r.Hdr.Writes); Assert.Null(r.Journal.Load());
    }
    [Fact] public async Task DisconnectDuringEnableDelayDoesNotEnableAfterGameAlreadyExited()
    {
        var r = new Rig(); r.Settings.EnableDelaySeconds = 3; await r.Tick(0); r.Disconnect(); await r.Tick(1);
        await r.Tick(2, false); r.Reconnect(); await r.Tick(6, false); await r.Tick(7, false);
        Assert.Empty(r.Hdr.Writes); Assert.Null(r.Journal.Load());
    }
    [Fact] public async Task ASecondDetachRequiresNewStableConfirmations()
    {
        var r = new Rig(); await r.StartAndDisconnect(); await r.Tick(2, false); r.Reconnect(); await r.Tick(6, false);
        r.Disconnect(); await r.Tick(7, false); r.Reconnect(); await r.Tick(12, false);
        Assert.Single(r.Hdr.Writes); await r.Tick(13, false); r.Restored();
    }
    [Fact] public async Task InactiveHdrWaitsForStableActiveMode()
    {
        var r = new Rig(); await r.StartAndDisconnect(); await r.Tick(2, false); r.Detached = r.Detached! with { Active = false }; r.Reconnect();
        await r.Tick(6, false); Assert.Single(r.Hdr.Writes); r.Hdr.Displays[0] = r.Hdr.Displays[0] with { Active = true };
        await r.Tick(11, false); Assert.Single(r.Hdr.Writes); await r.Tick(12, false); r.Restored();
    }
    [Fact] public async Task FlappingDoesNotExtendPendingDeadline()
    {
        var r = new Rig(); await r.StartAndDisconnect(); await r.Tick(2, false); var deadline = r.Journal.Load()!.RestoreDeadline;
        for (int t = 6; t < 590; t += 10)
        {
            r.Reconnect(); await r.Tick(t, false); r.Disconnect(); await r.Tick(t + 1, false);
            Assert.Equal(deadline, r.Journal.Load()!.RestoreDeadline);
        }
        await r.Tick(603, false); Assert.Null(r.Journal.Load()); Assert.Single(r.Hdr.Writes);
    }
    [Fact] public async Task DuplicateDevicePathDoesNotAllowRestoration()
    {
        var r = new Rig(); await r.StartAndDisconnect(); r.Reconnect(); r.Hdr.Displays.Add(r.Detached!);
        await r.Tick(6); await r.Tick(7, false); Assert.Single(r.Hdr.Writes);
    }
}
