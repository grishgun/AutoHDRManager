using Microsoft.Win32;
using AutoHDRManager.Core;

namespace AutoHDRManager.Windows;

public sealed class StartupManager : IStartupManager
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "AutoHDRManager";
    public bool IsEnabled { get { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue(Name) is string; } }
    public string? RegisteredCommand { get { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue(Name) as string; } }
    public static string ExpectedCommand => $"\"{Environment.ProcessPath ?? throw new InvalidOperationException("Путь к приложению неизвестен.")}\" --background";
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key, true);
        if (enabled) key.SetValue(Name, ExpectedCommand, RegistryValueKind.String); else key.DeleteValue(Name, false);
    }
    // Called only when the user previously enabled autostart and an entry still exists.
    public void RefreshExistingRegistration() { if (IsEnabled && RegisteredCommand != ExpectedCommand) SetEnabled(true); }
}
