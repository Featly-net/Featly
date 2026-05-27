using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Featly.Server.Authentication;

/// <summary>
/// Argon2id wrapper used by Featly's API-key pipeline. Generates plaintext
/// tokens, derives their prefix for indexed lookup, hashes them for storage,
/// and verifies an incoming token against a stored hash.
/// </summary>
/// <remarks>
/// <para>
/// Parameters follow the conservative defaults from OWASP / RFC 9106:
/// memory = 64 MiB, iterations = 3, parallelism = 2, 16-byte salt, 32-byte
/// output. Total per-hash cost is roughly 100ms on modern hardware which is
/// the right shape for an admin-API key (rare, slow). The prefix-indexed
/// candidate lookup ensures we Argon2-verify at most one candidate per
/// authentication request.
/// </para>
/// <para>
/// Plaintext token format: <c>featly_</c> + 32 base32-encoded random bytes
/// (URL-safe, no padding). The prefix (12 chars: <c>featly_</c> + the first
/// five characters of the random tail) is what gets indexed for lookup.
/// </para>
/// </remarks>
public sealed class ApiKeyHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 3;
    private const int MemoryKiB = 64 * 1024;
    private const int Parallelism = 2;

    private const string TokenPrefix = "featly_";
    private const int RandomBytes = 32;

    // 12 chars: "featly_" (7) + 5 from the random tail.
    private const int PrefixLength = 7 + 5;

    /// <summary>
    /// Mints a fresh random plaintext token. The caller persists only the
    /// hash + prefix; the plaintext is shown to the operator exactly once.
    /// </summary>
    public string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[RandomBytes];
        RandomNumberGenerator.Fill(bytes);
        return TokenPrefix + Base32Encode(bytes);
    }

    /// <summary>Returns the prefix portion of a plaintext token (for indexed lookup).</summary>
    public static string ExtractPrefix(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return plaintext.Length <= PrefixLength ? plaintext : plaintext[..PrefixLength];
    }

    /// <summary>Hashes a plaintext token with Argon2id. The returned string carries the parameters and salt inline.</summary>
    public string Hash(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        Span<byte> salt = stackalloc byte[SaltSize];
        RandomNumberGenerator.Fill(salt);

        using var argon2 = NewArgon2(plaintext, salt.ToArray());
        var hash = argon2.GetBytes(HashSize);

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"argon2id$v=19$m={MemoryKiB},t={Iterations},p={Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    /// <summary>Verifies a candidate plaintext against a stored hash in constant time.</summary>
    public static bool Verify(string plaintext, string storedHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(storedHash);

        // Expected layout: argon2id$v=19$m=<m>,t=<t>,p=<p>$<saltB64>$<hashB64>
        var parts = storedHash.Split('$');
        if (parts.Length != 5 || parts[0] != "argon2id")
        {
            return false;
        }

        // Parameters are fixed for v0.0.x. We still parse them so a future
        // bump can change them without invalidating old hashes — the values
        // come straight from the stored header.
        var paramSegment = parts[2].Split(',');
        if (paramSegment.Length != 3)
        {
            return false;
        }
        if (!TryParseKv(paramSegment[0], "m", out var m) ||
            !TryParseKv(paramSegment[1], "t", out var t) ||
            !TryParseKv(paramSegment[2], "p", out var p))
        {
            return false;
        }

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        using var argon2 = NewArgon2(plaintext, salt, iterations: t, memoryKiB: m, parallelism: p);
        var actual = argon2.GetBytes(expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static Argon2id NewArgon2(string plaintext, byte[] salt, int iterations = Iterations, int memoryKiB = MemoryKiB, int parallelism = Parallelism)
    {
        return new Argon2id(Encoding.UTF8.GetBytes(plaintext))
        {
            Salt = salt,
            DegreeOfParallelism = parallelism,
            Iterations = iterations,
            MemorySize = memoryKiB,
        };
    }

    private static bool TryParseKv(string segment, string expectedKey, out int value)
    {
        value = 0;
        var eq = segment.IndexOf('=');
        if (eq <= 0 || segment[..eq] != expectedKey)
        {
            return false;
        }
        return int.TryParse(segment[(eq + 1)..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    // RFC 4648 base32, no padding. Tokens stay URL-safe and case-insensitive
    // friendly without pulling in a third-party encoder.
    private static readonly char[] s_alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();

    private static string Base32Encode(ReadOnlySpan<byte> input)
    {
        var output = new StringBuilder((input.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        for (var i = 0; i < input.Length; i++)
        {
            buffer = (buffer << 8) | input[i];
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                output.Append(s_alphabet[(buffer >> bits) & 0x1F]);
            }
        }
        if (bits > 0)
        {
            output.Append(s_alphabet[(buffer << (5 - bits)) & 0x1F]);
        }
        return output.ToString();
    }
}
