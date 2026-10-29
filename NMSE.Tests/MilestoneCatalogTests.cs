using System.Text.Json;
using NMSE.Core;
using Xunit;

namespace NMSE.Tests;

/// <summary>
/// Coverage and consistency tests for the declarative Milestones catalog. The expected fixture
/// is generated from the issue's correspondence tables, so it is an independent check that no
/// stat or location is lost during the reorganisation.
/// </summary>
public class MilestoneCatalogTests
{
    [Fact]
    public void Placements_MatchIssueFixtureExactly()
    {
        var expected = LoadExpectedPlacements();
        var actual = MilestoneCatalog.Placements
            .Select(p => $"{p.StatId}|{p.Tab}|{p.SectionKey}|{p.SubcategoryKey}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(241, expected.Count);
        Assert.Equal(expected.OrderBy(value => value, StringComparer.Ordinal), actual);
        Assert.Equal(237, MilestoneCatalog.Stats.Count);
    }

    [Fact]
    public void TabAndCategoryCounts_MatchTheIssue()
    {
        Assert.Equal(9, CountTab(MilestoneTab.Milestones));
        Assert.Equal(16, CountTab(MilestoneTab.Lifeforms));
        Assert.Equal(29, CountTab(MilestoneTab.Factions));
        Assert.Equal(187, CountTab(MilestoneTab.OtherStats));

        var expectedCategories = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["milestone.section_general"] = 16,
            ["milestone.section_combat"] = 34,
            ["milestone.section_movement"] = 15,
            ["milestone.section_exploration"] = 25,
            ["milestone.section_resources"] = 14,
            ["milestone.section_companions"] = 7,
            ["milestone.section_construction"] = 6,
            ["milestone.section_equipment"] = 4,
            ["milestone.section_disc_planets"] = 28,
            ["milestone.section_disc_creatures"] = 9,
            ["milestone.section_multiplayer"] = 19,
            ["milestone.section_travel"] = 10,
        };

        foreach (var pair in expectedCategories)
        {
            Assert.Equal(pair.Value, MilestoneCatalog.Placements.Count(p =>
                p.Tab == MilestoneTab.OtherStats && p.SectionKey == pair.Key));
        }
    }

    [Fact]
    public void SharedStats_HaveBothLocations()
    {
        var shared = MilestoneCatalog.Stats.Values
            .Where(stat => stat.IsShared)
            .Select(stat => stat.StatId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "^DIST_WARP", "^MONEY", "^SENTINEL_KILLS", "^SMUGGLE_VALUE" },
            shared);

        Assert.Equal(2, MilestoneCatalog.Stats["^MONEY"].Placements.Count);
        Assert.Equal(2, MilestoneCatalog.Stats["^DIST_WARP"].Placements.Count);
        Assert.Equal(2, MilestoneCatalog.Stats["^SENTINEL_KILLS"].Placements.Count);
        Assert.Equal(2, MilestoneCatalog.Stats["^SMUGGLE_VALUE"].Placements.Count);
    }

    [Fact]
    public void NameAlignments_UseTheGameLabels()
    {
        var money = MilestoneCatalog.Stats["^MONEY"].Placements;
        Assert.Contains(money, p => p.GameNameKey == "milestone.game_units_accrued"
            && p.SectionKey == "milestone.section_survival_milestones");
        Assert.Contains(money, p => p.GameNameKey == "milestone.game_units_earned"
            && p.SectionKey == "milestone.merchants_guild");

        var warp = MilestoneCatalog.Stats["^DIST_WARP"].Placements;
        Assert.Contains(warp, p => p.GameNameKey == "milestone.space_exploration"
            && p.SectionKey == "milestone.section_exploration_milestones");
        Assert.Contains(warp, p => p.GameNameKey == "milestone.game_systems_mapped"
            && p.SectionKey == "milestone.explorers_guild");

        Assert.Equal("milestone.game_extreme_survival",
            MilestoneCatalog.Stats["^LONGEST_LIFE_EX"].Placements[0].GameNameKey);
        Assert.Equal("milestone.game_extreme_walk",
            MilestoneCatalog.Stats["^EXTREME_WALK"].Placements[0].GameNameKey);
        Assert.Equal("milestone.game_planetary_zoology",
            MilestoneCatalog.Stats["^DISC_ALL_CREATU"].Placements[0].GameNameKey);
        Assert.Equal("milestone.game_walkers_destroyed",
            MilestoneCatalog.Stats["^WALKERS_KILLED"].Placements[0].GameNameKey);
        Assert.Equal("milestone.game_nanite_clusters",
            MilestoneCatalog.Stats["^NANITES_EVER"].Placements[0].GameNameKey);
        Assert.Equal("milestone.game_autophage_salvage",
            MilestoneCatalog.Stats["^HEAD_REPAIRS"].Placements[0].GameNameKey);
        Assert.Equal("milestone.game_champions_defeated",
            MilestoneCatalog.Stats["^PB_BOSS_WINS"].Placements[0].GameNameKey);
        Assert.Equal("milestone.game_holo_arena_victories",
            MilestoneCatalog.Stats["^PB_WINS"].Placements[0].GameNameKey);
        Assert.Equal("milestone.game_apex_companions",
            MilestoneCatalog.Stats["^PB_PETS_MAXED"].Placements[0].GameNameKey);
        Assert.Equal("milestone.game_iteration_oceanus",
            MilestoneCatalog.Stats["^PB_D_NEXUS"].Placements[0].GameNameKey);
        Assert.Equal("milestone.game_hatch_eggs",
            MilestoneCatalog.Stats["^EGGS_HATCHED"].Placements[0].GameNameKey);
        Assert.Equal("milestone.game_freighters_plundered",
            MilestoneCatalog.Stats["^FPODS_BROKEN"].Placements[0].GameNameKey);
        Assert.Equal("milestone.missions_completed",
            MilestoneCatalog.Stats["^TDONE_MISSIONS"].Placements[0].GameNameKey);
    }

    [Fact]
    public void GuildRankPlacements_AreLimitedToFactions()
    {
        Assert.Equal(17, MilestoneCatalog.Placements.Count(p => p.ShowGuildRank));
        Assert.Equal(16, MilestoneCatalog.Placements.Count(p =>
            p.ShowGuildRank && p.Tab == MilestoneTab.Factions));
        Assert.True(MilestoneCatalog.Stats["^TRADERS_KILLED"].Placements[0].ShowGuildRank);

        Assert.False(MilestoneCatalog.Stats["^SMUGGLE_VALUE"].Placements
            .Single(p => p.SectionKey == "milestone.section_the_gek").ShowGuildRank);
        Assert.True(MilestoneCatalog.Stats["^SMUGGLE_VALUE"].Placements
            .Single(p => p.SectionKey == "milestone.outlaws").ShowGuildRank);
    }

    [Fact]
    public void EditorOnlyStats_MatchTheIssue()
    {
        var editorOnly = MilestoneCatalog.Placements
            .Where(p => p.EditorOnly)
            .Select(p => p.StatId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "^PB_CHALL_WINS", "^PB_LOSSES", "^PETS_OWNED", "^PET_LEVEL_SPENT" },
            editorOnly);
    }

    [Fact]
    public void SectionsHaveIcons_AndMenuOrderIsStable()
    {
        foreach (var tab in MilestoneCatalog.TabOrder)
        {
            var sections = MilestoneCatalog.GetSections(tab).ToList();
            Assert.NotEmpty(sections);
            Assert.All(sections, section => Assert.NotNull(MilestoneCatalog.IconForSection(section)));
        }

        Assert.Equal(
            new[]
            {
                "milestone.section_exploration_milestones",
                "milestone.section_survival_milestones",
            },
            MilestoneCatalog.GetSections(MilestoneTab.Milestones));
    }

    [Fact]
    public void Search_MatchesIdAndTranslatedNames()
    {
        var translator = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["milestone.game_extreme_survival"] = "Extreme Survival",
            ["milestone.longest_life_ex"] = "Longest Life",
            ["milestone.section_survival_milestones"] = "Survival Milestones",
            ["milestone.most_units_accrued"] = "Most Units Accrued",
            ["milestone.game_units_accrued"] = "Units Accrued",
        };

        string Translate(string key) => translator.TryGetValue(key, out string? value) ? value : key;

        Assert.Contains(MilestoneCatalog.Search("^LONGEST_LIFE_EX", Translate),
            p => p.StatId == "^LONGEST_LIFE_EX");
        Assert.Contains(MilestoneCatalog.Search("extreme survival", Translate),
            p => p.StatId == "^LONGEST_LIFE_EX");
        Assert.Contains(MilestoneCatalog.Search("longest life", Translate),
            p => p.StatId == "^LONGEST_LIFE_EX");
        Assert.Empty(MilestoneCatalog.Search("no such stat", Translate));
    }

    [Fact]
    public void FindFirst_ReturnsPrimaryPlacement()
    {
        var placement = MilestoneCatalog.FindFirst("^MONEY");
        Assert.NotNull(placement);
        Assert.Equal(MilestoneTab.Milestones, placement!.Tab);
        Assert.Equal("milestone.section_survival_milestones", placement.SectionKey);
    }

    private static int CountTab(MilestoneTab tab) =>
        MilestoneCatalog.Placements.Count(p => p.Tab == tab);

    private static List<string> LoadExpectedPlacements()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "MilestoneCatalogExpected.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("placements")
            .EnumerateArray()
            .Select(element =>
            {
                string id = element.GetProperty("id").GetString() ?? "";
                string tab = element.GetProperty("tab").GetString() ?? "";
                string section = element.GetProperty("section").GetString() ?? "";
                string? sub = element.GetProperty("sub").ValueKind == JsonValueKind.Null
                    ? null
                    : element.GetProperty("sub").GetString();
                return $"{id}|{tab}|{section}|{sub}";
            })
            .ToList();
    }
}
