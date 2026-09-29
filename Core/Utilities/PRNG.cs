namespace NMSE.Core.Utilities;

/// <summary>
/// Seed pair handling, the PRNG step, the roll fraction and the XXH3-based fork helpers shared
/// by the game-derived generators.
/// </summary>
internal static class PRNG
{
    // Packed algorithm constants (see the private notes for the layout and regeneration recipe).
    private static readonly byte[] Mask =
    [
        0x91, 0x3C, 0x5E, 0xA7, 0x28, 0xD4, 0x6B, 0x0F, 0x82, 0x57, 0xE9, 0x34, 0xB0, 0x4D, 0x16, 0xCA,
    ];

    private static readonly byte[] Packed =
    [
        0x08, 0xC4, 0x28, 0xFD, 0x28, 0xD4, 0x56, 0x0F, 0x11, 0x62, 0x6E, 0x2F,
    ];

    private static readonly byte[] Raw = Unmask(Packed);

    /// <summary>The PRNG step multiplier.</summary>
    internal static readonly uint StepMultiplier = BitConverter.ToUInt32(Raw, 0);

    private static readonly uint MixXor = BitConverter.ToUInt32(Raw, 4);
    private static readonly uint MixMultiplier = BitConverter.ToUInt32(Raw, 8);

    private static byte[] Unmask(byte[] data)
    {
        var raw = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            raw[i] = (byte)(data[i] ^ Mask[i & 15]);
        return raw;
    }

    /// <summary>A seed pair: the low (state) and high (hash) words of the 64-bit seed.</summary>
    internal readonly record struct SeedPair(uint State, uint Hash);

    /// <summary>
    /// Murmur-style mix of a 32-bit seed producing the initial 64-bit PRNG state used by the
    /// procedural technology generators.
    /// </summary>
    internal static ulong InitialState(uint seed)
    {
        uint h = unchecked(((seed ^ MixXor) >> 0x10 ^ seed) * 9u);
        h = unchecked((h >> 4 ^ h) * MixMultiplier);
        uint mixed = h ^ (h >> 0xf);
        uint addend = Rotl16(mixed) ^ mixed;
        uint multiplier = ((h >> 0xf) == h ? 1u : 0u) + mixed;
        return unchecked((ulong)multiplier * StepMultiplier + addend);
    }

    /// <summary>One PRNG step: multiply the low word, add the high word.</summary>
    internal static ulong Next(ulong state) =>
        unchecked((state & 0xffffffffUL) * StepMultiplier + (state >> 32));

    /// <summary>Advances a seed pair by one PRNG step.</summary>
    internal static uint NextValue(SeedPair pair) =>
        unchecked(pair.State * StepMultiplier + pair.Hash);

    /// <summary>Normalised roll fraction from the low word (0..1).</summary>
    internal static float Fraction(ulong state) => Fraction((uint)state);

    /// <summary>Normalised roll fraction from a 32-bit rolled value (0..1).</summary>
    internal static float Fraction(uint value) => value / 4294967296f;

    /// <summary>
    /// Builds the seed pair from a 64-bit seed: the state is max(low word, 1) and the hash is
    /// the rotl16 mix of the two words.
    /// </summary>
    internal static SeedPair FromSeed(ulong seed)
    {
        uint low = (uint)seed;
        return new SeedPair(low > 1 ? low : 1, Rotl16(low) ^ (uint)(seed >> 32) ^ low);
    }

    /// <summary>Forks a seed pair with a string id (XXH3 over state, hash and the id bytes).</summary>
    internal static SeedPair ForkChar(SeedPair seed, string id) => Fork(seed, id, id.Length);

    /// <summary>Forks a seed pair with an id padded to 16 bytes.</summary>
    internal static SeedPair ForkTkId(SeedPair seed, string id) => Fork(seed, id, 16);

    /// <summary>
    /// Forks a seed pair with a string root id and a padded id in a single hash.
    /// </summary>
    internal static SeedPair ForkCharAndTkId(SeedPair seed, string rootId, string tkId)
    {
        var input = new byte[8 + rootId.Length + 16];
        BitConverter.TryWriteBytes(input.AsSpan(0), seed.State);
        BitConverter.TryWriteBytes(input.AsSpan(4), seed.Hash);
        for (int i = 0; i < rootId.Length; i++)
            input[8 + i] = (byte)rootId[i];
        int offset = 8 + rootId.Length;
        int length = Math.Min(tkId.Length, 16);
        for (int i = 0; i < length; i++)
            input[offset + i] = (byte)tkId[i];
        return PostProcess(Xxh3.Hash64(input));
    }

    private static SeedPair Fork(SeedPair seed, string id, int idLength)
    {
        var input = new byte[8 + idLength];
        BitConverter.TryWriteBytes(input.AsSpan(0), seed.State);
        BitConverter.TryWriteBytes(input.AsSpan(4), seed.Hash);
        int length = Math.Min(id.Length, idLength);
        for (int i = 0; i < length; i++)
            input[8 + i] = (byte)id[i];
        return PostProcess(Xxh3.Hash64(input));
    }

    private static SeedPair PostProcess(ulong hash)
    {
        uint low = (uint)hash;
        return new SeedPair(low > 1 ? low : 1, Rotl16(low) ^ (uint)(hash >> 32) ^ low);
    }

    private static uint Rotl16(uint value) => (value << 16) | (value >> 16);
}
