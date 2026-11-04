using System.Diagnostics;
using AutoHDRManager.Core;

namespace AutoHDRManager.Windows;

public static class GameLauncher
{
    public static void Launch(Game game)
    {
        GamePaths.Validate(game);
        var info = new ProcessStartInfo { WorkingDirectory = Path.GetDirectoryName(game.ExecutablePath)!, UseShellExecute = true };
        switch (game.LaunchKind)
        {
            case LaunchKind.Executable: info.FileName = game.ExecutablePath; info.Arguments = game.Arguments ?? ""; break;
            case LaunchKind.Command:
                if (string.IsNullOrWhiteSpace(game.LaunchTarget) || !File.Exists(game.LaunchTarget)) throw new FileNotFoundException("Исполняемый файл команды запуска не найден.");
                info.FileName = game.LaunchTarget; info.Arguments = game.Arguments ?? ""; break;
            case LaunchKind.PlatformUri:
                if (!Uri.TryCreate(game.LaunchTarget, UriKind.Absolute, out var uri) || uri.Scheme is not ("steam" or "com.epicgames.launcher" or "goggalaxy")) throw new ArgumentException("Разрешены URI Steam, Epic Games и GOG Galaxy.");
                info.FileName = uri.AbsoluteUri; break;
        }
        using var process = Process.Start(info);
        if (process == null && game.LaunchKind != LaunchKind.PlatformUri) throw new InvalidOperationException("Не удалось передать команду запуска Windows.");
    }
}
