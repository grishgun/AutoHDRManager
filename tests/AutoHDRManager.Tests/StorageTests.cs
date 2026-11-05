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
    [Fact] public void PendingRestorationRetainsOriginalAndIdentityOnDisk()
    {
        var repository = new SessionRepository(root);
        var missing = DateTimeOffset.Parse("2026-11-05T18:00:50Z");
        var deadline = DateTimeOffset.Parse("2026-11-05T21:15:37Z");
        repository.Save(new Session { DisplayId = "device-path", DisplayFingerprint = "edid", DisplayHardwareId = "serial",
            Original = false, LastConfirmed = true, ChangedByUs = true, DisplayUnavailableSince = missing, RestoreDeadline = deadline, RestoreProbeCount = 3 });
        var loaded = new SessionRepository(root).Load()!;
        Assert.False(loaded.Original); Assert.True(loaded.LastConfirmed); Assert.True(loaded.ChangedByUs);
        Assert.False(loaded.ManualOverride); Assert.Equal("device-path", loaded.DisplayId); Assert.Equal("edid", loaded.DisplayFingerprint);
        Assert.Equal("serial", loaded.DisplayHardwareId); Assert.Equal(missing, loaded.DisplayUnavailableSince);
        Assert.Equal(deadline, loaded.RestoreDeadline); Assert.Equal(3, loaded.RestoreProbeCount);
    }
    [Fact] public void LegacySessionHasNoInventedDisplayIdentity()
    {
        File.WriteAllText(Path.Combine(root, "session.json"), """{"Version":1,"Data":{"Id":"5a65a343-5121-4470-ad11-647241c11845","DisplayId":"old-path","Original":false,"LastConfirmed":true,"ChangedByUs":true,"Processes":[]}}""");
        var loaded = new SessionRepository(root).Load()!;
        Assert.False(loaded.Original); Assert.Null(loaded.DisplayFingerprint); Assert.Null(loaded.DisplayHardwareId); Assert.Null(loaded.RestoreDeadline);
    }
    public void Dispose() => Directory.Delete(root, true);
}
