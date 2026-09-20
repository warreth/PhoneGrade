using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhoneGrade.UI.Converters;

/// <summary>Converts placeholder values to user-friendly fallback text in Dutch.</summary>
public class PlaceholderToFriendlyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str) return "Onbekend";
        
        return str switch
        {
            "NOCOLOR" => "Onbekend",
            "NOBATT" => "Onbekend",
            "NOQUALITY" => "In afwachting",
            "" => "Onbekend",
            _ => str
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts battery health placeholder to friendly text in Dutch.</summary>
public class BatteryHealthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str) return "Onbekend";
        
        return str switch
        {
            "NOBATT" => "Controleren...",
            "" => "Onbekend",
            _ => str
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts color placeholder to friendly text in Dutch.</summary>
public class ColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str) return "Niet opgegeven";
        
        return str switch
        {
            "NOCOLOR" => "Niet opgegeven",
            "" => "Niet opgegeven",
            _ => str
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts quality placeholder to friendly text in Dutch.</summary>
public class QualityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str) return "Niet beoordeeld";
        
        return str switch
        {
            "NOQUALITY" => "Niet beoordeeld",
            "" => "Niet beoordeeld",
            _ => str
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
