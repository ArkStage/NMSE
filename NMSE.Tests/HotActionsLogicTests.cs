using NMSE.Core;
using NMSE.Models;

namespace NMSE.Tests;

/// <summary>
/// Tests for <see cref="HotActionsLogic"/>: the action metadata table, context/slot
/// normalisation, slot read/write helpers and the import/export round-trip.
/// </summary>
public class HotActionsLogicTests
{
    [Fact]
    public void Actions_CoverAllUsableValues()
    {
        Assert.Equal(56, HotActionsLogic.Actions.Count);
        for (int i = 0; i < HotActionsLogic.Actions.Count; i++)
        {
            Assert.Equal(i, HotActionsLogic.Actions[i].Value);
            Assert.False(string.IsNullOrEmpty(HotActionsLogic.Actions[i].Name));
        }
        Assert.Equal("None", HotActionsLogic.Actions[0].Name);
        Assert.Equal("CallRocket", HotActionsLogic.Actions[^1].Name);
    }

    [Fact]
    public void Actions_ParameterKindsMatchTheReversedMapping()
    {
        Assert.Equal(HotActionParameterKind.Index, HotActionsLogic.FindAction("CallShip")!.ParameterKind);
        Assert.Equal(HotActionParameterKind.Index, HotActionsLogic.FindAction("SwapMultitool")!.ParameterKind);
        Assert.Equal(HotActionParameterKind.Index, HotActionsLogic.FindAction("SummonPet")!.ParameterKind);
        Assert.Equal(HotActionParameterKind.InventoryItem, HotActionsLogic.FindAction("ChargeMenu")!.ParameterKind);
        Assert.Equal(HotActionParameterKind.InventoryItem, HotActionsLogic.FindAction("Charge")!.ParameterKind);
        Assert.Equal(HotActionParameterKind.InventoryItem, HotActionsLogic.FindAction("Repair")!.ParameterKind);
        Assert.Equal(HotActionParameterKind.EmoteId, HotActionsLogic.FindAction("Emote")!.ParameterKind);
        Assert.Equal(HotActionParameterKind.WeaponMode, HotActionsLogic.FindAction("ChangeSecondaryWeapon")!.ParameterKind);
        Assert.Equal(HotActionParameterKind.CreatureFood, HotActionsLogic.FindAction("ChooseCreatureFood")!.ParameterKind);
        Assert.Equal(HotActionParameterKind.None, HotActionsLogic.FindAction("PhotoMode")!.ParameterKind);
        Assert.Null(HotActionsLogic.FindAction("Invalid"));
        Assert.Null(HotActionsLogic.FindAction("NotAnAction"));
    }

    [Fact]
    public void EnsureContext_CreatesThreeContextsOfTenSlots()
    {
        var playerState = new JsonObject();

        var context = HotActionsLogic.EnsureContext(playerState, 1);

        var hot = playerState.GetArray("HotActions")!;
        Assert.Equal(3, hot.Length);
        var slots = context.GetArray("KeyActions")!;
        Assert.Equal(10, slots.Length);
        Assert.Equal("None", HotActionsLogic.GetActionName(slots.GetObject(0)));
        Assert.Equal("^", HotActionsLogic.GetId(slots.GetObject(9)));
        Assert.Equal((-1, -1), HotActionsLogic.GetInventoryIndex(slots.GetObject(0)));
    }

    [Fact]
    public void SetSlot_WritesActionAndParameters()
    {
        var slot = new JsonObject();

        HotActionsLogic.SetSlot(slot, "ChargeMenu", "^", 2, 9, 0);

        Assert.Equal("ChargeMenu", HotActionsLogic.GetActionName(slot));
        Assert.Equal(2, HotActionsLogic.GetNumber(slot));
        Assert.Equal((9, 0), HotActionsLogic.GetInventoryIndex(slot));

        HotActionsLogic.ClearSlot(slot);
        Assert.Equal("None", HotActionsLogic.GetActionName(slot));
        Assert.Equal(0, HotActionsLogic.GetNumber(slot));
        Assert.Equal((-1, -1), HotActionsLogic.GetInventoryIndex(slot));
    }

    [Fact]
    public void BuildExport_ProducesNamedContextArrays()
    {
        var playerState = new JsonObject();
        var slots = HotActionsLogic.EnsureSlots(HotActionsLogic.EnsureContext(playerState, 0));
        HotActionsLogic.SetSlot(slots.GetObject(0), "CallShip", "^", 4);

        var export = HotActionsLogic.BuildExport(playerState);

        foreach (string name in HotActionsLogic.ContextNames)
        {
            var array = export.GetArray(name);
            Assert.NotNull(array);
            Assert.Equal(10, array!.Length);
        }
        Assert.Equal("CallShip", HotActionsLogic.GetActionName(export.GetArray("OnFoot")!.GetObject(0)));
        Assert.Equal(4, HotActionsLogic.GetNumber(export.GetArray("OnFoot")!.GetObject(0)));
    }

    [Fact]
    public void ApplyImport_NamedShape_RoundTrips()
    {
        var source = new JsonObject();
        var sourceSlots = HotActionsLogic.EnsureSlots(HotActionsLogic.EnsureContext(source, 2));
        HotActionsLogic.SetSlot(sourceSlots.GetObject(3), "Emote", "^EMOTE_WAVE");
        var export = HotActionsLogic.BuildExport(source);

        var target = new JsonObject();
        int applied = HotActionsLogic.ApplyImport(target, export);

        Assert.Equal(30, applied);
        var targetSlots = HotActionsLogic.EnsureSlots(HotActionsLogic.EnsureContext(target, 2));
        Assert.Equal("Emote", HotActionsLogic.GetActionName(targetSlots.GetObject(3)));
        Assert.Equal("^EMOTE_WAVE", HotActionsLogic.GetId(targetSlots.GetObject(3)));
    }

    [Fact]
    public void ApplyImport_SaveShape_Works()
    {
        var saveShape = JsonObject.Parse("""
        {
            "HotActions": [
                { "KeyActions": [ { "Action": { "QuickMenuActions": "PhotoMode" }, "Id": "^", "Number": 0, "InventoryIndex": { "X": -1, "Y": -1 } } ] },
                { "KeyActions": [] },
                { "KeyActions": [] }
            ]
        }
        """);

        var target = new JsonObject();
        int applied = HotActionsLogic.ApplyImport(target, saveShape);

        Assert.Equal(1, applied);
        var slots = HotActionsLogic.EnsureSlots(HotActionsLogic.EnsureContext(target, 0));
        Assert.Equal("PhotoMode", HotActionsLogic.GetActionName(slots.GetObject(0)));
    }

    [Fact]
    public void BuildExport_PreservesUnknownFields()
    {
        var playerState = new JsonObject();
        var slots = HotActionsLogic.EnsureSlots(HotActionsLogic.EnsureContext(playerState, 0));
        var slot = slots.GetObject(0);
        HotActionsLogic.SetSlot(slot, "PhotoMode");
        slot.Set("CustomField", "keep-me");

        var export = HotActionsLogic.BuildExport(playerState);

        Assert.Equal("keep-me", export.GetArray("OnFoot")!.GetObject(0)!.GetString("CustomField"));
    }

    [Fact]
    public void GetDisplayName_FallsBackToSpacedEnumName()
    {
        Assert.Equal("Third Person Character", HotActionsLogic.GetDisplayName(HotActionsLogic.FindAction("ThirdPersonCharacter")!));
        Assert.Equal("Call Rocket", HotActionsLogic.GetDisplayName(HotActionsLogic.FindAction("CallRocket")!));
    }
}
