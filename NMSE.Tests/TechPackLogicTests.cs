using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using Xunit;

namespace NMSE.Tests;

/// <summary>
/// Tests for <see cref="TechPackLogic"/>: pack eligibility rules, destination
/// enumeration, pack counter allocation, pack/unpack operations, and the hashed ID
/// codec used for stored technology packages.
/// </summary>
public class TechPackLogicTests
{
    private const string EmptyInventoriesJson = """
    {
        "Inventory": {
            "ValidSlotIndices": [ { "X": 0, "Y": 0 }, { "X": 1, "Y": 0 } ],
            "Slots": []
        },
        "Inventory_TechOnly": {
            "ValidSlotIndices": [ { "X": 0, "Y": 0 }, { "X": 1, "Y": 0 } ],
            "Slots": []
        }
    }
    """;

    private static GameItemDatabase CreateDatabase()
    {
        var database = new GameItemDatabase();
        database.InjectTestItem(new GameItem
        {
            Id = "TEST_TECH",
            Name = "Test Tech",
            SourceTable = "Technology",
            ChargeValue = 100,
            BuildFullyCharged = true
        });
        database.InjectTestItem(new GameItem
        {
            Id = "UP_JETX",
            Name = "Jetpack Upgrade",
            SourceTable = "Technology",
            ChargeValue = 100,
            IsProcedural = true
        });
        return database;
    }

    [Fact]
    public void CanPackageTech_AllowsNormalTechnology()
    {
        Assert.True(TechPackLogic.CanPackageTech(new GameItem { Id = "TEST_TECH" }, "^TEST_TECH", 0.0, null));
    }

    [Fact]
    public void CanPackageTech_BlocksCoreTechnology()
    {
        Assert.False(TechPackLogic.CanPackageTech(new GameItem { Id = "CORE", IsCore = true }, "^CORE", 0.0, null));
    }

    [Fact]
    public void CanPackageTech_BlocksDamagePlaceholders()
    {
        Assert.False(TechPackLogic.CanPackageTech(new GameItem { Id = "SHIPSLOT_DMG1" }, "^SHIPSLOT_DMG1", 0.0, null));
    }

    [Fact]
    public void CanPackageTech_BlocksLastIntactShipWeapon()
    {
        var slots = JsonObject.Parse("""{ "Slots": [] }""").GetArray("Slots")!;
        var source = new JsonObject();
        source.Add("Id", "^SHIPGUN1");
        source.Add("DamageFactor", 0.0);
        slots.Add(source);

        Assert.False(TechPackLogic.CanPackageTech(new GameItem { Id = "SHIPGUN1" }, "^SHIPGUN1", 0.0, slots));

        var second = new JsonObject();
        second.Add("Id", "^UP_LASER1");
        second.Add("DamageFactor", 0.0);
        slots.Add(second);

        Assert.True(TechPackLogic.CanPackageTech(new GameItem { Id = "SHIPGUN1" }, "^SHIPGUN1", 0.0, slots));
    }

    [Fact]
    public void CanPackageTech_AllowsDamagedLastShipWeapon()
    {
        var slots = JsonObject.Parse("""{ "Slots": [] }""").GetArray("Slots")!;
        var source = new JsonObject();
        source.Add("Id", "^SHIPGUN1");
        source.Add("DamageFactor", 1.0);
        slots.Add(source);

        Assert.True(TechPackLogic.CanPackageTech(new GameItem { Id = "SHIPGUN1" }, "^SHIPGUN1", 1.0, slots));
    }

    [Fact]
    public void IsCoreShipWeaponId_MatchesWeaponAndUpgradeIds()
    {
        Assert.True(TechPackLogic.IsCoreShipWeaponId("^SHIPGUN1"));
        Assert.True(TechPackLogic.IsCoreShipWeaponId("^UP_LASER1"));
        Assert.True(TechPackLogic.IsCoreShipWeaponId("^UP_SHOTX"));
        Assert.False(TechPackLogic.IsCoreShipWeaponId("^SHIPSHIELD"));
        Assert.False(TechPackLogic.IsCoreShipWeaponId("^HYPERDRIVE"));
        Assert.False(TechPackLogic.IsCoreShipWeaponId(null));
    }

    [Fact]
    public void EnumerateDestinations_FiltersFullInventories()
    {
        var playerState = JsonObject.Parse("""
        {
            "Inventory": {
                "ValidSlotIndices": [ { "X": 0, "Y": 0 } ],
                "Slots": [ { "Id": "^ITEM", "Index": { "X": 0, "Y": 0 } } ]
            },
            "Inventory_TechOnly": {
                "ValidSlotIndices": [ { "X": 0, "Y": 0 } ],
                "Slots": []
            }
        }
        """);

        var cargo = TechPackLogic.EnumerateDestinations(playerState, tech: false);
        Assert.DoesNotContain(cargo, d => d.NameKey == "techpack.dest_cargo_exosuit");

        var tech = TechPackLogic.EnumerateDestinations(playerState, tech: true);
        Assert.Contains(tech, d => d.NameKey == "techpack.dest_tech_exosuit");
    }

    [Fact]
    public void TryPack_WritesPackAndRemovesTechnology()
    {
        var database = CreateDatabase();
        var playerState = JsonObject.Parse(EmptyInventoriesJson);
        var source = playerState.GetObject("Inventory_TechOnly")!.GetArray("Slots")!;
        var sourceSlot = new JsonObject();
        sourceSlot.Add("Id", "^TEST_TECH");
        sourceSlot.Add("DamageFactor", 0.0);
        sourceSlot.Add("Index", Index(0, 0));
        source.Add(sourceSlot);

        var destination = TechPackLogic.EnumerateDestinations(playerState, tech: false)[0];
        Assert.True(TechPackLogic.TryPack(source, 0, "TEST_TECH", null, destination, 1, 0, database));

        Assert.Equal(0, source.Length);
        Assert.Equal(1, destination.Slots.Length);

        var pack = destination.Slots.GetObject(0)!;
        Assert.Equal("Product", pack.GetObject("Type")!.GetString("InventoryType"));
        Assert.Equal("^" + TechPackLogic.EncodeHashId("TEST_TECH") + "#00000", DisplayId(pack));
        Assert.Equal(1, pack.GetInt("Amount"));

        // The stored ID must be binary: '^', six raw hash bytes, '#', seed digits.
        var raw = Assert.IsType<BinaryData>(pack.Get("Id")).ToByteArray();
        Assert.Equal(0x5E, raw[0]);
        Assert.Equal(TechPackLogic.EncodeHashIdBytes("TEST_TECH"), raw[1..7]);
        Assert.Equal(0x23, raw[7]);
        Assert.Equal("00000", System.Text.Encoding.ASCII.GetString(raw, 8, 5));
    }

    [Fact]
    public void TryPack_ProceduralPackUsesHashAndSeed()
    {
        var database = CreateDatabase();
        var playerState = JsonObject.Parse(EmptyInventoriesJson);
        var source = playerState.GetObject("Inventory_TechOnly")!.GetArray("Slots")!;
        var sourceSlot = new JsonObject();
        sourceSlot.Add("Id", "^UP_JETX#01234");
        sourceSlot.Add("Index", Index(0, 0));
        source.Add(sourceSlot);

        var destination = TechPackLogic.EnumerateDestinations(playerState, tech: false)[0];
        Assert.True(TechPackLogic.TryPack(source, 0, "UP_JETX", "01234", destination, 0, 0, database));

        Assert.Equal("^" + TechPackLogic.EncodeHashId("UP_JETX") + "#01234",
            DisplayId(destination.Slots.GetObject(0)!));
    }

    [Fact]
    public void TryPack_RejectsOccupiedDestinationSlot()
    {
        var database = CreateDatabase();
        var playerState = JsonObject.Parse(EmptyInventoriesJson);
        var source = playerState.GetObject("Inventory_TechOnly")!.GetArray("Slots")!;
        var sourceSlot = new JsonObject();
        sourceSlot.Add("Id", "^TEST_TECH");
        sourceSlot.Add("Index", Index(0, 0));
        source.Add(sourceSlot);

        var destination = TechPackLogic.EnumerateDestinations(playerState, tech: false)[0];
        var occupied = new JsonObject();
        occupied.Add("Id", "^OTHER");
        occupied.Add("Index", Index(1, 0));
        destination.Slots.Add(occupied);

        Assert.False(TechPackLogic.TryPack(source, 0, "TEST_TECH", null, destination, 1, 0, database));
        Assert.Equal(1, source.Length);
    }

    [Fact]
    public void TryUnpack_InstallsTechnologyAndRemovesPack()
    {
        var database = CreateDatabase();
        var playerState = JsonObject.Parse(EmptyInventoriesJson);
        var source = playerState.GetObject("Inventory")!.GetArray("Slots")!;
        var packSlot = new JsonObject();
        packSlot.Add("Id", "^" + TechPackLogic.EncodeHashId("TEST_TECH") + "#00000");
        packSlot.Add("Index", Index(0, 0));
        source.Add(packSlot);

        var destination = TechPackLogic.EnumerateDestinations(playerState, tech: true)[0];
        var techItem = database.GetItem("TEST_TECH")!;
        Assert.True(TechPackLogic.TryUnpack(source, 0, destination, techItem, "TEST_TECH", null, 1, 0));

        Assert.Equal(0, source.Length);
        var installed = destination.Slots.GetObject(0)!;
        Assert.Equal("Technology", installed.GetObject("Type")!.GetString("InventoryType"));
        Assert.Equal("^TEST_TECH", installed.GetString("Id"));
        Assert.Equal(100, installed.GetInt("MaxAmount"));
        Assert.Equal(100, installed.GetInt("Amount"));
    }

    [Fact]
    public void TryUnpack_InstallsProceduralPackWithStoredSeed()
    {
        var database = CreateDatabase();
        var playerState = JsonObject.Parse(EmptyInventoriesJson);
        var source = playerState.GetObject("Inventory")!.GetArray("Slots")!;
        var packSlot = new JsonObject();
        packSlot.Add("Id", "^" + TechPackLogic.EncodeHashId("UP_JETX") + "#01234");
        packSlot.Add("Index", Index(0, 0));
        source.Add(packSlot);

        var destination = TechPackLogic.EnumerateDestinations(playerState, tech: true)[0];
        var techItem = database.GetItem("UP_JETX")!;
        Assert.True(TechPackLogic.TryUnpack(source, 0, destination, techItem, "UP_JETX", "01234", 1, 0));

        Assert.Equal("^UP_JETX#01234", destination.Slots.GetObject(0)!.GetString("Id"));
    }

    [Fact]
    public void TryUnpack_SeedlessProceduralInstallGetsFreshSeed()
    {
        var database = CreateDatabase();
        var playerState = JsonObject.Parse(EmptyInventoriesJson);
        var source = playerState.GetObject("Inventory")!.GetArray("Slots")!;
        var packSlot = new JsonObject();
        packSlot.Add("Id", "^UP_JETX");
        packSlot.Add("Index", Index(0, 0));
        source.Add(packSlot);

        var destination = TechPackLogic.EnumerateDestinations(playerState, tech: true)[0];
        var techItem = database.GetItem("UP_JETX")!;
        Assert.True(TechPackLogic.TryUnpack(source, 0, destination, techItem, "UP_JETX", null, 1, 0));

        string installed = destination.Slots.GetObject(0)!.GetString("Id")!;
        Assert.StartsWith("^UP_JETX#", installed, StringComparison.Ordinal);
        Assert.Equal(5, installed.Length - "^UP_JETX#".Length);
    }

    [Fact]
    public void TryResolveModuleTech_MapsDeploysInto()
    {
        var module = new GameItem { Id = "U_SENTSUIT", DeploysInto = "UP_SNSUIT" };
        Assert.True(TechPackLogic.TryResolveModuleTech(module, out string techId));
        Assert.Equal("UP_SNSUIT", techId);

        Assert.False(TechPackLogic.TryResolveModuleTech(new GameItem { Id = "STONE" }, out _));
        Assert.False(TechPackLogic.TryResolveModuleTech(null, out _));
    }

    [Fact]
    public void GetFreePositions_ReturnsUnoccupiedValidSlots()
    {
        var inventory = JsonObject.Parse("""
        {
            "ValidSlotIndices": [ { "X": 0, "Y": 0 }, { "X": 1, "Y": 0 }, { "X": 2, "Y": 0 } ],
            "Slots": [ { "Id": "^ITEM", "Index": { "X": 1, "Y": 0 } } ]
        }
        """);
        var slots = inventory.GetArray("Slots")!;

        var free = TechPackLogic.GetFreePositions(inventory, slots);

        Assert.Equal(new List<(int X, int Y)> { (0, 0), (2, 0) }, free);
    }

    private static JsonObject Index(int x, int y)
    {
        var index = new JsonObject();
        index.Add("X", x);
        index.Add("Y", y);
        return index;
    }

    /// <summary>
    /// Converts a slot's Id value (string or BinaryData) to the editor's display form:
    /// '^' + uppercase hex of the hash bytes + '#suffix'.
    /// </summary>
    private static string DisplayId(JsonObject slot)
    {
        object? value = slot.Get("Id");
        if (value is string text)
            return text;

        byte[] bytes = ((BinaryData)value!).ToByteArray();
        var sb = new System.Text.StringBuilder();
        bool afterHash = false;
        for (int i = 0; i < bytes.Length; i++)
        {
            int b = bytes[i];
            if (i == 0)
            {
                sb.Append('^');
                continue;
            }
            if (b == 0x23)
            {
                sb.Append('#');
                afterHash = true;
            }
            else if (afterHash)
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return sb.ToString();
    }
    // --- Codec ---

    [Fact]
    public void EncodesEveryMinedTechPackHash()
    {
        Assert.Equal(736, TechPacks.Dictionary.Count);
        foreach (var pair in TechPacks.Dictionary)
        {
            string expectedHash = CatalogueLogic.StripCaretPrefix(pair.Key).ToUpperInvariant();
            Assert.Equal(expectedHash, TechPackLogic.EncodeHashId(pair.Value.Id));
        }
    }

    [Fact]
    public void DecodesEveryMinedTechPackHash()
    {
        foreach (var pair in TechPacks.Dictionary)
        {
            string bareTechId = CatalogueLogic.StripCaretPrefix(pair.Value.Id);
            Assert.True(TechPackLogic.TryDecodeHashId(pair.Key, out uint hash), $"Decode failed for {pair.Key}");
            Assert.Equal(TechPackLogic.HashTechId(bareTechId), hash);
            Assert.Equal(CatalogueLogic.StripCaretPrefix(pair.Key), TechPackLogic.EncodeHash(hash));
        }
    }

    [Fact]
    public void EncodingIsCaseInsensitive()
    {
        Assert.True(TechPacks.Dictionary.TryGetValue("^808497C54986", out var protect));
        string bareTechId = CatalogueLogic.StripCaretPrefix(protect!.Id);
        Assert.Equal(TechPackLogic.EncodeHashId(bareTechId), TechPackLogic.EncodeHashId(bareTechId.ToLowerInvariant()));
    }

    [Theory]
    [InlineData(0x00000000u, "8F8058585858")]
    [InlineData(0x00002323u, "BC8058585858")]
    [InlineData(0x00000061u, "8E8141585858")]
    public void EncodeHashEscapesReservedBytes(uint hash, string expected)
    {
        Assert.Equal(expected, TechPackLogic.EncodeHash(hash));
        Assert.True(TechPackLogic.TryDecodeHashId(expected, out uint decoded));
        Assert.Equal(hash, decoded);
    }

    [Fact]
    public void BuildPackItemIdUsesFiveDigitCounter()
    {
        Assert.True(TechPacks.Dictionary.TryGetValue("^808497C54986", out var protect));
        Assert.Equal("^808497C54986#00012", TechPackLogic.BuildPackItemId(protect!.Id, 12));
    }

    [Fact]
    public void ResolvesHashedPackToTechnology()
    {
        Assert.True(TechPacks.Dictionary.TryGetValue("^808497C54986", out var protect));
        string expectedTechId = CatalogueLogic.StripCaretPrefix(protect!.Id);

        Assert.True(TechPackLogic.TryResolveTechId("^808497C54986", null, out string techId, out string? seed));
        Assert.Equal(expectedTechId, techId);
        Assert.Null(seed);

        Assert.True(TechPackLogic.TryResolveTechId("^808497C54986#00012", null, out techId, out seed));
        Assert.Equal(expectedTechId, techId);
        Assert.Equal("00012", seed);
    }

    [Fact]
    public void ResolvesLowercaseHashedPack()
    {
        Assert.True(TechPacks.Dictionary.TryGetValue("^808497C54986", out var protect));
        Assert.True(TechPackLogic.TryResolveTechId("^808497c54986", null, out string techId, out _));
        Assert.Equal(CatalogueLogic.StripCaretPrefix(protect!.Id), techId);
    }

    [Fact]
    public void ResolvesProceduralPackWithSeed()
    {
        var database = new GameItemDatabase();
        database.InjectTestItem(new GameItem { Id = "TEST_TECH", Name = "Test Tech", SourceTable = "Technology" });

        Assert.True(TechPackLogic.TryResolveTechId("TEST_TECH#00123", database, out string techId, out string? seed));
        Assert.Equal("TEST_TECH", techId);
        Assert.Equal("00123", seed);

        string hashId = TechPackLogic.BuildPackItemId("TEST_TECH", 4);
        Assert.StartsWith("^", hashId, StringComparison.Ordinal);
        Assert.True(TechPackLogic.TryResolveTechId(hashId, database, out techId, out seed));
        Assert.Equal("TEST_TECH", techId);
        Assert.Equal("00004", seed);

        // A technology ID that is itself 12 characters long must still resolve in procedural form.
        database.InjectTestItem(new GameItem { Id = "PHOTONIXCORE", Name = "Photonix Core", SourceTable = "Technology" });
        Assert.True(TechPackLogic.TryResolveTechId("PHOTONIXCORE#00042", database, out techId, out seed));
        Assert.Equal("PHOTONIXCORE", techId);
        Assert.Equal("00042", seed);
    }

    [Fact]
    public void ResolvesProceduralTemplateOutsideTechnologyTable()
    {
        var database = new GameItemDatabase();
        database.InjectTestItem(new GameItem
        {
            Id = "UP_SNSUIT",
            Name = "Exosuit",
            SourceTable = "Upgrades",
            IsProcedural = true
        });

        Assert.True(TechPackLogic.TryResolveTechId("^UP_SNSUIT#95049", database, out string techId, out string? seed));
        Assert.Equal("UP_SNSUIT", techId);
        Assert.Equal("95049", seed);
    }

    [Theory]
    [InlineData("^808497C54986", true)]
    [InlineData("808497C54986", true)]
    [InlineData("^808497c54986", true)]
    [InlineData("^808497C54986#00012", true)]
    [InlineData("^808497C54986#ABC", false)]
    [InlineData("^808497C5498", false)]
    [InlineData("^808497C54986#", false)]
    [InlineData("UP_JET1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPackHashIdClassifiesIds(string? id, bool expected)
    {
        Assert.Equal(expected, TechPackLogic.IsPackHashId(id));
    }

    [Fact]
    public void UnknownPackIdsDoNotResolve()
    {
        Assert.False(TechPackLogic.TryResolveTechId("^FFFFFFFFFFFF", null, out _, out _));
        Assert.False(TechPackLogic.TryResolveTechId("NOT_A_TECH#00001", null, out _, out _));
        Assert.False(TechPackLogic.TryResolveTechId("", null, out _, out _));
    }

}