using AutoHDRManager.Core;
using AutoHDRManager.Windows;
using System.Text.Json;

Console.InputEncoding = System.Text.Encoding.UTF8;
Console.OutputEncoding = System.Text.Encoding.UTF8;

var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoHDRManager");
try
{
    if (args.Length == 2 && args[0] == "--window-identity")
    {
        int pid = int.Parse(args[1]);
        Console.WriteLine(JsonSerializer.Serialize(new { Timestamp = DateTimeOffset.UtcNow, Windows = WindowIdentityDiagnostics.Capture(pid), Limitation = WindowIdentityDiagnostics.ProcessShellIdLimitation }, new JsonSerializerOptions { WriteIndented = true })); return 0;
    }
    var controller = new WindowsHdrController(new JsonEventLog(Path.Combine(root, "Logs")));
    if (args.Contains("--display-paths"))
    {
        Console.WriteLine(JsonSerializer.Serialize(WindowsHdrController.ProbePaths(), new JsonSerializerOptions { WriteIndented = true })); return 0;
    }
    if (args.Contains("--taskbar-support"))
    {
        Console.WriteLine(JsonSerializer.Serialize(await TaskbarSupport.ProbeAsync(), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    if (args.Length == 2 && args[0] == "--read-shortcut")
    {
        Console.WriteLine(JsonSerializer.Serialize(GameShortcuts.ReadLink(Path.GetFullPath(args[1])), new JsonSerializerOptions { WriteIndented = true })); return 0;
    }
    if (args.Contains("--test-startup"))
    {
        await using var exclusive = new SingleInstance();
        if (!exclusive.IsPrimary) throw new InvalidOperationException("Закройте приложение перед проверкой автозапуска.");
        Console.WriteLine("Тест временно изменит только HKCU Run/AutoHDRManager, затем восстановит исходную запись. Введите ПОДТВЕРЖДАЮ:");
        if (Console.ReadLine() != "ПОДТВЕРЖДАЮ") return 2;
        const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        using var originalKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath);
        object? original = originalKey?.GetValue("AutoHDRManager", null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);
        var kind = original != null ? originalKey!.GetValueKind("AutoHDRManager") : Microsoft.Win32.RegistryValueKind.String;
        var startup = new StartupManager();
        try
        {
            startup.SetEnabled(true);
            if (!startup.IsEnabled || startup.RegisteredCommand != StartupManager.ExpectedCommand) throw new InvalidOperationException("Запись автозапуска не подтверждена.");
            startup.SetEnabled(false);
            if (startup.IsEnabled) throw new InvalidOperationException("Удаление записи автозапуска не подтверждено.");
        }
        finally
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath, true);
            if (original != null) key.SetValue("AutoHDRManager", original, kind); else key.DeleteValue("AutoHDRManager", false);
        }
        Console.WriteLine("PASS: добавление и удаление записи проверены; исходное значение восстановлено. Перезагрузка не выполнялась.");
        return 0;
    }
    if (args.Contains("--restore-test-state"))
    {
        await using var exclusive = new SingleInstance();
        if (!exclusive.IsPrimary) throw new InvalidOperationException("Закройте приложение перед восстановлением теста.");
        var journal = new SessionRepository(root);
        var pending = journal.Load() ?? throw new InvalidOperationException("Журнал теста отсутствует.");
        Console.WriteLine("Восстановление исходного состояния незавершённого теста. Введите ПОДТВЕРЖДАЮ:");
        if (Console.ReadLine() != "ПОДТВЕРЖДАЮ") return 2;
        await controller.SetAsync(pending.DisplayId, pending.Original);
        var restored = await controller.ReadAsync(pending.DisplayId);
        if (restored.Enabled != pending.Original) throw new InvalidOperationException("Восстановление не подтверждено.");
        journal.Clear(); Console.WriteLine($"PASS: восстановлен исходный HDR={restored.Enabled}"); return 0;
    }
    var displays = await controller.GetDisplaysAsync();
    Console.WriteLine(JsonSerializer.Serialize(new { OS = Environment.OSVersion.Version.ToString(), Sizes = WindowsHdrController.StructureSizes, Displays = displays }, new JsonSerializerOptions { WriteIndented = true }));
    if (args.Length == 2 && args[0] == "--restore-approved-test-hdr")
    {
        // Explicit recovery of the previously approved acceptance test; never choose another monitor.
        await using var exclusive = new SingleInstance();
        if (!exclusive.IsPrimary) throw new InvalidOperationException("Закройте менеджер перед восстановлением теста.");
        using var test = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(args[1])));
        var original = test.RootElement.GetProperty("Original").Deserialize<Display>() ?? throw new InvalidDataException("Нет исходного состояния.");
        var actual = displays.Single(d => d.Id == original.Id && d.Name == original.Name);
        if (!actual.Supported) throw new InvalidOperationException("Целевой монитор не поддерживает HDR.");
        if (actual.Enabled != original.Enabled) await controller.SetAsync(original.Id, original.Enabled);
        actual = await controller.ReadAsync(original.Id);
        if (actual.Enabled != original.Enabled || actual.Active != original.Active) throw new InvalidOperationException("Восстановление исходного HDR не подтверждено.");
        Console.WriteLine("RESTORED: " + JsonSerializer.Serialize(actual)); return 0;
    }
    if (args.Contains("--test-hdr"))
    {
        await using var exclusive = new SingleInstance();
        if (!exclusive.IsPrimary) throw new InvalidOperationException("Закройте Auto HDR Manager перед тестом переключения HDR.");
        var display = displays.FirstOrDefault(d => d.Supported) ?? throw new InvalidOperationException("HDR-дисплей не найден.");
        Console.WriteLine("Тест изменит HDR и затем восстановит исходное состояние. Экран может кратко погаснуть. Введите ПОДТВЕРЖДАЮ:");
        if (Console.ReadLine() != "ПОДТВЕРЖДАЮ") return 2;
        var journal = new SessionRepository(root);
        if (journal.Load() != null) throw new InvalidOperationException("Сначала завершите существующую HDR-сессию приложения.");
        var s = new Session { Started = DateTimeOffset.UtcNow, DisplayId = display.Id, Original = display.Enabled, LastConfirmed = display.Enabled, WritePending = true };
        journal.Save(s);
        try
        {
            await controller.SetAsync(display.Id, !display.Enabled);
            s.LastConfirmed = !display.Enabled; s.ChangedByUs = true; s.WritePending = false; journal.Save(s);
            Console.WriteLine("Подтверждён противоположный режим. Восстановление через 2 секунды.");
            await Task.Delay(2000);
        }
        finally
        {
            var actual = await controller.ReadAsync(display.Id);
            if (actual.Enabled == !display.Enabled) await controller.SetAsync(display.Id, display.Enabled);
            else if (actual.Enabled != display.Enabled) throw new InvalidOperationException("Результат неоднозначен: журнал сохранён, автоматическое восстановление пропущено.");
            journal.Clear();
        }
        Console.WriteLine("PASS: исходное состояние восстановлено.");
    }
    var processes = await new ProcessMonitor().SnapshotAsync();
    Console.WriteLine($"Process read: {processes.Count} accessible, {processes.Count(p => p.Path != null)} paths; own PID found: {processes.Any(p => p.Identity.Pid == Environment.ProcessId && p.Path != null)}");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
