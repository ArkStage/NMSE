using System.Globalization;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Core;

/// <summary>
/// Implements the technology pack rules: which installed technologies can be packaged, how
/// packed technologies are written to inventories, and how packs are unpacked back into
/// technology slots. Also encodes and decodes the hashed IDs used for stored packs (a
/// one-at-a-time hash rendered as six bytes, with escapes for zero and '#' bytes), verified
/// against all 736 mined pack hashes.
/// </summary>
internal static class TechPackLogic
{
    // Packed algorithm constants (see the private notes for the layout and regeneration recipe).
    private static readonly byte[] Mask =
    [
        0x64, 0x1B, 0x8D, 0xF2, 0x37, 0xAE, 0x50, 0xC9, 0x05, 0x7E, 0xD3, 0x48, 0x9A, 0x26, 0xBB, 0x71,
    ];

    private static readonly byte[] Packed =
    [
        0x65, 0x1F, 0x8D, 0xF2, 0x3E, 0xAE, 0x50, 0xC9, 0x04, 0xFE, 0xD3, 0x48, 0x1A, 0x36, 0x9B, 0x51,
        0x3C, 0x38,
    ];

    private static readonly byte[] Raw = Unmask(Packed);

    private static readonly uint HashMul1 = BitConverter.ToUInt32(Raw, 0);
    private static readonly uint HashMul2 = BitConverter.ToUInt32(Raw, 4);
    private static readonly uint HashMul3 = BitConverter.ToUInt32(Raw, 8);
    private static readonly byte FlagBase = Raw[12];
    private static readonly byte FlagBitEven = Raw[13];
    private static readonly byte FlagBitOdd = Raw[14];
    private static readonly byte CaseFold = Raw[15];
    private static readonly byte EscapeChar = Raw[16];
    private static readonly byte HashChar = Raw[17];

    private static byte[] Unmask(byte[] data)
    {
        var raw = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            raw[i] = (byte)(data[i] ^ Mask[i & 15]);
        return raw;
    }

    /// <summary>
    /// ID prefixes for the ship weapon technologies the game treats as core ship weapons.
    /// Used only for the "last intact weapon" guard (the item database does not expose the
    /// technology's BaseStat).
    /// </summary>
    private static readonly string[] ShipWeaponPrefixes =
    {
        "SHIPGUN", "SHIPLAS", "SHIPMIS", "SHIPROCK", "SHIPSHOT", "SHIPMINI", "SHIPPLAS",
        "UP_GUN", "UP_LAS", "UP_MIS", "UP_ROCK", "UP_SHOT", "UP_MINI", "UP_PLAS"
    };

    /// <summary>
    /// A destination inventory offered for packing or unpacking, with a localisation
    /// key for its display name.
    /// </summary>
    internal sealed class TechPackDestination
    {
        /// <summary>Localisation key for the destination display name.</summary>
        public required string NameKey { get; init; }

        /// <summary>Arguments for the display name format string.</summary>
        public required object[] NameArgs { get; init; }

        /// <summary>The destination inventory JSON object.</summary>
        public required JsonObject Inventory { get; init; }

        /// <summary>The destination inventory's Slots array.</summary>
        public required JsonArray Slots { get; init; }

        /// <summary>Inventory group used for stack size rules (e.g. "ShipCargo").</summary>
        public required string Group { get; init; }

        /// <summary>True for technology inventories, false for cargo inventories.</summary>
        public required bool IsTech { get; init; }
    }

    /// <summary>
    /// Determines whether an installed technology can be packaged, mirroring the game's
    /// CanPackagePlayerTechItem: core technologies, repair technology placeholders and the
    /// last intact ship weapon cannot be packed.
    /// </summary>
    /// <param name="tech">The resolved technology item; null when unknown.</param>
    /// <param name="slotItemId">The stored item ID from the source slot.</param>
    /// <param name="damageFactor">The source slot's DamageFactor (1.0 = broken).</param>
    /// <param name="sourceSlots">All slots of the source technology inventory, for the last-weapon guard.</param>
    /// <returns>True when the technology can be packed.</returns>
    internal static bool CanPackageTech(GameItem? tech, string? slotItemId, double damageFactor, JsonArray? sourceSlots)
    {
        if (tech == null)
            return false;
        if (tech.IsCore)
            return false;

        string bare = StripItemId(slotItemId);
        if (bare.Contains("SLOT_DMG", StringComparison.OrdinalIgnoreCase))
            return false;

        return !IsLoneIntactCoreShipWeapon(sourceSlots, slotItemId, damageFactor);
    }

    /// <summary>True when the item ID names a technology the game counts as a core ship weapon.</summary>
    internal static bool IsCoreShipWeaponId(string? itemId)
    {
        string bare = StripItemId(itemId);
        if (bare.Length == 0)
            return false;
        foreach (string prefix in ShipWeaponPrefixes)
        {
            if (bare.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when the slot holds a core ship weapon that is intact and is the only intact
    /// core ship weapon in its inventory (the game prevents packing this last weapon).
    /// </summary>
    internal static bool IsLoneIntactCoreShipWeapon(JsonArray? sourceSlots, string? slotItemId, double damageFactor)
    {
        if (!IsCoreShipWeaponId(slotItemId))
            return false;
        if (damageFactor >= 1.0)
            return false;
        return CountIntactCoreShipWeapons(sourceSlots) <= 1;
    }

    /// <summary>Counts intact (DamageFactor below 1.0) core ship weapons in a technology inventory.</summary>
    internal static int CountIntactCoreShipWeapons(JsonArray? slots)
    {
        if (slots == null)
            return 0;
        int count = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            JsonObject? slot;
            try { slot = slots.GetObject(i); }
            catch { continue; }
            if (slot == null)
                continue;
            string? id = slot.GetString("Id");
            if (!IsCoreShipWeaponId(id))
                continue;
            double damage;
            try { damage = slot.GetDouble("DamageFactor"); }
            catch { damage = 0.0; }
            if (damage < 1.0)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Enumerates the destination inventories of the requested kind that have at least one
    /// free valid slot, with localisation keys for their display names.
    /// </summary>
    /// <param name="playerState">The save's PlayerStateData object.</param>
    /// <param name="tech">True to enumerate technology inventories, false for cargo inventories.</param>
    /// <returns>The available destinations.</returns>
    internal static List<TechPackDestination> EnumerateDestinations(JsonObject playerState, bool tech)
    {
        var results = new List<TechPackDestination>();
        foreach (var destination in EnumerateAllDestinations(playerState, tech))
        {
            if (GetFreePositions(destination.Inventory, destination.Slots).Count > 0)
                results.Add(destination);
        }
        return results;
    }

    /// <summary>Enumerates all destinations of the requested kind, including full inventories.</summary>
    private static IEnumerable<TechPackDestination> EnumerateAllDestinations(JsonObject playerState, bool tech)
    {
        if (tech)
        {
            yield return CreateDestination(playerState.GetObject(ExosuitLogic.TechInventoryKey),
                "techpack.dest_tech_exosuit", Array.Empty<object>(), "Personal", true);

            var multitools = playerState.GetArray("Multitools");
            if (multitools != null)
            {
                for (int i = 0; i < multitools.Length; i++)
                {
                    var tool = multitools.GetObject(i);
                    if (tool == null) continue;
                    yield return CreateDestination(tool.GetObject("Store"),
                        "techpack.dest_tech_multitool", new object[] { NameOrIndex(tool, i) }, "Personal", true);
                }
            }

            var ships = playerState.GetArray("ShipOwnership");
            if (ships != null)
            {
                for (int i = 0; i < ships.Length; i++)
                {
                    var ship = ships.GetObject(i);
                    if (ship == null) continue;
                    yield return CreateDestination(ship.GetObject("Inventory_TechOnly"),
                        "techpack.dest_tech_ship", new object[] { NameOrIndex(ship, i) }, "Ship", true);
                }
            }

            yield return CreateDestination(playerState.GetObject("FreighterInventory_TechOnly"),
                "techpack.dest_tech_freighter", Array.Empty<object>(), "Freighter", true);

            var vehicles = playerState.GetArray("VehicleOwnership");
            if (vehicles != null)
            {
                for (int i = 0; i < vehicles.Length; i++)
                {
                    var vehicle = vehicles.GetObject(i);
                    if (vehicle == null) continue;
                    yield return CreateDestination(vehicle.GetObject("Inventory_TechOnly"),
                        "techpack.dest_tech_exocraft", new object[] { NameOrIndex(vehicle, i) }, "Vehicle", true);
                }
            }
        }
        else
        {
            yield return CreateDestination(playerState.GetObject(ExosuitLogic.CargoInventoryKey),
                "techpack.dest_cargo_exosuit", Array.Empty<object>(), "PersonalCargo", false);

            var ships = playerState.GetArray("ShipOwnership");
            if (ships != null)
            {
                for (int i = 0; i < ships.Length; i++)
                {
                    var ship = ships.GetObject(i);
                    if (ship == null) continue;
                    yield return CreateDestination(ship.GetObject("Inventory"),
                        "techpack.dest_cargo_ship", new object[] { NameOrIndex(ship, i) }, "ShipCargo", false);
                }
            }

            yield return CreateDestination(playerState.GetObject("FreighterInventory"),
                "techpack.dest_cargo_freighter", Array.Empty<object>(), "FreighterCargo", false);

            var vehicles = playerState.GetArray("VehicleOwnership");
            if (vehicles != null)
            {
                for (int i = 0; i < vehicles.Length; i++)
                {
                    var vehicle = vehicles.GetObject(i);
                    if (vehicle == null) continue;
                    yield return CreateDestination(vehicle.GetObject("Inventory"),
                        "techpack.dest_cargo_exocraft", new object[] { NameOrIndex(vehicle, i) }, "Vehicle", false);
                }
            }

            var chestKeys = BaseLogic.ChestInventoryKeys;
            for (int i = 0; i < chestKeys.Length; i++)
            {
                yield return CreateDestination(playerState.GetObject(chestKeys[i]),
                    "techpack.dest_cargo_container", new object[] { i + 1 }, "Chest", false);
            }

            foreach (var (key, displayName, _) in BaseLogic.StorageInventories)
            {
                yield return CreateDestination(playerState.GetObject(key),
                    "techpack.dest_cargo_storage", new object[] { displayName }, "Default", false);
            }
        }
    }

    /// <summary>Builds a destination entry, returning a placeholder for absent inventories.</summary>
    private static TechPackDestination CreateDestination(JsonObject? inventory, string nameKey, object[] nameArgs,
        string group, bool tech)
    {
        // A placeholder inventory is filtered out by the free-slot check (no Slots array).
        return new TechPackDestination
        {
            NameKey = nameKey,
            NameArgs = nameArgs,
            Inventory = inventory ?? new JsonObject(),
            Slots = inventory?.GetArray("Slots") ?? new JsonArray(),
            Group = group,
            IsTech = tech
        };
    }

    /// <summary>Returns the item's name for display, falling back to a 1-based index.</summary>
    private static object NameOrIndex(JsonObject owner, int index)
    {
        string? name = owner.GetString("Name");
        if (string.IsNullOrWhiteSpace(name))
            name = owner.GetString("NameLower");
        return string.IsNullOrWhiteSpace(name) ? index + 1 : name;
    }

    /// <summary>
    /// Returns the free valid slot positions of an inventory: entries of ValidSlotIndices
    /// that hold no item.
    /// </summary>
    internal static List<(int X, int Y)> GetFreePositions(JsonObject inventory, JsonArray slots)
    {
        var positions = new List<(int X, int Y)>();
        var occupied = new HashSet<(int X, int Y)>();

        for (int i = 0; i < slots.Length; i++)
        {
            JsonObject? slot;
            try { slot = slots.GetObject(i); }
            catch { continue; }
            if (slot == null)
                continue;
            var index = slot.GetObject("Index");
            if (index == null)
                continue;
            try { occupied.Add((index.GetInt("X"), index.GetInt("Y"))); }
            catch { }
        }

        var validSlots = inventory.GetArray("ValidSlotIndices");
        if (validSlots == null)
            return positions;

        for (int i = 0; i < validSlots.Length; i++)
        {
            JsonObject? idx;
            try { idx = validSlots.GetObject(i); }
            catch { continue; }
            if (idx == null)
                continue;
            int x;
            int y;
            try
            {
                x = idx.GetInt("X");
                y = idx.GetInt("Y");
            }
            catch
            {
                continue;
            }
            if (!occupied.Contains((x, y)))
                positions.Add((x, y));
        }
        return positions;
    }

    /// <summary>
    /// Packs a technology: writes the pack product into the destination inventory and
    /// removes the technology from the source inventory. The pack ID is the technology
    /// hash plus the technology's seed (00000 for non-procedural technologies), matching
    /// the game's GenerateTechPackProduct.
    /// </summary>
    /// <returns>True when the pack was created and the source item removed.</returns>
    internal static bool TryPack(JsonArray sourceSlots, int sourceIndex,
        string techId, string? seed, TechPackDestination destination, int destX, int destY,
        GameItemDatabase? database)
    {
        if (sourceIndex < 0 || sourceIndex >= sourceSlots.Length)
            return false;
        if (!GetFreePositions(destination.Inventory, destination.Slots).Contains((destX, destY)))
            return false;

        string seedText = string.IsNullOrEmpty(seed) ? "00000" : seed;

        // Pack IDs are binary TkIDs in the save: '^', six raw hash bytes, '#', five seed digits.
        // Writing this as text would make the game treat the hex characters as the hash body.
        byte[] hashBytes = EncodeHashIdBytes(techId);
        var idBytes = new byte[1 + hashBytes.Length + 1 + seedText.Length];
        idBytes[0] = (byte)'^';
        Array.Copy(hashBytes, 0, idBytes, 1, hashBytes.Length);
        idBytes[1 + hashBytes.Length] = (byte)'#';
        for (int i = 0; i < seedText.Length; i++)
            idBytes[1 + hashBytes.Length + 1 + i] = (byte)seedText[i];

        GameItem? packItem = database?.GetItem("U_TECHPACK_ALIEN") ?? database?.GetItem("U_TECHPACK_CORE")
            ?? database?.GetItem("U_TECHBOX_ALIEN") ?? database?.GetItem("U_TECHBOX_CORE");
        int maxAmount = packItem != null
            ? InventoryStackDatabase.GetMaxAmount(packItem, "Product", destination.Group)
            : 1;

        var slot = BuildSlot("Product", new BinaryData(idBytes), 1, maxAmount, destX, destY);
        destination.Slots.Add(slot);
        sourceSlots.RemoveAt(sourceIndex);
        return true;
    }

    /// <summary>
    /// Unpacks a technology pack: installs the technology into the destination technology
    /// inventory and removes the pack from the source inventory.
    /// </summary>
    /// <returns>True when the technology was installed and the pack removed.</returns>
    internal static bool TryUnpack(JsonArray sourceSlots, int sourceIndex, TechPackDestination destination,
        GameItem techItem, string techId, string? seed, int destX, int destY)
    {
        if (sourceIndex < 0 || sourceIndex >= sourceSlots.Length)
            return false;
        if (!GetFreePositions(destination.Inventory, destination.Slots).Contains((destX, destY)))
            return false;

        int maxAmount = InventoryStackDatabase.GetMaxAmount(techItem, "Technology", destination.Group);
        int amount = techItem.BuildFullyCharged ? maxAmount : 0;

        // Procedural technologies are installed with their stored seed (the pack's digits);
        // seedless procedural installs (e.g. deployable modules) get a fresh roll, matching
        // the game's GenerateProceduralTechnology.
        string itemId = EnsureCaretPrefix(techId);
        if (techItem.IsProcedural)
            itemId += "#" + (string.IsNullOrEmpty(seed) ? GenerateProceduralSeed() : seed);

        var slot = BuildSlot("Technology", itemId, amount, maxAmount, destX, destY);
        destination.Slots.Add(slot);
        sourceSlots.RemoveAt(sourceIndex);
        return true;
    }

    /// <summary>
    /// Resolves the technology a deployable module product (U_*) installs as, using the
    /// product's DeploysInto field (e.g. U_SENTSUIT -> UP_SNSUIT).
    /// </summary>
    /// <param name="module">The resolved module item.</param>
    /// <param name="techId">Receives the installed technology ID.</param>
    /// <returns>True when the module deploys into a technology.</returns>
    internal static bool TryResolveModuleTech(GameItem? module, out string techId)
    {
        techId = CatalogueLogic.StripCaretPrefix(module?.DeploysInto ?? "");
        return techId.Length > 0;
    }

    /// <summary>Generates a five-digit procedural technology seed for module installs.</summary>
    internal static string GenerateProceduralSeed() => ProceduralSeedHelper.Generate();

    /// <summary>Builds a save slot entry for the given item.</summary>
    private static JsonObject BuildSlot(string inventoryType, object itemId, int amount, int maxAmount, int x, int y)
    {
        var slot = new JsonObject();

        var type = new JsonObject();
        type.Add("InventoryType", inventoryType);
        slot.Add("Type", type);

        slot.Add("Id", itemId);
        slot.Add("Amount", amount);
        slot.Add("MaxAmount", maxAmount);
        slot.Add("DamageFactor", 0.0);
        slot.Add("FullyInstalled", true);
        slot.Add("AddedAutomatically", false);

        var index = new JsonObject();
        index.Add("X", x);
        index.Add("Y", y);
        slot.Add("Index", index);

        return slot;
    }

    /// <summary>Strips a caret prefix and any '#suffix' from an item ID.</summary>
    private static string StripItemId(string? itemId)
    {
        string bare = CatalogueLogic.StripCaretPrefix(itemId ?? "");
        int suffixIndex = bare.IndexOf('#');
        return suffixIndex >= 0 ? bare[..suffixIndex] : bare;
    }

    /// <summary>Adds a caret prefix when the ID does not have one.</summary>
    private static string EnsureCaretPrefix(string itemId) =>
        itemId.StartsWith("^", StringComparison.Ordinal) ? itemId : "^" + itemId;
    // --- TechPack ID codec ---

	/// <summary>Number of characters in an encoded TechPack hash (six bytes as uppercase hex).</summary>
	internal const int HashLength = 12;

	private static readonly object CacheLock = new();
	private static GameItemDatabase? _cachedDatabase;
	private static int _cachedItemCount = -1;
	private static ResolutionMap? _cachedMap;

	/// <summary>
	/// Computes the technology hash for a technology ID.
	/// Lowercase ASCII letters are folded to uppercase per character before hashing.
	/// </summary>
	/// <param name="id">The technology ID, with or without a leading caret.</param>
	/// <returns>The 32-bit technology hash.</returns>
	internal static uint HashTechId(string id)
	{
		string bare = CatalogueLogic.StripCaretPrefix(id);
		uint hash = 0;
		foreach (char ch in bare)
		{
			uint c = (byte)ch;
			if (c >= (byte)'a' && c <= (byte)'z')
				c -= CaseFold;
			unchecked
			{
				hash += c;
				hash *= HashMul1;
				hash ^= hash >> 6;
			}
		}
		unchecked
		{
			hash *= HashMul2;
			hash ^= hash >> 11;
			hash *= HashMul3;
		}
		return hash;
	}

	/// <summary>
	/// Encodes a technology hash as the six-byte pack hash bytes
	/// (two flag bytes followed by four data bytes).
	/// Zero bytes and '#' bytes are replaced with 'X' and recorded in the flag bytes so they can be decoded.
	/// </summary>
	/// <param name="hash">The technology hash to encode.</param>
	/// <returns>The six byte hash body (without caret).</returns>
	internal static byte[] EncodeHashBytes(uint hash)
	{
		byte flag0 = FlagBase;
		byte flag1 = FlagBase;
		var chars = new byte[4];
		for (int i = 0; i < 4; i++)
		{
			byte b = (byte)((hash >> (8 * i)) & 0xFF);
			if (b >= (byte)'a' && b <= (byte)'z')
			{
				flag1 |= (byte)(1 << i);
				b -= CaseFold;
			}
			if (b == 0x00)
			{
				flag0 |= (byte)(1 << i);
				b = EscapeChar;
			}
			else if (b == HashChar)
			{
				flag0 |= (i % 2 == 0 ? FlagBitEven : FlagBitOdd);
				b = EscapeChar;
			}
			chars[i] = b;
		}
		return new[] { flag0, flag1, chars[0], chars[1], chars[2], chars[3] };
	}

	/// <summary>
	/// Encodes a technology hash as the six-byte pack ID (12 uppercase hex characters).
	/// </summary>
	/// <param name="hash">The technology hash to encode.</param>
	/// <returns>The 12-character uppercase hex representation.</returns>
	internal static string EncodeHash(uint hash)
	{
		byte[] bytes = EncodeHashBytes(hash);
		return string.Format(CultureInfo.InvariantCulture, "{0:X2}{1:X2}{2:X2}{3:X2}{4:X2}{5:X2}",
			bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5]);
	}

	/// <summary>
	/// Encodes a technology ID into its stored TechPack hash form (12 uppercase hex characters, without caret).
	/// </summary>
	/// <param name="techId">The technology ID, with or without a leading caret.</param>
	/// <returns>The 12-character uppercase hex representation.</returns>
	internal static string EncodeHashId(string techId) => EncodeHash(HashTechId(techId));

	/// <summary>
	/// Encodes a technology ID into the six raw hash bytes stored after '^' in a pack ID.
	/// </summary>
	/// <param name="techId">The technology ID, with or without a leading caret.</param>
	/// <returns>The six byte hash body.</returns>
	internal static byte[] EncodeHashIdBytes(string techId) => EncodeHashBytes(HashTechId(techId));

	/// <summary>
	/// Decodes a stored TechPack hash back into its 32-bit technology hash.
	/// Accepts the ID with or without caret and with an optional '#counter' suffix.
	/// </summary>
	/// <param name="packId">The stored pack ID.</param>
	/// <param name="hash">Receives the decoded technology hash.</param>
	/// <returns>True when the ID is well formed and was decoded.</returns>
	internal static bool TryDecodeHashId(string? packId, out uint hash)
	{
		hash = 0;
		string bare = CatalogueLogic.StripCaretPrefix(packId ?? "");
		int suffixIndex = bare.IndexOf('#');
		if (suffixIndex >= 0)
			bare = bare[..suffixIndex];
		if (bare.Length != HashLength)
			return false;

		var bytes = new byte[6];
		for (int i = 0; i < bytes.Length; i++)
		{
			if (!byte.TryParse(bare.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
				return false;
		}

		byte flag0 = bytes[0];
		byte flag1 = bytes[1];
		uint value = 0;
		for (int i = 0; i < 4; i++)
		{
			byte c = bytes[2 + i];
			byte decoded;
			if ((flag0 & (1 << i)) != 0)
				decoded = 0x00;
			else if ((flag1 & (1 << i)) != 0 && c >= (byte)'A' && c <= (byte)'Z')
				decoded = (byte)(c + CaseFold);
			else if ((flag0 & (i % 2 == 0 ? FlagBitEven : FlagBitOdd)) != 0 && c == EscapeChar)
				decoded = HashChar;
			else
				decoded = c;
			value |= (uint)decoded << (8 * i);
		}
		hash = value;
		return true;
	}

	/// <summary>
	/// Determines whether an ID is stored in the hashed TechPack form: an optional caret followed by
	/// exactly 12 hex characters and an optional numeric '#counter' suffix.
	/// </summary>
	/// <param name="id">The item ID to test.</param>
	/// <returns>True when the ID is a hashed TechPack ID.</returns>
	internal static bool IsPackHashId(string? id)
	{
		string bare = CatalogueLogic.StripCaretPrefix(id ?? "");
		int suffixIndex = bare.IndexOf('#');
		if (suffixIndex >= 0)
		{
			if (suffixIndex + 1 >= bare.Length)
				return false;
			for (int i = suffixIndex + 1; i < bare.Length; i++)
			{
				if (!char.IsAsciiDigit(bare[i]))
					return false;
			}
			bare = bare[..suffixIndex];
		}
		if (bare.Length != HashLength)
			return false;
		foreach (char ch in bare)
		{
			if (!Uri.IsHexDigit(ch))
				return false;
		}
		return true;
	}

	/// <summary>
	/// Builds the item ID stored in a save for a technology package of the given technology ID.
	/// </summary>
	/// <param name="techId">The packaged technology ID.</param>
	/// <param name="number">The technology seed written after the hash (00000 when non-procedural).</param>
	/// <returns>The stored pack item ID, e.g. '^808497C54986#00000'.</returns>
	internal static string BuildPackItemId(string techId, int number = 0) =>
		"^" + EncodeHashId(techId) + "#" + number.ToString("D5", CultureInfo.InvariantCulture);

	/// <summary>
	/// Resolves the technology ID behind a stored TechPack item ID.
	/// Hashed packs ('^'+12 hex, optional '#counter') are resolved through the technology catalog;
	/// procedural packs ('TECHID#seed') resolve when the base technology is known.
	/// </summary>
	/// <param name="packItemId">The stored pack item ID.</param>
	/// <param name="database">The item database used to widen the known technology set; may be null.</param>
	/// <param name="techId">Receives the resolved technology ID.</param>
	/// <param name="seed">Receives the procedural seed for procedural packs; null for hashed packs.</param>
	/// <returns>True when the pack resolved to a known technology.</returns>
	internal static bool TryResolveTechId(string packItemId, GameItemDatabase? database,
		out string techId, out string? seed)
	{
		techId = "";
		seed = null;
		if (string.IsNullOrEmpty(packItemId))
			return false;

		string bare = CatalogueLogic.StripCaretPrefix(packItemId);
		string suffix = "";
		int suffixIndex = bare.IndexOf('#');
		if (suffixIndex >= 0)
		{
			suffix = bare[(suffixIndex + 1)..];
			bare = bare[..suffixIndex];
		}

		var map = GetMap(database);

		// Procedural form: a known technology ID followed by a numeric seed. Checked before
		// the hash form so technology IDs that happen to be 12 characters long still resolve.
		if (suffix.Length > 0 && IsDigits(suffix) && map.KnownTechIds.Contains(bare))
		{
			techId = bare;
			seed = suffix;
			return true;
		}

		if (bare.Length == HashLength)
		{
			// Hashed form: the trailing number is the technology seed (00000 for
			// non-procedural technologies), preserved so the roll survives a repack.
			bool hasSeed = suffix.Length > 0 && IsDigits(suffix);
			if (map.HashToTech.TryGetValue(bare, out string? resolved))
			{
				techId = resolved;
				if (hasSeed)
					seed = suffix;
				return true;
			}
			// Decode and re-encode to reject ambiguous '#'/zero flag combinations whose
			// decoded hash does not round-trip back to the same ID.
			if (TryDecodeHashId(bare, out uint hash)
				&& string.Equals(EncodeHash(hash), bare, StringComparison.OrdinalIgnoreCase)
				&& map.HashValueToTech.TryGetValue(hash, out resolved))
			{
				techId = resolved;
				if (hasSeed)
					seed = suffix;
				return true;
			}
		}
		return false;
	}

	/// <summary>Tests whether a string consists solely of ASCII digits.</summary>
	private static bool IsDigits(string value)
	{
		foreach (char ch in value)
		{
			if (!char.IsAsciiDigit(ch))
				return false;
		}
		return true;
	}

	/// <summary>Returns the cached resolution map, rebuilding it when the database contents change.</summary>
	private static ResolutionMap GetMap(GameItemDatabase? database)
	{
		int count = database?.Items.Count ?? -1;
		lock (CacheLock)
		{
			if (_cachedMap != null && ReferenceEquals(_cachedDatabase, database) && _cachedItemCount == count)
				return _cachedMap;
			_cachedMap = BuildMap(database);
			_cachedDatabase = database;
			_cachedItemCount = count;
			return _cachedMap;
		}
	}

	/// <summary>
	/// Builds hash and technology ID lookups from the item database (all technologies) and
	/// the mined TechPack dictionary.
	/// </summary>
	private static ResolutionMap BuildMap(GameItemDatabase? database)
	{
		var map = new ResolutionMap();

		void Add(string techId)
		{
			string bare = CatalogueLogic.StripCaretPrefix(techId);
			if (bare.Length == 0)
				return;
			map.KnownTechIds.Add(bare);
			uint hash = HashTechId(bare);
			map.HashToTech.TryAdd(EncodeHash(hash), bare);
			map.HashValueToTech.TryAdd(hash, bare);
		}

		if (database != null)
		{
			foreach (var item in database.Items.Values)
			{
				// Procedural module templates (e.g. UP_SNSUIT) live outside the Technology
				// table but are still packable, so include them in the known technology set.
				if (string.Equals(item.SourceTable, "Technology", StringComparison.OrdinalIgnoreCase) || item.IsProcedural)
					Add(item.Id);
			}
		}

		foreach (var pack in TechPacks.Dictionary.Values)
			Add(pack.Id);

		return map;
	}

	/// <summary>Hash and technology ID lookups used to resolve stored TechPack IDs.</summary>
	private sealed class ResolutionMap
	{
		/// <summary>Encoded hash (12 hex characters) to technology ID.</summary>
		public Dictionary<string, string> HashToTech { get; } = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>Decoded 32-bit hash to technology ID.</summary>
		public Dictionary<uint, string> HashValueToTech { get; } = new();

		/// <summary>Technology IDs known to the item database and mined dictionary.</summary>
		public HashSet<string> KnownTechIds { get; } = new(StringComparer.OrdinalIgnoreCase);
	}

}