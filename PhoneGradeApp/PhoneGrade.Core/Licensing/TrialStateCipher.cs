using System.Security.Cryptography;
using System.Text;

namespace PhoneGrade.Core.Licensing;

/// <summary>
/// Encrypts the trial payload so neither storage location can be edited by hand:
/// the token is Base64, but the bytes behind it only ever decrypt on this machine
/// for this user, and AES-GCM authenticates them, so flipping any byte (for
/// instance lowering the scan count inside a copied token) fails verification
/// instead of producing a plausible value.
///
/// The key is derived with PBKDF2 (Rfc2898DeriveBytes) from the machine name, the
/// user name and a hardcoded salt baked into the binary. This is deliberate
/// tamper resistance for an published source tree, not secrecy: anyone can read the
/// source, but editing settings.json or sys_cache.dat without the key yields a
/// token that decrypts to nothing, and the store then falls back to the other
/// location.
///
/// Payload layout before Base64: version (1 byte) | nonce (12) | GCM tag (16) | ciphertext.
/// </summary>
public static class TrialStateCipher
{
    /// <summary>Hardcoded salt. Combined with the per-machine material, never used on its own.</summary>
    private const string Salt = "PhoneGrade.Licensing.TrialState.v1.salt";

    private const int Iterations = 100_000;
    private const int KeySize = 32;   // AES-256
    private const int NonceSize = 12; // standard GCM nonce
    private const int TagSize = 16;   // standard GCM tag
    private const byte Version = 0x01;

    /// <summary>Authenticated as additional data: a token from another product of this family is rejected early.</summary>
    private static readonly byte[] AssociatedData = Encoding.ASCII.GetBytes("PhoneGrade.TrialState");

    /// <summary>Serializes and encrypts <paramref name="state"/> as a Base64 token.</summary>
    public static string Encrypt(TrialState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Encrypt(state.ToJson());
    }

    /// <summary>Encrypts an arbitrary payload as a Base64 token.</summary>
    public static string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] tag = new byte[TagSize];

        using (var aes = new AesGcm(DeriveKey(), TagSize))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, AssociatedData);
        }

        byte[] payload = new byte[1 + NonceSize + TagSize + ciphertext.Length];
        payload[0] = Version;
        Buffer.BlockCopy(nonce, 0, payload, 1, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, 1 + NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, payload, 1 + NonceSize + TagSize, ciphertext.Length);
        return Convert.ToBase64String(payload);
    }

    /// <summary>
    /// Decrypts a Base64 token back to its payload, or null when the token is
    /// empty, not Base64, truncated, from another version, or fails GCM
    /// verification (edited bytes or another machine's key). Null is the only
    /// failure shape: callers treat it exactly like a missing token.
    /// </summary>
    public static string? TryDecrypt(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            byte[] payload = Convert.FromBase64String(token);
            int header = 1 + NonceSize + TagSize;
            if (payload.Length < header || payload[0] != Version) return null;

            byte[] nonce = payload.AsSpan(1, NonceSize).ToArray();
            byte[] tag = payload.AsSpan(1 + NonceSize, TagSize).ToArray();
            byte[] ciphertext = payload.AsSpan(header).ToArray();
            byte[] plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(DeriveKey(), TagSize);
            try
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
            }
            catch (CryptographicException)
            {
                return null; // authentication failed: the bytes were edited
            }

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception)
        {
            return null; // malformed Base64, wrong length, crypto failure
        }
    }

    /// <summary>Decrypts and parses in one step; null on any failure.</summary>
    public static TrialState? TryDecryptState(string? token) =>
        TrialState.FromJson(TryDecrypt(token));

    /// <summary>
    /// PBKDF2 over MachineName + UserName with the hardcoded salt. Combining
    /// both means a copied profile (same user, new machine) or a renamed account
    /// on the same machine invalidates every stored token, so the count cannot
    /// be carried over to another installation.
    /// </summary>
    private static byte[] DeriveKey()
    {
        string material = $"{Environment.MachineName}|{Environment.UserName}";
        using var deriveBytes = new Rfc2898DeriveBytes(
            material, Encoding.UTF8.GetBytes(Salt), Iterations, HashAlgorithmName.SHA256);
        return deriveBytes.GetBytes(KeySize);
    }
}
