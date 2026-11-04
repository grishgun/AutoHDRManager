using AutoHDRManager.Core;
using Xunit;

namespace AutoHDRManager.Tests;

public sealed class LaunchTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "HdrLaunch-" + Guid.NewGuid().ToString("N"));
    private sealed class Hdr : IHdrController
    {
        public bool Enabled, Active = true, Fail;
        public List<bool> Writes = [];
        public Display Display => new("display", "test", true, Enabled, Enabled && Active, true, 0);
        public Task<IReadOnlyList<Display>> GetDisplaysAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<Display>>([Display]);
        public Task<Display> ReadAsync(string id, CancellationToken token = default) => Task.FromResult(Display);
        public Task SetAsync(string id, bool value, CancellationToken token = default) { if (Fail) throw new IOException("fail"); Writes.Add(value); Enabled = value; return Task.CompletedTask; }
    }
    private sealed class Journal : ISessionRepository { public Session? Saved; public Session? Load() => Saved; public void Save(Session s) => Saved = s; public void Clear() => Saved = null; }
    private sealed class Sink : IEventLog, INotificationService { public void Write(string n, object? d = null) { } public void Show(string t, string m) { } }
    private sealed class Monitor : IGameProcessMonitor { public IReadOnlyList<GameProcess> Processes = []; public Task<IReadOnlyList<GameProcess>> SnapshotAsync(CancellationToken token = default) => Task.FromResult(Processes); }
    private readonly Game game;
    private readonly Hdr hdr = new(); private readonly Journal journal = new(); private readonly Monitor monitor = new(); private readonly Settings settings = new();
    private readonly HdrEngine engine;
    public LaunchTests() { Directory.CreateDirectory(root); var exe = Path.Combine(root, "game.exe"); File.WriteAllText(exe, "test"); game = new() { ExecutablePath = exe }; var sink = new Sink(); engine = new(hdr, journal, sink, sink, settings); }
    private GameLaunchService Service(Action<Game> launch, Func<TimeSpan, CancellationToken, Task>? delay = null) => new(engine, monitor, () => new[] { game }, launch, new Sink(), delay ?? ((_, _) => Task.CompletedTask));
    [Fact] public async Task ColdLaunchConfirmsHdrThenWaitsOneSecondThenDispatchesAndRestores()
    {
        bool waited = false, launched = false;
        var service = Service(_ => { Assert.True(waited); Assert.True(hdr.Enabled); launched = true; }, (duration, _) => { Assert.Equal(TimeSpan.FromSeconds(1), duration); Assert.True(hdr.Enabled); waited = true; return Task.CompletedTask; });
        await service.LaunchAsync(game.Id); Assert.True(launched);
        monitor.Processes = [new(new(123, 456), game.ExecutablePath, "game")];
        await engine.TickAsync(monitor.Processes, [game], DateTimeOffset.UtcNow);
        await engine.TickAsync([], [game], DateTimeOffset.UtcNow);
        await engine.TickAsync([], [game], DateTimeOffset.UtcNow.AddSeconds(3));
        Assert.Equal(new[] { true, false }, hdr.Writes); Assert.Null(journal.Saved);
    }
    [Fact] public async Task OnButInactiveHdrNeverLaunches() { hdr.Enabled = true; hdr.Active = false; await Assert.ThrowsAsync<InvalidOperationException>(() => Service(_ => Assert.Fail("launched")).LaunchAsync(game.Id)); }
    [Fact] public async Task EnableFailureNeverLaunches() { hdr.Fail = true; await Assert.ThrowsAsync<IOException>(() => Service(_ => Assert.Fail("launched")).LaunchAsync(game.Id)); }
    [Fact] public async Task ManualOffDuringStabilizationNeverLaunchesOrOverridesUser()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(_ => Assert.Fail("launched"), (_, _) => { hdr.Enabled = false; return Task.CompletedTask; }).LaunchAsync(game.Id));
        Assert.Equal(new[] { true }, hdr.Writes);
    }
    [Fact] public async Task PauseDuringStabilizationAbortsAndRestores()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(_ => Assert.Fail("launched"), (_, _) => { settings.Monitoring = false; return Task.CompletedTask; }).LaunchAsync(game.Id));
        Assert.Equal(new[] { true, false }, hdr.Writes);
    }
    [Fact] public async Task FailedLauncherRestoresOriginalState() { await Assert.ThrowsAsync<IOException>(() => Service(_ => throw new IOException("missing launcher")).LaunchAsync(game.Id)); Assert.False(hdr.Enabled); Assert.Null(journal.Saved); }
    [Fact] public async Task UnknownIdDoesNotTouchHdr() { await Assert.ThrowsAsync<InvalidOperationException>(() => Service(_ => Assert.Fail("launched")).LaunchAsync(Guid.NewGuid())); Assert.Empty(hdr.Writes); }
    [Fact] public async Task DoubleClickWhileProcessIsStartingDispatchesOnce() { int count = 0; var service = Service(_ => count++); await Task.WhenAll(service.LaunchAsync(game.Id), service.LaunchAsync(game.Id)); Assert.Equal(1, count); }
    [Fact] public async Task AlreadyRunningGameIsNotDispatchedAgain() { monitor.Processes = [new(new(123, 456), game.ExecutablePath, "game")]; await Service(_ => Assert.Fail("launched")).LaunchAsync(game.Id); }
    [Fact] public async Task OriginalHdrOnRemainsOn() { hdr.Enabled = true; await Service(_ => { }).LaunchAsync(game.Id); await engine.CancelLaunchAsync(); Assert.True(hdr.Enabled); Assert.Empty(hdr.Writes); }
    [Fact] public async Task CancellationDuringStabilizationRestores()
    {
        using var cts = new CancellationTokenSource();
        var service = Service(_ => Assert.Fail("launched"), (_, token) => { cts.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LaunchAsync(game.Id, cts.Token)); Assert.False(hdr.Enabled);
    }
    [Fact] public void CommandLineLaunchIsAlwaysBackground() { var request = LaunchRequest.Parse(["--launch-game", game.Id.ToString("D")]); Assert.Equal(game.Id, request.GameId); Assert.True(request.Background); Assert.False(LaunchRequest.Parse([]).Background); }
    [Theory] [InlineData("bad")] [InlineData("00000000-0000-0000-0000-000000000000")] [InlineData("; calc.exe")]
    public void InvalidIdRejected(string id) => Assert.Throws<ArgumentException>(() => LaunchRequest.Parse(["--launch-game", id]));
    [Fact] public void MissingOrRepeatedLaunchRejected() { Assert.Throws<ArgumentException>(() => LaunchRequest.Parse(["--launch-game"])); Assert.Throws<ArgumentException>(() => LaunchRequest.Parse(["--launch-game", game.Id.ToString(), "--launch-game", game.Id.ToString()])); }
    public void Dispose() => Directory.Delete(root, true);
}
