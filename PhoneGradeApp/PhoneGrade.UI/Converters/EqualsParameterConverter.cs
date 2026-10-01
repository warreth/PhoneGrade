using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhoneGrade.UI.Converters;

/// <summary>
/// Answers whether the bound value is the same word as the converter parameter.
///
/// The settings page keeps one topic on screen at a time, and this is what both
/// ends of that are keyed on: which panel is visible, and which button in the
/// topic list is lit. A single string in the view model beats a boolean per
/// topic, because a new topic then costs one line in the view instead of a
/// property, a backing field and three places to keep in step.
/// </summary>
public class EqualsParameterConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
