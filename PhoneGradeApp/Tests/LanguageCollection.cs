using Xunit;

namespace PhoneGrade.Tests;

/// <summary>
/// The tests that change the application's language, kept out of each other's way.
///
/// <c>LocalizationManager</c> is one static holding one dictionary, and a test that
/// asks for English while another asks for Dutch does not get its own answer: it
/// gets whichever dictionary was merged last. That is not a fault anybody wrote, it
/// is simply what a shared static does, and it shows up as a test that fails once in
/// a dozen runs and passes the next time.
///
/// Putting every such test in one collection that does not run beside itself fixes
/// it for good, and fixes it for the tests that were already there rather than only
/// for the new ones.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LanguageCollection
{
    public const string Name = "language";
}
