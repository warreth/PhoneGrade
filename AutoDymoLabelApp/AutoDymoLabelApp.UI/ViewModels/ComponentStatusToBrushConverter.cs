using System.Globalization;
using AutoDymoLabel.Core;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AutoDymoLabelApp.UI.ViewModels;

/// <summary>Maps a ComponentStatusType to its theme brush.</summary>
public class ComponentStatusToBrushConverter : IValueConverter
{
    public static readonly ComponentStatusToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            ComponentStatusType.Match => BrushFor("SeverityOkBrush"),
            ComponentStatusType.Mismatch => BrushFor("SeverityErrorBrush"),
            ComponentStatusType.Untrusted => BrushFor("SeverityWarningBrush"),
            ComponentStatusType.Unknown => BrushFor("ComponentUnknownBrush"),
            _ => BrushFor("ComponentUnknownBrush"),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static IBrush? BrushFor(string key) =>
        Avalonia.Application.Current?.TryGetResource(key,
            Avalonia.Application.Current.ActualThemeVariant, out object? b) == true ? b as IBrush : null;
}
