using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;

namespace Belac.Dock.App;

// Presentation adapters for the existing view-model contract. No provider or settings logic lives here.
public sealed class PercentTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not double number || !double.IsFinite(number)) return "No reading";
        var remaining = values.Length > 1 && values[1] is true;
        return remaining ? $"{Math.Max(0, 100 - number):0.#}% left" : $"{number:0.#}% used";
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed record UsageMetricPresentation(string Label, string Value, string Reset, double Percent, bool IsMetric)
{
    public string FullReset { get; init; } = Reset;
}

public sealed class UsageMetricPresentationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? "";
        var match = Regex.Match(text, @"^(?<label>.+): (?<value>[\d.,]+)% (?<mode>used|left)\. Resets (?<reset>.+)\.$");
        if (match.Success && double.TryParse(match.Groups["value"].Value, NumberStyles.Float, culture, out var percent))
        {
            var reset = match.Groups["reset"].Value;
            var relative = reset.Split('·').Last().Trim();
            var compact = reset.Contains('·') ? "Resets " + (relative == "now" ? "" : "in ") + relative : "Resets " + reset;
            return new UsageMetricPresentation(match.Groups["label"].Value,
                match.Groups["value"].Value + "% " + match.Groups["mode"].Value,
                compact, Math.Clamp(percent, 0, 100), true) { FullReset = "Resets " + reset };
        }
        return new UsageMetricPresentation(text, "", "", 0, false);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class UsageNoteVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length > 1 && Equals(values[0], values[1]) ? Visibility.Collapsed : Visibility.Visible;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
