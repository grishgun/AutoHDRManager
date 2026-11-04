using System.Text.Json;

namespace AutoHDRManager.Core;

public sealed class JsonFile<T>(string path, Func<T, bool>? validate = null)
{
    private sealed record Envelope(int Version, T Data)
    {
        [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
    }
    private static readonly JsonSerializerOptions options = new() { WriteIndented = true };
    private readonly object sync = new();
    public string? Warning { get; private set; }
    public T? Load()
    {
        lock (sync)
        {
            if (!File.Exists(path)) return default;
            try { return Read(path); }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
            {
                Warning = "Повреждён файл " + Path.GetFileName(path) + ". Оригинал сохранён для восстановления.";
                File.Copy(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), true);
                if (File.Exists(path + ".bak"))
                {
                    try { var recovered = Read(path + ".bak"); File.Copy(path + ".bak", path, true); return recovered; }
                    catch (Exception backupError) when (backupError is JsonException or IOException or InvalidDataException) { }
                }
                File.Move(path, path + ".unreadable-" + Guid.NewGuid().ToString("N"));
                return default;
            }
        }
    }
    private T Read(string source)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(source), options) ?? throw new InvalidDataException("Пустой JSON.");
        if (envelope.Version > 1) throw new NotSupportedException("Данные созданы более новой версией Auto HDR Manager. Обновите приложение.");
        if (envelope.Version != 1 || envelope.Data is null || validate?.Invoke(envelope.Data) == false) throw new InvalidDataException("Неверные данные JSON.");
        return envelope.Data;
    }
    public void Save(T data)
    {
        lock (sync)
        {
            if (validate?.Invoke(data) == false) throw new InvalidDataException("Неверные данные для сохранения.");
            Envelope? existing = null;
            if (File.Exists(path))
            {
                existing = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path), options);
                if (existing?.Version > 1) throw new NotSupportedException("Данные созданы более новой версией Auto HDR Manager. Обновите приложение.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { JsonSerializer.Serialize(file, new Envelope(1, data) { AdditionalData = existing?.AdditionalData }, options); file.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    public void Delete() { lock (sync) { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".bak")) File.Delete(path + ".bak"); } }
}
public sealed class GameRepository(string root) : IGameRepository
{
    private readonly JsonFile<List<Game>> file = new(Path.Combine(root, "games.json"), games => games.All(g => g != null && g.Id != Guid.Empty && !string.IsNullOrWhiteSpace(g.ExecutablePath) && !string.IsNullOrWhiteSpace(g.Name)));
    public string? Warning => file.Warning;
    public List<Game> Load() => file.Load() ?? [];
    public void Save(IReadOnlyList<Game> games)
    {
        if (games.Select(g => GamePaths.Normalize(g.ExecutablePath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != games.Count || games.Select(g => g.Id).Distinct().Count() != games.Count)
            throw new InvalidDataException("Эта игра уже есть в библиотеке.");
        file.Save(games.ToList());
    }
}
public sealed class SettingsRepository(string root) : ISettingsRepository
{
    private readonly JsonFile<Settings> file = new(Path.Combine(root, "settings.json"));
    public string? Warning => file.Warning;
    public Settings Load() { var settings = file.Load() ?? new(); settings.Validate(); return settings; }
    public void Save(Settings settings) { settings.Validate(); file.Save(settings); }
}
public sealed class SessionRepository(string root) : ISessionRepository
{
    private readonly JsonFile<Session> file = new(Path.Combine(root, "session.json"), s => s.Id != Guid.Empty && !string.IsNullOrWhiteSpace(s.DisplayId) && s.Processes != null);
    public Session? Load() => file.Load();
    public void Save(Session session) => file.Save(session);
    public void Clear() => file.Delete();
}
public sealed class JsonEventLog : IEventLog
{
    private readonly string root;
    private readonly object sync = new();
    public JsonEventLog(string root) { this.root = root; Directory.CreateDirectory(root); }
    public void Write(string name, object? details = null)
    {
        lock (sync)
        {
            try
            {
                string path = Path.Combine(root, "current.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                {
                    for (int i = 4; i >= 1; i--) { string old = Path.Combine(root, $"log-{i}.jsonl"); if (File.Exists(old)) File.Move(old, Path.Combine(root, $"log-{i + 1}.jsonl"), true); }
                    File.Move(path, Path.Combine(root, "log-1.jsonl"), true);
                }
                File.AppendAllText(path, JsonSerializer.Serialize(new { Time = DateTimeOffset.UtcNow, Event = name, Details = details }) + Environment.NewLine);
            }
            catch (IOException) { /* Logging failure must not stop HDR restoration. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
