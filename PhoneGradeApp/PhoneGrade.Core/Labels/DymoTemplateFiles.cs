namespace PhoneGrade.Core;

/// <summary>
/// Finds the .dymo template to fill.
///
/// The template is searched for rather than embedded, because it is a file an
/// operator is meant to be able to change. The search order is: the path the
/// caller was given, then the app's own folder, then where the app was started
/// from. The last one is what makes a build that has not been published still
/// work.
///
/// There is one shipped template per label arrangement. The cleaned arrangement
/// keeps the file the app has always shipped; the structured and grade block
/// arrangements have their own, because moving objects about is the whole of what
/// makes an arrangement different. A shop that points the app at its own template
/// replaces all three: their file is the layout, and the arrangement setting then
/// only changes the label the app itself draws.
///
/// The caller's path is passed in rather than read from a static, because the
/// setting belongs to the settings object and a static here would mean a second
/// source of truth for it that a test, a second window or a settings reset could
/// each get wrong.
/// </summary>
public static class DymoTemplateFiles
{
    /// <summary>The template shipped with the app, for the cleaned arrangement.</summary>
    public const string TemplateName = "my.dymo";

    /// <summary>
    /// The shipped template for one arrangement.
    /// </summary>
    /// <remarks>
    /// Plain names rather than a naming scheme derived from the enum, because the
    /// shipped file is something a person may look for in the install folder.
    /// </remarks>
    public static string TemplateNameFor(LabelVariant variant) => variant switch
    {
        LabelVariant.Structured => "my-structured.dymo",
        LabelVariant.GradeBlock => "my-gradeblock.dymo",
        _ => TemplateName,
    };

    /// <summary>
    /// The template that came with the app, or null when it cannot be found.
    ///
    /// For a caller that wants to offer the shipped template as a way back from
    /// an operator's own one. Reading it raises rather than returning null, because
    /// a template that is configured but not there is a setting the operator has to
    /// be told about.
    /// </summary>
    public static string? Shipped => FindShipped(TemplateName);

    /// <summary>The shipped template for an arrangement, or null when it is not there.</summary>
    public static string? ShippedFor(LabelVariant variant) =>
        FindShipped(TemplateNameFor(variant)) ?? FindShipped(TemplateName);

    /// <summary>
    /// Reads the template to fill. Throws with the places it looked, because a
    /// missing template is the one export failure where the fix is obvious and the
    /// message is the only place to say it.
    /// </summary>
    /// <exception cref="FileNotFoundException">No template was found anywhere.</exception>
    public static string Read(
        string? configured = null, LabelVariant variant = LabelVariant.Clean,
        ExportWording? wording = null)
    {
        string path = Locate(configured, variant, wording);
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                (wording ?? ExportWording.English).Say(
                    (wording ?? ExportWording.English).TemplateUnreadable, path, ex.Message), ex);
        }
    }

    /// <summary>Where the template that will be filled actually is.</summary>
    public static string Locate(
        string? configured = null, LabelVariant variant = LabelVariant.Clean,
        ExportWording? wording = null)
    {
        ExportWording words = wording ?? ExportWording.English;
        // A template the operator picked is used or it is reported. Falling back
        // to the shipped one quietly would put labels out on a layout the shop
        // never chose, and nothing on screen would say so.
        if (configured is { Length: > 0 })
        {
            if (File.Exists(configured)) return configured;

            // Named on its own because the operator set it once and has since
            // moved the file; the generic message below would send them looking
            // in the install folder instead.
            throw new FileNotFoundException(words.Say(words.TemplateMissing, configured), configured);
        }

        var tried = new List<string>();
        foreach (string candidate in Candidates(TemplateNameFor(variant)))
        {
            tried.Add(candidate);
            if (File.Exists(candidate)) return candidate;
        }

        // A variant template that is not there is not a failure: the arrangement
        // changes where the words sit, and the words themselves are on every
        // template. An install from before the variant files existed, or a build
        // with one of them removed, prints the cleaned template rather than
        // refusing to print at all.
        if (variant != LabelVariant.Clean)
        {
            foreach (string candidate in Candidates(TemplateName))
            {
                tried.Add(candidate);
                if (File.Exists(candidate)) return candidate;
            }
        }

        throw new FileNotFoundException(
            words.Say(words.NoTemplateFound, TemplateName, string.Join(", ", tried)));
    }

    private static IEnumerable<string> Candidates(string name)
    {
        string exeDir = AppContext.BaseDirectory;
        yield return Path.Combine(exeDir, "Assets", name);
        yield return Path.Combine(exeDir, name);
        yield return Path.Combine(Directory.GetCurrentDirectory(), "Assets", name);
    }

    /// <summary>
    /// The template that came with the app, whatever the settings say. Used to put
    /// a template back when an operator's own one has gone wrong, which is a thing
    /// they need to be able to do without finding the install folder.
    /// </summary>
    public static string? FindShipped() => FindShipped(TemplateName);

    /// <summary>The shipped template with this file name, or null when it is not there.</summary>
    public static string? FindShipped(string name)
    {
        foreach (string candidate in Candidates(name))
            if (File.Exists(candidate))
                return candidate;
        return null;
    }
}
