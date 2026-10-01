using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PhoneGrade.Core;
using PhoneGrade.UI.Services;

namespace PhoneGrade.UI.Converters;

/// <summary>
/// Turns the audit result of a part into the word shown on its badge. The word
/// comes from the string dictionaries, so the badge reads in the operator's
/// language; what this class decides is which of them applies.
/// </summary>
public class ComponentStatusConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ComponentStatusType status) return LocalizationManager.GetString("Value_Unknown");

        return status switch
        {
            ComponentStatusType.Match => LocalizationManager.GetString("Component_Original"),
            ComponentStatusType.Mismatch => LocalizationManager.GetString("Component_Mismatch"),
            ComponentStatusType.Untrusted => LocalizationManager.GetString("Component_Untrusted"),
            ComponentStatusType.Unknown => LocalizationManager.GetString("Value_Unknown"),
            _ => LocalizationManager.GetString("Value_Unknown"),
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
