namespace PhoneGrade.Core;

/// <summary>
/// Turns a value into a name a file system, a download and a path can all take.
/// </summary>
public static class FileNames
{
    /// <summary>
    /// Replaces what a file system or a browser will not take. Deliberately wider
    /// than Path.GetInvalidFileNameChars: the stems are used for downloads and for
    /// paths as well as for disk files, and a slash in one is a directory change.
    /// </summary>
    public static string Sanitize(string name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        char[] forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
        return string.Concat(name.Select(character => forbidden.Contains(character) ? '_' : character)).Trim();
    }
}
