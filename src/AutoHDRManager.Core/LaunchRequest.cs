namespace AutoHDRManager.Core;

public sealed record LaunchRequest(Guid? GameId = null, bool Background = false, string? VideoPath = null)
{
    public static LaunchRequest Parse(string[] args)
    {
        int video = Array.IndexOf(args, "--open-video");
        if (video >= 0)
        {
            if (args.Length != 2 || video != 0) throw new ArgumentException("VideoInvalidPath");
            return new(null, true, Core.VideoPath.Validate(args[1]));
        }
        int index = Array.IndexOf(args, "--launch-game");
        if (index < 0) return new(null, args.Contains("--background"));
        if (args.Count(a => a == "--launch-game") != 1 || index + 1 >= args.Length ||
            !Guid.TryParseExact(args[index + 1], "D", out var id) || id == Guid.Empty)
            throw new ArgumentException("Ожидается --launch-game <GameId в формате GUID>.");
        return new(id, true);
    }
}
