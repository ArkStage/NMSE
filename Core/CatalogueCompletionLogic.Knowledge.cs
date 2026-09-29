using NMSE.Data;
using NMSE.Models;

namespace NMSE.Core;

/// <summary>
/// Collected Knowledge completion: SeenStories page progress, lore GLOBAL counters,
/// SavedInteractionIndicies patches and mission completers.
/// </summary>
internal static partial class CatalogueCompletionLogic
{
    private const string GlobalStatsGroupId = "GLOBAL_STATS";
    private const string SiiKeySpelling1 = "SavedInteractionIndicies";
    private const string SiiKeySpelling2 = "SavedInteractionIndices";

    // --- SeenStories page progress ---

    /// <summary>Reads the LastSeen entry index for a page, or null when absent.</summary>
    internal static int? GetLastSeen(JsonObject playerState, int slot, int pageIndex)
    {
        var stories = playerState.GetArray("SeenStories");
        if (stories == null || slot < 0 || slot >= stories.Length) return null;

        var pages = stories.GetObject(slot)?.GetArray("PagesData");
        if (pages == null) return null;

        for (int i = 0; i < pages.Length; i++)
        {
            var page = pages.GetObject(i);
            if (page == null || page.Get("PageIdx") is null) continue;
            if (page.GetInt("PageIdx") != pageIndex) continue;
            return page.Get("LastSeenEntryIdx") is null ? 0 : page.GetInt("LastSeenEntryIdx");
        }
        return null;
    }

    /// <summary>Ensures the SeenStories array has the nine catalogue slots.</summary>
    internal static JsonArray EnsureSeenStories(JsonObject playerState)
    {
        var stories = playerState.GetArray("SeenStories");
        if (stories == null)
        {
            stories = new JsonArray();
            playerState.Set("SeenStories", stories);
        }

        while (stories.Length < 9)
        {
            var entry = new JsonObject();
            entry.Set("PagesData", new JsonArray());
            stories.Add(entry);
        }
        return stories;
    }

    /// <summary>Raises a page's LastSeen entry index. Returns true when it changed.</summary>
    internal static bool UpsertLastSeen(JsonObject playerState, int slot, int pageIndex, int value)
    {
        var stories = EnsureSeenStories(playerState);
        var entry = stories.GetObject(slot);
        if (entry == null)
        {
            entry = new JsonObject();
            entry.Set("PagesData", new JsonArray());
            stories.Set(slot, entry);
        }

        var pages = entry.GetArray("PagesData");
        if (pages == null)
        {
            pages = new JsonArray();
            entry.Set("PagesData", pages);
        }

        for (int i = 0; i < pages.Length; i++)
        {
            var page = pages.GetObject(i);
            if (page == null || page.Get("PageIdx") is null || page.GetInt("PageIdx") != pageIndex) continue;

            int old = page.Get("LastSeenEntryIdx") is null ? 0 : page.GetInt("LastSeenEntryIdx");
            if (old >= value) return false;
            page.Set("LastSeenEntryIdx", value);
            return true;
        }

        var newPage = new JsonObject();
        newPage.Set("PageIdx", pageIndex);
        newPage.Set("LastSeenEntryIdx", value);
        pages.Add(newPage);
        return true;
    }

    /// <summary>Removes a page's LastSeen entry. Returns true when it changed.</summary>
    internal static bool RemoveLastSeen(JsonObject playerState, int slot, int pageIndex)
    {
        var stories = playerState.GetArray("SeenStories");
        var pages = stories != null && slot >= 0 && slot < stories.Length
            ? stories.GetObject(slot)?.GetArray("PagesData")
            : null;
        if (pages == null) return false;

        for (int i = pages.Length - 1; i >= 0; i--)
        {
            var page = pages.GetObject(i);
            if (page == null || page.Get("PageIdx") is null || page.GetInt("PageIdx") != pageIndex) continue;
            pages.RemoveAt(i);
            return true;
        }
        return false;
    }

    // --- SavedInteractionIndicies ---

    private static string SiiKey(JsonObject playerState)
    {
        if (playerState.Contains(SiiKeySpelling1)) return SiiKeySpelling1;
        if (playerState.Contains(SiiKeySpelling2)) return SiiKeySpelling2;
        return SiiKeySpelling1;
    }

    /// <summary>Reads the race and looped arrays for an interaction index.</summary>
    internal static (int[] Races, bool[] Looped) GetSiiStatus(JsonObject playerState, int index)
    {
        var sii = playerState.GetArray(SiiKey(playerState));
        var entry = sii != null && index >= 0 && index < sii.Length ? sii.GetObject(index) : null;
        return (ReadIntArray(entry?.GetArray("SavedRaceIndicies"), 9), ReadBoolArray(entry?.GetArray("HasLoopedIndicies"), 9));
    }

    /// <summary>Finds or creates a SavedInteractionIndicies entry with nine race slots.</summary>
    internal static JsonObject EnsureSiiEntry(JsonObject playerState, int index)
    {
        string key = SiiKey(playerState);
        var sii = playerState.GetArray(key);
        if (sii == null)
        {
            sii = new JsonArray();
            playerState.Set(key, sii);
        }

        while (sii.Length <= index)
            sii.Add(NewSiiEntry());

        if (sii.GetObject(index) is not JsonObject entry)
        {
            entry = NewSiiEntry();
            sii.Set(index, entry);
        }

        entry.Set("SavedRaceIndicies", ToJsonArray(ReadIntArray(entry.GetArray("SavedRaceIndicies"), 9)));
        entry.Set("HasLoopedIndicies", ToJsonArray(ReadBoolArray(entry.GetArray("HasLoopedIndicies"), 9)));
        return entry;
    }

    private static JsonObject NewSiiEntry()
    {
        var entry = new JsonObject();
        entry.Set("SavedRaceIndicies", ToJsonArray(new int[9]));
        entry.Set("HasLoopedIndicies", ToJsonArray(new bool[9]));
        return entry;
    }

    /// <summary>Raises the race/looped values for an interaction index. Returns true when changed.</summary>
    internal static bool ApplySiiRacesAtLeast(JsonObject playerState, int index, IReadOnlyList<int> races, IReadOnlyList<bool> looped)
    {
        var entry = EnsureSiiEntry(playerState, index);
        var currentRaces = ReadIntArray(entry.GetArray("SavedRaceIndicies"), 9);
        var currentLooped = ReadBoolArray(entry.GetArray("HasLoopedIndicies"), 9);

        bool changed = false;
        int count = Math.Min(9, Math.Min(races.Count, looped.Count));
        for (int i = 0; i < count; i++)
        {
            int newRace = Math.Max(currentRaces[i], races[i]);
            bool newLooped = currentLooped[i] || looped[i];
            if (newRace == currentRaces[i] && newLooped == currentLooped[i]) continue;

            currentRaces[i] = newRace;
            currentLooped[i] = newLooped;
            changed = true;
        }

        if (changed)
        {
            entry.Set("SavedRaceIndicies", ToJsonArray(currentRaces));
            entry.Set("HasLoopedIndicies", ToJsonArray(currentLooped));
        }
        return changed;
    }

    /// <summary>Zeroes an interaction index's race and looped arrays. Returns true when changed.</summary>
    internal static bool ClearSiiEntry(JsonObject playerState, int index)
    {
        var sii = playerState.GetArray(SiiKey(playerState));
        if (sii == null || index < 0 || index >= sii.Length) return false;

        var entry = sii.GetObject(index);
        if (entry == null) return false;

        var races = ReadIntArray(entry.GetArray("SavedRaceIndicies"), 9);
        var looped = ReadBoolArray(entry.GetArray("HasLoopedIndicies"), 9);
        if (!races.Any(r => r != 0) && !looped.Any(b => b)) return false;

        entry.Set("SavedRaceIndicies", ToJsonArray(new int[9]));
        entry.Set("HasLoopedIndicies", ToJsonArray(new bool[9]));
        return true;
    }

    // --- Mission progress ---

    /// <summary>Raises a lore mission's progress to the target. Returns true when changed.</summary>
    internal static bool EnsureMissionProgress(JsonObject playerState, string missionId, int progress, int data)
    {
        missionId = NormalizeId(missionId);
        if (missionId.Length == 0) return false;

        var missions = playerState.GetArray("MissionProgress");
        if (missions == null)
        {
            missions = new JsonArray();
            playerState.Set("MissionProgress", missions);
        }

        for (int i = 0; i < missions.Length; i++)
        {
            var mission = missions.GetObject(i);
            if (mission == null || !MissionIdMatches(mission, missionId)) continue;

            int old = mission.Get("Progress") is null ? 0 : mission.GetInt("Progress");
            if (old != -1 && old >= progress) return false;

            mission.Set("Progress", progress);
            if (mission.Contains("Data") || data != 0)
                mission.Set("Data", mission.Get("Data") is null ? data : mission.GetInt("Data"));
            return true;
        }

        var newMission = new JsonObject();
        newMission.Set("Mission", "^" + missionId);
        newMission.Set("Progress", progress);
        newMission.Set("Seed", 0);
        newMission.Set("Data", data);
        newMission.Set("Stat", 0);
        newMission.Set("Participants", new JsonArray());
        missions.Add(newMission);
        return true;
    }

    /// <summary>Resets a lore mission's progress to -1. Returns true when changed.</summary>
    internal static bool ClearMissionProgress(JsonObject playerState, string missionId)
    {
        missionId = NormalizeId(missionId);
        var missions = playerState.GetArray("MissionProgress");
        if (missions == null || missionId.Length == 0) return false;

        for (int i = 0; i < missions.Length; i++)
        {
            var mission = missions.GetObject(i);
            if (mission == null || !MissionIdMatches(mission, missionId)) continue;

            int old = mission.Get("Progress") is null ? 0 : mission.GetInt("Progress");
            if (old == -1) return false;
            mission.Set("Progress", -1);
            return true;
        }
        return false;
    }

    /// <summary>Sets a lore mission's progress to an explicit value. Returns true when changed.</summary>
    internal static bool SetMissionProgress(JsonObject playerState, string missionId, int value)
    {
        missionId = NormalizeId(missionId);
        if (missionId.Length == 0 || value < 0) return false;

        var missions = playerState.GetArray("MissionProgress");
        if (missions == null)
        {
            missions = new JsonArray();
            playerState.Set("MissionProgress", missions);
        }

        for (int i = 0; i < missions.Length; i++)
        {
            var mission = missions.GetObject(i);
            if (mission == null || !MissionIdMatches(mission, missionId)) continue;

            int old = mission.Get("Progress") is null ? 0 : mission.GetInt("Progress");
            if (old == value) return false;
            mission.Set("Progress", value);
            return true;
        }

        var newMission = new JsonObject();
        newMission.Set("Mission", "^" + missionId);
        newMission.Set("Progress", value);
        newMission.Set("Seed", 0);
        newMission.Set("Data", 0);
        newMission.Set("Stat", 0);
        newMission.Set("Participants", new JsonArray());
        missions.Add(newMission);
        return true;
    }

    private static bool MissionIdMatches(JsonObject mission, string missionId) =>
        string.Equals(NormalizeId(mission.GetString("Mission")), missionId, StringComparison.OrdinalIgnoreCase);

    // --- Page status and apply/clear ---

    /// <summary>Computes the status of every Collected Knowledge page.</summary>
    internal static IReadOnlyList<KnowledgePageStatus> GetKnowledgeStatuses(JsonObject playerState)
    {
        var globals = GetGlobalStatsMap(playerState);
        var results = new List<KnowledgePageStatus>(KnowledgeCatalogue.Pages.Length);
        foreach (var page in KnowledgeCatalogue.Pages)
            results.Add(GetKnowledgeStatus(playerState, page, globals));
        return results;
    }

    /// <summary>Computes the status of one Collected Knowledge page.</summary>
    internal static KnowledgePageStatus GetKnowledgeStatus(
        JsonObject playerState, KnowledgePage page, Dictionary<string, JsonObject> globals)
    {
        if (page.Recipe == KnowledgeRecipe.Words)
            return new KnowledgePageStatus(page, null, null, "SKIP", "words/glyphs - not auto-maxed");

        int? last = GetLastSeen(playerState, page.Slot, page.PageIndex);
        if (string.Equals(page.Id, "jr_devnotes", StringComparison.OrdinalIgnoreCase))
        {
            // The game records commentary unlocks in DEV_NOTES; SeenStories may be absent
            // (for example when the commentary was unlocked outside the mission flow).
            int count = DevNotesLogic.GetUnlockedCount(playerState);
            last = count > 0 ? count : null;
        }
        int? global = null;
        if (page.GlobalStat != null)
            global = globals.TryGetValue(page.GlobalStat, out var stat) ? GetGlobalInt(stat) : 0;

        switch (page.Recipe)
        {
            case KnowledgeRecipe.Bitmask:
            {
                string detail = $"LastSeen {Display(last)} · Global {Display(global)}";
                if (last is null && !StatAtLeast(global, 1, page.GlobalStat) && (global ?? 0) == 0)
                    return new KnowledgePageStatus(page, last, global, "MISSING", $"{detail} · need mask {page.TableMax}");

                bool okGlobal = StatAtLeast(global, page.TableMax, page.GlobalStat);
                bool okLastSeen = StatAtLeast(last, page.TableMax, null);
                return okGlobal && okLastSeen
                    ? new KnowledgePageStatus(page, last, global, "OK", detail)
                    : new KnowledgePageStatus(page, last, global, "UNDER", $"{detail} · need both at {page.TableMax} (or -1)");
            }

            case KnowledgeRecipe.Counter:
            {
                string detail = $"LastSeen {Display(last)} · Global {Display(global)}";
                if (last is null && !StatAtLeast(global, 1, page.GlobalStat) && (global ?? 0) == 0)
                    return new KnowledgePageStatus(page, last, global, "MISSING", $"{detail} · need {page.TableMax}");

                bool okLastSeen = StatAtLeast(last, page.TableMax, null);
                // DEV_NOTES stores the highest unlocked index, one less than the count.
                int globalTarget = string.Equals(page.GlobalStat, "DEV_NOTES", StringComparison.OrdinalIgnoreCase)
                    ? page.TableMax - 1
                    : page.TableMax;
                bool okGlobal = global is null || StatAtLeast(global, globalTarget, page.GlobalStat);
                if (okLastSeen)
                {
                    string label = string.Equals(page.GlobalStat, "DEV_NOTES", StringComparison.OrdinalIgnoreCase)
                        ? "Unlocked"
                        : "LastSeen";
                    detail = $"{label} {Display(last)}/{page.TableMax}";
                    if (page.GlobalStat != null && !okGlobal)
                        detail += $" · Global {Display(global)} (can sync)";
                    return new KnowledgePageStatus(page, last, global, "OK", detail);
                }

                return new KnowledgePageStatus(page, last, global, last is null ? "MISSING" : "UNDER", detail);
            }

            default:
            {
                if (last is null)
                    return new KnowledgePageStatus(page, null, global, "MISSING", $"No entry · need LastSeen {page.TableMax}");

                string detail = $"LastSeen {Display(last)}/{page.TableMax}";
                if (last < page.TableMax)
                    return new KnowledgePageStatus(page, last, global, "UNDER", detail);

                if (page.SiiIndex != null && page.SiiTarget != null)
                {
                    var (races, looped) = GetSiiStatus(playerState, page.SiiIndex.Value);
                    int best = races.Length > 0 ? races.Max() : 0;
                    int bestIndex = Array.IndexOf(races, best);
                    detail += $" · Interaction[{page.SiiIndex}] {best}/{page.SiiTarget}";
                    if (best < page.SiiTarget)
                        return new KnowledgePageStatus(page, last, global, "UNDER", detail);

                    detail += bestIndex >= 0 && bestIndex < looped.Length && looped[bestIndex] ? " · looped" : " · not looped";
                }

                return new KnowledgePageStatus(page, last, global, "OK", detail);
            }
        }
    }

    /// <summary>Applies a page (and optionally the shared story completers, matching the reference).</summary>
    /// <returns>True when anything changed.</returns>
    internal static bool ApplyKnowledgePage(
        JsonObject playerState,
        KnowledgePage page,
        CatalogueDatabase.StoryCompleterPack pack,
        bool includeCompleters = true)
    {
        if (page.Recipe == KnowledgeRecipe.Words) return false;

        bool changed = includeCompleters && ApplyKnowledgeCompleters(playerState, pack) > 0;
        changed |= UpsertLastSeen(playerState, page.Slot, page.PageIndex, page.TableMax);

        if (page.GlobalStat != null)
        {
            if (page.Recipe == KnowledgeRecipe.Bitmask)
            {
                var stat = EnsureGlobalStat(playerState, page.GlobalStat);
                int current = GetGlobalInt(stat);
                int updated = current < 0 ? page.TableMax : current | page.TableMax;
                if (updated != current)
                {
                    SetGlobalInt(stat, updated);
                    changed = true;
                }
            }
            else if (string.Equals(page.GlobalStat, "DEV_NOTES", StringComparison.OrdinalIgnoreCase))
            {
                // Keeps the DEV_NOTES index in step with the SeenStories count.
                changed |= DevNotesLogic.SetUnlockedCount(playerState, KnowledgeCatalogue.DevNotesTarget);
            }
            else
            {
                changed |= SetGlobalStatAtLeast(playerState, page.GlobalStat, page.TableMax);
            }
        }

        if (page.Recipe == KnowledgeRecipe.Interaction && page.SiiIndex != null && page.SiiTarget != null)
        {
            var races = new int[9];
            var looped = new bool[9];
            foreach (int race in KnowledgeCatalogue.DefaultRaces)
            {
                if (race < 0 || race >= races.Length) continue;
                races[race] = page.SiiTarget.Value;
                looped[race] = true;
            }
            changed |= ApplySiiRacesAtLeast(playerState, page.SiiIndex.Value, races, looped);
        }

        return changed;
    }

    /// <summary>Reverses a page: removes LastSeen, zeroes its GLOBAL and SII slots.</summary>
    /// <returns>True when anything changed.</returns>
    internal static bool ClearKnowledgePage(JsonObject playerState, KnowledgePage page)
    {
        if (page.Recipe == KnowledgeRecipe.Words) return false;

        bool changed = RemoveLastSeen(playerState, page.Slot, page.PageIndex);
        if (page.GlobalStat != null)
        {
            changed |= string.Equals(page.GlobalStat, "DEV_NOTES", StringComparison.OrdinalIgnoreCase)
                ? DevNotesLogic.Clear(playerState)
                : ClearGlobalStat(playerState, page.GlobalStat);
        }
        if (page.Recipe == KnowledgeRecipe.Interaction && page.SiiIndex != null)
            changed |= ClearSiiEntry(playerState, page.SiiIndex.Value);
        return changed;
    }

    /// <summary>
    /// Sets a page's progress to an explicit value (0 clears the page). Counter pages
    /// sync their GLOBAL, bitmask pages OR their mask bits and interaction pages set
    /// LastSeen only.
    /// </summary>
    /// <returns>True when anything changed.</returns>
    internal static bool ApplyKnowledgePageProgress(JsonObject playerState, KnowledgePage page, int value)
    {
        if (page.Recipe == KnowledgeRecipe.Words) return false;
        if (value <= 0) return ClearKnowledgePage(playerState, page);

        bool changed = UpsertLastSeen(playerState, page.Slot, page.PageIndex, value);
        if (page.GlobalStat != null)
        {
            if (string.Equals(page.GlobalStat, "DEV_NOTES", StringComparison.OrdinalIgnoreCase))
            {
                // Keeps the DEV_NOTES index in step with the SeenStories count.
                changed |= DevNotesLogic.SetUnlockedCount(playerState, value);
            }
            else
            {
                changed |= page.Recipe == KnowledgeRecipe.Bitmask
                    ? SetGlobalStatBits(playerState, page.GlobalStat, value)
                    : SetGlobalStatValue(playerState, page.GlobalStat, value);
            }
        }
        return changed;
    }

    private static bool SetGlobalStatBits(JsonObject playerState, string statId, int bits)
    {
        var stat = EnsureGlobalStat(playerState, statId);
        int current = GetGlobalInt(stat);
        int updated = current < 0 ? bits : current | bits;
        if (updated == current) return false;

        SetGlobalInt(stat, updated);
        return true;
    }

    // --- Story completers ---

    /// <summary>Applies every story completer (globals, SII patches, lore missions and flags).</summary>
    /// <returns>The number of changes applied.</returns>
    internal static int ApplyKnowledgeCompleters(JsonObject playerState, CatalogueDatabase.StoryCompleterPack pack)
    {
        int changed = 0;

        foreach (var (slot, pageIndex, _) in KnowledgeCatalogue.LanguageGlyphPages)
            if (UpsertLastSeen(playerState, slot, pageIndex, 1)) changed++;

        foreach (var (statId, target) in KnowledgeCatalogue.StoryCompleterGlobals)
            if (SetGlobalStatAtLeast(playerState, statId, target)) changed++;

        changed += ApplyBaseComputer(playerState, pack) ? 1 : 0;
        changed += ApplyDevNotes(playerState) ? 1 : 0;

        foreach (var patch in pack.SiiPatches)
            if (ApplySiiRacesAtLeast(playerState, patch.Index, patch.Races, patch.Looped)) changed++;

        foreach (var mission in pack.Missions)
            if (EnsureMissionProgress(playerState, mission.Mission, mission.Progress, mission.Data)) changed++;

        if (playerState.Get("BuildersKnown") is not true)
        {
            playerState.Set("BuildersKnown", true);
            changed++;
        }

        if (playerState.Get("HasDiscoveredPurpleSystems") is not true)
        {
            playerState.Set("HasDiscoveredPurpleSystems", true);
            changed++;
        }

        return changed;
    }

    /// <summary>Reverses every story completer.</summary>
    /// <returns>The number of changes applied.</returns>
    internal static int ClearKnowledgeCompleters(JsonObject playerState, CatalogueDatabase.StoryCompleterPack pack)
    {
        int changed = 0;

        foreach (var (slot, pageIndex, _) in KnowledgeCatalogue.LanguageGlyphPages)
            if (RemoveLastSeen(playerState, slot, pageIndex)) changed++;

        foreach (var (statId, _) in KnowledgeCatalogue.StoryCompleterGlobals)
            if (ClearGlobalStat(playerState, statId)) changed++;

        if (RemoveLastSeen(playerState, KnowledgeCatalogue.BaseCompPageSlot, KnowledgeCatalogue.BaseCompPageIndex)) changed++;
        if (ClearGlobalStat(playerState, "BASECOMP_LORE")) changed++;
        if (DevNotesLogic.Clear(playerState)) changed++;

        foreach (var patch in pack.SiiPatches)
            if (ClearSiiEntry(playerState, patch.Index)) changed++;

        foreach (var mission in pack.Missions)
            if (ClearMissionProgress(playerState, mission.Mission)) changed++;

        if (playerState.Get("BuildersKnown") is true)
        {
            playerState.Set("BuildersKnown", false);
            changed++;
        }

        if (playerState.Get("HasDiscoveredPurpleSystems") is true)
        {
            playerState.Set("HasDiscoveredPurpleSystems", false);
            changed++;
        }

        return changed;
    }

    private static bool ApplyBaseComputer(JsonObject playerState, CatalogueDatabase.StoryCompleterPack pack)
    {
        bool changed = UpsertLastSeen(playerState, KnowledgeCatalogue.BaseCompPageSlot, KnowledgeCatalogue.BaseCompPageIndex, pack.BaseCompMax);
        changed |= SetGlobalStatAtLeast(playerState, "BASECOMP_LORE", pack.BaseCompMax);
        return changed;
    }

    private static bool ApplyDevNotes(JsonObject playerState)
    {
        // Writes both the DEV_NOTES high-water index and the SeenStories count.
        return DevNotesLogic.SetUnlockedCount(playerState, KnowledgeCatalogue.DevNotesTarget);
    }

    /// <summary>Builds the Story Completers group rows for the Collected Knowledge tab.</summary>
    internal static IReadOnlyList<KnowledgeCompleterStatus> GetKnowledgeCompleterStatuses(
        JsonObject playerState, CatalogueDatabase.StoryCompleterPack pack)
    {
        var globals = GetGlobalStatsMap(playerState);
        var results = new List<KnowledgeCompleterStatus>();

        foreach (var (statId, target) in KnowledgeCatalogue.StoryCompleterGlobals)
        {
            int? value = globals.TryGetValue(statId, out var stat) ? GetGlobalInt(stat) : null;
            bool complete = IsGlobalStatComplete(value, target);
            results.Add(new KnowledgeCompleterStatus(statId, statId, KnowledgeCompleterKind.Global, target, value, complete,
                $"{Display(value)} / {target}"));
        }

        int? baseCompLastSeen = GetLastSeen(playerState, KnowledgeCatalogue.BaseCompPageSlot, KnowledgeCatalogue.BaseCompPageIndex);
        int? baseCompGlobal = globals.TryGetValue("BASECOMP_LORE", out var baseCompStat) ? GetGlobalInt(baseCompStat) : null;
        bool baseCompComplete = StatAtLeast(baseCompLastSeen, pack.BaseCompMax, null);
        results.Add(new KnowledgeCompleterStatus("BASECOMP_LORE", "Base Computer Archives", KnowledgeCompleterKind.BaseComputer,
            pack.BaseCompMax, baseCompLastSeen, baseCompComplete,
            $"LastSeen {Display(baseCompLastSeen)} · Global {Display(baseCompGlobal)} · target {pack.BaseCompMax}"));

        int devNotesCount = DevNotesLogic.GetUnlockedCount(playerState);
        int? devNotesGlobal = globals.TryGetValue("DEV_NOTES", out var devNotesStat) ? GetGlobalInt(devNotesStat) : null;
        bool devNotesComplete = devNotesCount >= KnowledgeCatalogue.DevNotesTarget;
        results.Add(new KnowledgeCompleterStatus("DEV_NOTES", "Developer Commentary", KnowledgeCompleterKind.DevNotes,
            KnowledgeCatalogue.DevNotesTarget, devNotesCount, devNotesComplete,
            $"Unlocked {devNotesCount}/{KnowledgeCatalogue.DevNotesTarget} · Global {Display(devNotesGlobal)}"));

        foreach (var patch in pack.SiiPatches)
        {
            var (races, looped) = GetSiiStatus(playerState, patch.Index);
            bool complete = true;
            int required = 0;
            for (int i = 0; i < patch.Races.Length && i < races.Length; i++)
            {
                required = Math.Max(required, patch.Races[i]);
                if (patch.Races[i] != 0 && races[i] < patch.Races[i]) complete = false;
                if (i < patch.Looped.Length && patch.Looped[i] && !looped[i]) complete = false;
            }
            int best = races.Length > 0 ? races.Max() : 0;
            results.Add(new KnowledgeCompleterStatus($"SII_{patch.Index}", $"Interaction Progress [{patch.Index}]",
                KnowledgeCompleterKind.SiiPatch, 0, best, complete, $"best {best} · required {required}", patch));
        }

        results.Add(new KnowledgeCompleterStatus("BuildersKnown", "Builders Known", KnowledgeCompleterKind.Flag,
            1, playerState.Get("BuildersKnown") is true ? 1 : 0, playerState.Get("BuildersKnown") is true, ""));
        results.Add(new KnowledgeCompleterStatus("HasDiscoveredPurpleSystems", "Has Discovered Purple Systems", KnowledgeCompleterKind.Flag,
            1, playerState.Get("HasDiscoveredPurpleSystems") is true ? 1 : 0, playerState.Get("HasDiscoveredPurpleSystems") is true, ""));

        foreach (var mission in pack.Missions)
        {
            int? progress = GetMissionProgress(playerState, mission.Mission);
            bool complete = progress is int p && p >= mission.Progress;
            results.Add(new KnowledgeCompleterStatus(mission.Mission, mission.Mission, KnowledgeCompleterKind.Mission,
                mission.Progress, progress, complete, $"{Display(progress)} / {mission.Progress}", null, mission.Data));
        }

        return results;
    }

    /// <summary>Applies a single story completer row.</summary>
    internal static bool ApplyKnowledgeCompleter(JsonObject playerState, KnowledgeCompleterStatus status, CatalogueDatabase.StoryCompleterPack pack)
    {
        return status.Kind switch
        {
            KnowledgeCompleterKind.Global => SetGlobalStatAtLeast(playerState, status.Id, status.Target),
            KnowledgeCompleterKind.BaseComputer => ApplyBaseComputer(playerState, pack),
            KnowledgeCompleterKind.DevNotes => ApplyDevNotes(playerState),
            KnowledgeCompleterKind.SiiPatch => status.Patch != null && ApplySiiRacesAtLeast(playerState, status.Patch.Index, status.Patch.Races, status.Patch.Looped),
            KnowledgeCompleterKind.Flag => SetFlag(playerState, status.Id, true),
            KnowledgeCompleterKind.Mission => EnsureMissionProgress(playerState, status.Id, status.Target, status.Data),
            _ => false,
        };
    }

    /// <summary>Reverses a single story completer row.</summary>
    internal static bool ClearKnowledgeCompleter(JsonObject playerState, KnowledgeCompleterStatus status)
    {
        return status.Kind switch
        {
            KnowledgeCompleterKind.Global => ClearGlobalStat(playerState, status.Id),
            KnowledgeCompleterKind.BaseComputer =>
                RemoveLastSeen(playerState, KnowledgeCatalogue.BaseCompPageSlot, KnowledgeCatalogue.BaseCompPageIndex)
                | ClearGlobalStat(playerState, "BASECOMP_LORE"),
            KnowledgeCompleterKind.DevNotes => DevNotesLogic.Clear(playerState),
            KnowledgeCompleterKind.SiiPatch => status.Patch != null && ClearSiiEntry(playerState, status.Patch.Index),
            KnowledgeCompleterKind.Flag => SetFlag(playerState, status.Id, false),
            KnowledgeCompleterKind.Mission => ClearMissionProgress(playerState, status.Id),
            _ => false,
        };
    }

    private static bool SetFlag(JsonObject playerState, string name, bool value)
    {
        if (playerState.Get(name) is bool current && current == value) return false;
        playerState.Set(name, value);
        return true;
    }

    /// <summary>
    /// Sets a story completer's progress to an explicit value (0 clears it). SII patch
    /// and flag rows are not editable this way.
    /// </summary>
    /// <returns>True when anything changed.</returns>
    internal static bool ApplyKnowledgeCompleterProgress(JsonObject playerState, KnowledgeCompleterStatus status, int value)
    {
        switch (status.Kind)
        {
            case KnowledgeCompleterKind.Global:
                return value <= 0
                    ? ClearGlobalStat(playerState, status.Id)
                    : SetGlobalStatValue(playerState, status.Id, value);

            case KnowledgeCompleterKind.BaseComputer:
                if (value <= 0)
                {
                    return RemoveLastSeen(playerState, KnowledgeCatalogue.BaseCompPageSlot, KnowledgeCatalogue.BaseCompPageIndex)
                        | ClearGlobalStat(playerState, "BASECOMP_LORE");
                }
                return UpsertLastSeen(playerState, KnowledgeCatalogue.BaseCompPageSlot, KnowledgeCatalogue.BaseCompPageIndex, value)
                    | SetGlobalStatValue(playerState, "BASECOMP_LORE", value);

            case KnowledgeCompleterKind.DevNotes:
                return value <= 0
                    ? DevNotesLogic.Clear(playerState)
                    : DevNotesLogic.SetUnlockedCount(playerState, value);

            case KnowledgeCompleterKind.Mission:
                return value <= 0
                    ? ClearMissionProgress(playerState, status.Id)
                    : SetMissionProgress(playerState, status.Id, value);

            default:
                return false;
        }
    }

    internal static int? GetMissionProgress(JsonObject playerState, string missionId)
    {
        missionId = NormalizeId(missionId);
        var missions = playerState.GetArray("MissionProgress");
        if (missions == null) return null;

        for (int i = 0; i < missions.Length; i++)
        {
            var mission = missions.GetObject(i);
            if (mission == null || !MissionIdMatches(mission, missionId)) continue;
            return mission.Get("Progress") is null ? 0 : mission.GetInt("Progress");
        }
        return null;
    }

    // --- Helpers ---

    private static bool StatAtLeast(int? value, int target, string? statId)
    {
        if (value is not int current) return false;
        if (current < 0)
            return statId == null || !KnowledgeCatalogue.NegativeOneMeansEmpty.Contains(statId);
        return current >= target;
    }

    private static string Display(int? value) => value is int v ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";

    private static int[] ReadIntArray(JsonArray? array, int length)
    {
        var result = new int[length];
        if (array == null) return result;
        for (int i = 0; i < length && i < array.Length; i++)
        {
            object? value = array.Get(i);
            result[i] = value switch
            {
                int iv => iv,
                long lv => (int)lv,
                double dv => (int)dv,
                RawDouble rd => (int)rd.Value,
                _ => 0
            };
        }
        return result;
    }

    private static bool[] ReadBoolArray(JsonArray? array, int length)
    {
        var result = new bool[length];
        if (array == null) return result;
        for (int i = 0; i < length && i < array.Length; i++)
            result[i] = array.Get(i) is bool b && b;
        return result;
    }

    private static JsonArray ToJsonArray(int[] values)
    {
        var array = new JsonArray();
        foreach (int value in values) array.Add(value);
        return array;
    }

    private static JsonArray ToJsonArray(bool[] values)
    {
        var array = new JsonArray();
        foreach (bool value in values) array.Add(value);
        return array;
    }
}
