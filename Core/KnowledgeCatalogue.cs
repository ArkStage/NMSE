using NMSE.Data;

namespace NMSE.Core;

/// <summary>How a Collected Knowledge page tracks progress.</summary>
internal enum KnowledgeRecipe
{
    Counter,
    Bitmask,
    Interaction,
    Words,
}

/// <summary>Static definition of one Collected Knowledge catalogue page.</summary>
internal sealed record KnowledgePage(
    string Id,
    string Category,
    string Name,
    int Slot,
    int PageIndex,
    int TableMax,
    KnowledgeRecipe Recipe,
    string? GlobalStat = null,
    int? SiiIndex = null,
    int? SiiTarget = null,
    string? GameId = null);

/// <summary>Status of a Collected Knowledge page against its targets.</summary>
internal sealed record KnowledgePageStatus(
    KnowledgePage Page,
    int? LastSeen,
    int? GlobalValue,
    string Status,
    string Detail);

/// <summary>Kind of a story completer entry.</summary>
internal enum KnowledgeCompleterKind
{
    Global,
    BaseComputer,
    DevNotes,
    SiiPatch,
    Flag,
    Mission,
}

/// <summary>Status of one story completer entry.</summary>
internal sealed record KnowledgeCompleterStatus(
    string Id,
    string Name,
    KnowledgeCompleterKind Kind,
    int Target,
    int? Current,
    bool Complete,
    string Detail,
    CatalogueDatabase.SiiPatch? Patch = null,
    int Data = 0);

/// <summary>
/// Baked definition of the Collected Knowledge pages and story completers,
/// mirroring the verified reference tool catalogue.
/// </summary>
internal static class KnowledgeCatalogue
{
    /// <summary>Race slots patched when completing interaction pages.</summary>
    internal static readonly int[] DefaultRaces = [0, 1, 2, 6, 7];

    /// <summary>Race ordinal for each language word page (indexed as the game stores it).</summary>
    internal static readonly Dictionary<string, int> WordPageRaceOrdinals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gek_words"] = 0,
        ["vy_words"] = 1,
        ["kor_words"] = 2,
        ["atlas_words"] = 4,
        ["ap_words"] = 8,
    };

    /// <summary>
    /// LastSeen entries that mark the language/glyph pages as present, referenced by
    /// internal page ID so they follow game page reorders through <see cref="KnowledgePage.GameId"/>.
    /// </summary>
    internal static readonly (string PageId, string Label)[] LanguageGlyphPages =
    [
        ("gek_words", "Language Records (Gek)"),
        ("vy_words", "Language Records (Vy'keen)"),
        ("kor_words", "Language Records (Korvax)"),
        ("atlas_words", "The Words of the Atlas"),
        ("atlas_glyphs", "Portal Glyphs"),
        ("ap_words", "Language Records (Autophage)"),
    ];

    /// <summary>Soft story GLOBAL counters raised by the story completers.</summary>
    internal static readonly (string StatId, int Target)[] StoryCompleterGlobals =
    [
        ("ATLAS_STORY", 6),
        ("ATLAS_PATH", 9),
        ("ATLAS_LOOPS", 7),
        ("BUILDERS_INTRO", 3),
        ("PIRATE_MYSTERY", 4),
    ];

    /// <summary>GLOBAL stats where -1 means empty rather than full.</summary>
    internal static readonly HashSet<string> NegativeOneMeansEmpty = new(StringComparer.OrdinalIgnoreCase)
    {
        "DEV_NOTES",
    };

    internal const int DevNotesPageSlot = 5;
    internal const int DevNotesPageIndex = 1;
    internal const int DevNotesTarget = 39;
    internal const int BaseCompPageSlot = 5;
    internal const int BaseCompPageIndex = 2;

    /// <summary>
    /// The Collected Knowledge pages. Slot/page indices and targets match the current
    /// game build's story table; <see cref="KnowledgePage.GameId"/> lets the editor
    /// re-align them from the extracted story table when the game reorders pages.
    /// </summary>
    internal static readonly KnowledgePage[] Pages =
    [
        // Gek
        new("gek_plaque", "The Gek", "Ancient Plaque (Gek)", 0, 0, 29, KnowledgeRecipe.Interaction, SiiIndex: 18, SiiTarget: 30, GameId: "UI_PORTAL_TRA_PLAQUE_TITLE"),
        new("gek_archive", "The Gek", "Planetary Archive (Gek)", 0, 1, 1023, KnowledgeRecipe.Bitmask, "LIB_TRA_LORE", GameId: "UI_PORTAL_TRA_LIB_TITLE"),
        new("gek_mono", "The Gek", "Monolith Visions (Gek)", 0, 2, 19, KnowledgeRecipe.Interaction, SiiIndex: 10, SiiTarget: 20, GameId: "UI_PORTAL_TRA_MONO_TITLE"),
        new("gek_words", "The Gek", "Language Records (Gek)", 0, 3, -1, KnowledgeRecipe.Words, GameId: "UI_WIKI_WORDS_TRA_TITLE"),
        // Vy'keen
        new("vy_plaque", "The Vy'keen", "Ancient Plaque (Vy'keen)", 1, 0, 29, KnowledgeRecipe.Interaction, SiiIndex: 18, SiiTarget: 30, GameId: "UI_PORTAL_WAR_PLAQUE_TITLE"),
        new("vy_archive", "The Vy'keen", "Planetary Archive (Vy'keen)", 1, 1, 1023, KnowledgeRecipe.Bitmask, "LIB_WAR_LORE", GameId: "UI_PORTAL_WAR_LIB_TITLE"),
        new("vy_mono", "The Vy'keen", "Monolith Visions (Vy'keen)", 1, 2, 19, KnowledgeRecipe.Interaction, SiiIndex: 10, SiiTarget: 20, GameId: "UI_PORTAL_WAR_MONO_TITLE"),
        new("vy_words", "The Vy'keen", "Language Records (Vy'keen)", 1, 3, -1, KnowledgeRecipe.Words, GameId: "UI_WIKI_WORDS_WAR_TITLE"),
        // Korvax
        new("kor_plaque", "The Korvax", "Ancient Plaque (Korvax)", 2, 0, 32, KnowledgeRecipe.Interaction, SiiIndex: 18, SiiTarget: 33, GameId: "UI_PORTAL_EXP_PLAQUE_TITLE"),
        new("kor_archive", "The Korvax", "Planetary Archive (Korvax)", 2, 1, 1023, KnowledgeRecipe.Bitmask, "LIB_EXP_LORE", GameId: "UI_PORTAL_EXP_LIB_TITLE"),
        new("kor_mono", "The Korvax", "Monolith Visions (Korvax)", 2, 2, 18, KnowledgeRecipe.Interaction, SiiIndex: 10, SiiTarget: 19, GameId: "UI_PORTAL_EXP_MONO_TITLE"),
        new("kor_words", "The Korvax", "Language Records (Korvax)", 2, 3, -1, KnowledgeRecipe.Words, GameId: "UI_WIKI_WORDS_EXP_TITLE"),
        // Atlas
        new("atlas_iface", "The Atlas", "Atlas Interface", 4, 0, 11, KnowledgeRecipe.Counter, "ATLAS_LORE", GameId: "INTRCT_ATLASSTATION"),
        new("atlas_words", "The Atlas", "The Words of the Atlas", 4, 1, -1, KnowledgeRecipe.Words, GameId: "UI_WIKI_WORDS_ATL_TITLE"),
        new("atlas_glyphs", "The Atlas", "Portal Glyphs", 4, 2, -1, KnowledgeRecipe.Words, GameId: "UI_GUIDE_HEADING_RUNES_CATA"),
        // Journey Records (the current build places Developer Commentary at page 1)
        new("jr_journey", "Journey Records", "The Journey", 5, 0, 51, KnowledgeRecipe.Counter, "CORE_LORE", GameId: "UI_PORTAL_CORE_NAME"),
        new("jr_devnotes", "Journey Records", "Developer Commentary", 5, 1, 38, KnowledgeRecipe.Counter, "DEV_NOTES", GameId: "UI_S23_NOTES_NAME"),
        new("jr_basecomp", "Journey Records", "Base Computer Archives", 5, 2, 20, KnowledgeRecipe.Counter, "BASECOMP_LORE", GameId: "UI_BASELOG_TITLE"),
        new("jr_overseer", "Journey Records", "Expanding the Base", 5, 3, 12, KnowledgeRecipe.Counter, "OVERSEER_LORE", GameId: "UI_OVERSEER_TITLE"),
        new("jr_scientist", "Journey Records", "Scientific Research", 5, 4, 13, KnowledgeRecipe.Counter, "SCIENTIST_LORE", GameId: "UI_SCIENTIST_TITLE"),
        new("jr_weap", "Journey Records", "Weapons Research", 5, 5, 7, KnowledgeRecipe.Counter, "WEAPGUY_LORE", GameId: "UI_WEAPGUY_TITLE"),
        new("jr_farmer", "Journey Records", "Agricultural Research", 5, 6, 10, KnowledgeRecipe.Counter, "FARMER_LORE", GameId: "UI_FARMER_TITLE"),
        new("jr_exo", "Journey Records", "Exocraft Technician", 5, 7, 16, KnowledgeRecipe.Counter, "EXOTUT_LORE", GameId: "UI_EXOTUT_TITLE"),
        new("jr_water", "Journey Records", "Dreams of the Deep", 5, 8, 11, KnowledgeRecipe.Counter, "WATERSTORY_LORE", GameId: "UI_WATERMISSION_TITLE"),
        new("jr_bio", "Journey Records", "Starbirth", 5, 9, 13, KnowledgeRecipe.Counter, "BIOSHIP_LORE", GameId: "UI_BIO_SHIP_MISSION_TITLE"),
        new("jr_worm", "Journey Records", "Emergence", 5, 10, 5, KnowledgeRecipe.Counter, "WORM_LORE", GameId: "UI_PORTAL_WORMLORE_TITLE"),
        new("jr_bug", "Journey Records", "Liquidators", 5, 11, 5, KnowledgeRecipe.Counter, "BUG_LORE", GameId: "UI_SEASON_14_NAME"),
        new("jr_sent", "Journey Records", "A Trace of Metal", 5, 12, 20, KnowledgeRecipe.Counter, "SENT_MISS_LORE", GameId: "UI_SENT_MISS_TITLE"),
        new("jr_pirate", "Journey Records", "Under a Rebel Star", 5, 13, 7, KnowledgeRecipe.Counter, "PIRATES_LORE", GameId: "UI_PIRATESMISS_NAME"),
        // Other History
        new("oh_aband", "Other History", "Abandoned Building", 7, 0, 33, KnowledgeRecipe.Interaction, SiiIndex: 20, SiiTarget: 34, GameId: "BUILDING_ABANDONED_L"),
        new("oh_crash", "Other History", "Crashed Freighter", 7, 1, 19, KnowledgeRecipe.Interaction, SiiIndex: 54, SiiTarget: 20, GameId: "BUILDING_FREIGHTER_ALT"),
        new("oh_grave", "Other History", "Unknown Grave", 7, 2, 15, KnowledgeRecipe.Interaction, SiiIndex: 55, SiiTarget: 16, GameId: "INTRCT_CAVEGRAVE"),
        new("oh_bound", "Other History", "Boundary Failure", 7, 3, 29, KnowledgeRecipe.Interaction, SiiIndex: 56, SiiTarget: 30, GameId: "BUILDING_GLITCHYSTORYBOX_L"),
        new("oh_water", "Other History", "Water Relic", 7, 4, 7, KnowledgeRecipe.Interaction, SiiIndex: 76, SiiTarget: 8, GameId: "UI_WATER_RELIC_NAME_L"),
        new("oh_derelict", "Other History", "Derelict Freighter", 7, 5, 524287, KnowledgeRecipe.Bitmask, "ABAND_LORE", GameId: "UI_DERELICT1_LABEL"),
        new("oh_pillar", "Other History", "Sentinel Pillar", 7, 6, 21, KnowledgeRecipe.Interaction, SiiIndex: 123, SiiTarget: 22, GameId: "UI_SENTINEL_HIVE_NAME"),
        new("oh_epilogue", "Other History", "New Beginnings", 7, 7, 3, KnowledgeRecipe.Interaction, SiiIndex: 61, SiiTarget: 4, GameId: "UI_CORE_EPILOGUE_TITLE"),
        // Autophage
        new("ap_mono", "The Autophage", "Monolith Visions (Autophage)", 8, 0, 11, KnowledgeRecipe.Interaction, SiiIndex: 10, SiiTarget: 12, GameId: "UI_PORTAL_BUI_MONO_TITLE"),
        new("ap_words", "The Autophage", "Language Records (Autophage)", 8, 1, -1, KnowledgeRecipe.Words, GameId: "UI_WIKI_WORDS_BUI_TITLE"),
        new("ap_robo", "The Autophage", "They Who Returned", 8, 2, 16, KnowledgeRecipe.Counter, "ROBOMISS_LORE", GameId: "UI_ROBOMISS_TITLE"),
        new("ap_purp", "The Autophage", "In Stellar Multitudes", 8, 3, 12, KnowledgeRecipe.Counter, "PURPM_LORE", GameId: "UI_PURPM_TITLE"),
    ];
}
