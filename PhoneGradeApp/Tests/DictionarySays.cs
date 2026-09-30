using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace PhoneGrade.Tests;

/// <summary>
/// What a step says to the operator, read the way it is told now.
///
/// The sentences used to sit in the step's own script, so a test could check
/// that a card told the operator something by looking for the words in the
/// file. They live in the dictionary now, because the phone has to say the same
/// thing in English as well, and a script only holds the key it asks for.
///
/// So a check on what a card says reads the dictionary for the entry carrying
/// the sentence and then reads the script back for the key that entry is filed
/// under. That keeps the original question - does this step tell the operator
/// X - instead of turning it into the weaker one - does this script mention
/// something.
/// </summary>
internal static class DictionarySays
{
    private static readonly Regex Entry = new(
        @"^\s*'(?<key>[^']+)':\s*'(?<value>(?:[^'\\]|\\.)*)'\s*,?\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Dutch = new(() =>
        Load("PhoneGradeApp/PhoneGrade.UI/wwwroot/locales/nl.js"));

    private static readonly Lazy<IReadOnlyDictionary<string, string>> English = new(() =>
        Load("PhoneGradeApp/PhoneGrade.UI/wwwroot/locales/en.js"));

    /// <summary>
    /// Asserts that <paramref name="source"/> asks the dictionary for a key
    /// whose Dutch wording contains <paramref name="fragment"/>.
    /// </summary>
    /// <param name="source">the step's script, already read</param>
    /// <param name="fragment">the words the card is expected to show</param>
    public static void Says(string source, string fragment)
    {
        var candidates = Dutch.Value
            .Where(entry => entry.Value.Contains(fragment, StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .ToList();

        Assert.True(candidates.Count > 0,
            $"No Dutch dictionary entry carries \"{fragment}\".");

        var asked = candidates
            .Where(key => source.Contains($"t('{key}'", StringComparison.Ordinal))
            .ToList();

        Assert.True(asked.Count > 0,
            $"The step never asks the dictionary for \"{fragment}\". " +
            $"Filed under: {string.Join(", ", candidates)}");

        foreach (var key in asked)
        {
            Assert.True(English.Value.ContainsKey(key),
                $"nl.js has {key} but en.js does not, so an English operator would see the key.");
        }
    }

    private static IReadOnlyDictionary<string, string> Load(string path)
    {
        var file = RepoPath.Read(path);
        var loaded = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in Entry.Matches(file))
        {
            loaded[match.Groups["key"].Value] = Unescape(match.Groups["value"].Value);
        }

        Assert.True(loaded.Count > 0, $"No entries parsed out of {path}.");
        return loaded;
    }

    private static string Unescape(string value) => value
        .Replace("\\'", "'")
        .Replace("\\\"", "\"")
        .Replace("\\\\", "\\");
}
