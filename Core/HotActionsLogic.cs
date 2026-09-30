using System.Text;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Core;

/// <summary>Which save fields a quick menu action reads.</summary>
internal enum HotActionParameterKind
{
    /// <summary>No parameters; Id, Number and InventoryIndex are ignored.</summary>
    None,

    /// <summary>Number is an index (ship, multitool or pet).</summary>
    Index,

    /// <summary>Number selects the inventory and InventoryIndex the item slot.</summary>
    InventoryItem,

    /// <summary>Id holds an emote ID (for example <c>^EMOTE_WAVE</c>).</summary>
    EmoteId,

    /// <summary>Number is a secondary weapon mode.</summary>
    WeaponMode,

    /// <summary>Number is an amount and Id the creature food item.</summary>
    CreatureFood
}

/// <summary>Metadata for one <c>eQuickMenuActions</c> value.</summary>
/// <param name="Value">The enum value stored in the save.</param>
/// <param name="Name">The enum name stored in <c>Action.QuickMenuActions</c>.</param>
/// <param name="LabelKey">Game localisation key for the menu label, or empty.</param>
/// <param name="ParameterKind">Which save fields the action uses.</param>
internal sealed record HotActionInfo(int Value, string Name, string LabelKey, HotActionParameterKind ParameterKind);

/// <summary>
/// Reads and writes the quick menu hotkeys (<c>PlayerStateData.HotActions</c>): three context
/// arrays (OnFoot, InShip, InExocraft) of ten slots, each with an action name and optional
/// parameters. See <c>_RE_Docs/hot-actions.md</c> for the reversed structure and field mapping.
/// </summary>
internal static class HotActionsLogic
{
    /// <summary>Number of hot action contexts (OnFoot, InShip, InExocraft).</summary>
    internal const int ContextCount = 3;

    /// <summary>Number of hotkey slots per context.</summary>
    internal const int SlotsPerContext = 10;

    /// <summary>Context names used by the export format, in save order.</summary>
    internal static readonly string[] ContextNames = ["OnFoot", "InShip", "InExocraft"];

    /// <summary>All usable quick menu actions (0..55; Invalid and NumTypes are excluded).</summary>
    internal static readonly IReadOnlyList<HotActionInfo> Actions =
    [
        new(0, "None", "", HotActionParameterKind.None),
        new(1, "CallFreighter", "", HotActionParameterKind.None),
        new(2, "DismissFreighter", "", HotActionParameterKind.None),
        new(3, "SummonNexus", "", HotActionParameterKind.None),
        new(4, "CallShip", "", HotActionParameterKind.Index),
        new(5, "CallSquadron", "UI_SUMMON_SQUADRON", HotActionParameterKind.None),
        new(6, "SummonVehicleSubMenu", "", HotActionParameterKind.None),
        new(7, "SummonBuggy", "UI_SUMMON_BUGGY", HotActionParameterKind.None),
        new(8, "SummonBike", "UI_SUMMON_BIKE", HotActionParameterKind.None),
        new(9, "SummonTruck", "UI_SUMMON_TRUCK", HotActionParameterKind.None),
        new(10, "SummonWheeledBike", "UI_SUMMON_WHEELEDBIKE", HotActionParameterKind.None),
        new(11, "SummonHovercraft", "UI_SUMMON_HOVERSHIP", HotActionParameterKind.None),
        new(12, "SummonSubmarine", "UI_SUMMON_SUBMARINE", HotActionParameterKind.None),
        new(13, "SummonMech", "UI_SUMMON_MECH", HotActionParameterKind.None),
        new(14, "VehicleAIToggle", "", HotActionParameterKind.None),
        new(15, "VehicleScan", "", HotActionParameterKind.None),
        new(16, "VehicleScanSelect", "", HotActionParameterKind.None),
        new(17, "VehicleRestartRace", "", HotActionParameterKind.None),
        new(18, "Torch", "", HotActionParameterKind.None),
        new(19, "GalaxyMap", "", HotActionParameterKind.None),
        new(20, "PhotoMode", "QUICK_MENU_PHOTO_MODE", HotActionParameterKind.None),
        new(21, "ChargeMenu", "", HotActionParameterKind.InventoryItem),
        new(22, "Charge", "", HotActionParameterKind.InventoryItem),
        new(23, "ChargeSubMenu", "QUICK_MENU_CHARGE_MENU", HotActionParameterKind.None),
        new(24, "Repair", "", HotActionParameterKind.InventoryItem),
        new(25, "BuildMenu", "", HotActionParameterKind.None),
        new(26, "CommunicatorReceive", "", HotActionParameterKind.None),
        new(27, "CommunicatorInitiate", "", HotActionParameterKind.None),
        new(28, "ThirdPersonCharacter", "", HotActionParameterKind.None),
        new(29, "ThirdPersonShip", "", HotActionParameterKind.None),
        new(30, "ThirdPersonVehicle", "", HotActionParameterKind.None),
        new(31, "EconomyScan", "TRIGGER_ECON_SCANNER", HotActionParameterKind.None),
        new(32, "EmoteMenu", "QUICK_MENU_EMOTE_MENU", HotActionParameterKind.None),
        new(33, "Emote", "", HotActionParameterKind.EmoteId),
        new(34, "UtilitySubMenu", "QUICK_MENU_UTILITY_MENU", HotActionParameterKind.None),
        new(35, "SummonSubMenu", "QUICK_MENU_SUMMON_MENU", HotActionParameterKind.None),
        new(36, "SummonShipSubMenu", "QUICK_MENU_SUMMON_SHIPS_MENU", HotActionParameterKind.None),
        new(37, "ChangeSecondaryWeaponMenu", "QUICK_MENU_SECONDARY_WEAP_MENU", HotActionParameterKind.None),
        new(38, "ChangeSecondaryWeapon", "", HotActionParameterKind.WeaponMode),
        new(39, "ChooseCreatureFoodMenu", "QUICK_MENU_CREATURE_FOOD_MENU", HotActionParameterKind.None),
        new(40, "ChooseCreatureFood", "", HotActionParameterKind.CreatureFood),
        new(41, "EmergencyWarp", "", HotActionParameterKind.None),
        new(42, "SwapMultitool", "", HotActionParameterKind.Index),
        new(43, "SwapMultitoolSubMenu", "QUICK_MENU_SWAP_WEAP_MENU", HotActionParameterKind.None),
        new(44, "CreatureSubMenu", "UI_QUICK_MENU_CREATURE_MENU", HotActionParameterKind.None),
        new(45, "SummonPet", "", HotActionParameterKind.Index),
        new(46, "SummonPetSubMenu", "UI_QUICK_MENU_PET_PAGE", HotActionParameterKind.None),
        new(47, "WarpToNexus", "UI_QUICK_MENU_NEXUS_RETURN", HotActionParameterKind.None),
        new(48, "PetUI", "UI_QUICK_MENU_PET_UI", HotActionParameterKind.None),
        new(49, "ByteBeatSubMenu", "QUICK_MENU_BB_MENU", HotActionParameterKind.None),
        new(50, "ByteBeatPlay", "QUICK_MENU_BB_PLAY", HotActionParameterKind.None),
        new(51, "ByteBeatStop", "QUICK_MENU_BB_STOP", HotActionParameterKind.None),
        new(52, "ByteBeatLibrary", "QUICK_MENU_BB_LIBRARY", HotActionParameterKind.None),
        new(53, "ReportBase", "CTRL_BTN_REPORT_BASE", HotActionParameterKind.None),
        new(54, "CargoShield", "UI_SCAN_BLOCKER_LABEL", HotActionParameterKind.None),
        new(55, "CallRocket", "UI_QUICK_MENU_CALL_ROCKET", HotActionParameterKind.None)
    ];

    /// <summary>Finds the action metadata for an enum name, or null when unknown.</summary>
    /// <param name="name">The enum name stored in the save.</param>
    internal static HotActionInfo? FindAction(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var action in Actions)
        {
            if (string.Equals(action.Name, name, StringComparison.Ordinal)) return action;
        }
        return null;
    }

    /// <summary>
    /// Returns the display name for an action: the game localisation value when a label key is
    /// known and a service is supplied, otherwise a spaced version of the enum name.
    /// </summary>
    /// <param name="action">The action metadata.</param>
    /// <param name="localisation">Optional game localisation service.</param>
    internal static string GetDisplayName(HotActionInfo action, LocalisationService? localisation = null)
    {
        if (!string.IsNullOrEmpty(action.LabelKey) && localisation != null)
        {
            string? text = ProcTechLogic.LookupGameString(localisation, action.LabelKey);
            if (!string.IsNullOrEmpty(text)) return text;
        }
        return Prettify(action.Name);
    }

    /// <summary>Returns the HotActions array, creating it with three contexts when missing.</summary>
    /// <param name="playerState">The PlayerStateData object.</param>
    internal static JsonArray EnsureContexts(JsonObject playerState)
    {
        var hot = playerState.GetArray("HotActions");
        if (hot == null)
        {
            hot = new JsonArray();
            playerState.Set("HotActions", hot);
        }
        while (hot.Length < ContextCount)
            hot.Add(new JsonObject());
        return hot;
    }

    /// <summary>Returns a context object, creating its ten slots when missing.</summary>
    /// <param name="playerState">The PlayerStateData object.</param>
    /// <param name="contextIndex">Context index (0 OnFoot, 1 InShip, 2 InExocraft).</param>
    internal static JsonObject EnsureContext(JsonObject playerState, int contextIndex)
    {
        var hot = EnsureContexts(playerState);
        var context = hot.GetObject(Math.Clamp(contextIndex, 0, ContextCount - 1));
        EnsureSlots(context);
        return context;
    }

    /// <summary>Returns the KeyActions array of a context, padding it to ten slots.</summary>
    /// <param name="context">A HotActions context object.</param>
    internal static JsonArray EnsureSlots(JsonObject context)
    {
        var slots = context.GetArray("KeyActions");
        if (slots == null)
        {
            slots = new JsonArray();
            context.Set("KeyActions", slots);
        }
        while (slots.Length < SlotsPerContext)
            slots.Add(CreateEmptySlot());
        return slots;
    }

    /// <summary>Creates an unbound slot (<c>None</c> with empty parameters).</summary>
    internal static JsonObject CreateEmptySlot()
    {
        var slot = new JsonObject();
        SetSlot(slot, "None");
        return slot;
    }

    /// <summary>Writes an action and its parameters into a slot object.</summary>
    /// <param name="slot">The slot to update.</param>
    /// <param name="actionName">Action enum name (for example <c>PhotoMode</c>).</param>
    /// <param name="id">Id value; <c>^</c> when unused.</param>
    /// <param name="number">Number value.</param>
    /// <param name="indexX">InventoryIndex X.</param>
    /// <param name="indexY">InventoryIndex Y.</param>
    internal static void SetSlot(JsonObject slot, string actionName, string id = "^", int number = 0,
        int indexX = -1, int indexY = -1)
    {
        var action = slot.GetObject("Action");
        if (action == null)
        {
            action = new JsonObject();
            slot.Set("Action", action);
        }
        action.Set("QuickMenuActions", actionName);
        slot.Set("Id", id);
        slot.Set("Number", number);

        var index = slot.GetObject("InventoryIndex");
        if (index == null)
        {
            index = new JsonObject();
            slot.Set("InventoryIndex", index);
        }
        index.Set("X", indexX);
        index.Set("Y", indexY);
    }

    /// <summary>Clears a slot back to <c>None</c>.</summary>
    /// <param name="slot">The slot to clear.</param>
    internal static void ClearSlot(JsonObject slot) => SetSlot(slot, "None");

    /// <summary>Reads the action enum name from a slot.</summary>
    /// <param name="slot">The slot object, or null.</param>
    internal static string GetActionName(JsonObject? slot)
        => slot?.GetObject("Action")?.GetString("QuickMenuActions") ?? "None";

    /// <summary>Reads the Id value from a slot.</summary>
    /// <param name="slot">The slot object, or null.</param>
    internal static string GetId(JsonObject? slot) => slot?.GetString("Id") ?? "^";

    /// <summary>Reads the Number value from a slot (0 when missing or invalid).</summary>
    /// <param name="slot">The slot object, or null.</param>
    internal static int GetNumber(JsonObject? slot)
    {
        try { return slot?.GetInt("Number") ?? 0; }
        catch { return 0; }
    }

    /// <summary>Reads the InventoryIndex values from a slot (-1, -1 when missing or invalid).</summary>
    /// <param name="slot">The slot object, or null.</param>
    internal static (int X, int Y) GetInventoryIndex(JsonObject? slot)
    {
        var index = slot?.GetObject("InventoryIndex");
        if (index == null) return (-1, -1);
        try { return (index.GetInt("X"), index.GetInt("Y")); }
        catch { return (-1, -1); }
    }

    /// <summary>
    /// Builds the export object: three named arrays of ten slot objects. Unknown fields on the
    /// slot objects are preserved.
    /// </summary>
    /// <param name="playerState">The PlayerStateData object.</param>
    internal static JsonObject BuildExport(JsonObject playerState)
    {
        var root = new JsonObject();
        var hot = playerState.GetArray("HotActions");
        for (int c = 0; c < ContextCount; c++)
        {
            var array = new JsonArray();
            var context = hot != null && c < hot.Length ? hot.GetObject(c) : null;
            var slots = context?.GetArray("KeyActions");
            for (int s = 0; s < SlotsPerContext; s++)
            {
                object? slot = slots != null && s < slots.Length ? slots.Get(s) : null;
                array.Add(slot is JsonObject obj ? obj.DeepClone() : CreateEmptySlot());
            }
            root.Set(ContextNames[c], array);
        }
        return root;
    }

    /// <summary>
    /// Applies imported hotkey data. Accepts the export shape (named context arrays) and the
    /// save shape (<c>HotActions</c> array of <c>KeyActions</c> contexts, or a bare array of
    /// contexts). Existing contexts are normalised to ten slots.
    /// </summary>
    /// <param name="playerState">The PlayerStateData object.</param>
    /// <param name="imported">The imported JSON object.</param>
    /// <returns>The number of slots applied.</returns>
    internal static int ApplyImport(JsonObject playerState, JsonObject imported)
    {
        var sources = new List<JsonArray?>(ContextCount);
        foreach (string name in ContextNames)
            sources.Add(imported.GetArray(name));

        if (sources.All(s => s == null))
        {
            sources.Clear();
            var hot = imported.GetArray("HotActions");
            if (hot != null)
            {
                for (int i = 0; i < ContextCount && i < hot.Length; i++)
                    sources.Add(hot.GetObject(i)?.GetArray("KeyActions"));
            }
            else
            {
                foreach (string name in imported.Names())
                {
                    var array = imported.GetArray(name);
                    if (array == null) continue;
                    sources.Add(array);
                    break;
                }
            }
        }

        int applied = 0;
        for (int c = 0; c < ContextCount && c < sources.Count; c++)
        {
            var source = sources[c];
            if (source == null) continue;

            var slots = EnsureSlots(EnsureContext(playerState, c));
            for (int s = 0; s < SlotsPerContext && s < source.Length; s++)
            {
                if (source.Get(s) is not JsonObject slot) continue;
                slots.Set(s, slot.DeepClone());
                applied++;
            }
        }
        return applied;
    }

    /// <summary>Converts an enum name into spaced words (for example <c>CallRocket</c>).</summary>
    private static string Prettify(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var builder = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                builder.Append(' ');
            builder.Append(c);
        }
        return builder.ToString();
    }
}
