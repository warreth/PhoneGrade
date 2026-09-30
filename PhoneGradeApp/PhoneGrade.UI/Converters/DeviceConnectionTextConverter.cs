using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhoneGrade.UI.Converters;

/// <summary>
/// Turns the selected device key into the words the header pill shows: a key
/// means a phone is plugged in, an empty one means nothing is.
///
/// The boolean converters are the right tool for picking the pill's colour,
/// but printing their result answers "True" or "False" instead of saying
/// anything, which is what this replaces.
/// </summary>
public class DeviceConnectionTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key && !string.IsNullOrEmpty(key) ? "Toestel Verbonden" : "Geen Toestel";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
