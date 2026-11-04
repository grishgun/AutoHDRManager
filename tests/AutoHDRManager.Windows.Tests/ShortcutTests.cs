using AutoHDRManager.Core;
using AutoHDRManager.Windows;
using Xunit;

namespace AutoHDRManager.Windows.Tests;

public sealed class ShortcutTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "HdrShortcut-" + Guid.NewGuid().ToString("N"));
    public ShortcutTests() => Directory.CreateDirectory(root);
    [Fact] public void LinkPreservesGameIconArgumentsAndIndependentIdentity()
    {
        var game = new Game { Name = "Игра с пробелами", ExecutablePath = Environment.ProcessPath! };
        string path = Path.Combine(root, "Игра.lnk");
        GameShortcuts.WriteLink(path, Environment.ProcessPath!, game);
        var read = GameShortcuts.ReadLink(path);
        Assert.Equal(Environment.ProcessPath, read.Target); Assert.Equal("--launch-game " + game.Id.ToString("D"), read.Arguments);
        Assert.Equal(game.ExecutablePath, read.Icon); Assert.Equal(0, read.IconIndex);
        Assert.Equal(GameShortcuts.GameAppId(game.Id), read.AppId); Assert.NotEqual(GameShortcuts.ManagerAppId, read.AppId);
    }
    [Fact] public void StableTargetSurvivesPortableMoveAndNewVersionRefresh()
    {
        string exe = Path.Combine(root, "portable.exe"); File.WriteAllText(exe, "test release 1");
        var game = new Game { Name = "Игра", ExecutablePath = Environment.ProcessPath! };
        string data = Path.Combine(root, "data"); var shortcuts = new GameShortcuts(data, exe);
        string path = shortcuts.Create(game); string pinnedCopy = Path.Combine(root, "pinned-test.lnk"); File.Copy(path, pinnedCopy);
        string moved = Path.Combine(root, "moved.exe"); File.Move(exe, moved); File.WriteAllText(moved, "test release 2");
        new GameShortcuts(data, moved).RefreshExisting([game]);
        var read = GameShortcuts.ReadLink(pinnedCopy);
        Assert.Equal(shortcuts.Launcher, read.Target); Assert.Equal("test release 2", File.ReadAllText(read.Target));
        Assert.Equal(path, new GameShortcuts(data, moved).Create(game)); Assert.Single(Directory.GetFiles(shortcuts.DirectoryPath, "*.lnk"));
    }
    [Fact] public async Task LaunchIpcRoutesGuidAndAcknowledgesWithoutOpeningWindow()
    {
        string suffix = "-test-" + Guid.NewGuid().ToString("N"); Guid id = Guid.NewGuid();
        await using var primary = new SingleInstance(suffix);
        var received = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.Activated += () => Assert.Fail("OPEN must not be sent"); primary.LaunchRequested = gameId => received.TrySetResult(gameId); primary.StartListening();
        await using var secondary = new SingleInstance(suffix);
        await secondary.NotifyAsync(new LaunchRequest(id, true)); Assert.Equal(id, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
    [Fact] public async Task LaunchIpcReportsRejectedRequest()
    {
        string suffix = "-test-" + Guid.NewGuid().ToString("N");
        await using var primary = new SingleInstance(suffix); primary.LaunchRequested = _ => false; primary.StartListening();
        await using var secondary = new SingleInstance(suffix);
        await Assert.ThrowsAsync<IOException>(() => secondary.NotifyAsync(new LaunchRequest(Guid.NewGuid(), true)));
    }
    [Fact] public void MissingIconUsesLauncherWithoutChangingLaunchArguments()
    {
        string noIcon = Path.Combine(root, "empty.exe"); File.WriteAllText(noIcon, "not an executable resource");
        var game = new Game { ExecutablePath = noIcon, Name = "Test" };
        string path = Path.Combine(root, "fallback.lnk"); GameShortcuts.WriteLink(path, Environment.ProcessPath!, game);
        var read = GameShortcuts.ReadLink(path); Assert.Equal(Environment.ProcessPath, read.Icon);
        Assert.Equal("--launch-game " + game.Id.ToString("D"), read.Arguments);
        Assert.Equal(Environment.ProcessPath, GameShortcuts.IconSource(Path.Combine(root, "missing.exe"), Environment.ProcessPath!));
    }
    [Fact] public void DiagnosticLookupDoesNotCreateOrRefreshAnything()
    {
        var shortcuts = new GameShortcuts(Path.Combine(root, "data"), Path.Combine(root, "missing-manager.exe"));
        var game = new Game { ExecutablePath = Environment.ProcessPath!, Name = "Test" };
        Assert.Empty(shortcuts.ReadExisting(game.Id)); Assert.False(Directory.Exists(shortcuts.Root));
        Directory.CreateDirectory(shortcuts.DirectoryPath); string path = Path.Combine(shortcuts.DirectoryPath, "existing.lnk");
        GameShortcuts.WriteLink(path, shortcuts.Launcher, game);
        byte[] before = File.ReadAllBytes(path);
        var actual = Assert.Single(shortcuts.ReadExisting(game.Id));
        Assert.Equal(GameShortcuts.GameAppId(game.Id), actual.AppId); Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(shortcuts.ReadExisting(Guid.NewGuid())); Assert.False(File.Exists(shortcuts.Launcher));
        Assert.False(File.Exists(Path.Combine(shortcuts.Root, "shortcuts.json")));
    }
    public void Dispose() => Directory.Delete(root, true);
}
