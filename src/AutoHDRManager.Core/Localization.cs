using System.Globalization;
using System.Resources;

namespace AutoHDRManager.Core;

/// <summary>Standard .NET resource catalogs, shared by WinUI, native tray and notifications.</summary>
public static class Localization
{
    private static readonly ResourceManager resources = new("AutoHDRManager.Core.Resources.Strings", typeof(Localization).Assembly);
    private static CultureInfo culture = CultureInfo.GetCultureInfo(Resolve("System", CultureInfo.CurrentUICulture.Name));
    public static event Action? Changed;
    public static string Language => culture.Name;
    public static string Resolve(string? preference, string systemLanguage) => preference switch
    {
        "ru-RU" => "ru-RU", "en-US" => "en-US",
        _ => systemLanguage.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "ru-RU" : "en-US"
    };
    public static void SetLanguage(string? preference, string? systemLanguage = null)
    {
        var next = CultureInfo.GetCultureInfo(Resolve(preference, systemLanguage ?? CultureInfo.InstalledUICulture.Name));
        if (culture.Name == next.Name) return;
        culture = next; Changed?.Invoke();
    }
    public static string Get(string key) => resources.GetString(key, culture) ?? $"[{key}]";
    public static string Format(string key, params object?[] values) => string.Format(culture, Get(key), values);
    // The HDR engine keeps its original messages/logs. Translate only at the presentation boundary.
    public static string Message(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;
        return resources.GetString(message, culture) ?? (IsKnownTranslation(message) ? message : Get("UnknownError"));
    }
    private static bool IsKnownTranslation(string message) => resources.GetResourceSet(culture, true, true)!
        .Cast<System.Collections.DictionaryEntry>().Any(e => string.Equals(e.Value as string, message, StringComparison.Ordinal));
    public static IReadOnlyDictionary<string, string> Catalog(string language) => resources.GetResourceSet(CultureInfo.GetCultureInfo(language), true, true)!
        .Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
}
