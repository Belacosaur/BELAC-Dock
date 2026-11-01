namespace Belac.Dock.Core.Settings;

public sealed record DockTheme(string Id, string Name, string Accent, string Background, string Surface, string Hover, string Border);

public static class DockThemes
{
    public const string DefaultId = "mint";
    public static IReadOnlyList<DockTheme> All { get; } = Array.AsReadOnly(new[]
    {
        new DockTheme("mint", "Mint", "#AFE6C4", "#16191C", "#202629", "#2C3639", "#435153"),
        new DockTheme("ocean", "Ocean", "#92CCFF", "#141A23", "#1D2835", "#29394B", "#43566D"),
        new DockTheme("violet", "Violet", "#CDB6FF", "#1A1722", "#272231", "#373045", "#544864"),
        new DockTheme("rose", "Rose", "#F4B4C9", "#21181D", "#30232B", "#43303A", "#644858"),
        new DockTheme("amber", "Amber", "#F2CE91", "#201B16", "#2D2820", "#3E362A", "#5E5240"),
        new DockTheme("slate", "Slate", "#CBD3DF", "#191B1F", "#24272D", "#343840", "#505761")
    });

    public static DockTheme Find(string? id) => All.FirstOrDefault(theme => string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
