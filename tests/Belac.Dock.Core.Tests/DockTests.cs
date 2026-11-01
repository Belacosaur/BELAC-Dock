using Belac.Dock.Core.Catalog;
using Belac.Dock.Core.Geometry;
using Belac.Dock.Core.Matching;
using Belac.Dock.Core.Models;
using Belac.Dock.Core.Separation;
using Belac.Dock.Core.Settings;
using Belac.Dock.Core.Usage;
using Belac.Dock.Infrastructure;

namespace Belac.Dock.Core.Tests;

public class SettingsStoreTests
{
    [Theory]
    [InlineData("ocean", "ocean")]
    [InlineData("VIOLET", "violet")]
    [InlineData("invalid", "mint")]
    [InlineData(null, "mint")]
    public void Theme_survives_store_roundtrip_and_invalid_values_fall_back(string? input, string expected)
    {
        var store = new JsonSettingsStore(TempRoot());
        var settings = DockSettings.CreateDefault();
        settings.ThemeId = input!;
        store.Save(settings);
        Assert.Equal(expected, store.Load().Settings.ThemeId);
    }

    [Fact]
    public void RoundTrip_preserves_apps_and_bumps_schema()
    {
        var root = TempRoot();
        var store = new JsonSettingsStore(root);
        var settings = DockSettings.CreateDefault();
        settings.Apps.Add(DockAppFactory.FromExecutable(Path.Combine(root, "missing-is-ok.exe"), WellKnownGroups.Services, "--safe", root, 0));
        settings.Apps[0].ExePath = Path.Combine(root, "app.exe");
        store.Save(settings);

        var loaded = store.Load();
        Assert.False(loaded.UsedBackup);
        Assert.Equal(SettingsMigrations.Current, loaded.Settings.SchemaVersion);
        Assert.Equal("app.exe", Path.GetFileName(loaded.Settings.Apps[0].ExePath));
        Assert.Equal("--safe", loaded.Settings.Apps[0].LaunchArgs);
    }

    [Fact]
    public void Missing_usage_section_keeps_existing_apps()
    {
        var root = TempRoot();
        var id = Guid.NewGuid();
        var group = WellKnownGroups.Ript;
        File.WriteAllText(Path.Combine(root, "config.json"), $$"""
        {
          "schemaVersion": 1,
          "apps": [
            { "id": "{{id}}", "displayName": "Notepad", "exePath": "C:\\Windows\\notepad.exe", "groupId": "{{group}}", "matchRules": [] }
          ]
        }
        """);

        var loaded = new JsonSettingsStore(root).Load();
        Assert.Single(loaded.Settings.Apps);
        Assert.Equal("Notepad", loaded.Settings.Apps[0].DisplayName);
        Assert.NotNull(loaded.Settings.Usage);
        Assert.True(loaded.Settings.Usage.Enabled);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_backup_without_dropping_apps()
    {
        var root = TempRoot();
        var store = new JsonSettingsStore(root);
        var first = DockSettings.CreateDefault();
        first.Apps.Add(new DockApp { DisplayName = "Kept", ExePath = "C:\\Apps\\kept.exe", GroupId = WellKnownGroups.Services });
        store.Save(first);
        var second = DockSettings.CreateDefault();
        second.Apps.Add(new DockApp { DisplayName = "Newer", ExePath = "C:\\Apps\\newer.exe", GroupId = WellKnownGroups.Services });
        store.Save(second);
        File.WriteAllText(Path.Combine(root, "config.json"), "{ this is not json");

        var loaded = store.Load();
        Assert.True(loaded.UsedBackup);
        Assert.Equal("Kept", loaded.Settings.Apps[0].DisplayName);
        Assert.Contains("backup", loaded.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Corrupt_file_without_backup_uses_defaults_and_keeps_original()
    {
        var root = TempRoot();
        File.WriteAllText(Path.Combine(root, "config.json"), "{");
        var loaded = new JsonSettingsStore(root).Load();
        Assert.True(loaded.UsedDefaults);
        Assert.Empty(loaded.Settings.Apps);
        Assert.NotEmpty(Directory.GetFiles(root, "config.json.corrupt-*"));
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "belac-dock-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

public class MatchRuleTests
{
    [Fact]
    public void Exact_path_wins_over_same_process_name_in_another_folder()
    {
        var primary = App("A", @"C:\apps\a\tool.exe");
        var other = App("B", @"C:\apps\b\tool.exe");
        var window = Window(@"C:\apps\a\tool.exe", "tool");
        var match = AppMatcher.Match([primary, other], window);
        Assert.Equal(primary.Id, match.App?.Id);
    }

    [Fact]
    public void Process_name_rule_does_not_cross_a_different_path_rule()
    {
        var pathApp = App("A", @"C:\apps\a\tool.exe");
        var nameApp = App("B", @"C:\apps\b\tool.exe");
        nameApp.MatchRules = [new MatchRule { Kind = MatchKind.ProcessName, Pattern = "tool", Priority = 1 }];
        var window = Window(@"C:\somewhere\tool.exe", "tool");
        var match = AppMatcher.Match([pathApp, nameApp], window);
        Assert.Equal(nameApp.Id, match.App?.Id);
    }

    [Fact]
    public void Duplicate_process_name_rules_are_ambiguous()
    {
        var left = App("A", @"C:\a\tool.exe");
        var right = App("B", @"C:\b\tool.exe");
        left.MatchRules = [new MatchRule { Kind = MatchKind.ProcessName, Pattern = "tool" }];
        right.MatchRules = [new MatchRule { Kind = MatchKind.ProcessName, Pattern = "tool" }];
        var match = AppMatcher.Match([left, right], Window(@"C:\c\tool.exe", "tool"));
        Assert.True(match.Ambiguous);
        Assert.Null(match.App);
    }

    [Fact]
    public void Title_regex_does_not_seize_an_unrelated_window()
    {
        var app = App("Notes", @"C:\Windows\notepad.exe");
        app.MatchRules[0].TitleRegex = "^Special";
        Assert.Null(AppMatcher.Match([app], Window(@"C:\Windows\notepad.exe", "notepad") with { Title = "Other" }).App);
        Assert.Null(AppMatcher.Match([app], Window(@"C:\Other\nope.exe", "nope") with { Title = "Special project" }).App);
        Assert.NotNull(AppMatcher.Match([app], Window(@"C:\Windows\notepad.exe", "notepad") with { Title = "Special project" }).App);
    }

    [Fact]
    public void Title_regex_alone_is_not_a_rule_kind()
    {
        var app = App("Only title", @"C:\nope.exe");
        app.MatchRules = [new MatchRule { Kind = MatchKind.ExecutablePath, Pattern = "", TitleRegex = "Secret" }];
        Assert.Null(AppMatcher.Match([app], Window(@"C:\unrelated.exe", "unrelated") with { Title = "Secret" }).App);
    }

    private static DockApp App(string name, string path) => new()
    {
        DisplayName = name,
        ExePath = path,
        MatchRules = [new MatchRule { Kind = MatchKind.ExecutablePath, Pattern = path, Priority = 10 }]
    };

    private static WindowObservation Window(string path, string process) => new()
    {
        Hwnd = 10,
        ProcessId = 20,
        ProcessStartUtc = DateTime.UnixEpoch,
        ExecutablePath = path,
        ProcessName = process,
        IsVisible = true,
        Title = "Window"
    };
}

public class WindowIdentityTests
{
    [Fact]
    public void Recycled_hwnd_with_new_process_is_not_the_same_window()
    {
        var original = Snapshot(hwnd: 50, pid: 7, start: DateTime.UnixEpoch);
        var recycled = Probe(hwnd: 50, pid: 8, start: DateTime.UnixEpoch.AddMinutes(5));
        Assert.False(JournalValidator.IsSameWindow(original, recycled));
    }

    [Fact]
    public void Matching_pid_start_and_path_is_accepted()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var original = Snapshot(50, 7, start);
        var probe = Probe(50, 7, start);
        Assert.True(JournalValidator.IsSameWindow(original, probe));
    }

    [Fact]
    public void Distinct_replaces_hwnd_when_the_process_changes()
    {
        var first = Probe(9, 1, DateTime.UnixEpoch);
        var second = Probe(9, 2, DateTime.UnixEpoch.AddHours(1));
        var distinct = WindowIdentity.DistinctByHwnd([first, second]);
        Assert.Single(distinct);
        Assert.Equal(2, distinct[0].ProcessId);
    }

    [Fact]
    public void Cloaked_and_shell_windows_are_excluded_minimized_windows_stay()
    {
        Assert.False(WindowFilters.ShouldTrack(Probe(1, 1, DateTime.UnixEpoch) with { IsCloaked = true }));
        Assert.False(WindowFilters.ShouldTrack(Probe(1, 1, DateTime.UnixEpoch) with { IsVisible = false }));
        Assert.False(WindowFilters.ShouldTrack(Probe(1, 1, DateTime.UnixEpoch) with { ClassName = "Shell_TrayWnd" }));
        Assert.True(WindowFilters.ShouldTrack(Probe(1, 1, DateTime.UnixEpoch) with { IsMinimized = true }));
        Assert.False(WindowFilters.ShouldTrack(Probe(1, 1, DateTime.UnixEpoch) with { IsToolWindow = true }));
        Assert.True(WindowFilters.ShouldTrack(
            Probe(1, 1, DateTime.UnixEpoch) with { IsToolWindow = true, Title = "Sample", ExecutablePath = @"C:\apps\sample.exe" },
            path => path.EndsWith("sample.exe", StringComparison.OrdinalIgnoreCase)));
    }

    private static WindowMutationSnapshot Snapshot(long hwnd, int pid, DateTime start) => new()
    {
        Hwnd = hwnd,
        ProcessId = pid,
        ProcessStartUtc = start,
        ExecutablePath = @"C:\Windows\notepad.exe",
        Method = "ExStyle"
    };

    private static WindowObservation Probe(long hwnd, int pid, DateTime start) => new()
    {
        Hwnd = hwnd,
        ProcessId = pid,
        ProcessStartUtc = start,
        ExecutablePath = @"C:\Windows\notepad.exe",
        IsVisible = true,
        Title = "Notepad",
        ClassName = "Notepad"
    };
}

public class OrderingTests
{
    [Fact]
    public void Move_within_a_group_persists_stable_order()
    {
        var groups = DockSettings.CreateDefault().Groups;
        var apps = new List<DockApp>
        {
            new() { DisplayName = "One", GroupId = WellKnownGroups.Services, Order = 0 },
            new() { DisplayName = "Two", GroupId = WellKnownGroups.Services, Order = 1 },
            new() { DisplayName = "Three", GroupId = WellKnownGroups.Ript, Order = 0 }
        };

        AppOrdering.MoveBy(apps, groups, apps[0].Id, 1);
        var ordered = AppOrdering.InDisplayOrder(groups, apps);
        Assert.Equal(["Two", "One"], ordered.Where(app => app.GroupId == WellKnownGroups.Services).Select(app => app.DisplayName).ToArray());
        Assert.Equal("Three", ordered[0].DisplayName);
        Assert.Equal(WellKnownGroups.Ript, ordered[0].GroupId);
        Assert.Equal(0, ordered.First(app => app.DisplayName == "Two").Order);
        Assert.Equal(1, ordered.First(app => app.DisplayName == "One").Order);
    }
}

public class SeparationTransitionTests
{
    [Fact]
    public void Mirror_attempt_verified_disable_and_failure_paths()
    {
        var verified = new SeparationSession();
        Assert.Equal(SeparationPhase.Mirror, verified.Phase);
        verified.BeginAttempt(DateTime.UtcNow);
        Assert.Equal(SeparationPhase.Attempt, verified.Phase);
        verified.MarkVerified();
        Assert.Equal(SeparationPhase.DockOnly, verified.Phase);
        verified.Disable();
        Assert.Equal(SeparationPhase.Mirror, verified.Phase);

        var failed = new SeparationSession();
        failed.BeginAttempt(DateTime.UtcNow);
        failed.MarkFailed();
        Assert.Equal(SeparationPhase.Fallback, failed.Phase);

        var timedOut = new SeparationSession();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        timedOut.BeginAttempt(start);
        timedOut.Timeout(start.AddSeconds(2), TimeSpan.FromSeconds(5));
        Assert.Equal(SeparationPhase.Attempt, timedOut.Phase);
        timedOut.Timeout(start.AddSeconds(5), TimeSpan.FromSeconds(5));
        Assert.Equal(SeparationPhase.Fallback, timedOut.Phase);
    }
}

public class GeometryTests
{
    [Theory]
    [InlineData(96, 84, 84)]
    [InlineData(120, 84, 105)]
    [InlineData(144, 84, 126)]
    [InlineData(192, 84, 168)]
    public void Dip_conversion_matches_scale(int dpi, double dip, int expected) =>
        Assert.Equal(expected, AppbarGeometry.DipToPx(dip, dpi));

    [Fact]
    public void Right_dock_sits_inside_work_area_including_negative_coordinates()
    {
        var primary = AppbarGeometry.Place(new PixelRect(0, 0, 2560, 1400), 100, rightEdge: true);
        Assert.Equal(new PixelRect(2460, 0, 2560, 1400), primary);

        var leftOfPrimary = AppbarGeometry.Place(new PixelRect(-1920, 0, 0, 1080), 80, rightEdge: true);
        Assert.Equal(new PixelRect(-80, 0, 0, 1080), leftOfPrimary);

        var leftEdge = AppbarGeometry.Place(new PixelRect(0, 0, 1920, 1080), 80, rightEdge: false);
        Assert.Equal(new PixelRect(0, 0, 80, 1080), leftEdge);
    }
}

public class LaunchQuotingTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has space", "\"has space\"")]
    [InlineData("quote\"here", "\"quote\\\"here\"")]
    public void Quotes_windows_arguments(string input, string expected) =>
        Assert.Equal(expected, CommandLineQuoting.Quote(input));

    [Fact]
    public void Launch_request_keeps_the_executable_separate_from_arguments()
    {
        var exe = Environment.ProcessPath ?? Path.Combine(Environment.SystemDirectory, "notepad.exe");
        if (!File.Exists(exe))
            exe = Path.Combine(Environment.SystemDirectory, "notepad.exe");

        var info = LaunchRequest.Create(exe, "--alpha \"beta gamma\"", null);
        Assert.Equal(Path.GetFullPath(exe), info.FileName);
        Assert.Equal("--alpha \"beta gamma\"", info.Arguments);
        Assert.True(info.UseShellExecute);
    }

    [Fact]
    public void Shortcut_parser_requires_a_modifier()
    {
        Assert.True(HotkeyChord.TryParse("Ctrl+Alt+Shift+D", out var chord));
        Assert.Equal('D', chord.VirtualKey);
        Assert.False(HotkeyChord.TryParse("D", out _));
    }
}

public class UsageParserTests
{
    [Fact]
    public void Progress_lines_use_the_shorter_window_as_the_outer_ring()
    {
        const string json = """
        [
          {
            "providerId": "cursor",
            "displayName": "Cursor",
            "plan": "Test",
            "fetchedAt": "2026-11-01T00:00:00Z",
            "lines": [
              {"type":"progress","label":"Weekly","used":99,"limit":100,"format":{"kind":"percent"},"resetsAt":"2026-11-07T00:00:00Z","periodDurationMs":604800000},
              {"type":"progress","label":"Session","used":50,"limit":100,"format":{"kind":"percent"},"resetsAt":"2026-11-01T05:00:00Z","periodDurationMs":18000000},
              {"type":"text","label":"Credits","value":"12 left"}
            ]
          },
          {
            "providerId": "codex",
            "displayName": "Codex",
            "plan": "Test",
            "fetchedAt": "2026-11-01T00:00:00Z",
            "lines": [
              {"type":"progress","label":"Weekly","used":0,"limit":100,"format":{"kind":"percent"},"periodDurationMs":604800000}
            ]
          }
        ]
        """;

        var snapshot = UsageSnapshotNormalizer.Parse(System.Text.Encoding.UTF8.GetBytes(json), DateTimeOffset.Parse("2026-11-01T00:10:00Z"));
        Assert.Null(snapshot.SourceError);
        var cursor = snapshot.Providers[0];
        var (outer, inner, extra) = RingLayout.Select(cursor.Metrics);
        Assert.Equal("Session", outer?.Label);
        Assert.Equal(50, outer?.UsedPercent);
        Assert.Equal("Weekly", inner?.Label);
        Assert.Equal(99, inner?.UsedPercent);
        Assert.Equal(0, extra);
        Assert.Null(cursor.Metrics.Single(metric => metric.Label == "Credits").UsedPercent);
        Assert.Equal(0, snapshot.Providers[1].Metrics[0].UsedPercent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(100)]
    public void Percent_values_survive_normalization(int percent)
    {
        var json = "[{\"providerId\":\"cursor\",\"displayName\":\"Cursor\",\"lines\":[{\"type\":\"progress\",\"label\":\"Weekly\",\"used\":" + percent + ",\"limit\":100,\"format\":{\"kind\":\"percent\"}}]}]";
        var snapshot = UsageSnapshotNormalizer.Parse(System.Text.Encoding.UTF8.GetBytes(json), DateTimeOffset.UnixEpoch);
        Assert.Equal(percent, snapshot.Providers[0].Metrics[0].UsedPercent);
        Assert.Equal(percent, RingMath.NormalizePercent(snapshot.Providers[0].Metrics[0].UsedPercent));
    }

    [Fact]
    public void Over_range_unknown_and_missing_denominator_fail_safe()
    {
        Assert.Equal(100, RingMath.NormalizePercent(140));
        Assert.Equal(0, RingMath.NormalizePercent(-4));
        Assert.Null(RingMath.NormalizePercent(double.NaN));
        Assert.Null(RingMath.NormalizePercent(double.PositiveInfinity));
        Assert.Equal(RingBand.Critical, RingMath.Band(140, 80, 95));
        Assert.Equal(RingBand.Caution, RingMath.Band(80, 80, 95));
        Assert.Equal(RingBand.Normal, RingMath.Band(0, 80, 95));
        Assert.Equal(RingBand.Unknown, RingMath.Band(null, 80, 95));

        const string json = """
        [
          {"providerId":"grok","displayName":"Grok","lines":[{"type":"progress","label":"Credits","used":3,"limit":0,"format":{"kind":"percent"}}]},
          {"providerId":"other","displayName":"Other","lines":[{"type":"progress","label":"Tokens","used":3,"limit":9,"format":{"kind":"tokens"}}]}
        ]
        """;
        var snapshot = UsageSnapshotNormalizer.Parse(System.Text.Encoding.UTF8.GetBytes(json), DateTimeOffset.UnixEpoch);
        Assert.Null(snapshot.Providers[0].Metrics[0].UsedPercent);
        Assert.Null(snapshot.Providers[1].Metrics[0].UsedPercent);
        Assert.Equal((null, null, 0), RingLayout.Select(snapshot.Providers[1].Metrics));
    }

    [Fact]
    public void Invalid_payloads_do_not_invent_providers()
    {
        var observed = DateTimeOffset.UnixEpoch;
        var encoding = System.Text.Encoding.UTF8;
        Assert.NotNull(UsageSnapshotNormalizer.Parse(encoding.GetBytes("{"), observed).SourceError);
        Assert.NotNull(UsageSnapshotNormalizer.Parse(encoding.GetBytes("{\"providerId\":\"x\"}"), observed).SourceError);
        var empty = UsageSnapshotNormalizer.Parse(encoding.GetBytes("[]"), observed);
        Assert.Null(empty.SourceError);
        Assert.Empty(empty.Providers);
    }

    [Fact]
    public void Provider_order_prefers_the_user_then_cursor_codex_claude_grok()
    {
        var providers = new ProviderUsage[]
        {
            Provider("codex-wsl-1", "Codex WSL"),
            Provider("custom", "Custom"),
            Provider("claude", "Claude"),
            Provider("cursor", "Cursor"),
            Provider("grok", "Grok")
        };
        var settings = new UsageSettings { ProviderOrder = ["grok", "cursor"], HiddenProviderIds = ["custom"] };
        var ordered = ProviderOrdering.Order(providers, settings).Select(provider => provider.Id).ToArray();
        Assert.Equal(["grok", "cursor", "codex-wsl-1", "claude"], ordered);
    }

    [Fact]
    public void Reset_clock_uses_the_supplied_instant()
    {
        var now = DateTimeOffset.Parse("2026-11-01T00:00:00Z");
        Assert.Equal("5h 0m", ResetClock.Relative(now.AddHours(5), now));
        Assert.Equal("due now", ResetClock.Relative(now.AddMinutes(-1), now));
        Assert.Equal("No reset time", ResetClock.Relative(null, now));
    }

    private static ProviderUsage Provider(string id, string name) =>
        new(id, name, null, [], null, false, null);
}
