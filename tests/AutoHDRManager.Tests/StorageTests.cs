using AutoHDRManager.Core;
using Xunit;

namespace AutoHDRManager.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "AutoHdrTests-" + Guid.NewGuid());
    public StorageTests() => Directory.CreateDirectory(root);
    [Fact] public void CorruptJsonRecoversBackupAndPreservesOriginal()
    {
        var file = new JsonFile<Settings>(Path.Combine(root, "settings.json")); file.Save(new Settings { Theme = "Light" }); file.Save(new Settings { Theme = "Dark" });
        File.WriteAllText(Path.Combine(root, "settings.json"), "broken"); Assert.Equal("Light", file.Load()!.Theme); Assert.NotEmpty(Directory.GetFiles(root, "*.corrupt-*"));
    }
    [Fact] public void NoBackupCorruptionFallsBackSafely()
    {
        File.WriteAllText(Path.Combine(root, "settings.json"), "broken"); var repo = new SettingsRepository(root); Assert.Equal("Default", repo.Load().Theme); Assert.NotNull(repo.Warning);
    }
    [Fact] public void DuplicatePathsRejected()
    {
        var repo = new GameRepository(root); Assert.Throws<InvalidDataException>(() => repo.Save([new Game { ExecutablePath = Path.Combine(root, "a.exe") }, new Game { ExecutablePath = Path.Combine(root, "A.exe") }]));
    }
    [Fact] public void SettingsRoundTripAndValidation()
    {
        var repo = new SettingsRepository(root); repo.Save(new Settings { EnableDelaySeconds = -100, PollSeconds = double.NaN, Monitoring = false }); var s = repo.Load(); Assert.Equal(0, s.EnableDelaySeconds); Assert.Equal(1, s.PollSeconds); Assert.False(s.Monitoring);
    }
    [Fact] public void LibraryMissingExecutableRemainsAvailableForRepair()
    {
        var repo = new GameRepository(root); repo.Save([new Game { ExecutablePath = Path.Combine(root, "missing.exe"), Name = "Game" }]); Assert.Single(repo.Load());
    }
    [Fact] public void NewerSchemaIsNeverOverwritten()
    {
        File.WriteAllText(Path.Combine(root, "settings.json"), "{\"Version\":99,\"Data\":{}}"); Assert.Throws<NotSupportedException>(() => new SettingsRepository(root).Load());
    }
    public void Dispose() => Directory.Delete(root, true);
}
