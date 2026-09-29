using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Tests;

/// <summary>
/// Tests for <see cref="CatalogueCompletionLogic"/> and the verified
/// <see cref="CatalogueDatabase"/> loader.
/// </summary>
[Collection("MutableStaticDatabases")]
public class CatalogueCompletionLogicTests
{
    private static JsonObject NewPlayerState() => new();

    private static double AsDouble(object? value) => value is double d ? d : 0;

    // --- AddMissingIds / GetCompletion ---

    [Fact]
    public void AddMissingIds_CreatesArray_AndAddsCaretPrefix()
    {
        var playerState = NewPlayerState();

        int added = CatalogueCompletionLogic.AddMissingIds(
            playerState, "KnownProducts", new[] { "AMMO", "NANOTUBES" });

        Assert.Equal(2, added);
        var array = playerState.GetArray("KnownProducts")!;
        Assert.Equal(2, array.Length);
        Assert.Equal("^AMMO", array.GetString(0));
        Assert.Equal("^NANOTUBES", array.GetString(1));
    }

    [Fact]
    public void AddMissingIds_SkipsExisting_CaseInsensitive()
    {
        var playerState = NewPlayerState();
        var existing = new JsonArray();
        existing.Add("^ammo");
        playerState.Set("KnownProducts", existing);

        int added = CatalogueCompletionLogic.AddMissingIds(
            playerState, "KnownProducts", new[] { "AMMO", "NANOTUBES" });

        Assert.Equal(1, added);
        Assert.Equal(2, existing.Length);
        Assert.Equal("^NANOTUBES", existing.GetString(1));
    }

    [Fact]
    public void AddMissingIds_DedupesPackIds()
    {
        var playerState = NewPlayerState();

        int added = CatalogueCompletionLogic.AddMissingIds(
            playerState, "KnownTech", new[] { "BOLT", "bolt", "^BOLT", "BOLT" });

        Assert.Equal(1, added);
        Assert.Equal(1, playerState.GetArray("KnownTech")!.Length);
    }

    [Fact]
    public void AddMissingIds_HandlesObjectEntries()
    {
        var playerState = NewPlayerState();
        var existing = new JsonArray();
        var entry = new JsonObject();
        entry.Set("Id", "^AMMO");
        existing.Add(entry);
        playerState.Set("KnownProducts", existing);

        int added = CatalogueCompletionLogic.AddMissingIds(
            playerState, "KnownProducts", new[] { "AMMO", "NANOTUBES" });

        Assert.Equal(1, added);
    }

    [Fact]
    public void GetCompletion_CountsOnlyUniquePackIds()
    {
        var playerState = NewPlayerState();
        var array = new JsonArray();
        array.Add("^AMMO");
        array.Add("^EXTRA_NOT_IN_PACK");
        playerState.Set("KnownProducts", array);

        var (have, total) = CatalogueCompletionLogic.GetCompletion(
            playerState, "KnownProducts", new[] { "AMMO", "NANOTUBES", "ammo" });

        Assert.Equal(1, have);
        Assert.Equal(2, total);
    }

    // --- Word groups ---

    [Fact]
    public void AddMissingWordGroups_AddsGroupWithRaceFlags()
    {
        var playerState = NewPlayerState();
        var packGroups = new[]
        {
            new CatalogueDatabase.WordGroup("TRA_A", new[] { true, false, false })
        };

        var (added, upgraded) = CatalogueCompletionLogic.AddMissingWordGroups(playerState, packGroups);

        Assert.Equal(1, added);
        Assert.Equal(0, upgraded);
        var groups = playerState.GetArray("KnownWordGroups")!;
        Assert.Equal(1, groups.Length);
        var entry = groups.GetObject(0)!;
        Assert.Equal("^TRA_A", entry.GetString("Group"));
        var races = entry.GetArray("Races")!;
        Assert.True(races.GetBool(0));
        Assert.False(races.GetBool(1));
    }

    [Fact]
    public void AddMissingWordGroups_UpgradesExistingFlags()
    {
        var playerState = NewPlayerState();
        var existing = new JsonArray();
        var entry = new JsonObject();
        entry.Set("Group", "^TRA_A");
        var races = new JsonArray();
        races.Add(false);
        races.Add(true);
        entry.Set("Races", races);
        existing.Add(entry);
        playerState.Set("KnownWordGroups", existing);

        var packGroups = new[]
        {
            new CatalogueDatabase.WordGroup("TRA_A", new[] { true, true, false })
        };

        var (added, upgraded) = CatalogueCompletionLogic.AddMissingWordGroups(playerState, packGroups);

        Assert.Equal(0, added);
        Assert.Equal(1, upgraded);
        Assert.Equal(1, existing.Length);
        var updated = existing.GetObject(0)!.GetArray("Races")!;
        Assert.True(updated.GetBool(0));
        Assert.True(updated.GetBool(1));
        Assert.False(updated.GetBool(2));
    }

    [Fact]
    public void GetWordGroupCompletion_CountsGroupsPresent()
    {
        var playerState = NewPlayerState();
        var existing = new JsonArray();
        var entry = new JsonObject();
        entry.Set("Group", "^TRA_A");
        entry.Set("Races", new JsonArray());
        existing.Add(entry);
        playerState.Set("KnownWordGroups", existing);

        var packGroups = new[]
        {
            new CatalogueDatabase.WordGroup("TRA_A", new[] { true }),
            new CatalogueDatabase.WordGroup("TRA_B", new[] { true })
        };

        var (have, total) = CatalogueCompletionLogic.GetWordGroupCompletion(playerState, packGroups);

        Assert.Equal(1, have);
        Assert.Equal(2, total);
    }

    // --- Fishing ---

    [Fact]
    public void AddMissingFish_AddsAlignedLists()
    {
        var playerState = NewPlayerState();
        var packFish = new[]
        {
            new CatalogueDatabase.FishEntry("F_JELLYCHILD", 10, 25.5)
        };

        int added = CatalogueCompletionLogic.AddMissingFish(playerState, packFish);

        Assert.Equal(1, added);
        var record = playerState.GetObject("FishingRecord")!;
        Assert.Equal("^F_JELLYCHILD", record.GetArray("ProductList")!.GetString(0));
        Assert.Equal(10, record.GetArray("ProductCountList")!.GetInt(0));
        Assert.Equal(25.5, AsDouble(record.GetArray("LargestCatchList")!.Get(0)));
    }

    [Fact]
    public void AddMissingFish_KeepsExistingSpecies()
    {
        var playerState = NewPlayerState();
        var record = new JsonObject();
        var ids = new JsonArray();
        ids.Add("^F_JELLYCHILD");
        var counts = new JsonArray();
        counts.Add(3);
        var largest = new JsonArray();
        largest.Add(5.0);
        record.Set("ProductList", ids);
        record.Set("ProductCountList", counts);
        record.Set("LargestCatchList", largest);
        playerState.Set("FishingRecord", record);

        int added = CatalogueCompletionLogic.AddMissingFish(playerState, new[]
        {
            new CatalogueDatabase.FishEntry("F_JELLYCHILD", 10, 25.5),
            new CatalogueDatabase.FishEntry("F_OTHER", 1, 2.0)
        });

        Assert.Equal(1, added);
        Assert.Equal(2, ids.Length);
        Assert.Equal(3, counts.GetInt(0));
        Assert.Equal(1, counts.GetInt(1));
    }

    [Fact]
    public void GetFishingCompletion_CountsSpeciesPresent()
    {
        var playerState = NewPlayerState();
        var record = new JsonObject();
        var ids = new JsonArray();
        ids.Add("^F_JELLYCHILD");
        record.Set("ProductList", ids);
        playerState.Set("FishingRecord", record);

        var (have, total) = CatalogueCompletionLogic.GetFishingCompletion(playerState, new[]
        {
            new CatalogueDatabase.FishEntry("F_JELLYCHILD", 10, 25.5),
            new CatalogueDatabase.FishEntry("F_OTHER", 1, 2.0)
        });

        Assert.Equal(1, have);
        Assert.Equal(2, total);
    }

    // --- Verified pack integration ---

    [Fact]
    public void Pack_LoadsVerifiedTotals()
    {
        var jsonDir = FindResourceJsonDir();
        if (jsonDir == null) return; // Skip when the working directory does not contain Resources

        var pack = new CatalogueDatabase(jsonDir);

        Assert.True(pack.IsAvailable);
        Assert.Equal(3582, pack.KnownProducts.Count);
        Assert.Equal(429, pack.KnownTech.Count);
        Assert.Equal(276, pack.KnownSpecials.Count);
        Assert.Equal(1684, pack.KnownRefinerRecipes.Count);
        Assert.Equal(3831, pack.KnownWordGroups.Count);
        Assert.Equal(220, pack.Fishing.Count);
        Assert.Equal(65535, pack.KnownPortalRunes);
    }

    private static string? FindResourceJsonDir()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "Resources", "json");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return null;
    }

    [Fact]
    public void Pack_TBobbleIdsResolveViaPrefixFallback()
    {
        var jsonDir = FindResourceJsonDir();
        if (jsonDir == null) return; // Skip when the working directory does not contain Resources

        var db = new GameItemDatabase();
        db.LoadItemsFromJsonDirectory(jsonDir);

        // The extractor drops T_BOBBLE_* in favour of BOBBLE_*; the T_ prefix
        // fallback in GetItem keeps those save IDs resolvable for display.
        var item = db.GetItem("^T_BOBBLE_APOLLO");

        Assert.NotNull(item);
        Assert.Equal("BOBBLE_APOLLO", item!.Id);
    }
}
