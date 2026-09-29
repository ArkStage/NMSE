using NMSE.Core.Utilities;
using Xunit;

namespace NMSE.Tests;

/// <summary>
/// Tests for the shared PRNG utilities: SplitMix64 reference vectors and the seed-pair
/// primitives (pair construction, PRNG step and roll fraction).
/// </summary>
public class UtilityPrngTests
{
    [Fact]
    public void SplitMix64_MatchesReferenceVectors()
    {
        ulong state = 0;
        Assert.Equal(0xE220A8397B1DCDAFUL, SplitMix64.Next(ref state));
        Assert.Equal(0x6E789E6AA1B965F4UL, SplitMix64.Next(ref state));
        Assert.Equal(0x06C45D188009454FUL, SplitMix64.Next(ref state));
    }

    [Fact]
    public void PRNG_FromSeed_BuildsTheExpectedPair()
    {
        Assert.Equal(new PRNG.SeedPair(1, 0), PRNG.FromSeed(0));
        Assert.Equal(new PRNG.SeedPair(5, 0x00050005u), PRNG.FromSeed(5));

        // Low word 1 keeps the state at 1; the high word is mixed into the hash.
        Assert.Equal(new PRNG.SeedPair(1, 0x00010003u), PRNG.FromSeed(0x0000000200000001UL));
    }

    [Fact]
    public void PRNG_NextValue_MultipliesTheLowWord()
    {
        var pair = new PRNG.SeedPair(3, 7);
        Assert.Equal(unchecked(3u * PRNG.StepMultiplier + 7u), PRNG.NextValue(pair));
    }

    [Fact]
    public void PRNG_Fraction_MapsTheWordRange()
    {
        Assert.Equal(0f, PRNG.Fraction(0u));
        Assert.Equal(0.5f, PRNG.Fraction(0x80000000u));
        Assert.Equal(1f, PRNG.Fraction(0xFFFFFFFFu));
    }
}
