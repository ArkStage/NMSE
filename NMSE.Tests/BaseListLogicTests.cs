using NMSE.Core;
using NMSE.Models;

namespace NMSE.Tests;

/// <summary>
/// Tests for <see cref="BaseListLogic"/>: classification, hidden-entry filtering,
/// search, block reordering and sorting.
/// </summary>
public class BaseListLogicTests
{
    private static JsonObject MakeBase(string type, int version, string name, int objectCount = 0)
    {
        var baseObj = new JsonObject();
        var baseType = new JsonObject();
        baseType.Set("PersistentBaseTypes", type);
        baseObj.Set("BaseType", baseType);
        baseObj.Set("BaseVersion", version);
        baseObj.Set("Name", name);
        var objects = new JsonArray();
        for (int i = 0; i < objectCount; i++)
            objects.Add(new JsonObject());
        baseObj.Set("Objects", objects);
        return baseObj;
    }

    private static JsonObject MakePlayerState(params JsonObject[] bases)
    {
        var state = new JsonObject();
        state.Set("PlayerFreighterName", "The Barge");
        var array = new JsonArray();
        foreach (var baseObj in bases)
            array.Add(baseObj);
        state.Set("PersistentPlayerBases", array);
        return state;
    }

    [Fact]
    public void Classify_RecognisesTemporaryAndLegacyEntries()
    {
        Assert.Equal(BaseListLogic.BaseListKind.Home, BaseListLogic.Classify(MakeBase("HomePlanetBase", 8, "Home")));
        Assert.Equal(BaseListLogic.BaseListKind.TemporaryShip, BaseListLogic.Classify(MakeBase("UITempShipBase", 8, "Default")));
        Assert.Equal(BaseListLogic.BaseListKind.Legacy, BaseListLogic.Classify(MakeBase("HomePlanetBase", 1, "Old")));
        Assert.Equal(BaseListLogic.BaseListKind.Other, BaseListLogic.Classify(MakeBase("FutureBase", 8, "Future")));
    }

    [Fact]
    public void BuildRows_IncludesHiddenEntriesByDefault()
    {
        var state = MakePlayerState(
            MakeBase("HomePlanetBase", 8, "Home", 12),
            MakeBase("UITempShipBase", 8, "Default", 3),
            MakeBase("HomePlanetBase", 1, "Old"));

        var all = BaseListLogic.BuildRows(state, state.GetArray("PersistentPlayerBases"), null, includeHidden: true);
        Assert.Equal(3, all.Count);
        Assert.Equal(BaseListLogic.BaseListKind.TemporaryShip, all[1].Kind);
        Assert.Equal(BaseListLogic.BaseListKind.Legacy, all[2].Kind);

        var visible = BaseListLogic.BuildRows(state, state.GetArray("PersistentPlayerBases"), null, includeHidden: false);
        Assert.Single(visible);
        Assert.Equal("Home", visible[0].Name);
    }

    [Fact]
    public void BuildRows_FiltersByNameAndType()
    {
        var state = MakePlayerState(
            MakeBase("HomePlanetBase", 8, "Rattle Ridge"),
            MakeBase("UITempShipBase", 8, "Default"),
            MakeBase("FreighterBase", 8, ""));

        var bases = state.GetArray("PersistentPlayerBases");

        var byName = BaseListLogic.BuildRows(state, bases, "rattle", includeHidden: true);
        Assert.Single(byName);
        Assert.Equal("Rattle Ridge", byName[0].Name);

        var byType = BaseListLogic.BuildRows(state, bases, "UITemp", includeHidden: true);
        Assert.Single(byType);
        Assert.Equal(BaseListLogic.BaseListKind.TemporaryShip, byType[0].Kind);

        var byKind = BaseListLogic.BuildRows(state, bases, "freighter", includeHidden: true);
        Assert.Single(byKind);
        Assert.Equal("The Barge", byKind[0].Name);
    }

    [Fact]
    public void BuildRows_ReturnsEveryEntryForLargeSaves()
    {
        // Issue #146: a save with 256 entries (180 planetary + 76 temporary ship
        // bases) must produce 256 rows when hidden entries are shown.
        var bases = new List<JsonObject>();
        for (int i = 0; i < 180; i++)
            bases.Add(MakeBase("HomePlanetBase", 8, $"Home {i}"));
        for (int i = 0; i < 76; i++)
            bases.Add(MakeBase("UITempShipBase", 8, $"Temp {i}"));

        var state = MakePlayerState(bases.ToArray());
        var rows = BaseListLogic.BuildRows(
            state, state.GetArray("PersistentPlayerBases"), null, includeHidden: true);

        Assert.Equal(256, rows.Count);
        Assert.Equal(BaseListLogic.BaseListKind.TemporaryShip, rows[180].Kind);
    }

    [Fact]
    public void BuildRows_KeepsUnreadableEntriesAsPlaceholders()
    {
        var state = MakePlayerState(MakeBase("HomePlanetBase", 8, "Home"));
        var bases = state.GetArray("PersistentPlayerBases")!;
        bases.Add("not an object");

        var rows = BaseListLogic.BuildRows(state, bases, null, includeHidden: true);
        Assert.Equal(2, rows.Count);
        Assert.False(rows[1].IsReadable);
        Assert.Equal(1, rows[1].DataIndex);
    }

    [Fact]
    public void MoveBlock_MovesSelectionForwardAsOneBlock()
    {
        var state = MakePlayerState(
            MakeBase("HomePlanetBase", 8, "A"),
            MakeBase("HomePlanetBase", 8, "B"),
            MakeBase("HomePlanetBase", 8, "C"),
            MakeBase("HomePlanetBase", 8, "D"),
            MakeBase("HomePlanetBase", 8, "E"));
        var bases = state.GetArray("PersistentPlayerBases")!;

        var moved = BaseListLogic.MoveBlock(bases, new[] { 1, 2 }, 4);

        Assert.Equal(new[] { "A", "D", "B", "C", "E" }, Names(bases));
        Assert.Equal(new[] { 2, 3 }, moved);
    }

    [Fact]
    public void MoveBlock_MovesSelectionBackwardAsOneBlock()
    {
        var state = MakePlayerState(
            MakeBase("HomePlanetBase", 8, "A"),
            MakeBase("HomePlanetBase", 8, "B"),
            MakeBase("HomePlanetBase", 8, "C"),
            MakeBase("HomePlanetBase", 8, "D"),
            MakeBase("HomePlanetBase", 8, "E"));
        var bases = state.GetArray("PersistentPlayerBases")!;

        var moved = BaseListLogic.MoveBlock(bases, new[] { 3, 4 }, 0);

        Assert.Equal(new[] { "D", "E", "A", "B", "C" }, Names(bases));
        Assert.Equal(new[] { 0, 1 }, moved);
    }

    [Fact]
    public void MoveBlock_NoOpWhenDroppingInsideTheBlock()
    {
        var state = MakePlayerState(
            MakeBase("HomePlanetBase", 8, "A"),
            MakeBase("HomePlanetBase", 8, "B"),
            MakeBase("HomePlanetBase", 8, "C"),
            MakeBase("HomePlanetBase", 8, "D"));
        var bases = state.GetArray("PersistentPlayerBases")!;

        BaseListLogic.MoveBlock(bases, new[] { 1, 2 }, 2);

        Assert.Equal(new[] { "A", "B", "C", "D" }, Names(bases));
    }

    [Fact]
    public void SortByName_OrdersAscendingAndKeepsEmptyNamesLast()
    {
        var state = MakePlayerState(
            MakeBase("HomePlanetBase", 8, "Zed"),
            MakeBase("HomePlanetBase", 8, "Alpha"),
            MakeBase("HomePlanetBase", 8, ""));
        var bases = state.GetArray("PersistentPlayerBases")!;

        BaseListLogic.SortByName(state, bases, descending: false);

        Assert.Equal(new[] { "Alpha", "Zed", "" }, Names(bases));
    }

    [Fact]
    public void SortByName_OrdersDescendingAndKeepsEmptyNamesLast()
    {
        var state = MakePlayerState(
            MakeBase("HomePlanetBase", 8, "Zed"),
            MakeBase("HomePlanetBase", 8, "Alpha"),
            MakeBase("HomePlanetBase", 8, ""));
        var bases = state.GetArray("PersistentPlayerBases")!;

        BaseListLogic.SortByName(state, bases, descending: true);

        Assert.Equal(new[] { "Zed", "Alpha", "" }, Names(bases));
    }

    private static string[] Names(JsonArray bases)
    {
        var names = new string[bases.Length];
        for (int i = 0; i < bases.Length; i++)
            names[i] = bases.GetObject(i).GetString("Name") ?? "";
        return names;
    }
}
