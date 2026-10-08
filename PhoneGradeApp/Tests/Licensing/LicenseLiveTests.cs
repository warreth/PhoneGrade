using System;
using System.IO;
using PhoneGrade.Core.Licensing;
using PhoneGrade.Tests;
using Xunit;

namespace Tests;

/// <summary>
/// Where the live licence facts get their key from.
///
/// The key is read at run time so it never has to sit in the repository: CI can
/// pass PHONEGRADE_LICENSE_KEY as an environment variable, and a local run reads
/// PhoneGradeApp/Tests/.license-key, which is ignored by git. Without either of
/// them the live facts report themselves as skipped rather than quietly passing.
/// </summary>
internal static class LicenseLiveKey
{
    public const string EnvironmentVariable = "PHONEGRADE_LICENSE_KEY";

    /// <summary>Repository relative file the key is read from when there is no environment variable.</summary>
    public static readonly string[] FilePath = { "PhoneGradeApp", "Tests", ".license-key" };

    private static readonly object Gate = new();
    private static string? _key;
    private static LicenseValidationResponse? _validation;

    /// <summary>The key to run with, or null when the caller did not provide one.</summary>
    public static string? TryRead()
    {
        lock (Gate)
        {
            if (_key is not null)
                return _key;

            string? fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
                return _key = fromEnvironment.Trim();

            try
            {
                string path = RepoPath.Get(FilePath);
                if (File.Exists(path))
                {
                    string fromFile = File.ReadAllText(path).Trim();
                    if (fromFile.Length > 0)
                        return _key = fromFile;
                }
            }
            catch (DirectoryNotFoundException)
            {
                // Outside a checkout, so there is no file to fall back to.
            }

            return null;
        }
    }

    /// <summary>
    /// The key's state, read once per run. The attribute uses it so a fact that
    /// would claim a seat on a key whose seats are all taken can skip with the
    /// reason instead of failing on a state of the account.
    /// </summary>
    public static LicenseValidationResponse? Validation()
    {
        lock (Gate)
        {
            if (_validation is null && TryRead() is { } key)
            {
                using var client = new LemonSqueezyClient();
                _validation = client.ValidateDetailedAsync(key).GetAwaiter().GetResult();
            }

            return _validation;
        }
    }
}

/// <summary>
/// A fact that talks to the real Lemon Squeezy licensing endpoints.
///
/// It reports itself as skipped, with the reason, when no key was supplied, when
/// the store did not answer, or when a fact that claims a seat finds every seat
/// already taken. A fact that quietly returned instead would look the same as a
/// fact that passed.
/// </summary>
public sealed class LicenseLiveFactAttribute : FactAttribute
{
    public LicenseLiveFactAttribute(bool needsSeat = false)
    {
        string? key = LicenseLiveKey.TryRead();
        if (key is null)
        {
            Skip = "No licence key: set " + LicenseLiveKey.EnvironmentVariable
                + " or write one to " + string.Join("/", LicenseLiveKey.FilePath) + ".";
            return;
        }

        if (!needsSeat)
            return;

        LicenseValidationResponse? state = LicenseLiveKey.Validation();
        if (state is null)
        {
            Skip = "The Lemon Squeezy validate call did not answer.";
            return;
        }

        if (!state.Valid)
        {
            Skip = "The licence key is not active: " + state.Error;
            return;
        }

        if (state.ActivationLimit > 0 && state.ActivationUsage >= state.ActivationLimit)
        {
            Skip = $"All {state.ActivationLimit} seats on the key are taken, so a live activation would be refused.";
        }
    }
}

/// <summary>
/// The licensing endpoints against the real store.
///
/// The unit suite proves the parsing and the gate against fixtures; these facts
/// prove the key the owner holds is actually accepted for this product, takes a
/// seat, and hands it back. The key is never printed, and the seat fact releases
/// every seat it claims in a finally block.
/// </summary>
public class LicenseLiveTests
{
    private static string Key => LicenseLiveKey.TryRead() ?? "";

    [LicenseLiveFact]
    public async Task TheKeyIsValidAndSoldForThisProduct()
    {
        using var client = new LemonSqueezyClient();

        LicenseValidationResponse response = await client.ValidateDetailedAsync(Key);

        Assert.True(response.Valid, response.Error);
        Assert.Equal(LicenseValidationResult.Valid, response.Result);
        Assert.True(LemonSqueezyClient.IsPhoneGradeProduct(response.ProductId),
            $"the key belongs to product {response.ProductId}, which this build does not accept");
        Assert.True(response.ActivationLimit > 0, "the plan should carry at least one seat");
    }

    [LicenseLiveFact(needsSeat: true)]
    public async Task TheKeyTakesASeatAndHandsItBack()
    {
        using var client = new LemonSqueezyClient();
        string instanceName = "pg-livecheck-" + Guid.NewGuid().ToString("N")[..12];

        LicenseValidationResponse before = await client.ValidateDetailedAsync(Key);
        LicenseActivationResponse activation = await client.ActivateAsync(Key, instanceName);

        LicenseDeactivationResponse? released = null;
        try
        {
            Assert.Equal(LicenseValidationResult.Valid, activation.Result);
            Assert.False(string.IsNullOrWhiteSpace(activation.InstanceId), "no instance id came back");

            // With the instance named, the key answers about this seat rather than
            // about the key in general.
            LicenseValidationResponse seated = await client.ValidateDetailedAsync(Key, activation.InstanceId);
            Assert.True(seated.Valid, seated.Error);
            Assert.True(LemonSqueezyClient.IsPhoneGradeProduct(seated.ProductId),
                $"the seat answers with product {seated.ProductId}");
            Assert.Equal(activation.InstanceId, seated.InstanceId);
        }
        finally
        {
            // Released whenever the server may hold a seat, even after a failed
            // assert above: an activation that succeeded but was refused here still
            // took one, and walking away from it leaks exactly what this fact
            // exists to exercise.
            if (activation.Activated && activation.InstanceId.Length > 0)
            {
                released = await client.DeactivateAsync(Key, activation.InstanceId);
            }
        }

        Assert.NotNull(released);
        Assert.True(released.Deactivated, released.Error);

        LicenseValidationResponse after = await client.ValidateDetailedAsync(Key);
        Assert.Equal(before.ActivationUsage, after.ActivationUsage);
    }
}
