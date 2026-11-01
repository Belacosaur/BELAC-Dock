namespace Belac.Dock.Core.Models;

public enum TaskbarMode
{
    Mirror,
    DockOnly
}

public enum TaskbarStatus
{
    Unknown,
    Mirrored,
    DockOnly,
    Unsupported,
    Error
}

public enum MatchKind
{
    ExecutablePath,
    ProcessName,
    Aumid
}

public sealed class MatchRule
{
    public MatchKind Kind { get; set; } = MatchKind.ExecutablePath;
    public string Pattern { get; set; } = "";
    public string? WindowClass { get; set; }
    public string? TitleRegex { get; set; }
    public int Priority { get; set; }
}

public sealed class DockApp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string? LaunchArgs { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? IconPath { get; set; }
    public Guid GroupId { get; set; }
    public int Order { get; set; }
    public List<MatchRule> MatchRules { get; set; } = [];
    public TaskbarMode TaskbarMode { get; set; } = TaskbarMode.Mirror;
    public bool AllowStyleCompatibility { get; set; }
    public bool UserAdded { get; set; } = true;
    public bool Enabled { get; set; } = true;
}

public sealed class DockGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public int Order { get; set; }
    public bool Collapsed { get; set; }
}

public sealed class UsageSettings
{
    public List<string> NativeProviders { get; set; } = ["codex", "claude", "cursor"];
    public bool CompanionFallback { get; set; }
    public bool Enabled { get; set; } = true;
    public bool AutoHideOnShortDisplay { get; set; } = true;
    public int RefreshIntervalSeconds { get; set; } = 90;
    public bool ShowRemaining { get; set; }
    public double CautionThreshold { get; set; } = 80;
    public double CriticalThreshold { get; set; } = 95;
    public List<string> ProviderOrder { get; set; } = [];
    public List<string> HiddenProviderIds { get; set; } = [];
}

public sealed class DockSettings
{
    public const double DefaultWidthDip = 96;

    public int SchemaVersion { get; set; } = SettingsMigrations.Current;
    public string ThemeId { get; set; } = Belac.Dock.Core.Settings.DockThemes.DefaultId;
    public string Edge { get; set; } = "Left";
    public double WidthDip { get; set; } = DefaultWidthDip;
    public double IconSizeDip { get; set; } = 42;
    public double GapDip { get; set; } = 9;
    public bool ReserveWorkArea { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool Autostart { get; set; }
    public bool ShowLabelsOnHover { get; set; } = true;
    public bool Animation { get; set; } = true;
    public string? AssignedMonitorDeviceName { get; set; }
    public string KeyboardShortcut { get; set; } = "Ctrl+Alt+Shift+D";
    public bool StartMinimizedToTray { get; set; }
    public bool YieldOnFullscreen { get; set; } = true;
    public bool MinimizeIfFocused { get; set; } = true;
    public bool OnboardingCompleted { get; set; }
    public bool SeparationPaused { get; set; }
    public List<DockGroup> Groups { get; set; } = [];
    public List<DockApp> Apps { get; set; } = [];
    public UsageSettings Usage { get; set; } = new();

    public bool RightEdge => !string.Equals(Edge, "Left", StringComparison.OrdinalIgnoreCase);

    public static DockSettings CreateDefault()
    {
        return new DockSettings
        {
            Groups =
            [
                new DockGroup { Id = WellKnownGroups.TcgHunter, Label = "Apps", Order = 0 },
                new DockGroup { Id = WellKnownGroups.Ript, Label = "Work", Order = 1 },
                new DockGroup { Id = WellKnownGroups.Orchestrator, Label = "Tools", Order = 2 },
                new DockGroup { Id = WellKnownGroups.Services, Label = "Services", Order = 3 }
            ]
        };
    }
}

public static class WellKnownGroups
{
    public static readonly Guid TcgHunter = new("a11a0001-0000-4000-8000-000000000001");
    public static readonly Guid Ript = new("a11a0002-0000-4000-8000-000000000002");
    public static readonly Guid Orchestrator = new("a11a0003-0000-4000-8000-000000000003");
    public static readonly Guid Services = new("a11a0004-0000-4000-8000-000000000004");
}

public static class SettingsMigrations
{
    public const int Current = 1;
}

public sealed class WindowMutationSnapshot
{
    public long Hwnd { get; set; }
    public int ProcessId { get; set; }
    public DateTime ProcessStartUtc { get; set; }
    public string ExecutablePath { get; set; } = "";
    public int OriginalExStyle { get; set; }
    public bool WasVisible { get; set; }
    public string Method { get; set; } = "";
    public DateTime CapturedUtc { get; set; }
}

public sealed record WindowObservation
{
    public long Hwnd { get; init; }
    public int ProcessId { get; init; }
    public DateTime ProcessStartUtc { get; init; }
    public string ExecutablePath { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public string? Aumid { get; init; }
    public string Title { get; init; } = "";
    public string ClassName { get; init; } = "";
    public bool IsVisible { get; init; }
    public bool IsMinimized { get; init; }
    public bool IsCloaked { get; init; }
    public bool HasOwner { get; init; }
    public bool IsToolWindow { get; init; }
    public bool IsAppWindow { get; init; }
    public bool AccessDenied { get; init; }
    public string MonitorDeviceName { get; init; } = "";
}

public sealed class MonitorInfo
{
    public string DeviceName { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    public PixelRect Bounds { get; init; }
    public PixelRect Work { get; init; }
    public int Dpi { get; init; } = 96;
    public bool Primary { get; init; }

    public string Label
    {
        get
        {
            var mark = Primary ? "Primary" : "Secondary";
            var size = $"{Bounds.Width}×{Bounds.Height}";
            var name = string.IsNullOrWhiteSpace(FriendlyName) ? DeviceName : FriendlyName;
            return $"{name} · {size} · {mark}";
        }
    }
}

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public sealed record ShortcutTarget(string? TargetPath, string? Arguments, string? WorkingDirectory);

public sealed record SettingsLoadResult(DockSettings Settings, bool UsedBackup, bool UsedDefaults, string? Warning);

public sealed record SeparationResult(TaskbarStatus Status, string Detail, bool ChangedStyle);

public readonly record struct HotkeyChord(bool Ctrl, bool Alt, bool Shift, bool Win, int VirtualKey)
{
    public static bool TryParse(string? text, out HotkeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ctrl = false;
        var alt = false;
        var shift = false;
        var win = false;
        string? key = null;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    ctrl = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "win":
                case "windows":
                    win = true;
                    break;
                default:
                    if (key is not null)
                        return false;
                    key = part;
                    break;
            }
        }

        if (key is null || !(ctrl || alt || shift || win) || !TryVirtualKey(key, out var vk))
            return false;

        chord = new HotkeyChord(ctrl, alt, shift, win, vk);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));
        return string.Join('+', parts);
    }

    public static bool TryVirtualKey(string key, out int virtualKey)
    {
        virtualKey = 0;
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z')
            {
                virtualKey = c;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                virtualKey = c;
                return true;
            }
        }

        if (key.Length is >= 2 and <= 3 && (key[0] == 'F' || key[0] == 'f') && int.TryParse(key.AsSpan(1), out var fn) && fn is >= 1 and <= 24)
        {
            virtualKey = 0x70 + fn - 1;
            return true;
        }

        return false;
    }

    private static string KeyName(int virtualKey) => virtualKey switch
    {
        >= 0x70 and <= 0x87 => "F" + (virtualKey - 0x70 + 1),
        >= 'A' and <= 'Z' => ((char)virtualKey).ToString(),
        >= '0' and <= '9' => ((char)virtualKey).ToString(),
        _ => virtualKey.ToString("X")
    };
}
