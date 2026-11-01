using System.Diagnostics;
using Belac.Dock.Core.Matching;
using Belac.Dock.Core.Models;

namespace Belac.Dock.Core.Catalog;

public static class AppOrdering
{
    public static List<DockApp> InDisplayOrder(IEnumerable<DockGroup> groups, IEnumerable<DockApp> apps)
    {
        var groupOrder = groups.OrderBy(group => group.Order).ThenBy(group => group.Label, StringComparer.CurrentCultureIgnoreCase)
            .Select((group, index) => (group.Id, index))
            .ToDictionary(item => item.Id, item => item.index);

        return apps
            .OrderBy(app => groupOrder.TryGetValue(app.GroupId, out var index) ? index : int.MaxValue)
            .ThenBy(app => app.Order)
            .ThenBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static void MoveBy(IList<DockApp> apps, IList<DockGroup> groups, Guid appId, int delta)
    {
        if (delta == 0)
            return;

        var ordered = InDisplayOrder(groups, apps);
        var index = ordered.FindIndex(app => app.Id == appId);
        if (index < 0)
            return;

        var target = index + delta;
        if (target < 0 || target >= ordered.Count)
            return;

        var item = ordered[index];
        item.GroupId = ordered[target].GroupId;
        ordered.RemoveAt(index);
        ordered.Insert(target, item);
        Resequence(ordered);
    }

    public static void MoveToGroup(IList<DockApp> apps, Guid appId, Guid groupId)
    {
        var app = apps.FirstOrDefault(item => item.Id == appId);
        if (app is null)
            return;

        app.GroupId = groupId;
        app.Order = apps.Where(item => item.GroupId == groupId && item.Id != appId).Select(item => item.Order).DefaultIfEmpty(-1).Max() + 1;
    }

    private static void Resequence(IReadOnlyList<DockApp> ordered)
    {
        var counters = new Dictionary<Guid, int>();
        foreach (var app in ordered)
        {
            counters.TryGetValue(app.GroupId, out var next);
            app.Order = next;
            counters[app.GroupId] = next + 1;
        }
    }
}

public static class DockAppFactory
{
    public static DockApp FromExecutable(string executablePath, Guid groupId, string? arguments, string? workingDirectory, int order)
    {
        var fullPath = Path.GetFullPath(executablePath);
        return new DockApp
        {
            DisplayName = Path.GetFileNameWithoutExtension(fullPath),
            ExePath = fullPath,
            LaunchArgs = string.IsNullOrWhiteSpace(arguments) ? null : arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Path.GetDirectoryName(fullPath) : workingDirectory,
            GroupId = groupId,
            Order = order,
            MatchRules = [new MatchRule { Kind = MatchKind.ExecutablePath, Pattern = fullPath, Priority = 100 }],
            TaskbarMode = TaskbarMode.Mirror,
            Enabled = true,
            UserAdded = true
        };
    }
}

public static class LaunchRequest
{
    public static ProcessStartInfo Create(string executablePath, string? arguments, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new InvalidOperationException("Choose an executable before launching.");

        var fullPath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The executable was not found.", fullPath);

        if (arguments?.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw new InvalidOperationException("Launch arguments cannot contain line breaks.");

        var start = new ProcessStartInfo
        {
            FileName = fullPath,
            UseShellExecute = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory
                : workingDirectory
        };

        if (!string.IsNullOrWhiteSpace(arguments))
            start.Arguments = arguments;

        return start;
    }
}
