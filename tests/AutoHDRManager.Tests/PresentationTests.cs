using System.Text.Json;
using AutoHDRManager.Core;
using Xunit;

namespace AutoHDRManager.Tests;

public sealed class PresentationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "HdrPresentation-" + Guid.NewGuid());
    public PresentationTests() => Directory.CreateDirectory(root);
    [Theory]
    [InlineData("System", "ru-RU", "ru-RU")]
    [InlineData("System", "ru-BY", "ru-RU")]
    [InlineData("System", "en-GB", "en-US")]
    [InlineData("System", "ja-JP", "en-US")]
    [InlineData("ru-RU", "en-US", "ru-RU")]
    [InlineData("en-US", "ru-RU", "en-US")]
    public void LanguageSelection(string preference, string system, string expected) => Assert.Equal(expected, Localization.Resolve(preference, system));
    [Fact] public void CatalogsHaveIdenticalNonemptyKeys()
    {
        var en = Localization.Catalog("en-US"); var ru = Localization.Catalog("ru-RU");
        Assert.True(en.Count > 150); Assert.Equal(en.Keys.Order(), ru.Keys.Order());
        Assert.All(en.Values.Concat(ru.Values), value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.Equal("Game library", en["LibraryTitle"]); Assert.Equal("Библиотека игр", ru["LibraryTitle"]);
    }
    [Fact] public void LiteralUiResourceReferencesExistInBothCatalogs()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AutoHDRManager.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var source = Path.Combine(directory.FullName, "src");
        var patterns = new[] { @"(?:L|Localization)\.(?:Get|Format)\(""([^""]+)""", @"\{Binding \[([^\]]+)\], Source=\{StaticResource Strings\}" };
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(p => (p.EndsWith(".cs") || p.EndsWith(".xaml")) && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
            foreach (var pattern in patterns)
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), pattern))
                    foreach (var language in new[] { "en-US", "ru-RU" }) Assert.True(Localization.Catalog(language).ContainsKey(match.Groups[1].Value), $"Missing {language}: {match.Groups[1].Value} in {Path.GetFileName(file)}");
    }
    [Fact] public void IsolatedUiProfileRequiresSafeModeAndRejectsGameLaunch()
    {
        Assert.Equal(root, AppDataPath.Resolve(["--safe", "--ui-test-root", root]));
        Assert.Throws<ArgumentException>(() => AppDataPath.Resolve(["--ui-test-root", root]));
        Assert.Throws<ArgumentException>(() => AppDataPath.Resolve(["--safe", "--ui-test-root", root, "--launch-game", Guid.NewGuid().ToString()]));
        Assert.Throws<ArgumentException>(() => AppDataPath.Resolve(["--safe", "--ui-test-root", "relative"]));
    }
    [Fact] public void MissingResourceIsVisibleAndLanguageChangesNotify()
    {
        string old = Localization.Language; int changes = 0; void Handler() => changes++;
        Localization.Changed += Handler;
        try { Localization.SetLanguage("en-US"); Localization.SetLanguage("ru-RU"); Assert.True(changes > 0); Assert.Equal("[missing-test-key]", Localization.Get("missing-test-key")); }
        finally { Localization.Changed -= Handler; Localization.SetLanguage(old); }
    }
    [Fact] public void TrayAndNotificationsUseCurrentLanguage()
    {
        string old = Localization.Language;
        try {
            Localization.SetLanguage("en-US"); Assert.Equal("HDR enabled", Localization.Message("HDR включён")); Assert.Equal("Exit", Localization.Get("Выход"));
            Localization.SetLanguage("ru-RU"); Assert.Equal("HDR включён", Localization.Message("HDR включён")); Assert.Equal("Выход", Localization.Get("Выход"));
        } finally { Localization.SetLanguage(old); }
    }
    [Theory] [InlineData("ru-RU", "Dark")] [InlineData("en-US", "Light")] [InlineData("System", "Default")]
    public void LanguageAndThemePersistIndependently(string language, string theme)
    {
        var repo = new SettingsRepository(root); repo.Save(new Settings { Language = language, Theme = theme, Monitoring = false });
        var saved = repo.Load(); Assert.Equal(language, saved.Language); Assert.Equal(theme, saved.Theme); Assert.False(saved.Monitoring);
    }
    [Fact] public void Version11MigratesWithoutChangingExistingPreferencesOrUnknownData()
    {
        string path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, """{"Version":1,"FutureEnvelope":{"x":2},"Data":{"Theme":"Dark","Monitoring":false,"DisplayId":"stable-id","FutureSetting":{"level":7}}}""");
        var repo = new SettingsRepository(root); var value = repo.Load();
        Assert.Equal("System", value.Language); Assert.Equal("Dark", value.Theme); Assert.False(value.Monitoring);
        value.Language = "en-US"; repo.Save(value);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(7, json.RootElement.GetProperty("Data").GetProperty("FutureSetting").GetProperty("level").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("FutureEnvelope").GetProperty("x").GetInt32());
    }
    [Fact] public void NewInstallationFollowsSystemAndRejectsInvalidPreferences()
    {
        var value = new SettingsRepository(root).Load(); Assert.Equal("Default", value.Theme); Assert.Equal("System", value.Language);
        value.Theme = "neon"; value.Language = "xyz"; value.LibraryView = "fake"; value.Validate();
        Assert.Equal("Default", value.Theme); Assert.Equal("System", value.Language); Assert.Equal("Grid", value.LibraryView);
    }
    [Fact] public void SaveNeverOverwritesFutureSchema()
    {
        string path = Path.Combine(root, "settings.json"), data = """{"Version":99,"Data":{}}""";
        File.WriteAllText(path, data); Assert.Throws<NotSupportedException>(() => new SettingsRepository(root).Save(new())); Assert.Equal(data, File.ReadAllText(path));
    }
    [Fact] public void LibraryKeepsGameIdLaunchOptionsAndFutureFields()
    {
        Guid id = Guid.NewGuid(); string path = Path.Combine(root, "games.json");
        File.WriteAllText(path, $$"""{"Version":1,"Data":[{"Id":"{{id}}","Name":"Game","ExecutablePath":"C:\\Games\\Example.exe","Arguments":"--test","FutureGame":42}]}""");
        var repo = new GameRepository(root); var games = repo.Load(); repo.Save(games);
        Assert.Equal(id, repo.Load().Single().Id); Assert.Equal("--test", repo.Load().Single().Arguments);
        Assert.Equal(42, repo.Load().Single().AdditionalData!["FutureGame"].GetInt32());
    }
    public void Dispose() => Directory.Delete(root, true);
}

