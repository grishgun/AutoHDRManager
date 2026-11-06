using System.Reflection;

namespace AutoHDRManager.Core;

/// <summary>Public product version and unabridged diagnostic build identity.</summary>
public sealed record ApplicationVersion(string Diagnostic)
{
    public string Display
    {
        get
        {
            var version = Version.Parse(Diagnostic.Split('-', '+')[0]);
            return $"v{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        }
    }
    public static ApplicationVersion FromAssembly(Assembly assembly) => new(
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version!.ToString());
}
