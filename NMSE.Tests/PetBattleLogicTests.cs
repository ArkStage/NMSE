using NMSE.Core;
using NMSE.Core.Utilities;
using Xunit;

namespace NMSE.Tests;

/// <summary>
/// Tests for the procgen half of <see cref="PetBattleLogic"/>: the true pet battle stat class
/// roll and stat value prediction derived from a pet's SpeciesSeed and GenusSeed, plus the
/// overall rating table, the in-game bar fractions and the XXH3-64 hash used by the game's
/// personal RNG forks.
/// </summary>
public class PetBattleLogicTests
{
    // --- XXH3-64 reference vectors ---

    [Fact]
    public void Xxh3_MatchesReferenceVectors()
    {
        Assert.Equal(0x2D06800538D394C2UL, Xxh3.Hash64([]));
        Assert.Equal(0x78AF5F94892F3950UL, Xxh3.Hash64("abc"u8));

        var combat = new byte[24];
        BitConverter.TryWriteBytes(combat.AsSpan(0), 0x12345678u);
        BitConverter.TryWriteBytes(combat.AsSpan(4), 0x9ABCDEF0u);
        "COMBAT"u8.CopyTo(combat.AsSpan(8));
        Assert.Equal(0xCA468B09F77B5D9DUL, Xxh3.Hash64(combat));

        var root = new byte[25];
        BitConverter.TryWriteBytes(root.AsSpan(0), 0x12345678u);
        BitConverter.TryWriteBytes(root.AsSpan(4), 0x9ABCDEF0u);
        "CORE_STAT_CLASSES"u8.CopyTo(root.AsSpan(8));
        Assert.Equal(0x4566938B8FCFAC89UL, Xxh3.Hash64(root));
    }

    // --- Class roll golden vectors (verified against the game UI) ---

    [Theory]
    [InlineData(0xE43BC3969A5CB49DUL, 0x5A32AC1A984B61F6UL, 0, 0, 0)] // PROTOFLYER, C/C/C
    [InlineData(0xA1A02A0D833A1AF9UL, 0x04535BE423783F5AUL, 1, 0, 3)] // CAT, Combat B, Speed C, Health S
    [InlineData(0xBCA4B36C3A0A9155UL, 0x223CDDF9BDB28E41UL, 0, 0, 2)] // RODENT, Combat C, Speed C, Health A
    [InlineData(0xC1DE7EDE41662C05UL, 0xABC713E8E3F8F45CUL, 2, 1, 0)] // STRIDER, Combat A, Speed B, Health C
    public void RollClasses_MatchesGameVectors(
        ulong speciesSeed, ulong genusSeed, int combat, int speed, int health)
    {
        var classes = PetBattleLogic.RollClasses(speciesSeed, genusSeed);

        Assert.Equal(combat, classes.Combat);
        Assert.Equal(speed, classes.Speed);
        Assert.Equal(health, classes.Health);
    }

    [Theory]
    [InlineData(0xE43BC3969A5CB49DUL, 0x5A32AC1A984B61F6UL, 0)] // PROTOFLYER, C
    [InlineData(0xA1A02A0D833A1AF9UL, 0x04535BE423783F5AUL, 2)] // CAT, A
    [InlineData(0xBCA4B36C3A0A9155UL, 0x223CDDF9BDB28E41UL, 1)] // RODENT, B
    [InlineData(0xC1DE7EDE41662C05UL, 0xABC713E8E3F8F45CUL, 1)] // STRIDER, B
    public void OverallRating_UsesClassSumTable(ulong speciesSeed, ulong genusSeed, int overall)
    {
        var classes = PetBattleLogic.RollClasses(speciesSeed, genusSeed);

        Assert.Equal(overall, classes.Overall);
        Assert.Equal("CBAS"[overall], PetBattleLogic.ClassLetter(overall));
    }

    // --- Decode checks for the packed data tables ---

    [Fact]
    public void DecodedTables_MatchTheDocumentedValues()
    {
        var c = new PetBattleLogic.Classes(0, 0, 0);
        var s = new PetBattleLogic.Classes(3, 3, 3);

        var cRanges = PetBattleLogic.GetReachableRanges(c, 0, 0, 0);
        Assert.Equal(340f, cRanges.HealthMin, 3);
        Assert.Equal(630f, cRanges.HealthMax, 3);
        Assert.Equal(16f, cRanges.SpeedMin, 3);
        Assert.Equal(60f, cRanges.SpeedMax, 3);
        Assert.Equal(100f, cRanges.CombatMin, 3);
        Assert.Equal(120f, cRanges.CombatMax, 3);

        var sRanges = PetBattleLogic.GetReachableRanges(s, 0, 0, 0);
        Assert.Equal(460f, sRanges.HealthMin, 3);
        Assert.Equal(870f, sRanges.HealthMax, 3);
        Assert.Equal(48f, sRanges.SpeedMin, 3);
        Assert.Equal(120f, sRanges.SpeedMax, 3);
        Assert.Equal(180f, sRanges.CombatMin, 3);
        Assert.Equal(200f, sRanges.CombatMax, 3);

        var boosted = PetBattleLogic.GetReachableRanges(s, 1, 1, 1);
        Assert.Equal(1195f, boosted.HealthMax, 3);
        Assert.Equal(150f, boosted.SpeedMax, 3);
        Assert.Equal(270f, boosted.CombatMax, 3);

        var (healthFloor, healthCeiling) = PetBattleLogic.GetGameBarRange(PetBattleLogic.StatKind.Health);
        Assert.Equal(425f, healthFloor, 3);
        Assert.Equal(3975f, healthCeiling, 3);

        var (speedFloor, speedCeiling) = PetBattleLogic.GetGameBarRange(PetBattleLogic.StatKind.Speed);
        Assert.Equal(16f, speedFloor, 3);
        Assert.Equal(420f, speedCeiling, 3);

        var (combatFloor, combatCeiling) = PetBattleLogic.GetGameBarRange(PetBattleLogic.StatKind.Combat);
        Assert.Equal(100f, combatFloor, 3);
        Assert.Equal(900f, combatCeiling, 3);
    }

    // --- Stat value prediction ---

    [Fact]
    public void PredictCoreStats_IsDeterministic()
    {
        var classes = new PetBattleLogic.Classes(3, 3, 3);

        var first = PetBattleLogic.PredictCoreStats(123UL, 456UL, 1.5f, classes, 5, 5, 5);
        var second = PetBattleLogic.PredictCoreStats(123UL, 456UL, 1.5f, classes, 5, 5, 5);

        Assert.Equal(first, second);
    }

    [Fact]
    public void PredictCoreStats_LevelAddsExactClassBoost()
    {
        var classes = new PetBattleLogic.Classes(2, 1, 0);

        var level0 = PetBattleLogic.PredictCoreStats(0xA1A02A0D833A1AF9UL, 0x04535BE423783F5AUL, 1f, classes, 0, 0, 0);
        var level1 = PetBattleLogic.PredictCoreStats(0xA1A02A0D833A1AF9UL, 0x04535BE423783F5AUL, 1f, classes, 1, 1, 1);

        // C health boost 175, B speed boost 22, A combat boost 63.
        Assert.Equal(175, level1.Health - level0.Health);
        Assert.Equal(22, level1.Speed - level0.Speed);
        Assert.Equal(63, level1.Combat - level0.Combat);
    }

    [Fact]
    public void PredictCoreStats_LevelClampsAtPetMaxLevel()
    {
        var classes = new PetBattleLogic.Classes(3, 3, 3);

        var level10 = PetBattleLogic.PredictCoreStats(1UL, 2UL, 1f, classes, 10, 10, 10);
        var level15 = PetBattleLogic.PredictCoreStats(1UL, 2UL, 1f, classes, 15, 15, 15);

        Assert.Equal(level10, level15);
    }

    [Theory]
    [InlineData(0, 340, 630, 16, 60, 100, 120)] // class C variance/range bounds at level 0
    [InlineData(1, 380, 750, 32, 72, 120, 140)] // class B
    [InlineData(2, 440, 780, 40, 96, 160, 180)] // class A
    [InlineData(3, 460, 870, 48, 120, 180, 200)] // class S
    public void PredictCoreStats_StaysWithinClassRangeBounds(
        int classValue, int healthMin, int healthMax, int speedMin, int speedMax, int combatMin, int combatMax)
    {
        var classes = new PetBattleLogic.Classes(classValue, classValue, classValue);

        for (ulong seed = 0; seed < 200; seed++)
        {
            var stats = PetBattleLogic.PredictCoreStats(seed, seed * 31 + 7, 2.5f, classes, 0, 0, 0);

            Assert.InRange(stats.Health, healthMin, healthMax);
            Assert.InRange(stats.Speed, speedMin, speedMax);
            Assert.InRange(stats.Combat, combatMin, combatMax);
        }
    }

    [Fact]
    public void CurveSizeFactor_UsesAgilityScaleRange()
    {
        // PetAgilityScaleRange is 0.4..4.0 (not PetSizeRange); the curve is sqrt((2 - t) * t).
        Assert.Equal(0f, PetBattleLogic.CurveSizeFactor(0.4f), 5);
        Assert.Equal(1f, PetBattleLogic.CurveSizeFactor(4.0f), 5);
        Assert.Equal(0f, PetBattleLogic.CurveSizeFactor(0.1f), 5);
        Assert.Equal(1f, PetBattleLogic.CurveSizeFactor(5.0f), 5);
        Assert.Equal(MathF.Sqrt(0.75f), PetBattleLogic.CurveSizeFactor(2.2f), 5);
    }

    [Fact]
    public void GameBarFraction_MatchesThePetScreenNormalisation()
    {
        // The pet screen bars run from the class C floor to the class S ceiling at level 10
        // (health has no variance in the bar range, speed does).
        Assert.Equal(0f, PetBattleLogic.GetGameBarFraction(PetBattleLogic.StatKind.Health, 425), 5);
        Assert.Equal(1f, PetBattleLogic.GetGameBarFraction(PetBattleLogic.StatKind.Health, 3975), 5);
        Assert.Equal(0.125f, PetBattleLogic.GetGameBarFraction(PetBattleLogic.StatKind.Health, 870), 3);
        Assert.Equal(1f, PetBattleLogic.GetGameBarFraction(PetBattleLogic.StatKind.Speed, 420), 5);
        Assert.Equal(0f, PetBattleLogic.GetGameBarFraction(PetBattleLogic.StatKind.Speed, 16), 5);
        Assert.Equal(0f, PetBattleLogic.GetGameBarFraction(PetBattleLogic.StatKind.Combat, 100), 5);
        Assert.Equal(1f, PetBattleLogic.GetGameBarFraction(PetBattleLogic.StatKind.Combat, 900), 5);
    }

    [Fact]
    public void PredictCoreStats_SizeRaisesHealthAndLowersSpeed()
    {
        var classes = new PetBattleLogic.Classes(2, 2, 2);

        var small = PetBattleLogic.PredictCoreStats(0xBCA4B36C3A0A9155UL, 0x223CDDF9BDB28E41UL, 0.4f, classes, 0, 0, 0);
        var large = PetBattleLogic.PredictCoreStats(0xBCA4B36C3A0A9155UL, 0x223CDDF9BDB28E41UL, 4.0f, classes, 0, 0, 0);

        Assert.True(large.Health > small.Health);
        Assert.True(large.Speed < small.Speed);
    }

    // --- Seed search ---

    [Fact]
    public void FindSeed_FindsAllSClasses()
    {
        var target = new PetBattleLogic.Classes(3, 3, 3);

        var found = PetBattleLogic.FindSeed(target);

        Assert.NotNull(found);
        Assert.Equal(target, PetBattleLogic.RollClasses(found!.Value.SpeciesSeed, found.Value.GenusSeed));
        Assert.Equal(PetBattleLogic.GeneratedSeedMarker, (ushort)(found.Value.SpeciesSeed >> 48));
        Assert.Equal(PetBattleLogic.GeneratedSeedMarker, (ushort)(found.Value.GenusSeed >> 48));
    }

    [Fact]
    public void FindSeed_IsDeterministicForTheSameSalt()
    {
        var target = new PetBattleLogic.Classes(2, 1, 3);

        var first = PetBattleLogic.FindSeed(target, salt: 12345);
        var second = PetBattleLogic.FindSeed(target, salt: 12345);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(target, PetBattleLogic.RollClasses(first!.Value.SpeciesSeed, first.Value.GenusSeed));
    }

    [Fact]
    public void FindSeed_ReturnsNullWhenAttemptsAreExhausted()
    {
        var target = new PetBattleLogic.Classes(3, 3, 3);

        // A single attempt cannot realistically hit the 0.34% S/S/S roll; the search must
        // report failure rather than loop forever.
        Assert.Null(PetBattleLogic.FindSeed(target, maxAttempts: 1, salt: 1));
    }
}
