using Belac.Dock.Core.Models;

namespace Belac.Dock.Core.Matching;

public sealed record MatchOutcome(DockApp? App, bool Ambiguous, IReadOnlyList<Guid> CandidateAppIds)
{
    public static MatchOutcome None { get; } = new(null, false, []);

    public static MatchOutcome Hit(DockApp app) => new(app, false, [app.Id]);

    public static MatchOutcome Tie(IReadOnlyList<DockApp> apps) =>
        new(null, true, apps.Select(app => app.Id).ToArray());
}

public static class AppMatcher
{
    public static MatchOutcome Match(IEnumerable<DockApp> apps, WindowObservation window)
    {
        var hits = new List<(DockApp App, int Score)>();
        foreach (var app in apps)
        {
            if (!app.Enabled)
                continue;

            var best = 0;
            foreach (var rule in app.MatchRules)
            {
                var score = Score(rule, window);
                if (score > best)
                    best = score;
            }

            if (best > 0)
                hits.Add((app, best));
        }

        if (hits.Count == 0)
            return MatchOutcome.None;

        var max = hits.Max(hit => hit.Score);
        var top = hits.Where(hit => hit.Score == max).Select(hit => hit.App).DistinctBy(app => app.Id).ToList();
        return top.Count == 1 ? MatchOutcome.Hit(top[0]) : MatchOutcome.Tie(top);
    }

    public static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        left = left.Trim().Trim('"');
        right = right.Trim().Trim('"');
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static int Score(MatchRule rule, WindowObservation window)
    {
        if (!ClassMatches(rule, window) || !TitleMatches(rule, window))
            return 0;

        switch (rule.Kind)
        {
            case MatchKind.ExecutablePath:
                return PathsEqual(rule.Pattern, window.ExecutablePath) ? 1000 + rule.Priority : 0;
            case MatchKind.ProcessName:
                return ProcessNameMatches(rule.Pattern, window) ? 400 + rule.Priority : 0;
            case MatchKind.Aumid:
                return !string.IsNullOrWhiteSpace(window.Aumid) &&
                       string.Equals(rule.Pattern, window.Aumid, StringComparison.OrdinalIgnoreCase)
                    ? 800 + rule.Priority
                    : 0;
            default:
                return 0;
        }
    }

    private static bool ProcessNameMatches(string pattern, WindowObservation window)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var file = string.IsNullOrWhiteSpace(window.ExecutablePath) ? "" : Path.GetFileName(window.ExecutablePath);
        var stem = string.IsNullOrWhiteSpace(file) ? "" : Path.GetFileNameWithoutExtension(file);
        return string.Equals(pattern, window.ProcessName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(pattern, file, StringComparison.OrdinalIgnoreCase)
               || string.Equals(pattern, stem, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ClassMatches(MatchRule rule, WindowObservation window)
    {
        if (string.IsNullOrWhiteSpace(rule.WindowClass))
            return true;

        return string.Equals(rule.WindowClass, window.ClassName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TitleMatches(MatchRule rule, WindowObservation window)
    {
        if (string.IsNullOrWhiteSpace(rule.TitleRegex))
            return true;

        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                window.Title ?? "",
                rule.TitleRegex,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(50));
        }
        catch (Exception ex) when (ex is ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return false;
        }
    }
}

public static class WindowFilters
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "XamlExplorerHostIslandWindow",
        "DV2ControlHost"
    };

    public static bool ShouldTrack(WindowObservation window, Func<string, bool>? isManagedPath = null)
    {
        if (ShellClasses.Contains(window.ClassName))
            return false;

        if (window.IsCloaked || !window.IsVisible)
            return false;

        var managed = isManagedPath is not null && isManagedPath(window.ExecutablePath);
        if (managed && !string.IsNullOrWhiteSpace(window.Title))
            return true;

        if (window.IsToolWindow && !window.IsAppWindow)
            return false;

        if (window.HasOwner && !window.IsAppWindow)
            return false;

        return true;
    }
}

public static class WindowIdentity
{
    public static bool SameInstance(int processId, DateTime startUtc, int otherProcessId, DateTime otherStartUtc)
    {
        if (processId <= 0 || otherProcessId <= 0 || processId != otherProcessId)
            return false;

        return Math.Abs((startUtc.ToUniversalTime() - otherStartUtc.ToUniversalTime()).TotalSeconds) < 2;
    }

    public static IReadOnlyList<WindowObservation> DistinctByHwnd(IEnumerable<WindowObservation> windows)
    {
        var result = new List<WindowObservation>();
        foreach (var window in windows)
        {
            var index = result.FindIndex(existing => existing.Hwnd == window.Hwnd);
            if (index >= 0)
                result[index] = window;
            else
                result.Add(window);
        }

        return result;
    }
}

public static class JournalValidator
{
    public static bool IsSameWindow(WindowMutationSnapshot snapshot, WindowObservation probe)
    {
        if (snapshot.Hwnd == 0 || probe.Hwnd == 0 || snapshot.Hwnd != probe.Hwnd)
            return false;

        if (!WindowIdentity.SameInstance(snapshot.ProcessId, snapshot.ProcessStartUtc, probe.ProcessId, probe.ProcessStartUtc))
            return false;

        return AppMatcher.PathsEqual(snapshot.ExecutablePath, probe.ExecutablePath);
    }
}
