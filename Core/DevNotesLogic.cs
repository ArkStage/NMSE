using NMSE.Models;

namespace NMSE.Core;

/// <summary>
/// Developer commentary ("DEV_NOTES") unlock state for the expedition notes feature.
/// Each commentary entry is granted in game by reward R_DEV_NOTES_n, which sets the
/// DEV_NOTES player stat to n. The stat is a high-water mark index: value n means notes
/// 0..n are unlocked and can be replayed. It lives in
/// PlayerStateData.Stats -> ^GLOBAL_STATS -> { Id: ^DEV_NOTES, Value: { IntValue: n } }.
/// The Collected Knowledge tab tracks the unlocked count separately in SeenStories
/// slot 5 page 13; this class writes both together so the game and the catalogue agree.
/// </summary>
internal static class DevNotesLogic
{
    /// <summary>A developer commentary entry, one per game update.</summary>
    internal sealed record DevNoteEntry(int Index, string NameLocKey, string DateLocKey, string LogLocKey);

    /// <summary>The highest commentary index (39 entries, 0..38).</summary>
    internal const int MaxIndex = 38;

    /// <summary>The number of commentary entries (indices 0..38).</summary>
    internal const int EntryCount = MaxIndex + 1;

    private const string StatId = "DEV_NOTES";
    private const string GlobalStatsGroupId = "GLOBAL_STATS";

    /// <summary>The commentary entries in unlock order (extracted from the seasonal mission tables).</summary>
    internal static readonly IReadOnlyList<DevNoteEntry> Entries = BuildEntries();

    /// <summary>
    /// Builds the entry table from the localisation prefixes used by the game
    /// (UI_S23_&lt;PREFIX&gt;_NAME / _DESC / _LOG).
    /// </summary>
    private static DevNoteEntry[] BuildEntries()
    {
        string[] prefixes =
        {
            "LEAVE_PLANET", "BUILD_BASE", "BUILD_EXO_ANY", "WAKE_TITAN", "ARTEMIS",
            "NEXT", "EXO_BIKE", "GOT_FISHCORE", "GOT_GLITCH", "BEYOND",
            "BYTEBEAT", "WEEKEND", "LIVING_SHIP", "BUILD_MECH", "DO_ABAND_F",
            "ORIGINS", "GOT_PETS", "PIONEERS", "PRISMS", "FRONTIERS",
            "MINIWORMS", "RAID_HIVE", "OUTLAWS", "LEVIATHAN", "ENDURANCE",
            "WAYPOINT", "FRACTAL", "INTERCEPTOR", "ECHOES", "ORBITAL",
            "WORLDS1", "CATCH_RARE_FISH", "WORLDS2", "GOT_FOSSILS", "BEACON",
            "BIGGS_BUILD", "REMNANT", "PET_BATTLES", "FUTURE"
        };

        var entries = new DevNoteEntry[prefixes.Length];
        for (int i = 0; i < prefixes.Length; i++)
        {
            string prefix = prefixes[i];
            entries[i] = new DevNoteEntry(i,
                "UI_S23_" + prefix + "_NAME",
                "UI_S23_" + prefix + "_DESC",
                "UI_S23_" + prefix + "_LOG");
        }
        return entries;
    }

    /// <summary>
    /// Reads the number of unlocked commentary entries (0..39). The game records the
    /// highest unlocked index in DEV_NOTES while the catalogue records the count in
    /// SeenStories slot 5 page 13; the larger of the two is used so either source
    /// alone reports the correct progress.
    /// </summary>
    internal static int GetUnlockedCount(JsonObject? playerState)
    {
        if (playerState == null)
            return 0;

        int fromLastSeen = 0;
        if (CatalogueCompletionLogic.GetLastSeen(playerState,
                KnowledgeCatalogue.DevNotesPageSlot, KnowledgeCatalogue.DevNotesPageIndex) is int seen)
        {
            fromLastSeen = seen;
        }

        int fromGlobal = 0;
        int index = GetUnlockedIndex(playerState);
        if (index >= 0)
            fromGlobal = Math.Min(index + 1, EntryCount);

        return Math.Clamp(Math.Max(fromLastSeen, fromGlobal), 0, EntryCount);
    }

    /// <summary>
    /// Reads the highest unlocked commentary index from the save, or -1 when the stat
    /// is absent (nothing unlocked). Legacy saves that stored the count (39) are
    /// normalised to the highest valid index (38).
    /// </summary>
    internal static int GetUnlockedIndex(JsonObject? playerState)
    {
        if (playerState == null)
            return -1;

        var map = CatalogueCompletionLogic.GetGlobalStatsMap(playerState);
        if (!map.TryGetValue(StatId, out var stat))
            return -1;

        int value = CatalogueCompletionLogic.GetGlobalInt(stat);
        if (value < 0)
            return -1;
        return Math.Min(value, MaxIndex);
    }

    /// <summary>
    /// Sets the number of unlocked commentary entries (clamped to 0..39), writing both
    /// the DEV_NOTES high-water index and the SeenStories count. A count of zero clears
    /// the commentary state.
    /// </summary>
    /// <returns>True when anything changed.</returns>
    internal static bool SetUnlockedCount(JsonObject playerState, int count)
    {
        count = Math.Clamp(count, 0, EntryCount);
        if (count == 0)
            return Clear(playerState);

        bool changed = CatalogueCompletionLogic.UpsertLastSeen(playerState,
            KnowledgeCatalogue.DevNotesPageSlot, KnowledgeCatalogue.DevNotesPageIndex, count);
        changed |= SetGlobalIndex(playerState, count - 1);
        return changed;
    }

    /// <summary>Removes the commentary state (DEV_NOTES stat and SeenStories count).</summary>
    /// <returns>True when anything changed.</returns>
    internal static bool Clear(JsonObject playerState)
    {
        bool changed = CatalogueCompletionLogic.RemoveLastSeen(playerState,
            KnowledgeCatalogue.DevNotesPageSlot, KnowledgeCatalogue.DevNotesPageIndex);

        var map = CatalogueCompletionLogic.GetGlobalStatsMap(playerState);
        if (map.TryGetValue(StatId, out var stat)
            && CatalogueCompletionLogic.GetGlobalInt(stat) != -1)
        {
            // -1 is the game's "nothing unlocked" marker for this stat.
            CatalogueCompletionLogic.SetGlobalInt(stat, -1);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Sets the highest unlocked commentary index (clamped to 0..38), writing both the
    /// DEV_NOTES stat and the matching SeenStories count.
    /// </summary>
    /// <returns>True when anything changed.</returns>
    internal static bool SetUnlockedIndex(JsonObject playerState, int index)
    {
        if (index < 0) index = 0;
        if (index > MaxIndex) index = MaxIndex;
        return SetUnlockedCount(playerState, index + 1);
    }

    /// <summary>
    /// Writes the DEV_NOTES stat to an index (clamped to 0..38), creating the stat
    /// entry and its group when the save does not have one yet.
    /// </summary>
    /// <returns>True when anything changed.</returns>
    private static bool SetGlobalIndex(JsonObject playerState, int index)
    {
        if (index < 0) index = 0;
        if (index > MaxIndex) index = MaxIndex;

        var map = CatalogueCompletionLogic.GetGlobalStatsMap(playerState);
        if (map.TryGetValue(StatId, out var existing))
        {
            if (CatalogueCompletionLogic.GetGlobalInt(existing) == index)
                return false;
            CatalogueCompletionLogic.SetGlobalInt(existing, index);
            return true;
        }

        var groups = playerState.GetArray("Stats");
        JsonObject? globalGroup = null;
        if (groups != null)
        {
            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups.GetObject(i);
                if (group == null) continue;
                if (string.Equals(CatalogueCompletionLogic.NormalizeId(group.GetString("GroupId")),
                        GlobalStatsGroupId, StringComparison.OrdinalIgnoreCase))
                {
                    globalGroup = group;
                    break;
                }
            }
        }

        if (globalGroup == null)
        {
            globalGroup = new JsonObject();
            globalGroup.Set("GroupId", "^" + GlobalStatsGroupId);
            globalGroup.Set("Address", 0);
            globalGroup.Set("Stats", new JsonArray());
            if (groups == null)
            {
                groups = new JsonArray();
                playerState.Set("Stats", groups);
            }
            groups.Add(globalGroup);
        }

        var stats = globalGroup.GetArray("Stats");
        if (stats == null)
        {
            stats = new JsonArray();
            globalGroup.Set("Stats", stats);
        }

        var entry = new JsonObject();
        entry.Set("Id", "^" + StatId);
        entry.Set("Value", new JsonObject());
        stats.Add(entry);
        CatalogueCompletionLogic.SetGlobalInt(entry, index);
        return true;
    }
}
