using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PhoneGrade.UI.Services;

namespace PhoneGrade.UI.Converters;

/// <summary>
/// The words next to a stored default, for the two pickers in settings.
///
/// The stored value stays what the product has always stored - "", "A", "B",
/// "C" and "", "Marge", "BTW" - and only the label in front of the operator is
/// translated. An empty value means "ask at every inspection", which used to
/// show as a blank line in the picker: the one option a picker must never have.
/// </summary>
public class QualityOptionLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // In settings an empty value means "ask at every inspection"; in the
        // editor the same empty value means nobody has graded this phone yet.
        // The view says which of the two it is asking about.
        string empty = parameter?.ToString() == "Unset"
            ? LocalizationManager.GetString("Editor_Unset")
            : LocalizationManager.GetString("Settings_QualityAsk");

        return value?.ToString() switch
        {
            "" or null => empty,
            "A" => LocalizationManager.GetString("Quality_A"),
            "B" => LocalizationManager.GetString("Quality_B"),
            "C" => LocalizationManager.GetString("Quality_C"),
            var other => other,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Same idea for the invoice method: the stored value, said in words.</summary>
public class PaymentOptionLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string empty = parameter?.ToString() == "Unset"
            ? LocalizationManager.GetString("Editor_Unset")
            : LocalizationManager.GetString("Settings_PaymentAsk");

        return value?.ToString() switch
        {
            "" or null => empty,
            PhoneGrade.Core.PaymentMethods.NeverAsk => LocalizationManager.GetString("Settings_PaymentNever"),
            "Marge" => LocalizationManager.GetString("Payment_Marge"),
            "BTW" => LocalizationManager.GetString("Payment_BTW"),
            var other => other,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
