using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PhoneGrade.Core;

namespace PhoneGrade.UI.Converters;

/// <summary>Converts ComponentStatusType enum to Dutch display text.</summary>
public class ComponentStatusConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ComponentStatusType status) return "Onbekend";
        
        return status switch
        {
            ComponentStatusType.Match => "Origineel",
            ComponentStatusType.Mismatch => "Vervangen",
            ComponentStatusType.Untrusted => "Niet-origineel",
            ComponentStatusType.Unknown => "Onbekend",
            _ => "Onbekend"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}