namespace AutoHDRManager.Core;

/// <summary>One shared path for UI and IPC; never dispatches a game unless HDR has been read back.</summary>
public sealed class GameLaunchService(HdrEngine engine, IGameProcessMonitor monitor, Func<IReadOnlyList<Game>> library,
    Action<Game> launch, IEventLog log, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<Guid, DateTimeOffset> pending = [];
    public async Task DrainAsync() { await gate.WaitAsync(); gate.Release(); }
    public async Task LaunchAsync(Guid id, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        bool reserved = false;
        try
        {
            var games = library();
            var game = games.FirstOrDefault(g => g.Id == id) ?? throw new InvalidOperationException("Игра удалена из библиотеки. Создайте ярлык заново для нужной игры.");
            GamePaths.Validate(game);
            var processes = await monitor.SnapshotAsync(token);
            await engine.TickAsync(processes, games, DateTimeOffset.UtcNow, token);
            if (processes.Any(p => GamePaths.Equal(p.Path, game.ExecutablePath)) ||
                (pending.TryGetValue(id, out var until) && until > DateTimeOffset.UtcNow && await engine.IsLaunchPendingAsync(id, token)))
            { log.Write("DuplicateLaunchIgnored", new { GameId = id }); return; }
            reserved = true; await engine.PrepareLaunchAsync(game, DateTimeOffset.UtcNow, token);
            log.Write("LaunchHdrConfirmed", new { GameId = id });
            await (delay ?? Task.Delay)(TimeSpan.FromSeconds(1), token);
            // A pause, display loss or manual HDR change during the delay must abort dispatch.
            await engine.DispatchLaunchAsync(game, () => launch(game), token);
            pending[id] = DateTimeOffset.UtcNow.AddMinutes(5);
            log.Write("GameLaunchDispatched", new { GameId = id });
        }
        catch
        {
            if (reserved) await engine.CancelLaunchAsync(gameId: id);
            throw;
        }
        finally { gate.Release(); }
    }
}
