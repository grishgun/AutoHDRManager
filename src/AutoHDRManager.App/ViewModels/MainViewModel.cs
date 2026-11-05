using L = AutoHDRManager.Core.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AutoHDRManager.Core;
using AutoHDRManager.Windows;
using AutoHDRManager.App.Services;
using Microsoft.UI.Dispatching;

namespace AutoHDRManager.App.ViewModels;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Raise(name); return true; }
}
public sealed class GameViewModel(Game game) : Observable
{
    public Game Model { get; } = game;
    public string Name => Model.Name;
    public string Path => Model.ExecutablePath;
    // The provider can return an EXE icon when a saved cover is missing or corrupt.
    public bool HasArtwork => cover != null && GamePaths.Equal(cover, Model.CoverPath);
    public bool HasPlaceholder => !HasArtwork;
    public bool HasIcon => HasPlaceholder && cover != null;
    public bool NeedsGlyph => HasPlaceholder && cover == null;
    public Microsoft.UI.Xaml.Media.Stretch CoverStretch => HasArtwork ? Microsoft.UI.Xaml.Media.Stretch.UniformToFill : Microsoft.UI.Xaml.Media.Stretch.Uniform;
    private double cardWidth = 180;
    public double CardWidth { get => cardWidth; set { if (Set(ref cardWidth, value)) Raise(nameof(CoverHeight)); } }
    public double CoverHeight => (CardWidth - 2) * 1.5;
    public void RefreshLanguage() => Raise(nameof(Status));
    private string? cover;
    public Microsoft.UI.Xaml.Media.Imaging.BitmapImage? CoverImage { get; private set; }
    public string? Cover
    {
        get => cover == null ? null : new Uri(cover).AbsoluteUri;
        set
        {
            if (!Set(ref cover, value)) return;
            // Rebuild only when the provider changes/reloads the image, including cache refresh.
            CoverImage = value == null ? null : new Microsoft.UI.Xaml.Media.Imaging.BitmapImage
            {
                CreateOptions = Microsoft.UI.Xaml.Media.Imaging.BitmapCreateOptions.IgnoreImageCache,
                DecodePixelWidth = 512, UriSource = new Uri(value)
            };
            Raise(nameof(CoverImage)); Raise(nameof(HasArtwork)); Raise(nameof(HasPlaceholder));
            Raise(nameof(HasIcon)); Raise(nameof(NeedsGlyph)); Raise(nameof(CoverStretch));
        }
    }
    private bool running;
    public bool Running { get => running; set { if (Set(ref running, value)) Raise(nameof(Status)); } }
    public string Status => Running ? L.Get("Запущена  ·  HDR") : L.Get("Не запущена  ·  HDR");
}
public sealed class MainViewModel : Observable
{
    public AppRuntime Runtime { get; }
    public ObservableCollection<GameViewModel> Games { get; } = [];
    public ObservableCollection<GameViewModel> FilteredGames { get; } = [];
    private string search = "";
    public string Search { get => search; set { search = value; Filter(); } }
    public bool ShowGrid => HasGames && Runtime.Settings.LibraryView == "Grid";
    public bool ShowList => HasGames && Runtime.Settings.LibraryView == "List";
    public bool NoResults => HasGames && FilteredGames.Count == 0;
    private RuntimeSnapshot? latest;
    private string monitorName = "—";
    public string MonitorName { get => monitorName; private set => Set(ref monitorName, value); }
    public bool HasActiveGames => Games.Any(g => g.Running);
    public string DiagnosticError => HasError ? Error : Localization.Get("NoError");
    public ObservableCollection<Display> Displays { get; } = [];
    public Settings EditSettings { get; private set; }
    private string hdr = L.Get("Проверка…"), active = L.Get("Нет"), error = "", engine = L.Get("Ожидание");
    public string HdrStatus { get => hdr; private set => Set(ref hdr, value); }
    public string ActiveGames { get => active; private set => Set(ref active, value); }
    public string Error { get => Localization.Message(error); set { if (Set(ref error, value)) { Raise(nameof(HasError)); Raise(nameof(DiagnosticError)); } } }
    public bool HasError => !string.IsNullOrEmpty(Error);
    public string EngineStatus { get => engine; private set => Set(ref engine, value); }
    public string MonitoringStatus => Runtime.Settings.Monitoring ? L.Get("Активен") : L.Get("Приостановлен");
    public string PauseLabel => Runtime.Settings.Monitoring ? L.Get("Приостановить") : L.Get("Возобновить");
    public bool IsEmpty => Games.Count == 0;
    public bool HasGames => Games.Count != 0;
    public event Action? ThemeChanged;
    public event Action? StatusChanged;
    public MainViewModel(AppRuntime runtime, DispatcherQueue dispatcher)
    {
        Runtime = runtime; EditSettings = runtime.Settings with { };
        runtime.Updated += snapshot => dispatcher.TryEnqueue(() => Apply(snapshot));
        foreach (var game in runtime.Games) Games.Add(new(game));
        Games.CollectionChanged += (_, _) => { Raise(nameof(IsEmpty)); Raise(nameof(HasGames)); Filter(); };
        Localization.Changed += RefreshLanguage;
        Filter();
        Error = runtime.SettingsRepository.Warning ?? runtime.GamesRepository.Warning ?? "";
    }
    private void Apply(RuntimeSnapshot snapshot)
    {
        latest = snapshot;
        MonitorName = snapshot.Display?.Name ?? "—";
        HdrStatus = snapshot.Display is null || !snapshot.Display.Supported ? L.Get("Недоступен") : snapshot.Display.Enabled ? L.Get("Включён") : L.Get("Выключен");
        foreach (var game in Games) game.Running = snapshot.Processes.Any(p => snapshot.Active.Contains(p.Identity) && p.Path != null && GamePaths.Equal(p.Path, game.Path));
        ActiveGames = string.Join(", ", Games.Where(g => g.Running).Select(g => g.Name)); if (ActiveGames.Length == 0) ActiveGames = L.Get("Нет");
        EngineStatus = snapshot.State switch
        {
            HdrState.Idle => L.Get("Готов к запуску игр"), HdrState.GameDetected => L.Get("Игра обнаружена"), HdrState.WaitingToEnable => L.Get("Ожидание перед включением HDR"),
            HdrState.EnablingHDR => L.Get("Включение HDR"), HdrState.HDRActive => L.Get("Игровая HDR-сессия"), HdrState.WaitingToRestore => L.Get("Ожидание завершения сессии"),
            HdrState.RestoringHDR => L.Get("Восстановление HDR"), HdrState.Suspended => Runtime.Engine.ManualOverride ? L.Get("HDR изменён вручную — управление приостановлено до выхода из игр") : L.Get("Мониторинг приостановлен"), _ => L.Get("Ошибка управления HDR")
        };
        if (snapshot.Error != null) Error = snapshot.Error;
        Raise(nameof(HasActiveGames));
        StatusChanged?.Invoke();
    }
    private void Filter()
    {
        FilteredGames.Clear();
        foreach (var game in Games.Where(g => g.Name.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase))) FilteredGames.Add(game);
        Raise(nameof(ShowGrid)); Raise(nameof(ShowList)); Raise(nameof(NoResults));
    }
    private void RefreshLanguage()
    {
        foreach (var game in Games) game.RefreshLanguage();
        Raise(nameof(MonitoringStatus)); Raise(nameof(PauseLabel)); Raise(nameof(Error)); Raise(nameof(DiagnosticError));
        if (latest != null) Apply(latest);
    }
    // Presentation changes do not touch startup, monitoring, the engine or the active HDR session.
    public void SetPresentation(string? language = null, string? theme = null, string? view = null)
    {
        var saved = Runtime.SettingsRepository.Load();
        if (language != null) saved.Language = language;
        if (theme != null) saved.Theme = theme;
        if (view != null) saved.LibraryView = view;
        saved.Validate(); Runtime.SettingsRepository.Save(saved);
        Runtime.Settings.Language = EditSettings.Language = saved.Language;
        Runtime.Settings.Theme = EditSettings.Theme = saved.Theme;
        Runtime.Settings.LibraryView = EditSettings.LibraryView = saved.LibraryView;
        Runtime.ApplyLanguage(); Filter(); ThemeChanged?.Invoke();
    }
    public async Task LoadCoversAsync()
    {
        foreach (var game in Games.ToArray()) game.Cover = await Runtime.Covers.GetAsync(game.Model);
    }
    public async Task AddAsync(string path)
    {
        var game = new Game { ExecutablePath = path }; GamePaths.Validate(game);
        if (Games.Any(g => GamePaths.Equal(g.Path, game.ExecutablePath))) throw new InvalidOperationException(L.Get("Эта игра уже есть в библиотеке."));
        var metadata = FileVersionInfo.GetVersionInfo(game.ExecutablePath);
        game.Name = !string.IsNullOrWhiteSpace(metadata.ProductName) ? metadata.ProductName : Path.GetFileNameWithoutExtension(path);
        var all = Runtime.Games; all.Add(game); Runtime.SaveGames(all);
        var vm = new GameViewModel(game); Games.Add(vm); Raise(nameof(IsEmpty)); vm.Cover = await Runtime.Covers.GetAsync(game);
    }
    public void Remove(GameViewModel game)
    {
        if (game.Running) throw new InvalidOperationException(L.Get("Дождитесь завершения игры, чтобы удалить её из отслеживания."));
        var all = Runtime.Games; all.RemoveAll(g => g.Id == game.Model.Id); Runtime.SaveGames(all); Games.Remove(game); Raise(nameof(IsEmpty));
    }
    public async Task UpdateAsync(GameViewModel vm, Game replacement)
    {
        GamePaths.Validate(replacement);
        if (vm.Running && !GamePaths.Equal(vm.Path, replacement.ExecutablePath)) throw new InvalidOperationException(L.Get("Изменить EXE можно после завершения игры."));
        var all = Runtime.Games; int index = all.FindIndex(g => g.Id == replacement.Id); all[index] = replacement; Runtime.SaveGames(all);
        int uiIndex = Games.IndexOf(vm); var item = new GameViewModel(replacement) { Running = vm.Running }; Games[uiIndex] = item; item.Cover = await Runtime.Covers.GetAsync(replacement);
    }
    public async Task LaunchAsync(GameViewModel game)
    {
        await Runtime.LaunchAsync(game.Model.Id);
    }
    public void SaveSettings()
    {
        if (Runtime.SafeMode && EditSettings.Startup != Runtime.Startup.IsEnabled) throw new InvalidOperationException(L.Get("Режим проверки --safe запрещает изменение автозапуска."));
        EditSettings.Validate();
        if (EditSettings.DisplayId != Runtime.Settings.DisplayId && Runtime.Engine.HasSession) throw new InvalidOperationException(L.Get("Изменить монитор можно после завершения HDR-сессии."));
        if (EditSettings.Startup != Runtime.Startup.IsEnabled) Runtime.Startup.SetEnabled(EditSettings.Startup);
        Runtime.SettingsRepository.Save(EditSettings with { Monitoring = Runtime.SafeMode ? Runtime.SettingsRepository.Load().Monitoring : EditSettings.Monitoring });
        var s = Runtime.Settings; var e = EditSettings;
        s.Startup = e.Startup; s.StartMinimized = e.StartMinimized; s.CloseToTray = e.CloseToTray; s.Notifications = e.Notifications;
        s.Monitoring = Runtime.SafeMode ? false : e.Monitoring; s.Theme = e.Theme; s.EnableDelaySeconds = e.EnableDelaySeconds; s.RestoreDelaySeconds = e.RestoreDelaySeconds;
        s.PollSeconds = e.PollSeconds; s.LaunchTimeoutSeconds = e.LaunchTimeoutSeconds; s.DisplayId = e.DisplayId;
        s.OnlineCovers = e.OnlineCovers; s.Language = e.Language; s.LibraryView = e.LibraryView;
        Runtime.ApplyLanguage(); Filter();
        Raise(nameof(MonitoringStatus)); Raise(nameof(PauseLabel)); ThemeChanged?.Invoke();
    }
    public void ToggleMonitoring()
    {
        if (Runtime.SafeMode) throw new InvalidOperationException(L.Get("Режим --safe: автоматическое управление отключено для проверки интерфейса."));
        Runtime.Settings.Monitoring = !Runtime.Settings.Monitoring;
        EditSettings.Monitoring = Runtime.Settings.Monitoring;
        Runtime.SettingsRepository.Save(Runtime.Settings);
        Raise(nameof(EditSettings)); Raise(nameof(MonitoringStatus)); Raise(nameof(PauseLabel));
    }
    public async Task RefreshDisplaysAsync()
    {
        var values = await Task.Run(() => Runtime.Hdr.GetDisplaysAsync()); Displays.Clear(); foreach (var d in values) Displays.Add(d);
    }
    public string DiagnosticReport() => JsonSerializer.Serialize(new
    {
        Version = "1.2.4", OS = Environment.OSVersion.Version.ToString(), Runtime.SafeMode, Language = Localization.Language, Theme = Runtime.Settings.Theme,
        HDR = HdrStatus, State = EngineStatus, Monitoring = Runtime.Settings.Monitoring,
        Displays = Displays.Select(d => new { d.Name, d.Supported, d.Enabled, d.Active, d.ModernApi, d.Flags }),
        Games = Games.Count, StructureSizes = WindowsHdrController.StructureSizes
    }, new JsonSerializerOptions { WriteIndented = true }); // No game paths, usernames or raw exception text.
}

