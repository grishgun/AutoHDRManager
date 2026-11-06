using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using AutoHDRManager.Core;

namespace AutoHDRManager.Windows;

/// <summary>Bounded metadata reads. No decoder, network fetching, playlists or full-file scan.</summary>
public static class MediaInfoMetadata
{
    public const int MaxReadBytes = 32 * 1024 * 1024;
    public static VideoMetadata Read(string file)
    {
        VideoPath.Validate(file);
        nint handle = MediaInfo_New();
        if (handle == 0) return new(VideoKind.Unknown, "ParserUnavailable");
        try
        {
            MediaInfo_Option(handle, "ParseSpeed", "0");
            MediaInfo_Option(handle, "Language", "raw");
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            MediaInfo_Open_Buffer_Init(handle, (ulong)stream.Length, 0);
            byte[] buffer = new byte[64 * 1024]; int total = 0; bool complete = false;
            var clock = Stopwatch.StartNew();
            while (total < MaxReadBytes && clock.Elapsed < TimeSpan.FromSeconds(5))
            {
                int count = stream.Read(buffer, 0, Math.Min(buffer.Length, MaxReadBytes - total));
                if (count == 0) { complete = true; break; }
                total += count;
                nuint status = MediaInfo_Open_Buffer_Continue(handle, buffer, (nuint)count);
                if ((status & 8) != 0) { complete = true; break; }
                ulong seek = MediaInfo_Open_Buffer_Continue_GoTo_Get(handle);
                if (seek != ulong.MaxValue)
                {
                    if (seek > (ulong)stream.Length) return new(VideoKind.Unknown, "InvalidParserSeek");
                    stream.Position = (long)seek; MediaInfo_Open_Buffer_Init(handle, (ulong)stream.Length, seek);
                }
            }
            MediaInfo_Open_Buffer_Finalize(handle);
            if (!complete) return new(VideoKind.Unknown, "MetadataReadBudgetExceeded");
            int countVideo = checked((int)MediaInfo_Count_Get(handle, 1, nuint.MaxValue));
            if (countVideo is <= 0 or > 64) return new(VideoKind.Unknown, "NoUnambiguousVideoStream");
            var defaults = Enumerable.Range(0, countVideo).Where(i => Get(handle, i, "Default") == "Yes").ToArray();
            // Multiple tracks without a unique default cannot safely predict which track VLC will render.
            int selected = defaults.Length == 1 ? defaults[0] : countVideo == 1 ? 0 : -1;
            if (selected < 0) return new(VideoKind.Unknown, "AmbiguousMainVideoStream");
            foreach (string key in new[] { "transfer_characteristics", "colour_primaries", "BitDepth" })
            {
                string original = Get(handle, selected, key + "_Original");
                if (original.Length != 0 && original != Get(handle, selected, key)) return new(VideoKind.Unknown, "ConflictingContainerAndStreamMetadata");
            }
            return VideoClassification.Classify(Get(handle, selected, "transfer_characteristics"), Get(handle, selected, "colour_primaries"),
                Get(handle, selected, "BitDepth"), Get(handle, selected, "HDR_Format"), Get(handle, selected, "HDR_Format_Compatibility"));
        }
        finally { MediaInfo_Delete(handle); }
    }
    private static string Get(nint h, int index, string key) => Marshal.PtrToStringUni(MediaInfo_Get(h, 1, (nuint)index, key, 1, 0)) ?? "";
    // The DLL ships inside the portable bundle and is resolved only in the application directory.
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport("MediaInfo.dll")] private static extern nint MediaInfo_New();
    [DllImport("MediaInfo.dll")] private static extern void MediaInfo_Delete(nint handle);
    [DllImport("MediaInfo.dll", CharSet = CharSet.Unicode)] private static extern nint MediaInfo_Option(nint handle, string option, string value);
    [DllImport("MediaInfo.dll", CharSet = CharSet.Unicode)] private static extern nint MediaInfo_Get(nint handle, nuint kind, nuint stream, string parameter, nuint info, nuint search);
    [DllImport("MediaInfo.dll")] private static extern nuint MediaInfo_Count_Get(nint handle, nuint kind, nuint stream);
    [DllImport("MediaInfo.dll")] private static extern nuint MediaInfo_Open_Buffer_Init(nint handle, ulong size, ulong offset);
    [DllImport("MediaInfo.dll")] private static extern nuint MediaInfo_Open_Buffer_Continue(nint handle, byte[] buffer, nuint size);
    [DllImport("MediaInfo.dll")] private static extern ulong MediaInfo_Open_Buffer_Continue_GoTo_Get(nint handle);
    [DllImport("MediaInfo.dll")] private static extern nuint MediaInfo_Open_Buffer_Finalize(nint handle);
}

/// <summary>Native parsing and stalled UNC I/O are isolated from the HDR engine in a short-lived copy of our EXE.</summary>
public sealed class MediaInfoVideoProbe(string? executable = null) : IVideoProbe
{
    public async Task<VideoMetadata> ReadAsync(string path, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable ?? Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--video-probe"); start.ArgumentList.Add(VideoPath.Validate(path));
        using var process = Process.Start(start) ?? throw new IOException("ProbeWorkerUnavailable");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string json = await output; await errors;
            if (process.ExitCode != 0 || json.Length > 16384) return new(VideoKind.Unknown, "ProbeWorkerFailed");
            return JsonSerializer.Deserialize<VideoMetadata>(json) ?? new(VideoKind.Unknown, "ProbeResponseInvalid");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new(VideoKind.Unknown, "ProbeTimeout"); }
        finally
        {
            // Only our metadata worker may be terminated. VLC is never killed.
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
        }
    }
}
