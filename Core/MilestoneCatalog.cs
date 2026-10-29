using NMSE.Models;

namespace NMSE.Core;

/// <summary>Top-level Milestones tab.</summary>
internal enum MilestoneTab
{
    Milestones,
    Lifeforms,
    Factions,
    OtherStats,
}

/// <summary>One stat placement in the Milestones panel.</summary>
/// <param name="StatId">Save stat ID, including the ^ prefix.</param>
/// <param name="Tab">Top-level tab.</param>
/// <param name="SectionKey">Section/category localization key.</param>
/// <param name="SubcategoryKey">Thematic subcategory key for Other Stats, or null.</param>
/// <param name="GameNameKey">Primary in-game display name key.</param>
/// <param name="EditorNameKey">Legacy editor name key for the reference table and tooltips.</param>
/// <param name="Order">Order within the section or subcategory.</param>
/// <param name="ShowGuildRank">Whether the row shows guild rank/promotion labels.</param>
/// <param name="NoteKey">Optional reference-table note key.</param>
/// <param name="EditorOnly">Whether the stat has no in-game menu equivalent.</param>
internal sealed record MilestonePlacement(
    string StatId,
    MilestoneTab Tab,
    string SectionKey,
    string? SubcategoryKey,
    string GameNameKey,
    string EditorNameKey,
    int Order,
    bool ShowGuildRank,
    string? NoteKey,
    bool EditorOnly);

/// <summary>One unique stat and all of its placements.</summary>
internal sealed class MilestoneStat
{
    internal MilestoneStat(string statId, IReadOnlyList<MilestonePlacement> placements)
    {
        StatId = statId;
        Placements = placements;
    }

    internal string StatId { get; }
    internal IReadOnlyList<MilestonePlacement> Placements { get; }
    internal bool IsShared => Placements.Count > 1;
}

/// <summary>Declarative definition of the Milestones panel, mapped to the in-game menus.</summary>
internal static class MilestoneCatalog
{
    /// <summary>Every placement in the panel (241 placements, 237 unique stats).</summary>
    internal static readonly MilestonePlacement[] Placements =
    [
        // Milestones: Exploration Milestones
        new("^ALIENS_MET", MilestoneTab.Milestones, "milestone.section_exploration_milestones", null, "milestone.alien_encounters", "milestone.alien_encounters", 0, false, null, false),
        new("^WORDS_LEARNT", MilestoneTab.Milestones, "milestone.section_exploration_milestones", null, "milestone.words_collected", "milestone.words_collected", 1, false, null, false),
        new("^DIST_WARP", MilestoneTab.Milestones, "milestone.section_exploration_milestones", null, "milestone.space_exploration", "milestone.space_exploration", 2, false, "milestone.note_hard_to_find", false),
        new("^DISC_ALL_CREATU", MilestoneTab.Milestones, "milestone.section_exploration_milestones", null, "milestone.game_planetary_zoology", "milestone.planet_zoology_scanned", 3, false, "milestone.note_hard_to_find", false),
        // Milestones: Survival Milestones
        new("^DIST_WALKED", MilestoneTab.Milestones, "milestone.section_survival_milestones", null, "milestone.on_foot_exploration", "milestone.on_foot_exploration", 0, false, null, false),
        new("^LONGEST_LIFE_EX", MilestoneTab.Milestones, "milestone.section_survival_milestones", null, "milestone.game_extreme_survival", "milestone.longest_life_ex", 1, false, "milestone.note_hard_to_find", false),
        new("^SENTINEL_KILLS", MilestoneTab.Milestones, "milestone.section_survival_milestones", null, "milestone.sentinels_destroyed", "milestone.sentinels_destroyed", 2, false, null, false),
        new("^MONEY", MilestoneTab.Milestones, "milestone.section_survival_milestones", null, "milestone.game_units_accrued", "milestone.most_units_accrued", 3, false, "milestone.note_hard_to_find", false),
        new("^ENEMIES_KILLED", MilestoneTab.Milestones, "milestone.section_survival_milestones", null, "milestone.ships_destroyed", "milestone.ships_destroyed", 4, false, null, false),
        // Lifeforms: The Gek
        new("^TRA_STANDING", MilestoneTab.Lifeforms, "milestone.section_the_gek", null, "milestone.standing", "milestone.standing", 0, false, null, false),
        new("^TDONE_MISSIONS", MilestoneTab.Lifeforms, "milestone.section_the_gek", null, "milestone.missions_completed", "milestone.missions", 1, false, null, false),
        new("^TSEEN_SYSTEMS", MilestoneTab.Lifeforms, "milestone.section_the_gek", null, "milestone.systems_visited", "milestone.systems_visited", 2, false, null, false),
        new("^SMUGGLE_VALUE", MilestoneTab.Lifeforms, "milestone.section_the_gek", null, "milestone.smuggled_value", "milestone.smuggled_value", 3, false, null, false),
        // Lifeforms: The Vy'keen
        new("^WAR_STANDING", MilestoneTab.Lifeforms, "milestone.section_the_vykeen", null, "milestone.standing", "milestone.standing", 0, false, null, false),
        new("^WDONE_MISSIONS", MilestoneTab.Lifeforms, "milestone.section_the_vykeen", null, "milestone.missions_completed", "milestone.missions", 1, false, null, false),
        new("^WSEEN_SYSTEMS", MilestoneTab.Lifeforms, "milestone.section_the_vykeen", null, "milestone.systems_visited", "milestone.systems_visited", 2, false, null, false),
        new("^WALKERS_KILLED", MilestoneTab.Lifeforms, "milestone.section_the_vykeen", null, "milestone.game_walkers_destroyed", "milestone.sentinel_walkers", 3, false, "milestone.note_hard_to_find", false),
        // Lifeforms: The Korvax
        new("^EXP_STANDING", MilestoneTab.Lifeforms, "milestone.section_the_korvax", null, "milestone.standing", "milestone.standing", 0, false, null, false),
        new("^EDONE_MISSIONS", MilestoneTab.Lifeforms, "milestone.section_the_korvax", null, "milestone.missions_completed", "milestone.missions", 1, false, null, false),
        new("^ESEEN_SYSTEMS", MilestoneTab.Lifeforms, "milestone.section_the_korvax", null, "milestone.systems_visited", "milestone.systems_visited", 2, false, null, false),
        new("^NANITES_EVER", MilestoneTab.Lifeforms, "milestone.section_the_korvax", null, "milestone.game_nanite_clusters", "milestone.nanites_all_time", 3, false, "milestone.note_hard_to_find", false),
        // Lifeforms: The Autophage
        new("^BUI_STANDING", MilestoneTab.Lifeforms, "milestone.section_the_autophage", null, "milestone.standing", "milestone.standing", 0, false, null, false),
        new("^BDONE_MISSIONS", MilestoneTab.Lifeforms, "milestone.section_the_autophage", null, "milestone.missions_completed", "milestone.missions", 1, false, null, false),
        new("^DRONE_SHARDS", MilestoneTab.Lifeforms, "milestone.section_the_autophage", null, "milestone.radiant_shards", "milestone.radiant_shards", 2, false, null, false),
        new("^HEAD_REPAIRS", MilestoneTab.Lifeforms, "milestone.section_the_autophage", null, "milestone.game_autophage_salvage", "milestone.head_repairs", 3, false, "milestone.note_hard_to_find", false),
        // Factions: Merchants Guild
        new("^TGUILD_STAND", MilestoneTab.Factions, "milestone.merchants_guild", null, "milestone.standing", "milestone.standing", 0, true, null, false),
        new("^TGDONE_MISSIONS", MilestoneTab.Factions, "milestone.merchants_guild", null, "milestone.missions_completed", "milestone.missions_completed", 1, true, null, false),
        new("^MONEY", MilestoneTab.Factions, "milestone.merchants_guild", null, "milestone.game_units_earned", "milestone.most_units_accrued", 2, false, "milestone.note_hard_to_find", false),
        new("^PLANTS_PLANTED", MilestoneTab.Factions, "milestone.merchants_guild", null, "milestone.plants_farmed", "milestone.plants_farmed", 3, true, null, false),
        new("^PROC_PRODS", MilestoneTab.Factions, "milestone.merchants_guild", null, "milestone.proc_prods", "milestone.proc_prods", 4, true, null, false),
        // Factions: Mercenaries Guild
        new("^WGUILD_STAND", MilestoneTab.Factions, "milestone.mercenaries_guild", null, "milestone.standing", "milestone.standing", 0, true, null, false),
        new("^WGDONE_MISSIONS", MilestoneTab.Factions, "milestone.mercenaries_guild", null, "milestone.missions_completed", "milestone.missions_completed", 1, true, null, false),
        new("^SENTINEL_KILLS", MilestoneTab.Factions, "milestone.mercenaries_guild", null, "milestone.sentinels_destroyed", "milestone.sentinels_destroyed", 2, false, null, false),
        new("^PIRATES_KILLED", MilestoneTab.Factions, "milestone.mercenaries_guild", null, "milestone.pirates", "milestone.pirates", 3, true, null, false),
        new("^FIENDS_KILLED", MilestoneTab.Factions, "milestone.mercenaries_guild", null, "milestone.fiends", "milestone.fiends", 4, true, null, false),
        // Factions: Explorers Guild
        new("^EGUILD_STAND", MilestoneTab.Factions, "milestone.explorers_guild", null, "milestone.standing", "milestone.standing", 0, true, null, false),
        new("^EGDONE_MISSIONS", MilestoneTab.Factions, "milestone.explorers_guild", null, "milestone.missions_completed", "milestone.missions_completed", 1, true, null, false),
        new("^RARE_SCANNED", MilestoneTab.Factions, "milestone.explorers_guild", null, "milestone.rare_creatures", "milestone.rare_creatures", 2, true, null, false),
        new("^DIST_WARP", MilestoneTab.Factions, "milestone.explorers_guild", null, "milestone.game_systems_mapped", "milestone.space_exploration", 3, false, "milestone.note_hard_to_find", false),
        new("^DISC_FLORA", MilestoneTab.Factions, "milestone.explorers_guild", null, "milestone.flora_discovered", "milestone.flora_discovered", 4, true, null, false),
        // Factions: Arena League
        new("^PB_BOSS_WINS", MilestoneTab.Factions, "milestone.section_arena_league", null, "milestone.game_champions_defeated", "milestone.pb_boss_wins", 0, false, "milestone.note_hard_to_find", false),
        new("^PB_WINS", MilestoneTab.Factions, "milestone.section_arena_league", null, "milestone.game_holo_arena_victories", "milestone.pb_wins", 1, false, "milestone.note_hard_to_find", false),
        new("^PB_PETS_MAXED", MilestoneTab.Factions, "milestone.section_arena_league", null, "milestone.game_apex_companions", "milestone.pb_maxed_pets", 2, false, "milestone.note_hard_to_find", false),
        new("^PB_D_NEXUS", MilestoneTab.Factions, "milestone.section_arena_league", null, "milestone.game_iteration_oceanus", "milestone.pb_nexus", 3, false, "milestone.note_hard_to_find", false),
        new("^EGGS_HATCHED", MilestoneTab.Factions, "milestone.section_arena_league", null, "milestone.game_hatch_eggs", "milestone.eggs_hatched", 4, false, "milestone.note_hard_to_find", false),
        new("^PB_CHALL_WINS", MilestoneTab.Factions, "milestone.section_arena_league", null, "milestone.pb_challenge_hall_wins", "milestone.pb_challenge_hall_wins", 5, false, "milestone.note_editor_only", true),
        new("^PB_LOSSES", MilestoneTab.Factions, "milestone.section_arena_league", null, "milestone.pb_losses", "milestone.pb_losses", 6, false, "milestone.note_editor_only", true),
        new("^PETS_OWNED", MilestoneTab.Factions, "milestone.section_arena_league", null, "milestone.pets_owned", "milestone.pets_owned", 7, false, "milestone.note_editor_only", true),
        new("^PET_LEVEL_SPENT", MilestoneTab.Factions, "milestone.section_arena_league", null, "milestone.pet_levels_spent", "milestone.pet_levels_spent", 8, false, "milestone.note_editor_only", true),
        // Factions: Outlaws
        new("^PIRATE_STAND", MilestoneTab.Factions, "milestone.outlaws", null, "milestone.standing", "milestone.standing", 0, true, null, false),
        new("^PIRATE_MISSIONS", MilestoneTab.Factions, "milestone.outlaws", null, "milestone.missions_completed", "milestone.missions_completed", 1, true, null, false),
        new("^SMUGGLE_VALUE", MilestoneTab.Factions, "milestone.outlaws", null, "milestone.smuggled_value", "milestone.smuggled_value", 2, true, null, false),
        new("^BOUNTIES", MilestoneTab.Factions, "milestone.outlaws", null, "milestone.bounties", "milestone.bounties", 3, true, null, false),
        new("^FPODS_BROKEN", MilestoneTab.Factions, "milestone.outlaws", null, "milestone.game_freighters_plundered", "milestone.pods_broken", 4, false, "milestone.note_hard_to_find", false),
        // OtherStats: General (16)
        new("^EXTREME_WALK", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_survival", "milestone.game_extreme_walk", "milestone.extreme_survival", 0, false, "milestone.note_name_trap", false),
        new("^LONGEST_LIFE", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_survival", "milestone.longest_life", "milestone.longest_life", 1, false, "milestone.note_name_trap", false),
        new("^STORM_WALK", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_survival", "milestone.storm_survival", "milestone.storm_survival", 2, false, null, false),
        new("^DEATHS", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_survival", "milestone.total_deaths", "milestone.total_deaths", 3, false, null, false),
        new("^PLAY_SESSIONS", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_time", "milestone.play_sessions", "milestone.play_sessions", 0, false, null, false),
        new("^APP_SESSIONS", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_time", "milestone.app_sessions", "milestone.app_sessions", 1, false, null, false),
        new("^SPACE_TIME", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_time", "milestone.time_in_space", "milestone.time_in_space", 2, false, "milestone.note_name_trap", false),
        new("^TIMES_IN_SPACE", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_time", "milestone.times_in_space", "milestone.times_in_space", 3, false, "milestone.note_name_trap", false),
        new("^TIME", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_time", "milestone.total_play_time", "milestone.total_play_time", 4, false, null, false),
        new("^PHOTO_MODE_USED", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_modes", "milestone.photo_mode_used", "milestone.photo_mode_used", 0, false, null, false),
        new("^VR_GRABS", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_modes", "milestone.vr_grabs", "milestone.vr_grabs", 1, false, null, false),
        new("^VR_INIT", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_modes", "milestone.vr_inits", "milestone.vr_inits", 2, false, null, false),
        new("^VR_SNAPTURNS", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_modes", "milestone.vr_snapturns", "milestone.vr_snapturns", 3, false, null, false),
        new("^DICE_GAME_LOST", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_activities", "milestone.dice_games_lost", "milestone.dice_games_lost", 0, false, null, false),
        new("^DICE_GAME_WON", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_activities", "milestone.dice_games_won", "milestone.dice_games_won", 1, false, null, false),
        new("^MEDITATION", MilestoneTab.OtherStats, "milestone.section_general", "milestone.subcat_activities", "milestone.meditation", "milestone.meditation", 2, false, null, false),
        // OtherStats: Combat (34)
        new("^CREATURES_KILL", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_creatures", "milestone.creatures_killed", "milestone.creatures_killed", 0, false, null, false),
        new("^FISH_KILLS", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_creatures", "milestone.fish_killed", "milestone.fish_killed", 1, false, null, false),
        new("^FLORA_KILLED", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_creatures", "milestone.flora_killed", "milestone.flora_killed", 2, false, null, false),
        new("^GRUBS_KILLED", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_creatures", "milestone.grubs", "milestone.grubs", 3, false, null, false),
        new("^JELLYBOSS", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_creatures", "milestone.jellyfish_boss_1", "milestone.jellyfish_boss_1", 4, false, null, false),
        new("^S20_JELLYBOSS", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_creatures", "milestone.jellyfish_boss_2", "milestone.jellyfish_boss_2", 5, false, null, false),
        new("^MINIWORM_KILL", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_creatures", "milestone.miniworms", "milestone.miniworms", 6, false, null, false),
        new("^PREDS_KILLED", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_creatures", "milestone.predators", "milestone.predators", 7, false, null, false),
        new("^QUEENS_KILLED", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_creatures", "milestone.queens", "milestone.queens", 8, false, null, false),
        new("^ROAD_KILL", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_creatures", "milestone.road_kill", "milestone.road_kill", 9, false, null, false),
        new("^SPIDERS_KILLED", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_creatures", "milestone.spiders", "milestone.spiders", 10, false, null, false),
        new("^SPOOKBOSS", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_creatures", "milestone.spookfiend_boss", "milestone.spookfiend_boss", 11, false, null, false),
        new("^SPOOK_JUICE", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_creatures", "milestone.spookfiend_juice", "milestone.spookfiend_juice", 12, false, null, false),
        new("^SPOOK_KILLS", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_creatures", "milestone.spookfiends", "milestone.spookfiends", 13, false, null, false),
        new("^MECHS_KILLED", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_sentinels", "milestone.mechs", "milestone.mechs", 0, false, null, false),
        new("^DRONES_KILLED", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_sentinels", "milestone.sentinel_drones", "milestone.sentinel_drones", 1, false, null, false),
        new("^SENTFREI_KILLED", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_sentinels", "milestone.sentinel_freighters", "milestone.sentinel_freighters", 2, false, null, false),
        new("^QUADS_KILLED", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_sentinels", "milestone.sentinel_quads", "milestone.sentinel_quads", 3, false, null, false),
        new("^STONE_KILLS", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_sentinels", "milestone.stone_guardians", "milestone.stone_guardians", 4, false, null, false),
        new("^ASTEROIDS", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_space", "milestone.asteroids_destroyed", "milestone.asteroids_destroyed", 0, false, null, false),
        new("^CIV_FREI_KILLS", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_space", "milestone.civilian_freighters", "milestone.civilian_freighters", 1, false, null, false),
        new("^PIR_FREI_WINS", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_space", "milestone.pirate_freighters_destroyed", "milestone.pirate_freighters_destroyed", 2, false, null, false),
        new("^SPACE_BATTLES", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_space", "milestone.space_battles", "milestone.space_battles", 3, false, null, false),
        new("^PIR_FREI_SEEN", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_piracy", "milestone.pirate_freighters_seen", "milestone.pirate_freighters_seen", 0, false, null, false),
        new("^MISSION_PIRATES", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_piracy", "milestone.pirate_missions_req", "milestone.pirate_missions_req", 1, false, null, false),
        new("^PDONE_MISSIONS", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_piracy", "milestone.pirate_missions_done", "milestone.pirate_missions_done", 2, false, null, false),
        new("^PIRATE_MYSTERY", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_piracy", "milestone.pirate_mysteries", "milestone.pirate_mysteries", 3, false, null, false),
        new("^PIRATE_SYSTEMS", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_piracy", "milestone.pirate_systems_visited", "milestone.pirate_systems_visited", 4, false, null, false),
        new("^POLICE_KILLED", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_piracy", "milestone.police", "milestone.police", 5, false, null, false),
        new("^POLICE_SUMMON", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_piracy", "milestone.police_summons", "milestone.police_summons", 6, false, null, false),
        new("^TRADERS_KILLED", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_piracy", "milestone.traders_killed", "milestone.traders_killed", 7, true, null, false),
        new("^AMMO_FIRED", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_weaponry", "milestone.ammo_fired", "milestone.ammo_fired", 0, false, null, false),
        new("^KILLS_IN_MECH", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_weaponry", "milestone.kills_in_mech", "milestone.kills_in_mech", 1, false, null, false),
        new("^GUNSLOTREPAIRS", MilestoneTab.OtherStats, "milestone.section_combat", "milestone.subcat_weaponry", "milestone.weapon_repairs", "milestone.weapon_repairs", 2, false, null, false),
        // OtherStats: Movement (15)
        new("^DIST_EXO", MilestoneTab.OtherStats, "milestone.section_movement", "milestone.subcat_distance", "milestone.distance_exocraft", "milestone.distance_exocraft", 0, false, null, false),
        new("^DIST_FLY", MilestoneTab.OtherStats, "milestone.section_movement", "milestone.subcat_distance", "milestone.distance_flying", "milestone.distance_flying", 1, false, null, false),
        new("^DIST_SPACE", MilestoneTab.OtherStats, "milestone.section_movement", "milestone.subcat_distance", "milestone.distance_in_space", "milestone.distance_in_space", 2, false, null, false),
        new("^DIST_JETPACK", MilestoneTab.OtherStats, "milestone.section_movement", "milestone.subcat_distance", "milestone.distance_jetpack", "milestone.distance_jetpack", 3, false, null, false),
        new("^DIST_PULSE", MilestoneTab.OtherStats, "milestone.section_movement", "milestone.subcat_distance", "milestone.distance_pulse", "milestone.distance_pulse", 4, false, null, false),
        new("^DIST_SUB", MilestoneTab.OtherStats, "milestone.section_movement", "milestone.subcat_distance", "milestone.distance_submarine", "milestone.distance_submarine", 5, false, null, false),
        new("^TUNNELLED", MilestoneTab.OtherStats, "milestone.section_movement", "milestone.subcat_distance", "milestone.tunnelled_distance", "milestone.tunnelled_distance", 6, false, null, false),
        new("^SPACE_WALK", MilestoneTab.OtherStats, "milestone.section_movement", "milestone.subcat_walking", "milestone.space_walks", "milestone.space_walks", 0, false, null, false),
        new("^EX_TOX_WALK", MilestoneTab.OtherStats, "milestone.section_movement", "milestone.subcat_walking", "milestone.walked_in_toxic", "milestone.walked_in_toxic", 1, false, null, false),
        new("^ACROBAT", MilestoneTab.OtherStats, "milestone.section_movement", "milestone.subcat_flight", "milestone.acrobat", "milestone.acrobat", 0, false, null, false),
        new("^BLACKHOLE_WALKS", MilestoneTab.OtherStats, "milestone.section_movement", "milestone.subcat_warp", "milestone.black_hole_walks", "milestone.black_hole_walks", 0, false, null, false),
        new("^BLACKHOLE_WARPS", MilestoneTab.OtherStats, "milestone.section_movement", "milestone.subcat_warp", "milestone.black_hole_warps", "milestone.black_hole_warps", 1, false, null, false),
        new("^EARLY_WARPS", MilestoneTab.OtherStats, "milestone.section_movement", "milestone.subcat_warp", "milestone.early_warps", "milestone.early_warps", 2, false, null, false),
        new("^ITEMS_TELEPRT", MilestoneTab.OtherStats, "milestone.section_movement", "milestone.subcat_warp", "milestone.items_teleported", "milestone.items_teleported", 3, false, null, false),
        new("^PORTAL_WARPS", MilestoneTab.OtherStats, "milestone.section_movement", "milestone.subcat_warp", "milestone.portal_warps", "milestone.portal_warps", 4, false, null, false),
        // OtherStats: Exploration (25)
        new("^ABAND_FREIGHTER", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_discovery", "milestone.abandoned_freighters", "milestone.abandoned_freighters", 0, false, null, false),
        new("^ARTIFACT_HINTS", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_discovery", "milestone.artifact_hints", "milestone.artifact_hints", 1, false, null, false),
        new("^ATLAS_LOOPS", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_discovery", "milestone.atlas_loops", "milestone.atlas_loops", 2, false, null, false),
        new("^BASECOMP_LORE", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_discovery", "milestone.basecamp_lore", "milestone.basecamp_lore", 3, false, null, false),
        new("^CAVE_WALK", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_discovery", "milestone.cave_exploration", "milestone.cave_exploration", 4, false, null, false),
        new("^DISC_CREATURES", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_discovery", "milestone.creatures_discovered", "milestone.creatures_discovered", 5, false, null, false),
        new("^DISC_MINERALS", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_discovery", "milestone.minerals_discovered", "milestone.minerals_discovered", 6, false, null, false),
        new("^DISC_PLANETS", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_discovery", "milestone.planets_discovered", "milestone.planets_discovered", 7, false, null, false),
        new("^VISIT_PLANETS", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_discovery", "milestone.planets_visited", "milestone.planets_visited", 8, false, null, false),
        new("^ANALYSE_PROP", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_discovery", "milestone.props_analysed", "milestone.props_analysed", 9, false, null, false),
        new("^SPACE_POI", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_discovery", "milestone.space_pois", "milestone.space_pois", 10, false, null, false),
        new("^DISC_SYSTEMS", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_discovery", "milestone.systems_discovered", "milestone.systems_discovered", 11, false, null, false),
        new("^DISC_WAYPOINTS", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_discovery", "milestone.waypoints_discovered", "milestone.waypoints_discovered", 12, false, null, false),
        new("^BONES_FOUND", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_excavation", "milestone.bones_found", "milestone.bones_found", 0, false, null, false),
        new("^EXCAVATED", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_excavation", "milestone.excavated", "milestone.excavated", 1, false, null, false),
        new("^FOS_MADE", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_excavation", "milestone.fossils_made", "milestone.fossils_made", 2, false, null, false),
        new("^RUINS_LOOTED", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_excavation", "milestone.ruins_looted", "milestone.ruins_looted", 3, false, null, false),
        new("^SALVAGE_LOOTED", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_excavation", "milestone.salvage_looted", "milestone.salvage_looted", 4, false, null, false),
        new("^TREASURE_FOUND", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_excavation", "milestone.treasure_found", "milestone.treasure_found", 5, false, "milestone.note_name_trap", false),
        new("^BUI_MET", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_encounters", "milestone.autophage_met", "milestone.autophage_met", 0, false, null, false),
        new("^TRA_MET", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_encounters", "milestone.gek_met", "milestone.gek_met", 1, false, null, false),
        new("^GIFTS_GIVEN", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_encounters", "milestone.gifts_given", "milestone.gifts_given", 2, false, null, false),
        new("^EXP_MET", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_encounters", "milestone.korvax_met", "milestone.korvax_met", 3, false, null, false),
        new("^NPCS_RESCUED", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_encounters", "milestone.npcs_rescued", "milestone.npcs_rescued", 4, false, null, false),
        new("^WAR_MET", MilestoneTab.OtherStats, "milestone.section_exploration", "milestone.subcat_encounters", "milestone.vykeen_met", "milestone.vykeen_met", 5, false, null, false),
        // OtherStats: Resources (14)
        new("^JM", MilestoneTab.OtherStats, "milestone.section_resources", "milestone.subcat_gathering", "milestone.junk_metal", "milestone.junk_metal", 0, false, null, false),
        new("^JM_BANKED", MilestoneTab.OtherStats, "milestone.section_resources", "milestone.subcat_gathering", "milestone.junk_metal_banked", "milestone.junk_metal_banked", 1, false, null, false),
        new("^PLANTS_GATHERED", MilestoneTab.OtherStats, "milestone.section_resources", "milestone.subcat_gathering", "milestone.plants_gathered", "milestone.plants_gathered", 2, false, null, false),
        new("^RES_EXTRACTED", MilestoneTab.OtherStats, "milestone.section_resources", "milestone.subcat_gathering", "milestone.resources_extracted", "milestone.resources_extracted", 3, false, null, false),
        new("^STORM_CRYSTALS", MilestoneTab.OtherStats, "milestone.section_resources", "milestone.subcat_gathering", "milestone.storm_crystals", "milestone.storm_crystals", 4, false, null, false),
        new("^NANITES", MilestoneTab.OtherStats, "milestone.section_resources", "milestone.subcat_economy", "milestone.nanites", "milestone.nanites", 0, false, "milestone.note_name_trap", false),
        new("^QS_SPENT", MilestoneTab.OtherStats, "milestone.section_resources", "milestone.subcat_economy", "milestone.quicksilver_spent", "milestone.quicksilver_spent", 1, false, null, false),
        new("^MONEY_EVER", MilestoneTab.OtherStats, "milestone.section_resources", "milestone.subcat_economy", "milestone.units_all_time", "milestone.units_all_time", 2, false, "milestone.note_name_trap", false),
        new("^FISH_BOOT", MilestoneTab.OtherStats, "milestone.section_resources", "milestone.subcat_fishing", "milestone.boots_fished", "milestone.boots_fished", 0, false, null, false),
        new("^FISH_CASH", MilestoneTab.OtherStats, "milestone.section_resources", "milestone.subcat_fishing", "milestone.fish_cash", "milestone.fish_cash", 1, false, null, false),
        new("^FISH_CAUGHT", MilestoneTab.OtherStats, "milestone.section_resources", "milestone.subcat_fishing", "milestone.fish_caught", "milestone.fish_caught", 2, false, null, false),
        new("^FISH_RELEASED", MilestoneTab.OtherStats, "milestone.section_resources", "milestone.subcat_fishing", "milestone.fish_released", "milestone.fish_released", 3, false, null, false),
        new("^FISH_TRAPPED", MilestoneTab.OtherStats, "milestone.section_resources", "milestone.subcat_fishing", "milestone.fish_trapped", "milestone.fish_trapped", 4, false, null, false),
        new("^FISH_LEGEND", MilestoneTab.OtherStats, "milestone.section_resources", "milestone.subcat_fishing", "milestone.legendary_fish", "milestone.legendary_fish", 5, false, null, false),
        // OtherStats: Companions (7)
        new("^EGG_PODS", MilestoneTab.OtherStats, "milestone.section_companions", "milestone.subcat_eggs", "milestone.egg_pods", "milestone.egg_pods", 0, false, null, false),
        new("^EGGS_MODDED", MilestoneTab.OtherStats, "milestone.section_companions", "milestone.subcat_eggs", "milestone.eggs_modified", "milestone.eggs_modified", 1, false, null, false),
        new("^EGGS_GOT", MilestoneTab.OtherStats, "milestone.section_companions", "milestone.subcat_eggs", "milestone.eggs_received", "milestone.eggs_received", 2, false, null, false),
        new("^FIEND_EGG", MilestoneTab.OtherStats, "milestone.section_companions", "milestone.subcat_eggs", "milestone.fiend_eggs", "milestone.fiend_eggs", 3, false, null, false),
        new("^CREATURES_FED", MilestoneTab.OtherStats, "milestone.section_companions", "milestone.subcat_husbandry", "milestone.creatures_fed", "milestone.creatures_fed", 0, false, null, false),
        new("^PETS_ADOPTED", MilestoneTab.OtherStats, "milestone.section_companions", "milestone.subcat_husbandry", "milestone.pets_adopted", "milestone.pets_adopted", 1, false, null, false),
        new("^POOP_COLLECTED", MilestoneTab.OtherStats, "milestone.section_companions", "milestone.subcat_husbandry", "milestone.poop_collected", "milestone.poop_collected", 2, false, null, false),
        // OtherStats: Construction (6)
        new("^BASEPARTS_GOT", MilestoneTab.OtherStats, "milestone.section_construction", "milestone.subcat_bases", "milestone.base_parts_got", "milestone.base_parts_got", 0, false, null, false),
        new("^PARTS_PLACED", MilestoneTab.OtherStats, "milestone.section_construction", "milestone.subcat_bases", "milestone.parts_placed", "milestone.parts_placed", 1, false, null, false),
        new("^JUDGEMENTS", MilestoneTab.OtherStats, "milestone.section_construction", "milestone.subcat_bases", "milestone.settlement_judgements", "milestone.settlement_judgements", 2, false, null, false),
        new("^BIGGS_PART_GOT", MilestoneTab.OtherStats, "milestone.section_construction", "milestone.subcat_fleet", "milestone.corvette_parts", "milestone.corvette_parts", 0, false, null, false),
        new("^FRIGATES", MilestoneTab.OtherStats, "milestone.section_construction", "milestone.subcat_fleet", "milestone.frigates", "milestone.frigates", 1, false, null, false),
        new("^SHIPS_BOUGHT", MilestoneTab.OtherStats, "milestone.section_construction", "milestone.subcat_fleet", "milestone.ships_bought", "milestone.ships_bought", 2, false, null, false),
        // OtherStats: Equipment (4)
        new("^GRAV_THROW", MilestoneTab.OtherStats, "milestone.section_equipment", "milestone.subcat_gravity", "milestone.grav_throws", "milestone.grav_throws", 0, false, null, false),
        new("^GRAVBALLS", MilestoneTab.OtherStats, "milestone.section_equipment", "milestone.subcat_gravity", "milestone.gravitino_balls", "milestone.gravitino_balls", 1, false, null, false),
        new("^GRAV_GRAB", MilestoneTab.OtherStats, "milestone.section_equipment", "milestone.subcat_gravity", "milestone.gravity_grabs", "milestone.gravity_grabs", 2, false, null, false),
        new("^GRAV_PUSH", MilestoneTab.OtherStats, "milestone.section_equipment", "milestone.subcat_gravity", "milestone.gravity_pushes", "milestone.gravity_pushes", 3, false, null, false),
        // OtherStats: Discoveries (Planets)
        new("^DISC_ABAND", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.disc_abandoned", "milestone.disc_abandoned", 0, false, null, false),
        new("^DISC_P_COLD", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.disc_cold", "milestone.disc_cold", 1, false, null, false),
        new("^DISC_P_DEAD", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.disc_dead", "milestone.disc_dead", 2, false, null, false),
        new("^DISC_P_DUST", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.disc_dust", "milestone.disc_dust", 3, false, null, false),
        new("^DISC_P_GAS", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.disc_gas", "milestone.disc_gas", 4, false, null, false),
        new("^DISC_P_HOT", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.disc_hot", "milestone.disc_hot", 5, false, null, false),
        new("^DISC_P_LAVA", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.disc_lava", "milestone.disc_lava", 6, false, null, false),
        new("^DISC_P_LUSH", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.disc_lush", "milestone.disc_lush", 7, false, null, false),
        new("^DISC_P_RAD", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.disc_radioactive", "milestone.disc_radioactive", 8, false, null, false),
        new("^DISC_P_RGB", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.disc_rgb", "milestone.disc_rgb", 9, false, null, false),
        new("^DISC_P_SWAMP", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.disc_swamp", "milestone.disc_swamp", 10, false, null, false),
        new("^DISC_P_TOX", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.disc_toxic", "milestone.disc_toxic", 11, false, null, false),
        new("^DISC_P_WATER", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.disc_water", "milestone.disc_water", 12, false, null, false),
        new("^DISC_P_WEIRD", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.disc_weird", "milestone.disc_weird", 13, false, null, false),
        new("^DISC_RARE_SYS", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.disc_rare_system", "milestone.disc_rare_system", 14, false, null, false),
        new("^VISIT_COLD", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.visit_cold", "milestone.visit_cold", 15, false, null, false),
        new("^VISIT_DEAD", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.visit_dead", "milestone.visit_dead", 16, false, null, false),
        new("^VISIT_DUST", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.visit_dust", "milestone.visit_dust", 17, false, null, false),
        new("^VISIT_GAS", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.visit_gas", "milestone.visit_gas", 18, false, null, false),
        new("^VISIT_HOT", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.visit_hot", "milestone.visit_hot", 19, false, null, false),
        new("^VISIT_LAVA", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.visit_lava", "milestone.visit_lava", 20, false, null, false),
        new("^VISIT_LUSH", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.visit_lush", "milestone.visit_lush", 21, false, null, false),
        new("^VISIT_RAD", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.visit_radioactive", "milestone.visit_radioactive", 22, false, null, false),
        new("^VISIT_RGB", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.visit_rgb", "milestone.visit_rgb", 23, false, null, false),
        new("^VISIT_SWAMP", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.visit_swamp", "milestone.visit_swamp", 24, false, null, false),
        new("^VISIT_TOX", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.visit_toxic", "milestone.visit_toxic", 25, false, null, false),
        new("^VISIT_WATER", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.visit_water", "milestone.visit_water", 26, false, null, false),
        new("^VISIT_WEIRD", MilestoneTab.OtherStats, "milestone.section_disc_planets", null, "milestone.visit_weird", "milestone.visit_weird", 27, false, null, false),
        // OtherStats: Discoveries (Creatures)
        new("^DISC_CRE_AGGRO", MilestoneTab.OtherStats, "milestone.section_disc_creatures", null, "milestone.disc_cre_aggressive", "milestone.disc_cre_aggressive", 0, false, null, false),
        new("^DISC_CRE_AIR", MilestoneTab.OtherStats, "milestone.section_disc_creatures", null, "milestone.disc_cre_flying", "milestone.disc_cre_flying", 1, false, null, false),
        new("^DISC_CRE_CAVE", MilestoneTab.OtherStats, "milestone.section_disc_creatures", null, "milestone.disc_cre_cave", "milestone.disc_cre_cave", 2, false, null, false),
        new("^DISC_CRE_DISS", MilestoneTab.OtherStats, "milestone.section_disc_creatures", null, "milestone.disc_cre_dissonant", "milestone.disc_cre_dissonant", 3, false, null, false),
        new("^DISC_CRE_LAND", MilestoneTab.OtherStats, "milestone.section_disc_creatures", null, "milestone.disc_cre_land", "milestone.disc_cre_land", 4, false, null, false),
        new("^DISC_CRE_ROBOT", MilestoneTab.OtherStats, "milestone.section_disc_creatures", null, "milestone.disc_cre_robot", "milestone.disc_cre_robot", 5, false, null, false),
        new("^DISC_CRE_WATER", MilestoneTab.OtherStats, "milestone.section_disc_creatures", null, "milestone.disc_cre_water", "milestone.disc_cre_water", 6, false, null, false),
        new("^DISC_CRE_WEIRD", MilestoneTab.OtherStats, "milestone.section_disc_creatures", null, "milestone.disc_cre_weird", "milestone.disc_cre_weird", 7, false, null, false),
        new("^DISC_STRIDERGLO", MilestoneTab.OtherStats, "milestone.section_disc_creatures", null, "milestone.disc_glowing_strider", "milestone.disc_glowing_strider", 8, false, null, false),
        // OtherStats: Multiplayer
        new("^MP_DEPOT_DONE", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.mp_depots_done", "milestone.mp_depots_done", 0, false, null, false),
        new("^MP_DEPOT_HACK", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.mp_depots_hacked", "milestone.mp_depots_hacked", 1, false, null, false),
        new("^MP_EVENT_COUNT", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.mp_events", "milestone.mp_events", 2, false, null, false),
        new("^MP_FISH_COUNT", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.mp_fish", "milestone.mp_fish", 3, false, null, false),
        new("^MP_FULL_COUNT", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.mp_full_session_count", "milestone.mp_full_session_count", 4, false, null, false),
        new("^MP_FULL_TIME", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.mp_full_time_spent", "milestone.mp_full_time_spent", 5, false, null, false),
        new("^MP_MIS_ACCESS", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.mp_missions_accessed", "milestone.mp_missions_accessed", 6, false, null, false),
        new("^MP_MIS_STARTED", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.mp_missions_started", "milestone.mp_missions_started", 7, false, null, false),
        new("^MP_ORB_COUNT", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.mp_orb_count", "milestone.mp_orb_count", 8, false, null, false),
        new("^MP_ORB_TIME", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.mp_orb_time", "milestone.mp_orb_time", 9, false, null, false),
        new("^MP_PIRATES_WAVE", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.mp_pirate_waves", "milestone.mp_pirate_waves", 10, false, null, false),
        new("^MP_PQ_RMARKER", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.mp_planet_quest_markers", "milestone.mp_planet_quest_markers", 11, false, null, false),
        new("^MP_PQ_WSTONES", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.mp_planet_quest_stones", "milestone.mp_planet_quest_stones", 12, false, null, false),
        new("^MP_REP_FAILS", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.mp_rep_fails", "milestone.mp_rep_fails", 13, false, null, false),
        new("^MP_SESSIONS", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.mp_sessions", "milestone.mp_sessions", 14, false, null, false),
        new("^NEXUS_MISSIONS", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.nexus_missions", "milestone.nexus_missions", 15, false, null, false),
        new("^NEXUS_MISS_PQ", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.nexus_planet_quests", "milestone.nexus_planet_quests", 16, false, null, false),
        new("^NEXUS_MISS_QS", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.nexus_qs_missions", "milestone.nexus_qs_missions", 17, false, null, false),
        new("^NEXUS_STAND", MilestoneTab.OtherStats, "milestone.section_multiplayer", null, "milestone.nexus_standing", "milestone.nexus_standing", 18, false, null, false),
        // OtherStats: Travel
        new("^DIST_BIGGS", MilestoneTab.OtherStats, "milestone.section_travel", null, "milestone.dist_any_corvette", "milestone.dist_any_corvette", 0, false, null, false),
        new("^DIST_CRE_RIDE", MilestoneTab.OtherStats, "milestone.section_travel", null, "milestone.dist_creature", "milestone.dist_creature", 1, false, null, false),
        new("^DIST_MY_BIGGS", MilestoneTab.OtherStats, "milestone.section_travel", null, "milestone.dist_own_corvette", "milestone.dist_own_corvette", 2, false, null, false),
        new("^DIST_OTH_BIGGS", MilestoneTab.OtherStats, "milestone.section_travel", null, "milestone.dist_other_corvette", "milestone.dist_other_corvette", 3, false, null, false),
        new("^DIST_PET_FLY", MilestoneTab.OtherStats, "milestone.section_travel", null, "milestone.dist_flying_pet", "milestone.dist_flying_pet", 4, false, null, false),
        new("^DIST_PET_RIDE", MilestoneTab.OtherStats, "milestone.section_travel", null, "milestone.dist_pet", "milestone.dist_pet", 5, false, null, false),
        new("^DIST_SWAM", MilestoneTab.OtherStats, "milestone.section_travel", null, "milestone.dist_swam", "milestone.dist_swam", 6, false, null, false),
        new("^EX_COLD_WALK", MilestoneTab.OtherStats, "milestone.section_travel", null, "milestone.walked_in_cold", "milestone.walked_in_cold", 7, false, null, false),
        new("^EX_HOT_WALK", MilestoneTab.OtherStats, "milestone.section_travel", null, "milestone.walked_in_heat", "milestone.walked_in_heat", 8, false, null, false),
        new("^EX_RAD_WALK", MilestoneTab.OtherStats, "milestone.section_travel", null, "milestone.walked_in_radiation", "milestone.walked_in_radiation", 9, false, null, false),
    ];

    private static readonly IReadOnlyDictionary<string, MilestoneStat> _stats = BuildStats();
    private static readonly HashSet<string> _guildLeveled = BuildGuildLeveled();

    /// <summary>Unique stats keyed by save stat ID.</summary>
    internal static IReadOnlyDictionary<string, MilestoneStat> Stats => _stats;

    /// <summary>Section icons keyed by section localization key.</summary>
    internal static readonly IReadOnlyDictionary<string, string> SectionIcons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["milestone.section_exploration_milestones"] = "UI-MILESTONES.PNG",
        ["milestone.section_survival_milestones"] = "UI-MILESTONES.PNG",
        ["milestone.section_the_gek"] = "UI-GEK.PNG",
        ["milestone.section_the_vykeen"] = "UI-VYKEEN.PNG",
        ["milestone.section_the_korvax"] = "UI-KORVAX.PNG",
        ["milestone.section_the_autophage"] = "UI-BUILDERS.PNG",
        ["milestone.merchants_guild"] = "UI-TRADERS.PNG",
        ["milestone.mercenaries_guild"] = "UI-WARRIORS.PNG",
        ["milestone.explorers_guild"] = "UI-EXPLORERS.PNG",
        ["milestone.section_arena_league"] = "UI-PERK.PNG",
        ["milestone.outlaws"] = "UI-PIRATE.PNG",
        ["milestone.section_general"] = "UI-PERK.PNG",
        ["milestone.section_combat"] = "UI-SENT.PNG",
        ["milestone.section_movement"] = "UI-MILESTONES.PNG",
        ["milestone.section_exploration"] = "UI-MILESTONES.PNG",
        ["milestone.section_resources"] = "UI-PERK.PNG",
        ["milestone.section_companions"] = "UI-PERK.PNG",
        ["milestone.section_construction"] = "UI-PERK.PNG",
        ["milestone.section_equipment"] = "UI-PERK.PNG",
        ["milestone.section_disc_planets"] = "UI-MILESTONES.PNG",
        ["milestone.section_disc_creatures"] = "UI-MILESTONES.PNG",
        ["milestone.section_multiplayer"] = "UI-PERK.PNG",
        ["milestone.section_travel"] = "UI-MILESTONES.PNG",
    };

    /// <summary>Tab label keys in display order.</summary>
    internal static readonly MilestoneTab[] TabOrder =
    [
        MilestoneTab.Milestones,
        MilestoneTab.Lifeforms,
        MilestoneTab.Factions,
        MilestoneTab.OtherStats,
    ];

    /// <summary>Returns the localization key for a tab label.</summary>
    internal static string TabLabelKey(MilestoneTab tab) => tab switch
    {
        MilestoneTab.Milestones => "milestone.tab_milestones",
        MilestoneTab.Lifeforms => "milestone.tab_lifeforms",
        MilestoneTab.Factions => "milestone.tab_factions",
        _ => "milestone.tab_other",
    };

    /// <summary>Returns the icon file for a section, or null.</summary>
    internal static string? IconForSection(string sectionKey) =>
        SectionIcons.TryGetValue(sectionKey, out string? icon) ? icon : null;

    /// <summary>Returns section keys for a tab in menu order.</summary>
    internal static IEnumerable<string> GetSections(MilestoneTab tab)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var placement in Placements)
        {
            if (placement.Tab == tab && seen.Add(placement.SectionKey))
                yield return placement.SectionKey;
        }
    }

    /// <summary>Returns placements for a section, in catalog order.</summary>
    internal static IEnumerable<MilestonePlacement> GetSectionPlacements(
        MilestoneTab tab, string sectionKey)
    {
        foreach (var placement in Placements)
        {
            if (placement.Tab == tab && placement.SectionKey == sectionKey)
                yield return placement;
        }
    }

    /// <summary>Returns subcategory keys for a section in catalog order.</summary>
    internal static IEnumerable<string> GetSubcategories(MilestoneTab tab, string sectionKey)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var placement in GetSectionPlacements(tab, sectionKey))
        {
            if (placement.SubcategoryKey != null && seen.Add(placement.SubcategoryKey))
                yield return placement.SubcategoryKey;
        }
    }

    /// <summary>Returns the first placement for a stat ID, or null.</summary>
    internal static MilestonePlacement? FindFirst(string statId) =>
        _stats.TryGetValue(statId, out MilestoneStat? stat) && stat.Placements.Count > 0
            ? stat.Placements[0]
            : null;

    /// <summary>Returns all placements matching a translated search query.</summary>
    internal static IReadOnlyList<MilestonePlacement> Search(
        string query, Func<string, string> translate)
    {
        if (string.IsNullOrWhiteSpace(query)) return Placements;
        query = query.Trim();
        var results = new List<MilestonePlacement>();
        foreach (var placement in Placements)
        {
            string game = translate(placement.GameNameKey);
            string editor = translate(placement.EditorNameKey);
            string section = translate(placement.SectionKey);
            string sub = placement.SubcategoryKey != null
                ? translate(placement.SubcategoryKey) : "";
            if (placement.StatId.Contains(query, StringComparison.OrdinalIgnoreCase)
                || game.Contains(query, StringComparison.OrdinalIgnoreCase)
                || editor.Contains(query, StringComparison.OrdinalIgnoreCase)
                || section.Contains(query, StringComparison.OrdinalIgnoreCase)
                || sub.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(placement);
            }
        }
        return results;
    }

    private static IReadOnlyDictionary<string, MilestoneStat> BuildStats()
    {
        var byId = new Dictionary<string, List<MilestonePlacement>>(StringComparer.Ordinal);
        foreach (var placement in Placements)
        {
            if (!byId.TryGetValue(placement.StatId, out var list))
            {
                list = new List<MilestonePlacement>();
                byId[placement.StatId] = list;
            }
            list.Add(placement);
        }
        var result = new Dictionary<string, MilestoneStat>(StringComparer.Ordinal);
        foreach (var pair in byId)
            result[pair.Key] = new MilestoneStat(pair.Key, pair.Value);
        return result;
    }

    private static HashSet<string> BuildGuildLeveled()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in MilestoneLogic.GuildLeveledStats.Keys)
            result.Add(id.TrimStart('^'));
        return result;
    }

    /// <summary>Returns whether a stat has guild rank data.</summary>
    internal static bool IsGuildLeveled(string statId) =>
        _guildLeveled.Contains(statId.TrimStart('^'));
}
