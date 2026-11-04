using L = AutoHDRManager.Core.Localization;
using System.Diagnostics;
using AutoHDRManager.Core;
using AutoHDRManager.Windows;
using AutoHDRManager.App.Services;
using AutoHDRManager.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AutoHDRManager.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private readonly AppRuntime runtime;
    private readonly TrayService tray;
    private readonly string[] arguments;
    private volatile bool exiting;
    private bool busy;
    private bool uiReady;
    private int queuedLaunches;
    private readonly nint hwnd;
    public MainWindow(SingleInstance instance, string[] args)
    {
        arguments = args;
        var notifications = new DispatcherNotifications(action => DispatcherQueue.TryEnqueue(action));
        runtime = new AppRuntime(notifications, args.Contains("--safe"), AppDataPath.Resolve(args));
        ViewModel = new(runtime, DispatcherQueue);
        InitializeComponent();
        Navigation.SelectedItem = Navigation.MenuItems[0];
        Root.DataContext = ViewModel;
        hwnd = WindowNative.GetWindowHandle(this);
        var iconFile = Path.Combine(AppContext.BaseDirectory, "Assets", "Hdr.ico");
        if (File.Exists(iconFile)) AppWindow.SetIcon(iconFile);
        tray = new TrayService(hwnd); notifications.Target = tray;
        tray.OpenRequested += ShowWindow;
        tray.PauseRequested += () => _ = Guard(() => { ViewModel.ToggleMonitoring(); return Task.CompletedTask; });
        tray.ExitRequested += () => _ = ExitAsync();
        ViewModel.StatusChanged += () => tray.Update(ViewModel.HdrStatus, runtime.Settings.Monitoring);
        ViewModel.ThemeChanged += ApplyTheme;
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1360, 820));
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.PreferredMinimumWidth = 620;
        try { SystemBackdrop = new MicaBackdrop(); } catch { Root.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]; }
        ApplyTheme();
        SyncPresentation(); uiReady = true;
        Localization.Changed += () => { SyncPresentation(); UpdateMonitorDetails(); tray.Update(ViewModel.HdrStatus, runtime.Settings.Monitoring); };
        Activated += (_, _) => runtime.ApplyLanguage();
        Navigation.DisplayModeChanged += (_, _) => NavFooter.Visibility = Navigation.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        Navigation.PaneOpened += (_, _) => NavFooter.Visibility = Visibility.Visible;
        Navigation.PaneClosed += (_, _) => NavFooter.Visibility = Visibility.Collapsed;
        AppWindow.Closing += (sender, e) => { if (exiting) return; e.Cancel = true; if (runtime.Settings.CloseToTray) AppWindow.Hide(); else _ = ExitAsync(); };
    }
    public void Start()
    {
        runtime.Start();
        var request = LaunchRequest.Parse(arguments);
        if (!request.Background && !runtime.Settings.StartMinimized) ShowWindow();
        try { if (!runtime.SafeMode) runtime.Shortcuts.RefreshExisting(runtime.Games); }
        catch (Exception ex) { ViewModel.Error = ex.Message; runtime.Log.Write("ShortcutRefreshFailed", new { ex.Message }); tray.Show(L.Get("Ярлыки требуют внимания"), ex.Message); }
        if (request.GameId is Guid id) EnqueueLaunch(id);
        _ = Guard(ViewModel.LoadCoversAsync);
    }
    public bool EnqueueLaunch(Guid id)
    {
        if (exiting) return false;
        if (Interlocked.Increment(ref queuedLaunches) > 16) { Interlocked.Decrement(ref queuedLaunches); return false; }
        bool accepted = DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (exiting) return;
                runtime.Log.Write("LaunchRequestReceived", new { GameId = id });
                await runtime.LaunchAsync(id);
            }
            catch (OperationCanceledException) when (exiting) { }
            catch (Exception ex) { ViewModel.Error = ex.Message; runtime.Log.Write("ShortcutLaunchFailed", new { GameId = id, ex.Message }); tray.Show(L.Get("Игра не запущена"), ex.Message); ShowWindow(); }
            finally { Interlocked.Decrement(ref queuedLaunches); }
        });
        if (!accepted) Interlocked.Decrement(ref queuedLaunches);
        return accepted;
    }
    public void ShowWindow() { AppWindow.Show(); Activate(); TrayService.RestoreWindow(hwnd); runtime.Log.Write("WindowOpened"); }
    private void ApplyTheme()
    {
        Root.RequestedTheme = runtime.Settings.Theme switch { "Light" => ElementTheme.Light, "Default" => ElementTheme.Default, _ => ElementTheme.Dark };
        AppWindow.TitleBar.PreferredTheme = runtime.Settings.Theme switch { "Light" => Microsoft.UI.Windowing.TitleBarTheme.Light, "Dark" => Microsoft.UI.Windowing.TitleBarTheme.Dark, _ => Microsoft.UI.Windowing.TitleBarTheme.UseDefaultAppMode };
    }
    private async Task Guard(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        try { await action(); }
        catch (Exception ex) { ViewModel.Error = ex.Message; runtime.Log.Write("UiOperationError", new { ex.Message }); }
        finally { busy = false; }
    }
    private async Task<string?> PickAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker(); InitializeWithWindow.Initialize(picker, hwnd);
        foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
        return (await picker.PickSingleFileAsync())?.Path;
    }
    private async void Navigate(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (LibraryPage == null) return;
        var tag = (e.SelectedItem as NavigationViewItem)?.Tag as string;
        LibraryPage.Visibility = tag == "library" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = tag == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "settings") await Guard(async () =>
        {
            await ViewModel.RefreshDisplaysAsync();
            MonitorPicker.SelectedItem = ViewModel.Displays.FirstOrDefault(d => d.Id == ViewModel.EditSettings.DisplayId) ?? ViewModel.Displays.FirstOrDefault(d => d.Supported) ?? ViewModel.Displays.FirstOrDefault();
            ThemePicker.SelectedItem = ThemePicker.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == ViewModel.EditSettings.Theme);
            UpdateMonitorDetails();
        });
    }
    private void UpdateMonitorDetails()
    {
        var d = MonitorPicker.SelectedItem as Display;
        MonitorDetails.Text = d == null ? L.Get("Активный монитор не найден.") : L.Format("MonitorDetails", L.Get(d.Supported ? "Yes" : "No"), L.Get(d.Enabled ? "On" : "Off"));
    }
    private void SyncPresentation()
    {
        bool ready = uiReady; uiReady = false;
        foreach (var picker in new[] { LanguagePicker, SettingsLanguagePicker })
        {
            picker.SelectedItem = null; // Refresh WinUI's cached selection label after a language change.
            picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == runtime.Settings.Language);
        }
        ThemePicker.SelectedItem = null;
        ThemePicker.SelectedItem = ThemePicker.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == runtime.Settings.Theme);
        ViewPicker.SelectedItem = null;
        ViewPicker.SelectedItem = ViewPicker.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == runtime.Settings.LibraryView);
        GridViewButton.IsChecked = runtime.Settings.LibraryView == "Grid";
        ListViewButton.IsChecked = runtime.Settings.LibraryView == "List";
        uiReady = ready;
    }
    private async void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!uiReady || (sender as ComboBox)?.SelectedItem is not ComboBoxItem item || (string)item.Tag == runtime.Settings.Language) return;
        await Guard(() => { ViewModel.SetPresentation(language: (string)item.Tag); SyncPresentation(); return Task.CompletedTask; });
    }
    private async void PreferredThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!uiReady || ThemePicker.SelectedItem is not ComboBoxItem item || (string)item.Tag == runtime.Settings.Theme) return;
        await Guard(() => { ViewModel.SetPresentation(theme: (string)item.Tag); return Task.CompletedTask; });
    }
    private async void PreferredViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!uiReady || ViewPicker.SelectedItem is not ComboBoxItem item || (string)item.Tag == runtime.Settings.LibraryView) return;
        await Guard(() => { ViewModel.SetPresentation(view: (string)item.Tag); SyncPresentation(); return Task.CompletedTask; });
    }
    private void MonitorChanged(object sender, SelectionChangedEventArgs e) { if (MonitorDetails != null) UpdateMonitorDetails(); }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (ViewModel != null) ViewModel.Search = ((TextBox)sender).Text; }
    private async void ViewModeClick(object sender, RoutedEventArgs e) => await Guard(() => { ViewModel.SetPresentation(view: (string)((FrameworkElement)sender).Tag); SyncPresentation(); return Task.CompletedTask; });
    private void LibrarySizeChanged(object sender, SizeChangedEventArgs e)
    {
        // One toolbar row on large windows; a full-width second row on compact windows.
        // ItemsWrapGrid handles columns; the cards themselves never stretch to fill empty space.
        if (LibraryToolbar == null) return;
        bool wide = e.NewSize.Width >= 1020;
        LibraryHeader.RowSpacing = wide ? 0 : 20;
        Grid.SetRow(LibraryToolbar, wide ? 0 : 1);
        Grid.SetColumn(LibraryToolbar, wide ? 1 : 0);
        Grid.SetColumnSpan(LibraryToolbar, wide ? 1 : 2);
        LibrarySearch.Width = wide ? 250 : double.NaN;
    }
    private void CardPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => ((Grid)sender).Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
    private void CardPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => ((Grid)sender).Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    private async void AddClick(object sender, RoutedEventArgs e) => await Guard(async () => { var path = await PickAsync(".exe"); if (path != null) { await ViewModel.AddAsync(path); await OfferCoverAsync(ViewModel.Games.Last()); } });
    private async void ProcessesClick(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var processes = await Task.Run(() => runtime.Monitor.SnapshotAsync());
        var showSystem = new CheckBox { Content = L.Get("Показать системные процессы") };
        var list = new ListView { Height = 360, SelectionMode = ListViewSelectionMode.Single };
        list.ItemTemplate = (DataTemplate)Root.Resources["ProcessTemplate"];
        using var thumbnails = new CancellationTokenSource();
        list.ContainerContentChanging += async (s, itemArgs) =>
        {
            if (itemArgs.InRecycleQueue || itemArgs.Item is not ProcessChoice choice || choice.Process.Path == null || choice.Icon != null) return;
            try
            {
                var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(choice.Process.Path.ToUpperInvariant()));
                var icon = await runtime.Covers.GetAsync(new Game { Id = new Guid(hash.AsSpan(0, 16)), ExecutablePath = choice.Process.Path }, thumbnails.Token);
                choice.Icon = icon == null ? null : new Uri(icon).AbsoluteUri;
            }
            catch (OperationCanceledException) { }
        };
        var search = new TextBox { PlaceholderText = L.Get("Поиск по названию или пути") };
        void Filter()
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            list.ItemsSource = processes.Where(p => showSystem.IsChecked == true || (p.Path != null && !p.Path.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase)))
                .Where(p => (p.Name + p.Path).Contains(search.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Name)
                .Select(p => new ProcessChoice(p)).ToList();
        }
        showSystem.Checked += (_, _) => Filter(); showSystem.Unchecked += (_, _) => Filter(); search.TextChanged += (_, _) => Filter(); Filter();
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(new TextBlock { Text = L.Get("Выберите игровой процесс, а не Steam или другой лаунчер."), TextWrapping = TextWrapping.Wrap }); panel.Children.Add(search); panel.Children.Add(showSystem); panel.Children.Add(list);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = L.Get("Из запущенных процессов"), Content = panel, PrimaryButtonText = L.Get("Добавить"), CloseButtonText = L.Get("Отмена"), DefaultButton = ContentDialogButton.Primary };
        var result = await dialog.ShowAsync(); thumbnails.Cancel();
        if (result == ContentDialogResult.Primary && list.SelectedItem is ProcessChoice choice)
        {
            if (choice.Process.Path == null) throw new InvalidOperationException(L.Get("Путь процесса недоступен. Выберите его EXE вручную."));
            await ViewModel.AddAsync(choice.Process.Path); await OfferCoverAsync(ViewModel.Games.Last());
        }
    });
    public sealed class ProcessChoice(GameProcess process) : Observable
    {
        public GameProcess Process { get; } = process;
        public string Label => $"{Process.Name} · PID {Process.Identity.Pid}";
        public string PathLabel => Process.Path ?? L.Get("Путь недоступен");
        private string? icon;
        public string? Icon { get => icon; set => Set(ref icon, value); }
    }
    private static GameViewModel Item(object sender) => (GameViewModel)((FrameworkElement)sender).Tag;
    private async void LaunchClick(object sender, RoutedEventArgs e) => await Guard(() => ViewModel.LaunchAsync(Item(sender)));
    private async void PinClick(object sender, RoutedEventArgs e) => await Guard(() => CreateShortcutAsync(Item(sender)));
    private async void ShortcutClick(object sender, RoutedEventArgs e) => await Guard(() => CreateShortcutAsync(Item(sender)));
    private async void GroupingClick(object sender, RoutedEventArgs e) => await Guard(() => GroupingDiagnosticsDialog.ShowAsync(Root, Item(sender).Model, runtime.Shortcuts));
    private async Task CreateShortcutAsync(GameViewModel item)
    {
        string path = runtime.Shortcuts.Create(item.Model);
        runtime.Log.Write("GameShortcutCreated", new { GameId = item.Model.Id, Mechanism = "ShellLink", Pinned = "NotConfirmed" });
        GameShortcuts.OpenLocation(path);
        var dialog = new ContentDialog {
            XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = L.Get("ShortcutCreated"),
            Content = L.Get("PinInstructions"), CloseButtonText = L.Get("Закрыть")
        };
        await dialog.ShowAsync();
    }
    private async void RemoveClick(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var game = Item(sender); var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = L.Get("Удалить из библиотеки?"), Content = game.Name + "\n" + L.Get("Файлы игры останутся на диске."), PrimaryButtonText = L.Get("Удалить"), CloseButtonText = L.Get("Отмена") };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) ViewModel.Remove(game);
    });
    private async void EditClick(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var vm = Item(sender); var game = vm.Model with { };
        var name = new TextBox { Header = L.Get("Название"), Text = game.Name };
        var path = new TextBox { Header = L.Get("Фактический EXE для отслеживания"), Text = game.ExecutablePath };
        var browse = new Button { Content = L.Get("Выбрать EXE") }; browse.Click += async (_, _) => { try { var picked = await PickAsync(".exe"); if (picked != null) path.Text = picked; } catch (Exception ex) { ViewModel.Error = ex.Message; } };
        var kind = new ComboBox { Header = L.Get("Способ запуска"), ItemsSource = new[] { L.Get("Прямой EXE"), L.Get("Пользовательская команда"), L.Get("URI платформы") }, SelectedIndex = (int)game.LaunchKind, HorizontalAlignment = HorizontalAlignment.Stretch };
        var target = new TextBox { Header = L.Get("EXE команды или URI (например steam://rungameid/…)"), Text = game.LaunchTarget ?? "" };
        var arguments = new TextBox { Header = L.Get("Аргументы команды / EXE"), Text = game.Arguments ?? "" };
        var panel = new StackPanel { Spacing = 12 }; foreach (var control in new UIElement[] { name, path, browse, kind, target, arguments }) panel.Children.Add(control);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = L.Get("Настройки игры"), Content = new ScrollViewer { Content = panel, MaxHeight = 500 }, PrimaryButtonText = L.Get("Сохранить"), CloseButtonText = L.Get("Отмена") };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) { game.Name = name.Text; game.ExecutablePath = path.Text; game.LaunchKind = (LaunchKind)kind.SelectedIndex; game.LaunchTarget = target.Text; game.Arguments = arguments.Text; await ViewModel.UpdateAsync(vm, game); runtime.Shortcuts.RefreshExisting(runtime.Games); }
    });
    private async void CoverClick(object sender, RoutedEventArgs e) => await Guard(async () => { var vm = Item(sender); var path = await PickAsync(".png", ".jpg", ".jpeg", ".webp"); if (path != null) { var game = vm.Model with { CoverPath = await runtime.Covers.ImportAsync(path), CoverSource = null }; await ViewModel.UpdateAsync(vm, game); } });
    private async void FindCoverClick(object sender, RoutedEventArgs e) => await Guard(() => OfferCoverAsync(Item(sender)));
    private async void FolderClick(object sender, RoutedEventArgs e) => await Guard(() => { OpenFolder(Path.GetDirectoryName(Item(sender).Path)!); return Task.CompletedTask; });
    private async void PauseClick(object sender, RoutedEventArgs e) => await Guard(() => { ViewModel.ToggleMonitoring(); return Task.CompletedTask; });
    private async void CheckHdrClick(object sender, RoutedEventArgs e) => await Guard(async () => { await ViewModel.RefreshDisplaysAsync(); MonitorPicker.SelectedItem = ViewModel.Displays.FirstOrDefault(d => d.Id == runtime.Settings.DisplayId) ?? ViewModel.Displays.FirstOrDefault(d => d.Supported); UpdateMonitorDetails(); });
    private async void SaveSettingsClick(object sender, RoutedEventArgs e) => await Guard(() =>
    {
        if (!string.IsNullOrWhiteSpace(CoverApiKey.Password)) { runtime.OnlineCovers.SaveKey(CoverApiKey.Password); CoverApiKey.Password = ""; }
        if (ViewModel.EditSettings.OnlineCovers && !runtime.OnlineCovers.HasKey) throw new InvalidOperationException(L.Get("Для SteamGridDB нужен API-ключ. Локальные обложки работают без него."));
        ViewModel.EditSettings.Theme = (ThemePicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "Default";
        ViewModel.EditSettings.DisplayId = (MonitorPicker.SelectedItem as Display)?.Id;
        ViewModel.EditSettings.LibraryView = (ViewPicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "Grid"; ViewModel.SaveSettings(); SavedLabel.Text = L.Get("Настройки сохранены."); return Task.CompletedTask;
    });
    private async void RemoveKeyClick(object sender, RoutedEventArgs e) => await Guard(() => { runtime.OnlineCovers.RemoveKey(); ViewModel.EditSettings.OnlineCovers = false; runtime.Settings.OnlineCovers = false; var saved = runtime.SettingsRepository.Load(); saved.OnlineCovers = false; runtime.SettingsRepository.Save(saved); SavedLabel.Text = L.Get("API-ключ удалён."); return Task.CompletedTask; });
    private async Task OfferCoverAsync(GameViewModel game)
    {
        if (!runtime.Settings.OnlineCovers || !runtime.OnlineCovers.HasKey) return;
        var results = await runtime.OnlineCovers.SearchAsync(game.Name);
        if (results.Count == 0) return;
        var choices = new ListView { ItemsSource = results, SelectedIndex = 0, MaxHeight = 320 };
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = L.Get("Обложка из SteamGridDB"), Content = choices, PrimaryButtonText = L.Get("Загрузить"), CloseButtonText = L.Get("Оставить иконку") };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && choices.SelectedItem is CoverGame chosen)
        {
            var cover = await runtime.OnlineCovers.DownloadAsync(chosen.Id);
            await ViewModel.UpdateAsync(game, game.Model with { CoverPath = cover.Path, CoverSource = cover.Source });
        }
    }
    private async void RefreshCoversClick(object sender, RoutedEventArgs e) => await Guard(ViewModel.LoadCoversAsync);
    private async void ClearCoversClick(object sender, RoutedEventArgs e) => await Guard(async () => { foreach (var g in ViewModel.Games) g.Cover = null; runtime.Covers.ClearCache(); await ViewModel.LoadCoversAsync(); });
    private async void DiagnosticsClick(object sender, RoutedEventArgs e) => await Guard(async () => { await ViewModel.RefreshDisplaysAsync(); var data = new DataPackage(); data.SetText(ViewModel.DiagnosticReport()); Clipboard.SetContent(data); DiagnosticFeedback.Text = L.Get("Отчёт скопирован. Личные пути не включены."); });
    private void LogsClick(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(runtime.Root, "Logs"));
    private async void LicensesClick(object sender, RoutedEventArgs e) => await Guard(() => { OpenFolder(Path.Combine(AppContext.BaseDirectory, "Assets", "Licenses")); return Task.CompletedTask; });
    private void DataClick(object sender, RoutedEventArgs e) => OpenFolder(runtime.Root);
    private static void OpenFolder(string path) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException(L.Get("Папка не найдена.")); using var p = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
    private async void ExitClick(object sender, RoutedEventArgs e) => await ExitAsync();
    private async Task ExitAsync()
    {
        if (exiting) return; exiting = true;
        try { await runtime.StopAsync(); }
        catch (Exception ex) { runtime.Log.Write("ShutdownRestoreFailed", new { ex.Message }); tray.Show(L.Get("HDR не восстановлен"), L.Get("Не удалось подтвердить восстановление. Журнал сессии сохранён.")); }
        finally { tray.Dispose(); Close(); Application.Current.Exit(); }
    }
    private sealed class DispatcherNotifications(Func<Microsoft.UI.Dispatching.DispatcherQueueHandler, bool> dispatch) : INotificationService
    {
        public INotificationService? Target { get; set; }
        public void Show(string title, string message) => dispatch(() => Target?.Show(title, message));
    }
}




