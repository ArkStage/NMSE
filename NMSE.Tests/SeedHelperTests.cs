using NMSE.Core.Utilities;
using Xunit;

namespace NMSE.Tests;

/// <summary>Tests for <see cref="SeedHelper"/> seed formatting and normalisation round trips.</summary>
public class SeedHelperTests
{
    [Theory]
    [InlineData(0UL, "0x0000000000000000")]
    [InlineData(1UL, "0x0000000000000001")]
    [InlineData(0xA1A02A0D833A1AF9UL, "0xA1A02A0D833A1AF9")]
    [InlineData(ulong.MaxValue, "0xFFFFFFFFFFFFFFFF")]
    public void FormatSeed_UsesSaveFormat(ulong seed, string expected)
    {
        Assert.Equal(expected, SeedHelper.FormatSeed(seed));
    }

    [Fact]
    public void FormatSeed_RoundTripsThroughNormalizeSeed()
    {
        ulong seed = 0xBCA4B36C3A0A9155UL;

        Assert.Equal("0xBCA4B36C3A0A9155", SeedHelper.NormalizeSeed(SeedHelper.FormatSeed(seed)));
    }
}
