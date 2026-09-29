using NMSE.Models;

namespace NMSE.IO;

/// <summary>
/// File paths for a save slot.
/// </summary>
public class SlotFiles
{
    /// <summary>Gets or sets the path to the save data file.</summary>
    public string? DataFile { get; set; }
    /// <summary>Gets or sets the path to the companion metadata file.</summary>
    public string? MetaFile { get; set; }
}

/// <summary>
/// Options for cross-platform save transfer.
/// Controls which ownership references to rewrite and the destination user identity.
/// </summary>
public class TransferOptions
{
    /// <summary>Source user UID to match (leave empty to transfer all).</summary>
    public string? SourceUID { get; set; }
    /// <summary>Destination user UID.</summary>
    public string? DestUID { get; set; }
    /// <summary>Destination user LID (lobby ID).</summary>
    public string? DestLID { get; set; }
    /// <summary>Destination user USN (username).</summary>
    public string? DestUSN { get; set; }
    /// <summary>Destination platform token (PC, XBX, PS4, NX).</summary>
    public string? DestPTK { get; set; }
    /// <summary>Transfer base ownership references.</summary>
    public bool TransferBases { get; set; } = true;
    /// <summary>Transfer discovery ownership references.</summary>
    public bool TransferDiscoveries { get; set; } = true;
    /// <summary>Transfer settlement ownership references.</summary>
    public bool TransferSettlements { get; set; } = true;
    /// <summary>Transfer ByteBeat song authorship.</summary>
    public bool TransferByteBeat { get; set; } = true;
}

/// <summary>
/// Thrown when a save slot operation targets a slot that contains no save data.
/// </summary>
public sealed class SlotEmptyException : Exception
{
    /// <summary>Gets the 0-based index of the empty slot.</summary>
    public int SlotIndex { get; }

    /// <summary>Gets the 1-based slot number as shown in the user interface.</summary>
    public int SlotNumber => SlotIndex + 1;

    /// <summary>Initialises a new instance for the given 0-based slot index.</summary>
    /// <param name="slotIndex">0-based index of the empty slot.</param>
    public SlotEmptyException(int slotIndex)
        : base($"Slot {slotIndex + 1} is empty.")
    {
        SlotIndex = slotIndex;
    }
}

/// <summary>
/// Thrown when a save slot operation is not supported for the detected save format.
/// </summary>
public sealed class SlotOperationUnsupportedException : Exception
{
    /// <summary>Gets the detected platform.</summary>
    public SaveFileManager.Platform Platform { get; }

    /// <summary>Gets whether the save directory is a PS4 memory.dat save.</summary>
    public bool IsMemoryDat { get; }

    /// <summary>Initialises a new instance for the given platform.</summary>
    /// <param name="platform">The detected platform.</param>
    /// <param name="isMemoryDat">True for PS4 memory.dat saves.</param>
    public SlotOperationUnsupportedException(SaveFileManager.Platform platform, bool isMemoryDat = false)
        : base(isMemoryDat
            ? "Save slot operations are not supported for PS4 memory.dat saves."
            : $"Save slot operations are not supported for the {platform} platform.")
    {
        Platform = platform;
        IsMemoryDat = isMemoryDat;
    }
}

/// <summary>
/// Save slot operations: copy, move, swap within a platform, and cross-platform transfer.
///
/// Slot copy/move/swap operates within the same save directory.
/// Cross-platform transfer converts ownership UIDs, platform tokens, as well as base,
/// settlement, discovery and ByteBeat author references so saves work correctly on the
/// destination platform.
///
/// Each NMS save "slot" (as shown in the game's UI) contains TWO files: an auto save
/// and a manual save.  The slot index is 0-based (slot 0 = game "Slot 1").
///
/// Steam/GOG file layout (15 slots x 2 files each = 30 files):
///   Slot 0: save.hg   (auto)  + save2.hg   (manual)
///   Slot 1: save3.hg  (auto)  + save4.hg   (manual)
///   Slot N: save(2N+1).hg     + save(2N+2).hg   (with special case: slot 0 auto = save.hg)
///
/// Switch / PS4 streaming file layout (15 slots x 2 files each = 30 files):
///   Slot 0: savedata02.hg (auto) + savedata03.hg (manual)
///   Slot 1: savedata04.hg (auto) + savedata05.hg (manual)
///   Slot N: savedata(2N+2).hg    + savedata(2N+3).hg
///   (savedata00.hg is the settings file and account data lives in accountdata.hg)
///
/// Xbox Game Pass saves live in containers.index as "SlotNAuto"/"SlotNManual" entries
/// that point at GUID-named blob directories rather than plain files, so those slots
/// are handled through the containers.index helpers below.
/// </summary>
public static class SaveSlotManager
{
    /// <summary>Number of game save slots offered by NMS and the editor UI.</summary>
    public const int MaxGameSlots = 15;

    private const string XboxContainersIndexName = "containers.index";
    // Helpers

    /// <summary>
    /// Get the token for a given platform.
    /// </summary>
    private static string GetPlatformToken(SaveFileManager.Platform platform) => platform switch
    {
        SaveFileManager.Platform.Steam => "PC",
        SaveFileManager.Platform.GOG => "PC",
        SaveFileManager.Platform.XboxGamePass => "XBX",
        SaveFileManager.Platform.PS4 => "PS4",
        SaveFileManager.Platform.Switch => "NX",
        _ => "PC",
    };

    /// <summary>
    /// Returns the 0-based indices of all game slots that contain save data in the given
    /// directory. Used by the UI to offer only existing slots as copy/move/swap/delete sources.
    /// </summary>
    /// <param name="saveDirectory">Path to the save directory.</param>
    /// <param name="platform">Platform type for the saves.</param>
    /// <returns>Sorted list of 0-based slot indices; empty when none exist or the format is unsupported.</returns>
    public static List<int> GetExistingSlotIndices(string saveDirectory, SaveFileManager.Platform platform)
    {
        var indices = new List<int>();

        switch (platform)
        {
            case SaveFileManager.Platform.XboxGamePass:
                {
                    foreach (var slot in SaveFileManager.EnumerateXboxSlots(saveDirectory).Values)
                    {
                        if (!ContainersIndexManager.IsSaveSlot(slot.Identifier)) continue;

                        int number = ContainersIndexManager.ExtractSlotNumber(slot.Identifier);
                        if (number < 1 || number > MaxGameSlots) continue;
                        if (!indices.Contains(number - 1)) indices.Add(number - 1);
                    }
                    break;
                }

            case SaveFileManager.Platform.Steam:
            case SaveFileManager.Platform.GOG:
            case SaveFileManager.Platform.Switch:
            case SaveFileManager.Platform.PS4:
                {
                    if (platform == SaveFileManager.Platform.PS4 &&
                        File.Exists(Path.Combine(saveDirectory, "memory.dat")))
                        break;

                    for (int i = 0; i < MaxGameSlots; i++)
                    {
                        bool exists = GetAllSlotFiles(saveDirectory, i, platform)
                            .Any(f => f.DataFile != null && File.Exists(f.DataFile));
                        if (exists) indices.Add(i);
                    }
                    break;
                }
        }

        indices.Sort();
        return indices;
    }

    /// <summary>
    /// Returns whether copy/move/swap/delete slot operations are supported for the given save format.
    /// </summary>
    /// <param name="saveDirectory">Path to the save directory.</param>
    /// <param name="platform">Platform type for the saves.</param>
    /// <returns>True when slot operations are available for this save format.</returns>
    public static bool IsSlotOperationSupported(string saveDirectory, SaveFileManager.Platform platform)
    {
        try
        {
            EnsureSlotOperationsSupported(saveDirectory, platform);
            return true;
        }
        catch (SlotOperationUnsupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Throws <see cref="SlotOperationUnsupportedException"/> when the platform or save format
    /// does not support copy/move/swap/delete slot operations.
    /// </summary>
    /// <param name="saveDirectory">Path to the save directory.</param>
    /// <param name="platform">Platform type for the saves.</param>
    private static void EnsureSlotOperationsSupported(string saveDirectory, SaveFileManager.Platform platform)
    {
        switch (platform)
        {
            case SaveFileManager.Platform.Steam:
            case SaveFileManager.Platform.GOG:
            case SaveFileManager.Platform.Switch:
            case SaveFileManager.Platform.XboxGamePass:
                return;

            case SaveFileManager.Platform.PS4:
                // PS4 streaming saves (savedata*.hg) support slot operations; the
                // monolithic SaveWizard/Apollo memory.dat container does not.
                if (File.Exists(Path.Combine(saveDirectory, "memory.dat")))
                    throw new SlotOperationUnsupportedException(platform, isMemoryDat: true);
                return;

            default:
                throw new SlotOperationUnsupportedException(platform);
        }
    }

    /// <summary>
    /// Returns both file pairs (auto save and manual save) for a given slot index on a
    /// given platform.  Index 0 is the auto save, index 1 is the manual save.
    /// </summary>
    public static SlotFiles[] GetAllSlotFiles(string saveDirectory, int slotIndex,
        SaveFileManager.Platform platform)
    {
        return platform switch
        {
            SaveFileManager.Platform.Steam or SaveFileManager.Platform.GOG =>
                GetSteamAllSlotFiles(saveDirectory, slotIndex),
            SaveFileManager.Platform.Switch =>
                GetSwitchAllSlotFiles(saveDirectory, slotIndex),
            SaveFileManager.Platform.PS4 =>
                GetPlaystationAllSlotFiles(saveDirectory, slotIndex),
            _ => Array.Empty<SlotFiles>()
        };
    }

    /// <summary>
    /// Get file paths for the primary (manual) save in a slot on a given platform.
    /// Used by <see cref="TransferCrossPlatform"/> to locate the destination file.
    /// </summary>
    public static SlotFiles GetSlotFiles(string saveDirectory, int slotIndex,
        SaveFileManager.Platform platform)
    {
        var all = GetAllSlotFiles(saveDirectory, slotIndex, platform);
        // Return the manual save (index 1) when available; fall back to index 0 or empty.
        return all.Length > 1 ? all[1] : (all.Length == 1 ? all[0] : new SlotFiles());
    }

    // === Steam / GOG ===

    private static SlotFiles[] GetSteamAllSlotFiles(string dir, int slotIndex)
    {
        // Slot N:  auto  = save.hg (N==0) or save{2N+1}.hg (N>0)
        //          manual = save{2N+2}.hg
        string autoDataName   = slotIndex == 0 ? "save.hg" : $"save{slotIndex * 2 + 1}.hg";
        string manualDataName = $"save{slotIndex * 2 + 2}.hg";

        string autoDataPath   = Path.Combine(dir, autoDataName);
        string manualDataPath = Path.Combine(dir, manualDataName);

        return
        [
            new SlotFiles { DataFile = autoDataPath,   MetaFile = MetaFileWriter.GetSteamMetaPath(autoDataPath)   },
            new SlotFiles { DataFile = manualDataPath, MetaFile = MetaFileWriter.GetSteamMetaPath(manualDataPath) },
        ];
    }

    // === Switch ===

    private static SlotFiles[] GetSwitchAllSlotFiles(string dir, int slotIndex)
    {
        // Switch saves use the same layout as PS4 HTOS: savedata00.hg is the settings
        // file (with manifest00.hg) and account data lives in accountdata.hg, so game
        // slots start at savedata02.hg:
        //   Slot 0:  auto   = savedata02.hg  + manifest02.hg
        //            manual = savedata03.hg  + manifest03.hg
        //   Slot N:  auto   = savedata{2N+2:D2}.hg + manifest{2N+2:D2}.hg
        //            manual = savedata{2N+3:D2}.hg + manifest{2N+3:D2}.hg
        int autoIdx   = slotIndex * 2 + 2;
        int manualIdx = slotIndex * 2 + 3;

        return
        [
            new SlotFiles
            {
                DataFile = Path.Combine(dir, $"savedata{autoIdx:D2}.hg"),
                MetaFile = Path.Combine(dir, $"manifest{autoIdx:D2}.hg"),
            },
            new SlotFiles
            {
                DataFile = Path.Combine(dir, $"savedata{manualIdx:D2}.hg"),
                MetaFile = Path.Combine(dir, $"manifest{manualIdx:D2}.hg"),
            },
        ];
    }

    // === PS4 streaming ===

    private static SlotFiles[] GetPlaystationAllSlotFiles(string dir, int slotIndex)
    {
        // PS4 HTOS layout: savedata00.hg is the account data file (not a game slot).
        // Game slot N (0-based) maps to:
        //   auto   = savedata{N*2+2:D2}.hg + manifest{N*2+2:D2}.hg  (N=0 -> savedata02)
        //   manual = savedata{N*2+3:D2}.hg + manifest{N*2+3:D2}.hg  (N=0 -> savedata03)
        int autoIdx   = slotIndex * 2 + 2;
        int manualIdx = slotIndex * 2 + 3;

        return
        [
            new SlotFiles
            {
                DataFile = Path.Combine(dir, $"savedata{autoIdx:D2}.hg"),
                MetaFile = Path.Combine(dir, $"manifest{autoIdx:D2}.hg"),
            },
            new SlotFiles
            {
                DataFile = Path.Combine(dir, $"savedata{manualIdx:D2}.hg"),
                MetaFile = Path.Combine(dir, $"manifest{manualIdx:D2}.hg"),
            },
        ];
    }

    private static void WriteMetaForPlatform(SlotFiles files, JsonObject saveData,
        SaveFileManager.Platform platform, int slotIndex)
    {
        if (files.DataFile == null) return;

        var metaInfo = MetaFileWriter.ExtractMetaInfo(saveData);

        switch (platform)
        {
            case SaveFileManager.Platform.Steam:
            case SaveFileManager.Platform.GOG:
                if (File.Exists(files.DataFile))
                {
                    byte[] compressedData = File.ReadAllBytes(files.DataFile);
                    // Calculate decompressed size from the save data
                    string json = saveData.ToString();
                    uint decompressedSize = (uint)(System.Text.Encoding.GetEncoding(28591).GetByteCount(json) + 1);
                    int storageSlot = StorageSlotFromFileName(files.DataFile);
                    MetaFileWriter.WriteSteamMeta(files.DataFile, compressedData, decompressedSize, metaInfo, storageSlot);
                }
                break;

            case SaveFileManager.Platform.Switch:
                {
                    string json = saveData.ToString();
                    uint decompressedSize = (uint)(System.Text.Encoding.GetEncoding(28591).GetByteCount(json) + 1);
                    // Derive the manifest index from the savedata file name (savedata01.hg -> 1).
                    // Falls back to the raw slotIndex when the file name cannot be parsed.
                    int manifestIdx = ExtractSwitchManifestIndex(files.DataFile);
                    if (manifestIdx < 0) manifestIdx = slotIndex;
                    MetaFileWriter.WriteSwitchMeta(files.DataFile, decompressedSize, metaInfo, manifestIdx);
                    break;
                }

            case SaveFileManager.Platform.PS4:
                {
                    string json = saveData.ToString();
                    uint decompressedSize = (uint)(System.Text.Encoding.GetEncoding(28591).GetByteCount(json) + 1);
                    int manifestIdx = ExtractSwitchManifestIndex(files.DataFile);
                    if (manifestIdx < 0) manifestIdx = slotIndex;
                    MetaFileWriter.WritePlaystationStreamingMeta(files.DataFile, decompressedSize, metaInfo, manifestIdx);
                    break;
                }
        }
    }

    private static JsonArray? GetJsonArray(JsonObject root, string path)
    {
        object? value = root.GetValue(path);
        return value as JsonArray;
    }

    private static void SetJsonValueByPath(JsonObject root, string key, object value)
    {
        root.Set(key, value);
    }

    /// <summary>
    /// Copy all files in a save slot (auto save + manual save) to another slot within the
    /// same save directory.
    /// For Steam/GOG, the companion meta file is re-keyed to the destination storage slot
    /// so that the game can decrypt it correctly.
    /// </summary>
    /// <param name="saveDirectory">Path to the save directory.</param>
    /// <param name="sourceSlotIndex">Source slot index (0-based; 0 = game "Slot 1").</param>
    /// <param name="destSlotIndex">Destination slot index.</param>
    /// <param name="platform">Platform type for the saves.</param>
    public static void CopySlot(string saveDirectory, int sourceSlotIndex, int destSlotIndex,
        SaveFileManager.Platform platform)
    {
        if (sourceSlotIndex == destSlotIndex) return;

        EnsureSlotOperationsSupported(saveDirectory, platform);

        if (platform == SaveFileManager.Platform.XboxGamePass)
        {
            if (SaveFileManager.DetectXboxSaveFormat(saveDirectory) == SaveFileManager.XboxSaveFormat.Xgs)
                CopyXgsSlot(saveDirectory, sourceSlotIndex, destSlotIndex);
            else
                CopyXboxSlot(saveDirectory, sourceSlotIndex, destSlotIndex);
            return;
        }

        var sourcePairs = GetAllSlotFiles(saveDirectory, sourceSlotIndex, platform);
        var destPairs   = GetAllSlotFiles(saveDirectory, destSlotIndex,   platform);

        bool anySourceFound = sourcePairs.Any(f => f.DataFile != null && File.Exists(f.DataFile));
        if (!anySourceFound)
            throw new SlotEmptyException(sourceSlotIndex);

        int count = Math.Min(sourcePairs.Length, destPairs.Length);
        for (int i = 0; i < count; i++)
        {
            var src = sourcePairs[i];
            var dst = destPairs[i];

            if (src.DataFile == null || !File.Exists(src.DataFile))
                continue;

            // Copy data file
            File.Copy(src.DataFile, dst.DataFile!, true);

            // Copy meta file with re-keying for Steam/GOG
            if (src.MetaFile != null && File.Exists(src.MetaFile) && dst.MetaFile != null)
                CopyMetaFile(src.DataFile, src.MetaFile, dst.DataFile!, dst.MetaFile, platform);
        }
    }

    /// <summary>
    /// Move all files in a save slot to another slot (copy then delete source).
    /// </summary>
    public static void MoveSlot(string saveDirectory, int sourceSlotIndex, int destSlotIndex,
        SaveFileManager.Platform platform)
    {
        CopySlot(saveDirectory, sourceSlotIndex, destSlotIndex, platform);
        DeleteSlot(saveDirectory, sourceSlotIndex, platform);
    }

    /// <summary>
    /// Swap all files in two save slots (auto save and manual save for each).
    /// For Steam/GOG, meta files are re-keyed to the swapped destination storage slots.
    /// </summary>
    public static void SwapSlots(string saveDirectory, int slotA, int slotB,
        SaveFileManager.Platform platform)
    {
        if (slotA == slotB) return;

        EnsureSlotOperationsSupported(saveDirectory, platform);

        if (platform == SaveFileManager.Platform.XboxGamePass)
        {
            if (SaveFileManager.DetectXboxSaveFormat(saveDirectory) == SaveFileManager.XboxSaveFormat.Xgs)
                SwapXgsSlots(saveDirectory, slotA, slotB);
            else
                SwapXboxSlots(saveDirectory, slotA, slotB);
            return;
        }

        var filesA = GetAllSlotFiles(saveDirectory, slotA, platform);
        var filesB = GetAllSlotFiles(saveDirectory, slotB, platform);

        bool anyA = filesA.Any(f => f.DataFile != null && File.Exists(f.DataFile));
        bool anyB = filesB.Any(f => f.DataFile != null && File.Exists(f.DataFile));
        if (!anyA && !anyB)
            throw new SlotEmptyException(slotA);

        string tempDir = Path.Combine(Path.GetTempPath(), $"nmse_swap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            int count = Math.Min(filesA.Length, filesB.Length);
            for (int i = 0; i < count; i++)
            {
                var fa = filesA[i];
                var fb = filesB[i];

                // Capture the original file paths before any moves so that
                // StorageSlotFromFileName always operates on the correct filename,
                // even after the underlying files have been relocated.
                string? faDataPath = fa.DataFile;
                string? fbDataPath = fb.DataFile;

                string tmpData = Path.Combine(tempDir, $"data_{i}");
                string tmpMeta = Path.Combine(tempDir, $"meta_{i}");

                // Phase 1: move A's files to temp
                if (faDataPath != null && File.Exists(faDataPath))
                    File.Move(faDataPath, tmpData);
                if (fa.MetaFile != null && File.Exists(fa.MetaFile))
                    File.Move(fa.MetaFile, tmpMeta);

                // Phase 2: move B's files to A's location, re-key B's meta to A's slot
                if (fbDataPath != null && File.Exists(fbDataPath) && faDataPath != null)
                {
                    File.Move(fbDataPath, faDataPath);
                    // Re-key B's meta using the original B path (for srcSlot) -> A path (for dstSlot)
                    if (fb.MetaFile != null && File.Exists(fb.MetaFile) && fa.MetaFile != null)
                        CopyMetaFile(fbDataPath, fb.MetaFile, faDataPath, fa.MetaFile, platform);
                }
                // Delete B's meta separately (it was either copied above, or B had no meta)
                if (fb.MetaFile != null && File.Exists(fb.MetaFile))
                    File.Delete(fb.MetaFile);

                // Phase 3: move A's original files (from temp) to B's location, re-key to B's slot
                if (File.Exists(tmpData) && fbDataPath != null)
                {
                    File.Move(tmpData, fbDataPath);
                    // Re-key A's meta using the original A path (for srcSlot) -> B path (for dstSlot)
                    if (File.Exists(tmpMeta) && fb.MetaFile != null && faDataPath != null)
                        CopyMetaFile(faDataPath, tmpMeta, fbDataPath, fb.MetaFile, platform);
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Delete all files in a save slot (auto save and manual save).
    /// </summary>
    /// <param name="saveDirectory">Path to the save directory.</param>
    /// <param name="slotIndex">0-based slot index.</param>
    /// <param name="platform">Platform type for the saves.</param>
    /// <exception cref="SlotEmptyException">The slot contains no save data.</exception>
    /// <exception cref="SlotOperationUnsupportedException">The save format does not support slot deletion.</exception>
    public static void DeleteSlot(string saveDirectory, int slotIndex,
        SaveFileManager.Platform platform)
    {
        EnsureSlotOperationsSupported(saveDirectory, platform);

        if (platform == SaveFileManager.Platform.XboxGamePass)
        {
            if (SaveFileManager.DetectXboxSaveFormat(saveDirectory) == SaveFileManager.XboxSaveFormat.Xgs)
                DeleteXgsSlot(saveDirectory, slotIndex);
            else
                DeleteXboxSlot(saveDirectory, slotIndex);
            return;
        }

        var slotFiles = GetAllSlotFiles(saveDirectory, slotIndex, platform);
        if (!slotFiles.Any(f => f.DataFile != null && File.Exists(f.DataFile)))
            throw new SlotEmptyException(slotIndex);

        foreach (var files in slotFiles)
        {
            if (files.DataFile != null && File.Exists(files.DataFile))
                File.Delete(files.DataFile);
            if (files.MetaFile != null && File.Exists(files.MetaFile))
                File.Delete(files.MetaFile);
        }
    }

    // === Xbox Game Pass slot operations ===
    //
    // Xbox slots live in containers.index as "SlotNAuto" / "SlotNManual" entries that
    // point at GUID-named blob directories, so file-based copy/move/swap/delete does not
    // apply.  The helpers below read and rewrite the index and reuse
    // ContainersIndexManager.WriteXboxSave for the blob data, copying the compressed
    // blobs byte-for-byte so the save payload is never re-encoded.

    /// <summary>
    /// Copies all entries of an Xbox slot (auto and manual) into the destination slot,
    /// creating destination entries when they do not exist yet.
    /// </summary>
    private static void CopyXboxSlot(string saveDirectory, int sourceSlotIndex, int destSlotIndex)
    {
        string indexPath = Path.Combine(saveDirectory, XboxContainersIndexName);
        if (!File.Exists(indexPath))
            throw new FileNotFoundException("Xbox save directory does not contain containers.index.", indexPath);

        var index = ContainersIndexManager.ParseContainersIndexFull(indexPath);
        var sourceEntries = GetXboxSlotEntries(index, sourceSlotIndex);
        if (sourceEntries.Count == 0)
            throw new SlotEmptyException(sourceSlotIndex);

        foreach (var source in sourceEntries)
        {
            string destIdentifier = BuildXboxDestinationIdentifier(source.Identifier, destSlotIndex);
            if (!index.Slots.TryGetValue(destIdentifier, out var dest))
            {
                dest = CreateXboxSlotEntry(saveDirectory, destIdentifier);
                index.Slots[destIdentifier] = dest;
            }

            CopyXboxBlobs(source, dest);
        }

        WriteXboxIndex(indexPath, index);
    }

    /// <summary>
    /// Swaps all entries of two Xbox slots.
    /// </summary>
    private static void SwapXboxSlots(string saveDirectory, int slotA, int slotB)
    {
        string indexPath = Path.Combine(saveDirectory, XboxContainersIndexName);
        if (!File.Exists(indexPath))
            throw new FileNotFoundException("Xbox save directory does not contain containers.index.", indexPath);

        var index = ContainersIndexManager.ParseContainersIndexFull(indexPath);
        var entriesA = GetXboxSlotEntries(index, slotA);
        var entriesB = GetXboxSlotEntries(index, slotB);
        if (entriesA.Count == 0 && entriesB.Count == 0)
            throw new SlotEmptyException(slotA);

        var payloadA = entriesA.Select(ReadXboxPayload).ToList();
        var payloadB = entriesB.Select(ReadXboxPayload).ToList();

        RemoveXboxSlotEntries(index, entriesA);
        RemoveXboxSlotEntries(index, entriesB);

        WriteXboxPayloads(saveDirectory, index, slotB, payloadA);
        WriteXboxPayloads(saveDirectory, index, slotA, payloadB);

        WriteXboxIndex(indexPath, index);
    }

    /// <summary>
    /// Deletes all entries and blobs of an Xbox slot.
    /// </summary>
    private static void DeleteXboxSlot(string saveDirectory, int slotIndex)
    {
        string indexPath = Path.Combine(saveDirectory, XboxContainersIndexName);
        if (!File.Exists(indexPath))
            throw new FileNotFoundException("Xbox save directory does not contain containers.index.", indexPath);

        var index = ContainersIndexManager.ParseContainersIndexFull(indexPath);
        var entries = GetXboxSlotEntries(index, slotIndex);
        if (entries.Count == 0)
            throw new SlotEmptyException(slotIndex);

        RemoveXboxSlotEntries(index, entries);
        WriteXboxIndex(indexPath, index);
    }

    /// <summary>
    /// Returns the containers.index entries belonging to a 0-based game slot.
    /// </summary>
    private static List<XboxSlotInfo> GetXboxSlotEntries(ContainersIndexData index, int slotIndex)
    {
        int slotNumber = slotIndex + 1;
        return index.Slots.Values
            .Where(s => ContainersIndexManager.IsSaveSlot(s.Identifier) &&
                        ContainersIndexManager.ExtractSlotNumber(s.Identifier) == slotNumber)
            .ToList();
    }

    /// <summary>
    /// Builds the destination identifier for a copied Xbox entry, keeping the
    /// Auto/Manual suffix of the source ("Slot1Auto" -> "Slot3Auto").
    /// </summary>
    private static string BuildXboxDestinationIdentifier(string sourceIdentifier, int destSlotIndex)
    {
        int sourceNumber = ContainersIndexManager.ExtractSlotNumber(sourceIdentifier);
        string suffix = sourceIdentifier.Substring(4 + sourceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        return $"Slot{destSlotIndex + 1}{suffix}";
    }

    /// <summary>
    /// Creates a new Xbox slot entry with its own blob directory. Sync GUIDs are not
    /// copied from the source: a new slot must not reuse another slot's cloud identity.
    /// </summary>
    private static XboxSlotInfo CreateXboxSlotEntry(string saveDirectory, string identifier)
    {
        Guid directoryGuid = Guid.NewGuid();
        return new XboxSlotInfo
        {
            Identifier = identifier,
            SyncTime = "",
            BlobContainerExtension = 0,
            SyncState = 0,
            DirectoryGuid = directoryGuid,
            BlobDirectoryPath = Path.Combine(saveDirectory, directoryGuid.ToString("N").ToUpperInvariant()),
            LastModified = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Reads the raw compressed data and meta blob bytes of an Xbox slot entry.
    /// </summary>
    private static (string Suffix, byte[] Data, byte[] Meta) ReadXboxPayload(XboxSlotInfo entry)
    {
        if (entry.DataFilePath == null || !File.Exists(entry.DataFilePath))
            throw new FileNotFoundException($"Xbox save slot '{entry.Identifier}' has no data blob.");

        byte[] data = File.ReadAllBytes(entry.DataFilePath);
        byte[] meta = entry.MetaFilePath != null && File.Exists(entry.MetaFilePath)
            ? File.ReadAllBytes(entry.MetaFilePath)
            : new byte[24];

        int number = ContainersIndexManager.ExtractSlotNumber(entry.Identifier);
        string suffix = entry.Identifier.Substring(4 + number.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        return (suffix, data, meta);
    }

    /// <summary>
    /// Copies the blob bytes of an Xbox slot entry into a destination entry (existing or new).
    /// </summary>
    private static void CopyXboxBlobs(XboxSlotInfo source, XboxSlotInfo dest)
    {
        var payload = ReadXboxPayload(source);
        ContainersIndexManager.WriteXboxSave(dest, payload.Data, payload.Meta);
        dest.LastModified = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Writes retained slot payloads into a 0-based slot, creating entries as needed.
    /// </summary>
    private static void WriteXboxPayloads(string saveDirectory, ContainersIndexData index, int slotIndex,
        IEnumerable<(string Suffix, byte[] Data, byte[] Meta)> payloads)
    {
        foreach (var (suffix, data, meta) in payloads)
        {
            string identifier = $"Slot{slotIndex + 1}{suffix}";
            if (!index.Slots.TryGetValue(identifier, out var entry))
            {
                entry = CreateXboxSlotEntry(saveDirectory, identifier);
                index.Slots[identifier] = entry;
            }

            ContainersIndexManager.WriteXboxSave(entry, data, meta);
            entry.LastModified = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Removes Xbox slot entries from the index and deletes their blob directories when
    /// no remaining entry references the same directory.
    /// </summary>
    private static void RemoveXboxSlotEntries(ContainersIndexData index, IEnumerable<XboxSlotInfo> entries)
    {
        foreach (var entry in entries.ToList())
        {
            index.Slots.Remove(entry.Identifier);

            bool stillReferenced = index.Slots.Values.Any(s => s.DirectoryGuid == entry.DirectoryGuid);
            if (stillReferenced || string.IsNullOrEmpty(entry.BlobDirectoryPath))
                continue;

            try
            {
                if (Directory.Exists(entry.BlobDirectoryPath))
                    Directory.Delete(entry.BlobDirectoryPath, true);
            }
            catch
            {
                // A locked or already removed blob directory must not fail the operation
            }
        }
    }

    /// <summary>
    /// Rewrites a containers.index file after slot entries have changed.
    /// </summary>
    private static void WriteXboxIndex(string indexPath, ContainersIndexData index)
    {
        ContainersIndexManager.WriteContainersIndex(indexPath, index.Slots.Values,
            index.ProcessIdentifier, index.AccountGuid, DateTimeOffset.UtcNow);
    }

    // === Xbox Game Pass (xgs / XGameSaveFiles) slot operations ===
    //
    // xgs slots are plain identifier folders (SlotNAuto / SlotNManual) containing data and
    // meta files, so copy/move/swap/delete operate on folders and copy payloads
    // byte-for-byte.  Each operation is mirrored to the sibling legacy wgs container when
    // one exists so both copies stay in step.

    /// <summary>
    /// Copies an xgs slot by duplicating its identifier folders, creating destination
    /// folders when they do not exist yet.
    /// </summary>
    private static void CopyXgsSlot(string containerDirectory, int sourceSlotIndex, int destSlotIndex)
    {
        var sourceEntries = GetXgsSlotEntries(containerDirectory, sourceSlotIndex);
        if (sourceEntries.Count == 0)
            throw new SlotEmptyException(sourceSlotIndex);

        foreach (var source in sourceEntries)
        {
            string destIdentifier = BuildXboxDestinationIdentifier(source.Identifier, destSlotIndex);
            string destDirectory = XgsSaveManager.GetSlotDirectory(containerDirectory, destIdentifier);
            CopyXgsFiles(source, destDirectory);
        }

        MirrorXgsSlotOperation(containerDirectory, wgsDirectory =>
        {
            var index = ParseWgsIndex(wgsDirectory);
            foreach (var source in sourceEntries)
            {
                var payload = ReadXboxPayload(source);
                string destIdentifier = BuildXboxDestinationIdentifier(source.Identifier, destSlotIndex);
                if (!index.Slots.TryGetValue(destIdentifier, out var dest))
                {
                    dest = CreateXboxSlotEntry(wgsDirectory, destIdentifier);
                    index.Slots[destIdentifier] = dest;
                }

                ContainersIndexManager.WriteXboxSave(dest, payload.Data, payload.Meta);
                dest.LastModified = DateTimeOffset.UtcNow;
            }

            WriteXboxIndex(Path.Combine(wgsDirectory, XboxContainersIndexName), index);
        });
    }

    /// <summary>
    /// Swaps all entries of two xgs slots, keeping each payload's Auto/Manual suffix and
    /// moving it under the other slot number (matching the wgs swap semantics).
    /// </summary>
    private static void SwapXgsSlots(string containerDirectory, int slotA, int slotB)
    {
        var entriesA = GetXgsSlotEntries(containerDirectory, slotA);
        var entriesB = GetXgsSlotEntries(containerDirectory, slotB);
        if (entriesA.Count == 0 && entriesB.Count == 0)
            throw new SlotEmptyException(slotA);

        var payloadA = entriesA.Select(ReadXboxPayload).ToList();
        var payloadB = entriesB.Select(ReadXboxPayload).ToList();

        RemoveXgsSlotEntries(entriesA);
        RemoveXgsSlotEntries(entriesB);

        WriteXgsPayloads(containerDirectory, slotB, payloadA);
        WriteXgsPayloads(containerDirectory, slotA, payloadB);

        MirrorXgsSlotOperation(containerDirectory, wgsDirectory =>
        {
            var index = ParseWgsIndex(wgsDirectory);
            RemoveXboxSlotEntries(index, GetXboxSlotEntries(index, slotA));
            RemoveXboxSlotEntries(index, GetXboxSlotEntries(index, slotB));
            WriteXboxPayloads(wgsDirectory, index, slotB, payloadA);
            WriteXboxPayloads(wgsDirectory, index, slotA, payloadB);
            WriteXboxIndex(Path.Combine(wgsDirectory, XboxContainersIndexName), index);
        });
    }

    /// <summary>
    /// Deletes all identifier folders of an xgs slot.
    /// </summary>
    private static void DeleteXgsSlot(string containerDirectory, int slotIndex)
    {
        var entries = GetXgsSlotEntries(containerDirectory, slotIndex);
        if (entries.Count == 0)
            throw new SlotEmptyException(slotIndex);

        RemoveXgsSlotEntries(entries);

        MirrorXgsSlotOperation(containerDirectory, wgsDirectory =>
        {
            var index = ParseWgsIndex(wgsDirectory);
            RemoveXboxSlotEntries(index, GetXboxSlotEntries(index, slotIndex));
            WriteXboxIndex(Path.Combine(wgsDirectory, XboxContainersIndexName), index);
        });
    }

    /// <summary>
    /// Returns the xgs entry folders belonging to a 0-based game slot.
    /// </summary>
    private static List<XboxSlotInfo> GetXgsSlotEntries(string containerDirectory, int slotIndex)
    {
        int slotNumber = slotIndex + 1;
        return XgsSaveManager.EnumerateSlots(containerDirectory).Values
            .Where(s => ContainersIndexManager.IsSaveSlot(s.Identifier) &&
                        ContainersIndexManager.ExtractSlotNumber(s.Identifier) == slotNumber)
            .ToList();
    }

    /// <summary>
    /// Copies the data and meta files of an xgs entry into a destination folder.
    /// </summary>
    private static void CopyXgsFiles(XboxSlotInfo source, string destDirectory)
    {
        if (source.DataFilePath == null || !File.Exists(source.DataFilePath))
            throw new FileNotFoundException($"xgs save slot '{source.Identifier}' has no data file.");

        Directory.CreateDirectory(destDirectory);
        File.Copy(source.DataFilePath, Path.Combine(destDirectory, XgsSaveManager.DataFileName), overwrite: true);

        if (source.MetaFilePath != null && File.Exists(source.MetaFilePath))
            File.Copy(source.MetaFilePath, Path.Combine(destDirectory, XgsSaveManager.MetaFileName), overwrite: true);
    }

    /// <summary>
    /// Deletes the identifier folders of the given xgs entries.
    /// </summary>
    private static void RemoveXgsSlotEntries(IEnumerable<XboxSlotInfo> entries)
    {
        foreach (var entry in entries)
        {
            try
            {
                if (!string.IsNullOrEmpty(entry.BlobDirectoryPath) && Directory.Exists(entry.BlobDirectoryPath))
                    Directory.Delete(entry.BlobDirectoryPath, true);
            }
            catch
            {
                // A locked or already removed slot folder must not fail the operation
            }
        }
    }

    /// <summary>
    /// Writes retained xgs payloads into a 0-based slot, keeping their Auto/Manual suffix.
    /// </summary>
    private static void WriteXgsPayloads(string containerDirectory, int slotIndex,
        IEnumerable<(string Suffix, byte[] Data, byte[] Meta)> payloads)
    {
        foreach (var (suffix, data, meta) in payloads)
        {
            string identifier = $"Slot{slotIndex + 1}{suffix}";
            string destDirectory = XgsSaveManager.GetSlotDirectory(containerDirectory, identifier);
            Directory.CreateDirectory(destDirectory);
            File.WriteAllBytes(Path.Combine(destDirectory, XgsSaveManager.DataFileName), data);
            File.WriteAllBytes(Path.Combine(destDirectory, XgsSaveManager.MetaFileName), meta);
        }
    }

    /// <summary>
    /// Parses the containers.index of a legacy wgs container, throwing when absent.
    /// </summary>
    private static ContainersIndexData ParseWgsIndex(string wgsDirectory)
    {
        string indexPath = Path.Combine(wgsDirectory, XboxContainersIndexName);
        if (!File.Exists(indexPath))
            throw new FileNotFoundException("Xbox save directory does not contain containers.index.", indexPath);

        return ContainersIndexManager.ParseContainersIndexFull(indexPath);
    }

    /// <summary>
    /// Mirrors an xgs slot operation to the sibling wgs container (best effort: the game
    /// rebuilds wgs from xgs at exit, so a mirror failure must not fail the xgs operation).
    /// </summary>
    private static void MirrorXgsSlotOperation(string xgsContainerDirectory, Action<string> wgsOperation)
    {
        string? wgsDirectory = SaveFileManager.FindSiblingWgsDirectory(xgsContainerDirectory);
        if (wgsDirectory == null)
            return;

        try
        {
            wgsOperation(wgsDirectory);
        }
        catch
        {
            // Best effort only
        }
    }

    // === Meta file helpers ===

    /// <summary>
    /// Copy a meta file from source to destination.
    /// For Steam/GOG the meta is re-encrypted with the destination storage slot key.
    /// For Switch the slot-index field at offset 12 is updated.
    /// For other platforms the file is copied verbatim.
    /// </summary>
    private static void CopyMetaFile(
        string srcDataFile, string srcMetaFile,
        string dstDataFile, string dstMetaFile,
        SaveFileManager.Platform platform)
    {
        if (platform is SaveFileManager.Platform.Steam or SaveFileManager.Platform.GOG)
        {
            ReKeyMetaFile(srcDataFile, srcMetaFile, dstDataFile, dstMetaFile, platform);
        }
        else if (platform == SaveFileManager.Platform.Switch)
        {
            // Copy then patch the slot-index field at byte offset 12.
            byte[] bytes = File.ReadAllBytes(srcMetaFile);
            int dstIdx = ExtractSwitchManifestIndex(dstDataFile);
            if (dstIdx >= 0 && bytes.Length >= 16)
            {
                byte[] idx = BitConverter.GetBytes(dstIdx);
                Buffer.BlockCopy(idx, 0, bytes, 12, 4);
            }
            File.WriteAllBytes(dstMetaFile, bytes);
        }
        else
        {
            File.Copy(srcMetaFile, dstMetaFile, true);
        }
    }

    /// <summary>
    /// Decrypt a Steam/GOG meta file with the source storage slot key and
    /// re-encrypt it with the destination storage slot key, writing the result
    /// to <paramref name="dstMetaFile"/>.
    /// Falls back to a plain file copy if decryption fails.
    /// </summary>
    private static void ReKeyMetaFile(
        string srcDataFile, string srcMetaFile,
        string dstDataFile, string dstMetaFile,
        SaveFileManager.Platform platform)
    {
        if (platform is not (SaveFileManager.Platform.Steam or SaveFileManager.Platform.GOG))
        {
            File.Copy(srcMetaFile, dstMetaFile, true);
            return;
        }

        int srcSlot = StorageSlotFromFileName(srcDataFile);
        int dstSlot = StorageSlotFromFileName(dstDataFile);

        if (srcSlot == dstSlot)
        {
            File.Copy(srcMetaFile, dstMetaFile, true);
            return;
        }

        byte[] raw = File.ReadAllBytes(srcMetaFile);
        if (raw.Length < 4)
        {
            File.Copy(srcMetaFile, dstMetaFile, true);
            return;
        }

        uint[] encrypted  = MetaFileWriter.BytesToUInts(raw);
        int    iterations = raw.Length == MetaFileWriter.STEAM_META_LENGTH_VANILLA ? 8 : 6;
        uint[] decrypted  = MetaCrypto.Decrypt(encrypted, srcSlot, iterations);

        if (decrypted[0] != MetaFileWriter.META_HEADER)
        {
            // Decryption failed - copy verbatim as a best-effort fallback
            File.Copy(srcMetaFile, dstMetaFile, true);
            return;
        }

        uint[] reKeyed = MetaCrypto.Encrypt(decrypted, dstSlot, iterations);
        File.WriteAllBytes(dstMetaFile, MetaFileWriter.UIntsToBytes(reKeyed));
    }

    /// <summary>
    /// Derive the Switch manifest index from a savedata file path.
    /// e.g. "savedata03.hg" -> 3, "savedata00.hg" -> 0.
    /// Returns -1 if the name cannot be parsed.
    /// </summary>
    private static int ExtractSwitchManifestIndex(string dataFilePath)
    {
        string name = Path.GetFileNameWithoutExtension(dataFilePath);
        const string prefix = "savedata";
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name.AsSpan(prefix.Length),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int idx))
            return idx;
        return -1;
    }

    /// <summary>
    /// Transfer a save file from one platform to another.
    /// Loads the source JSON, rewrites ownership UIDs for the destination platform,
    /// and saves in the destination format.
    /// </summary>
    /// <param name="sourceFilePath">Source save file path.</param>
    /// <param name="destDirectory">Destination save directory.</param>
    /// <param name="destSlotIndex">Destination slot index.</param>
    /// <param name="destPlatform">Destination platform type.</param>
    /// <param name="transferOptions">Options controlling what data to transfer.</param>
    public static void TransferCrossPlatform(string sourceFilePath, string destDirectory,
        int destSlotIndex, SaveFileManager.Platform destPlatform, TransferOptions? transferOptions = null)
    {
        var options = transferOptions ?? new TransferOptions();

        // Load source save
        var saveData = SaveFileManager.LoadSaveFile(sourceFilePath);

        // Update Platform token in the save
        string platformToken = GetPlatformToken(destPlatform);
        SetJsonValueByPath(saveData, "Platform", platformToken);

        // Transfer ownership references
        if (options.TransferBases)
            TransferBaseOwnership(saveData, options);

        if (options.TransferDiscoveries)
            TransferDiscoveryOwnership(saveData, options);

        if (options.TransferSettlements)
            TransferSettlementOwnership(saveData, options);

        if (options.TransferByteBeat)
            TransferByteBeatOwnership(saveData, options);

        // Save to destination.  Xbox Game Pass saves are GUID-named blobs tracked by
        // containers.index rather than plain *.hg files, so they need dedicated handling.
        if (destPlatform == SaveFileManager.Platform.XboxGamePass)
        {
            SaveToXboxGamePass(destDirectory, destSlotIndex, saveData);
            return;
        }

        var destFiles = GetSlotFiles(destDirectory, destSlotIndex, destPlatform);
        if (destFiles.DataFile == null)
            throw new InvalidOperationException("Cannot determine destination file path.");

        SaveFileManager.SaveToFile(destFiles.DataFile, saveData, compress: true);

        // Write platform-appropriate meta file
        WriteMetaForPlatform(destFiles, saveData, destPlatform, destSlotIndex);
    }

    /// <summary>
    /// Saves the given data to an Xbox Game Pass destination directory (xgs container or
    /// legacy containers.index folder). The destination slot is located by its identifier
    /// rather than a file name, so both formats share this path.
    /// Writes to the manual save entry when present, falling back to the auto save.
    /// </summary>
    /// <param name="destDirectory">Path to the Xbox save directory (xgs container or containers.index folder).</param>
    /// <param name="destSlotIndex">Destination slot index (0-based; 0 = game "Slot 1").</param>
    /// <param name="saveData">The save data to write.</param>
    private static void SaveToXboxGamePass(string destDirectory, int destSlotIndex, JsonObject saveData)
    {
        if (SaveFileManager.DetectXboxSaveFormat(destDirectory) == SaveFileManager.XboxSaveFormat.None)
            throw new InvalidOperationException("Cannot determine destination file path: the Xbox directory is not a valid save container.");

        int targetSlotNumber = destSlotIndex + 1;
        string? identifier = null;
        foreach (var (slotId, _) in SaveFileManager.EnumerateXboxSlots(destDirectory))
        {
            if (!ContainersIndexManager.IsSaveSlot(slotId))
                continue;
            if (ContainersIndexManager.ExtractSlotNumber(slotId) != targetSlotNumber)
                continue;

            identifier = slotId;
            // Prefer the manual save entry, matching the Steam/GOG behaviour of
            // writing to the manual save file (all[1]).
            if (slotId.Contains("Manual", StringComparison.OrdinalIgnoreCase))
                break;
        }

        if (identifier == null)
            throw new InvalidOperationException($"Cannot determine destination file path: slot {targetSlotNumber} not found in the Xbox save container.");

        SaveFileManager.SaveXboxSave(destDirectory, identifier, saveData);
    }

    /// <summary>
    /// Rewrite ownership UIDs in base objects.
    /// Bases have Owner.UID, Owner.LID, Owner.USN, Owner.PTK fields that need
    /// to match the destination platform user.
    /// </summary>
    private static void TransferBaseOwnership(JsonObject saveData, TransferOptions options)
    {
        // Walk PersistentPlayerBases array
        var bases = GetJsonArray(saveData, "PlayerStateData.PersistentPlayerBases");
        if (bases == null) return;

        for (int i = 0; i < bases.Length; i++)
        {
            if (bases.Get(i) is not JsonObject baseObj) continue;

            var owner = baseObj.Get("Owner") as JsonObject;
            if (owner == null) continue;

            // Only transfer bases owned by the source user (match UID)
            string? ownerUid = owner.Get("UID") as string;
            if (!string.IsNullOrEmpty(options.SourceUID) && ownerUid != options.SourceUID)
                continue;

            // Rewrite ownership
            RewriteOwnership(owner, options);
        }
    }

    private static void TransferDiscoveryOwnership(JsonObject saveData, TransferOptions options)
    {
        // Walk DiscoveryManagerData.DiscoveryData-v1.Store.Record array
        var record = GetJsonArray(saveData, "DiscoveryManagerData.DiscoveryData-v1.Store.Record");
        if (record == null) return;

        for (int i = 0; i < record.Length; i++)
        {
            if (record.Get(i) is not JsonObject discoveryObj) continue;

            var ows = discoveryObj.Get("OWS") as JsonObject;
            if (ows == null) continue;

            string? ownerUid = ows.Get("UID") as string;
            if (!string.IsNullOrEmpty(options.SourceUID) && ownerUid != options.SourceUID)
                continue;

            RewriteOwnership(ows, options);
        }
    }

    private static void TransferSettlementOwnership(JsonObject saveData, TransferOptions options)
    {
        // Walk PlayerStateData.SettlementStatesV2 array
        var settlements = GetJsonArray(saveData, "PlayerStateData.SettlementStatesV2");
        if (settlements == null) return;

        for (int i = 0; i < settlements.Length; i++)
        {
            if (settlements.Get(i) is not JsonObject settlementObj) continue;

            var owner = settlementObj.Get("Owner") as JsonObject;
            if (owner == null) continue;

            string? ownerUid = owner.Get("UID") as string;
            if (!string.IsNullOrEmpty(options.SourceUID) && ownerUid != options.SourceUID)
                continue;

            RewriteOwnership(owner, options);
        }
    }

    private static void TransferByteBeatOwnership(JsonObject saveData, TransferOptions options)
    {
        // Walk PlayerStateData.ByteBeatLibrary.MySongs array
        var songs = GetJsonArray(saveData, "PlayerStateData.ByteBeatLibrary.MySongs");
        if (songs == null) return;

        for (int i = 0; i < songs.Length; i++)
        {
            if (songs.Get(i) is not JsonObject songObj) continue;

            string? authorId = songObj.Get("AuthorOnlineID") as string;
            if (!string.IsNullOrEmpty(options.SourceUID) && authorId != options.SourceUID)
                continue;

            if (!string.IsNullOrEmpty(options.DestUID))
                songObj.Set("AuthorOnlineID", options.DestUID);
            if (!string.IsNullOrEmpty(options.DestUSN))
                songObj.Set("AuthorUsername", options.DestUSN);
            if (!string.IsNullOrEmpty(options.DestPTK))
                songObj.Set("AuthorPlatform", options.DestPTK);
        }
    }

    private static void RewriteOwnership(JsonObject ownerObj, TransferOptions options)
    {
        if (!string.IsNullOrEmpty(options.DestUID))
            ownerObj.Set("UID", options.DestUID);

        string? lid = ownerObj.Get("LID") as string;
        if (!string.IsNullOrEmpty(lid) && !string.IsNullOrEmpty(options.DestLID))
            ownerObj.Set("LID", options.DestLID);

        string? usn = ownerObj.Get("USN") as string;
        if (!string.IsNullOrEmpty(usn) && !string.IsNullOrEmpty(options.DestUSN))
            ownerObj.Set("USN", options.DestUSN);

        if (!string.IsNullOrEmpty(options.DestPTK))
            ownerObj.Set("PTK", options.DestPTK);
    }

    /// <summary>
    /// Convert a save slot index to the persistent storage slot used in meta encryption.
    /// Slot 0 = AccountData (storage slot 0), slots 1+ = save data (storage slot 2+slotIndex).
    /// The gap at slot 1 is reserved for Settings data.
    /// </summary>
    internal static int SlotIndexToStorageSlot(int slotIndex)
    {
        return slotIndex == 0 ? 0 : 2 + slotIndex;
    }

    /// <summary>
    /// Derive the persistent storage slot from a save file name.
    /// NMS uses the following convention:
    /// <list type="bullet">
    ///   <item><c>accountdata.hg</c> -> storage slot 0</item>
    ///   <item><c>save.hg</c> -> storage slot 2 (first manual save)</item>
    ///   <item><c>saveN.hg</c> (N >= 2) -> storage slot N + 1</item>
    /// </list>
    /// The meta encryption key depends on the storage slot, so using the
    /// wrong slot produces a garbled meta file that the game cannot read.
    /// </summary>
    internal static int StorageSlotFromFileName(string filePath)
    {
        string name = Path.GetFileNameWithoutExtension(filePath);

        if (name.Equals("accountdata", StringComparison.OrdinalIgnoreCase))
            return 0;

        // save.hg -> storage slot 2
        if (name.Equals("save", StringComparison.OrdinalIgnoreCase))
            return 2;

        // saveN.hg -> storage slot N + 1  (save2.hg -> 3, save3.hg -> 4, etc.)
        if (name.StartsWith("save", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name.AsSpan(4), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) && n >= 2)
            return n + 1;

        // Unknown file name - fall back to slot 2 as a safe default
        return 2;
    }
}
