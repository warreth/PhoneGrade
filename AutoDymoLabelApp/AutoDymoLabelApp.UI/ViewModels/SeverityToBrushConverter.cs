using System.Globalization;
using AutoDymoLabel.Core;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AutoDymoLabelApp.UI.ViewModels;

/// <summary>Maps a Severity to its theme brush for the diagnostics list dot.</summary>
public class SeverityToBrushConverter : IValueConverter
{
    public static readonly SeverityToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            Severity.Ok => BrushFor("SeverityOkBrush"),
            Severity.Warning => BrushFor("SeverityWarningBrush"),
            Severity.Error => BrushFor("SeverityErrorBrush"),
            _ => BrushFor("SeverityWarningBrush"),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static IBrush? BrushFor(string key) =>
        Avalonia.Application.Current?.TryGetResource(key,
            Avalonia.Application.Current.ActualThemeVariant, out object? b) == true ? b as IBrush : null;
}
