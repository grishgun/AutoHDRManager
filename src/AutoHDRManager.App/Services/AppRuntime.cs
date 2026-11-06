using AutoHDRManager.Core;
using AutoHDRManager.Windows;

namespace AutoHDRManager.App.Services;

public sealed record RuntimeSnapshot(Display? Display, HdrState State, string? Error, IReadOnlySet<ProcessIdentity> Active, IReadOnlyList<GameProcess> Processes);
public sealed class AppRuntime
{
    public string Root { get; }
    public Settings Settings { get; }
    public SettingsRepository SettingsRepository { get; }
    public GameRepository GamesRepository { get; }
    public IHdrController Hdr { get; }
    public ProcessMonitor Monitor { get; } = new();
    public HdrEngine Engine { get; }
    public GameLaunchService Launcher { get; }
    public VideoLaunchService Video { get; }
    public VideoPlayer VideoPlayer { get; } = new();
    public VideoAssociations VideoAssociations { get; }
    public GameShortcuts Shortcuts { get; } = new();
    public StartupManager Startup { get; } = new();
    public ICoverProvider Covers { get; }
    public SteamGridDb OnlineCovers { get; }
    public JsonEventLog Log { get; }
    private readonly CancellationTokenSource stop = new();
    private readonly object libraryLock = new();
    private List<Game> library;
    private Task? loop;
    public bool SafeMode { get; }
    public event Action<RuntimeSnapshot>? Updated;
    public AppRuntime(INotificationService notifications, bool safeMode, string? dataRoot = null)
    {
        SafeMode = safeMode;
        Root = dataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoHDRManager");
        Log = new JsonEventLog(Path.Combine(Root, "Logs"));
        SettingsRepository = new(Root); GamesRepository = new(Root);
        Settings = SettingsRepository.Load(); library = GamesRepository.Load();
        ApplyLanguage();
        if (safeMode) Settings.Monitoring = false;
        if (!safeMode && Settings.Startup && Startup.IsEnabled) Startup.RefreshExistingRegistration();
        Settings.Startup = Startup.IsEnabled;
        Covers = new LocalCoverProvider(Path.Combine(Root, "Covers"));
        OnlineCovers = new(Root, Covers);
        Hdr = new WindowsHdrController(Log);
        if (safeMode) Hdr = new ReadOnlyHdr(Hdr);
        Engine = new(Hdr, new SessionRepository(Root), notifications, Log, Settings);
        Launcher = new(Engine, Monitor, () => Games, GameLauncher.Launch, Log);
        Video = new(Engine, Monitor, () => Games, Settings, new MediaInfoVideoProbe(), VideoPlayer, Log, notifications);
        VideoAssociations = new(Shortcuts, () => VideoPlayer.Find(Settings.VlcPath));
        Log.Write("ApplicationStarted", new { SafeMode, Version = AppVersion.Current.Diagnostic, OS = Environment.OSVersion.Version.ToString() });
    }
    public void ApplyLanguage() => Localization.SetLanguage(Settings.Language, global::Windows.System.UserProfile.GlobalizationPreferences.Languages.FirstOrDefault() ?? System.Globalization.CultureInfo.CurrentUICulture.Name);
    public List<Game> Games { get { lock (libraryLock) return library.Select(g => g with { }).ToList(); } }
    public void SaveGames(List<Game> games) { lock (libraryLock) { GamesRepository.Save(games); library = games.Select(g => g with { }).ToList(); } }
    public Task LaunchAsync(Guid id) => Launcher.LaunchAsync(id, stop.Token);
    public Task OpenVideoAsync(string path) => Video.OpenAsync(path, stop.Token);
    public void Start() => loop ??= Task.Run(RunAsync);
    private async Task RunAsync()
    {
        Log.Write("MonitoringStarted");
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var snapshot = await Monitor.SnapshotAsync(stop.Token);
                await Engine.TickAsync(snapshot, Games, DateTimeOffset.UtcNow, stop.Token);
                await Video.ReapAsync();
                Updated?.Invoke(new(Engine.CurrentDisplay, Engine.State, Engine.LastError, Engine.ActiveProcesses.ToHashSet(), snapshot));
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex) { Log.Write("MonitorError", new { ex.Message }); Updated?.Invoke(new(null, HdrState.Error, ex.Message, new HashSet<ProcessIdentity>(), [])); }
            try { await Task.Delay(TimeSpan.FromSeconds(Settings.PollSeconds), stop.Token); } catch (OperationCanceledException) { break; }
        }
    }
    public async Task StopAsync()
    {
        stop.Cancel(); if (loop != null) await loop;
        await Launcher.DrainAsync();
        await Video.DrainAsync();
        // Do not cancel an in-flight write: let it complete and then restore safely.
        await Engine.ShutdownAsync();
        OnlineCovers.Dispose();
    }
    private sealed class ReadOnlyHdr(IHdrController inner) : IHdrController
    {
        public Task<IReadOnlyList<Display>> GetDisplaysAsync(CancellationToken token = default) => inner.GetDisplaysAsync(token);
        public Task<Display> ReadAsync(string displayId, CancellationToken token = default) => inner.ReadAsync(displayId, token);
        public Task SetAsync(string displayId, bool enabled, CancellationToken token = default) => throw new InvalidOperationException("Режим --safe запрещает переключение HDR.");
    }
}
