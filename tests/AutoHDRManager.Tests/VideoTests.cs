using AutoHDRManager.Core;
using Xunit;

namespace AutoHDRManager.Tests;

public sealed class VideoTests
{
    private sealed class Hdr : IHdrController
    {
        public bool Enabled, Missing, FailWrite;
        public List<bool> Writes = [];
        public Display Display => new("screen", "test", true, Enabled, Enabled, true, 0) { Fingerprint = "edid" };
        public Task<IReadOnlyList<Display>> GetDisplaysAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<Display>>(Missing ? [] : [Display]);
        public Task<Display> ReadAsync(string id, CancellationToken token = default) => Missing ? throw new IOException("disconnected") : Task.FromResult(Display);
        public Task SetAsync(string id, bool enabled, CancellationToken token = default) { if (FailWrite) throw new IOException("write"); Enabled = enabled; Writes.Add(enabled); return Task.CompletedTask; }
    }
    private sealed class Journal : ISessionRepository { public Session? Value; public Session? Load() => Value; public void Save(Session value) => Value = value; public void Clear() => Value = null; }
    private sealed class Sink : IEventLog, INotificationService { public List<string> Events = []; public void Write(string name, object? details = null) => Events.Add(name); public void Show(string title, string message) { } }
    private sealed class Monitor : IGameProcessMonitor { public IReadOnlyList<GameProcess> Values = []; public Task<IReadOnlyList<GameProcess>> SnapshotAsync(CancellationToken token = default) => Task.FromResult(Values); }
    private sealed class Probe : IVideoProbe { public VideoKind Kind = VideoKind.Hdr10; public bool Fail; public int Calls; public Task<VideoMetadata> ReadAsync(string path, CancellationToken token) { Calls++; return Fail ? throw new IOException("probe") : Task.FromResult(new VideoMetadata(Kind, "test")); } }
    private sealed class Player : IVideoPlayer
    {
        public bool Missing, Fail; public int Starts; public Action? OnStart; public List<Child> Children = [];
        public string? Find(string? configuredPath) => Missing ? null : "vlc.exe";
        public IVideoProcess Start(string executable, string path) { Starts++; if (Fail) throw new VideoPlayerStartException(new IOException()); OnStart?.Invoke(); var child = new Child(Starts); Children.Add(child); return child; }
    }
    private sealed class Child(int id) : IVideoProcess { public ProcessIdentity Identity => new(id, id); public bool HasExited { get; set; } public bool Disposed; public void Dispose() => Disposed = true; }
    private sealed class Rig
    {
        public Hdr Hdr = new(); public Journal Journal = new(); public Sink Log = new(); public Monitor Monitor = new(); public Probe Probe = new(); public Player Player = new();
        public Settings Settings = new() { SmartVideoHdr = true, RestoreDelaySeconds = 0, EnableDelaySeconds = 0 };
        public HdrEngine Engine; public VideoLaunchService Video; public Game Game = new() { ExecutablePath = Path.GetFullPath("game.exe") };
        public bool Waited;
        public Rig(Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            Engine = new(Hdr, Journal, Log, Log, Settings);
            Video = new(Engine, Monitor, () => [Game], Settings, Probe, Player, Log, Log, delay ?? ((span, _) => { Assert.Equal(TimeSpan.FromSeconds(1), span); Assert.True(Hdr.Enabled); Waited = true; return Task.CompletedTask; }));
        }
        public Task Open(string name = "movie.mkv") => Video.OpenAsync(Path.GetFullPath(name));
        public Task Tick(DateTimeOffset? now = null) => Engine.TickAsync(Monitor.Values, [Game], now ?? DateTimeOffset.UtcNow);
        public async Task Close() { Player.Children[^1].HasExited = true; await Tick(); await Video.ReapAsync(); }
    }
    [Theory][InlineData(VideoKind.Hdr10)][InlineData(VideoKind.Hlg)]
    public async Task HdrVideoConfirmsWaitsLaunchesAndRestores(VideoKind kind)
    {
        var r = new Rig(); r.Probe.Kind = kind; r.Player.OnStart = () => { Assert.True(r.Waited); Assert.True(r.Hdr.Enabled); };
        await r.Open(); Assert.True(r.Engine.HasSession); await r.Tick(); Assert.Single(r.Hdr.Writes);
        await r.Close(); Assert.Equal(new[] { true, false }, r.Hdr.Writes); Assert.Null(r.Journal.Value);
    }
    [Theory][InlineData(VideoKind.Sdr)][InlineData(VideoKind.Unknown)][InlineData(VideoKind.UnsupportedDolbyVision)]
    public async Task NonHdrOrUncertainMetadataLaunchesWithoutWrites(VideoKind kind) { var r = new Rig(); r.Probe.Kind = kind; await r.Open(); await r.Close(); Assert.Empty(r.Hdr.Writes); Assert.Equal(1, r.Player.Starts); Assert.False(r.Waited); }
    [Fact] public async Task OriginalOnStaysOn() { var r = new Rig(); r.Hdr.Enabled = true; await r.Open(); await r.Close(); Assert.True(r.Hdr.Enabled); Assert.Empty(r.Hdr.Writes); }
    [Fact] public async Task ManualChangeRetainsPriority() { var r = new Rig(); await r.Open(); r.Hdr.Enabled = false; await r.Tick(); r.Hdr.Enabled = true; await r.Close(); Assert.True(r.Hdr.Enabled); Assert.Single(r.Hdr.Writes); }
    [Fact] public async Task ProbeErrorStillOpens() { var r = new Rig(); r.Probe.Fail = true; await r.Open(); Assert.Equal(1, r.Player.Starts); Assert.Empty(r.Hdr.Writes); }
    [Fact] public async Task VlcMissingDoesNotTouchHdr() { var r = new Rig(); r.Player.Missing = true; var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => r.Open()); Assert.Equal("VideoVlcMissing", ex.Message); Assert.Empty(r.Hdr.Writes); Assert.Equal(0, r.Probe.Calls); }
    [Fact] public async Task PlayerFailureRestoresAndIsNotRetried() { var r = new Rig(); r.Player.Fail = true; await Assert.ThrowsAsync<VideoPlayerStartException>(() => r.Open()); Assert.Equal(1, r.Player.Starts); Assert.Equal(new[] { true, false }, r.Hdr.Writes); Assert.Null(r.Journal.Value); }
    [Fact] public async Task HdrFailureStillOpensOnce() { var r = new Rig(); r.Hdr.FailWrite = true; await r.Open(); Assert.Equal(1, r.Player.Starts); Assert.Contains("VideoHdrFallback", r.Log.Events); }
    [Fact] public async Task FastDoubleOpenStartsOneProcess() { var r = new Rig(); await Task.WhenAll(r.Open(), r.Open()); Assert.Equal(1, r.Player.Starts); await r.Close(); Assert.False(r.Hdr.Enabled); }
    [Fact] public async Task ReopenAfterCloseStartsNewSession() { var r = new Rig(); await r.Open(); await r.Close(); await r.Open(); await r.Close(); Assert.Equal(new[] { true, false, true, false }, r.Hdr.Writes); }
    [Fact] public async Task AnotherControlledVlcKeepsHdrOn() { var r = new Rig(); await r.Open(); await r.Open("second.mp4"); await r.Close(); Assert.True(r.Hdr.Enabled); r.Player.Children[0].HasExited = true; await r.Video.ReapAsync(); Assert.False(r.Hdr.Enabled); }
    [Fact] public async Task UnrelatedExistingVlcDoesNotOwnOrBlockSession() { var r = new Rig(); r.Monitor.Values = [new(new(900, 800), Path.GetFullPath("vlc.exe"), "vlc")]; await r.Open(); await r.Close(); Assert.Equal(new[] { true, false }, r.Hdr.Writes); Assert.Equal(1, r.Player.Starts); }
    [Fact] public async Task VideoExitCannotRestoreWhileGameIsRunning()
    {
        var r = new Rig(); await r.Open(); r.Monitor.Values = [new(new(99, 100), r.Game.ExecutablePath, "game")]; await r.Tick(); await r.Close(); Assert.True(r.Hdr.Enabled);
        r.Monitor.Values = []; await r.Tick(); Assert.Equal(new[] { true, false }, r.Hdr.Writes);
    }
    [Fact] public async Task GameExitCannotRestoreWhileVideoIsRunning()
    {
        var r = new Rig(); r.Monitor.Values = [new(new(99, 100), r.Game.ExecutablePath, "game")]; await r.Tick(); await r.Open(); r.Monitor.Values = []; await r.Tick(); Assert.True(r.Hdr.Enabled); await r.Close(); Assert.Equal(new[] { true, false }, r.Hdr.Writes);
    }
    [Fact] public async Task CancelGameLaunchCannotReleaseVideoDemand() { var r = new Rig(); await r.Open(); await r.Engine.CancelLaunchAsync(); Assert.True(r.Hdr.Enabled); await r.Close(); Assert.False(r.Hdr.Enabled); }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task MonitorReconnectDuringOrAfterVideoUsesExistingRecovery(bool closeWhileMissing)
    {
        var r = new Rig(); await r.Open(); r.Hdr.Missing = true; var now = DateTimeOffset.UtcNow; await r.Tick(now);
        if (closeWhileMissing) await r.Close();
        Assert.False(r.Engine.ManualOverride); Assert.False(r.Journal.Value!.Original);
        r.Hdr.Missing = false; await r.Tick(now.AddSeconds(6)); await r.Tick(now.AddSeconds(7));
        if (!closeWhileMissing) await r.Close();
        Assert.Equal(new[] { true, false }, r.Hdr.Writes); Assert.Null(r.Journal.Value);
    }
    [Fact] public async Task DisabledByDefaultAndPassthrough() { Assert.False(new Settings().SmartVideoHdr); var r = new Rig(); r.Settings.SmartVideoHdr = false; await r.Open(); await r.Close(); Assert.Empty(r.Hdr.Writes); Assert.Equal(0, r.Probe.Calls); }
    [Fact] public async Task DisablingFeatureDoesNotAbandonExistingVideoSession() { var r = new Rig(); await r.Open(); r.Settings.SmartVideoHdr = false; await r.Tick(); Assert.True(r.Hdr.Enabled); await r.Close(); Assert.False(r.Hdr.Enabled); }
    [Fact] public async Task PauseDuringPreparationFallsBackWithoutBlockingMovie()
    {
        Rig? r = null; r = new Rig((_, _) => { r!.Settings.Monitoring = false; return Task.CompletedTask; });
        await r.Open(); Assert.Equal(1, r.Player.Starts); Assert.Equal(new[] { true, false }, r.Hdr.Writes);
    }
    [Fact] public async Task CancellationBeforeDispatchReleasesDemand()
    {
        using var cts = new CancellationTokenSource(); var r = new Rig((_, token) => { cts.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => r.Video.OpenAsync(Path.GetFullPath("movie.mkv"), cts.Token));
        Assert.Equal(0, r.Player.Starts); Assert.Equal(new[] { true, false }, r.Hdr.Writes);
    }
    [Theory]
    [InlineData("PQ", "BT.2020", "10", "", "", VideoKind.Hdr10)]
    [InlineData("HLG", "BT.2020", "10", "", "", VideoKind.Hlg)]
    [InlineData("BT.709", "BT.709", "8", "", "", VideoKind.Sdr)]
    [InlineData("", "", "", "", "", VideoKind.Unknown)]
    [InlineData("PQ", "BT.709", "10", "", "", VideoKind.Unknown)]
    [InlineData("PQ", "BT.2020", "8", "", "", VideoKind.Unknown)]
    [InlineData("PQ", "BT.2020", "10", "Dolby Vision", "", VideoKind.UnsupportedDolbyVision)]
    [InlineData("PQ", "BT.2020", "10", "Dolby Vision", "HDR10", VideoKind.Hdr10)]
    [InlineData("PQ / BT.709", "BT.2020", "10", "", "", VideoKind.Unknown)]
    public void MetadataClassificationIsConservative(string transfer, string primary, string depth, string format, string compatibility, VideoKind expected)
        => Assert.Equal(expected, VideoClassification.Classify(transfer, primary, depth, format, compatibility).Kind);
    [Fact] public void VideoActivationIsBackgroundAndDoesNotLoseUnicode()
    {
        string file = Path.GetFullPath("фильм с пробелами.mkv"); var request = LaunchRequest.Parse(["--open-video", file]); Assert.True(request.Background); Assert.Equal(file, request.VideoPath); Assert.Null(request.GameId);
    }
    [Fact] public void MixedOrIncompleteActivationIsRejected()
    {
        Assert.Throws<ArgumentException>(() => LaunchRequest.Parse(["--open-video"]));
        Assert.Throws<ArgumentException>(() => LaunchRequest.Parse(["--open-video", Path.GetFullPath("movie.mp4"), "--launch-game", Guid.NewGuid().ToString()]));
        Assert.Throws<ArgumentException>(() => VideoPath.Validate("https://example.org/movie.mp4"));
    }
}
