using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhoneGrade.UI.Converters;

/// <summary>Converts placeholder values to user-friendly fallback text.</summary>
public class PlaceholderToFriendlyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str) return "N/A";
        
        return str switch
        {
            "NOCOLOR" => "N/A",
            "NOBATT" => "N/A",
            "NOQUALITY" => "Pending",
            "" => "N/A",
            _ => str
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts battery health placeholder to friendly text.</summary>
public class BatteryHealthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str) return "Unknown";
        
        return str switch
        {
            "NOBATT" => "Checking...",
            "" => "Unknown",
            _ => str
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts color placeholder to friendly text.</summary>
public class ColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str) return "Not specified";
        
        return str switch
        {
            "NOCOLOR" => "Not specified",
            "" => "Not specified",
            _ => str
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts quality placeholder to friendly text.</summary>
public class QualityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str) return "Not graded";
        
        return str switch
        {
            "NOQUALITY" => "Not graded",
            "" => "Not graded",
            _ => str
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
