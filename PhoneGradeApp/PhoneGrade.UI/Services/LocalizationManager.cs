using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhoneGrade.UI.Services;

/// <summary>
/// Manages runtime language switching by swapping merged resource dictionaries.
/// Call SetLanguage("nl") or any other code from
/// <see cref="SupportedLanguages"/> to update all DynamicResource bindings.
///
/// Which languages exist is <see cref="SupportedLanguages"/>' business, not this
/// class's. This one only loads the dictionary that was asked for, and the list of
/// them is a single table so a language is added in one place.
/// </summary>
public static class LocalizationManager
{
    private static IResourceProvider? _currentLanguageDictionary;
    private static string _currentLanguage = SupportedLanguages.DefaultCode;

    /// <summary>The code in use, which is what the phone suite is told as well.</summary>
    public static string CurrentLanguage => _currentLanguage;

    /// <summary>
    /// Loads and applies a language resource dictionary at runtime. A code this
    /// build does not carry becomes Dutch rather than leaving the previous
    /// dictionary on screen: a setting written by a build with more languages must
    /// not end up as an empty panel.
    /// </summary>
    public static void SetLanguage(string languageCode)
    {
        string wanted = SupportedLanguages.Normalize(languageCode);

        if (_currentLanguage == wanted && _currentLanguageDictionary != null)
        {
            // Already loaded
            return;
        }

        _currentLanguage = wanted;

        if (Application.Current?.Resources == null)
            return;

        // Remove previous language dictionary if present
        if (_currentLanguageDictionary != null)
        {
            Application.Current.Resources.MergedDictionaries.Remove(_currentLanguageDictionary);
            _currentLanguageDictionary = null;
        }

        // Load the new language resource dictionary
        var resourceUri = new Uri($"avares://PhoneGrade.UI/Resources/Strings.{wanted}.axaml");
        try
        {
            var newDict = (ResourceDictionary)AvaloniaXamlLoader.Load(resourceUri);
            Application.Current.Resources.MergedDictionaries.Add(newDict);
            _currentLanguageDictionary = newDict;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load language resource {resourceUri}: {ex.Message}");
        }
    }

    /// <summary>
    /// Initialize localization on app startup with the saved language preference.
    /// </summary>
    public static void Initialize(string languageCode)
    {
        SetLanguage(languageCode);
    }

    /// <summary>
    /// Reads a localized string from the current language dictionary. Returns the
    /// key itself when the dictionary is not yet loaded or the key is missing, so
    /// the UI never shows empty text by mistake.
    /// </summary>
    public static string GetString(string key)
    {
        if (Application.Current?.Resources == null)
            return key;
        if (Application.Current.Resources.TryGetResource(key, null, out object? value) &&
            value is string str && str.Length > 0)
            return str;
        return key;
    }
}
