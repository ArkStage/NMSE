using System.Linq;
using NMSE.Core;
using NMSE.Models;
using Xunit;

namespace NMSE.Tests;

/// <summary>
/// Tests for the reward half of <see cref="PetBattleLogic"/>: the five Arena League reward
/// species, their granted S/S/S class overrides and the reset behaviour that preserves them.
/// </summary>
public class PetBattleRewardTests
{
    [Theory]
    [InlineData("WALKER_CRAB")]
    [InlineData("^WALKER_CRAB")]
    [InlineData("walker_crab")]
    [InlineData("FISHBOWL_PET3")]
    [InlineData("HORROR_PET")]
    [InlineData("LANDSQUID_PET")]
    [InlineData("SPIDERQUAD_PET")]
    public void IsRewardSpecies_MatchesTheFiveArenaLeagueSpecies(string id)
    {
        Assert.True(PetBattleLogic.IsRewardSpecies(id));
    }

    [Theory]
    [InlineData("PROTOFLYER")]
    [InlineData("^CAT")]
    [InlineData("")]
    [InlineData("^")]
    [InlineData(null)]
    public void IsRewardSpecies_RejectsOtherIds(string? id)
    {
        Assert.False(PetBattleLogic.IsRewardSpecies(id));
    }

    [Fact]
    public void TryGetSpecies_ReturnsTheGrantedSeeds()
    {
        Assert.True(PetBattleLogic.TryGetSpecies("^WALKER_CRAB", out var crab));
        Assert.Equal(1426UL, crab.SpeciesSeed);
        Assert.Equal(1427UL, crab.GenusSeed);

        Assert.True(PetBattleLogic.TryGetSpecies("HORROR_PET", out var horror));
        Assert.Equal(14678070221471843610UL, horror.SpeciesSeed);
        Assert.Equal(12248461076955987377UL, horror.GenusSeed);

        Assert.False(PetBattleLogic.TryGetSpecies("PROTOFLYER", out _));
    }

    [Fact]
    public void RewardClasses_AreAllS()
    {
        Assert.Equal(new PetBattleLogic.Classes(3, 3, 3), PetBattleLogic.RewardClasses);
    }

    [Fact]
    public void ApplyResetClassOverrides_KeepsRewardSpeciesAtSAndDefaultsOthersToC()
    {
        var reward = BuildPet("^WALKER_CRAB", "C");
        PetBattleLogic.ApplyResetClassOverrides(reward);
        Assert.True(reward.GetBool("PetBattlerUseCoreStatClassOverrides"));
        Assert.Equal(new[] { "S", "S", "S" }, ReadOverrideClasses(reward));

        var normal = BuildPet("^PROTOFLYER", "S");
        PetBattleLogic.ApplyResetClassOverrides(normal);
        Assert.False(normal.GetBool("PetBattlerUseCoreStatClassOverrides"));
        Assert.Equal(new[] { "C", "C", "C" }, ReadOverrideClasses(normal));
    }

    [Fact]
    public void ResetBattleRecord_ClearsTheRecordButKeepsAbilitiesAndRewardClasses()
    {
        var pet = BuildPet("^HORROR_PET", "C");
        pet.Set("PetBattlerTreatsEaten", BuildIntArray(3, 2, 1));
        pet.Set("PetBattlerTreatsAvailable", 7);
        pet.Set("PetBattleProgressToTreat", 0.5);
        pet.Set("PetBattlerVictories", 9);
        pet.Set("PetBattlerMoves", BuildStringArray("^MOVE_A", "^MOVE_B", "^", "^", "^"));

        PetBattleLogic.ResetBattleRecord(pet);

        Assert.Equal(new[] { 0, 0, 0 }, ReadIntArray(pet, "PetBattlerTreatsEaten"));
        Assert.Equal(0, pet.GetInt("PetBattlerTreatsAvailable"));
        Assert.Equal(0.0, pet.GetDouble("PetBattleProgressToTreat"));
        Assert.Equal(0, pet.GetInt("PetBattlerVictories"));
        Assert.True(pet.GetBool("PetBattlerUseCoreStatClassOverrides"));
        Assert.Equal(new[] { "S", "S", "S" }, ReadOverrideClasses(pet));

        // Abilities are kept on a record reset.
        var moves = pet.GetArray("PetBattlerMoves");
        Assert.NotNull(moves);
        Assert.Equal("^MOVE_A", moves!.GetString(0));
        Assert.Equal("^MOVE_B", moves.GetString(1));
    }

    [Fact]
    public void ResetBattleData_ClearsAbilitiesToo()
    {
        var pet = BuildPet("^PROTOFLYER", "S");
        pet.Set("PetBattlerMoves", BuildStringArray("^MOVE_A", "^MOVE_B", "^", "^", "^"));

        PetBattleLogic.ResetBattleData(pet);

        Assert.False(pet.GetBool("PetBattlerUseCoreStatClassOverrides"));
        Assert.Equal(new[] { "C", "C", "C" }, ReadOverrideClasses(pet));

        var moves = pet.GetArray("PetBattlerMoves");
        Assert.NotNull(moves);
        Assert.Equal(new[] { "^", "^", "^", "^", "^" },
            Enumerable.Range(0, 5).Select(i => moves!.GetString(i)).ToArray());
    }

    private static JsonObject BuildPet(string creatureId, string classLetter)
    {
        var pet = new JsonObject();
        pet.Set("CreatureID", creatureId);
        pet.Set("PetBattlerUseCoreStatClassOverrides", false);
        pet.Set("PetBattlerCoreStatClassOverrides", BuildOverrideArray(classLetter));
        return pet;
    }

    private static JsonArray BuildOverrideArray(string classLetter)
    {
        var array = new JsonArray();
        for (int i = 0; i < 3; i++)
        {
            var obj = new JsonObject();
            obj.Set("InventoryClass", classLetter);
            array.Add(obj);
        }
        return array;
    }

    private static JsonArray BuildIntArray(params int[] values)
    {
        var array = new JsonArray();
        foreach (int value in values) array.Add(value);
        return array;
    }

    private static JsonArray BuildStringArray(params string[] values)
    {
        var array = new JsonArray();
        foreach (string value in values) array.Add(value);
        return array;
    }

    private static string[] ReadOverrideClasses(JsonObject pet)
    {
        var overrides = pet.GetArray("PetBattlerCoreStatClassOverrides");
        Assert.NotNull(overrides);
        return Enumerable.Range(0, 3)
            .Select(i => overrides!.GetObject(i)?.GetString("InventoryClass") ?? "")
            .ToArray();
    }

    private static int[] ReadIntArray(JsonObject pet, string key)
    {
        var array = pet.GetArray(key);
        Assert.NotNull(array);
        return Enumerable.Range(0, array!.Length).Select(array.GetInt).ToArray();
    }
}
