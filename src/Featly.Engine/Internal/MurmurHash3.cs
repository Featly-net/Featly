using System.Runtime.CompilerServices;
using System.Text;

namespace Featly.Engine.Internal;

/// <summary>
/// MurmurHash3 32-bit (x86 variant). Deterministic, fast, and non-cryptographic —
/// the exact properties we need to bucket a subject into a stable variant.
///
/// Implementation follows Austin Appleby's reference algorithm. Same subject
/// + same key yields the same bucket forever, which means the same subject
/// gets the same variant as long as the rule's weights don't change.
/// </summary>
/// <remarks>
/// Not for cryptographic use. SDK / Server share this implementation so
/// client and server produce identical buckets for identical inputs.
/// </remarks>
internal static class MurmurHash3
{
    private const uint C1 = 0xCC9E2D51;
    private const uint C2 = 0x1B873593;
    private const uint Seed = 0; // Featly always seeds with zero; the per-flag salt
                                 // is folded into the input string by the caller.

    /// <summary>
    /// Hashes the UTF-8 bytes of <paramref name="input"/> and returns a value
    /// in <c>[0, 10000)</c>. The 10 000 buckets match the precision documented
    /// in ARCHITECTURE.md and give per-1/100-percentage-point granularity.
    /// </summary>
    public static int BucketOf10000(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = Hash32(bytes);
        // Modulo bias is negligible at 10 000 buckets vs 2^32, and matches
        // LaunchDarkly / Unleash conventions.
        return (int)(hash % 10000u);
    }

    /// <summary>Direct hash of arbitrary byte data. Useful for tests.</summary>
    public static uint Hash32(ReadOnlySpan<byte> data)
    {
        var h1 = Seed;
        var length = data.Length;

        // Body — 4-byte blocks.
        var blocks = length / 4;
        for (var i = 0; i < blocks; i++)
        {
            var k1 = BitConverter.ToUInt32(data.Slice(i * 4, 4));
            k1 *= C1;
            k1 = RotateLeft(k1, 15);
            k1 *= C2;

            h1 ^= k1;
            h1 = RotateLeft(h1, 13);
            h1 = (h1 * 5u) + 0xE6546B64u;
        }

        // Tail — up to 3 trailing bytes.
        uint tail = 0;
        var tailStart = blocks * 4;
        var remaining = length - tailStart;
        if (remaining >= 3)
        { tail ^= (uint)data[tailStart + 2] << 16; }
        if (remaining >= 2)
        { tail ^= (uint)data[tailStart + 1] << 8; }
        if (remaining >= 1)
        {
            tail ^= data[tailStart];
            tail *= C1;
            tail = RotateLeft(tail, 15);
            tail *= C2;
            h1 ^= tail;
        }

        // Finalisation mix.
        h1 ^= (uint)length;
        h1 ^= h1 >> 16;
        h1 *= 0x85EBCA6Bu;
        h1 ^= h1 >> 13;
        h1 *= 0xC2B2AE35u;
        h1 ^= h1 >> 16;

        return h1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint RotateLeft(uint x, int r) => (x << r) | (x >> (32 - r));
}
