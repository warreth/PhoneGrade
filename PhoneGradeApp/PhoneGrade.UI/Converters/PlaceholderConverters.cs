using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PhoneGrade.Core;
using PhoneGrade.UI.Services;

namespace PhoneGrade.UI.Converters;

/// <summary>
/// Words for a value the phone did not report.
///
/// Every sentence these converters return lives in the string dictionaries in
/// both languages, so an English operator reads English and a Dutch one reads
/// Dutch. What the converter owns is the decision of which word fits the value,
/// never the word itself.
/// </summary>

/// <summary>Show the current charge level as a subline under the battery condition.</summary>
public class BatteryLevelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int level || level <= 0) return "";
        return string.Format(LocalizationManager.GetString("Value_BatteryLevel"), level);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Shows the current charge level as a subline under the battery condition.</summary>
public class BatteryHealthConverter : IValueConverter
{
    /// <summary>
    /// The platform status codes Android reports for its battery, which arrive as
    /// tokens rather than as a percentage. The wording belongs to the operator's
    /// language, so the conversion happens here instead of at the place the code is
    /// read, where it would have to guess a language.
    /// </summary>
    private static readonly Dictionary<string, string> StatusKeys = new(StringComparer.Ordinal)
    {
        ["Unknown"] = "BatteryStatus_Unknown",
        ["Good"] = "BatteryStatus_Good",
        ["Overheated"] = "BatteryStatus_Overheated",
        ["Defective"] = "BatteryStatus_Defective",
        ["Overvoltage"] = "BatteryStatus_Overvoltage",
        ["StorageFault"] = "BatteryStatus_StorageFault",
        ["TooCold"] = "BatteryStatus_TooCold",
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str) return LocalizationManager.GetString("Value_Unknown");

        if (StatusKeys.TryGetValue(str, out string? key))
            return LocalizationManager.GetString(key);

        return str switch
        {
            "NOBATT" => LocalizationManager.GetString("Value_Checking"),
            "" => LocalizationManager.GetString("Value_Unknown"),
            _ => str,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Color placeholder: the phone never sent a colour rather than it being white.</summary>
public class ColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str) return LocalizationManager.GetString("Value_NotSet");

        return str switch
        {
            "NOCOLOR" => LocalizationManager.GetString("Value_NotSet"),
            "" => LocalizationManager.GetString("Value_NotSet"),
            _ => str,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Installed memory. Android reports it; iOS does not, so the row reads as
/// unknown there instead of showing the raw placeholder.
/// </summary>
public class MemoryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str) return LocalizationManager.GetString("Value_NotSet");

        return str switch
        {
            "NOMEMORY" => LocalizationManager.GetString("Value_NotSet"),
            "" => LocalizationManager.GetString("Value_NotSet"),
            _ => str,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a localization key to its localized string value.
/// Used for dynamic keys in data templates.
/// </summary>
public class LocalizationConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key && !string.IsNullOrWhiteSpace(key))
            return LocalizationManager.GetString(key);
        return value?.ToString() ?? "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Grade placeholder: not graded yet rather than a bad grade.</summary>
public class QualityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return GradeWording.Grade(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Activation Lock status of the phone.</summary>
public class ActivationLockConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PhoneGrade.Core.SecurityServices.ActivationLockService.ActivationLockStatus status)
            return LocalizationManager.GetString("Value_Unknown");

        return status switch
        {
            PhoneGrade.Core.SecurityServices.ActivationLockService.ActivationLockStatus.Locked => LocalizationManager.GetString("ActivationLock_On"),
            PhoneGrade.Core.SecurityServices.ActivationLockService.ActivationLockStatus.Unlocked => LocalizationManager.GetString("ActivationLock_Off"),
            _ => LocalizationManager.GetString("Value_Unknown"),
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Turns Activation Lock status into a colour that reads in both themes.</summary>
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
            _ => Avalonia.Media.Brushes.Gray,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Invoice method placeholder, and the two methods in the wording they are offered in.</summary>
public class PayMethodConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return GradeWording.InvoiceMethod(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Turns the raw model into the friendly display model (e.g. '8' -> 'iPhone 8').
/// Takes the whole <see cref="DeviceData"/> rather than the model string, because
/// deciding between an "iPhone " prefix and no prefix at all needs the platform.
/// </summary>
public class ModelDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DeviceData data)
            return Mappers.FormatDisplayModel(data.Model, data.ProductType);

        if (value is not string model) return LocalizationManager.GetString("Value_UnknownDevice");
        return Mappers.FormatDisplayModel(model);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
