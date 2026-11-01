using System.Text.Json;
using System.Text.Json.Serialization;
using Belac.Dock.Core.Models;

namespace Belac.Dock.Core.Settings;

public static class DockJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public static class DockPaths
{
    public static string Root { get; } = ResolveRoot();

    private static string ResolveRoot()
    {
        var over = Environment.GetEnvironmentVariable("BELAC_DOCK_HOME");
        if (!string.IsNullOrWhiteSpace(over))
            return over;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BELAC",
            "Dock");
    }

    public static string ConfigPath => Path.Combine(Root, "config.json");
    public static string JournalPath => Path.Combine(Root, "recovery-journal.json");
    public static string LogDirectory => Path.Combine(Root, "logs");
}

public static class SettingsNormalizer
{
    public static DockSettings Normalize(DockSettings? settings)
    {
        settings ??= DockSettings.CreateDefault();
        settings.ThemeId = DockThemes.Find(settings.ThemeId).Id;
        settings.Apps ??= [];
        settings.Groups ??= [];
        settings.Usage ??= new UsageSettings();
        settings.Usage.ProviderOrder ??= [];
        settings.Usage.HiddenProviderIds ??= [];
        foreach (var app in settings.Apps)
            app.MatchRules ??= [];

        if (settings.Groups.Count == 0)
            settings.Groups = DockSettings.CreateDefault().Groups;

        if (settings.SchemaVersion <= 0)
            settings.SchemaVersion = SettingsMigrations.Current;

        settings.WidthDip = Math.Clamp(settings.WidthDip <= 0 ? DockSettings.DefaultWidthDip : settings.WidthDip, 56, 124);
        settings.IconSizeDip = Math.Clamp(settings.IconSizeDip <= 0 ? 42 : settings.IconSizeDip, 32, 64);
        settings.GapDip = Math.Clamp(settings.GapDip <= 0 ? 9 : settings.GapDip, 4, 24);
        settings.Usage.RefreshIntervalSeconds = Math.Clamp(
            settings.Usage.RefreshIntervalSeconds <= 0 ? 90 : settings.Usage.RefreshIntervalSeconds,
            30,
            900);
        settings.Usage.CautionThreshold = Math.Clamp(settings.Usage.CautionThreshold <= 0 ? 80 : settings.Usage.CautionThreshold, 1, 99);
        settings.Usage.CriticalThreshold = Math.Clamp(
            settings.Usage.CriticalThreshold <= settings.Usage.CautionThreshold ? 95 : settings.Usage.CriticalThreshold,
            settings.Usage.CautionThreshold + 1,
            100);

        if (!string.Equals(settings.Edge, "Left", StringComparison.OrdinalIgnoreCase))
            settings.Edge = "Right";

        if (string.IsNullOrWhiteSpace(settings.KeyboardShortcut) || !HotkeyChord.TryParse(settings.KeyboardShortcut, out var chord))
            settings.KeyboardShortcut = "Ctrl+Alt+Shift+D";
        else
            settings.KeyboardShortcut = chord.ToString();

        return settings;
    }
}
