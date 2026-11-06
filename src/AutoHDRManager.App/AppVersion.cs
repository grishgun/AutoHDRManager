using AutoHDRManager.Core;

namespace AutoHDRManager.App;

internal static class AppVersion
{
    internal static ApplicationVersion Current { get; } = ApplicationVersion.FromAssembly(typeof(AppVersion).Assembly);
}
