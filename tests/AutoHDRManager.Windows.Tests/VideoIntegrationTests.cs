using System.Buffers.Binary;
using System.Text;
using AutoHDRManager.Core;
using AutoHDRManager.Windows;
using Xunit;

namespace AutoHDRManager.Windows.Tests;

public sealed class VideoIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "HdrVideo-" + Guid.NewGuid().ToString("N"));
    public VideoIntegrationTests() => Directory.CreateDirectory(root);
    [Theory][InlineData("C:\\Films\\фильм с пробелами.mp4")][InlineData("\\\\server\\share\\кино & фильм.mkv")]
    public void VlcArgumentsPreservePathAsOneArgumentAndDisableInstanceReuse(string file)
    {
        var start = VideoPlayer.CreateStartInfo(@"C:\Program Files\VideoLAN\VLC\vlc.exe", file);
        Assert.False(start.UseShellExecute); Assert.Equal("", start.Arguments);
        Assert.Equal(new[] { "--no-one-instance", "--no-one-instance-when-started-from-file", "--", file }, start.ArgumentList);
    }
    [Theory][InlineData("C:\\movie.mp4:alternate.mp4")][InlineData("\\\\.\\device.mkv")][InlineData("relative.mkv")][InlineData("C:\\bad\"movie.mkv")]
    public void InvalidVideoPathsAreRejected(string file) => Assert.Throws<ArgumentException>(() => VideoPath.Validate(file));
    [Fact] public async Task VideoIpcPreservesUnicodeAndDoesNotActivateManager()
    {
        string suffix = "-video-test-" + Guid.NewGuid().ToString("N"); string path = @"\\server\share\Фильм с пробелами.mkv";
        await using var first = new SingleInstance(suffix); var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Activated += () => Assert.Fail("No foreground window expected"); first.VideoRequested = file => received.TrySetResult(file); first.StartListening();
        await using var second = new SingleInstance(suffix); await second.NotifyAsync(new LaunchRequest(null, true, path));
        Assert.Equal(path, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
    [Fact] public void AssociationPlanOnlyAddsUserCapabilitiesAndOpenWithWithoutChangingDefaults()
    {
        var values = VideoAssociations.Registration(@"C:\Users\Example\AppData\Roaming\AutoHDRManager\Launcher\AutoHDRManager.exe", "video.ico,0");
        Assert.All(values, value => { Assert.StartsWith("Software\\", value.Key); Assert.DoesNotContain("UserChoice", value.Key); Assert.DoesNotContain(@"Explorer\FileExts", value.Key); });
        Assert.DoesNotContain(values, value => value.Key is @"Software\Classes\.mkv" or @"Software\Classes\.mp4");
        Assert.Equal("\"C:\\Users\\Example\\AppData\\Roaming\\AutoHDRManager\\Launcher\\AutoHDRManager.exe\" --open-video \"%1\"", Assert.Single(values, v => v.Key.EndsWith(@"shell\open\command")).Value);
        Assert.Equal(2, values.Count(v => v.Key.EndsWith("FileAssociations")));
    }
    [Fact] public void AssociationStableTargetUpdatesAfterPortableMoveWithoutChangingCommand()
    {
        string original = Path.Combine(root, "first.exe"), moved = Path.Combine(root, "new.exe"), data = Path.Combine(root, "data"); File.WriteAllText(original, "first");
        var shortcuts = new GameShortcuts(data, original); shortcuts.EnsureLauncher(); var before = VideoAssociations.Registration(shortcuts.Launcher, "video.ico,0");
        File.Move(original, moved); File.WriteAllText(moved, "second"); var updated = new GameShortcuts(data, moved); updated.EnsureLauncher();
        Assert.Equal(before, VideoAssociations.Registration(updated.Launcher, "video.ico,0")); Assert.Equal("second", File.ReadAllText(shortcuts.Launcher));
    }
    [Theory][InlineData(16, 9, 10, VideoKind.Unknown)][InlineData(18, 9, 10, VideoKind.Unknown)][InlineData(1, 1, 8, VideoKind.Sdr)][InlineData(0, 0, 0, VideoKind.Unknown)]
    public void MatroskaHeaderWithoutCodedDepthCannotConfirmHdr(int transfer, int primary, int depth, VideoKind expected)
    {
        string path = Path.Combine(root, "метаданные фильма.mkv"); File.WriteAllBytes(path, Matroska(transfer, primary, depth));
        var actual = MediaInfoMetadata.Read(path); Assert.True(expected == actual.Kind, actual.ToString());
    }
    [Fact] public void CorruptMetadataIsUnknown() { string file = Path.Combine(root, "corrupt.mp4"); File.WriteAllText(file, "invalid video"); Assert.Equal(VideoKind.Unknown, MediaInfoMetadata.Read(file).Kind); }
    [Theory][InlineData(16, 9, VideoKind.Unknown)][InlineData(18, 9, VideoKind.Unknown)][InlineData(1, 1, VideoKind.Sdr)]
    public void Mp4HeaderWithoutCodedDepthCannotConfirmHdr(int transfer, int primary, VideoKind expected)
    {
        string path = Path.Combine(root, "movie.mp4"); File.WriteAllBytes(path, Mp4(transfer, primary));
        var actual = MediaInfoMetadata.Read(path); Assert.True(expected == actual.Kind, actual.ToString());
    }
    [Theory]
    [InlineData("hdr10.mkv", VideoKind.Hdr10)][InlineData("hdr10.mp4", VideoKind.Hdr10)]
    [InlineData("hlg.mkv", VideoKind.Hlg)][InlineData("hlg.mp4", VideoKind.Hlg)]
    [InlineData("sdr.mkv", VideoKind.Sdr)][InlineData("sdr.mp4", VideoKind.Sdr)]
    public void RealTenBitVideoStreamAndContainerAreParsed(string file, VideoKind expected)
    {
        var actual = MediaInfoMetadata.Read(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Video", file));
        Assert.True(expected == actual.Kind, actual.ToString()); Assert.Equal("10", actual.Depth);
    }
    // Original generated container headers, no third-party movie or decoded frames.
    private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static byte[] E(uint id, byte[] payload)
    {
        var bytes = BitConverter.GetBytes(id).Reverse().SkipWhile(b => b == 0).ToArray();
        byte[] size = payload.Length < 127 ? [(byte)(128 | payload.Length)] : [(byte)(0x40 | (payload.Length >> 8)), (byte)payload.Length];
        return Join(bytes, size, payload);
    }
    private static byte[] N(uint id, int n) => E(id, n < 256 ? [(byte)n] : [(byte)(n >> 8), (byte)n]);
    private static byte[] Matroska(int transfer, int primary, int depth)
    {
        var header = E(0x1A45DFA3, Join(N(0x4286, 1), N(0x42F7, 1), N(0x42F2, 4), N(0x42F3, 8), E(0x4282, Encoding.ASCII.GetBytes("matroska")), N(0x4287, 4), N(0x4285, 2)));
        var color = depth == 0 ? Array.Empty<byte>() : E(0x55B0, Join(N(0x55B2, depth), N(0x55BA, transfer), N(0x55BB, primary), N(0x55B1, 9)));
        var track = E(0xAE, Join(N(0xD7, 1), N(0x73C5, 1), N(0x83, 1), N(0x88, 1), E(0x86, Encoding.ASCII.GetBytes("V_MPEGH/ISO/HEVC")), E(0xE0, Join(N(0xB0, 1920), N(0xBA, 1080), color))));
        return Join(header, E(0x18538067, Join(E(0x1549A966, N(0x2AD7B1, 1000)), E(0x1654AE6B, track), E(0x1F43B675, Join(N(0xE7, 0), E(0xA3, [0x81, 0, 0, 0x80, 0]))))));
    }
    private static byte[] Box(string type, params byte[][] parts)
    {
        var payload = Join(parts); byte[] size = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(size, payload.Length + 8); return Join(size, Encoding.ASCII.GetBytes(type), payload);
    }
    private static byte[] Mp4(int transfer, int primary)
    {
        byte[] sample = new byte[78]; sample[7] = 1; sample[24] = 7; sample[25] = 128; sample[26] = 4; sample[27] = 56; sample[75] = 24; sample[76] = 255; sample[77] = 255;
        byte[] hvcc = new byte[23]; hvcc[0] = 1; hvcc[1] = 2; hvcc[12] = 120; hvcc[13] = 240; hvcc[15] = 252; hvcc[16] = 253; hvcc[17] = 250; hvcc[18] = 250; hvcc[21] = 15;
        byte[] colr = Join(Encoding.ASCII.GetBytes("nclx"), [0, (byte)primary, 0, (byte)transfer, 0, 9, 0]);
        var stsd = Box("stsd", [0, 0, 0, 0, 0, 0, 0, 1], Box("hvc1", sample, Box("hvcC", hvcc), Box("colr", colr)));
        var handler = Box("hdlr", new byte[8], Encoding.ASCII.GetBytes("vide"), new byte[13]);
        return Join(Box("ftyp", Encoding.ASCII.GetBytes("isom"), new byte[4], Encoding.ASCII.GetBytes("isomiso2mp41")), Box("moov", Box("trak", Box("mdia", handler, Box("minf", Box("stbl", stsd))))));
    }
    public void Dispose() => Directory.Delete(root, true);
}
