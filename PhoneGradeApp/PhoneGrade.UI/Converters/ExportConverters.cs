using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace PhoneGrade.UI.Converters;

/// <summary>
/// The colours on the export panel's result rows.
///
/// They are drawn from the theme rather than spelled out here, so the panel reads
/// as the rest of the app in both themes. The tints come from the theme dictionary
/// for the same reason: a row that glows green in the dark theme is a row nobody
/// can read the reason off.
/// </summary>
public class ExportResultBrushConverter : IValueConverter
{
    /// <summary>Kept so the view can bind a static instance of it.</summary>
    public static ExportResultBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Theme.Tint(Theme.Colors.Success) : Theme.Tint(Theme.Colors.Danger);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// The line under the export buttons. A failure is the one thing on this panel
/// that has to be findable at a glance, because it is the only thing the operator
/// would otherwise act on wrongly.
/// </summary>
public class ExportStatusBrushConverter : IValueConverter
{
    public static ExportStatusBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Theme.Solid(Theme.Colors.Danger) : Theme.Solid(Theme.Colors.TextDim);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Reads a colour out of the theme dictionary.
///
/// The panel needs the tint and solid forms of the same four colours, and the
/// values live in the application resources where a light and a dark variant can
/// differ. Going through the resources rather than through literals here is what
/// keeps the export panel in the same palette as the rest of the app.
/// </summary>
internal static class Theme
{
    public readonly record struct Palette(string Success, string Danger, string TextDim);

    /// <summary>
    /// The palette for the theme that is applied. Read once per conversion rather
    /// than cached, because the theme is switched at run time and a cached brush
    /// would keep the colours of the theme that was on when the panel was first
    /// drawn.
    /// </summary>
    public static Palette Colors
    {
        get
        {
            Avalonia.Application? app = Avalonia.Application.Current;
            string fallbackSuccess = "#2FBF71";
            string fallbackDanger = "#F0564A";
            string fallbackDim = "#9B9BA6";
            return new Palette(
                Read(app, "SuccessColor", fallbackSuccess),
                Read(app, "DangerColor", fallbackDanger),
                Read(app, "TextDimColor", fallbackDim));
        }
    }

    private static string Read(Avalonia.Application? app, string key, string fallback)
    {
        if (app is null) return fallback;
        return app.TryGetResource(key, null, out object? value) && value is Color color
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : fallback;
    }

    /// <summary>The translucent fill a row with news is drawn on.</summary>
    public static IBrush Tint(string hex) => WithAlpha(hex, 0x26);

    /// <summary>The full strength colour, for text.</summary>
    public static IBrush Solid(string hex) => WithAlpha(hex, 0xFF);

    private static IBrush WithAlpha(string hex, byte alpha)
    {
        string digits = hex.TrimStart('#');
        byte r = Convert.ToByte(digits[..2], 16);
        byte g = Convert.ToByte(digits[2..4], 16);
        byte b = Convert.ToByte(digits[4..6], 16);
        return new ImmutableSolidColorBrush(Color.FromArgb(alpha, r, g, b));
    }
}