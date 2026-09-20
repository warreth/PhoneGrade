using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhoneGrade.UI.Services;

/// <summary>
/// Manages runtime language switching by swapping merged resource dictionaries.
/// Call SetLanguage("en") or SetLanguage("nl") to dynamically update all DynamicResource bindings.
/// </summary>
public static class LocalizationManager
{
    private static IResourceProvider? _currentLanguageDictionary;
    private static string _currentLanguage = "nl";

    public static string CurrentLanguage => _currentLanguage;

    /// <summary>
    /// Load and apply a language resource dictionary at runtime.
    /// Supported: "en" (English), "nl" (Nederlands).
    /// </summary>
    public static void SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            languageCode = "nl";

        languageCode = languageCode.ToLowerInvariant();
        if (languageCode != "en" && languageCode != "nl")
            languageCode = "nl";

        if (_currentLanguage == languageCode && _currentLanguageDictionary != null)
        {
            // Already loaded
            return;
        }

        _currentLanguage = languageCode;

        if (Application.Current?.Resources == null)
            return;

        // Remove previous language dictionary if present
        if (_currentLanguageDictionary != null)
        {
            Application.Current.Resources.MergedDictionaries.Remove(_currentLanguageDictionary);
            _currentLanguageDictionary = null;
        }

        // Load the new language resource dictionary
        var resourceUri = new Uri($"avares://PhoneGrade.UI/Resources/Strings.{languageCode}.axaml");
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
}
