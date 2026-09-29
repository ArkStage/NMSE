using System.Threading;
using NMSE.Core;
using Xunit;

namespace NMSE.Tests;

/// <summary>
/// Tests for the solver half of <see cref="PetBattleLogic"/>: exact stat targets, maximise
/// mode, class matching, scale handling, weighted priority and determinism/cancellation.
/// </summary>
public class PetBattleSeedSolverTests
{
    [Fact]
    public void Solve_ExactTargets_FindsCandidatesThatHitEveryTarget()
    {
        var classes = new PetBattleLogic.Classes(0, 0, 0);
        var reference = PetBattleLogic.PredictCoreStats(
            0xA1A02A0D833A1AF9UL, 0x04535BE423783F5AUL, 1f, classes, 0, 0, 0);

        var request = new PetBattleLogic.SolveRequest(
            classes, reference.Health, reference.Speed, reference.Combat,
            CurrentScale: 1.0, AdjustScale: true, HealthLevel: 0, SpeedLevel: 0, CombatLevel: 0,
            MaxResults: 5, MaxCandidates: 1_000_000, Salt: 123);

        var results = PetBattleLogic.Solve(request, CancellationToken.None);

        Assert.NotEmpty(results);
        var best = results[0];
        Assert.Equal(reference, best.Stats);
        Assert.Equal(1.0, best.Score, 6);
        Assert.Equal(classes, PetBattleLogic.RollClasses(best.SpeciesSeed, best.GenusSeed));

        // Applying the candidate's seeds and scale must reproduce the predicted stats.
        var applied = PetBattleLogic.PredictCoreStats(
            best.SpeciesSeed, best.GenusSeed, best.Scale, classes, 0, 0, 0);
        Assert.Equal(reference, applied);
    }

    [Fact]
    public void Solve_Maximise_FindsHighRollsAndMatchesClasses()
    {
        var classes = new PetBattleLogic.Classes(0, 0, 0);
        var ranges = PetBattleLogic.GetReachableRanges(classes, 0, 0, 0);
        double combatSpan = ranges.CombatMax - ranges.CombatMin;

        var request = new PetBattleLogic.SolveRequest(
            classes, null, null, null,
            CurrentScale: 1.0, AdjustScale: true, HealthLevel: 0, SpeedLevel: 0, CombatLevel: 0,
            MaxResults: 5, MaxCandidates: 200_000, Salt: 7);

        var results = PetBattleLogic.Solve(request, CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(classes, PetBattleLogic.RollClasses(r.SpeciesSeed, r.GenusSeed)));

        // Roll dominance: with a few thousand matches the best candidate should be near the
        // top of the combat and speed spans (the shared scale balances health against speed).
        var best = results[0];
        Assert.True(best.Stats.Combat >= ranges.CombatMin + 0.94 * combatSpan);
        Assert.True(best.Stats.Speed >= ranges.SpeedMin + 0.9 * (ranges.SpeedMax - ranges.SpeedMin));
        Assert.True(best.Stats.Health >= ranges.HealthMin + 0.5 * (ranges.HealthMax - ranges.HealthMin));
        Assert.True(best.Score > 0.8);

        // Results are ranked by score.
        for (int i = 1; i < results.Count; i++)
            Assert.True(results[i - 1].Score >= results[i].Score);
    }

    [Fact]
    public void Solve_WithScaleAdjustment_KeepsScaleInPetSizeRange()
    {
        var classes = new PetBattleLogic.Classes(3, 3, 2);
        var request = new PetBattleLogic.SolveRequest(
            classes, null, null, null,
            CurrentScale: 1.0, AdjustScale: true, HealthLevel: 0, SpeedLevel: 0, CombatLevel: 0,
            MaxResults: 10, MaxCandidates: 200_000, Salt: 99);

        var results = PetBattleLogic.Solve(request, CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.InRange(r.Scale, 0.4f, 4.0f));
    }

    [Fact]
    public void Solve_WithoutScaleAdjustment_KeepsTheCurrentScale()
    {
        var classes = new PetBattleLogic.Classes(0, 0, 0);
        var request = new PetBattleLogic.SolveRequest(
            classes, null, null, null,
            CurrentScale: 2.0, AdjustScale: false, HealthLevel: 0, SpeedLevel: 0, CombatLevel: 0,
            MaxResults: 5, MaxCandidates: 50_000, Salt: 5);

        var results = PetBattleLogic.Solve(request, CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(2.0f, r.Scale));
    }

    [Fact]
    public void GetReference_Maximise_StaysWithinRangesAndBelowOneHundredPercent()
    {
        var classes = new PetBattleLogic.Classes(3, 3, 3);
        var request = new PetBattleLogic.SolveRequest(
            classes, null, null, null,
            CurrentScale: 1.0, AdjustScale: true, HealthLevel: 0, SpeedLevel: 0, CombatLevel: 0,
            Salt: 0);
        var ranges = PetBattleLogic.GetReachableRanges(classes, 0, 0, 0);

        var reference = PetBattleLogic.GetReference(request);

        Assert.InRange(reference.Health, ranges.HealthMin, ranges.HealthMax);
        Assert.InRange(reference.Speed, ranges.SpeedMin, ranges.SpeedMax);
        Assert.InRange(reference.Combat, ranges.CombatMin, ranges.CombatMax);
        Assert.InRange(reference.IdealScore, 0.05, 1.0);

        // Maximising all three cannot reach 100% because Health and Speed pull the shared
        // scale in opposite directions.
        Assert.True(reference.IdealScore < 1.0);
    }

    [Fact]
    public void GetReference_Priority_ShiftsTheBestAchievableValues()
    {
        var classes = new PetBattleLogic.Classes(3, 3, 3);
        var balanced = new PetBattleLogic.SolveRequest(
            classes, null, null, null,
            CurrentScale: 1.0, AdjustScale: true, HealthLevel: 0, SpeedLevel: 0, CombatLevel: 0,
            Salt: 0);
        var healthPriority = balanced with { HealthWeight = 4f };
        var speedPriority = balanced with { SpeedWeight = 4f };

        var balancedRef = PetBattleLogic.GetReference(balanced);
        var healthRef = PetBattleLogic.GetReference(healthPriority);
        var speedRef = PetBattleLogic.GetReference(speedPriority);

        // Prioritising Health moves the best shared scale towards maximum size: more Health,
        // less Speed. Speed priority cannot beat the balanced speed (already at the boundary).
        Assert.True(healthRef.Health > balancedRef.Health);
        Assert.True(healthRef.Speed < balancedRef.Speed);
        Assert.True(speedRef.Speed >= balancedRef.Speed);
        Assert.True(speedRef.Health <= balancedRef.Health);
        Assert.True(healthRef.IdealScore > balancedRef.IdealScore);
    }

    [Fact]
    public void Solve_Priority_ChangesWhatTheTopCandidatesOptimise()
    {
        var classes = new PetBattleLogic.Classes(0, 0, 0);
        var baseRequest = new PetBattleLogic.SolveRequest(
            classes, null, null, null,
            CurrentScale: 1.0, AdjustScale: true, HealthLevel: 0, SpeedLevel: 0, CombatLevel: 0,
            MaxResults: 3, MaxCandidates: 200_000, Salt: 8);

        var healthResults = PetBattleLogic.Solve(baseRequest with { HealthWeight = 4f }, CancellationToken.None);
        var combatResults = PetBattleLogic.Solve(baseRequest with { CombatWeight = 4f }, CancellationToken.None);

        Assert.NotEmpty(healthResults);
        Assert.NotEmpty(combatResults);

        // The Health-priority search maxes the health bar (large scale) while the
        // Combat-priority search keeps the balanced scale and spends its seeds on Combat.
        Assert.True(healthResults[0].Stats.Health > combatResults[0].Stats.Health);
    }

    [Fact]
    public void Solve_GeneratedSeeds_CarryTheCommunityMarker()
    {
        var classes = new PetBattleLogic.Classes(0, 0, 0);
        var request = new PetBattleLogic.SolveRequest(
            classes, null, null, null,
            CurrentScale: 1.0, AdjustScale: true, HealthLevel: 0, SpeedLevel: 0, CombatLevel: 0,
            MaxResults: 5, MaxCandidates: 50_000, Salt: 3);

        var results = PetBattleLogic.Solve(request, CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.All(results, r =>
        {
            Assert.Equal(PetBattleLogic.GeneratedSeedMarker, (ushort)(r.SpeciesSeed >> 48));
            Assert.Equal(PetBattleLogic.GeneratedSeedMarker, (ushort)(r.GenusSeed >> 48));
        });
    }

    [Fact]
    public void Solve_DeeperBudget_KeepsTheBestScoreAtLeastAsHigh()
    {
        var classes = new PetBattleLogic.Classes(0, 0, 0);
        var shallow = new PetBattleLogic.SolveRequest(
            classes, null, null, null,
            CurrentScale: 1.0, AdjustScale: true, HealthLevel: 0, SpeedLevel: 0, CombatLevel: 0,
            MaxResults: 5, MaxCandidates: 50_000, Salt: 0);
        var deep = shallow with { MaxCandidates = 200_000 };

        var shallowResults = PetBattleLogic.Solve(shallow, CancellationToken.None);
        var deepResults = PetBattleLogic.Solve(deep, CancellationToken.None);

        Assert.NotEmpty(shallowResults);
        Assert.NotEmpty(deepResults);

        // The deeper sweep continues the same deterministic sequence, so its best score can
        // only improve or stay the same.
        Assert.True(deepResults[0].Score >= shallowResults[0].Score);
    }

    [Fact]
    public void Solve_IsDeterministicForTheSameSaltAndBudget()
    {
        var classes = new PetBattleLogic.Classes(0, 0, 0);
        var request = new PetBattleLogic.SolveRequest(
            classes, null, 25f, null,
            CurrentScale: 2.0, AdjustScale: false, HealthLevel: 0, SpeedLevel: 0, CombatLevel: 0,
            MaxResults: 3, MaxCandidates: 100_000, Salt: 4242);

        var first = PetBattleLogic.Solve(request, CancellationToken.None);
        var second = PetBattleLogic.Solve(request, CancellationToken.None);

        Assert.NotEmpty(first);
        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
            Assert.Equal(first[i], second[i]);
    }

    [Fact]
    public void Solve_HonoursAnAlreadyCancelledToken()
    {
        var classes = new PetBattleLogic.Classes(3, 3, 3);
        var request = new PetBattleLogic.SolveRequest(
            classes, null, null, null,
            CurrentScale: 1.0, AdjustScale: true, HealthLevel: 0, SpeedLevel: 0, CombatLevel: 0,
            MaxCandidates: 10_000_000, Salt: 1);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var results = PetBattleLogic.Solve(request, cts.Token);

        Assert.Empty(results);
    }
}
