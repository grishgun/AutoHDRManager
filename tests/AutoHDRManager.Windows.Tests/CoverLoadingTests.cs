using AutoHDRManager.App.Services;
using AutoHDRManager.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Xunit;

namespace AutoHDRManager.Windows.Tests;

public sealed class CoverLoadingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "HdrCovers-" + Guid.NewGuid());
    public CoverLoadingTests() => Directory.CreateDirectory(root);

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".webp")]
    public async Task ImportDecodesEachFormatAndReusesItsContentCache(string extension)
    {
        string source = Path.Combine(root, "input" + extension);
        if (extension == ".webp")
            await File.WriteAllBytesAsync(source, Convert.FromBase64String("UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA"));
        else await WriteImage(source, extension == ".jpg" ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId);
        var provider = new LocalCoverProvider(Path.Combine(root, "cache"));
        string imported; try { imported = await provider.ImportAsync(source); } catch (Exception ex) { throw new InvalidOperationException("Image decode HRESULT: " + ex.HResult.ToString("X8"), ex); }
        Assert.EndsWith(".png", imported);
        byte[] first = await File.ReadAllBytesAsync(imported);
        Assert.Equal(imported, await provider.ImportAsync(source));
        Assert.Equal(first, await File.ReadAllBytesAsync(imported));
        await File.WriteAllTextAsync(imported, "Interrupted cache write");
        Assert.Equal(imported, await provider.ImportAsync(source));
        Assert.Equal(imported, await provider.GetAsync(new Game { CoverPath = imported, ExecutablePath = Path.Combine(root, "missing.exe") }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrCorruptCoverUsesIconWithoutChangingSavedCover(bool corrupt)
    {
        string missing = Path.Combine(root, "cover.png");
        if (corrupt) await File.WriteAllTextAsync(missing, "This is not an image.");
        var game = new Game { CoverPath = missing, ExecutablePath = Path.Combine(root, "missing.exe") };
        string cache = Path.Combine(root, "cache"); Directory.CreateDirectory(Path.Combine(cache, "Icons"));
        string icon = Path.Combine(cache, "Icons", game.Id + ".png");
        await WriteImage(icon, BitmapEncoder.PngEncoderId);
        var provider = new LocalCoverProvider(cache);
        Assert.Equal(icon, await provider.GetAsync(game));
        Assert.Equal(missing, game.CoverPath);
        await File.WriteAllTextAsync(icon, "Broken icon cache");
        Assert.Null(await provider.GetAsync(game));
    }

    [Fact]
    public async Task ExistingUserCoverSurvivesReloadAndIconCacheCleanup()
    {
        string source = Path.Combine(root, "user.png");
        await WriteImage(source, BitmapEncoder.PngEncoderId);
        var provider = new LocalCoverProvider(Path.Combine(root, "cache"));
        string imported = await provider.ImportAsync(source);
        byte[] original = await File.ReadAllBytesAsync(imported);
        var game = new Game { Name = "Existing game", CoverPath = imported, CoverSource = "user-choice", ExecutablePath = Path.Combine(root, "missing.exe") };
        var reopenedProvider = new LocalCoverProvider(Path.Combine(root, "cache"));
        reopenedProvider.ClearCache();
        Assert.Equal(imported, await reopenedProvider.GetAsync(game));
        Assert.Equal(original, await File.ReadAllBytesAsync(imported));
        Assert.Equal(imported, game.CoverPath);
        Assert.Equal("user-choice", game.CoverSource);
    }

    [Fact]
    public async Task CancelledLoadDoesNotReplaceUserCover()
    {
        string source = Path.Combine(root, "user.png");
        await WriteImage(source, BitmapEncoder.PngEncoderId);
        var game = new Game { CoverPath = source };
        var provider = new LocalCoverProvider(Path.Combine(root, "cache"));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetAsync(game, cancelled.Token));
        Assert.Equal(source, game.CoverPath);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task CorruptImageCannotEnterTheCoverCache()
    {
        string source = Path.Combine(root, "broken.jpg");
        await File.WriteAllTextAsync(source, "This response is not a JPEG image.");
        string cache = Path.Combine(root, "cache");
        var provider = new LocalCoverProvider(cache);
        await Assert.ThrowsAnyAsync<Exception>(() => provider.ImportAsync(source));
        Assert.False(Directory.Exists(Path.Combine(cache, "Custom")));
    }

    private static async Task WriteImage(string path, Guid encoderId)
    {
        await File.WriteAllBytesAsync(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(encoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 2, 3, 96, 96,
            [40,70,110,255, 70,110,140,255, 90,140,180,255, 110,160,200,255, 140,180,210,255, 170,200,230,255]);
        await encoder.FlushAsync();
    }
    public void Dispose() => Directory.Delete(root, true);
}
