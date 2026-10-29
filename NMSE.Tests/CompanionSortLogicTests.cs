using NMSE.Core;
using NMSE.Models;
using Xunit;

namespace NMSE.Tests;

/// <summary>
/// Tests for <see cref="CompanionSortLogic"/>: packing sorts, block moves, accessory and
/// battle-team remapping, and locked-slot safety.
/// </summary>
public class CompanionSortLogicTests
{
    [Fact]
    public void SortPets_Ascending_PacksIntoUnlockedSlots_AndRemapsParallelData()
    {
        var pets = new JsonArray();
        pets.Add(Pet("Charlie", "p0"));
        pets.Add(Pet(null, "empty1", occupied: false));
        pets.Add(Pet("alpha", "p2"));
        pets.Add(Pet("Bravo", "p3"));
        pets.Add(Pet("delta", "p4"));
        pets.Add(Pet(null, "empty5", occupied: false));

        var pac = new JsonArray();
        for (int i = 0; i < 6; i++) pac.Add(Pac(i));

        var ps = PlayerState(
            pets,
            new[] { true, true, true, true, true, false },
            pac,
            new[] { 0, 2, 4 });

        Assert.True(CompanionSortLogic.SortPets(ps, descending: false));

        Assert.Equal("alpha", Name(pets, 0));
        Assert.Equal("Bravo", Name(pets, 1));
        Assert.Equal("Charlie", Name(pets, 2));
        Assert.Equal("delta", Name(pets, 3));
        Assert.Equal("empty1", Marker(pets, 4));
        Assert.Equal("empty5", Marker(pets, 5));

        Assert.Equal(new[] { 2, 3, 0, 4, 1, 5 }, PacTags(ps));
        Assert.Equal(new[] { 2, 0, 3 }, Team(ps));
        Assert.Equal(6, pets.Length);
        Assert.Equal(6, ps.GetArray("PetAccessoryCustomisation")!.Length);
        Assert.True(ps.GetArray("UnlockedPetSlots")!.GetBool(0));
        Assert.True(ps.GetArray("UnlockedPetSlots")!.GetBool(4));
        Assert.False(ps.GetArray("UnlockedPetSlots")!.GetBool(5));
    }

    [Fact]
    public void SortPets_Descending_PutsUnnamedLast()
    {
        var pets = new JsonArray();
        pets.Add(Pet("beta", "p0"));
        pets.Add(Pet(null, "u1"));
        pets.Add(Pet("Alpha", "p2"));
        pets.Add(Pet("^", "u3"));

        var ps = PlayerState(pets, new[] { true, true, true, true });

        Assert.True(CompanionSortLogic.SortPets(ps, descending: true));

        Assert.Equal("beta", Name(pets, 0));
        Assert.Equal("Alpha", Name(pets, 1));
        Assert.Equal("u1", Marker(pets, 2));
        Assert.Equal("u3", Marker(pets, 3));
    }

    [Fact]
    public void SortPets_LeavesLockedAndOccupiedLockedSlotsPinned()
    {
        var pets = new JsonArray();
        pets.Add(Pet("Zulu", "p0"));
        pets.Add(Pet("alpha", "p1"));
        pets.Add(Pet("lockedPet", "locked"));
        pets.Add(Pet(null, "empty3", occupied: false));
        pets.Add(Pet(null, "empty4", occupied: false));

        var pac = new JsonArray();
        for (int i = 0; i < 5; i++) pac.Add(Pac(i));

        var ps = PlayerState(
            pets,
            new[] { true, true, false, false, false },
            pac,
            new[] { 2 });

        Assert.True(CompanionSortLogic.SortPets(ps, descending: false));

        Assert.Equal("alpha", Name(pets, 0));
        Assert.Equal("Zulu", Name(pets, 1));
        Assert.Equal("lockedPet", Name(pets, 2));
        Assert.Equal("empty3", Marker(pets, 3));
        Assert.Equal("empty4", Marker(pets, 4));
        Assert.Equal(2, Team(ps)[0]);
        Assert.False(ps.GetArray("UnlockedPetSlots")!.GetBool(2));
    }

    [Fact]
    public void SortPets_PinsPetsOutsideAccessoryArray()
    {
        var pets = new JsonArray();
        pets.Add(Pet("Zulu", "p0"));
        pets.Add(Pet("alpha", "p1"));
        pets.Add(Pet("Charlie", "p2"));
        pets.Add(Pet(null, "empty3", occupied: false));

        var pac = new JsonArray();
        pac.Add(Pac(0));
        pac.Add(Pac(1));

        var ps = PlayerState(
            pets,
            new[] { true, true, true, true },
            pac);

        Assert.True(CompanionSortLogic.SortPets(ps, descending: false));

        Assert.Equal("alpha", Name(pets, 0));
        Assert.Equal("Zulu", Name(pets, 1));
        Assert.Equal("Charlie", Name(pets, 2));
        Assert.Equal(2, ps.GetArray("PetAccessoryCustomisation")!.Length);
        Assert.Equal(new[] { 1, 0 }, PacTags(ps));
    }

    [Fact]
    public void SortPets_WithoutAccessoryOrBattleTeam_StillWorks()
    {
        var pets = new JsonArray();
        pets.Add(Pet("Zulu", "p0"));
        pets.Add(Pet("alpha", "p1"));

        var ps = PlayerState(pets, new[] { true, true });

        Assert.True(CompanionSortLogic.SortPets(ps, descending: false));
        Assert.Equal("alpha", Name(pets, 0));
        Assert.Equal("Zulu", Name(pets, 1));
        Assert.Null(ps.GetArray("PetAccessoryCustomisation"));
        Assert.Null(ps.GetObject("PetBattleTeam"));
    }

    [Fact]
    public void SortEggs_Ascending_PacksIntoFirstEggSlots()
    {
        var eggs = new JsonArray();
        eggs.Add(Pet(null, "e0", occupied: false));
        eggs.Add(Pet("Zeta", "e1"));
        eggs.Add(Pet(null, "e2", occupied: false));
        eggs.Add(Pet("Alpha", "e3"));
        eggs.Add(Pet(null, "e4", occupied: false));

        Assert.True(CompanionSortLogic.SortEggs(eggs, descending: false));

        Assert.Equal("Alpha", Name(eggs, 0));
        Assert.Equal("Zeta", Name(eggs, 1));
        Assert.Equal("e2", Marker(eggs, 2));
        Assert.Equal("e0", Marker(eggs, 3));
        Assert.Equal("e4", Marker(eggs, 4));
        Assert.Equal(5, eggs.Length);
    }

    [Fact]
    public void SortEggs_Descending_PutsUnnamedLast()
    {
        var eggs = new JsonArray();
        eggs.Add(Pet("beta", "e0"));
        eggs.Add(Pet("Alpha", "e1"));
        eggs.Add(Pet(null, "e2", occupied: false));
        eggs.Add(Pet("gamma", "e3"));

        Assert.True(CompanionSortLogic.SortEggs(eggs, descending: true));

        Assert.Equal("gamma", Name(eggs, 0));
        Assert.Equal("beta", Name(eggs, 1));
        Assert.Equal("Alpha", Name(eggs, 2));
        Assert.Equal("e2", Marker(eggs, 3));
    }

    [Fact]
    public void MovePets_ContiguousBlockMovesAndRemapsParallelData()
    {
        var pets = new JsonArray();
        pets.Add(Pet("A", "p0"));
        pets.Add(Pet("B", "p1"));
        pets.Add(Pet("C", "p2"));
        pets.Add(Pet("D", "p3"));

        var pac = new JsonArray();
        for (int i = 0; i < 4; i++) pac.Add(Pac(i));

        var ps = PlayerState(
            pets,
            new[] { true, true, true, true },
            pac,
            new[] { 0, 1, 2 });

        Assert.True(CompanionSortLogic.MovePets(ps, new[] { 1, 2 }, -1));

        Assert.Equal(new[] { "B", "C", "A", "D" }, Names(pets));
        Assert.Equal(new[] { 1, 2, 0, 3 }, PacTags(ps));
        Assert.Equal(new[] { 2, 0, 1 }, Team(ps));

        var downPets = new JsonArray();
        downPets.Add(Pet("A", "p0"));
        downPets.Add(Pet("B", "p1"));
        downPets.Add(Pet("C", "p2"));
        downPets.Add(Pet("D", "p3"));

        var downPac = new JsonArray();
        for (int i = 0; i < 4; i++) downPac.Add(Pac(i));

        var down = PlayerState(
            downPets,
            new[] { true, true, true, true },
            downPac,
            new[] { 0, 1, 2 });

        Assert.True(CompanionSortLogic.MovePets(down, new[] { 1, 2 }, 1));

        Assert.Equal(new[] { "A", "D", "B", "C" }, Names(downPets));
        Assert.Equal(new[] { 0, 3, 1, 2 }, PacTags(down));
        Assert.Equal(new[] { 0, 2, 3 }, Team(down));
    }

    [Fact]
    public void MovePets_RejectsNonContiguousBoundaryAndEmptySelections()
    {
        var pets = new JsonArray();
        pets.Add(Pet("A", "p0"));
        pets.Add(Pet("B", "p1"));
        pets.Add(Pet("C", "p2"));

        var ps = PlayerState(pets, new[] { true, true, true });

        Assert.False(CompanionSortLogic.MovePets(ps, new[] { 0 }, -1));
        Assert.False(CompanionSortLogic.MovePets(ps, new[] { 2 }, 1));
        Assert.False(CompanionSortLogic.MovePets(ps, new[] { 0, 2 }, -1));
        Assert.False(CompanionSortLogic.MovePets(ps, Array.Empty<int>(), 1));
        Assert.False(CompanionSortLogic.CanMovePets(ps, new[] { 0, 2 }, -1));
        Assert.True(CompanionSortLogic.CanMovePets(ps, new[] { 1, 2 }, -1));
        Assert.Equal(new[] { "A", "B", "C" }, Names(pets));
    }

    [Fact]
    public void MoveEggs_ContiguousBlockMoves()
    {
        var eggs = new JsonArray();
        eggs.Add(Pet("A", "e0"));
        eggs.Add(Pet("B", "e1"));
        eggs.Add(Pet("C", "e2"));
        eggs.Add(Pet("D", "e3"));

        Assert.True(CompanionSortLogic.MoveEggs(eggs, new[] { 1, 2 }, -1));
        Assert.Equal(new[] { "B", "C", "A", "D" }, Names(eggs));

        Assert.False(CompanionSortLogic.MoveEggs(eggs, new[] { 0 }, -1));
        Assert.False(CompanionSortLogic.CanMoveEggs(eggs, new[] { 0, 2 }, 1));
    }

    [Fact]
    public void ResolveSortName_TrimsAndTreatsCaretAsUnnamed()
    {
        Assert.Equal("Alpha", CompanionSortLogic.ResolveSortName(Pet("  Alpha  ", "x")));
        Assert.Equal("", CompanionSortLogic.ResolveSortName(Pet("^", "x")));
        Assert.Equal("", CompanionSortLogic.ResolveSortName(Pet("   ", "x")));
        Assert.Equal("", CompanionSortLogic.ResolveSortName(new JsonObject()));
    }

    [Fact]
    public void SortPets_IsIdempotent()
    {
        var pets = new JsonArray();
        pets.Add(Pet("Zulu", "p0"));
        pets.Add(Pet("alpha", "p1"));
        pets.Add(Pet("Bravo", "p2"));

        var pac = new JsonArray();
        for (int i = 0; i < 3; i++) pac.Add(Pac(i));

        var ps = PlayerState(
            pets,
            new[] { true, true, true },
            pac,
            new[] { 0, 2 });

        Assert.True(CompanionSortLogic.SortPets(ps, descending: false));
        var firstNames = Names(pets);
        var firstPac = PacTags(ps);
        var firstTeam = Team(ps);

        Assert.True(CompanionSortLogic.SortPets(ps, descending: false));

        Assert.Equal(firstNames, Names(pets));
        Assert.Equal(firstPac, PacTags(ps));
        Assert.Equal(firstTeam, Team(ps));
    }

    [Fact]
    public void SortPets_RequiresUnlockedArrayAndMultiplePets()
    {
        var one = new JsonArray();
        one.Add(Pet("Solo", "p0"));
        var psOne = PlayerState(one, new[] { true });
        Assert.False(CompanionSortLogic.SortPets(psOne, descending: false));

        var two = new JsonArray();
        two.Add(Pet("B", "p0"));
        two.Add(Pet("A", "p1"));
        var psNoUnlocked = new JsonObject();
        psNoUnlocked.Set("Pets", two);
        Assert.False(CompanionSortLogic.SortPets(psNoUnlocked, descending: false));
    }

    private static JsonObject Pet(string? name, string marker, bool occupied = true)
    {
        var pet = new JsonObject();
        var seed = new JsonArray();
        seed.Add(occupied);
        seed.Add(occupied ? "0x1" : "0x0");
        pet.Set("CreatureSeed", seed);
        pet.Set("CustomName", name ?? "^");
        pet.Set("Marker", marker);
        return pet;
    }

    private static JsonObject Pac(int tag)
    {
        var pac = new JsonObject();
        pac.Set("Data", new JsonArray());
        pac.Set("Tag", tag);
        return pac;
    }

    private static JsonObject PlayerState(
        JsonArray pets,
        bool[] unlocked,
        JsonArray? pac = null,
        int[]? team = null)
    {
        var ps = new JsonObject();
        ps.Set("Pets", pets);

        var unlockedArray = new JsonArray();
        foreach (bool value in unlocked) unlockedArray.Add(value);
        ps.Set("UnlockedPetSlots", unlockedArray);

        if (pac != null) ps.Set("PetAccessoryCustomisation", pac);

        if (team != null)
        {
            var battleTeam = new JsonObject();
            var members = new JsonArray();
            foreach (int index in team)
            {
                var member = new JsonObject();
                member.Set("PetIndex", index);
                members.Add(member);
            }
            battleTeam.Set("TeamMembers", members);
            ps.Set("PetBattleTeam", battleTeam);
        }

        return ps;
    }

    private static string Name(JsonArray array, int index) =>
        array.GetObject(index)?.GetString("CustomName") ?? "";

    private static string Marker(JsonArray array, int index) =>
        array.GetObject(index)?.GetString("Marker") ?? "";

    private static string[] Names(JsonArray array)
    {
        var names = new string[array.Length];
        for (int i = 0; i < array.Length; i++)
            names[i] = Name(array, i);
        return names;
    }

    private static int[] PacTags(JsonObject playerState)
    {
        var pac = playerState.GetArray("PetAccessoryCustomisation")!;
        var tags = new int[pac.Length];
        for (int i = 0; i < pac.Length; i++)
            tags[i] = pac.GetObject(i)?.GetInt("Tag") ?? -1;
        return tags;
    }

    private static int[] Team(JsonObject playerState)
    {
        var members = playerState.GetObject("PetBattleTeam")?.GetArray("TeamMembers");
        if (members == null) return Array.Empty<int>();
        var indices = new int[members.Length];
        for (int i = 0; i < members.Length; i++)
            indices[i] = members.GetObject(i)?.GetInt("PetIndex") ?? -1;
        return indices;
    }
}
