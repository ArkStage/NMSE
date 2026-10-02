using NMSE.Data;
using NMSE.Models;

namespace NMSE.Core;

/// <summary>
/// Completion helpers that compare the verified catalogue packs against the current
/// save state and add anything missing. Used by the existing catalogue tabs for their
/// completion counters and "Add All Missing" actions.
/// </summary>
internal static partial class CatalogueCompletionLogic
{
    /// <summary>Strips the leading caret and surrounding whitespace from a save ID.</summary>
    /// <param name="id">The raw ID from the save or pack.</param>
    /// <returns>The normalised ID without the caret prefix.</returns>
    internal static string NormalizeId(string? id) => (id ?? "").Trim().TrimStart('^');

    /// <summary>
    /// Builds a case-insensitive set of the IDs stored in a named player-state array.
    /// String entries and object entries with an <c>Id</c> field are both supported.
    /// </summary>
    /// <param name="playerState">The PlayerStateData object.</param>
    /// <param name="arrayName">The JSON key of the array.</param>
    /// <returns>The set of normalised IDs currently present.</returns>
    internal static HashSet<string> GetCurrentIdSet(JsonObject playerState, string arrayName)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = playerState.GetArray(arrayName);
        if (items == null) return set;

        for (int i = 0; i < items.Length; i++)
        {
            string id = ReadArrayId(items.Get(i));
            if (id.Length > 0) set.Add(id);
        }
        return set;
    }

    /// <summary>
    /// Computes the completion counts for a verified pack list: how many unique pack
    /// IDs are present in the save versus the unique pack total.
    /// </summary>
    /// <param name="playerState">The PlayerStateData object.</param>
    /// <param name="arrayName">The JSON key of the array.</param>
    /// <param name="packIds">The verified pack IDs.</param>
    /// <returns>The number of pack IDs present and the unique pack total.</returns>
    internal static (int Have, int Total) GetCompletion(
        JsonObject playerState, string arrayName, IReadOnlyList<string> packIds)
    {
        var current = GetCurrentIdSet(playerState, arrayName);
        int have = 0, total = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in packIds)
        {
            string id = NormalizeId(raw);
            if (id.Length == 0 || !seen.Add(id)) continue;
            total++;
            if (current.Contains(id)) have++;
        }
        return (have, total);
    }

    /// <summary>
    /// Computes completion counts against both the save array and an optional account
    /// seen array: an entry only counts when it is present in both. Used by the tabs whose
    /// in-game state depends on the account seen lists (products, technologies and specials).
    /// </summary>
    /// <param name="playerState">The PlayerStateData object.</param>
    /// <param name="arrayName">The JSON key of the save array.</param>
    /// <param name="userSettings">The account UserSettingsData object, or null.</param>
    /// <param name="accountArrayName">The Seen* array name, or null when there is no account side.</param>
    /// <param name="packIds">The verified pack IDs.</param>
    /// <returns>The number of fully present pack IDs and the unique pack total.</returns>
    internal static (int Have, int Total) GetCombinedCompletion(
        JsonObject playerState, string arrayName,
        JsonObject? userSettings, string? accountArrayName,
        IReadOnlyList<string> packIds)
    {
        var current = GetCurrentIdSet(playerState, arrayName);
        HashSet<string>? account = null;
        if (userSettings != null && !string.IsNullOrEmpty(accountArrayName))
            account = GetSeenIdSet(userSettings, accountArrayName);

        int have = 0, total = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in packIds)
        {
            string id = NormalizeId(raw);
            if (id.Length == 0 || !seen.Add(id)) continue;
            total++;
            if (current.Contains(id) && (account == null || account.Contains(id))) have++;
        }
        return (have, total);
    }

    /// <summary>
    /// Adds every verified pack ID that is not already present in the named array,
    /// writing the game's caret-prefixed form. Existing entries and their order are
    /// preserved.
    /// </summary>
    /// <param name="playerState">The PlayerStateData object.</param>
    /// <param name="arrayName">The JSON key of the array.</param>
    /// <param name="packIds">The verified pack IDs to add.</param>
    /// <returns>The number of IDs added.</returns>
    internal static int AddMissingIds(
        JsonObject playerState, string arrayName, IReadOnlyList<string> packIds)
    {
        var items = playerState.GetArray(arrayName);
        if (items == null)
        {
            items = new JsonArray();
            playerState.Set(arrayName, items);
        }

        var have = GetCurrentIdSet(playerState, arrayName);
        int added = 0;
        foreach (string raw in packIds)
        {
            string id = NormalizeId(raw);
            if (id.Length == 0 || !have.Add(id)) continue;
            items.Add("^" + id);
            added++;
        }
        return added;
    }

    /// <summary>Builds the set of word groups currently present in KnownWordGroups.</summary>
    /// <param name="playerState">The PlayerStateData object.</param>
    /// <returns>The set of normalised group IDs.</returns>
    internal static HashSet<string> GetCurrentWordGroupSet(JsonObject playerState)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = playerState.GetArray("KnownWordGroups");
        if (groups == null) return set;

        for (int i = 0; i < groups.Length; i++)
        {
            var entry = groups.GetObject(i);
            if (entry == null) continue;
            string id = NormalizeId(entry.GetString("Group"));
            if (id.Length > 0) set.Add(id);
        }
        return set;
    }

    /// <summary>Computes the word group completion counts.</summary>
    /// <param name="playerState">The PlayerStateData object.</param>
    /// <param name="packGroups">The verified pack word groups.</param>
    /// <returns>The number of pack groups present and the unique pack total.</returns>
    internal static (int Have, int Total) GetWordGroupCompletion(
        JsonObject playerState, IReadOnlyList<CatalogueDatabase.WordGroup> packGroups)
    {
        var current = GetCurrentWordGroupSet(playerState);
        int have = 0, total = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in packGroups)
        {
            string id = NormalizeId(group.Group);
            if (id.Length == 0 || !seen.Add(id)) continue;
            total++;
            if (current.Contains(id)) have++;
        }
        return (have, total);
    }

    /// <summary>
    /// Adds missing word groups with the pack race flags and raises any existing
    /// group's race flags to match the pack (true flags are never lowered).
    /// </summary>
    /// <param name="playerState">The PlayerStateData object.</param>
    /// <param name="packGroups">The verified pack word groups.</param>
    /// <returns>The number of groups added and the number of existing groups upgraded.</returns>
    internal static (int Added, int Upgraded) AddMissingWordGroups(
        JsonObject playerState, IReadOnlyList<CatalogueDatabase.WordGroup> packGroups)
    {
        var groups = playerState.GetArray("KnownWordGroups");
        if (groups == null)
        {
            groups = new JsonArray();
            playerState.Set("KnownWordGroups", groups);
        }

        var byGroup = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < groups.Length; i++)
        {
            var entry = groups.GetObject(i);
            if (entry == null) continue;
            string id = NormalizeId(entry.GetString("Group"));
            if (id.Length > 0) byGroup.TryAdd(id, entry);
        }

        int added = 0, upgraded = 0;
        foreach (var packGroup in packGroups)
        {
            string id = NormalizeId(packGroup.Group);
            if (id.Length == 0) continue;

            if (!byGroup.TryGetValue(id, out var entry))
            {
                entry = new JsonObject();
                entry.Set("Group", "^" + id);
                var races = new JsonArray();
                foreach (bool known in CatalogueDatabase.NormalizeRaceFlags(packGroup.Races))
                    races.Add(known);
                entry.Set("Races", races);
                groups.Add(entry);
                byGroup[id] = entry;
                added++;
                continue;
            }

            if (UpgradeRaceFlags(entry, packGroup.Races)) upgraded++;
        }
        return (added, upgraded);
    }

    private static bool UpgradeRaceFlags(JsonObject entry, bool[] packRaces)
    {
        var races = entry.GetArray("Races");
        if (races == null)
        {
            races = new JsonArray();
            entry.Set("Races", races);
        }

        bool changed = false;

        // Arrays written by older builds used the compact 5-slot layout; expand them so
        // the Atlas and Autophage flags land on the ordinal indices the game reads.
        if (races.Length == CatalogueDatabase.WordRaceOrdinals.Length)
        {
            var expanded = new JsonArray();
            for (int i = 0; i < CatalogueDatabase.WordRaceCount; i++)
                expanded.Add(false);
            for (int i = 0; i < races.Length; i++)
            {
                if (races.Get(i) is true)
                    expanded.Set(CatalogueDatabase.WordRaceOrdinals[i], true);
            }
            entry.Set("Races", expanded);
            races = expanded;
            changed = true;
        }

        packRaces = CatalogueDatabase.NormalizeRaceFlags(packRaces);
        for (int i = 0; i < packRaces.Length; i++)
        {
            if (i >= races.Length)
            {
                races.Add(false);
                if (packRaces[i])
                {
                    races.Set(i, true);
                    changed = true;
                }
                continue;
            }

            if (packRaces[i] && races.Get(i) is not true)
            {
                races.Set(i, true);
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>
    /// The game's fixed number of fishing record slots. The three FishingRecord lists are
    /// paired by slot index and entries beyond this capacity are not read by the game.
    /// </summary>
    internal const int FishRecordCapacity = 256;

    /// <summary>Builds the set of fish species currently in FishingRecord.ProductList.</summary>
    /// <param name="playerState">The PlayerStateData object.</param>
    /// <returns>The set of normalised species IDs.</returns>
    internal static HashSet<string> GetCurrentFishIdSet(JsonObject playerState)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = playerState.GetObject("FishingRecord")?.GetArray("ProductList");
        if (ids == null) return set;

        // Only the game's fixed slots count; entries appended beyond the capacity are ignored.
        int limit = Math.Min(ids.Length, FishRecordCapacity);
        for (int i = 0; i < limit; i++)
        {
            string id = NormalizeId(ids.Get(i) as string);
            if (id.Length > 0) set.Add(id);
        }
        return set;
    }

    /// <summary>Computes the fish species completion counts.</summary>
    /// <param name="playerState">The PlayerStateData object.</param>
    /// <param name="packFish">The verified pack fish entries.</param>
    /// <returns>The number of pack species present and the unique pack total.</returns>
    internal static (int Have, int Total) GetFishingCompletion(
        JsonObject playerState, IReadOnlyList<CatalogueDatabase.FishEntry> packFish)
    {
        var current = GetCurrentFishIdSet(playerState);
        int have = 0, total = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fish in packFish)
        {
            string id = NormalizeId(fish.Id);
            if (id.Length == 0 || !seen.Add(id)) continue;
            total++;
            if (current.Contains(id)) have++;
        }
        return (have, total);
    }

    /// <summary>
    /// Adds missing fish species into the game's pre-allocated fishing record slots and keeps
    /// the three FishingRecord lists aligned. Existing catches keep their values; entries that
    /// a previous build appended beyond the slot capacity are moved back into free slots, and
    /// stub entries are dropped. Also raises the aggregate FISH_* GLOBAL stats used by the
    /// fishing records UI.
    /// </summary>
    /// <param name="playerState">The PlayerStateData object.</param>
    /// <param name="packFish">The verified pack fish entries.</param>
    /// <returns>The number of species added.</returns>
    internal static int AddMissingFish(
        JsonObject playerState, IReadOnlyList<CatalogueDatabase.FishEntry> packFish)
    {
        var record = playerState.GetObject("FishingRecord");
        if (record == null)
        {
            record = new JsonObject();
            playerState.Set("FishingRecord", record);
        }

        var ids = EnsureArray(record, "ProductList");
        var counts = EnsureArray(record, "ProductCountList");
        var largest = EnsureArray(record, "LargestCatchList");

        // Keep the species already recorded in the game's slots, preserving the raw values so
        // unmodified catches keep their exact serialised text.
        var keptIds = new List<object?>();
        var keptCounts = new List<object?>();
        var keptLargest = new List<object?>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int limit = Math.Min(ids.Length, FishRecordCapacity);
        for (int i = 0; i < limit; i++)
        {
            string id = NormalizeId(ids.Get(i) as string);
            if (id.Length == 0 || !seen.Add(id)) continue;
            keptIds.Add("^" + id);
            keptCounts.Add(i < counts.Length ? counts.Get(i) : 0);
            keptLargest.Add(i < largest.Length ? largest.Get(i) : 0.0);
        }

        int added = 0;
        foreach (var fish in packFish)
        {
            string id = NormalizeId(fish.Id);
            if (id.Length == 0 || !seen.Add(id)) continue;
            keptIds.Add("^" + id);
            keptCounts.Add(fish.Count);
            keptLargest.Add(fish.Largest);
            added++;
        }

        // Pad with stub slots so the record keeps the shape the game expects.
        int length = Math.Max(FishRecordCapacity, keptIds.Count);
        while (keptIds.Count < length)
        {
            keptIds.Add("^");
            keptCounts.Add(0);
            keptLargest.Add(0.0);
        }

        RebuildArray(ids, keptIds);
        RebuildArray(counts, keptCounts);
        RebuildArray(largest, keptLargest);

        // Mirror the record into the aggregate GLOBAL stats used by the records UI.
        foreach (var (statId, target) in CatalogueKnownValues.DiscoveryStats)
        {
            if (statId.StartsWith("FISH_", StringComparison.OrdinalIgnoreCase))
                SetGlobalStatAtLeast(playerState, statId, target);
        }

        return added;
    }

    private static void RebuildArray(JsonArray array, List<object?> values)
    {
        array.Clear();
        foreach (var value in values)
            array.Add(value);
    }

    private static JsonArray EnsureArray(JsonObject parent, string key)
    {
        var array = parent.GetArray(key);
        if (array == null)
        {
            array = new JsonArray();
            parent.Set(key, array);
        }
        return array;
    }

    private static string ReadArrayId(object? value) => value switch
    {
        string s => NormalizeId(s),
        JsonObject obj => NormalizeId(obj.GetString("Id")),
        _ => ""
    };
}
