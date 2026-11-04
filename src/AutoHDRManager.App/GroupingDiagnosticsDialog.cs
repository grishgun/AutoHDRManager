using AutoHDRManager.Core;
using AutoHDRManager.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using L = AutoHDRManager.Core.Localization;

namespace AutoHDRManager.App;

internal static class GroupingDiagnosticsDialog
{
    public static async Task ShowAsync(FrameworkElement root, Game game, GameShortcuts shortcuts)
    {
        var links = await Task.Run(() => shortcuts.ReadExisting(game.Id));
        var windows = await Task.Run(() => WindowIdentityDiagnostics.Capture());
        var picker = new ComboBox { Header = L.Get("GroupingPickWindow"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var window in windows.OrderBy(w => w.Title))
            picker.Items.Add(new ComboBoxItem { Content = $"{window.Title.Trim()} · PID {window.Pid}", Tag = window });
        // Only a path match is preselected. A launcher and its actual game may be different processes.
        picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => GamePaths.Equal(((WindowIdentityInfo)i.Tag).ExecutablePath, game.ExecutablePath));
        var details = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12, MinHeight = 200 };
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var copy = new Button { Content = L.Get("GroupingCopy") };
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var refresh = new Button { Content = L.Get("GroupingRefresh") };
        WindowIdentityInfo? selected = (picker.SelectedItem as ComboBoxItem)?.Tag as WindowIdentityInfo;
        void Render()
        {
            var text = new List<string> { game.Name, $"GameId: {game.Id:D}", L.Get("GroupingExpectedId") + ": " + GameShortcuts.GameAppId(game.Id) };
            if (links.Count == 0) text.Add(L.Get("GroupingNoShortcut"));
            foreach (var link in links)
            {
                text.Add(L.Get("GroupingShortcut") + ": " + link.Path);
                text.Add("AppUserModelID: " + link.AppId);
                text.Add("Target: " + link.Target); text.Add("Arguments: " + link.Arguments); text.Add("Icon: " + link.Icon);
            }
            if (selected != null)
            {
                text.Add($"\nHWND: {selected.Hwnd}   PID: {selected.Pid}");
                text.Add("EXE: " + (selected.ExecutablePath ?? L.Get("GroupingUnavailable")));
                text.Add("Window: " + selected.Title + "   Class: " + selected.ClassName);
                text.Add("System.AppUserModel.ID: " + Property(selected.WindowAppUserModelId));
                text.Add("GetApplicationUserModelId: " + Property(selected.ProcessApplicationIdentity));
                note.Text = selected.WindowAppUserModelId.Status == "Present" ? L.Get("GroupingExplicitFound") : L.Get("GroupingNoReadableId");
            }
            else note.Text = L.Get("GroupingSelectHelp");
            text.Add("\n" + L.Get("GroupingProcessLimit"));
            details.Text = string.Join("\n", text); feedback.Text = "";
        }
        picker.SelectionChanged += (_, _) => { selected = (picker.SelectedItem as ComboBoxItem)?.Tag as WindowIdentityInfo; Render(); };
        refresh.Click += async (_, _) =>
        {
            if (selected == null) return;
            refresh.IsEnabled = false; picker.IsEnabled = false;
            try
            {
                var original = selected;
                var current = await Task.Run(() => WindowIdentityDiagnostics.Capture(original.Pid));
                // Refuse a recycled HWND/PID. No interpretation of stale snapshots as current identity.
                selected = current.FirstOrDefault(w => w.Hwnd == original.Hwnd && w.ProcessCreationTime == original.ProcessCreationTime);
                Render(); if (selected == null) note.Text = L.Get("GroupingWindowGone");
            }
            catch { note.Text = L.Get("GroupingReadFailed"); }
            finally { refresh.IsEnabled = true; picker.IsEnabled = true; }
        };
        copy.Click += (_, _) => { var data = new DataPackage(); data.SetText(details.Text + "\n" + note.Text); Clipboard.SetContent(data); feedback.Text = L.Get("GroupingCopied"); };
        Render();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; actions.Children.Add(refresh); actions.Children.Add(copy);
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = L.Get("GroupingSelectHelp"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(picker); panel.Children.Add(details); panel.Children.Add(note); panel.Children.Add(actions); panel.Children.Add(feedback);
        var dialog = new ContentDialog { XamlRoot = root.XamlRoot, RequestedTheme = root.ActualTheme, Title = L.Get("GroupingTitle"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 480, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, CloseButtonText = L.Get("Закрыть") };
        await dialog.ShowAsync();
    }
    private static string Property(IdentityProperty property)
    {
        string value = property.Value ?? L.Get(property.Status is "Absent" or "NoApplicationIdentity" ? "GroupingAbsent" : "GroupingUnavailable");
        return value + " [" + property.NativeResult + (property.VariantType is ushort type ? $", VT={type}" : "") + "]";
    }
}
