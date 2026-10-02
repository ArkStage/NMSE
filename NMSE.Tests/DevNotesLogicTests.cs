using NMSE.Core;
using NMSE.Models;
using Xunit;

namespace NMSE.Tests;

/// <summary>
/// Tests for <see cref="DevNotesLogic"/>: the developer commentary entry table and the
/// DEV_NOTES high-water-mark stat in PlayerStateData.Stats (^GLOBAL_STATS group).
/// </summary>
public class DevNotesLogicTests
{
    [Fact]
    public void Entries_CoverAllIndicesInOrder()
    {
        Assert.Equal(DevNotesLogic.MaxIndex + 1, DevNotesLogic.Entries.Count);
        for (int i = 0; i < DevNotesLogic.Entries.Count; i++)
        {
            var entry = DevNotesLogic.Entries[i];
            Assert.Equal(i, entry.Index);
            Assert.StartsWith("UI_S23_", entry.NameLocKey, StringComparison.Ordinal);
            Assert.EndsWith("_NAME", entry.NameLocKey, StringComparison.Ordinal);
            Assert.EndsWith("_DESC", entry.DateLocKey, StringComparison.Ordinal);
            Assert.EndsWith("_LOG", entry.LogLocKey, StringComparison.Ordinal);
        }

        // Spot-check the first, middle and last updates.
        Assert.Equal("UI_S23_LEAVE_PLANET_LOG", DevNotesLogic.Entries[0].LogLocKey);
        Assert.Equal("UI_S23_FRACTAL_LOG", DevNotesLogic.Entries[26].LogLocKey);
        Assert.Equal("UI_S23_FUTURE_LOG", DevNotesLogic.Entries[38].LogLocKey);
    }

    [Fact]
    public void GetUnlockedIndex_ReturnsMinusOneWhenAbsent()
    {
        var playerState = new JsonObject();
        Assert.Equal(-1, DevNotesLogic.GetUnlockedIndex(playerState));
        Assert.Equal(-1, DevNotesLogic.GetUnlockedIndex(null));
    }

    [Fact]
    public void SetUnlockedCount_WritesIndexAndSeenStories()
    {
        var playerState = new JsonObject();

        Assert.True(DevNotesLogic.SetUnlockedCount(playerState, DevNotesLogic.EntryCount));

        Assert.Equal(38, DevNotesLogic.GetUnlockedIndex(playerState));
        Assert.Equal(39, DevNotesLogic.GetUnlockedCount(playerState));
        Assert.Equal(39, CatalogueCompletionLogic.GetLastSeen(playerState, KnowledgeCatalogue.DevNotesPageSlot, KnowledgeCatalogue.DevNotesPageIndex));
    }

    [Fact]
    public void SetUnlockedIndex_WritesMatchingSeenStories()
    {
        var playerState = new JsonObject();

        DevNotesLogic.SetUnlockedIndex(playerState, 5);

        Assert.Equal(6, DevNotesLogic.GetUnlockedCount(playerState));
        Assert.Equal(6, CatalogueCompletionLogic.GetLastSeen(playerState, KnowledgeCatalogue.DevNotesPageSlot, KnowledgeCatalogue.DevNotesPageIndex));
    }

    [Fact]
    public void GetUnlockedCount_DerivesFromIndexWhenSeenStoriesAbsent()
    {
        var playerState = new JsonObject();
        DevNotesLogic.SetUnlockedIndex(playerState, 20);
        CatalogueCompletionLogic.RemoveLastSeen(playerState, KnowledgeCatalogue.DevNotesPageSlot, KnowledgeCatalogue.DevNotesPageIndex);

        Assert.Equal(21, DevNotesLogic.GetUnlockedCount(playerState));
    }

    [Fact]
    public void GetUnlockedCount_NormalisesLegacyCountGlobal()
    {
        var playerState = new JsonObject();
        var group = new JsonObject();
        group.Set("GroupId", "^GLOBAL_STATS");
        var stats = new JsonArray();
        var stat = new JsonObject();
        stat.Set("Id", "^DEV_NOTES");
        var value = new JsonObject();
        value.Set("IntValue", 39); // legacy saves wrote the count instead of the index
        stat.Set("Value", value);
        stats.Add(stat);
        group.Set("Stats", stats);
        var groups = new JsonArray();
        groups.Add(group);
        playerState.Set("Stats", groups);

        Assert.Equal(38, DevNotesLogic.GetUnlockedIndex(playerState));
        Assert.Equal(39, DevNotesLogic.GetUnlockedCount(playerState));
    }

    [Fact]
    public void SetUnlockedCount_ZeroClearsBoth()
    {
        var playerState = new JsonObject();
        DevNotesLogic.SetUnlockedCount(playerState, 20);

        Assert.True(DevNotesLogic.SetUnlockedCount(playerState, 0));

        Assert.Equal(-1, DevNotesLogic.GetUnlockedIndex(playerState));
        Assert.Equal(0, DevNotesLogic.GetUnlockedCount(playerState));
        Assert.Null(CatalogueCompletionLogic.GetLastSeen(playerState, KnowledgeCatalogue.DevNotesPageSlot, KnowledgeCatalogue.DevNotesPageIndex));
    }

    [Fact]
    public void SetUnlockedIndex_CreatesStatAndGroup()
    {
        var playerState = new JsonObject();

        DevNotesLogic.SetUnlockedIndex(playerState, 12);

        Assert.Equal(12, DevNotesLogic.GetUnlockedIndex(playerState));

        var groups = playerState.GetArray("Stats");
        Assert.NotNull(groups);
        Assert.Equal(1, groups!.Length);
        var group = groups.GetObject(0);
        Assert.NotNull(group);
        Assert.Equal("^GLOBAL_STATS", group!.GetString("GroupId"));
        var stats = group.GetArray("Stats");
        Assert.NotNull(stats);
        Assert.Equal(1, stats!.Length);
        Assert.Equal("^DEV_NOTES", stats.GetObject(0)!.GetString("Id"));
        Assert.Equal(12, stats.GetObject(0)!.GetObject("Value")!.GetInt("IntValue"));
    }

    [Fact]
    public void SetUnlockedIndex_UpdatesExistingStat()
    {
        var playerState = new JsonObject();
        var group = new JsonObject();
        group.Set("GroupId", "^GLOBAL_STATS");
        group.Set("Address", 0);
        var stats = new JsonArray();
        var stat = new JsonObject();
        stat.Set("Id", "^DEV_NOTES");
        var value = new JsonObject();
        value.Set("IntValue", 5);
        stat.Set("Value", value);
        stats.Add(stat);
        group.Set("Stats", stats);
        var groups = new JsonArray();
        groups.Add(group);
        playerState.Set("Stats", groups);

        DevNotesLogic.SetUnlockedIndex(playerState, 30);

        Assert.Equal(30, DevNotesLogic.GetUnlockedIndex(playerState));
        Assert.Equal(1, playerState.GetArray("Stats")!.Length);
        Assert.Equal(1, playerState.GetArray("Stats")!.GetObject(0)!.GetArray("Stats")!.Length);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(38, 38)]
    [InlineData(99, 38)]
    public void SetUnlockedIndex_ClampsToRange(int input, int expected)
    {
        var playerState = new JsonObject();

        DevNotesLogic.SetUnlockedIndex(playerState, input);

        Assert.Equal(expected, DevNotesLogic.GetUnlockedIndex(playerState));
    }
}
