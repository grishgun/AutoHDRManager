using System.Security.Cryptography;
using AutoHDRManager.Core;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

namespace AutoHDRManager.App.Services;

// Offline by design: executable thumbnail, then the built-in card illustration. No scraping or hidden API keys.
public sealed class LocalCoverProvider(string root) : ICoverProvider
{
    private readonly string icons = Path.Combine(root, "Icons");
    private readonly string custom = Path.Combine(root, "Custom");
    private readonly SemaphoreSlim extraction = new(1, 1);
    public async Task<string?> GetAsync(Game game, CancellationToken token = default)
    {
        await extraction.WaitAsync(token);
        try { return await GetCoreAsync(game, token); }
        finally { extraction.Release(); }
    }
    private async Task<string?> GetCoreAsync(Game game, CancellationToken token)
    {
        if (game.CoverPath != null && await CanDecodeAsync(game.CoverPath, token)) return game.CoverPath;
        Directory.CreateDirectory(icons);
        string destination = Path.Combine(icons, game.Id + ".png");
        if (await CanDecodeAsync(destination, token)) return destination;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(game.ExecutablePath).AsTask(token);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 256, ThumbnailOptions.UseCurrentScale).AsTask(token);
            if (thumb == null || thumb.Size == 0 || thumb.Size > 8 * 1024 * 1024) return null;
            // Decode and re-encode: thumbnail's actual format need not be PNG.
            var decoder = await BitmapDecoder.CreateAsync(thumb).AsTask(token);
            using var bitmap = await decoder.GetSoftwareBitmapAsync().AsTask(token);
            var output = await StorageFile.GetFileFromPathAsync(CreateEmpty(destination)).AsTask(token);
            using var stream = await output.OpenAsync(FileAccessMode.ReadWrite).AsTask(token);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(token); encoder.SetSoftwareBitmap(bitmap); await encoder.FlushAsync().AsTask(token);
            return destination;
        }
        catch (OperationCanceledException) { if (File.Exists(destination)) File.Delete(destination); throw; }
        catch { if (File.Exists(destination)) File.Delete(destination); return null; }
    }
    public async Task<string> ImportAsync(string path, CancellationToken token = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 12 or > 10 * 1024 * 1024) throw new InvalidDataException("Выберите изображение размером до 10 МБ.");
        if (Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".webp")) throw new InvalidDataException("Поддерживаются PNG, JPG и WEBP.");
        var source = await StorageFile.GetFileFromPathAsync(path).AsTask(token);
        using var input = await source.OpenReadAsync().AsTask(token);
        var decoder = await BitmapDecoder.CreateAsync(input).AsTask(token);
        if (decoder.PixelWidth > 8192 || decoder.PixelHeight > 8192 || (ulong)decoder.PixelWidth * decoder.PixelHeight > 32_000_000) throw new InvalidDataException("Изображение слишком большое (максимум 32 мегапикселя).");
        using var bitmap = await decoder.GetSoftwareBitmapAsync().AsTask(token);
        Directory.CreateDirectory(custom);
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, token)));
        string destination = Path.Combine(custom, hash + ".png");
        if (await CanDecodeAsync(destination, token)) return destination;
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var output = await StorageFile.GetFileFromPathAsync(CreateEmpty(temporary)).AsTask(token);
            using (var stream = await output.OpenAsync(FileAccessMode.ReadWrite).AsTask(token))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(token);
                encoder.SetSoftwareBitmap(bitmap); await encoder.FlushAsync().AsTask(token);
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return destination;
    }
    private static string CreateEmpty(string path) { using var file = File.Create(path); return path; }
    private static async Task<bool> CanDecodeAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path)) return false;
        try
        {
            // A WinRT decoder may retain its source until GC. Validate from memory so
            // refreshing/replacing a cached image never leaves the disk file mapped.
            if (new FileInfo(path).Length > 10 * 1024 * 1024) return false;
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync((await File.ReadAllBytesAsync(path, token)).AsBuffer()).AsTask(token);
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || decoder.PixelWidth > 8192 || decoder.PixelHeight > 8192 || (ulong)decoder.PixelWidth * decoder.PixelHeight > 32_000_000) return false;
            using var bitmap = await decoder.GetSoftwareBitmapAsync().AsTask(token);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }
    public void ClearCache()
    {
        if (Directory.Exists(icons)) foreach (var file in Directory.EnumerateFiles(icons, "*.png")) File.Delete(file);
    }
}
