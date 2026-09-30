using System;
using System.Diagnostics;

namespace PhoneGrade.UI.Services;

/// <summary>
/// The one place that knows where the paid plan is sold. The app never shows a
/// price itself: prices, currencies and regional tax are the checkout's job, and
/// a price typed into this repository goes stale the moment it changes there.
/// </summary>
public static class PricingLink
{
    /// <summary>The pricing page the app points people at.</summary>
    public const string Url = "https://phonegrade.app/pricing";

    /// <summary>
    /// Builds the launch for a URL without starting it, so the test can assert
    /// the launch is a shell execute (the thing that differs per platform)
    /// without opening a browser during the test run.
    /// </summary>
    public static ProcessStartInfo BuildOpen(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("A link to open cannot be empty.", nameof(url));

        return new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        };
    }

    /// <summary>Opens <paramref name="url"/> in the user's browser. Failures are swallowed: a blocked browser must never break the app.</summary>
    public static void Open(string url)
    {
        try
        {
            Process.Start(BuildOpen(url));
        }
        catch (Exception)
        {
            // No browser registered, sandboxed session, or the OS refused. The
            // operator can still type the address; nothing here is worth a crash.
        }
    }
}
