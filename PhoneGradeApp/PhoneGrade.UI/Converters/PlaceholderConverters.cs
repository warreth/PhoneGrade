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

/// <summary>Converts Activation Lock status to friendly Dutch text.</summary>
public class ActivationLockConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PhoneGrade.Core.SecurityServices.ActivationLockService.ActivationLockStatus status) 
            return "Onbekend";
            
        return status switch
        {
            PhoneGrade.Core.SecurityServices.ActivationLockService.ActivationLockStatus.Locked => "AAN (Gelocked)",
            PhoneGrade.Core.SecurityServices.ActivationLockService.ActivationLockStatus.Unlocked => "UIT (Vrij)",
            _ => "Onbekend"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts Activation Lock status to brush color.</summary>
public class ActivationLockBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PhoneGrade.Core.SecurityServices.ActivationLockService.ActivationLockStatus status) 
            return Avalonia.Media.Brushes.Gray;
            
        return status switch
        {
            PhoneGrade.Core.SecurityServices.ActivationLockService.ActivationLockStatus.Locked => Avalonia.Media.Brushes.Red,
            PhoneGrade.Core.SecurityServices.ActivationLockService.ActivationLockStatus.Unlocked => Avalonia.Media.Brushes.Green,
            _ => Avalonia.Media.Brushes.Gray
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts payment method placeholder to friendly Dutch text.</summary>
public class PayMethodConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str || string.IsNullOrWhiteSpace(str) || str == "NOPAY") 
            return "Niet opgegeven";
            
        return str switch
        {
            "Marge" => "Marge (0% BTW)",
            "BTW" => "BTW (21%)",
            _ => str
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts raw model name to full friendly display model (e.g. '8' -> 'iPhone 8').</summary>
public class ModelDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string model) return "Onbekend Toestel";
        return PhoneGrade.Core.Mappers.FormatDisplayModel(model);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
