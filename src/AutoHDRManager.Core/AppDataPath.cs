namespace AutoHDRManager.Core;

public static class AppDataPath
{
    public static string Resolve(string[] args)
    {
        int index = Array.IndexOf(args, "--ui-test-root");
        if (index < 0) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoHDRManager");
        if (!args.Contains("--safe") || args.Contains("--launch-game") || index + 1 >= args.Length || !Path.IsPathFullyQualified(args[index + 1]))
            throw new ArgumentException("--ui-test-root requires --safe, an absolute path and no game launch.");
        return Path.GetFullPath(args[index + 1]);
    }
}
