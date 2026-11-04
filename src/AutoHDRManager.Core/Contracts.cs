namespace AutoHDRManager.Core;

public interface IHdrController
{
    Task<IReadOnlyList<Display>> GetDisplaysAsync(CancellationToken token = default);
    Task<Display> ReadAsync(string displayId, CancellationToken token = default);
    Task SetAsync(string displayId, bool enabled, CancellationToken token = default);
}
public interface IGameProcessMonitor { Task<IReadOnlyList<GameProcess>> SnapshotAsync(CancellationToken token = default); }
public interface IGameRepository { List<Game> Load(); void Save(IReadOnlyList<Game> games); }
public interface ISettingsRepository { Settings Load(); void Save(Settings settings); }
public interface ISessionRepository { Session? Load(); void Save(Session session); void Clear(); }
public interface ICoverProvider { Task<string?> GetAsync(Game game, CancellationToken token = default); Task<string> ImportAsync(string path, CancellationToken token = default); void ClearCache(); }
public interface INotificationService { void Show(string title, string message); }
public interface IStartupManager { bool IsEnabled { get; } void SetEnabled(bool enabled); }
public interface ITrayService : IDisposable { event Action? OpenRequested; event Action? PauseRequested; event Action? ExitRequested; void Update(string hdr, bool monitoring); }
public interface IEventLog { void Write(string name, object? details = null); }
