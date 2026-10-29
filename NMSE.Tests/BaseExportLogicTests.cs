using NMSE.Core;
using NMSE.Models;

namespace NMSE.Tests;

/// <summary>
/// Tests for raw base exports, terrain sidecars and type-safe base imports.
/// </summary>
public class BaseExportLogicTests
{
    private const string HomeBaseJson = """
        {
          "Name": "Test Base",
          "BaseType": { "PersistentBaseTypes": "HomePlanetBase" },
          "BaseVersion": 3,
          "OriginalBaseVersion": 3,
          "GalacticAddress": "0x1234",
          "Position": [ 1.5, 2.5, 3.5 ],
          "Forward": [ 0.0, 0.0, 1.0 ],
          "UserData": 7,
          "Objects": [
            { "ObjectID": "^BASE_FLAG", "Position": [ 0.0, 0.0, 0.0 ] }
          ]
        }
        """;

    private const string TerrainJson = """
        {
          "GalacticAddresses": [ "0x1234", "0x9999" ],
          "BufferSizes": [ 2, 1 ],
          "BufferAges": [ 4, 9 ],
          "BufferAnchors": [ [ 1, 2 ], [ 3, 4 ] ],
          "BufferProtected": [ false, true ],
          "Edits": [ 10, 11, 99 ]
        }
        """;

    [Fact]
    public void WriteExport_WritesRawBaseAndTerrainSidecar()
    {
        string basePath = NewBasePath();
        string sidecarPath = BaseExportLogic.GetTerrainSidecarPath(basePath);
        try
        {
            var player = BuildPlayerState(HomeBaseJson, TerrainJson);
            var baseObject = player.GetArray("PersistentPlayerBases")!.GetObject(0);

            var result = BaseExportLogic.WriteExport(player, baseObject, basePath);

            Assert.Equal(1, result.TerrainBuffers);
            Assert.Equal(sidecarPath, result.TerrainSidecarPath);
            Assert.True(File.Exists(basePath));
            Assert.True(File.Exists(sidecarPath));

            var raw = JsonObject.ImportFromFile(basePath);
            Assert.True(raw.Contains("Objects"));
            Assert.True(raw.Contains("Position"));
            Assert.False(raw.Contains("Format"));
            Assert.False(raw.Contains("NMSE_ExportData"));
            Assert.False(raw.Contains("TerrainEditData"));

            var sidecar = BaseExportLogic.ReadTerrainSidecar(basePath, raw);
            Assert.Equal(BaseExportLogic.TerrainSidecarStatus.Ok, sidecar.Status);
            Assert.Equal(1, sidecar.TerrainEditData!.GetArray("GalacticAddresses")!.Length);
        }
        finally
        {
            DeleteFile(basePath);
            DeleteFile(sidecarPath);
        }
    }

    [Fact]
    public void WriteExport_WithoutTerrain_RemovesStaleSidecar()
    {
        string basePath = NewBasePath();
        string sidecarPath = BaseExportLogic.GetTerrainSidecarPath(basePath);
        try
        {
            File.WriteAllText(basePath, "{}");
            File.WriteAllText(sidecarPath, "stale");
            var baseObject = JsonObject.Parse(HomeBaseJson);

            var result = BaseExportLogic.WriteExport(new JsonObject(), baseObject, basePath);

            Assert.Equal(0, result.TerrainBuffers);
            Assert.True(result.RemovedStaleSidecar);
            Assert.False(File.Exists(sidecarPath));
        }
        finally
        {
            DeleteFile(basePath);
            DeleteFile(sidecarPath);
        }
    }

    [Fact]
    public void WriteExport_OutputCanBeImportedByTheOldImporter()
    {
        string basePath = NewBasePath();
        string sidecarPath = BaseExportLogic.GetTerrainSidecarPath(basePath);
        try
        {
            var player = BuildPlayerState(HomeBaseJson, TerrainJson);
            BaseExportLogic.WriteExport(
                player, player.GetArray("PersistentPlayerBases")!.GetObject(0), basePath);

            var raw = JsonObject.ImportFromFile(basePath);
            var oldTarget = new JsonObject();
            if (raw.Contains("Objects"))
                oldTarget.Set("Objects", raw.Get("Objects"));
            if (raw.Contains("BaseVersion"))
                oldTarget.Set("BaseVersion", raw.Get("BaseVersion"));
            if (raw.Contains("UserData"))
                oldTarget.Set("UserData", raw.Get("UserData"));

            Assert.True(oldTarget.Contains("Objects"));
            Assert.Equal(3, oldTarget.GetInt("BaseVersion"));
            Assert.Equal(7, oldTarget.GetInt("UserData"));
            Assert.False(oldTarget.Contains("NMSE_ExportData"));
        }
        finally
        {
            DeleteFile(basePath);
            DeleteFile(sidecarPath);
        }
    }

    [Fact]
    public void ReadExport_OldRawBase_IsAcceptedWithoutTerrain()
    {
        var raw = JsonObject.Parse(HomeBaseJson);

        var read = BaseExportLogic.ReadExport(raw);

        Assert.Equal(BaseExportLogic.ReadStatus.Ok, read.Status);
        Assert.NotNull(read.Payload);
        Assert.Null(read.Payload!.TerrainEditData);
        Assert.Equal("Test Base", read.Payload.Base.GetString("Name"));
        Assert.Equal("0x1234", read.Payload.Base.GetString("GalacticAddress"));
    }

    [Fact]
    public void ResolveBaseFilePath_AcceptsTheSidecarPath()
    {
        string basePath = NewBasePath();
        string sidecarPath = BaseExportLogic.GetTerrainSidecarPath(basePath);
        try
        {
            File.WriteAllText(basePath, "{}");
            File.WriteAllText(sidecarPath, "{}");

            Assert.Equal(basePath, BaseExportLogic.ResolveBaseFilePath(sidecarPath));
        }
        finally
        {
            DeleteFile(basePath);
            DeleteFile(sidecarPath);
        }
    }

    [Fact]
    public void ReadTerrainSidecar_Missing_IsNotAnError()
    {
        string basePath = NewBasePath();
        try
        {
            var baseObject = JsonObject.Parse(HomeBaseJson);
            var result = BaseExportLogic.ReadTerrainSidecar(basePath, baseObject);

            Assert.Equal(BaseExportLogic.TerrainSidecarStatus.Missing, result.Status);
        }
        finally
        {
            DeleteFile(basePath);
            DeleteFile(BaseExportLogic.GetTerrainSidecarPath(basePath));
        }
    }

    [Fact]
    public void ReadTerrainSidecar_Invalid_IsRejected()
    {
        string basePath = NewBasePath();
        string sidecarPath = BaseExportLogic.GetTerrainSidecarPath(basePath);
        try
        {
            File.WriteAllText(sidecarPath, "{ not json");
            var result = BaseExportLogic.ReadTerrainSidecar(basePath, JsonObject.Parse(HomeBaseJson));

            Assert.Equal(BaseExportLogic.TerrainSidecarStatus.Invalid, result.Status);
        }
        finally
        {
            DeleteFile(basePath);
            DeleteFile(sidecarPath);
        }
    }

    [Fact]
    public void ReadTerrainSidecar_ForAnotherAddress_IsRejected()
    {
        string basePath = NewBasePath();
        string sidecarPath = BaseExportLogic.GetTerrainSidecarPath(basePath);
        try
        {
            var player = BuildPlayerState(HomeBaseJson, TerrainJson);
            BaseExportLogic.WriteExport(
                player, player.GetArray("PersistentPlayerBases")!.GetObject(0), basePath);

            var otherBase = JsonObject.Parse(HomeBaseJson);
            otherBase.Set("GalacticAddress", "0x9999");
            var result = BaseExportLogic.ReadTerrainSidecar(basePath, otherBase);

            Assert.Equal(BaseExportLogic.TerrainSidecarStatus.Invalid, result.Status);
        }
        finally
        {
            DeleteFile(basePath);
            DeleteFile(sidecarPath);
        }
    }

    [Fact]
    public void ImportAsNew_PreservesPositionAndAppendsTerrain()
    {
        string basePath = NewBasePath();
        string sidecarPath = BaseExportLogic.GetTerrainSidecarPath(basePath);
        try
        {
            var sourcePlayer = BuildPlayerState(HomeBaseJson, TerrainJson);
            BaseExportLogic.WriteExport(
                sourcePlayer, sourcePlayer.GetArray("PersistentPlayerBases")!.GetObject(0), basePath);
            var payload = ReadPayloadWithSidecar(basePath);

            var target = new JsonObject();
            target.Set("PersistentPlayerBases", new JsonArray());
            target.Set("TerrainEditData", JsonObject.Parse("""{ "GalacticAddresses": [], "BufferSizes": [], "Edits": [] }"""));

            var result = BaseExportLogic.ImportAsNew(target, payload);

            Assert.Equal(0, result.BaseIndex);
            Assert.Equal(1, result.TerrainBuffers);
            var bases = target.GetArray("PersistentPlayerBases")!;
            Assert.Equal(1, bases.Length);
            var imported = bases.GetObject(0);
            Assert.Equal("0x1234", imported.GetString("GalacticAddress"));
            Assert.Equal(2.5, imported.GetArray("Position")!.GetDouble(1), 3);
            Assert.False(imported.Contains("NMSE_ExportData"));
            var addresses = target.GetObject("TerrainEditData")!.GetArray("GalacticAddresses")!;
            Assert.Equal(1, addresses.Length);
            Assert.Equal("0x1234", addresses.GetString(0));
        }
        finally
        {
            DeleteFile(basePath);
            DeleteFile(sidecarPath);
        }
    }

    [Fact]
    public void ImportAsNew_CreatesBasesArrayWhenMissing()
    {
        var payload = new BaseExportLogic.ExportPayload(
            JsonObject.Parse(HomeBaseJson), null);
        var target = new JsonObject();

        var result = BaseExportLogic.ImportAsNew(target, payload);

        Assert.Equal(0, result.BaseIndex);
        Assert.NotNull(target.GetArray("PersistentPlayerBases"));
        Assert.Equal(1, target.GetArray("PersistentPlayerBases")!.Length);
    }

    [Fact]
    public void ReplaceBase_CopiesPlacementAndReplacesOldTerrain()
    {
        string basePath = NewBasePath();
        string sidecarPath = BaseExportLogic.GetTerrainSidecarPath(basePath);
        try
        {
            var sourcePlayer = BuildPlayerState(HomeBaseJson, TerrainJson);
            BaseExportLogic.WriteExport(
                sourcePlayer, sourcePlayer.GetArray("PersistentPlayerBases")!.GetObject(0), basePath);
            var payload = ReadPayloadWithSidecar(basePath);

            var targetBase = HomeBaseJson.Replace("0x1234", "0xAAAA", StringComparison.Ordinal)
                .Replace("Test Base", "Old Base", StringComparison.Ordinal)
                .Replace("[ 1.5, 2.5, 3.5 ]", "[ 9.0, 9.0, 9.0 ]", StringComparison.Ordinal);
            var target = BuildPlayerState(targetBase, """
                {
                  "GalacticAddresses": [ "0xAAAA" ],
                  "BufferSizes": [ 1 ],
                  "Edits": [ 77 ]
                }
                """);

            var result = BaseExportLogic.ReplaceBase(target, 0, payload);

            Assert.Equal(0, result.BaseIndex);
            var bases = target.GetArray("PersistentPlayerBases")!;
            Assert.Equal(1, bases.Length);
            var replaced = bases.GetObject(0);
            Assert.Equal("Test Base", replaced.GetString("Name"));
            Assert.Equal("0x1234", replaced.GetString("GalacticAddress"));
            Assert.Equal(1.5, replaced.GetArray("Position")!.GetDouble(0), 3);
            Assert.False(replaced.Contains("NMSE_ExportData"));

            var terrain = target.GetObject("TerrainEditData")!;
            var addresses = terrain.GetArray("GalacticAddresses")!;
            Assert.Equal(1, addresses.Length);
            Assert.Equal("0x1234", addresses.GetString(0));
            Assert.Equal(10, terrain.GetArray("Edits")!.GetInt(0));
        }
        finally
        {
            DeleteFile(basePath);
            DeleteFile(sidecarPath);
        }
    }

    [Fact]
    public void ImportAsNew_AllRecognisedBaseTypesAreSupported()
    {
        string[] types =
        [
            "HomePlanetBase",
            "FreighterBase",
            "PlayerSpaceStationBase",
            "PlayerShipBase",
            "PlayerSpaceBase",
            "UITempShipBase"
        ];

        foreach (string type in types)
        {
            var baseObject = JsonObject.Parse(HomeBaseJson);
            baseObject.GetObject("BaseType")!.Set("PersistentBaseTypes", type);
            Assert.True(BaseExportLogic.IsSupportedKind(baseObject), type);
        }

        var other = JsonObject.Parse(HomeBaseJson);
        other.GetObject("BaseType")!.Set("PersistentBaseTypes", "UnknownBase");
        Assert.False(BaseExportLogic.IsSupportedKind(other));
    }

    [Fact]
    public void IsSameType_RejectsHomeToFreighter()
    {
        var home = JsonObject.Parse(HomeBaseJson);
        var freighter = JsonObject.Parse(HomeBaseJson);
        freighter.GetObject("BaseType")!.Set("PersistentBaseTypes", "FreighterBase");

        Assert.True(BaseExportLogic.IsSameKind(home, JsonObject.Parse(HomeBaseJson)));
        Assert.False(BaseExportLogic.IsSameKind(home, freighter));
        Assert.True(BaseExportLogic.IsSameType(home, JsonObject.Parse(HomeBaseJson)));
        Assert.False(BaseExportLogic.IsSameType(home, freighter));
    }

    [Fact]
    public void HasTerrainConflict_DetectsAnotherBaseAtDestination()
    {
        var player = BuildPlayerState(HomeBaseJson, TerrainJson);
        var payload = new BaseExportLogic.ExportPayload(
            JsonObject.Parse(HomeBaseJson), JsonObject.Parse(TerrainJson));
        var second = JsonObject.Parse(HomeBaseJson);
        second.Set("Name", "Second");
        player.GetArray("PersistentPlayerBases")!.Add(second);

        Assert.True(BaseExportLogic.HasTerrainConflict(player, payload, targetIndex: -1));
    }

    [Fact]
    public void HasTerrainConflict_LegacyReplaceWarnsWhenOldAddressIsShared()
    {
        var player = BuildPlayerState(HomeBaseJson, TerrainJson);
        var second = JsonObject.Parse(HomeBaseJson);
        second.Set("Name", "Second");
        player.GetArray("PersistentPlayerBases")!.Add(second);
        var payload = new BaseExportLogic.ExportPayload(
            JsonObject.Parse(HomeBaseJson.Replace("0x1234", "0xBBBB", StringComparison.Ordinal)),
            null);

        Assert.True(BaseExportLogic.HasTerrainConflict(player, payload, targetIndex: 0));
    }

    [Fact]
    public void HasMetadataMismatch_DetectsOwnerAndGameModeDifferences()
    {
        var target = BuildPlayerState(HomeBaseJson, TerrainJson);
        var imported = JsonObject.Parse(HomeBaseJson);
        imported.Set("Owner", JsonObject.Parse("""{ "LID": "other", "UID": "other" }"""));
        target.GetArray("PersistentPlayerBases")!.GetObject(0).Set(
            "Owner", JsonObject.Parse("""{ "LID": "self", "UID": "self" }"""));

        Assert.True(BaseExportLogic.HasMetadataMismatch(target, imported, 0));

        imported.Set("Owner", JsonObject.Parse("""{ "LID": "self", "UID": "self" }"""));
        imported.Set("GameMode", "Creative");
        target.GetArray("PersistentPlayerBases")!.GetObject(0).Set("GameMode", "Normal");
        Assert.True(BaseExportLogic.HasMetadataMismatch(target, imported, 0));
    }

    private static BaseExportLogic.ExportPayload ReadPayloadWithSidecar(string basePath)
    {
        var baseObject = JsonObject.ImportFromFile(basePath);
        var sidecar = BaseExportLogic.ReadTerrainSidecar(basePath, baseObject);
        return new BaseExportLogic.ExportPayload(baseObject, sidecar.TerrainEditData);
    }

    private static JsonObject BuildPlayerState(string baseJson, string? terrainJson)
    {
        var player = new JsonObject();
        player.Set("PersistentPlayerBases", JsonArray.Parse($"[{baseJson}]"));
        if (terrainJson != null)
            player.Set("TerrainEditData", JsonObject.Parse(terrainJson));
        return player;
    }

    private static string NewBasePath() =>
        Path.Combine(Path.GetTempPath(), $"nmse-base-{Guid.NewGuid():N}.nmsbase");

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
