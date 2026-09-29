namespace NMSE.Core.Utilities;

/// <summary>
/// XXH3 64-bit hash (xxHash), used by the game-derived fork helpers in <see cref="PRNG"/>.
/// Ported from the public domain xxHash reference implementation and verified against
/// reference vectors for the short, 17..128 and 129..240 byte input paths.
/// </summary>
internal static class Xxh3
{
    private const ulong Prime64_1 = 0x9E3779B185EBCA87UL;
    private const ulong Prime64_2 = 0xC2B2AE3D27D4EB4FUL;
    private const ulong Prime64_3 = 0x165667B19E3779F9UL;
    private const ulong AvalancheMultiplier = 0x165667919E3779F9UL;
    private const ulong RrmxmxMultiplier = 0x9FB21C651E98DF25UL;
    private const int SecretLastOffset = 136 - 17;

    private static readonly byte[] Secret = BuildSecret();

    /// <summary>Computes the XXH3 64-bit hash of the given bytes (seed 0).</summary>
    internal static ulong Hash64(ReadOnlySpan<byte> input)
    {
        int length = input.Length;
        if (length <= 16) return HashLength0To16(input, length);
        if (length <= 128) return HashLength17To128(input, length);
        return HashLength129To240(input, length);
    }

    private static byte[] BuildSecret()
    {
        ulong[] words =
        [
            0xBE4BA423396CFEB8UL, 0x1CAD21F72C81017CUL, 0xDB979083E96DD4DEUL, 0x1F67B3B7A4A44072UL,
            0x78E5C0CC4EE679CBUL, 0x2172FFCC7DD05A82UL, 0x8E2443F7744608B8UL, 0x4C263A81E69035E0UL,
            0xCB00C391BB52283CUL, 0xA32E531B8B65D088UL, 0x4EF90DA297486471UL, 0xD8ACDEA946EF1938UL,
            0x3F349CE33F76FAA8UL, 0x1D4F0BC7C7BBDCF9UL, 0x3159B4CD4BE0518AUL, 0x647378D9C97E9FC8UL,
            0xC3EBD33483ACC5EAUL, 0xEB6313FAFFA081C5UL, 0x49DAF0B751DD0D17UL, 0x9E68D429265516D3UL,
            0xFCA1477D58BE162BUL, 0xCE31D07AD1B8F88FUL, 0x280416958F3ACB45UL, 0x7E404BBBCAFBD7AFUL,
        ];
        var bytes = new byte[words.Length * 8];
        for (int i = 0; i < words.Length; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 8), words[i]);
        return bytes;
    }

    private static ulong Read64(ReadOnlySpan<byte> input, int offset) =>
        BitConverter.ToUInt64(input[offset..]);

    private static uint Read32(ReadOnlySpan<byte> input, int offset) =>
        BitConverter.ToUInt32(input[offset..]);

    private static ulong Swap64(ulong value) =>
        System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(value);

    private static ulong Rotl64(ulong value, int count) => (value << count) | (value >> (64 - count));

    private static ulong Xorshift(ulong value, int count) => value ^ (value >> count);

    private static ulong Multiply128Fold64(ulong left, ulong right)
    {
        ulong low = 0;
        ulong high = 0;
        for (int i = 0; i < 64; i++)
        {
            if (((right >> i) & 1) == 0) continue;
            ulong addLow = left << i;
            ulong addHigh = i == 0 ? 0 : left >> (64 - i);
            ulong newLow = unchecked(low + addLow);
            if (newLow < low) high++;
            low = newLow;
            high = unchecked(high + addHigh);
        }
        return low ^ high;
    }

    private static ulong Avalanche64(ulong value)
    {
        value = Xorshift(value, 33);
        value *= Prime64_2;
        value = Xorshift(value, 29);
        value *= Prime64_3;
        return Xorshift(value, 32);
    }

    private static ulong Avalanche3(ulong value)
    {
        value = Xorshift(value, 37);
        value *= AvalancheMultiplier;
        return Xorshift(value, 32);
    }

    private static ulong Rrmxmx(ulong value, ulong length)
    {
        value ^= Rotl64(value, 49) ^ Rotl64(value, 24);
        value *= RrmxmxMultiplier;
        value ^= (value >> 35) + length;
        value *= RrmxmxMultiplier;
        return Xorshift(value, 28);
    }

    private static ulong Mix16(ReadOnlySpan<byte> input, int inputOffset, int secretOffset)
    {
        ulong low = Read64(input, inputOffset) ^ Read64(Secret, secretOffset);
        ulong high = Read64(input, inputOffset + 8) ^ Read64(Secret, secretOffset + 8);
        return Multiply128Fold64(low, high);
    }

    private static ulong HashLength0To16(ReadOnlySpan<byte> input, int length)
    {
        if (length > 8)
        {
            ulong bitflip1 = Read64(Secret, 24) ^ Read64(Secret, 32);
            ulong bitflip2 = Read64(Secret, 40) ^ Read64(Secret, 48);
            ulong low = Read64(input, 0) ^ bitflip1;
            ulong high = Read64(input, length - 8) ^ bitflip2;
            ulong acc = unchecked((ulong)length + Swap64(low) + high + Multiply128Fold64(low, high));
            return Avalanche3(acc);
        }
        if (length >= 4)
        {
            uint input1 = Read32(input, 0);
            uint input2 = Read32(input, length - 4);
            ulong bitflip = Read64(Secret, 8) ^ Read64(Secret, 16);
            ulong input64 = input2 + ((ulong)input1 << 32);
            return Rrmxmx(input64 ^ bitflip, (ulong)length);
        }
        if (length > 0)
        {
            uint c1 = input[0];
            uint c2 = input[length >> 1];
            uint c3 = input[length - 1];
            uint combined = (c1 << 16) | (c2 << 24) | c3 | ((uint)length << 8);
            ulong bitflip = Read32(Secret, 0) ^ Read32(Secret, 4);
            return Avalanche64(combined ^ bitflip);
        }
        return Avalanche64(Read64(Secret, 56) ^ Read64(Secret, 64));
    }

    private static ulong HashLength17To128(ReadOnlySpan<byte> input, int length)
    {
        ulong acc = (ulong)length * Prime64_1;
        if (length > 32)
        {
            if (length > 64)
            {
                if (length > 96)
                {
                    acc += Mix16(input, 48, 96);
                    acc += Mix16(input, length - 64, 112);
                }
                acc += Mix16(input, 32, 64);
                acc += Mix16(input, length - 48, 80);
            }
            acc += Mix16(input, 16, 32);
            acc += Mix16(input, length - 32, 48);
        }
        acc += Mix16(input, 0, 0);
        acc += Mix16(input, length - 16, 16);
        return Avalanche3(acc);
    }

    private static ulong HashLength129To240(ReadOnlySpan<byte> input, int length)
    {
        ulong acc = (ulong)length * Prime64_1;
        int rounds = length / 16;
        for (int i = 0; i < 8; i++) acc += Mix16(input, 16 * i, 16 * i);
        acc = Avalanche3(acc);
        for (int i = 8; i < rounds; i++) acc += Mix16(input, 16 * i, 16 * (i - 8) + 3);
        acc += Mix16(input, length - 16, SecretLastOffset);
        return Avalanche3(acc);
    }
}
