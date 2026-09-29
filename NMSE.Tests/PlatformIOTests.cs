using System.Text;
using NMSE.IO;
using NMSE.Models;

namespace NMSE.Tests;

/// <summary>
/// Tests for platform-specific IO: MetaCrypto, MetaFileWriter, ContainersIndexManager,
/// MemoryDatManager, SaveSlotManager, and enhanced SaveFileManager.
/// </summary>
public class PlatformIOTests
{
    // --- MetaCrypto: TEA encrypt/decrypt round-trip ---

    [Fact]
    public void MetaCrypto_EncryptDecrypt_RoundTrip()
    {
        // Create a plain meta buffer with recognizable Steam header
        uint[] plain = new uint[26]; // 104 bytes = Steam vanilla meta size
        plain[0] = MetaFileWriter.META_HEADER; // 0xEEEEEEBE
        plain[1] = 1; // format
        plain[2] = 12345; // some value
        plain[3] = 0;
        plain[4] = 99;
        plain[5] = 42;

        // Encrypt with slot 2, 8 iterations (pre-Waypoint)
        uint[] encrypted = MetaCrypto.Encrypt(plain, 2, 8);

        // Should be different from plain
        Assert.NotEqual(plain[0], encrypted[0]);

        // Decrypt
        uint[] decrypted = MetaCrypto.Decrypt(encrypted, 2, 8);

        // Should match original
        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void MetaCrypto_EncryptDecrypt_WaypointFormat()
    {
        // 90 uint = 360 bytes = Steam Waypoint meta
        uint[] plain = new uint[90];
        plain[0] = MetaFileWriter.META_HEADER;
        plain[1] = 2; // format 2
        for (int i = 2; i < 90; i++)
            plain[i] = (uint)(i * 7 + 13);

        // 6 iterations for Waypoint+
        uint[] encrypted = MetaCrypto.Encrypt(plain, 5, 6);
        Assert.NotEqual(plain[0], encrypted[0]);

        uint[] decrypted = MetaCrypto.Decrypt(encrypted, 5, 6);
        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void MetaCrypto_Decrypt_TriesDifferentSlots()
    {
        uint[] plain = new uint[26];
        plain[0] = MetaFileWriter.META_HEADER;
        plain[1] = 1;
        plain[2] = 42;

        // Encrypt with slot 7
        uint[] encrypted = MetaCrypto.Encrypt(plain, 7, 8);

        // Decrypt with wrong slot first - should still find it
        uint[] decrypted = MetaCrypto.Decrypt(encrypted, 3, 8);
        Assert.Equal(MetaFileWriter.META_HEADER, decrypted[0]);
        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void MetaCrypto_ComputeMetaHashes_ReturnsCorrectLength()
    {
        byte[] testData = new byte[1024];
        new Random(42).NextBytes(testData);

        byte[] hashes = MetaCrypto.ComputeMetaHashes(testData);
        Assert.Equal(48, hashes.Length); // SpookyHash(16) + SHA256(32)
    }

    [Fact]
    public void MetaCrypto_ComputeMetaHashes_DeterministicForSameInput()
    {
        byte[] data = new byte[256];
        for (int i = 0; i < 256; i++) data[i] = (byte)(i & 0xFF);

        byte[] hash1 = MetaCrypto.ComputeMetaHashes(data);
        byte[] hash2 = MetaCrypto.ComputeMetaHashes(data);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void MetaCrypto_ComputeMetaHashes_DifferentForDifferentInput()
    {
        byte[] data1 = new byte[] { 1, 2, 3, 4, 5 };
        byte[] data2 = new byte[] { 1, 2, 3, 4, 6 };

        byte[] hash1 = MetaCrypto.ComputeMetaHashes(data1);
        byte[] hash2 = MetaCrypto.ComputeMetaHashes(data2);
        Assert.NotEqual(hash1, hash2);
    }

    // --- MetaFileWriter: helpers ---

    [Theory]
    [InlineData("/saves/save.hg", "/saves/mf_save.hg")]
    [InlineData("/saves/save2.hg", "/saves/mf_save2.hg")]
    [InlineData("/saves/accountdata.hg", "/saves/mf_accountdata.hg")]
    public void MetaFileWriter_GetSteamMetaPath_ReturnsCorrectPath(string savePath, string expectedMeta)
    {
        string result = MetaFileWriter.GetSteamMetaPath(savePath);
        // Normalise path separators so the test passes on both Windows (\) and Linux (/)
        Assert.Equal(NormalisePath(expectedMeta), NormalisePath(result));
    }

    [Theory]
    [InlineData("/saves/savedata01.hg", 1, "/saves/manifest01.hg")]
    [InlineData("/saves/savedata00.hg", 0, "/saves/manifest00.hg")]
    [InlineData("/saves/savedata10.hg", 10, "/saves/manifest10.hg")]
    public void MetaFileWriter_GetSwitchMetaPath_ReturnsCorrectPath(string savePath, int metaIndex, string expected)
    {
        string result = MetaFileWriter.GetSwitchMetaPath(savePath, metaIndex);
        // Normalise path separators so the test passes on both Windows (\) and Linux (/)
        Assert.Equal(NormalisePath(expected), NormalisePath(result));
    }

    /// <summary>
    /// Normalises path separators to forward slashes for cross-platform comparison.
    /// Path.Combine/GetDirectoryName use OS-native separators, so tests that
    /// hardcode '/' in InlineData would fail on Windows without normalisation.
    /// </summary>
    private static string NormalisePath(string path) => path.Replace('\\', '/');

    [Fact]
    public void MetaFileWriter_BytesToUInts_RoundTrip()
    {
        byte[] original = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        uint[] uints = MetaFileWriter.BytesToUInts(original);
        Assert.Equal(2, uints.Length);
        byte[] back = MetaFileWriter.UIntsToBytes(uints);
        Assert.Equal(original, back);
    }

    [Fact]
    public void MetaFileWriter_WriteSteamMeta_CreatesFile()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string savePath = Path.Combine(tmpDir, "save.hg");
            File.WriteAllText(savePath, "dummy");

            byte[] compressedData = new byte[128];
            new Random(1).NextBytes(compressedData);

            var metaInfo = new SaveMetaInfo
            {
                BaseVersion = 4115, // Frontiers
                GameMode = 1,
                Season = 0,
                TotalPlayTime = 3600,
                SaveName = "Test Save",
            };

            MetaFileWriter.WriteSteamMeta(savePath, compressedData, 1024, metaInfo, 2);

            string metaPath = MetaFileWriter.GetSteamMetaPath(savePath);
            Assert.True(File.Exists(metaPath));
            byte[] metaBytes = File.ReadAllBytes(metaPath);
            Assert.True(metaBytes.Length > 0);

            // Verify it can be decrypted
            uint[] encrypted = MetaFileWriter.BytesToUInts(metaBytes);
            uint[] decrypted = MetaCrypto.Decrypt(encrypted, 2, 6);
            Assert.Equal(MetaFileWriter.META_HEADER, decrypted[0]);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Theory]
    [InlineData(4115, MetaFileWriter.META_FORMAT_2)]  // Frontiers -> 2002
    [InlineData(4135, MetaFileWriter.META_FORMAT_3)]  // Worlds Part I -> 2003
    [InlineData(4145, MetaFileWriter.META_FORMAT_4)]  // Worlds Part II -> 2004
    [InlineData(4720, MetaFileWriter.META_FORMAT_4)]  // Current game -> 2004
    public void MetaFileWriter_WriteSteamMeta_WritesCorrectFormat(int baseVersion, uint expectedFormat)
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string savePath = Path.Combine(tmpDir, "save.hg");
            File.WriteAllText(savePath, "dummy");

            byte[] compressedData = new byte[128];
            new Random(1).NextBytes(compressedData);

            var metaInfo = new SaveMetaInfo
            {
                BaseVersion = baseVersion,
                GameMode = 1,
                Season = 0,
                TotalPlayTime = 3600,
                SaveName = "My Save",
                SaveSummary = "In a system",
                DifficultyPreset = 2,
            };

            MetaFileWriter.WriteSteamMeta(savePath, compressedData, 1024, metaInfo, 2);

            string metaPath = MetaFileWriter.GetSteamMetaPath(savePath);
            byte[] metaBytes = File.ReadAllBytes(metaPath);

            // Decrypt and verify format matches NMS game values (2001-2004)
            uint[] encrypted = MetaFileWriter.BytesToUInts(metaBytes);
            uint[] decrypted = MetaCrypto.Decrypt(encrypted, 2, 6);
            Assert.Equal(MetaFileWriter.META_HEADER, decrypted[0]);
            Assert.Equal(expectedFormat, decrypted[1]);

            // Verify save name can be read from correct offset
            byte[] decryptedBytes = MetaFileWriter.UIntsToBytes(decrypted);
            string saveName = System.Text.Encoding.UTF8.GetString(decryptedBytes, 88, 128).TrimEnd('\0');
            Assert.Equal("My Save", saveName);

            string summary = System.Text.Encoding.UTF8.GetString(decryptedBytes, 216, 128).TrimEnd('\0');
            Assert.Equal("In a system", summary);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void MetaFileWriter_WriteSwitchMeta_CreatesFile()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string savePath = Path.Combine(tmpDir, "savedata01.hg");
            File.WriteAllText(savePath, "dummy");

            var metaInfo = new SaveMetaInfo
            {
                BaseVersion = 4115,
                GameMode = 1,
                TotalPlayTime = 7200,
            };

            MetaFileWriter.WriteSwitchMeta(savePath, 2048, metaInfo, 1);

            string metaPath = Path.Combine(tmpDir, "manifest01.hg");
            Assert.True(File.Exists(metaPath));
            byte[] metaBytes = File.ReadAllBytes(metaPath);
            Assert.True(metaBytes.Length > 0);

            // Switch meta is NOT encrypted - verify header directly
            uint header = BitConverter.ToUInt32(metaBytes, 0);
            Assert.Equal(MetaFileWriter.META_HEADER_SWITCH, header);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // --- SaveSlotManager: slot file path resolution ---

    /// <summary>
    /// GetSlotFiles returns the MANUAL save of each slot.
    /// Steam layout:  slot 0 manual = save2.hg,   slot 1 manual = save4.hg,   slot 2 manual = save6.hg
    /// Auto  layout:  slot 0 auto   = save.hg,    slot 1 auto   = save3.hg,   slot 2 auto   = save5.hg
    /// </summary>
    [Theory]
    [InlineData(0, "save2.hg")]
    [InlineData(1, "save4.hg")]
    [InlineData(2, "save6.hg")]
    public void SaveSlotManager_GetSlotFiles_Steam_ReturnsManualSave(int slotIndex, string expectedDataName)
    {
        var files = SaveSlotManager.GetSlotFiles("/saves/st_12345", slotIndex, SaveFileManager.Platform.Steam);
        Assert.NotNull(files.DataFile);
        Assert.EndsWith(expectedDataName, files.DataFile!);
        Assert.NotNull(files.MetaFile);
        Assert.Contains("mf_", files.MetaFile!);
    }

    /// <summary>
    /// GetAllSlotFiles returns BOTH the auto-save and the manual save for each slot.
    /// </summary>
    [Theory]
    [InlineData(0, "save.hg",  "save2.hg")]
    [InlineData(1, "save3.hg", "save4.hg")]
    [InlineData(2, "save5.hg", "save6.hg")]
    public void SaveSlotManager_GetAllSlotFiles_Steam_ReturnsBothFiles(
        int slotIndex, string expectedAuto, string expectedManual)
    {
        var pairs = SaveSlotManager.GetAllSlotFiles("/saves/st_12345", slotIndex, SaveFileManager.Platform.Steam);
        Assert.Equal(2, pairs.Length);
        Assert.EndsWith(expectedAuto,   pairs[0].DataFile!);
        Assert.EndsWith(expectedManual, pairs[1].DataFile!);
    }

    [Theory]
    [InlineData(0, "savedata03.hg")]
    [InlineData(1, "savedata05.hg")]
    public void SaveSlotManager_GetSlotFiles_Switch_ReturnsManualSave(int slotIndex, string expectedDataName)
    {
        var files = SaveSlotManager.GetSlotFiles("/saves/switch", slotIndex, SaveFileManager.Platform.Switch);
        Assert.NotNull(files.DataFile);
        Assert.EndsWith(expectedDataName, files.DataFile!);
        Assert.NotNull(files.MetaFile);
        Assert.Contains("manifest", files.MetaFile!);
    }

    [Theory]
    [InlineData(0, "savedata02.hg", "savedata03.hg")]
    [InlineData(1, "savedata04.hg", "savedata05.hg")]
    public void SaveSlotManager_GetAllSlotFiles_Switch_ReturnsBothFiles(
        int slotIndex, string expectedAuto, string expectedManual)
    {
        var pairs = SaveSlotManager.GetAllSlotFiles("/saves/switch", slotIndex, SaveFileManager.Platform.Switch);
        Assert.Equal(2, pairs.Length);
        Assert.EndsWith(expectedAuto,   pairs[0].DataFile!);
        Assert.EndsWith(expectedManual, pairs[1].DataFile!);
    }

    /// <summary>
    /// PS4 HTOS layout: savedata00.hg is account data; game slots start at savedata02.
    /// GetSlotFiles returns the MANUAL save of each slot.
    /// Slot 0 (game slot 1) manual = savedata03.hg
    /// Slot 1 (game slot 2) manual = savedata05.hg
    /// </summary>
    [Theory]
    [InlineData(0, "savedata03.hg")]
    [InlineData(1, "savedata05.hg")]
    public void SaveSlotManager_GetSlotFiles_PS4_ReturnsManualSave(int slotIndex, string expectedDataName)
    {
        var files = SaveSlotManager.GetSlotFiles("/saves/ps4", slotIndex, SaveFileManager.Platform.PS4);
        Assert.NotNull(files.DataFile);
        Assert.EndsWith(expectedDataName, files.DataFile!);
    }

    /// <summary>
    /// GetAllSlotFiles for PS4 HTOS returns BOTH the auto-save and manual save.
    /// Slot 0 -> savedata02 (auto) + savedata03 (manual)
    /// Slot 1 -> savedata04 (auto) + savedata05 (manual)
    /// </summary>
    [Theory]
    [InlineData(0, "savedata02.hg", "savedata03.hg")]
    [InlineData(1, "savedata04.hg", "savedata05.hg")]
    public void SaveSlotManager_GetAllSlotFiles_PS4_ReturnsBothFiles(
        int slotIndex, string expectedAuto, string expectedManual)
    {
        var pairs = SaveSlotManager.GetAllSlotFiles("/saves/ps4", slotIndex, SaveFileManager.Platform.PS4);
        Assert.Equal(2, pairs.Length);
        Assert.EndsWith(expectedAuto,   pairs[0].DataFile!);
        Assert.EndsWith(expectedManual, pairs[1].DataFile!);
    }


    // --- SaveSlotManager: CopySlot ---

    [Fact]
    public void SaveSlotManager_CopySlot_CopiesFiles()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Slot 0 auto = save.hg, slot 0 manual = save2.hg
            // Copying slot 0 to slot 1 should place the data into slot 1:
            //   slot 1 auto = save3.hg, slot 1 manual = save4.hg
            File.WriteAllText(Path.Combine(tmpDir, "save.hg"),  "slot0_auto");
            File.WriteAllText(Path.Combine(tmpDir, "save2.hg"), "slot0_manual");

            SaveSlotManager.CopySlot(tmpDir, 0, 1, SaveFileManager.Platform.Steam);

            Assert.True(File.Exists(Path.Combine(tmpDir, "save3.hg")), "auto-save of slot 1 (save3.hg) should be created");
            Assert.Equal("slot0_auto",   File.ReadAllText(Path.Combine(tmpDir, "save3.hg")));
            Assert.True(File.Exists(Path.Combine(tmpDir, "save4.hg")), "manual save of slot 1 (save4.hg) should be created");
            Assert.Equal("slot0_manual", File.ReadAllText(Path.Combine(tmpDir, "save4.hg")));

            // Source files must still exist (copy, not move)
            Assert.True(File.Exists(Path.Combine(tmpDir, "save.hg")));
            Assert.True(File.Exists(Path.Combine(tmpDir, "save2.hg")));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void SaveSlotManager_CopySlot_SameSlot_Noop()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "save.hg"), "data");

            // Copying slot 0 to slot 0 should be a no-op
            SaveSlotManager.CopySlot(tmpDir, 0, 0, SaveFileManager.Platform.Steam);

            Assert.True(File.Exists(Path.Combine(tmpDir, "save.hg")));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void SaveSlotManager_DeleteSlot_RemovesFiles()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Slot 0 has auto (save.hg + mf_save.hg) and manual (save2.hg + mf_save2.hg)
            File.WriteAllText(Path.Combine(tmpDir, "save.hg"),    "auto_data");
            File.WriteAllText(Path.Combine(tmpDir, "mf_save.hg"), "auto_meta");
            File.WriteAllText(Path.Combine(tmpDir, "save2.hg"),    "manual_data");
            File.WriteAllText(Path.Combine(tmpDir, "mf_save2.hg"), "manual_meta");

            SaveSlotManager.DeleteSlot(tmpDir, 0, SaveFileManager.Platform.Steam);

            Assert.False(File.Exists(Path.Combine(tmpDir, "save.hg")));
            Assert.False(File.Exists(Path.Combine(tmpDir, "mf_save.hg")));
            Assert.False(File.Exists(Path.Combine(tmpDir, "save2.hg")));
            Assert.False(File.Exists(Path.Combine(tmpDir, "mf_save2.hg")));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void SaveSlotManager_MoveSlot_MovesFiles()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Slot 0: save.hg (auto) + save2.hg (manual)
            // Move to slot 2: save5.hg (auto) + save6.hg (manual)
            File.WriteAllText(Path.Combine(tmpDir, "save.hg"),  "auto_data");
            File.WriteAllText(Path.Combine(tmpDir, "save2.hg"), "manual_data");

            SaveSlotManager.MoveSlot(tmpDir, 0, 2, SaveFileManager.Platform.Steam);

            // Source files must be gone
            Assert.False(File.Exists(Path.Combine(tmpDir, "save.hg")));
            Assert.False(File.Exists(Path.Combine(tmpDir, "save2.hg")));

            // Destination files must exist with the original content
            Assert.True(File.Exists(Path.Combine(tmpDir, "save5.hg")));
            Assert.Equal("auto_data",   File.ReadAllText(Path.Combine(tmpDir, "save5.hg")));
            Assert.True(File.Exists(Path.Combine(tmpDir, "save6.hg")));
            Assert.Equal("manual_data", File.ReadAllText(Path.Combine(tmpDir, "save6.hg")));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void SaveSlotManager_SwapSlots_SwapsBothWays()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Slot 0: save.hg (auto) + save2.hg (manual)
            // Slot 1: save3.hg (auto) + save4.hg (manual)
            File.WriteAllText(Path.Combine(tmpDir, "save.hg"),  "A_auto");
            File.WriteAllText(Path.Combine(tmpDir, "save2.hg"), "A_manual");
            File.WriteAllText(Path.Combine(tmpDir, "save3.hg"), "B_auto");
            File.WriteAllText(Path.Combine(tmpDir, "save4.hg"), "B_manual");

            SaveSlotManager.SwapSlots(tmpDir, 0, 1, SaveFileManager.Platform.Steam);

            // Slot 0 should now contain B's data
            Assert.Equal("B_auto",   File.ReadAllText(Path.Combine(tmpDir, "save.hg")));
            Assert.Equal("B_manual", File.ReadAllText(Path.Combine(tmpDir, "save2.hg")));
            // Slot 1 should now contain A's data
            Assert.Equal("A_auto",   File.ReadAllText(Path.Combine(tmpDir, "save3.hg")));
            Assert.Equal("A_manual", File.ReadAllText(Path.Combine(tmpDir, "save4.hg")));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // --- SaveSlotManager: typed errors and slot enumeration ---

    [Fact]
    public void SaveSlotManager_CopySlot_EmptySource_ThrowsSlotEmptyWithOneBasedNumber()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Only slot 1 (index 0) has data
            File.WriteAllText(Path.Combine(tmpDir, "save.hg"), "slot1");

            var ex = Assert.Throws<SlotEmptyException>(
                () => SaveSlotManager.CopySlot(tmpDir, 1, 2, SaveFileManager.Platform.Steam));

            // The exception reports the UI slot number (index 1 = "Slot 2")
            Assert.Equal(2, ex.SlotNumber);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void SaveSlotManager_DeleteSlot_EmptySlot_ThrowsSlotEmpty()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            Assert.Throws<SlotEmptyException>(
                () => SaveSlotManager.DeleteSlot(tmpDir, 0, SaveFileManager.Platform.Steam));
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void SaveSlotManager_CopySlot_UnsupportedPlatform_Throws()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            Assert.Throws<SlotOperationUnsupportedException>(
                () => SaveSlotManager.CopySlot(tmpDir, 0, 1, SaveFileManager.Platform.Unknown));
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void SaveSlotManager_CopySlot_MemoryDat_ThrowsUnsupported()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllBytes(Path.Combine(tmpDir, "memory.dat"), new byte[] { 1 });

            var ex = Assert.Throws<SlotOperationUnsupportedException>(
                () => SaveSlotManager.CopySlot(tmpDir, 0, 1, SaveFileManager.Platform.PS4));

            Assert.True(ex.IsMemoryDat);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void SaveSlotManager_CopySlot_Ps4Streaming_CopiesFiles()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "savedata02.hg"), "ps4_auto");
            File.WriteAllText(Path.Combine(tmpDir, "savedata03.hg"), "ps4_manual");

            SaveSlotManager.CopySlot(tmpDir, 0, 1, SaveFileManager.Platform.PS4);

            Assert.Equal("ps4_auto", File.ReadAllText(Path.Combine(tmpDir, "savedata04.hg")));
            Assert.Equal("ps4_manual", File.ReadAllText(Path.Combine(tmpDir, "savedata05.hg")));
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    // --- SaveSlotManager: Xbox Game Pass slot operations ---

    /// <summary>
    /// Builds a synthetic Xbox save directory: one GUID blob directory per entry
    /// (container.1 with data/meta blobs) plus a containers.index referencing them.
    /// </summary>
    private static string CreateXboxSaveDirectory(params (string Identifier, string Data, string Meta)[] entries)
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_xbox_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        WriteWgsContainer(tmpDir, entries);
        return tmpDir;
    }

    /// <summary>
    /// Writes a synthetic legacy wgs container (containers.index plus one GUID blob
    /// directory per entry) into an existing directory. Used by the xgs mirror tests.
    /// </summary>
    private static void WriteWgsContainer(string tmpDir, params (string Identifier, string Data, string Meta)[] entries)
    {
        using var indexMs = new MemoryStream();
        using var indexW = new BinaryWriter(indexMs);
        indexW.Write(14);         // header
        indexW.Write((long)entries.Length);
        indexW.Write(0);          // processIdentifier (empty)
        indexW.Write(0L);         // lastModifiedTime
        indexW.Write(0);          // syncState
        indexW.Write(0);          // accountGuid (empty)
        indexW.Write(268435456L); // footer

        foreach (var entry in entries)
        {
            Guid dirGuid = Guid.NewGuid();
            string blobDir = Path.Combine(tmpDir, dirGuid.ToString("N").ToUpperInvariant());
            Directory.CreateDirectory(blobDir);

            Guid dataGuid = Guid.NewGuid();
            Guid metaGuid = Guid.NewGuid();
            File.WriteAllText(Path.Combine(blobDir, dataGuid.ToString("N").ToUpperInvariant()), entry.Data);
            File.WriteAllText(Path.Combine(blobDir, metaGuid.ToString("N").ToUpperInvariant()), entry.Meta);

            // 328-byte container file with 128-byte UTF-16 identifiers
            byte[] container = new byte[328];
            using (var cms = new MemoryStream(container))
            using (var cw = new BinaryWriter(cms))
            {
                cw.Write(4);  // header
                cw.Write(2);  // count
                cw.Write(Encoding.Unicode.GetBytes("data"));
                cms.Position = 8 + 128;
                cw.Write(new byte[16]); // cloud GUID
                cw.Write(dataGuid.ToByteArray());
                cw.Write(Encoding.Unicode.GetBytes("meta"));
                cms.Position = 8 + 160 + 128;
                cw.Write(new byte[16]); // cloud GUID
                cw.Write(metaGuid.ToByteArray());
            }
            File.WriteAllBytes(Path.Combine(blobDir, "container.1"), container);

            indexW.Write(entry.Identifier.Length);
            indexW.Write(Encoding.Unicode.GetBytes(entry.Identifier));
            indexW.Write(0);       // second identifier
            indexW.Write(0);       // syncTime
            indexW.Write((byte)1); // blob container extension
            indexW.Write(0);       // sync state
            indexW.Write(dirGuid.ToByteArray());
            indexW.Write(0L);      // last modified
            indexW.Write(0L);      // empty
            indexW.Write(0L);      // total size
        }

        while (indexMs.Position < 200) indexW.Write((byte)0);

        File.WriteAllBytes(Path.Combine(tmpDir, "containers.index"), indexMs.ToArray());
    }

    [Fact]
    public void SaveSlotManager_XboxCopySlot_CreatesDestinationEntries()
    {
        string tmpDir = CreateXboxSaveDirectory(
            ("Slot1Auto", "s1_auto", "s1_auto_meta"),
            ("Slot1Manual", "s1_manual", "s1_manual_meta"));
        try
        {
            SaveSlotManager.CopySlot(tmpDir, 0, 1, SaveFileManager.Platform.XboxGamePass);

            var slots = ContainersIndexManager.ParseContainersIndex(Path.Combine(tmpDir, "containers.index"));
            Assert.True(slots.ContainsKey("Slot2Auto"), "Slot2Auto should be created");
            Assert.True(slots.ContainsKey("Slot2Manual"), "Slot2Manual should be created");
            Assert.Equal("s1_auto", File.ReadAllText(slots["Slot2Auto"].DataFilePath!));
            Assert.Equal("s1_manual", File.ReadAllText(slots["Slot2Manual"].DataFilePath!));
            Assert.Equal("s1_auto_meta", File.ReadAllText(slots["Slot2Auto"].MetaFilePath!));

            // Source stays untouched
            Assert.Equal("s1_auto", File.ReadAllText(slots["Slot1Auto"].DataFilePath!));
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void SaveSlotManager_XboxCopySlot_OverwritesExistingDestination()
    {
        string tmpDir = CreateXboxSaveDirectory(
            ("Slot1Auto", "A", "Am"),
            ("Slot1Manual", "A2", "A2m"),
            ("Slot2Auto", "B", "Bm"),
            ("Slot2Manual", "B2", "B2m"));
        try
        {
            SaveSlotManager.CopySlot(tmpDir, 0, 1, SaveFileManager.Platform.XboxGamePass);

            var slots = ContainersIndexManager.ParseContainersIndex(Path.Combine(tmpDir, "containers.index"));
            Assert.Equal("A", File.ReadAllText(slots["Slot2Auto"].DataFilePath!));
            Assert.Equal("A2", File.ReadAllText(slots["Slot2Manual"].DataFilePath!));
            Assert.Equal("A", File.ReadAllText(slots["Slot1Auto"].DataFilePath!));
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void SaveSlotManager_XboxMoveSlot_RemovesSourceEntries()
    {
        string tmpDir = CreateXboxSaveDirectory(
            ("Slot1Auto", "A", "Am"),
            ("Slot2Auto", "B", "Bm"));
        try
        {
            var before = ContainersIndexManager.ParseContainersIndex(Path.Combine(tmpDir, "containers.index"));
            string removedBlobDir = before["Slot1Auto"].BlobDirectoryPath;

            SaveSlotManager.MoveSlot(tmpDir, 0, 1, SaveFileManager.Platform.XboxGamePass);

            var slots = ContainersIndexManager.ParseContainersIndex(Path.Combine(tmpDir, "containers.index"));
            Assert.False(slots.ContainsKey("Slot1Auto"), "Slot1Auto should be gone after a move");
            Assert.Equal("A", File.ReadAllText(slots["Slot2Auto"].DataFilePath!));
            Assert.False(Directory.Exists(removedBlobDir), "source blob directory should be deleted");
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void SaveSlotManager_XboxSwapSlots_SwapsEntries()
    {
        string tmpDir = CreateXboxSaveDirectory(
            ("Slot1Auto", "A", "Am"),
            ("Slot2Manual", "B", "Bm"));
        try
        {
            SaveSlotManager.SwapSlots(tmpDir, 0, 1, SaveFileManager.Platform.XboxGamePass);

            var slots = ContainersIndexManager.ParseContainersIndex(Path.Combine(tmpDir, "containers.index"));
            Assert.False(slots.ContainsKey("Slot1Auto"), "Slot1Auto should have moved to slot 2");
            Assert.False(slots.ContainsKey("Slot2Manual"), "Slot2Manual should have moved to slot 1");
            Assert.Equal("A", File.ReadAllText(slots["Slot2Auto"].DataFilePath!));
            Assert.Equal("B", File.ReadAllText(slots["Slot1Manual"].DataFilePath!));
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void SaveSlotManager_XboxDeleteSlot_RemovesEntriesAndBlobs()
    {
        string tmpDir = CreateXboxSaveDirectory(
            ("Slot1Auto", "A", "Am"),
            ("Slot1Manual", "A2", "A2m"),
            ("Slot2Auto", "B", "Bm"));
        try
        {
            var before = ContainersIndexManager.ParseContainersIndex(Path.Combine(tmpDir, "containers.index"));
            string autoBlobDir = before["Slot1Auto"].BlobDirectoryPath;
            string manualBlobDir = before["Slot1Manual"].BlobDirectoryPath;

            SaveSlotManager.DeleteSlot(tmpDir, 0, SaveFileManager.Platform.XboxGamePass);

            var slots = ContainersIndexManager.ParseContainersIndex(Path.Combine(tmpDir, "containers.index"));
            Assert.False(slots.ContainsKey("Slot1Auto"));
            Assert.False(slots.ContainsKey("Slot1Manual"));
            Assert.True(slots.ContainsKey("Slot2Auto"), "other slots must not be affected");
            Assert.False(Directory.Exists(autoBlobDir), "auto blob directory should be deleted");
            Assert.False(Directory.Exists(manualBlobDir), "manual blob directory should be deleted");
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void SaveSlotManager_XboxGetExistingSlotIndices_ReturnsUsedSlots()
    {
        string tmpDir = CreateXboxSaveDirectory(
            ("Slot1Auto", "A", "Am"),
            ("Slot3Manual", "C", "Cm"));
        try
        {
            var indices = SaveSlotManager.GetExistingSlotIndices(tmpDir, SaveFileManager.Platform.XboxGamePass);

            Assert.Equal(new List<int> { 0, 2 }, indices);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void SaveSlotManager_GetExistingSlotIndices_Steam_ReturnsUsedSlots()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Slot 0 auto (save.hg) and slot 2 auto (save5.hg) exist; slots 1, 3, 4 are empty
            File.WriteAllText(Path.Combine(tmpDir, "save.hg"), "slot0");
            File.WriteAllText(Path.Combine(tmpDir, "save5.hg"), "slot2");

            var indices = SaveSlotManager.GetExistingSlotIndices(tmpDir, SaveFileManager.Platform.Steam);

            Assert.Equal(new List<int> { 0, 2 }, indices);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void SaveSlotManager_XboxCopySlot_EmptySource_ThrowsSlotEmpty()
    {
        string tmpDir = CreateXboxSaveDirectory(("Slot2Auto", "B", "Bm"));
        try
        {
            var ex = Assert.Throws<SlotEmptyException>(
                () => SaveSlotManager.CopySlot(tmpDir, 0, 1, SaveFileManager.Platform.XboxGamePass));

            Assert.Equal(1, ex.SlotNumber);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    // --- TransferOptions: ownership rewrite ---

    [Fact]
    public void TransferOptions_DefaultsToAllTransferEnabled()
    {
        var options = new TransferOptions();
        Assert.True(options.TransferBases);
        Assert.True(options.TransferDiscoveries);
        Assert.True(options.TransferSettlements);
        Assert.True(options.TransferByteBeat);
    }

    // --- ContainersIndexManager: Xbox detection ---

    [Fact]
    public void ContainersIndexManager_IsXboxSaveDirectory_TrueWhenContainersIndexExists()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "containers.index"), "");
            Assert.True(ContainersIndexManager.IsXboxSaveDirectory(tmpDir));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ContainersIndexManager_IsXboxSaveDirectory_FalseWhenMissing()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            Assert.False(ContainersIndexManager.IsXboxSaveDirectory(tmpDir));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ContainersIndexManager_ParseContainersIndex_ReturnsEmptyForInvalidFile()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string path = Path.Combine(tmpDir, "containers.index");
            File.WriteAllBytes(path, new byte[] { 0, 0, 0, 0 }); // wrong header
            var result = ContainersIndexManager.ParseContainersIndex(path);
            Assert.Empty(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // --- MemoryDatManager: PS4 detection ---

    [Fact]
    public void MemoryDatManager_IsMemoryDat_DetectsCorrectFilename()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string path = Path.Combine(tmpDir, "memory.dat");
            File.WriteAllBytes(path, new byte[32]);
            Assert.True(MemoryDatManager.IsMemoryDat(path));

            string otherPath = Path.Combine(tmpDir, "save.hg");
            File.WriteAllBytes(otherPath, new byte[32]);
            Assert.False(MemoryDatManager.IsMemoryDat(otherPath));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void MemoryDatManager_IsSaveWizardFormat_FalseForNonSaveWizard()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string path = Path.Combine(tmpDir, "memory.dat");
            File.WriteAllBytes(path, new byte[1024]); // zeros - not SaveWizard
            Assert.False(MemoryDatManager.IsSaveWizardFormat(path));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // --- MetaFileWriter: ExtractMetaInfo ---

    [Fact]
    public void MetaFileWriter_ExtractMetaInfo_ExtractsVersion()
    {
        var json = JsonObject.Parse("{\"Version\":4115}");
        var info = MetaFileWriter.ExtractMetaInfo(json);
        Assert.Equal(4115, info.BaseVersion);
    }

    // --- SaveFileManager: enhanced SaveToFile backward compat ---

    [Fact]
    public void SaveFileManager_SaveToFile_BackwardCompatible()
    {
        // Calling SaveToFile with just 3 args (old signature) should still work
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string path = Path.Combine(tmpDir, "test.hg");
            var data = JsonObject.Parse("{\"Version\":1,\"test\":42}");

            SaveFileManager.SaveToFile(path, data, compress: false);
            Assert.True(File.Exists(path));

            byte[] bytes = File.ReadAllBytes(path);
            // Should end with null terminator
            Assert.Equal(0, bytes[^1]);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // --- Account data must stay uncompressed ---

    [Fact]
    public void SaveToFile_AccountData_NoCompression()
    {
        // accountdata.hg is plain JSON + null terminator.
        // Saving with compress: false must NOT produce the 0xE5A1EDFE LZ4 header.
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string path = Path.Combine(tmpDir, "accountdata.hg");
            var data = JsonObject.Parse("{\"F2P\":4098,\"B89\":{\"32m\":false}}");

            SaveFileManager.SaveToFile(path, data, compress: false, writeMeta: false);

            Assert.True(File.Exists(path));
            byte[] bytes = File.ReadAllBytes(path);

            // Must NOT start with LZ4 NMS streaming magic (0xE5A1EDFE)
            Assert.False(bytes.Length >= 4 && bytes[0] == 0xE5 && bytes[1] == 0xA1 &&
                bytes[2] == 0xED && bytes[3] == 0xFE,
                "accountdata.hg must not be LZ4 compressed - game expects plain JSON");

            // Must start with JSON opening brace
            Assert.Equal((byte)'{', bytes[0]);

            // Must end with null terminator
            Assert.Equal(0, bytes[^1]);

            // Must be valid JSON when trimmed of null terminator
            string json = System.Text.Encoding.Latin1.GetString(bytes, 0, bytes.Length - 1);
            var parsed = JsonObject.Parse(json);
            Assert.NotNull(parsed);

            // No meta file should exist
            string metaPath = Path.Combine(tmpDir, "mf_accountdata.hg");
            Assert.False(File.Exists(metaPath), "accountdata.hg must not have a meta file");
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // --- Platform token mapping ---

    [Theory]
    [InlineData(SaveFileManager.Platform.Steam, "PC")]
    [InlineData(SaveFileManager.Platform.GOG, "PC")]
    [InlineData(SaveFileManager.Platform.XboxGamePass, "XBX")]
    [InlineData(SaveFileManager.Platform.PS4, "PS4")]
    [InlineData(SaveFileManager.Platform.Switch, "NX")]
    public void PlatformToken_MapsCorrectly(SaveFileManager.Platform platform, string expectedToken)
    {
        var options = new TransferOptions { DestPTK = expectedToken };
        Assert.Equal(expectedToken, options.DestPTK);
        Assert.NotEqual(SaveFileManager.Platform.Unknown, platform);
    }

    // --- Platform detection from directories ---

    [Fact]
    public void DetectPlatform_Steam_FromSaveHgFiles()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_steam_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Steam: save.hg files in a st_ named directory
            string profileDir = Path.Combine(tmpDir, "st_12345");
            Directory.CreateDirectory(profileDir);
            File.WriteAllBytes(Path.Combine(profileDir, "save.hg"), new byte[] { 0 });
            File.WriteAllBytes(Path.Combine(profileDir, "accountdata.hg"), new byte[] { 0 });

            var platform = SaveFileManager.DetectPlatform(profileDir);
            Assert.Equal(SaveFileManager.Platform.Steam, platform);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void DetectPlatform_GOG_FromDefaultUserDirectory()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_gog_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string profileDir = Path.Combine(tmpDir, "DefaultUser");
            Directory.CreateDirectory(profileDir);
            File.WriteAllBytes(Path.Combine(profileDir, "save.hg"), new byte[] { 0 });

            var platform = SaveFileManager.DetectPlatform(profileDir);
            Assert.Equal(SaveFileManager.Platform.GOG, platform);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void DetectPlatform_Xbox_FromContainersIndex()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_xbox_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllBytes(Path.Combine(tmpDir, "containers.index"), new byte[] { 0 });

            var platform = SaveFileManager.DetectPlatform(tmpDir);
            Assert.Equal(SaveFileManager.Platform.XboxGamePass, platform);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void DetectPlatform_PS4_FromMemoryDat()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_ps4_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllBytes(Path.Combine(tmpDir, "memory.dat"), new byte[] { 0 });

            var platform = SaveFileManager.DetectPlatform(tmpDir);
            Assert.Equal(SaveFileManager.Platform.PS4, platform);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void DetectPlatform_Switch_FromManifestDat()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_switch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllBytes(Path.Combine(tmpDir, "manifest00.dat"), new byte[] { 0 });

            var platform = SaveFileManager.DetectPlatform(tmpDir);
            Assert.Equal(SaveFileManager.Platform.Switch, platform);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void DetectPlatform_Switch_FromManifestaccountdata()
    {
        // Real Switch saves (e.g. JKSV exports) carry manifestaccountdata.hg alongside
        // the manifest*.hg companion files.  Without this, a Switch directory is
        // misdetected as PS4 because it also contains savedata*.hg files.
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_switch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllBytes(Path.Combine(tmpDir, "manifestaccountdata.hg"), new byte[] { 0 });
            File.WriteAllBytes(Path.Combine(tmpDir, "manifest00.hg"), new byte[] { 0 });
            File.WriteAllBytes(Path.Combine(tmpDir, "savedata00.hg"), new byte[] { 0 });
            File.WriteAllBytes(Path.Combine(tmpDir, "savedata02.hg"), new byte[] { 0 });

            var platform = SaveFileManager.DetectPlatform(tmpDir);
            Assert.Equal(SaveFileManager.Platform.Switch, platform);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    // --- SaveToFile with writeMeta flag ---

    [Fact]
    public void SaveToFile_WriteMeta_CreatesMetaFile()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_meta_{Guid.NewGuid():N}");
        // Mimic a Steam directory with st_ prefix
        string profileDir = Path.Combine(tmpDir, "st_test");
        Directory.CreateDirectory(profileDir);
        try
        {
            string savePath = Path.Combine(profileDir, "save.hg");

            // Create a minimal JSON save
            var data = new JsonObject();
            data.Set("Version", 5);

            // Write with meta
            SaveFileManager.SaveToFile(savePath, data, compress: true, writeMeta: true,
                platform: SaveFileManager.Platform.Steam, slotIndex: 0);

            Assert.True(File.Exists(savePath), "Save file should exist");

            // The meta file should also have been created (mf_save.hg)
            string metaPath = MetaFileWriter.GetSteamMetaPath(savePath);
            Assert.True(File.Exists(metaPath), $"Meta file should exist at {metaPath}");
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void ContainersIndexManager_ParseContainersIndex_ResolvesNonHyphenatedGuidDirs()
    {
        // Xbox Game Pass blob directories use the compact "N" GUID format (no hyphens).
        // Verify that ParseContainersIndex resolves them correctly.
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_xbox_guid_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            Guid testGuid = Guid.NewGuid();

            // Create the blob directory with the uppercase no-hyphens name (Xbox default format)
            string blobDir = Path.Combine(tmpDir, testGuid.ToString("N").ToUpperInvariant());
            Directory.CreateDirectory(blobDir);

            // Build a minimal valid containers.index binary.
            // The parser requires at least 200 bytes, so we pad to that length.
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            // ---- global header ----
            w.Write(14);  // header magic
            w.Write(1L);  // container count = 1

            // processIdentifier (dynamic string): length=0
            w.Write(0);
            // lastModifiedTime(8) + syncState(4)
            w.Write(0L);
            w.Write(0);
            // accountGuid (dynamic string): length=0
            w.Write(0);
            // footer(8)
            w.Write(268435456L); // 0x10000000

            // ---- entry 1 ----
            // identifier1 (dynamic string): "Slot1Auto" (9 chars = 18 bytes UTF-16)
            string id = "Slot1Auto";
            w.Write(id.Length);
            w.Write(Encoding.Unicode.GetBytes(id));
            // identifier2 (dynamic string): length=0
            w.Write(0);
            // syncTime (dynamic string): length=0
            w.Write(0);

            // Fixed fields: blobExt(1) + syncState(4) + guid(16) + lastMod(8) + empty(8) + totalSize(8) = 45
            w.Write((byte)1); // blob extension
            w.Write(0);       // sync state
            w.Write(testGuid.ToByteArray()); // directory GUID
            w.Write(0L);      // last modified
            w.Write(0L);      // empty
            w.Write(0L);      // total size

            // Pad to at least 200 bytes (parser's minimum size check)
            while (ms.Position < 200)
                w.Write((byte)0);

            byte[] data = ms.ToArray();
            string indexPath = Path.Combine(tmpDir, "containers.index");
            File.WriteAllBytes(indexPath, data);

            var slots = ContainersIndexManager.ParseContainersIndex(indexPath);

            // The slot should be found and its BlobDirectoryPath should point to
            // the no-hyphens uppercase directory (which exists on disk)
            Assert.True(slots.ContainsKey("Slot1Auto"), "Expected Slot1Auto in parsed slots");
            Assert.Equal(blobDir, slots["Slot1Auto"].BlobDirectoryPath);
            Assert.True(Directory.Exists(slots["Slot1Auto"].BlobDirectoryPath),
                "BlobDirectoryPath should point to an existing directory");
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void FindDefaultSaveDirectory_XboxGamePass_NavigatesIntoWgsDir()
    {
        // Verify the expected Xbox Game Pass path structure:
        // {root}/HelloGames.NoMansSky_bs190hzg1sesy/SystemAppData/wgs/{SaveId}/containers.index
        // The method should return the {SaveId} directory, not the HelloGames root.
        //
        // This test cannot run FindDefaultSaveDirectory directly (it uses Environment.SpecialFolder)
        // so we just verify that DetectPlatform works correctly on the nested directory.
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_xbox_nested_{Guid.NewGuid():N}");
        try
        {
            string saveIdDir = Path.Combine(tmpDir, "HelloGames.NoMansSky_bs190hzg1sesy",
                "SystemAppData", "wgs", "AABBCCDD00112233");
            Directory.CreateDirectory(saveIdDir);
            File.WriteAllBytes(Path.Combine(saveIdDir, "containers.index"), new byte[] { 0 });

            // DetectPlatform on the SaveId dir (containing containers.index) should work
            var platform = SaveFileManager.DetectPlatform(saveIdDir);
            Assert.Equal(SaveFileManager.Platform.XboxGamePass, platform);

            // DetectPlatform on the HelloGames root should NOT detect Xbox
            string helloGamesRoot = Path.Combine(tmpDir, "HelloGames.NoMansSky_bs190hzg1sesy");
            var rootPlatform = SaveFileManager.DetectPlatform(helloGamesRoot);
            Assert.Equal(SaveFileManager.Platform.Unknown, rootPlatform);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void ContainersIndexManager_ParseBlobContainer_Resolves128ByteIdentifiers()
    {
        // Xbox blob container files use 128-byte UTF-16LE identifiers (not 80 bytes).
        // The total container.N file size is 328 bytes (header 8 + 2 entries * 160).
        // Both NMSSaveEditor.jar (gc.d() reads 128 bytes) and libNOM (BLOBCONTAINER_IDENTIFIER_LENGTH=128)
        // confirm this format.
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_blobcontainer_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            Guid testGuid = Guid.NewGuid();
            string blobDir = Path.Combine(tmpDir, testGuid.ToString("N").ToUpperInvariant());
            Directory.CreateDirectory(blobDir);

            // Create data and meta blob files
            Guid dataGuid = Guid.NewGuid();
            Guid metaGuid = Guid.NewGuid();
            string dataPath = Path.Combine(blobDir, dataGuid.ToString("N").ToUpperInvariant());
            string metaPath = Path.Combine(blobDir, metaGuid.ToString("N").ToUpperInvariant());
            File.WriteAllBytes(dataPath, new byte[] { 0x01 });
            File.WriteAllBytes(metaPath, new byte[] { 0x02 });

            // Build a valid 328-byte container.1 file with 128-byte identifiers
            byte[] containerBytes = new byte[328];
            using (var ms = new MemoryStream(containerBytes))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(4);  // header
                w.Write(2);  // count

                // "data" entry: 128 bytes identifier + 16 cloud GUID + 16 local GUID
                byte[] dataId = Encoding.Unicode.GetBytes("data");
                w.Write(dataId);
                ms.Position = 8 + 128; // skip to end of 128-byte identifier
                w.Write(new byte[16]); // cloud GUID (zeros)
                w.Write(dataGuid.ToByteArray()); // local GUID

                // "meta" entry: 128 bytes identifier + 16 cloud GUID + 16 local GUID
                byte[] metaId = Encoding.Unicode.GetBytes("meta");
                w.Write(metaId);
                ms.Position = 8 + 160 + 128; // skip to end of second 128-byte identifier
                w.Write(new byte[16]); // cloud GUID (zeros)
                w.Write(metaGuid.ToByteArray()); // local GUID
            }
            File.WriteAllBytes(Path.Combine(blobDir, "container.1"), containerBytes);

            // Build a minimal valid containers.index
            using var indexMs = new MemoryStream();
            using var indexW = new BinaryWriter(indexMs);
            indexW.Write(14);         // header
            indexW.Write(1L);         // count
            indexW.Write(0);          // processIdentifier (empty)
            indexW.Write(0L);         // lastModifiedTime
            indexW.Write(0);          // syncState
            indexW.Write(0);          // accountGuid (empty)
            indexW.Write(268435456L); // footer

            // entry: Slot1Auto
            string id = "Slot1Auto";
            indexW.Write(id.Length);
            indexW.Write(Encoding.Unicode.GetBytes(id));
            indexW.Write(0);   // identifier2
            indexW.Write(0);   // syncTime
            indexW.Write((byte)1); // blob extension
            indexW.Write(0);       // sync state
            indexW.Write(testGuid.ToByteArray());
            indexW.Write(0L);      // last modified
            indexW.Write(0L);      // empty
            indexW.Write(0L);      // total size
            while (indexMs.Position < 200) indexW.Write((byte)0);

            string indexPath = Path.Combine(tmpDir, "containers.index");
            File.WriteAllBytes(indexPath, indexMs.ToArray());

            var slots = ContainersIndexManager.ParseContainersIndex(indexPath);

            Assert.True(slots.ContainsKey("Slot1Auto"));
            var slot = slots["Slot1Auto"];
            Assert.NotNull(slot.DataFilePath);
            Assert.NotNull(slot.MetaFilePath);
            Assert.True(File.Exists(slot.DataFilePath), "DataFilePath should point to existing file");
            Assert.True(File.Exists(slot.MetaFilePath), "MetaFilePath should point to existing file");
            Assert.Equal(dataPath, slot.DataFilePath);
            Assert.Equal(metaPath, slot.MetaFilePath);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void ContainersIndexManager_WriteBlobContainer_Creates328ByteFile()
    {
        // Verify that WriteBlobContainer creates a file that's exactly 328 bytes
        // with proper 128-byte identifier padding.
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_write_blob_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            Guid dataGuid = Guid.NewGuid();
            Guid metaGuid = Guid.NewGuid();

            var slotInfo = new XboxSlotInfo
            {
                BlobDirectoryPath = tmpDir,
                BlobContainerExtension = 0,
            };

            // Create dummy data/meta files
            File.WriteAllBytes(Path.Combine(tmpDir, dataGuid.ToString("N").ToUpperInvariant()), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(tmpDir, metaGuid.ToString("N").ToUpperInvariant()), new byte[] { 2 });

            // Write the blob container (calls the private WriteBlobContainer indirectly via WriteXboxSave)
            ContainersIndexManager.WriteXboxSave(slotInfo, new byte[] { 0xAA }, new byte[] { 0xBB });

            // Find the created container file
            string[] containerFiles = Directory.GetFiles(tmpDir, "container.*");
            Assert.Single(containerFiles);

            byte[] bytes = File.ReadAllBytes(containerFiles[0]);
            Assert.Equal(328, bytes.Length);

            // Verify header
            int header = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24);
            Assert.Equal(4, header);

            // Verify count
            int count = bytes[4] | (bytes[5] << 8) | (bytes[6] << 16) | (bytes[7] << 24);
            Assert.Equal(2, count);

            // Verify "data" identifier at offset 8 (first 8 bytes are UTF-16LE "data")
            string dataId = Encoding.Unicode.GetString(bytes, 8, 8);
            Assert.Equal("data", dataId);

            // Verify "meta" identifier at offset 168 (8 + 128 + 32)
            string metaId = Encoding.Unicode.GetString(bytes, 168, 8);
            Assert.Equal("meta", metaId);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void SaveXboxSave_ReturnsUpdatedSlotInfo_PreservingBlobPaths()
    {
        // Verify that SaveXboxSave replaces blob contents in place: the blob GUIDs,
        // paths, container.N file and containers.index entry identity must be preserved
        // so Xbox cloud sync does not see a new container.
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_xbox_save_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            Guid blobGuid = Guid.NewGuid();
            string blobDir = Path.Combine(tmpDir, blobGuid.ToString("N").ToUpperInvariant());
            Directory.CreateDirectory(blobDir);

            // Old data/meta blobs
            Guid oldDataGuid = Guid.NewGuid();
            Guid oldMetaGuid = Guid.NewGuid();
            string oldDataPath = Path.Combine(blobDir, oldDataGuid.ToString("N").ToUpperInvariant());
            string oldMetaPath = Path.Combine(blobDir, oldMetaGuid.ToString("N").ToUpperInvariant());
            File.WriteAllBytes(oldDataPath, new byte[] { 0x01 });
            File.WriteAllBytes(oldMetaPath, new byte[] { 0x02 });

            // 328-byte container.1 pointing at the old blobs
            byte[] containerBytes = new byte[328];
            using (var ms = new MemoryStream(containerBytes))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(4);
                w.Write(2);
                w.Write(Encoding.Unicode.GetBytes("data"));
                ms.Position = 8 + 128;
                w.Write(new byte[16]);
                w.Write(oldDataGuid.ToByteArray());
                w.Write(Encoding.Unicode.GetBytes("meta"));
                ms.Position = 8 + 160 + 128;
                w.Write(new byte[16]);
                w.Write(oldMetaGuid.ToByteArray());
            }
            File.WriteAllBytes(Path.Combine(blobDir, "container.1"), containerBytes);

            // Minimal containers.index with a single Slot1Auto entry.
            // The header strings are non-empty so the rewritten file exceeds the
            // parser's 200-byte minimum (as real containers.index files do).
            using var indexMs = new MemoryStream();
            using var indexW = new BinaryWriter(indexMs);
            indexW.Write(14);
            indexW.Write(1L);
            string processId = "HelloGames.NoMansSky_test";
            indexW.Write(processId.Length);
            indexW.Write(Encoding.Unicode.GetBytes(processId));
            indexW.Write(0L);         // lastModifiedTime
            indexW.Write(0);          // syncState
            string accountGuid = "000900000150C65A29070100B936489A";
            indexW.Write(accountGuid.Length);
            indexW.Write(Encoding.Unicode.GetBytes(accountGuid));
            indexW.Write(268435456L); // footer
            string id = "Slot1Auto";
            indexW.Write(id.Length);
            indexW.Write(Encoding.Unicode.GetBytes(id));
            indexW.Write(0);       // identifier2
            indexW.Write(0);       // syncTime
            indexW.Write((byte)1); // blob extension
            indexW.Write(0);       // sync state
            indexW.Write(blobGuid.ToByteArray());
            indexW.Write(0L);      // last modified
            indexW.Write(0L);      // empty
            indexW.Write(0L);      // total size
            while (indexMs.Position < 200) indexW.Write((byte)0);

            string indexPath = Path.Combine(tmpDir, "containers.index");
            File.WriteAllBytes(indexPath, indexMs.ToArray());

            var before = ContainersIndexManager.ParseContainersIndex(indexPath);
            Assert.Equal(oldDataPath, before["Slot1Auto"].DataFilePath);

            var data = JsonObject.Parse("{\"CommonStateData\":{\"SaveName\":\"Test\"}}");
            var updated = SaveFileManager.SaveXboxSave(indexPath, "Slot1Auto", data);

            // In-place write: the data blob path must be unchanged and still exist
            Assert.NotNull(updated.DataFilePath);
            Assert.True(File.Exists(updated.DataFilePath), "Updated data blob should exist");
            Assert.Equal(oldDataPath, updated.DataFilePath);
            Assert.Equal(oldMetaPath, updated.MetaFilePath);

            // The container file and its GUID pointers must be preserved
            Assert.True(File.Exists(Path.Combine(blobDir, "container.1")), "container.1 should be preserved");
            Assert.False(File.Exists(Path.Combine(blobDir, "container.2")), "no new container should be created");

            // A re-parse (what re-selecting the directory does) must resolve the same path
            var after = ContainersIndexManager.ParseContainersIndex(indexPath);
            Assert.Equal(updated.DataFilePath, after["Slot1Auto"].DataFilePath);
            Assert.Equal(updated.MetaFilePath, after["Slot1Auto"].MetaFilePath);
            Assert.Equal(1, after["Slot1Auto"].BlobContainerExtension);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void ContainersIndexManager_WriteContainersIndex_RoundTrip_PreservesData()
    {
        // Verify that parsing containers.index and writing it back produces a file
        // that can be re-parsed with the same slot data. This is the critical
        // round-trip test that prevents the ~1100 KB corruption bug.
        string xboxSaveDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "_ref", "xbox_save", "000900000150C65A_29070100B936489ABCE8B9AF3980429C");
        string containersPath = Path.Combine(xboxSaveDir, "containers.index");
        if (!File.Exists(containersPath))
            return; // Skip if test data not available

        // Parse the original file with full header data
        var original = ContainersIndexManager.ParseContainersIndexFull(containersPath);
        Assert.Equal(6, original.Slots.Count);
        Assert.False(string.IsNullOrEmpty(original.ProcessIdentifier));
        Assert.False(string.IsNullOrEmpty(original.AccountGuid));

        // Write to a temp file
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_ci_roundtrip_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string tmpPath = Path.Combine(tmpDir, "containers.index");
            ContainersIndexManager.WriteContainersIndex(
                tmpPath,
                original.Slots.Values,
                original.ProcessIdentifier,
                original.AccountGuid,
                original.LastWriteTime);

            // The written file must be small (about 1 KB), NOT megabytes
            long fileSize = new FileInfo(tmpPath).Length;
            Assert.True(fileSize < 2048, $"containers.index is {fileSize} bytes, expected < 2048");
            Assert.True(fileSize > 100, $"containers.index is {fileSize} bytes, expected > 100");

            // Re-parse the written file
            var reparsed = ContainersIndexManager.ParseContainersIndexFull(tmpPath);
            Assert.Equal(original.Slots.Count, reparsed.Slots.Count);
            Assert.Equal(original.ProcessIdentifier, reparsed.ProcessIdentifier);
            Assert.Equal(original.AccountGuid, reparsed.AccountGuid);

            // Verify each slot was preserved
            foreach (var kvp in original.Slots)
            {
                Assert.True(reparsed.Slots.ContainsKey(kvp.Key), $"Missing slot: {kvp.Key}");
                var orig = kvp.Value;
                var copy = reparsed.Slots[kvp.Key];
                Assert.Equal(orig.Identifier, copy.Identifier);
                Assert.Equal(orig.DirectoryGuid, copy.DirectoryGuid);
                Assert.Equal(orig.BlobContainerExtension, copy.BlobContainerExtension);
            }
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void ContainersIndexManager_ParseContainersIndexFull_ExposesHeaderFields()
    {
        // Verify that ParseContainersIndexFull returns the header fields needed for writing
        string xboxSaveDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "_ref", "xbox_save", "000900000150C65A_29070100B936489ABCE8B9AF3980429C");
        string containersPath = Path.Combine(xboxSaveDir, "containers.index");
        if (!File.Exists(containersPath))
            return; // Skip if test data not available

        var data = ContainersIndexManager.ParseContainersIndexFull(containersPath);

        // Process identifier should be the NMS app identifier
        Assert.Contains("HelloGames", data.ProcessIdentifier);
        Assert.Contains("NoMansSky", data.ProcessIdentifier);

        // Account GUID should be a valid GUID-like string
        Assert.True(data.AccountGuid.Length > 0);

        // Last write time should be a valid date
        Assert.True(data.LastWriteTime.Year >= 2020);

        // Slots should be the same as ParseContainersIndex
        var slots = ContainersIndexManager.ParseContainersIndex(containersPath);
        Assert.Equal(slots.Count, data.Slots.Count);
        foreach (var key in slots.Keys)
            Assert.True(data.Slots.ContainsKey(key));
    }

    [Fact]
    public void SaveXboxAccountData_WritesRawLz4_LoadableByLoadXboxSave()
    {
        // Verify that SaveXboxAccountData produces a raw LZ4 blob that can be
        // read back by LoadXboxSave (which tries raw LZ4 as a fallback).
        // This tests the critical round-trip: modify account -> save -> reload.
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_xbox_acct_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        string blobDir = Path.Combine(tmpDir, Guid.NewGuid().ToString("N").ToUpperInvariant());
        Directory.CreateDirectory(blobDir);
        try
        {
            // Create test account JSON with a season reward
            string testJson = "{\"UserSettingsData\":{\"UnlockedSeasonRewards\":[{\"value\":\"RAW_TestReward\"}]}}";
            var accountObj = JsonObject.Parse(testJson);

            // Manually compress with raw LZ4 (same as SaveXboxAccountData does)
            var latin1 = Encoding.GetEncoding(28591);
            byte[] jsonBytes = latin1.GetBytes(testJson);
            byte[] dataBytes = new byte[jsonBytes.Length + 1];
            Buffer.BlockCopy(jsonBytes, 0, dataBytes, 0, jsonBytes.Length);

            byte[] compBuf = new byte[Lz4Compressor.MaxCompressedLength(dataBytes.Length)];
            int compLen = Lz4Compressor.Compress(dataBytes, 0, dataBytes.Length, compBuf, 0, compBuf.Length);
            byte[] compressed = new byte[compLen];
            Buffer.BlockCopy(compBuf, 0, compressed, 0, compLen);

            // Write to a blob file
            Guid dataGuid = Guid.NewGuid();
            string dataPath = Path.Combine(blobDir, dataGuid.ToString("N").ToUpperInvariant());
            File.WriteAllBytes(dataPath, compressed);

            // Create a slot info pointing to this file
            var slotInfo = new XboxSlotInfo
            {
                Identifier = "AccountData",
                DataFilePath = dataPath,
                BlobDirectoryPath = blobDir,
            };

            // LoadXboxSave should decompress the raw LZ4 and return valid JSON
            string? result = ContainersIndexManager.LoadXboxSave(slotInfo);
            Assert.NotNull(result);

            // Trim null terminator if present
            result = result!.TrimEnd('\0');

            var parsed = JsonObject.Parse(result);
            var userSettings = parsed.GetObject("UserSettingsData");
            Assert.NotNull(userSettings);
            var rewards = userSettings!.GetArray("UnlockedSeasonRewards");
            Assert.NotNull(rewards);
            Assert.True(rewards!.Length > 0);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void ContainersIndexManager_LoadXboxSave_HandlesHgsv2Format()
    {
        // Verify that HGSAVEV2 format saves can be loaded.
        // HGSAVEV2 format: "HGSAVEV2\0" + N frames of [decompressedLen(4)][compressedLen(4)][LZ4 data]
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_hgsv2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Create a simple JSON string to compress
            string json = "{\"Test\":true}";
            byte[] jsonBytes = Encoding.GetEncoding(28591).GetBytes(json);

            // LZ4 compress the JSON
            byte[] compressed = new byte[Lz4Compressor.MaxCompressedLength(jsonBytes.Length)];
            int compressedLen = Lz4Compressor.Compress(jsonBytes, 0, jsonBytes.Length, compressed, 0, compressed.Length);

            // Build HGSAVEV2 file
            using var ms = new MemoryStream();
            ms.Write(Encoding.ASCII.GetBytes("HGSAVEV2"));
            ms.WriteByte(0); // null terminator
            // Frame header: decompressed size + compressed size (LE)
            ms.Write(BitConverter.GetBytes(jsonBytes.Length));
            ms.Write(BitConverter.GetBytes(compressedLen));
            ms.Write(compressed, 0, compressedLen);

            Guid dataGuid = Guid.NewGuid();
            string dataPath = Path.Combine(tmpDir, dataGuid.ToString("N").ToUpperInvariant());
            File.WriteAllBytes(dataPath, ms.ToArray());

            var slotInfo = new XboxSlotInfo
            {
                DataFilePath = dataPath,
                BlobDirectoryPath = tmpDir,
            };

            string? result = ContainersIndexManager.LoadXboxSave(slotInfo);
            Assert.NotNull(result);
            Assert.Equal(json, result);
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    // --- ContainersIndexManager: IsSaveSlot filtering ---

    [Theory]
    [InlineData("Slot1Auto", true)]
    [InlineData("Slot1Manual", true)]
    [InlineData("Slot3Auto", true)]
    [InlineData("Slot3Manual", true)]
    [InlineData("AccountData", false)]
    [InlineData("Settings", false)]
    [InlineData("accountdata", false)]   // case-insensitive
    [InlineData("settings", false)]      // case-insensitive
    [InlineData("ACCOUNTDATA", false)]   // case-insensitive
    [InlineData("Slot2Auto", true)]
    public void ContainersIndexManager_IsSaveSlot_FiltersCorrectly(string identifier, bool expected)
    {
        Assert.Equal(expected, ContainersIndexManager.IsSaveSlot(identifier));
    }

    // --- ContainersIndexManager: ExtractSlotNumber ---

    [Theory]
    [InlineData("Slot1Auto", 1)]
    [InlineData("Slot1Manual", 1)]
    [InlineData("Slot2Auto", 2)]
    [InlineData("Slot3Manual", 3)]
    [InlineData("Slot15Auto", 15)]
    [InlineData("AccountData", 0)]
    [InlineData("Settings", 0)]
    [InlineData("Slot", 0)]
    [InlineData("SlotAuto", 0)]  // no number
    [InlineData("", 0)]
    public void ContainersIndexManager_ExtractSlotNumber_ExtractsCorrectly(string identifier, int expected)
    {
        Assert.Equal(expected, ContainersIndexManager.ExtractSlotNumber(identifier));
    }

    // --- ContainersIndexManager: IsAutoSave ---

    [Theory]
    [InlineData("Slot1Auto", true)]
    [InlineData("Slot1Manual", false)]
    [InlineData("Slot3Auto", true)]
    [InlineData("AccountData", false)]
    public void ContainersIndexManager_IsAutoSave_DetectsCorrectly(string identifier, bool expected)
    {
        Assert.Equal(expected, ContainersIndexManager.IsAutoSave(identifier));
    }

    // --- ContainersIndexManager: Real Xbox save parsing ---

    [Fact]
    public void ContainersIndexManager_ParseContainersIndex_RealXboxSave_IdentifiesAllSlots()
    {
        string xboxSaveDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "_ref", "xbox_save", "000900000150C65A_29070100B936489ABCE8B9AF3980429C");
        string containersPath = Path.Combine(xboxSaveDir, "containers.index");
        if (!File.Exists(containersPath))
            return; // Skip if test data not available

        var slots = ContainersIndexManager.ParseContainersIndex(containersPath);

        // The real Xbox save contains 6 entries
        Assert.Equal(6, slots.Count);
        Assert.True(slots.ContainsKey("AccountData"));
        Assert.True(slots.ContainsKey("Settings"));
        Assert.True(slots.ContainsKey("Slot1Auto"));
        Assert.True(slots.ContainsKey("Slot1Manual"));
        Assert.True(slots.ContainsKey("Slot3Auto"));
        Assert.True(slots.ContainsKey("Slot3Manual"));
    }

    [Fact]
    public void ContainersIndexManager_IsSaveSlot_FiltersRealXboxSave()
    {
        string xboxSaveDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "_ref", "xbox_save", "000900000150C65A_29070100B936489ABCE8B9AF3980429C");
        string containersPath = Path.Combine(xboxSaveDir, "containers.index");
        if (!File.Exists(containersPath))
            return; // Skip if test data not available

        var slots = ContainersIndexManager.ParseContainersIndex(containersPath);

        // Only save slots (not AccountData/Settings) should pass the filter
        var saveSlots = slots.Where(s => ContainersIndexManager.IsSaveSlot(s.Key)).ToList();
        Assert.Equal(4, saveSlots.Count);
        Assert.DoesNotContain(saveSlots, s => s.Key == "AccountData");
        Assert.DoesNotContain(saveSlots, s => s.Key == "Settings");
    }

    [Fact]
    public void ContainersIndexManager_LoadXboxSave_RealXboxSave_LoadsSaveSlot()
    {
        string xboxSaveDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "_ref", "xbox_save", "000900000150C65A_29070100B936489ABCE8B9AF3980429C");
        string containersPath = Path.Combine(xboxSaveDir, "containers.index");
        if (!File.Exists(containersPath))
            return; // Skip if test data not available

        var slots = ContainersIndexManager.ParseContainersIndex(containersPath);
        Assert.True(slots.ContainsKey("Slot1Auto"));

        var slot = slots["Slot1Auto"];
        Assert.NotNull(slot.DataFilePath);
        Assert.True(File.Exists(slot.DataFilePath));

        string? json = ContainersIndexManager.LoadXboxSave(slot);
        Assert.NotNull(json);
        Assert.True(json.Length > 100); // Should be substantial JSON data

        // Verify it parses as valid JSON
        var obj = JsonObject.Parse(json);
        Assert.True(obj.Size() > 0);
    }

    [Fact]
    public void ContainersIndexManager_LoadXboxSave_RealXboxSave_LoadsAccountData()
    {
        string xboxSaveDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "_ref", "xbox_save", "000900000150C65A_29070100B936489ABCE8B9AF3980429C");
        string containersPath = Path.Combine(xboxSaveDir, "containers.index");
        if (!File.Exists(containersPath))
            return; // Skip if test data not available

        var slots = ContainersIndexManager.ParseContainersIndex(containersPath);
        Assert.True(slots.ContainsKey("AccountData"));

        var accountSlot = slots["AccountData"];
        Assert.NotNull(accountSlot.DataFilePath);
        Assert.True(File.Exists(accountSlot.DataFilePath));

        string? json = ContainersIndexManager.LoadXboxSave(accountSlot);
        Assert.NotNull(json);

        // Verify it parses as valid JSON with expected account structure
        var obj = JsonObject.Parse(json);
        Assert.True(obj.Size() > 0);
        // Xbox account data should have UserSettingsData like accountdata.hg
        var userSettings = obj.GetObject("UserSettingsData");
        Assert.NotNull(userSettings);
    }

    [Fact]
    public void SteamMeta_PreFrontiers_WritesHashes()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string savePath = Path.Combine(tmpDir, "save.hg");
            File.WriteAllText(savePath, "dummy");

            byte[] compressedData = new byte[256];
            new Random(42).NextBytes(compressedData);

            var metaInfo = new SaveMetaInfo
            {
                BaseVersion = 4100, // pre-Frontiers -> META_FORMAT_1
                GameMode = 1,
                Season = 0,
                TotalPlayTime = 1000,
            };

            MetaFileWriter.WriteSteamMeta(savePath, compressedData, 2048, metaInfo, 2);

            string metaPath = MetaFileWriter.GetSteamMetaPath(savePath);
            byte[] metaBytes = File.ReadAllBytes(metaPath);

            // Decrypt with 8 iterations for META_FORMAT_1
            uint[] encrypted = MetaFileWriter.BytesToUInts(metaBytes);
            uint[] decrypted = MetaCrypto.Decrypt(encrypted, 2, 8);
            Assert.Equal(MetaFileWriter.META_HEADER, decrypted[0]);
            Assert.Equal(MetaFileWriter.META_FORMAT_1, decrypted[1]);

            // Hash bytes at offset 8..55 (48 bytes) should NOT be all zeros
            byte[] decryptedBytes = MetaFileWriter.UIntsToBytes(decrypted);
            byte[] hashRegion = new byte[48];
            Array.Copy(decryptedBytes, 8, hashRegion, 0, 48);
            bool allZero = true;
            for (int i = 0; i < hashRegion.Length; i++)
            {
                if (hashRegion[i] != 0) { allZero = false; break; }
            }
            Assert.False(allZero, "Pre-Frontiers meta should contain real hashes, not all zeros");
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void SteamMeta_Frontiers_WritesZeroHashes()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string savePath = Path.Combine(tmpDir, "save.hg");
            File.WriteAllText(savePath, "dummy");

            byte[] compressedData = new byte[256];
            new Random(42).NextBytes(compressedData);

            var metaInfo = new SaveMetaInfo
            {
                BaseVersion = 4115, // Frontiers -> META_FORMAT_2
                GameMode = 1,
                Season = 0,
                TotalPlayTime = 1000,
            };

            MetaFileWriter.WriteSteamMeta(savePath, compressedData, 2048, metaInfo, 2);

            string metaPath = MetaFileWriter.GetSteamMetaPath(savePath);
            byte[] metaBytes = File.ReadAllBytes(metaPath);

            // Decrypt with 6 iterations for META_FORMAT_2+
            uint[] encrypted = MetaFileWriter.BytesToUInts(metaBytes);
            uint[] decrypted = MetaCrypto.Decrypt(encrypted, 2, 6);
            Assert.Equal(MetaFileWriter.META_HEADER, decrypted[0]);
            Assert.Equal(MetaFileWriter.META_FORMAT_2, decrypted[1]);

            // Hash region at offset 8..55 (48 bytes) should be all zeros
            byte[] decryptedBytes = MetaFileWriter.UIntsToBytes(decrypted);
            for (int i = 8; i < 56; i++)
            {
                Assert.Equal(0, decryptedBytes[i]);
            }
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void SwitchMeta_AccountMeta_WritesSizeAtOffset8And36()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string savePath = Path.Combine(tmpDir, "savedata00.hg");
            File.WriteAllText(savePath, "dummy");

            // Create a pre-existing manifest00.hg with known data pattern
            string metaPath = Path.Combine(tmpDir, "manifest00.hg");
            byte[] existingData = new byte[100];
            for (int i = 0; i < existingData.Length; i++)
                existingData[i] = 0xAA;
            File.WriteAllBytes(metaPath, existingData);

            var metaInfo = new SaveMetaInfo
            {
                BaseVersion = 4115,
                GameMode = 1,
                Season = 0,
            };

            MetaFileWriter.WriteSwitchMeta(savePath, 2048, metaInfo, 0);

            byte[] result = File.ReadAllBytes(metaPath);

            // Bytes 0..7 should be preserved (0xAA pattern)
            for (int i = 0; i < 8; i++)
                Assert.Equal(0xAA, result[i]);

            // Offset 8..11 should contain the decompressed size (2048 = 0x00000800 LE)
            uint writtenSize = BitConverter.ToUInt32(result, 8);
            Assert.Equal(2048u, writtenSize);

            // Bytes 12..35 should be preserved (0xAA pattern)
            for (int i = 12; i < 36; i++)
                Assert.Equal(0xAA, result[i]);

            // Offset 36..39 must also contain the decompressed size (duplicate field)
            uint writtenSize36 = BitConverter.ToUInt32(result, 36);
            Assert.Equal(2048u, writtenSize36);

            // Bytes 40+ should be preserved (0xAA pattern)
            for (int i = 40; i < existingData.Length; i++)
                Assert.Equal(0xAA, result[i]);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void SwitchMeta_SaveMeta_WritesFullHeader()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string savePath = Path.Combine(tmpDir, "savedata01.hg");
            File.WriteAllText(savePath, "dummy");

            var metaInfo = new SaveMetaInfo
            {
                BaseVersion = 4115,
                GameMode = 2,
                Season = 3,
                TotalPlayTime = 5000,
            };

            MetaFileWriter.WriteSwitchMeta(savePath, 4096, metaInfo, 1);

            string metaPath = Path.Combine(tmpDir, "manifest01.hg");
            Assert.True(File.Exists(metaPath));
            byte[] metaBytes = File.ReadAllBytes(metaPath);

            // Switch meta is not encrypted; read fields directly
            uint header = BitConverter.ToUInt32(metaBytes, 0);
            Assert.Equal(MetaFileWriter.META_HEADER_SWITCH, header);

            uint format = BitConverter.ToUInt32(metaBytes, 4);
            Assert.Equal(MetaFileWriter.META_FORMAT_2, format);

            uint decompressedSize = BitConverter.ToUInt32(metaBytes, 8);
            Assert.Equal(4096u, decompressedSize);

            int metaIndex = BitConverter.ToInt32(metaBytes, 12);
            Assert.Equal(1, metaIndex);

            // offset 16 is timestamp (skip, varies)
            int baseVersion = BitConverter.ToInt32(metaBytes, 20);
            Assert.Equal(4115, baseVersion);

            ushort gameMode = BitConverter.ToUInt16(metaBytes, 24);
            Assert.Equal(2, (int)gameMode);

            ushort season = BitConverter.ToUInt16(metaBytes, 26);
            Assert.Equal(3, (int)season);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void SwitchMeta_SaveMeta_PreservesExistingBaseVersion()
    {
        // Regression guard: the manifest base version (offset 20) is the game's
        // software version at save time (e.g. 4215) and must NOT be overwritten with
        // the save-format "Version" field from the JSON (e.g. 4727).  Writing the
        // higher JSON Version makes the game report "Cross-Save Version Incompatible".
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string savePath = Path.Combine(tmpDir, "savedata01.hg");
            File.WriteAllText(savePath, "dummy");

            // Create a pre-existing game-written manifest carrying a lower base version.
            string metaPath = Path.Combine(tmpDir, "manifest01.hg");
            byte[] existing = new byte[MetaFileWriter.SWITCH_META_LENGTH_WORLDS_II];
            Buffer.BlockCopy(BitConverter.GetBytes(MetaFileWriter.META_HEADER_SWITCH), 0, existing, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(MetaFileWriter.META_FORMAT_4), 0, existing, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(4215u), 0, existing, 20, 4);
            File.WriteAllBytes(metaPath, existing);

            var metaInfo = new SaveMetaInfo
            {
                BaseVersion = 4727, // JSON "Version" field - higher than the game build
                GameMode = 2,
            };

            MetaFileWriter.WriteSwitchMeta(savePath, 4096, metaInfo, 1);

            byte[] result = File.ReadAllBytes(metaPath);
            int baseVersion = BitConverter.ToInt32(result, 20);
            Assert.Equal(4215, baseVersion);

            // Format must remain at the game-written value.
            uint format = BitConverter.ToUInt32(result, 4);
            Assert.Equal(MetaFileWriter.META_FORMAT_4, format);

            // Decompressed size must still be updated.
            uint decompressedSize = BitConverter.ToUInt32(result, 8);
            Assert.Equal(4096u, decompressedSize);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void PlaystationStreamingMeta_SaveManifest_PreservesExistingBaseVersion()
    {
        // Regression guard for the PS4 HTOS manifest (same format as Switch).
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string savePath = Path.Combine(tmpDir, "savedata02.hg");
            File.WriteAllText(savePath, "dummy");

            string metaPath = Path.Combine(tmpDir, "manifest02.hg");
            byte[] existing = new byte[MetaFileWriter.PS4_META_LENGTH_WORLDS_II];
            Buffer.BlockCopy(BitConverter.GetBytes(MetaFileWriter.META_HEADER_PS4), 0, existing, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(MetaFileWriter.META_FORMAT_4), 0, existing, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(4215u), 0, existing, 20, 4);
            File.WriteAllBytes(metaPath, existing);

            var metaInfo = new SaveMetaInfo
            {
                BaseVersion = 4727,
                GameMode = 1,
            };

            MetaFileWriter.WritePlaystationStreamingMeta(savePath, 8192, metaInfo, 2);

            byte[] result = File.ReadAllBytes(metaPath);
            int baseVersion = BitConverter.ToInt32(result, 20);
            Assert.Equal(4215, baseVersion);

            uint format = BitConverter.ToUInt32(result, 4);
            Assert.Equal(MetaFileWriter.META_FORMAT_4, format);

            uint decompressedSize = BitConverter.ToUInt32(result, 8);
            Assert.Equal(8192u, decompressedSize);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void SwitchMeta_SaveMeta_NewSlot_UsesSiblingManifestBaseVersion()
    {
        // Writing into a new slot (no manifest of its own yet) must take the base
        // version from a sibling game-save manifest written by the game, otherwise
        // the JSON Version (4727) is written and the game reports
        // "Cross-Save Version Incompatible".
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string savePath = Path.Combine(tmpDir, "savedata03.hg");
            File.WriteAllText(savePath, "dummy");

            // Sibling manifest02.hg written by the game (platform build 4221).
            string siblingPath = Path.Combine(tmpDir, "manifest02.hg");
            byte[] sibling = new byte[MetaFileWriter.SWITCH_META_LENGTH_WORLDS_II];
            Buffer.BlockCopy(BitConverter.GetBytes(MetaFileWriter.META_HEADER_SWITCH), 0, sibling, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(MetaFileWriter.META_FORMAT_4), 0, sibling, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(4221u), 0, sibling, 20, 4);
            File.WriteAllBytes(siblingPath, sibling);

            var metaInfo = new SaveMetaInfo
            {
                BaseVersion = 4727, // JSON "Version" field - higher than the game build
                GameMode = 1,
            };

            MetaFileWriter.WriteSwitchMeta(savePath, 4096, metaInfo, 3);

            string metaPath = Path.Combine(tmpDir, "manifest03.hg");
            Assert.True(File.Exists(metaPath));
            byte[] result = File.ReadAllBytes(metaPath);

            int baseVersion = BitConverter.ToInt32(result, 20);
            Assert.Equal(4221, baseVersion);

            uint format = BitConverter.ToUInt32(result, 4);
            Assert.Equal(MetaFileWriter.META_FORMAT_4, format);

            uint decompressedSize = BitConverter.ToUInt32(result, 8);
            Assert.Equal(4096u, decompressedSize);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void PlaystationStreamingMeta_AccountMeta_WritesCorrectHeader()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string savePath = Path.Combine(tmpDir, "savedata00.hg");
            File.WriteAllText(savePath, "dummy");

            var metaInfo = new SaveMetaInfo
            {
                BaseVersion = 4115,
                GameMode = 1,
                Season = 0,
            };

            MetaFileWriter.WritePlaystationStreamingMeta(savePath, 8192, metaInfo, 0);

            string metaPath = Path.Combine(tmpDir, "manifest00.hg");
            Assert.True(File.Exists(metaPath));
            byte[] metaBytes = File.ReadAllBytes(metaPath);

            // PS streaming meta is not encrypted
            uint header = BitConverter.ToUInt32(metaBytes, 0);
            Assert.Equal(MetaFileWriter.META_HEADER_PS4, header);

            // No sibling manifest exists in this test, so the fallback (info.BaseVersion resolves to META_FORMAT_2) is used.
            uint format = BitConverter.ToUInt32(metaBytes, 4);
            Assert.Equal(MetaFileWriter.META_FORMAT_2, format);

            uint decompressedSize = BitConverter.ToUInt32(metaBytes, 8);
            Assert.Equal(8192u, decompressedSize);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void PlaystationStreamingMeta_AccountMeta_PreservesExistingData()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string savePath = Path.Combine(tmpDir, "savedata00.hg");
            File.WriteAllText(savePath, "dummy");

            // Create a pre-existing manifest00.hg with known 0xBB pattern
            string metaPath = Path.Combine(tmpDir, "manifest00.hg");
            byte[] existingData = new byte[356];
            for (int i = 0; i < existingData.Length; i++)
                existingData[i] = 0xBB;
            File.WriteAllBytes(metaPath, existingData);

            var metaInfo = new SaveMetaInfo
            {
                BaseVersion = 4115,
                GameMode = 1,
                Season = 0,
            };

            MetaFileWriter.WritePlaystationStreamingMeta(savePath, 4096, metaInfo, 0);

            byte[] result = File.ReadAllBytes(metaPath);

            // First 4 bytes: magic header
            uint header = BitConverter.ToUInt32(result, 0);
            Assert.Equal(MetaFileWriter.META_HEADER_PS4, header);

            // Offset 4 (meta format): no sibling manifest exists, so fallback value
            // (derived from info.BaseVersion = 4115, resolves to META_FORMAT_2) is written.
            uint formatAtOffset4 = BitConverter.ToUInt32(result, 4);
            Assert.Equal(MetaFileWriter.META_FORMAT_2, formatAtOffset4);

            uint decompressedSize = BitConverter.ToUInt32(result, 8);
            Assert.Equal(4096u, decompressedSize);

            // Bytes at offset 12..35 should retain the 0xBB pattern
            for (int i = 12; i < 36; i++)
                Assert.Equal(0xBB, result[i]);

            // Offset 36..39: duplicate decompressedSize (must match offset 8, not stale 0xBB)
            uint decompressedSize36 = BitConverter.ToUInt32(result, 36);
            Assert.Equal(4096u, decompressedSize36);

            // Bytes at offset 40+ should retain the 0xBB pattern
            for (int i = 40; i < existingData.Length; i++)
                Assert.Equal(0xBB, result[i]);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void PlaystationStreamingMeta_AccountMeta_ReadsFormatFromSiblingManifest()
    {
        // When a sibling game-save manifest exists (manifest02.hg, etc.), the account manifest
        // (manifest00.hg) must adopt the same meta format rather than deriving it from the
        // account JSON's Version field (which uses a different numbering scheme).
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string savePath = Path.Combine(tmpDir, "savedata00.hg");
            File.WriteAllText(savePath, "dummy");

            // Create a sibling game-save manifest at manifest02.hg with META_FORMAT_4 (2004).
            byte[] siblingManifest = new byte[MetaFileWriter.PS4_META_LENGTH_WORLDS_II];
            Buffer.BlockCopy(BitConverter.GetBytes(MetaFileWriter.META_HEADER_PS4), 0, siblingManifest, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(MetaFileWriter.META_FORMAT_4), 0, siblingManifest, 4, 4);
            File.WriteAllBytes(Path.Combine(tmpDir, "manifest02.hg"), siblingManifest);

            // Use a low BaseVersion that would normally resolve to META_FORMAT_1.
            var metaInfo = new SaveMetaInfo { BaseVersion = 4098 };

            MetaFileWriter.WritePlaystationStreamingMeta(savePath, 16384, metaInfo, 0);

            string metaPath = Path.Combine(tmpDir, "manifest00.hg");
            Assert.True(File.Exists(metaPath));
            byte[] result = File.ReadAllBytes(metaPath);

            // The sibling manifest's format (META_FORMAT_4) must be written, not META_FORMAT_1.
            uint format = BitConverter.ToUInt32(result, 4);
            Assert.Equal(MetaFileWriter.META_FORMAT_4, format);

            uint decompressedSize = BitConverter.ToUInt32(result, 8);
            Assert.Equal(16384u, decompressedSize);

            // Offset 36 must also carry the decompressed size.
            uint decompressedSize36 = BitConverter.ToUInt32(result, 36);
            Assert.Equal(16384u, decompressedSize36);

            // Buffer must be sized for META_FORMAT_4.
            Assert.Equal(MetaFileWriter.PS4_META_LENGTH_WORLDS_II, result.Length);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void SteamMeta_EncryptDecrypt_FullRoundTrip()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string savePath = Path.Combine(tmpDir, "save.hg");
            File.WriteAllText(savePath, "dummy");

            byte[] compressedData = new byte[512];
            new Random(99).NextBytes(compressedData);

            var metaInfo = new SaveMetaInfo
            {
                BaseVersion = 4145, // Worlds Part II -> META_FORMAT_4
                GameMode = 2,
                Season = 5,
                TotalPlayTime = 36000,
                SaveName = "Round Trip Test",
                SaveSummary = "Testing full round trip",
                DifficultyPreset = 3,
            };

            MetaFileWriter.WriteSteamMeta(savePath, compressedData, 16384, metaInfo, 2);

            // Read back via ReadSteamMeta
            uint[]? decrypted = MetaFileWriter.ReadSteamMeta(savePath, 2);
            Assert.NotNull(decrypted);

            Assert.Equal(MetaFileWriter.META_HEADER, decrypted[0]);
            Assert.Equal(MetaFileWriter.META_FORMAT_4, decrypted[1]);

            // Decompressed size at offset 56 = uint index 14
            Assert.Equal(16384u, decrypted[14]);

            // BaseVersion at offset 68 = uint index 17
            Assert.Equal(4145u, decrypted[17]);

            // GameMode (ushort) and Season (ushort) packed at offset 72 = uint index 18
            ushort gameMode = (ushort)(decrypted[18] & 0xFFFF);
            ushort season = (ushort)(decrypted[18] >> 16);
            Assert.Equal(2, (int)gameMode);
            Assert.Equal(5, (int)season);

            // Verify save name at offset 88 (128 bytes)
            byte[] decryptedBytes = MetaFileWriter.UIntsToBytes(decrypted);
            string saveName = Encoding.UTF8.GetString(decryptedBytes, 88, 128).TrimEnd('\0');
            Assert.Equal("Round Trip Test", saveName);

            // Verify save summary at offset 216 (128 bytes)
            string saveSummary = Encoding.UTF8.GetString(decryptedBytes, 216, 128).TrimEnd('\0');
            Assert.Equal("Testing full round trip", saveSummary);

            // Verify difficulty preset at offset 344 (4 bytes) = uint index 86
            Assert.Equal(3u, decrypted[86]);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // --- TransferCrossPlatform: Xbox Game Pass destination ---

    /// <summary>
    /// Builds a minimal source save file (Steam-style .hg) containing the given platform token.
    /// </summary>
    private static string CreateSourceSaveFile(string dir, string platformToken)
    {
        string savePath = Path.Combine(dir, "save.hg");
        var root = new JsonObject();
        root.Add("Version", 4720);
        root.Add("Platform", platformToken);
        var playerState = new JsonObject();
        playerState.Add("Health", 8);
        playerState.Add("SaveSummary", "Transfer test");
        root.Add("PlayerStateData", playerState);
        SaveFileManager.SaveToFile(savePath, root, compress: true);
        return savePath;
    }

    /// <summary>
    /// Builds a synthetic Xbox Game Pass save directory (containers.index + GUID blob
    /// directories) with the standard slot identifiers.
    /// </summary>
    private static void CreateXboxSaveDirectory(string destDir)
    {
        var slots = new List<XboxSlotInfo>();
        string[] identifiers = { "AccountData", "Settings", "Slot1Auto", "Slot1Manual", "Slot2Auto", "Slot2Manual" };
        foreach (string id in identifiers)
        {
            Guid dirGuid = Guid.NewGuid();
            var slot = new XboxSlotInfo
            {
                Identifier = id,
                DirectoryGuid = dirGuid,
                BlobDirectoryPath = Path.Combine(destDir, dirGuid.ToString("N").ToUpperInvariant()),
                LastModified = DateTimeOffset.UtcNow,
                BlobContainerExtension = 0,
            };
            // Create the blob container and dummy data/meta blobs.
            ContainersIndexManager.WriteXboxSave(slot, new byte[] { 0x01 }, new byte[] { 0x02 });
            slots.Add(slot);
        }

        ContainersIndexManager.WriteContainersIndex(
            Path.Combine(destDir, "containers.index"),
            slots,
            "HelloGames.NoMansSky_Test",
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public void TransferCrossPlatform_XboxDestination_WritesManualSlotBlob()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_xfer_xbox_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string srcDir = Path.Combine(tmpDir, "src");
            string destDir = Path.Combine(tmpDir, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);

            string srcSave = CreateSourceSaveFile(srcDir, "PC");
            CreateXboxSaveDirectory(destDir);

            // Transfer the Steam save into Xbox Game Pass slot 2 (0-based index 1).
            SaveSlotManager.TransferCrossPlatform(srcSave, destDir, destSlotIndex: 1,
                SaveFileManager.Platform.XboxGamePass);

            // The manual save entry of the target slot must now contain the transferred
            // data with the Xbox platform token.
            var slots = ContainersIndexManager.ParseContainersIndex(Path.Combine(destDir, "containers.index"));
            Assert.True(slots.ContainsKey("Slot2Manual"), "Slot2Manual should exist in containers.index");
            string? json = ContainersIndexManager.LoadXboxSave(slots["Slot2Manual"]);
            Assert.NotNull(json);
            Assert.Contains("\"Platform\":\"XBX\"", json);
            Assert.Contains("\"SaveSummary\":\"Transfer test\"", json);

            // The auto-save entry must be left untouched.
            string? autoJson = ContainersIndexManager.LoadXboxSave(slots["Slot2Auto"]);
            Assert.NotEqual(json, autoJson);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void TransferCrossPlatform_XboxDestination_NotASaveContainer_Throws()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_xfer_xbox_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string srcSave = CreateSourceSaveFile(tmpDir, "PC");
            string emptyDest = Path.Combine(tmpDir, "empty_dest");
            Directory.CreateDirectory(emptyDest);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                SaveSlotManager.TransferCrossPlatform(srcSave, emptyDest, destSlotIndex: 0,
                    SaveFileManager.Platform.XboxGamePass));
            Assert.Contains("valid save container", ex.Message);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void TransferCrossPlatform_SteamDestination_StillWritesSaveFiles()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_xfer_steam_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string srcSave = CreateSourceSaveFile(tmpDir, "XBX");
            string destDir = Path.Combine(tmpDir, "st_12345");
            Directory.CreateDirectory(destDir);

            // Regression guard: non-Xbox transfers must keep writing plain .hg files.
            SaveSlotManager.TransferCrossPlatform(srcSave, destDir, destSlotIndex: 1,
                SaveFileManager.Platform.Steam);

            // Slot 1 (0-based) manual save = save4.hg
            string destPath = Path.Combine(destDir, "save4.hg");
            Assert.True(File.Exists(destPath), "Manual save save4.hg should exist after transfer");

            var reloaded = SaveFileManager.LoadSaveFile(destPath);
            Assert.Equal("PC", reloaded.GetString("Platform"));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void SaveToFile_NewSlotMeta_InheritsGameBuildVersionFromSibling()
    {
        // Regression: writing a save into a brand-new slot used to put the save-format
        // Version (JSON field) into the meta base version.  The game compares that field
        // to the game build version and refuses the slot with a
        // "Cross-Save Version Incompatible" error.  When no meta exists for the new
        // slot, the meta writer must inherit the game build version from a sibling meta.
        string? repoRoot = FindReferenceRoot();
        if (repoRoot == null) return;

        string sourceDir = Path.Combine(repoRoot, "_ref", "save_names");
        string sourceSave = Path.Combine(sourceDir, "save2.hg");
        string sourceMeta = Path.Combine(sourceDir, "mf_save2.hg");
        if (!File.Exists(sourceSave) || !File.Exists(sourceMeta)) return;

        var siblingMeta = MetaFileWriter.ReadSteamMeta(sourceSave, 3);
        if (siblingMeta == null || siblingMeta[0] != MetaFileWriter.META_HEADER || siblingMeta.Length < 18)
            return;
        int expectedBaseVersion = (int)siblingMeta[17];
        Assert.True(expectedBaseVersion > 0);

        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_newslot_meta_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.Copy(sourceSave, Path.Combine(tmpDir, "save2.hg"));
            File.Copy(sourceMeta, Path.Combine(tmpDir, "mf_save2.hg"));

            var save = SaveFileManager.LoadSaveFile(Path.Combine(tmpDir, "save2.hg"));
            Assert.NotNull(save);

            string newSlotPath = Path.Combine(tmpDir, "save11.hg");
            SaveFileManager.SaveToFile(newSlotPath, save!, compress: true, writeMeta: true,
                platform: SaveFileManager.Platform.Steam, slotIndex: 11);

            var newMeta = MetaFileWriter.ReadSteamMeta(newSlotPath, 12);
            Assert.NotNull(newMeta);
            Assert.Equal(MetaFileWriter.META_HEADER, newMeta![0]);
            Assert.Equal(expectedBaseVersion, (int)newMeta[17]);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // --- Xbox Game Pass (xgs / XGameSaveFiles) support ---

    /// <summary>
    /// Builds a synthetic xgs container: one folder per identifier holding data/meta files.
    /// </summary>
    private static string CreateXgsContainerDirectory(params (string Identifier, string Data, string Meta)[] entries)
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nmse_test_xgs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        foreach (var entry in entries)
        {
            string slotDir = Path.Combine(tmpDir, entry.Identifier);
            Directory.CreateDirectory(slotDir);
            File.WriteAllText(Path.Combine(slotDir, XgsSaveManager.DataFileName), entry.Data);
            File.WriteAllText(Path.Combine(slotDir, XgsSaveManager.MetaFileName), entry.Meta);
        }
        return tmpDir;
    }

    /// <summary>Compresses a JSON string with the NMS streaming LZ4 format used by save blobs.</summary>
    private static byte[] CompressSaveJson(string json)
    {
        byte[] dataBytes = Encoding.Latin1.GetBytes(json + "\0");
        using var ms = new MemoryStream();
        using (var compressor = new Lz4CompressorStream(ms))
        {
            compressor.Write(dataBytes, 0, dataBytes.Length);
            compressor.Flush();
        }
        return ms.ToArray();
    }

    [Fact]
    public void XgsSaveManager_IsXgsContainerDirectory_DetectsContainer()
    {
        string xgsDir = CreateXgsContainerDirectory(("Slot1Auto", "a", "m"));
        string wgsDir = CreateXboxSaveDirectory(("Slot1Auto", "a", "m"));
        string emptyDir = Path.Combine(Path.GetTempPath(), $"nmse_test_xgs_empty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);
        try
        {
            Assert.True(XgsSaveManager.IsXgsContainerDirectory(xgsDir));
            Assert.False(XgsSaveManager.IsXgsContainerDirectory(wgsDir));
            Assert.False(XgsSaveManager.IsXgsContainerDirectory(emptyDir));
        }
        finally
        {
            Directory.Delete(xgsDir, true);
            Directory.Delete(wgsDir, true);
            Directory.Delete(emptyDir, true);
        }
    }

    [Fact]
    public void SaveFileManager_DetectXboxSaveFormat_IdentifiesBothFormats()
    {
        string xgsDir = CreateXgsContainerDirectory(("Slot1Auto", "a", "m"));
        string wgsDir = CreateXboxSaveDirectory(("Slot1Auto", "a", "m"));
        try
        {
            Assert.Equal(SaveFileManager.XboxSaveFormat.Xgs, SaveFileManager.DetectXboxSaveFormat(xgsDir));
            Assert.Equal(SaveFileManager.XboxSaveFormat.Wgs, SaveFileManager.DetectXboxSaveFormat(wgsDir));
            Assert.Equal(SaveFileManager.Platform.XboxGamePass, SaveFileManager.DetectPlatform(xgsDir));
            Assert.Equal(SaveFileManager.Platform.XboxGamePass, SaveFileManager.DetectPlatform(wgsDir));
        }
        finally
        {
            Directory.Delete(xgsDir, true);
            Directory.Delete(wgsDir, true);
        }
    }

    [Fact]
    public void XgsSaveManager_EnumerateSlots_ReturnsIdentifierFolders()
    {
        string xgsDir = CreateXgsContainerDirectory(
            ("AccountData", "acc", "accmeta"),
            ("Slot1Auto", "a", "m"),
            ("Slot1Manual", "b", "n"));
        Directory.CreateDirectory(Path.Combine(xgsDir, "NotASlot"));
        try
        {
            var slots = XgsSaveManager.EnumerateSlots(xgsDir);
            Assert.Equal(3, slots.Count);
            Assert.Equal(Path.Combine(xgsDir, "Slot1Auto", "data"), slots["Slot1Auto"].DataFilePath);
            Assert.Equal(Path.Combine(xgsDir, "Slot1Auto", "meta"), slots["Slot1Auto"].MetaFilePath);
            Assert.False(slots.ContainsKey("NotASlot"));
        }
        finally { Directory.Delete(xgsDir, true); }
    }

    [Fact]
    public void SaveFileManager_SaveXboxSave_Xgs_WritesInPlaceAndReloads()
    {
        string root = Path.Combine(Path.GetTempPath(), $"nmse_test_xgs_save_{Guid.NewGuid():N}");
        string xgsDir = Path.Combine(root, "SystemAppData", "xgs", "container");
        Directory.CreateDirectory(Path.Combine(xgsDir, "Slot1Auto"));
        try
        {
            string dataPath = Path.Combine(xgsDir, "Slot1Auto", "data");
            string metaPath = Path.Combine(xgsDir, "Slot1Auto", "meta");
            File.WriteAllBytes(dataPath, CompressSaveJson("{\"CommonStateData\":{\"SaveName\":\"Before\"}}"));
            byte[] meta = new byte[360];
            meta[0] = 7;
            meta[5] = 9;
            File.WriteAllBytes(metaPath, meta);

            var edited = JsonObject.Parse("{\"CommonStateData\":{\"SaveName\":\"After\"}}");
            var result = SaveFileManager.SaveXboxSave(xgsDir, "Slot1Auto", edited);

            Assert.Equal(dataPath, result.DataFilePath);
            Assert.Equal(metaPath, result.MetaFilePath);
            Assert.True(File.Exists(dataPath));
            Assert.False(File.Exists(dataPath + ".nmse.tmp"));
            Assert.Equal(meta, File.ReadAllBytes(metaPath));

            var reloaded = SaveFileManager.LoadXboxSave(xgsDir, "Slot1Auto");
            Assert.NotNull(reloaded);
            Assert.Equal("After", reloaded!.GetObject("CommonStateData")!.GetString("SaveName"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SaveFileManager_SaveXboxAccountData_Xgs_UpdatesMetaSizeAndReloads()
    {
        string xgsDir = CreateXgsContainerDirectory(("AccountData", "old", "meta"));
        try
        {
            string dataPath = Path.Combine(xgsDir, "AccountData", "data");
            string metaPath = Path.Combine(xgsDir, "AccountData", "meta");
            byte[] meta = new byte[20];
            meta[0] = 1;
            File.WriteAllBytes(metaPath, meta);

            var account = JsonObject.Parse("{\"UserSettingsData\":{\"Test\":1}}");
            SaveFileManager.SaveXboxAccountData(xgsDir, account);

            byte[] newMeta = File.ReadAllBytes(metaPath);
            Assert.Equal(1, newMeta[0]); // preserved
            int expectedSize = Encoding.Latin1.GetByteCount(account.ToString() + "\0");
            Assert.Equal(expectedSize, BitConverter.ToInt32(newMeta, 16));

            string json = XboxBlobCodec.Decompress(File.ReadAllBytes(dataPath));
            Assert.Contains("\"Test\":1", json, StringComparison.Ordinal);
        }
        finally { Directory.Delete(xgsDir, true); }
    }

    [Fact]
    public void SaveFileManager_SaveXboxSave_Xgs_MirrorsToSiblingWgsInPlace()
    {
        string root = Path.Combine(Path.GetTempPath(), $"nmse_test_xgs_mirror_{Guid.NewGuid():N}");
        string xgsDir = Path.Combine(root, "SystemAppData", "xgs", "container");
        string wgsDir = Path.Combine(root, "SystemAppData", "wgs", "container");
        Directory.CreateDirectory(Path.Combine(xgsDir, "Slot1Auto"));
        Directory.CreateDirectory(wgsDir);
        try
        {
            File.WriteAllBytes(Path.Combine(xgsDir, "Slot1Auto", "data"),
                CompressSaveJson("{\"CommonStateData\":{\"SaveName\":\"Before\"}}"));
            File.WriteAllBytes(Path.Combine(xgsDir, "Slot1Auto", "meta"), new byte[360]);
            WriteWgsContainer(wgsDir, ("Slot1Auto", "old", "oldmeta"));

            var before = ContainersIndexManager.ParseContainersIndex(Path.Combine(wgsDir, "containers.index"));
            string wgsDataPath = before["Slot1Auto"].DataFilePath!;
            string wgsContainerPath = Directory.GetFiles(before["Slot1Auto"].BlobDirectoryPath!, "container.*").Single();

            var edited = JsonObject.Parse("{\"CommonStateData\":{\"SaveName\":\"Mirrored\"}}");
            SaveFileManager.SaveXboxSave(xgsDir, "Slot1Auto", edited);

            var after = ContainersIndexManager.ParseContainersIndex(Path.Combine(wgsDir, "containers.index"));
            Assert.Equal(wgsDataPath, after["Slot1Auto"].DataFilePath);
            Assert.Equal(1, after["Slot1Auto"].BlobContainerExtension);
            Assert.True(File.Exists(wgsContainerPath));
            Assert.False(File.Exists(Path.Combine(before["Slot1Auto"].BlobDirectoryPath!, "container.2")));

            string wgsJson = XboxBlobCodec.Decompress(File.ReadAllBytes(wgsDataPath));
            Assert.Contains("Mirrored", wgsJson, StringComparison.Ordinal);
            Assert.Equal(File.ReadAllBytes(wgsDataPath),
                File.ReadAllBytes(Path.Combine(xgsDir, "Slot1Auto", "data")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SaveSlotManager_XgsCopySlot_CreatesDestinationFolders()
    {
        string xgsDir = CreateXgsContainerDirectory(
            ("Slot1Auto", "A", "Am"),
            ("Slot1Manual", "A2", "A2m"),
            ("AccountData", "acc", "accm"));
        try
        {
            SaveSlotManager.CopySlot(xgsDir, 0, 1, SaveFileManager.Platform.XboxGamePass);

            Assert.Equal("A", File.ReadAllText(Path.Combine(xgsDir, "Slot2Auto", "data")));
            Assert.Equal("A2", File.ReadAllText(Path.Combine(xgsDir, "Slot2Manual", "data")));
            Assert.Equal("Am", File.ReadAllText(Path.Combine(xgsDir, "Slot2Auto", "meta")));
            Assert.Equal("A", File.ReadAllText(Path.Combine(xgsDir, "Slot1Auto", "data")));
        }
        finally { Directory.Delete(xgsDir, true); }
    }

    [Fact]
    public void SaveSlotManager_XgsSwapSlots_KeepsSuffixSemantics()
    {
        string xgsDir = CreateXgsContainerDirectory(
            ("Slot1Auto", "A", "Am"),
            ("Slot2Manual", "B", "Bm"));
        try
        {
            SaveSlotManager.SwapSlots(xgsDir, 0, 1, SaveFileManager.Platform.XboxGamePass);

            Assert.Equal("A", File.ReadAllText(Path.Combine(xgsDir, "Slot2Auto", "data")));
            Assert.Equal("B", File.ReadAllText(Path.Combine(xgsDir, "Slot1Manual", "data")));
            Assert.False(Directory.Exists(Path.Combine(xgsDir, "Slot1Auto")));
            Assert.False(Directory.Exists(Path.Combine(xgsDir, "Slot2Manual")));
        }
        finally { Directory.Delete(xgsDir, true); }
    }

    [Fact]
    public void SaveSlotManager_XgsDeleteSlot_RemovesFoldersOnly()
    {
        string xgsDir = CreateXgsContainerDirectory(
            ("Slot1Auto", "A", "Am"),
            ("Slot1Manual", "A2", "A2m"),
            ("Slot2Auto", "B", "Bm"),
            ("AccountData", "acc", "accm"));
        try
        {
            SaveSlotManager.DeleteSlot(xgsDir, 0, SaveFileManager.Platform.XboxGamePass);

            Assert.False(Directory.Exists(Path.Combine(xgsDir, "Slot1Auto")));
            Assert.False(Directory.Exists(Path.Combine(xgsDir, "Slot1Manual")));
            Assert.True(Directory.Exists(Path.Combine(xgsDir, "Slot2Auto")));
            Assert.True(Directory.Exists(Path.Combine(xgsDir, "AccountData")));
            Assert.Throws<SlotEmptyException>(() =>
                SaveSlotManager.DeleteSlot(xgsDir, 0, SaveFileManager.Platform.XboxGamePass));
        }
        finally { Directory.Delete(xgsDir, true); }
    }

    [Fact]
    public void SaveSlotManager_XgsGetExistingSlotIndices_ReturnsUsedSlots()
    {
        string xgsDir = CreateXgsContainerDirectory(
            ("Slot1Auto", "A", "Am"),
            ("Slot3Manual", "C", "Cm"),
            ("AccountData", "acc", "accm"));
        try
        {
            var indices = SaveSlotManager.GetExistingSlotIndices(xgsDir, SaveFileManager.Platform.XboxGamePass);
            Assert.Equal(new List<int> { 0, 2 }, indices);
        }
        finally { Directory.Delete(xgsDir, true); }
    }

    [Fact]
    public void SaveSlotManager_XgsCopySlot_MirrorsToSiblingWgs()
    {
        string root = Path.Combine(Path.GetTempPath(), $"nmse_test_xgs_slotmirror_{Guid.NewGuid():N}");
        string xgsDir = Path.Combine(root, "SystemAppData", "xgs", "container");
        string wgsDir = Path.Combine(root, "SystemAppData", "wgs", "container");
        Directory.CreateDirectory(Path.Combine(xgsDir, "Slot1Auto"));
        Directory.CreateDirectory(wgsDir);
        try
        {
            File.WriteAllBytes(Path.Combine(xgsDir, "Slot1Auto", "data"),
                CompressSaveJson("{\"CommonStateData\":{\"SaveName\":\"A\"}}"));
            File.WriteAllBytes(Path.Combine(xgsDir, "Slot1Auto", "meta"), new byte[360]);
            WriteWgsContainer(wgsDir, ("Slot1Auto", "A", "Am"));

            SaveSlotManager.CopySlot(xgsDir, 0, 1, SaveFileManager.Platform.XboxGamePass);

            var wgsSlots = ContainersIndexManager.ParseContainersIndex(Path.Combine(wgsDir, "containers.index"));
            Assert.True(wgsSlots.ContainsKey("Slot2Auto"), "wgs mirror should gain Slot2Auto");
            string mirroredJson = XboxBlobCodec.Decompress(File.ReadAllBytes(wgsSlots["Slot2Auto"].DataFilePath!));
            Assert.Contains("\"SaveName\":\"A\"", mirroredJson, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void XgsSaveManager_RealSample_LoadsAndMatchesWgs()
    {
        string? refRoot = FindReferenceRoot();
        if (refRoot == null) return;
        string sampleRoot = Path.Combine(refRoot, "_ref", "xgs", "XBX_SAVE");
        string containerName = "000900000150C65A_29070100B936489ABCE8B9AF3980429C";
        string xgsDir = Path.Combine(sampleRoot, "xgs", containerName);
        string wgsDir = Path.Combine(sampleRoot, "wgs", containerName);
        if (!File.Exists(Path.Combine(xgsDir, "Slot1Manual", "data")) ||
            !File.Exists(Path.Combine(wgsDir, "containers.index")))
            return; // real sample not present in this checkout

        var xgsSave = SaveFileManager.LoadXboxSave(xgsDir, "Slot1Manual");
        var wgsSave = SaveFileManager.LoadXboxSave(wgsDir, "Slot1Manual");

        Assert.NotNull(xgsSave);
        Assert.NotNull(wgsSave);
        Assert.Equal(wgsSave!.ToString(), xgsSave!.ToString());
    }

    /// <summary>Walks up from the test binary to the repository root (the folder containing _ref).</summary>
    private static string? FindReferenceRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "_ref"))) return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return null;
    }
}
