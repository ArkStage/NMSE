namespace NMSE.Core.Utilities;

/// <summary>
/// SplitMix64: a small, fast, deterministic 64-bit PRNG. Used for reproducible seed searches
/// (the same salt and budget always produce the same candidates).
/// </summary>
internal static class SplitMix64
{
    /// <summary>The state increment constant (the golden gamma).</summary>
    internal const ulong Gamma = 0x9E3779B97F4A7C15UL;

    /// <summary>Advances the state and returns the next value in the sequence.</summary>
    internal static ulong Next(ref ulong state)
    {
        state = unchecked(state + Gamma);
        ulong z = state;
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }
}
