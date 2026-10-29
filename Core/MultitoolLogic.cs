using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using System.Reflection;

namespace NMSE.Core;

/// <summary>
/// Handles multitool data operations including loading, saving, type/class management, and inventory export.
/// </summary>
internal static class MultitoolLogic
{
    /// <summary>
    /// Available multitool class grades, ordered from lowest to highest.
    /// </summary>
    internal static readonly string[] ToolClasses = { "C", "B", "A", "S" };

    /// <summary>
    /// Known multitool types with their display names and corresponding resource filenames.
    /// </summary>
    /// <summary>Resource path used by all non-unique-model multitools.</summary>
    private const string SharedModelResource = "MODELS/COMMON/WEAPONS/MULTITOOL/MULTITOOL.SCENE.MBIN";

    internal static readonly (string Name, string Filename)[] ToolTypes = new[]
    {
        ("Standard", SharedModelResource),
        ("Rifle", SharedModelResource),
        ("Royal", "MODELS/COMMON/WEAPONS/MULTITOOL/ROYALMULTITOOL.SCENE.MBIN"),
        ("Alien", SharedModelResource),
        ("Pristine", SharedModelResource),
        ("Experimental", SharedModelResource),
        ("Sentinel", "MODELS/COMMON/WEAPONS/MULTITOOL/SENTINELMULTITOOL.SCENE.MBIN"),
        ("Sentinel B", "MODELS/COMMON/WEAPONS/MULTITOOL/SENTINELMULTITOOLB.SCENE.MBIN"),
        ("Switch", "MODELS/COMMON/WEAPONS/MULTITOOL/SWITCHMULTITOOL.SCENE.MBIN"),
        ("Staff", "MODELS/COMMON/WEAPONS/MULTITOOL/STAFFMULTITOOL.SCENE.MBIN"),
        ("Staff NPC", "MODELS/COMMON/WEAPONS/MULTITOOL/STAFFNPCMULTITOOL.SCENE.MBIN"),
        ("Staff Ruin", "MODELS/COMMON/WEAPONS/MULTITOOL/STAFFMULTITOOLRUIN.SCENE.MBIN"),
        ("Staff Bone", "MODELS/COMMON/WEAPONS/MULTITOOL/STAFFMULTITOOLBONE.SCENE.MBIN"),
        ("Atlantid", "MODELS/COMMON/WEAPONS/MULTITOOL/ATLASMULTITOOL.SCENE.MBIN"),
        ("Voltaic Staff", "MODELS/COMMON/WEAPONS/MULTITOOL/STAFFMULTITOOLATLAS.SCENE.MBIN"),
        ("Direwasp Disintegrator", "MODELS/COMMON/WEAPONS/MULTITOOL/SWARMMULTITOOL.SCENE.MBIN"),
        ("Starbound", "MODELS/COMMON/WEAPONS/MULTITOOL/RETROMULTITOOL.SCENE.MBIN"),
        ("Larva Cannon", "MODELS/COMMON/WEAPONS/MULTITOOL/GRUBCANNONMULTITOOL.SCENE.MBIN"),
    };

    internal static readonly Dictionary<string, string> ToolTypeLocKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Standard"] = "multitool.type_standard",
        ["Rifle"] = "multitool.type_rifle",
        ["Royal"] = "multitool.type_royal",
        ["Alien"] = "multitool.type_alien",
        ["Pristine"] = "multitool.type_pristine",
        ["Experimental"] = "multitool.type_experimental",
        ["Sentinel"] = "multitool.type_sentinel",
        ["Sentinel B"] = "multitool.type_sentinel_b",
        ["Switch"] = "multitool.type_switch",
        ["Staff"] = "multitool.type_staff",
        ["Staff NPC"] = "multitool.type_staff_npc",
        ["Staff Ruin"] = "multitool.type_staff_ruin",
        ["Staff Bone"] = "multitool.type_staff_bone",
        ["Atlantid"] = "multitool.type_atlantid",
        ["Voltaic Staff"] = "multitool.type_voltaic_staff",
		["Direwasp Disintegrator"] = "multitool.type_direwasp",
		["Starbound"] = "multitool.type_starbound",
		["Larva Cannon"] = "multitool.type_larva_cannon",
	};

    internal static string GetLocalisedToolTypeName(string internalName)
    {
        if (ToolTypeLocKeys.TryGetValue(internalName, out var key))
            return UiStrings.Get(key);
        return internalName;
    }

    internal sealed class ToolTypeItem
    {
        public string InternalName { get; }
        public string DisplayName { get; }
        public ToolTypeItem(string internalName, string displayName) { InternalName = internalName; DisplayName = displayName; }
        public override string ToString() => DisplayName;
    }

    internal static ToolTypeItem[] GetToolTypeItems()
    {
        return ToolTypes.Select(t => new ToolTypeItem(t.Name, GetLocalisedToolTypeName(t.Name))).ToArray();
    }

    /// <summary>
    /// Returns the canonical IsLarge combobox index for the given type name,
    /// or -1 when the type does not prescribe a specific value.
    /// Index 0 = "1" = <c>true</c>; index 1 = "2" = <c>false</c>.
    /// </summary>
    internal static int GetCanonicalIsLargeIndex(string typeName)
    {
        if (string.Equals(typeName, "Standard", StringComparison.Ordinal)) return 0; // IsLarge = true
        if (string.Equals(typeName, "Rifle", StringComparison.Ordinal)) return 1;    // IsLarge = false
        return -1;
    }

    /// <summary>Returns the two localised display strings for the IsLarge combobox.</summary>
    internal static string[] GetToolSizeItems()
    {
        return new[]
        {
            UiStrings.Get("multitool.size_1"),
            UiStrings.Get("multitool.size_2"),
        };
    }

    /// <summary>
    /// Builds a list of owned multitools from the multitools JSON array, skipping empty slots.
    /// </summary>
    /// <param name="multitools">The JSON array of multitool entries.</param>
    /// <returns>A list of tool items with display names and data indices.</returns>
    internal static List<ToolListItem> BuildToolList(JsonArray multitools)
    {
        var list = new List<ToolListItem>();
        for (int i = 0; i < multitools.Length; i++)
        {
            try
            {
                var tool = multitools.GetObject(i);
                var seedArr = tool?.GetArray("Seed");
                bool hasSeed = false;
                try { hasSeed = seedArr != null && seedArr.Length > 0 && seedArr.GetBool(0); }
                catch { }

                if (!hasSeed) continue;

                string name = tool?.GetString("Name") ?? "";
                if (string.IsNullOrEmpty(name))
                    name = UiStrings.Format("multitool.default_name", i + 1);
                list.Add(new ToolListItem(name, i));
            }
            catch
            {
                list.Add(new ToolListItem(UiStrings.Format("multitool.default_name", i + 1), i));
            }
        }
        return list;
    }

    /// <summary>
    /// Loads multitool data from a JSON tool object for display and editing.
    /// </summary>
    /// <param name="tool">The JSON object representing the multitool.</param>
    /// <returns>A populated <see cref="ToolData"/> instance.</returns>
    internal static ToolData LoadToolData(JsonObject tool)
    {
        string name = tool.GetString("Name") ?? "";

        string filename = "";
        try
        {
            // NMS 3.81+ (Sentinel): multitool resource is under Resource.Filename
            var resource = tool.GetObject("Resource");
            filename = resource?.GetString("Filename") ?? "";
        }
        catch { }
        int typeIndex = Array.FindIndex(ToolTypes, t => t.Filename.Equals(filename, StringComparison.OrdinalIgnoreCase));

        string cls = "";
        try
        {
            var store = tool.GetObject("Store");
            var classObj = store?.GetObject("Class");
            cls = classObj?.GetString("InventoryClass") ?? "";
        }
        catch { }
        int classIndex = Array.IndexOf(ToolClasses, cls);

        string seed = "";
        try
        {
            var seedArr = tool.GetArray("Seed");
            if (seedArr != null && seedArr.Length > 1)
                seed = seedArr.Get(1)?.ToString() ?? "";
        }
        catch { }

        var toolStore = tool.GetObject("Store");
        double damage = 0, mining = 0, scan = 0;
        string damageText = "0", miningText = "0", scanText = "0";
        try { damage = StatHelper.ReadBaseStatValue(toolStore, "^WEAPON_DAMAGE"); damageText = StatHelper.ReadBaseStatText(toolStore, "^WEAPON_DAMAGE"); } catch { }
        try { mining = StatHelper.ReadBaseStatValue(toolStore, "^WEAPON_MINING"); miningText = StatHelper.ReadBaseStatText(toolStore, "^WEAPON_MINING"); } catch { }
        try { scan = StatHelper.ReadBaseStatValue(toolStore, "^WEAPON_SCAN"); scanText = StatHelper.ReadBaseStatText(toolStore, "^WEAPON_SCAN"); } catch { }

        // Shared-model type detection: when filename matches MULTITOOL.SCENE.MBIN,
        // Standard (0), Rifle (1), Alien (3), and Pristine (4) all share the same resource.
        // Uses damage/mining/scan against known per-class BaseStat ranges from the game database.
        //   Pistol.C.DAMAGE.Min = 0     Rifle.C.MINING.Min  = 0
        //   Rifle.C.SCAN.Max    = 5     Pistol.C.SCAN.Max   = 20
        //   Pristine.C.SCAN.Min = 40    Pristine.A.SCAN.Min = 80
        if (typeIndex == 0 && string.Equals(filename, SharedModelResource, StringComparison.OrdinalIgnoreCase) && classIndex >= 0)
        {
            if (damage == 0.0)
            {
                if (classIndex == 0) // C class
                {
                    if (mining == 0.0)
                        typeIndex = 1; // Rifle
                    else if (scan > 20.0)
                        typeIndex = 4; // Pristine
                    else
                        typeIndex = 0; // Standard (Pistol)
                }
                else
                {
                    typeIndex = 0; // Standard (Pistol)
                }
            }
            else if (mining == 0.0)
            {
                if (classIndex == 0 && scan > 5.0)
                    typeIndex = 3; // Alien
                else
                    typeIndex = 1; // Rifle
            }
            else if (classIndex <= 1) // C or B class
            {
                if (scan < 40.0)
                    typeIndex = 3; // Alien
                else
                    typeIndex = 4; // Pristine
            }
            else // A or S class
            {
                if (scan >= 80.0)
                    typeIndex = 4; // Pristine
                else
                    typeIndex = 3; // Alien
            }
        }

        string safeName = StringHelper.SanitizeFileName(name);
        string cls2 = classIndex >= 0 ? ToolClasses[classIndex] : "C";

        bool? isLarge = null;
        try { isLarge = tool.GetBool("IsLarge"); } catch { }

        return new ToolData
        {
            Name = name,
            TypeIndex = typeIndex >= 0 ? typeIndex : 0,
            ClassIndex = classIndex,
            Seed = seed,
            Damage = damage,
            Mining = mining,
            Scan = scan,
            DamageText = damageText,
            MiningText = miningText,
            ScanText = scanText,
            Store = toolStore,
            IsLarge = isLarge,
            ExportFileName = ExportConfig.BuildFileName(
                ExportConfig.Instance.MultitoolTemplate,
                ExportConfig.Instance.MultitoolExt,
                new Dictionary<string, string>
                {
                    ["multitool_name"] = safeName,
                    ["type"] = typeIndex >= 0 && typeIndex < ToolTypes.Length ? ToolTypes[typeIndex].Name : "Unknown",
                    ["class"] = cls2
                })
        };
    }

    /// <summary>
    /// Saves multitool data back to the JSON tool object and player state.
    /// </summary>
    /// <param name="tool">The JSON object representing the multitool.</param>
    /// <param name="playerState">The player state for primary weapon syncing.</param>
    /// <param name="values">The values to write.</param>
    /// <param name="isPrimary">Whether this tool is the active/primary multitool.</param>
    internal static void SaveToolData(JsonObject tool, JsonObject? playerState, ToolSaveValues values, bool isPrimary)
    {
        // Always write name (allow empty string to clear name)
        tool.Set("Name", values.Name ?? "");

        if (values.ClassIndex >= 0 && values.ClassIndex != values.OriginalClassIndex)
        {
            string cls = ToolClasses[values.ClassIndex];
            // Set class on all multitool inventories (Store, Store_TechOnly)
            var store = tool.GetObject("Store");
            var classObj = store?.GetObject("Class");
            classObj?.Set("InventoryClass", cls);
            var techStore = tool.GetObject("Store_TechOnly");
            var techClassObj = techStore?.GetObject("Class");
            techClassObj?.Set("InventoryClass", cls);
        }

        if (values.TypeIndex >= 0)
        {
            // NMS 3.81+ (Sentinel): multitool resource is under Resource.Filename
            var resource = tool.GetObject("Resource");
            resource?.Set("Filename", ToolTypes[values.TypeIndex].Filename);

            // If primary tool, keep CurrentWeapon.Filename in sync with the new model.
            // The game's own saves always have CurrentWeapon matching the active tool;
            // a mismatch makes the game rebuild the equipped multitool on load.
            if (isPrimary && playerState != null)
            {
                try
                {
                    var currentWeapon = playerState.GetObject("CurrentWeapon");
                    currentWeapon?.Set("Filename", ToolTypes[values.TypeIndex].Filename);
                }
                catch { }
            }
        }

        // IsLarge controls the body shape for the shared-model multitools (MULTITOOL.SCENE.MBIN).
        // The value is driven by the IsLarge combobox (index 0 = true/"1", index 1 = false/"2"),
        // which is automatically updated to the canonical value when the type changes.
        if (values.IsLargeIndex == 0)
            tool.Set("IsLarge", true);
        else if (values.IsLargeIndex == 1)
            tool.Set("IsLarge", false);
        // IsLargeIndex = -1: no selection - leave the save value untouched.

        try
        {
            var seedArr = tool.GetArray("Seed");
            var normalizedSeed = SeedHelper.NormalizeSeed(values.Seed);
            if (seedArr != null && seedArr.Length > 1 && normalizedSeed != null)
            {
                seedArr.Set(1, normalizedSeed);

                // If primary tool, also sync seed to CurrentWeapon.GenerationSeed[1]
                if (isPrimary && playerState != null)
                {
                    try
                    {
                        var currentWeapon = playerState.GetObject("CurrentWeapon");
                        var genSeed = currentWeapon?.GetArray("GenerationSeed");
                        if (genSeed != null && genSeed.Length > 1)
                            genSeed.Set(1, normalizedSeed);
                    }
                    catch { }
                }
            }
        }
        catch { }

        var toolStore = tool.GetObject("Store");
        double writeDamage = Data.BaseStatLimits.ConditionalClampStatValue("Normal", "^WEAPON_DAMAGE", values.Damage, Data.StatCategory.Weapon, values.RawStatValues);
        double writeMining = Data.BaseStatLimits.ConditionalClampStatValue("Normal", "^WEAPON_MINING", values.Mining, Data.StatCategory.Weapon, values.RawStatValues);
        double writeScan = Data.BaseStatLimits.ConditionalClampStatValue("Normal", "^WEAPON_SCAN", values.Scan, Data.StatCategory.Weapon, values.RawStatValues);

        // Only preserve display text when clamping did not alter the value.
        string? damageText = values.Damage == writeDamage ? values.DamageText : null;
        string? miningText = values.Mining == writeMining ? values.MiningText : null;
        string? scanText = values.Scan == writeScan ? values.ScanText : null;

        StatHelper.WriteBaseStatValue(toolStore, "^WEAPON_DAMAGE", writeDamage, damageText);
        StatHelper.WriteBaseStatValue(toolStore, "^WEAPON_MINING", writeMining, miningText);
        StatHelper.WriteBaseStatValue(toolStore, "^WEAPON_SCAN", writeScan, scanText);

        // If primary tool, sync Store to WeaponInventory in PlayerStateData
        // This keeps the game's live inventory copy in sync with the tool data
        if (isPrimary && playerState != null && toolStore != null)
        {
            try { playerState.Set("WeaponInventory", toolStore); }
            catch { }
        }
    }

    /// <summary>
    /// Fully resets a multitool slot by clearing its seed, name, resource, and all
    /// inventory data. The entry remains in the Multitools array to preserve index
    /// alignment. We clear every field in-place so no remnant data is left behind.
    /// </summary>
    /// <param name="tool">The JSON object representing the multitool to delete.</param>
    internal static void DeleteToolData(JsonObject tool)
    {
        // 1. Invalidate the seed - marks the slot as empty
        var seedArr = tool.GetArray("Seed");
        if (seedArr != null && seedArr.Length > 1)
        {
            seedArr.Set(0, false);
            seedArr.Set(1, "0x0");
        }

        // 2. Clear the name
        tool.Set("Name", "");

        // 3. Clear the resource (filename + seed + AltId + ProceduralTexture)
        var resource = tool.GetObject("Resource");
        if (resource != null)
        {
            resource.Set("Filename", "");
            var resSeed = resource.GetArray("Seed");
            if (resSeed != null && resSeed.Length > 1)
            {
                resSeed.Set(0, false);
                resSeed.Set(1, "0x0");
            }
            try { resource.Set("AltId", ""); } catch { }
            try
            {
                var pt = resource.GetObject("ProceduralTexture");
                if (pt != null)
                    ClearJsonArray(pt.GetArray("Samplers"));
            }
            catch { }
        }

        // 4. Clear both inventory objects (Store and Store_TechOnly)
        ResetInventoryObject(tool.GetObject("Store"));
        ResetInventoryObject(tool.GetObject("Store_TechOnly"));
    }

    /// <summary>
    /// Resets an inventory JSON object by clearing its Slots, ValidSlotIndices,
    /// BaseStatValues, and SpecialSlots arrays while preserving the object structure.
    /// </summary>
    private static void ResetInventoryObject(JsonObject? inventory)
    {
        if (inventory == null) return;

        ClearJsonArray(inventory.GetArray("Slots"));
        ClearJsonArray(inventory.GetArray("ValidSlotIndices"));
        ClearJsonArray(inventory.GetArray("BaseStatValues"));
        ClearJsonArray(inventory.GetArray("SpecialSlots"));
    }

    private static void ClearJsonArray(JsonArray? arr)
    {
        if (arr == null) return;
        for (int i = arr.Length - 1; i >= 0; i--)
            arr.RemoveAt(i);
    }

    /// <summary>
    /// Counts the number of valid (non-invalidated) multitools in the array.
    /// A tool is valid when its Seed[0] is true.
    /// </summary>
    internal static int CountValidTools(JsonArray multitools)
    {
        int count = 0;
        for (int i = 0; i < multitools.Length; i++)
        {
            try
            {
                var tool = multitools.GetObject(i);
                var seedArr = tool?.GetArray("Seed");
                if (seedArr != null && seedArr.Length > 0 && seedArr.GetBool(0))
                    count++;
            }
            catch { }
        }
        return count;
    }

    /// <summary>
    /// Returns the array index of the first valid (non-invalidated) multitool, or -1 if none.
    /// </summary>
    internal static int FindFirstValidToolIndex(JsonArray multitools)
    {
        for (int i = 0; i < multitools.Length; i++)
        {
            try
            {
                var tool = multitools.GetObject(i);
                var seedArr = tool?.GetArray("Seed");
                if (seedArr != null && seedArr.Length > 0 && seedArr.GetBool(0))
                    return i;
            }
            catch { }
        }
        return -1;
    }

    /// <summary>
    /// Finds the first empty multitool slot in the multitools array.
    /// </summary>
    /// <param name="multitools">The JSON array of multitool entries.</param>
    /// <returns>The index of the first empty slot, or -1 if all slots are occupied.</returns>
    internal static int FindEmptySlot(JsonArray multitools)
    {
        for (int i = 0; i < multitools.Length; i++)
        {
            try
            {
                var slot = multitools.GetObject(i);
                var seedArr = slot?.GetArray("Seed");
                bool hasSeed = false;
                try { hasSeed = seedArr != null && seedArr.Length > 0 && seedArr.GetBool(0); }
                catch { }
                if (!hasSeed) return i;
            }
            catch { }
        }
        return -1;
    }

    // --- Archive (ArchivedMultitools) helpers ---

    /// <summary>
    /// Determines whether an archived multitool slot is occupied.
    /// A slot is occupied when its MultitoolData.Seed[0] is true.
    /// </summary>
    internal static bool IsArchivedToolSlotOccupied(JsonObject archivedSlot)
    {
        try
        {
            var mtd = archivedSlot.GetObject("MultitoolData");
            var seed = mtd?.GetArray("Seed");
            if (seed != null && seed.Length > 0)
                return seed.GetBool(0);
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Finds the first empty slot in the ArchivedMultitools array.
    /// Returns -1 if all slots are occupied.
    /// </summary>
    internal static int FindEmptyArchivedToolSlot(JsonArray archivedTools)
    {
        for (int i = 0; i < archivedTools.Length; i++)
        {
            try
            {
                var slot = archivedTools.GetObject(i);
                if (!IsArchivedToolSlotOccupied(slot))
                    return i;
            }
            catch { }
        }
        return -1;
    }

    /// <summary>
    /// Builds a display list of occupied archived multitools for the import dialog.
    /// </summary>
    internal static List<ArchivedToolListItem> BuildArchivedToolList(JsonArray archivedTools)
    {
        var list = new List<ArchivedToolListItem>();
        for (int i = 0; i < archivedTools.Length; i++)
        {
            try
            {
                var slot = archivedTools.GetObject(i);
                if (!IsArchivedToolSlotOccupied(slot)) continue;

                string name = slot.GetString("ArchivedName") ?? "";
                string cls = "";
                try { cls = slot.GetObject("ArchivedInventoryClass")?.GetString("InventoryClass") ?? ""; } catch { }

                string filename = "";
                try { filename = slot.GetObject("MultitoolData")?.GetObject("Resource")?.GetString("Filename") ?? ""; } catch { }
                int typeIdx = Array.FindIndex(ToolTypes, t => t.Filename.Equals(filename, StringComparison.OrdinalIgnoreCase));
                string typeName = typeIdx >= 0 ? ToolTypes[typeIdx].Name : "";

                string display;
                if (string.IsNullOrEmpty(name))
                    display = string.IsNullOrEmpty(typeName) ? $"[{i + 1}] Multitool - {cls}" : $"[{i + 1}] {typeName} - {cls}";
                else
                    display = $"[{i + 1}] {name} - {cls}";

                list.Add(new ArchivedToolListItem(display, i));
            }
            catch { }
        }
        return list;
    }

    /// <summary>
    /// Resolves the archived <c>WeaponStatClass</c> value the game writes for a multitool.
    /// Unique-model tools map from their resource file; shared-model tools use the same
    /// stat-based Pistol/Rifle/Alien/Pristine detection as <see cref="LoadToolData"/>.
    /// </summary>
    /// <param name="tool">The JSON object representing the multitool.</param>
    /// <returns>The weapon stat class enum name (e.g. "Pistol", "Rifle").</returns>
    internal static string GetArchivedWeaponClass(JsonObject tool)
    {
        string filename = "";
        try { filename = tool.GetObject("Resource")?.GetString("Filename") ?? ""; } catch { }

        // Unique-model classes below match the game's own reward table (GcRewardSpecificWeapon),
        // which uses Rifle for the Switch and Swarm models and Staff for every staff variant.
        if (filename.Contains("SENTINELMULTITOOL", StringComparison.OrdinalIgnoreCase))
            return "Robot";
        if (filename.Contains("SWITCHMULTITOOL", StringComparison.OrdinalIgnoreCase))
            return "Rifle";
        if (filename.Contains("ROYALMULTITOOL", StringComparison.OrdinalIgnoreCase))
            return "Royal";
        if (filename.Contains("STAFF", StringComparison.OrdinalIgnoreCase))
            return "Staff";
        if (filename.Contains("ATLASMULTITOOL", StringComparison.OrdinalIgnoreCase))
            return "Atlas";
        if (filename.Contains("SWARMMULTITOOL", StringComparison.OrdinalIgnoreCase))
            return "Rifle";
        if (filename.Contains("RETROMULTITOOL", StringComparison.OrdinalIgnoreCase))
            return "Pistol";
        if (filename.Contains("GRUBCANNONMULTITOOL", StringComparison.OrdinalIgnoreCase))
            return "Alien";

        // Shared procedural model: the game derives the class from the base stats.
        var data = LoadToolData(tool);
        if (data.TypeIndex >= 0 && data.TypeIndex < ToolTypes.Length)
        {
            return ToolTypes[data.TypeIndex].Name switch
            {
                "Rifle" => "Rifle",
                "Alien" => "Alien",
                "Pristine" => "Pristine",
                _ => "Pistol",
            };
        }
        return "Pistol";
    }

    /// <summary>
    /// Moves a multitool from Multitools into an ArchivedMultitools slot.
    /// Copies the tool data into the archive slot and resets the source slot.
    /// </summary>
    internal static void MoveToolToArchive(JsonObject tool, JsonObject archivedSlot)
    {
        // Copy full tool data into MultitoolData.
        // DeepClone is required so that the subsequent DeleteToolData call on the source tool
        // does not corrupt the shared nested objects (Seed, Resource, inventories) in the archive slot.
        var mtd = archivedSlot.GetObject("MultitoolData");
        if (mtd != null)
        {
            var toolClone = tool.DeepClone();
            foreach (var key in toolClone.Names())
                mtd.Set(key, toolClone.Get(key));
        }

        // Set archive metadata
        archivedSlot.Set("ArchivedName", tool.GetString("Name") ?? "");

        // ArchivedInventoryClass: class from Store.Class
        try
        {
            string cls = tool.GetObject("Store")?.GetObject("Class")?.GetString("InventoryClass") ?? "C";
            var archivedInvClass = archivedSlot.GetObject("ArchivedInventoryClass");
            archivedInvClass?.Set("InventoryClass", cls);
        }
        catch { }

        // WeaponClass: match the value the game itself writes when archiving.
        try
        {
            var wc = archivedSlot.GetObject("WeaponClass");
            wc?.Set("WeaponStatClass", GetArchivedWeaponClass(tool));
        }
        catch { }

        // Reset the source tool slot
        DeleteToolData(tool);
    }

    /// <summary>
    /// Imports an archived multitool into a target Multitools slot.
    /// Copies tool data from the archive into the target slot and clears the archive slot.
    /// </summary>
    internal static void ImportToolFromArchive(JsonObject archivedSlot, JsonObject targetTool)
    {
        // Copy tool data from MultitoolData to target slot.
        // DeepClone is required so that the subsequent DeleteToolData call on the archive slot
        // does not corrupt the shared nested objects (Seed, Resource, inventories) in targetTool.
        var mtd = archivedSlot.GetObject("MultitoolData");
        if (mtd != null)
        {
            var mtdClone = mtd.DeepClone();
            foreach (var key in mtdClone.Names())
                targetTool.Set(key, mtdClone.Get(key));
        }

        // Clear the archive slot
        var archivedMtd = archivedSlot.GetObject("MultitoolData");
        if (archivedMtd != null)
            DeleteToolData(archivedMtd);

        // Reset archive metadata
        archivedSlot.Set("ArchivedName", "");
        try { archivedSlot.GetObject("ArchivedInventoryClass")?.Set("InventoryClass", "C"); } catch { }
        try { archivedSlot.GetObject("WeaponClass")?.Set("WeaponStatClass", "Pistol"); } catch { }
    }

    /// <summary>
    /// Represents an item in the archived multitool selection list for the import dialog.
    /// </summary>
    internal sealed class ArchivedToolListItem
    {
        public string DisplayName { get; }
        public int ArchiveIndex { get; }
        public ArchivedToolListItem(string displayName, int archiveIndex)
        {
            DisplayName = displayName;
            ArchiveIndex = archiveIndex;
        }
        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Returns the display name for the multitool at the given index.
    /// </summary>
    /// <param name="multitools">The JSON array of multitool entries.</param>
    /// <param name="primaryIndex">The index of the primary multitool.</param>
    /// <returns>The multitool name, or a fallback if unavailable.</returns>
    internal static string GetPrimaryToolName(JsonArray? multitools, int primaryIndex)
    {
        if (multitools == null || primaryIndex < 0 || primaryIndex >= multitools.Length)
            return UiStrings.Get("common.unknown");
        try
        {
            var tool = multitools.GetObject(primaryIndex);
            string name = tool.GetString("Name") ?? "";
            return string.IsNullOrEmpty(name) ? UiStrings.Format("multitool.default_name", primaryIndex + 1) : name;
        }
        catch { return UiStrings.Get("common.unknown"); }
    }

    /// <summary>
    /// Represents an item in the multitool selection list.
    /// </summary>
    internal sealed class ToolListItem
    {
        /// <summary>The display name shown for this multitool.</summary>
        public string DisplayName { get; set; }
        /// <summary>The index of this multitool in the multitools array.</summary>
        public int DataIndex { get; }

        /// <summary>
        /// Initializes a new tool list item.
        /// </summary>
        /// <param name="displayName">The display name for the multitool.</param>
        /// <param name="dataIndex">The index in the multitools array.</param>
        public ToolListItem(string displayName, int dataIndex)
        {
            DisplayName = displayName;
            DataIndex = dataIndex;
        }

        /// <inheritdoc/>
        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Holds loaded multitool data for display and editing in the UI.
    /// </summary>
    internal sealed class ToolData
    {
        /// <summary>The player-assigned multitool name.</summary>
        public string Name { get; set; } = "";
        /// <summary>Index into <see cref="ToolTypes"/> for the multitool type.</summary>
        public int TypeIndex { get; set; }
        /// <summary>Index into <see cref="ToolClasses"/> for the class grade.</summary>
        public int ClassIndex { get; set; } = -1;
        /// <summary>The multitool's procedural generation seed as a hex string.</summary>
        public string Seed { get; set; } = "";
        /// <summary>The multitool's base damage stat.</summary>
        public double Damage { get; set; }
        /// <summary>The multitool's base mining stat.</summary>
        public double Mining { get; set; }
        /// <summary>The multitool's base scan stat.</summary>
        public double Scan { get; set; }
        /// <summary>The original text representation of the damage stat from the save file.</summary>
        public string DamageText { get; set; } = "0";
        /// <summary>The original text representation of the mining stat from the save file.</summary>
        public string MiningText { get; set; } = "0";
        /// <summary>The original text representation of the scan stat from the save file.</summary>
        public string ScanText { get; set; } = "0";
        /// <summary>The multitool's store (inventory) JSON object.</summary>
        public JsonObject? Store { get; set; }
        /// <summary>The IsLarge field read from the save, or <c>null</c> if not present.</summary>
        public bool? IsLarge { get; set; }
        /// <summary>Suggested filename for exporting the inventory.</summary>
        public string ExportFileName { get; set; } = "";
    }

    /// <summary>
    /// Holds values to be saved back to a multitool's JSON data.
    /// </summary>
    internal sealed class ToolSaveValues
    {
        /// <summary>The multitool name to set.</summary>
        public string Name { get; set; } = "";
        /// <summary>Index into <see cref="ToolTypes"/> for the desired type.</summary>
        public int TypeIndex { get; set; } = -1;
        /// <summary>Index into <see cref="ToolClasses"/> for the desired class grade.</summary>
        public int ClassIndex { get; set; } = -1;
        /// <summary>Original class index loaded from save; used to skip class writes when unchanged.</summary>
        public int OriginalClassIndex { get; set; } = -1;
        /// <summary>The seed hex string to set.</summary>
        public string Seed { get; set; } = "";
        /// <summary>The damage stat value to write.</summary>
        public double Damage { get; set; }
        /// <summary>The mining stat value to write.</summary>
        public double Mining { get; set; }
        /// <summary>The scan stat value to write.</summary>
        public double Scan { get; set; }
        /// <summary>Display text for the damage stat, used to create a RawDouble on save.</summary>
        public string? DamageText { get; set; }
        /// <summary>Display text for the mining stat, used to create a RawDouble on save.</summary>
        public string? MiningText { get; set; }
        /// <summary>Display text for the scan stat, used to create a RawDouble on save.</summary>
        public string? ScanText { get; set; }

        /// <summary>Raw (unclamped) stat values read from JSON at load time.
        /// When set, each stat is only written if the UI value differs from
        /// the clamped raw value - preserving externally-edited values.</summary>
        public Dictionary<string, double>? RawStatValues { get; set; }

        /// <summary>
        /// Index into the IsLarge combobox: 0 = true ("1"), 1 = false ("2"), -1 = no change.
        /// </summary>
        public int IsLargeIndex { get; set; } = -1;
    }
}
