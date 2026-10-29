using NMSE.Models;

namespace NMSE.Core;

/// <summary>
/// Sorts and moves companion (pet) and egg slots in <c>PlayerStateData</c>.
/// Pets are packed into the first unlocked pet slots and eggs into the first egg slots.
/// Pet sorting also moves the parallel <c>PetAccessoryCustomisation</c> entries and remaps
/// <c>PetBattleTeam</c> pet indices. <c>UnlockedPetSlots</c> is never modified.
/// </summary>
internal static class CompanionSortLogic
{
    /// <summary>
    /// Returns the display name used for sorting a companion or egg:
    /// the trimmed <c>CustomName</c>, or an empty string when unnamed.
    /// </summary>
    /// <param name="companion">The companion or egg object.</param>
    internal static string ResolveSortName(JsonObject companion)
    {
        if (companion == null) return "";
        string? name = companion.GetString("CustomName");
        if (string.IsNullOrWhiteSpace(name)) return "";
        name = name.Trim();
        return name == "^" ? "" : name;
    }

    /// <summary>
    /// Sorts the movable pet slots by name and packs them into the first unlocked pet slots.
    /// Empty unlocked slots may be used as targets; locked slots are never touched.
    /// </summary>
    /// <param name="playerState">The <c>PlayerStateData</c> object.</param>
    /// <param name="descending">Whether to sort Z-A.</param>
    /// <returns><c>true</c> when at least two pets were reordered.</returns>
    internal static bool SortPets(JsonObject playerState, bool descending)
    {
        if (playerState == null) return false;
        var pets = playerState.GetArray("Pets");
        if (pets == null || pets.Length < 2) return false;

        var pac = playerState.GetArray("PetAccessoryCustomisation");
        var movable = GetMovablePetSlots(playerState, pets, pac);
        if (movable.Count < 2) return false;

        var ordered = OrderSlots(pets, movable, descending);
        var targets = GetPetTargetSlots(playerState, pets, pac, movable.Count);
        if (targets.Count != movable.Count) return false;
        if (!AllPetSlotsUnlocked(playerState, targets)) return false;

        var map = BuildPackingMap(ordered, targets);
        if (map == null) return false;
        if (!ApplyPermutation(pets, pac, map)) return false;

        var petMap = BuildPetMap(ordered, targets);
        RemapBattleTeam(playerState, petMap);
        return true;
    }

    /// <summary>
    /// Sorts the occupied egg slots by name and packs them into the first egg slots.
    /// </summary>
    /// <param name="eggs">The <c>PlayerStateData.Eggs</c> array.</param>
    /// <param name="descending">Whether to sort Z-A.</param>
    /// <returns><c>true</c> when at least two eggs were reordered.</returns>
    internal static bool SortEggs(JsonArray eggs, bool descending)
    {
        if (eggs == null || eggs.Length < 2) return false;

        var movable = GetEggSlots(eggs);
        if (movable.Count < 2) return false;

        var ordered = OrderSlots(eggs, movable, descending);
        var targets = new List<int>(movable.Count);
        for (int i = 0; i < movable.Count; i++)
            targets.Add(i);

        var map = BuildPackingMap(ordered, targets);
        if (map == null) return false;
        return ApplyPermutation(eggs, null, map);
    }

    /// <summary>
    /// Moves a contiguous block of selected pet slots one movable position up or down.
    /// Moves never leave the unlocked pet slots.
    /// </summary>
    /// <param name="playerState">The <c>PlayerStateData</c> object.</param>
    /// <param name="slotIndices">The selected pet slot indices.</param>
    /// <param name="direction">-1 to move up, +1 to move down.</param>
    /// <returns><c>true</c> when the block moved.</returns>
    internal static bool MovePets(JsonObject playerState, IReadOnlyList<int> slotIndices, int direction)
    {
        if (playerState == null) return false;
        var pets = playerState.GetArray("Pets");
        if (pets == null) return false;

        var pac = playerState.GetArray("PetAccessoryCustomisation");
        var movable = GetMovablePetSlots(playerState, pets, pac);
        var map = BuildMoveMap(movable, slotIndices, direction);
        if (map == null) return false;
        if (!ApplyPermutation(pets, pac, map)) return false;

        RemapBattleTeam(playerState, map);
        return true;
    }

    /// <summary>
    /// Moves a contiguous block of selected egg slots one position up or down.
    /// </summary>
    /// <param name="eggs">The <c>PlayerStateData.Eggs</c> array.</param>
    /// <param name="slotIndices">The selected egg slot indices.</param>
    /// <param name="direction">-1 to move up, +1 to move down.</param>
    /// <returns><c>true</c> when the block moved.</returns>
    internal static bool MoveEggs(JsonArray eggs, IReadOnlyList<int> slotIndices, int direction)
    {
        if (eggs == null) return false;
        var movable = GetEggSlots(eggs);
        var map = BuildMoveMap(movable, slotIndices, direction);
        if (map == null) return false;
        return ApplyPermutation(eggs, null, map);
    }

    /// <summary>
    /// Returns whether the selected pet slots can be moved in the given direction.
    /// </summary>
    /// <param name="playerState">The <c>PlayerStateData</c> object.</param>
    /// <param name="slotIndices">The selected pet slot indices.</param>
    /// <param name="direction">-1 to move up, +1 to move down.</param>
    internal static bool CanMovePets(JsonObject playerState, IReadOnlyList<int> slotIndices, int direction)
    {
        if (playerState == null) return false;
        var pets = playerState.GetArray("Pets");
        if (pets == null) return false;
        var pac = playerState.GetArray("PetAccessoryCustomisation");
        var movable = GetMovablePetSlots(playerState, pets, pac);
        return BuildMoveMap(movable, slotIndices, direction) != null;
    }

    /// <summary>
    /// Returns whether the selected egg slots can be moved in the given direction.
    /// </summary>
    /// <param name="eggs">The <c>PlayerStateData.Eggs</c> array.</param>
    /// <param name="slotIndices">The selected egg slot indices.</param>
    /// <param name="direction">-1 to move up, +1 to move down.</param>
    internal static bool CanMoveEggs(JsonArray eggs, IReadOnlyList<int> slotIndices, int direction)
    {
        if (eggs == null) return false;
        var movable = GetEggSlots(eggs);
        return BuildMoveMap(movable, slotIndices, direction) != null;
    }

    /// <summary>
    /// Returns the occupied, unlocked pet slots that take part in sorting and moving.
    /// </summary>
    private static List<int> GetMovablePetSlots(JsonObject playerState, JsonArray pets, JsonArray? pac)
    {
        var result = new List<int>();
        var unlocked = playerState.GetArray("UnlockedPetSlots");
        if (unlocked == null) return result;

        for (int i = 0; i < pets.Length; i++)
        {
            if (!IsUnlocked(unlocked, i)) continue;
            if (pac != null && i >= pac.Length) continue;
            if (!CompanionLogic.IsSlotOccupied(pets.Get(i) as JsonObject)) continue;
            result.Add(i);
        }

        return result;
    }

    /// <summary>
    /// Returns the first unlocked pet slots that can receive a sorted pet.
    /// </summary>
    private static List<int> GetPetTargetSlots(JsonObject playerState, JsonArray pets, JsonArray? pac, int count)
    {
        var result = new List<int>(count);
        var unlocked = playerState.GetArray("UnlockedPetSlots");
        if (unlocked == null) return result;

        for (int i = 0; i < pets.Length && result.Count < count; i++)
        {
            if (!IsUnlocked(unlocked, i)) continue;
            if (pac != null && i >= pac.Length) continue;
            result.Add(i);
        }

        return result;
    }

    /// <summary>
    /// Returns the occupied egg slots that take part in sorting and moving.
    /// </summary>
    private static List<int> GetEggSlots(JsonArray eggs)
    {
        var result = new List<int>();
        for (int i = 0; i < eggs.Length; i++)
        {
            if (CompanionLogic.IsSlotOccupied(eggs.Get(i) as JsonObject))
                result.Add(i);
        }

        return result;
    }

    /// <summary>
    /// Orders the given slots by their companion name, with unnamed entries last.
    /// </summary>
    private static List<int> OrderSlots(JsonArray array, IReadOnlyList<int> slots, bool descending)
    {
        var entries = new List<(int Slot, string Name)>(slots.Count);
        foreach (int slot in slots)
        {
            var companion = array.Get(slot) as JsonObject;
            entries.Add((slot, companion != null ? ResolveSortName(companion) : ""));
        }

        return (descending
                ? entries
                    .OrderBy(e => e.Name.Length == 0 ? 1 : 0)
                    .ThenByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.Slot)
                : entries
                    .OrderBy(e => e.Name.Length == 0 ? 1 : 0)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.Slot))
            .Select(e => e.Slot)
            .ToList();
    }

    /// <summary>
    /// Builds a slot-content permutation that moves each ordered source to the matching target.
    /// Sources and targets may be the same set or overlap; empty target slots are paired with
    /// vacated source slots so the permutation covers every affected slot exactly once.
    /// </summary>
    private static Dictionary<int, int>? BuildPackingMap(
        IReadOnlyList<int> orderedSources, IReadOnlyList<int> targets)
    {
        if (orderedSources.Count != targets.Count) return null;

        var map = new Dictionary<int, int>(targets.Count * 2);
        for (int i = 0; i < targets.Count; i++)
            map[orderedSources[i]] = targets[i];

        var sourceSet = new HashSet<int>(orderedSources);
        var targetSet = new HashSet<int>(targets);
        var vacated = orderedSources.Where(s => !targetSet.Contains(s)).OrderBy(s => s).ToList();
        var filled = targets.Where(t => !sourceSet.Contains(t)).OrderBy(t => t).ToList();
        if (vacated.Count != filled.Count) return null;

        for (int i = 0; i < filled.Count; i++)
        {
            if (map.ContainsKey(filled[i])) return null;
            map[filled[i]] = vacated[i];
        }

        var destinations = new HashSet<int>();
        foreach (var pair in map)
        {
            if (!destinations.Add(pair.Value)) return null;
        }

        return map;
    }

    /// <summary>
    /// Builds the pet-slot-only mapping used to remap battle team indices.
    /// </summary>
    private static Dictionary<int, int> BuildPetMap(
        IReadOnlyList<int> orderedSources, IReadOnlyList<int> targets)
    {
        var map = new Dictionary<int, int>(targets.Count);
        for (int i = 0; i < targets.Count; i++)
            map[orderedSources[i]] = targets[i];
        return map;
    }

    /// <summary>
    /// Builds the rotation map for moving a contiguous block one movable position.
    /// Returns <c>null</c> when the selection is not a contiguous movable block or is at a boundary.
    /// </summary>
    private static Dictionary<int, int>? BuildMoveMap(
        IReadOnlyList<int> movable, IReadOnlyList<int> selected, int direction)
    {
        if (direction != -1 && direction != 1) return null;
        if (selected == null || selected.Count == 0) return null;

        var positionsBySlot = new Dictionary<int, int>(movable.Count);
        for (int i = 0; i < movable.Count; i++)
            positionsBySlot[movable[i]] = i;

        var positions = new List<int>(selected.Count);
        foreach (int slot in selected)
        {
            if (!positionsBySlot.TryGetValue(slot, out int position)) return null;
            positions.Add(position);
        }

        positions.Sort();
        for (int i = 1; i < positions.Count; i++)
        {
            if (positions[i] != positions[i - 1] + 1) return null;
        }

        int first = positions[0];
        int count = positions.Count;
        var map = new Dictionary<int, int>(count + 1);

        if (direction < 0)
        {
            if (first == 0) return null;
            for (int i = 0; i < count; i++)
                map[movable[first + i]] = movable[first + i - 1];
            map[movable[first - 1]] = movable[first + count - 1];
        }
        else
        {
            if (first + count >= movable.Count) return null;
            for (int i = 0; i < count; i++)
                map[movable[first + i]] = movable[first + i + 1];
            map[movable[first + count]] = movable[first];
        }

        return map;
    }

    /// <summary>
    /// Applies a slot-content permutation to an array and, when present, its parallel array.
    /// Validates the full map before writing so a failure leaves the data untouched.
    /// </summary>
    private static bool ApplyPermutation(
        JsonArray array, JsonArray? parallel, IReadOnlyDictionary<int, int> map)
    {
        foreach (var pair in map)
        {
            if (pair.Key < 0 || pair.Key >= array.Length) return false;
            if (pair.Value < 0 || pair.Value >= array.Length) return false;
            if (parallel != null)
            {
                if (pair.Key >= parallel.Length) return false;
                if (pair.Value >= parallel.Length) return false;
            }
        }

        var values = new Dictionary<int, object?>(map.Count);
        foreach (int slot in map.Keys)
            values[slot] = array.Get(slot);

        Dictionary<int, object?>? parallelValues = null;
        if (parallel != null)
        {
            parallelValues = new Dictionary<int, object?>(map.Count);
            foreach (int slot in map.Keys)
                parallelValues[slot] = parallel.Get(slot);
        }

        foreach (var pair in map)
        {
            array.Set(pair.Value, values[pair.Key]);
            if (parallel != null)
                parallel.Set(pair.Value, parallelValues![pair.Key]);
        }

        return true;
    }

    /// <summary>
    /// Remaps <c>PetBattleTeam.TeamMembers[].PetIndex</c> through the given old-to-new slot map.
    /// </summary>
    private static void RemapBattleTeam(JsonObject playerState, IReadOnlyDictionary<int, int> map)
    {
        var battleTeam = playerState.GetObject("PetBattleTeam");
        var members = battleTeam?.GetArray("TeamMembers");
        if (members == null) return;

        for (int i = 0; i < members.Length; i++)
        {
            var member = members.GetObject(i);
            if (member == null) continue;

            int index;
            try
            {
                index = member.GetInt("PetIndex");
            }
            catch
            {
                continue;
            }

            if (map.TryGetValue(index, out int mapped))
                member.Set("PetIndex", mapped);
        }
    }

    /// <summary>
    /// Returns whether every given pet slot is unlocked.
    /// </summary>
    private static bool AllPetSlotsUnlocked(JsonObject playerState, IReadOnlyList<int> slots)
    {
        var unlocked = playerState.GetArray("UnlockedPetSlots");
        if (unlocked == null) return false;

        foreach (int slot in slots)
        {
            if (!IsUnlocked(unlocked, slot)) return false;
        }

        return true;
    }

    /// <summary>
    /// Returns whether a pet slot is unlocked in <c>UnlockedPetSlots</c>.
    /// </summary>
    private static bool IsUnlocked(JsonArray unlocked, int index)
    {
        if (index < 0 || index >= unlocked.Length) return false;
        try
        {
            return unlocked.GetBool(index);
        }
        catch
        {
            return false;
        }
    }
}
