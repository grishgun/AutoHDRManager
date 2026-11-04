using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoHDRManager.Core;

namespace AutoHDRManager.App.Services;

public sealed record CoverGame(int Id, string Name) { public override string ToString() => Name; }
public sealed class SteamGridDb : IDisposable
{
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
    private readonly string keyPath;
    private readonly string temporary;
    private readonly ICoverProvider local;
    public SteamGridDb(string root, ICoverProvider local)
    {
        keyPath = Path.Combine(root, "cover-key.bin"); temporary = Path.Combine(root, "Covers", "Downloads"); this.local = local;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AutoHDRManager/1.2");
    }
    public bool HasKey => File.Exists(keyPath);
    public void SaveKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var bytes = Encoding.UTF8.GetBytes(value.Trim());
        try { File.WriteAllBytes(keyPath, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void RemoveKey() { if (File.Exists(keyPath)) File.Delete(keyPath); }
    private string ReadKey()
    {
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(keyPath), null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private async Task<JsonDocument> GetJsonAsync(string route, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.steamgriddb.com/api/v2/" + route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ReadKey());
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) throw new InvalidOperationException("SteamGridDB отклонил API-ключ. Проверьте ключ в настройках.");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await ReadLimitedAsync(response.Content, 1024 * 1024, token));
    }
    public async Task<IReadOnlyList<CoverGame>> SearchAsync(string name, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var document = await GetJsonAsync("search/autocomplete/" + Uri.EscapeDataString(name), timeout.Token);
        return document.RootElement.GetProperty("data").EnumerateArray().Take(12).Select(item => new CoverGame(item.GetProperty("id").GetInt32(), item.GetProperty("name").GetString() ?? "Игра")).ToArray();
    }
    public async Task<(string Path, string Source)> DownloadAsync(int gameId, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var document = await GetJsonAsync($"grids/game/{gameId}?dimensions=600x900&types=static&mimes=image%2Fpng,image%2Fjpeg&nsfw=false&humor=false&limit=1", timeout.Token);
        var items = document.RootElement.GetProperty("data");
        if (items.GetArrayLength() == 0) throw new InvalidOperationException("У этой игры пока нет подходящей обложки.");
        var item = items[0]; var uri = new Uri(item.GetProperty("url").GetString()!);
        if (uri.Scheme != "https" || !uri.Host.EndsWith(".steamgriddb.com", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Неожиданный адрес изображения SteamGridDB.");
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token); response.EnsureSuccessStatusCode();
        string? mime = response.Content.Headers.ContentType?.MediaType;
        if (mime is not ("image/png" or "image/jpeg")) throw new InvalidDataException("Неожиданный формат изображения.");
        byte[] bytes = await ReadLimitedAsync(response.Content, 10 * 1024 * 1024, timeout.Token);
        Directory.CreateDirectory(temporary); string path = Path.Combine(temporary, Guid.NewGuid().ToString("N") + (mime == "image/png" ? ".png" : ".jpg"));
        try { await File.WriteAllBytesAsync(path, bytes, timeout.Token); return (await local.ImportAsync(path, timeout.Token), $"https://www.steamgriddb.com/grid/{item.GetProperty("id").GetInt32()}"); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken token)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("Ответ сервера превышает допустимый размер.");
        using var input = await content.ReadAsStreamAsync(token); using var output = new MemoryStream(); var buffer = new byte[16384];
        int count; while ((count = await input.ReadAsync(buffer, token)) > 0) { if (output.Length + count > limit) throw new InvalidDataException("Ответ сервера слишком большой."); await output.WriteAsync(buffer.AsMemory(0, count), token); }
        return output.ToArray();
    }
    public void Dispose() => client.Dispose();
}
