using System.Text.Json;
using System.Text.Json.Serialization;
namespace AutoHDRManager.Core;

public enum HdrState { Idle, GameDetected, WaitingToEnable, EnablingHDR, HDRActive, WaitingToRestore, RestoringHDR, Suspended, Error }
public enum LaunchKind { Executable, Command, PlatformUri }
public sealed record Game
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Игра";
    public string ExecutablePath { get; set; } = "";
    public string? CoverPath { get; set; }
    public string? CoverSource { get; set; }
    public LaunchKind LaunchKind { get; set; }
    public string? LaunchTarget { get; set; }
    public string? Arguments { get; set; }
}
public sealed record Settings
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
    public string Language { get; set; } = "System";
    public string LibraryView { get; set; } = "Grid";
    public bool Startup { get; set; }
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool Notifications { get; set; } = true;
    public bool Monitoring { get; set; } = true;
    public string Theme { get; set; } = "Default";
    public double EnableDelaySeconds { get; set; } = 3;
    public double RestoreDelaySeconds { get; set; } = 2;
    public double PollSeconds { get; set; } = 1;
    public double LaunchTimeoutSeconds { get; set; } = 60;
    public string? DisplayId { get; set; }
    public bool OnlineCovers { get; set; }
    public void Validate()
    {
        EnableDelaySeconds = Clamp(EnableDelaySeconds, 0, 60, 3);
        RestoreDelaySeconds = Clamp(RestoreDelaySeconds, 0, 60, 2);
        PollSeconds = Clamp(PollSeconds, .25, 10, 1);
        LaunchTimeoutSeconds = Clamp(LaunchTimeoutSeconds, 5, 300, 60);
        if (Theme is not ("Dark" or "Light" or "Default")) Theme = "Default";
        if (Language is not ("System" or "ru-RU" or "en-US")) Language = "System";
        if (LibraryView is not ("Grid" or "List")) LibraryView = "Grid";
    }
    private static double Clamp(double x, double min, double max, double fallback) => double.IsFinite(x) ? Math.Clamp(x, min, max) : fallback;
}
public readonly record struct ProcessIdentity(int Pid, long CreationTime);
public sealed record GameProcess(ProcessIdentity Identity, string? Path, string Name);
public sealed record Display(string Id, string Name, bool Supported, bool Enabled, bool Active, bool ModernApi, uint Flags);
public sealed record Session
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset Started { get; init; }
    public string DisplayId { get; init; } = "";
    public bool Original { get; init; }
    public bool LastConfirmed { get; set; }
    public bool ChangedByUs { get; set; }
    public bool WritePending { get; set; }
    public bool ManualOverride { get; set; }
    public List<ProcessIdentity> Processes { get; set; } = [];
}
public static class GamePaths
{
    public static string Normalize(string path) => Path.GetFullPath(path.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar);
    public static bool Equal(string? a, string? b) => a is not null && b is not null && StringComparer.OrdinalIgnoreCase.Equals(Normalize(a), Normalize(b));
    public static void Validate(Game game)
    {
        game.ExecutablePath = Normalize(game.ExecutablePath);
        if (!string.Equals(Path.GetExtension(game.ExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Выберите исполняемый файл EXE.");
        if (!File.Exists(game.ExecutablePath)) throw new FileNotFoundException("EXE игры не найден.", game.ExecutablePath);
        if (string.IsNullOrWhiteSpace(game.Name)) game.Name = Path.GetFileNameWithoutExtension(game.ExecutablePath);
    }
}
