using System;
using PhoneGrade.Core.Licensing;
using Xunit;

namespace Tests;

/// <summary>
/// Covers the AES-GCM trial cipher: round trips on this machine, and every way a
/// token can be wrong (hand edited, truncated, wrong version, foreign payload),
/// which all have to land on null rather than on a usable scan count.
/// </summary>
public class TrialStateCipherTests
{
    [Fact]
    public void Encrypt_RoundTrip_RestoresScanCountAndLicenseKey()
    {
        var state = new TrialState { ScanCount = 7, LicenseKey = "LEMON-KEY-1234-ABCD" };

        string token = TrialStateCipher.Encrypt(state);
        TrialState? restored = TrialStateCipher.TryDecryptState(token);

        Assert.NotNull(restored);
        Assert.Equal(7, restored!.ScanCount);
        Assert.Equal("LEMON-KEY-1234-ABCD", restored.LicenseKey);
    }

    [Fact]
    public void Encrypt_RoundTrip_EmptyStateSurvives()
    {
        string token = TrialStateCipher.Encrypt(new TrialState());
        TrialState? restored = TrialStateCipher.TryDecryptState(token);

        Assert.NotNull(restored);
        Assert.Equal(0, restored!.ScanCount);
        Assert.Equal("", restored.LicenseKey);
    }

    [Fact]
    public void Encrypt_IsNonDeterministic_SameStateYieldsDifferentTokens()
    {
        // A fresh GCM nonce per call: identical counts in two files must not be
        // byte-identical, otherwise copying a token would look legitimate.
        var state = new TrialState { ScanCount = 3, LicenseKey = "KEY" };

        string first = TrialStateCipher.Encrypt(state);
        string second = TrialStateCipher.Encrypt(state);

        Assert.NotEqual(first, second);
        Assert.Equal(3, TrialStateCipher.TryDecryptState(first)!.ScanCount);
        Assert.Equal(3, TrialStateCipher.TryDecryptState(second)!.ScanCount);
    }

    [Fact]
    public void Encrypt_OutputIsBase64AndDoesNotCarryPlaintext()
    {
        var state = new TrialState { ScanCount = 9, LicenseKey = "SECRET-LICENSE-KEY" };
        string token = TrialStateCipher.Encrypt(state);

        byte[] bytes = Convert.FromBase64String(token); // throws when not Base64
        Assert.NotEmpty(bytes);
        Assert.DoesNotContain("SECRET-LICENSE-KEY", token, StringComparison.Ordinal);
        Assert.DoesNotContain("ScanCount", token, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 at all !!")]
    [InlineData("AAAA")] // valid Base64, far too short for the header
    public void TryDecrypt_MalformedToken_ReturnsNull(string? token)
    {
        Assert.Null(TrialStateCipher.TryDecrypt(token));
        Assert.Null(TrialStateCipher.TryDecryptState(token));
    }

    [Fact]
    public void TryDecrypt_TamperedCiphertext_ReturnsNull()
    {
        string token = TrialStateCipher.Encrypt(new TrialState { ScanCount = 9, LicenseKey = "KEY" });
        byte[] payload = Convert.FromBase64String(token);

        // Flip one bit deep inside the ciphertext: the scan count would silently
        // change if GCM authentication were not checked.
        int last = payload.Length - 1;
        payload[last] ^= 0x01;

        Assert.Null(TrialStateCipher.TryDecrypt(Convert.ToBase64String(payload)));
    }

    [Fact]
    public void TryDecrypt_TamperedTag_ReturnsNull()
    {
        string token = TrialStateCipher.Encrypt(new TrialState { ScanCount = 9 });
        byte[] payload = Convert.FromBase64String(token);

        payload[1 + 12] ^= 0xFF; // first byte of the GCM tag (version | nonce | tag | body)

        Assert.Null(TrialStateCipher.TryDecrypt(Convert.ToBase64String(payload)));
    }

    [Fact]
    public void TryDecrypt_UnsupportedVersion_ReturnsNull()
    {
        string token = TrialStateCipher.Encrypt(new TrialState { ScanCount = 4 });
        byte[] payload = Convert.FromBase64String(token);
        payload[0] = 0x7F;

        Assert.Null(TrialStateCipher.TryDecrypt(Convert.ToBase64String(payload)));
    }

    [Fact]
    public void TryDecrypt_TokenForOtherPayloadShape_ReturnsNullState()
    {
        // Encrypts fine, but decrypts to something that is not a trial payload.
        string token = TrialStateCipher.Encrypt("this is not a trial state");

        Assert.Equal("this is not a trial state", TrialStateCipher.TryDecrypt(token));
        Assert.Null(TrialStateCipher.TryDecryptState(token));
    }

    [Fact]
    public void TryDecryptState_NegativeScanCount_IsRejectedAsCorrupt()
    {
        string token = TrialStateCipher.Encrypt("""{"ScanCount":-1,"LicenseKey":""}""");

        Assert.Null(TrialStateCipher.TryDecryptState(token));
    }
}
