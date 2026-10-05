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
/// The key is derived with PBKDF2 (Rfc2898DeriveBytes) from the machine fingerprint
/// and a hardcoded salt baked into the binary. This is deliberate tamper resistance
/// for an published source tree, not secrecy: anyone can read the source, but editing
/// settings.json or sys_cache.dat without the key yields a token that decrypts to
/// nothing, and the store then falls back to the other location.
///
/// The fingerprint replaced a computer name plus user name here. A name is editable
/// without leaving the machine, so a token keyed on one could be moved to another
/// computer by renaming it, and it said nothing the app could act on. There are no
/// stored tokens in the wild to migrate, so the old material is gone rather than
/// kept as a second way in; a token from the previous build simply stops decrypting
/// and the free tier starts again at zero.
///
/// A machine with no readable fingerprint falls back to the name pair, because the
/// ten free scans must not depend on the operating system handing out a machine id.
/// Such a machine cannot activate, so nothing rides on this except the free count.
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
    /// PBKDF2 over the machine fingerprint with the hardcoded salt. Keying on the
    /// machine rather than on the account means a profile copied to another
    /// computer (or a token carried over by hand) decrypts to nothing there, so
    /// the free scan count cannot travel between machines.
    /// </summary>
    private static byte[] DeriveKey()
    {
        using var deriveBytes = new Rfc2898DeriveBytes(
            MachineFingerprint.Current.CipherMaterial,
            Encoding.UTF8.GetBytes(Salt),
            Iterations,
            HashAlgorithmName.SHA256);
        return deriveBytes.GetBytes(KeySize);
    }
}
