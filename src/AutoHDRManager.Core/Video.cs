namespace AutoHDRManager.Core;

public enum VideoKind { Unknown, Sdr, Hdr10, Hlg, UnsupportedDolbyVision }
public sealed record VideoMetadata(VideoKind Kind, string Reason, string Transfer = "", string Primaries = "", string Depth = "", string HdrFormat = "");
public interface IVideoProbe { Task<VideoMetadata> ReadAsync(string path, CancellationToken token); }
public interface IVideoPlayer
{
    string? Find(string? configuredPath);
    IVideoProcess Start(string executable, string path);
}
public interface IVideoProcess : IDisposable { ProcessIdentity Identity { get; } bool HasExited { get; } }

public static class VideoPath
{
    public static string Validate(string path)
    {
        // No existence check here: an unavailable UNC share must not block the UI or IPC thread.
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32760 || !Path.IsPathFullyQualified(path) ||
            path.IndexOfAny(['\0', '\r', '\n', '"', '*', '?']) >= 0 || path.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            path.AsSpan(Math.Min(2, path.Length)).Contains(':')) throw new ArgumentException("VideoInvalidPath");
        path = Path.GetFullPath(path);
        if (!new[] { ".mkv", ".mp4" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("VideoInvalidPath");
        return path;
    }
}

public static class VideoClassification
{
    public static VideoMetadata Classify(string transfer, string primaries, string depth, string hdrFormat, string compatibility)
    {
        bool dv = hdrFormat.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase);
        if (dv && !compatibility.Contains("HDR10", StringComparison.OrdinalIgnoreCase) && !compatibility.Contains("HLG", StringComparison.OrdinalIgnoreCase))
            return new(VideoKind.UnsupportedDolbyVision, "DolbyVisionWithoutCompatibleBaseLayer", transfer, primaries, depth, hdrFormat);
        var kind = VideoKind.Unknown;
        if (primaries == "BT.2020" && int.TryParse(depth, out int bits) && bits >= 10)
        {
            if (transfer is "PQ" or "SMPTE ST 2084") kind = VideoKind.Hdr10;
            if (transfer is "HLG" or "ARIB STD-B67") kind = VideoKind.Hlg;
        }
        // Missing, mixed or unrecognized transfer information is never labelled confirmed SDR.
        if (!dv && transfer is "BT.709" or "BT.601" or "sRGB" or "BT.470 System M" or "BT.470 System B, BT.470 System G") kind = VideoKind.Sdr;
        return new(kind, kind == VideoKind.Unknown ? "InsufficientOrConflictingMetadata" : "ExplicitTransferMetadata", transfer, primaries, depth, hdrFormat);
    }
}

/// <summary>Serializes open requests; process reaping is called by the existing runtime loop, never a second HDR loop.</summary>
public sealed class VideoLaunchService(HdrEngine engine, IGameProcessMonitor monitor, Func<IReadOnlyList<Game>> games,
    Settings settings, IVideoProbe probe, IVideoPlayer player, IEventLog log, INotificationService notifications,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<Guid, (string Path, IVideoProcess Process)> running = [];
    public async Task OpenAsync(string file, CancellationToken token = default)
    {
        file = VideoPath.Validate(file);
        await gate.WaitAsync(token);
        Guid request = Guid.NewGuid(); bool demanded = false; IVideoProcess? process = null;
        try
        {
            if (running.Count > 0)
                await engine.TickAsync(await monitor.SnapshotAsync(token), games(), DateTimeOffset.UtcNow, token);
            await ReapCoreAsync();
            if (running.Values.Any(p => StringComparer.OrdinalIgnoreCase.Equals(p.Path, file)))
            { log.Write("DuplicateVideoIgnored"); return; }
            string executable = player.Find(settings.VlcPath) ?? throw new InvalidOperationException("VideoVlcMissing");
            var metadata = new VideoMetadata(VideoKind.Unknown, "FeatureDisabled");
            if (settings.SmartVideoHdr)
            {
                try { metadata = await probe.ReadAsync(file, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) { metadata = new(VideoKind.Unknown, "ProbeFailed"); log.Write("VideoProbeFailed", new { Error = ex.GetType().Name }); }
            }
            log.Write("VideoMetadata", new { Kind = metadata.Kind.ToString(), metadata.Reason, metadata.Transfer, metadata.Primaries, metadata.Depth, metadata.HdrFormat }); // No movie name/path or viewing history is persisted.
            if (settings.SmartVideoHdr && metadata.Kind is VideoKind.Hdr10 or VideoKind.Hlg)
            {
                try
                {
                    await engine.TickAsync(await monitor.SnapshotAsync(token), games(), DateTimeOffset.UtcNow, token);
                    demanded = true; await engine.PrepareVideoAsync(request, token);
                    await (delay ?? Task.Delay)(TimeSpan.FromSeconds(1), token);
                    await engine.DispatchVideoAsync(request, () => (process = player.Start(executable, file)).Identity, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) when (process == null)
                {
                    // A player start failure is not retried; HDR failure must not prevent playback.
                    if (ex is VideoPlayerStartException) throw;
                    try { await engine.ReleaseVideoAsync(request); }
                    catch (Exception restoreError) { log.Write("VideoRestoreFailed", new { Error = restoreError.GetType().Name }); }
                    demanded = false;
                    log.Write("VideoHdrFallback", new { Error = ex.GetType().Name });
                    if (settings.Notifications) notifications.Show(Localization.Get("VideoTitle"), Localization.Get("VideoHdrFallback"));
                }
            }
            token.ThrowIfCancellationRequested();
            process ??= player.Start(executable, file);
            running.Add(request, (file, process));
            log.Write("VideoStarted", new { RequestId = request, process.Identity.Pid, HdrDemand = demanded });
        }
        catch
        {
            if (demanded && process == null) await engine.ReleaseVideoAsync(request);
            // Never terminate a player, even if a later bookkeeping operation fails.
            if (process != null && !running.ContainsKey(request)) running.Add(request, (file, process));
            throw;
        }
        finally { gate.Release(); }
    }
    public async Task ReapAsync()
    {
        if (!await gate.WaitAsync(0)) return;
        try { await ReapCoreAsync(); } finally { gate.Release(); }
    }
    private async Task ReapCoreAsync()
    {
        foreach (var item in running.ToArray())
        {
            if (!item.Value.Process.HasExited) continue;
            await engine.ReleaseVideoAsync(item.Key);
            item.Value.Process.Dispose(); running.Remove(item.Key);
            log.Write("VideoExited", new { RequestId = item.Key });
        }
    }
    public async Task DrainAsync()
    {
        await gate.WaitAsync();
        try { foreach (var entry in running.Values) entry.Process.Dispose(); running.Clear(); }
        finally { gate.Release(); }
    }
}
public sealed class VideoPlayerStartException(Exception inner) : IOException("VideoVlcStartFailed", inner);
