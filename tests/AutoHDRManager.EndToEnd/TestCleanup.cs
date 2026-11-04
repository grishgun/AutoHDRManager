using AutoHDRManager.Core;
using AutoHDRManager.Windows;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

// Recovery for an interrupted acceptance runner. Does not write HDR or clear its recovery journal.
internal static class TestCleanup
{
    public static async Task RunAsync(string output)
    {
        var result = new Dictionary<string, object?> { ["Time"] = DateTimeOffset.UtcNow };
        try
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoHDRManager");
            using var file = File.OpenHandle(Path.Combine(root, "games.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var physical = new StringBuilder(32768);
            if (GetFinalPathNameByHandle(file, physical, (uint)physical.Capacity, 0) == 0) throw new IOException("Cannot verify the profile path.");
            if (!GamePaths.Equal(physical.ToString().Replace(@"\\?\", ""), Path.Combine(root, "games.json")))
                throw new InvalidOperationException("Redirected profile: launch this helper from Explorer.");
            await using var exclusive = new SingleInstance();
            if (!exclusive.IsPrimary) throw new InvalidOperationException("Exit Auto HDR Manager normally first. No files changed.");
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "result.json")));
            Guid fixture = report.RootElement.GetProperty("GameId").GetGuid();
            string originalGamesPath = Path.Combine(output, "games.json.original");
            var originalGames = File.Exists(originalGamesPath) ? Read<List<Game>>(originalGamesPath) : [];
            var currentGames = Read<List<Game>>(Path.Combine(root, "games.json"));
            string originalSettingsPath = Path.Combine(output, "settings.json.original");
            var originalSettings = File.Exists(originalSettingsPath) ? Read<Settings>(originalSettingsPath) : new Settings();
            var currentSettings = Read<Settings>(Path.Combine(root, "settings.json"));
            var expectedSettings = originalSettings with { Monitoring = true, Startup = false, PollSeconds = 1, RestoreDelaySeconds = 2, LaunchTimeoutSeconds = 15 };
            if (!currentGames.Where(g => g.Id != fixture).SequenceEqual(originalGames) ||
                currentGames.Count(g => g.Id == fixture) > 1 ||
                (currentSettings != expectedSettings && currentSettings != originalSettings))
                throw new InvalidOperationException("Library/settings changed after the test. Preserve them for manual comparison; no files changed.");
            // Preserve failure evidence before restoring exactly the four backed-up files.
            foreach (string name in new[] { "session.json", "session.json.bak", "Logs/current.jsonl" })
            {
                string source = Path.Combine(root, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(output, "failure-" + Path.GetFileName(name)), true);
            }
            var names = new[] { "games.json", "games.json.bak", "settings.json", "settings.json.bak" };
            foreach (string name in names)
            {
                string backup = Path.Combine(output, name + ".original"), destination = Path.Combine(root, name);
                if (File.Exists(backup)) File.WriteAllBytes(destination, File.ReadAllBytes(backup));
                else if (File.Exists(destination)) File.Delete(destination);
            }
            string link = Path.Combine(output, "HDR lifecycle test.lnk");
            if (File.Exists(link)) File.Delete(link);
            result["LibraryAndSettingsRestored"] = names.All(name =>
                File.Exists(Path.Combine(output, name + ".original"))
                    ? File.ReadAllBytes(Path.Combine(root, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(output, name + ".original")))
                    : !File.Exists(Path.Combine(root, name)));
            result["SessionJournalPreserved"] = File.Exists(Path.Combine(root, "session.json"));
            try { result["Displays"] = await new WindowsHdrController(new Sink()).GetDisplaysAsync(); }
            catch (Exception ex) { result["DisplayReadError"] = ex.Message; }
        }
        catch (Exception ex) { result["Error"] = ex.ToString(); }
        File.WriteAllText(Path.Combine(output, "cleanup.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static T Read<T>(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        if (json.RootElement.GetProperty("Version").GetInt32() != 1) throw new InvalidDataException("Unexpected data version.");
        return json.RootElement.GetProperty("Data").Deserialize<T>() ?? throw new InvalidDataException("Missing data.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
