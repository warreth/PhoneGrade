using System;

namespace PhoneGrade.UI.Services;

/// <summary>
/// Whether the operator is being asked to agree to the licence, and to which one.
///
/// The distinction that matters: a customer who has never paid is running the free
/// plan, which is covered by the licence the source is published under. A customer
/// who has a Subscription is running under the separate commercial end user licence
/// agreement, and that is a different document with a liability cap in it. Showing
/// the wrong one is worse than showing neither, so the state is worked out rather
/// than guessed at the call site.
/// </summary>
public static class LicenceTerms
{
    /// <summary>
    /// The licence the source is published under. Applies to the free plan.
    /// </summary>
    public const string Repository = "https://github.com/warreth/PhoneGrade/blob/main/LICENSE";

    /// <summary>The commercial end user licence agreement, for paying customers.</summary>
    public const string Commercial = "https://github.com/warreth/PhoneGrade/blob/main/COMMERCIAL_EULA.md";

    /// <summary>The trademark policy, which the commercial terms also point at.</summary>
    public const string Trademark = "https://github.com/warreth/PhoneGrade/blob/main/TRADEMARK_POLICY.md";

    /// <summary>Where the author is. Named on every screen that asks for agreement.</summary>
    public const string Author = "WarreTh";

    public const string AuthorEmail = "hello@phonegrade.app";

    /// <summary>
    /// Which set of terms applies.
    ///
    /// A free operator is covered by the licence the source is published under, which
    /// is what they downloaded and what the attribution requirement hangs off. A
    /// paying operator is not covered by that licence, because PolyForm Noncommercial
    /// does not reach commercial use, so they are running under the commercial end
    /// user licence agreement instead. The two carry different liability language, so
    /// showing the wrong one is worse than showing neither.
    /// </summary>
    public static string DocumentFor(bool isPro) => isPro ? Commercial : Repository;

    /// <summary>
    /// What the checkbox is agreeing to, in one sentence.
    ///
    /// A box that says only "I agree" leaves an operator who did not read the
    /// document agreeing to something they cannot describe afterwards. It names the
    /// document, and the word "read" is deliberate: agreeing without reading is still
    /// agreement, and the label should say which document it was.
    /// </summary>
    public static string Describe(bool isPro) => isPro
        ? "the commercial end user licence agreement"
        : "the licence terms";
}