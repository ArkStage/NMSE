using System.Text;

namespace NMSE.IO;

/// <summary>
/// Information about a save slot within Xbox containers.index.
/// </summary>
public class XboxSlotInfo
{
    /// <summary>Gets or sets the primary save slot identifier (e.g., "Slot1Auto").</summary>
    public string Identifier { get; set; } = "";
    /// <summary>Gets or sets the secondary identifier, if present.</summary>
    public string? SecondIdentifier { get; set; }
    /// <summary>Gets or sets the synchronization timestamp string.</summary>
    public string? SyncTime { get; set; }
    /// <summary>Gets or sets the blob container file extension number.</summary>
    public byte BlobContainerExtension { get; set; }
    /// <summary>Gets or sets the synchronization state value.</summary>
    public int SyncState { get; set; }
    /// <summary>Gets or sets the GUID identifying the blob directory for this slot.</summary>
    public Guid DirectoryGuid { get; set; }
    /// <summary>Gets or sets the full path to the blob directory on disk.</summary>
    public string BlobDirectoryPath { get; set; } = "";
    /// <summary>Gets or sets the last modified timestamp of the slot.</summary>
    public DateTimeOffset LastModified { get; set; }
    /// <summary>Gets or sets the resolved file path to the save data blob.</summary>
    public string? DataFilePath { get; set; }
    /// <summary>Gets or sets the resolved file path to the metadata blob.</summary>
    public string? MetaFilePath { get; set; }
    /// <summary>Gets or sets the cloud/sync GUID for the data blob (preserved for round-trip fidelity).</summary>
    public Guid? DataSyncGuid { get; set; }
    /// <summary>Gets or sets the cloud/sync GUID for the meta blob (preserved for round-trip fidelity).</summary>
    public Guid? MetaSyncGuid { get; set; }
}

/// <summary>
/// Holds the full parsed contents of a containers.index file, including
/// the global header fields needed to write the file back to disk.
/// </summary>
public class ContainersIndexData
{
    /// <summary>The process identifier string from the global header (e.g., "HelloGames.NoMansSky_...").</summary>
    public string ProcessIdentifier { get; set; } = "";
    /// <summary>The account GUID string from the global header.</summary>
    public string AccountGuid { get; set; } = "";
    /// <summary>The last-write timestamp from the global header.</summary>
    public DateTimeOffset LastWriteTime { get; set; }
    /// <summary>The sync state from the global header.</summary>
    public int SyncState { get; set; }
    /// <summary>All parsed slot entries, keyed by primary identifier.</summary>
    public Dictionary<string, XboxSlotInfo> Slots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Reads and writes Xbox Game Pass / Microsoft Store containers.index files.
///
/// Xbox/Microsoft NMS saves use a containers.index file to map save slot identifiers
/// (e.g., "Slot1Auto", "Slot1Manual", "AccountData", "Settings") to GUID-named
/// blob directories. Each blob directory has a container.N file pointing to the actual
/// data and meta blob files (also GUID-named).
///
/// File hierarchy:
///   containers.index         - global index mapping identifiers to blob directories
///   {GUID}/container.{N}     - blob container pointing to data + meta files
///   {GUID}/{GUID}            - actual data or meta blob file
/// </summary>
public static class ContainersIndexManager
{
    private const int CONTAINERSINDEX_HEADER = 14;
    private const long CONTAINERSINDEX_FOOTER = 268435456; // 0x10000000
    private const int BLOBCONTAINER_HEADER = 4;
    private const int BLOBCONTAINER_COUNT = 2;
    private const int BLOBCONTAINER_IDENTIFIER_LENGTH = 128;
    private const int BLOBCONTAINER_TOTAL_LENGTH = 328;

    /// <summary>Xbox containers.index identifier for the account data blob.</summary>
    public const string AccountDataIdentifier = "AccountData";
    /// <summary>Xbox containers.index identifier for the settings blob.</summary>
    public const string SettingsIdentifier = "Settings";

    /// <summary>
    /// Returns <c>true</c> if the given slot identifier represents an actual save slot
    /// (e.g. "Slot1Auto", "Slot1Manual") rather than a special entry like "AccountData"
    /// or "Settings".
    /// </summary>
    public static bool IsSaveSlot(string identifier)
    {
        return !string.Equals(identifier, AccountDataIdentifier, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(identifier, SettingsIdentifier, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extract the numeric slot number from an Xbox slot identifier.
    /// E.g., "Slot1Auto" -> 1, "Slot2Manual" -> 2, "Slot15Auto" -> 15.
    /// Returns 0 if the identifier does not follow the "Slot{N}..." pattern.
    /// </summary>
    public static int ExtractSlotNumber(string identifier)
    {
        if (!identifier.StartsWith("Slot", StringComparison.OrdinalIgnoreCase) || identifier.Length <= 4)
            return 0;

        int numEnd = 4;
        while (numEnd < identifier.Length && char.IsDigit(identifier[numEnd]))
            numEnd++;

        if (numEnd == 4) return 0;
        return int.TryParse(identifier.AsSpan(4, numEnd - 4),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int num) ? num : 0;
    }

    /// <summary>
    /// Returns <c>true</c> if the slot identifier represents an auto-save slot
    /// (contains "Auto" in the name, e.g. "Slot1Auto").
    /// </summary>
    public static bool IsAutoSave(string identifier)
    {
        return identifier.Contains("Auto", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Check if a directory contains Xbox Game Pass saves.
    /// </summary>
    public static bool IsXboxSaveDirectory(string directory)
    {
        return File.Exists(Path.Combine(directory, "containers.index"));
    }

    /// <summary>
    /// Parse the containers.index file and discover all save slots.
    /// </summary>
    /// <param name="containersIndexPath">Path to containers.index</param>
    /// <returns>Dictionary of save identifier to slot info.</returns>
    public static Dictionary<string, XboxSlotInfo> ParseContainersIndex(string containersIndexPath)
    {
        return ParseContainersIndexFull(containersIndexPath).Slots;
    }

    /// <summary>
    /// Parse the containers.index file and return all data including the global header
    /// fields needed to write the file back (process identifier, account GUID, timestamps).
    /// </summary>
    /// <param name="containersIndexPath">Path to containers.index</param>
    /// <returns>Full parsed contents including header fields and all slot entries.</returns>
    public static ContainersIndexData ParseContainersIndexFull(string containersIndexPath)
    {
        var data = new ContainersIndexData();
        byte[] bytes = File.ReadAllBytes(containersIndexPath);
        string baseDir = Path.GetDirectoryName(containersIndexPath)!;

        if (bytes.Length < 200) return data;

        // Validate header
        int header = ReadInt32LE(bytes, 0);
        if (header != CONTAINERSINDEX_HEADER) return data;

        long containerCount = ReadInt64LE(bytes, 4);

        // Parse global header: header(4) + count(8) + processIdentifierLen(4) + processIdentifier(var) + lastModifiedTime(8) + syncState(4) + accountGuidLen(4) + accountGuid(var) + footer(8)
        int offset = 12;
        offset += ReadDynamicString(bytes, offset, out string processIdentifier);
        data.ProcessIdentifier = processIdentifier;

        long lastWriteFileTime = ReadInt64LE(bytes, offset);
        data.LastWriteTime = DateTimeOffset.FromFileTime(lastWriteFileTime);
        int syncState = ReadInt32LE(bytes, offset + 8);
        data.SyncState = syncState;
        offset += 12; // lastModifiedTime(8) + syncState(4)

        offset += ReadDynamicString(bytes, offset, out string accountGuid);
        data.AccountGuid = accountGuid;
        offset += 8; // footer

        // Parse each blob container entry
        for (int i = 0; i < containerCount && offset < bytes.Length; i++)
        {
            // Read two identifiers
            offset += ReadDynamicString(bytes, offset, out string identifier1);
            offset += ReadDynamicString(bytes, offset, out string identifier2);

            // Read sync time
            offset += ReadDynamicString(bytes, offset, out string syncTime);

            // Read remaining fixed fields
            if (offset + 45 > bytes.Length) break;
            byte blobExtension = bytes[offset]; // 1
            int slotSyncState = ReadInt32LE(bytes, offset + 1); // 4
            byte[] guidBytes = new byte[16];
            Buffer.BlockCopy(bytes, offset + 5, guidBytes, 0, 16);
            Guid directoryGuid = new Guid(guidBytes);
            long lastModified = ReadInt64LE(bytes, offset + 21); // 8
            // skip empty (8) and total size (8)

            offset += 45;

            string blobDirPath = ResolveBlobDirectory(baseDir, directoryGuid);

            var slotInfo = new XboxSlotInfo
            {
                Identifier = identifier1,
                SecondIdentifier = identifier2,
                SyncTime = syncTime,
                BlobContainerExtension = blobExtension,
                SyncState = slotSyncState,
                DirectoryGuid = directoryGuid,
                BlobDirectoryPath = blobDirPath,
                LastModified = DateTimeOffset.FromFileTime(lastModified),
            };

            // Try to parse the blob container to find actual data/meta files
            if (Directory.Exists(blobDirPath))
            {
                ParseBlobContainer(slotInfo);
            }

            data.Slots[identifier1] = slotInfo;
        }

        return data;
    }

    /// <summary>
    /// Load a save file from an Xbox blob file.
    /// Returns the decompressed JSON string, or null if not found.
    /// Supports HGSAVEV2, NMS streaming LZ4 and plain/raw LZ4 payloads
    /// (see <see cref="XboxBlobCodec"/>).
    /// </summary>
    public static string? LoadXboxSave(XboxSlotInfo slotInfo)
    {
        if (slotInfo.DataFilePath == null || !File.Exists(slotInfo.DataFilePath))
            return null;

        try
        {
            byte[] data = File.ReadAllBytes(slotInfo.DataFilePath);
            return XboxBlobCodec.Decompress(data);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Read the meta blob file for an Xbox save slot.
    /// Returns the raw metadata bytes, or null if not found.
    /// </summary>
    public static byte[]? LoadXboxMeta(XboxSlotInfo slotInfo)
    {
        if (slotInfo.MetaFilePath == null || !File.Exists(slotInfo.MetaFilePath))
            return null;

        return File.ReadAllBytes(slotInfo.MetaFilePath);
    }

    /// <summary>
    /// Write save data and meta to an Xbox blob directory.
    /// When the blob already exists its contents are overwritten in place, preserving the
    /// blob GUIDs, the container extension and the sync identity used by Xbox cloud saves.
    /// Only slots with no existing blob create fresh GUID-named files and a container.
    /// </summary>
    public static void WriteXboxSave(XboxSlotInfo slotInfo, byte[] compressedData, byte[] metaData)
    {
        if (!Directory.Exists(slotInfo.BlobDirectoryPath))
            Directory.CreateDirectory(slotInfo.BlobDirectoryPath);

        // Existing blob: replace contents in place to preserve cloud container identity.
        if (slotInfo.DataFilePath != null && File.Exists(slotInfo.DataFilePath))
        {
            WriteAtomic(slotInfo.DataFilePath, compressedData);

            if (metaData.Length > 0 && slotInfo.MetaFilePath != null && File.Exists(slotInfo.MetaFilePath))
            {
                byte[] existingMeta = File.ReadAllBytes(slotInfo.MetaFilePath);
                if (!existingMeta.AsSpan().SequenceEqual(metaData))
                    WriteAtomic(slotInfo.MetaFilePath, metaData);
            }
            return;
        }

        // No existing blob (new slot or copy destination): create fresh GUID-named files.
        WriteXboxSaveNewBlobs(slotInfo, compressedData, metaData);
    }

    /// <summary>
    /// Creates fresh GUID-named data/meta blobs and a new container file for a slot
    /// that has no existing blob on disk.
    /// </summary>
    private static void WriteXboxSaveNewBlobs(XboxSlotInfo slotInfo, byte[] compressedData, byte[] metaData)
    {
        Guid newDataGuid = Guid.NewGuid();
        Guid newMetaGuid = Guid.NewGuid();

        string newDataPath = GetBlobFilePath(slotInfo.BlobDirectoryPath, newDataGuid);
        string newMetaPath = GetBlobFilePath(slotInfo.BlobDirectoryPath, newMetaGuid);

        WriteAtomic(newDataPath, compressedData);
        WriteAtomic(newMetaPath, metaData);

        slotInfo.DataFilePath = newDataPath;
        slotInfo.MetaFilePath = newMetaPath;

        WriteBlobContainer(slotInfo, newDataGuid, newMetaGuid);
    }

    /// <summary>
    /// Writes a file via a temporary sibling and an atomic replace, so an interrupted
    /// write cannot leave a truncated save blob behind.
    /// </summary>
    private static void WriteAtomic(string path, byte[] bytes)
    {
        string tmp = path + ".nmse.tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Write an updated containers.index file.
    /// </summary>
    public static void WriteContainersIndex(string containersIndexPath, IEnumerable<XboxSlotInfo> slots,
        string processIdentifier, string accountGuid, DateTimeOffset lastWriteTime)
    {
        // Estimate buffer size
        var slotList = slots.ToList();
        int estimatedSize = 200 + (slotList.Count * 200);
        byte[] buffer = new byte[estimatedSize];

        using var ms = new MemoryStream(buffer);
        using var writer = new BinaryWriter(ms);

        writer.Write(CONTAINERSINDEX_HEADER);
        writer.Write((long)slotList.Count);
        WriteDynamicString(writer, processIdentifier);
        writer.Write(lastWriteTime.ToUniversalTime().ToFileTime());
        writer.Write(2); // sync state = MODIFIED
        WriteDynamicString(writer, accountGuid);
        writer.Write(CONTAINERSINDEX_FOOTER);

        foreach (var slot in slotList)
        {
            if (!string.IsNullOrEmpty(slot.SecondIdentifier))
            {
                WriteDynamicString(writer, slot.Identifier);
                WriteDynamicString(writer, slot.SecondIdentifier);
            }
            else
            {
                WriteDynamicString(writer, slot.Identifier);
                writer.Write(0); // empty second identifier
            }

            WriteDynamicString(writer, slot.SyncTime ?? "");
            writer.Write(slot.BlobContainerExtension);
            writer.Write(slot.SyncState);
            writer.Write(slot.DirectoryGuid.ToByteArray());
            writer.Write(slot.LastModified.ToUniversalTime().ToFileTime());
            writer.Write(0L); // empty
            // Calculate total size of blob files
            long totalSize = 0;
            if (slot.DataFilePath != null && File.Exists(slot.DataFilePath))
                totalSize += new FileInfo(slot.DataFilePath).Length;
            if (slot.MetaFilePath != null && File.Exists(slot.MetaFilePath))
                totalSize += new FileInfo(slot.MetaFilePath).Length;
            writer.Write(totalSize);
        }

        // The parser requires at least 200 bytes; keep small indexes (for example after
        // deleting most slots) valid by padding with zeros.
        if (ms.Position < 200)
        {
            ms.Position = 199;
            writer.Write((byte)0);
        }

        byte[] result = buffer.AsSpan(0, (int)ms.Position).ToArray();
        File.WriteAllBytes(containersIndexPath, result);
    }

    // Internal

    /// <summary>
    /// Resolves the on-disk blob directory for a GUID.
    /// Xbox wgs directories may use either the hyphenated ("D") or compact ("N") GUID
    /// format in upper- or lower-case.  Try all common variants and fall back to the
    /// no-hyphens uppercase form (used by most Game Pass installs).
    /// </summary>
    private static string ResolveBlobDirectory(string baseDir, Guid guid)
    {
        // Most Xbox Game Pass installs use uppercase, no-hyphens
        string upperN = Path.Combine(baseDir, guid.ToString("N").ToUpperInvariant());
        if (Directory.Exists(upperN)) return upperN;

        string lowerN = Path.Combine(baseDir, guid.ToString("N"));
        if (Directory.Exists(lowerN)) return lowerN;

        // Some older installs may use the hyphenated form
        string hyphenated = Path.Combine(baseDir, guid.ToString("D"));
        if (Directory.Exists(hyphenated)) return hyphenated;

        return upperN; // default for new directories
    }

    /// <summary>
    /// Get the file path for a GUID-named blob file, trying both uppercase and lowercase.
    /// Xbox blob files use GUID-named files without hyphens.
    /// </summary>
    private static string GetBlobFilePath(string directory, Guid guid)
    {
        string upper = Path.Combine(directory, guid.ToString("N").ToUpperInvariant());
        if (File.Exists(upper)) return upper;
        string lower = Path.Combine(directory, guid.ToString("N"));
        if (File.Exists(lower)) return lower;
        return upper; // default to uppercase for new files
    }

    private static void ParseBlobContainer(XboxSlotInfo slotInfo)
    {
        // Try container files in descending extension order (newest first)
        var containerFiles = Directory.GetFiles(slotInfo.BlobDirectoryPath, "container.*")
            .OrderByDescending(f => Path.GetExtension(f))
            .ToArray();

        foreach (var containerFile in containerFiles)
        {
            byte[] bytes = File.ReadAllBytes(containerFile);
            if (bytes.Length != BLOBCONTAINER_TOTAL_LENGTH) continue;

            int header = ReadInt32LE(bytes, 0);
            if (header != BLOBCONTAINER_HEADER) continue;

            int blobCount = ReadInt32LE(bytes, 4);
            int offset = 8;

            for (int j = 0; j < blobCount && offset + BLOBCONTAINER_IDENTIFIER_LENGTH + 32 <= bytes.Length; j++)
            {
                // Read identifier (UTF-16, fixed 128 bytes)
                string blobId = Encoding.Unicode.GetString(bytes, offset, BLOBCONTAINER_IDENTIFIER_LENGTH).TrimEnd('\0');
                offset += BLOBCONTAINER_IDENTIFIER_LENGTH;

                // Read cloud/sync GUID (16 bytes) and local GUID (16 bytes)
                byte[] syncGuidBytes = new byte[16];
                Buffer.BlockCopy(bytes, offset, syncGuidBytes, 0, 16);
                Guid syncGuid = new Guid(syncGuidBytes);
                offset += 16; // cloud/sync guid

                byte[] localGuidBytes = new byte[16];
                Buffer.BlockCopy(bytes, offset, localGuidBytes, 0, 16);
                Guid localGuid = new Guid(localGuidBytes);
                offset += 16;

                string blobPath = GetBlobFilePath(slotInfo.BlobDirectoryPath, localGuid);

                if (blobId.StartsWith("data", StringComparison.OrdinalIgnoreCase))
                {
                    slotInfo.DataFilePath = blobPath;
                    slotInfo.DataSyncGuid = syncGuid;
                }
                else if (blobId.StartsWith("meta", StringComparison.OrdinalIgnoreCase))
                {
                    slotInfo.MetaFilePath = blobPath;
                    slotInfo.MetaSyncGuid = syncGuid;
                }
            }

            // If we found data file, we're done
            if (slotInfo.DataFilePath != null && File.Exists(slotInfo.DataFilePath))
            {
                slotInfo.BlobContainerExtension = byte.TryParse(Path.GetExtension(containerFile).TrimStart('.'), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out byte ext) ? ext : (byte)1;
                break;
            }
        }
    }

    private static void WriteBlobContainer(XboxSlotInfo slotInfo, Guid dataGuid, Guid metaGuid)
    {
        byte newExt = (byte)(slotInfo.BlobContainerExtension == 255 ? 1 : slotInfo.BlobContainerExtension + 1);
        slotInfo.BlobContainerExtension = newExt;

        string containerPath = Path.Combine(slotInfo.BlobDirectoryPath, $"container.{newExt}");
        byte[] buffer = new byte[BLOBCONTAINER_TOTAL_LENGTH];

        using var ms = new MemoryStream(buffer);
        using var writer = new BinaryWriter(ms);

        writer.Write(BLOBCONTAINER_HEADER);
        writer.Write(BLOBCONTAINER_COUNT);

        // Data blob entry
        byte[] dataIdBytes = Encoding.Unicode.GetBytes("data");
        writer.Write(dataIdBytes);
        ms.Position = 8 + BLOBCONTAINER_IDENTIFIER_LENGTH; // skip rest of identifier padding
        writer.Write(slotInfo.DataSyncGuid?.ToByteArray() ?? new byte[16]); // cloud/sync GUID (preserved)
        writer.Write(dataGuid.ToByteArray()); // local GUID

        // Meta blob entry
        byte[] metaIdBytes = Encoding.Unicode.GetBytes("meta");
        writer.Write(metaIdBytes);
        ms.Position = 8 + BLOBCONTAINER_IDENTIFIER_LENGTH + 32 + BLOBCONTAINER_IDENTIFIER_LENGTH;
        writer.Write(slotInfo.MetaSyncGuid?.ToByteArray() ?? new byte[16]); // cloud/sync GUID (preserved)
        writer.Write(metaGuid.ToByteArray()); // local GUID

        // Delete old container files
        foreach (var old in Directory.GetFiles(slotInfo.BlobDirectoryPath, "container.*"))
            File.Delete(old);

        File.WriteAllBytes(containerPath, buffer);
    }

    private static int ReadDynamicString(byte[] bytes, int offset, out string value)
    {
        if (offset + 4 > bytes.Length) { value = ""; return 4; }
        int length = ReadInt32LE(bytes, offset);
        if (length <= 0 || offset + 4 + length * 2 > bytes.Length)
        {
            value = "";
            return 4;
        }
        value = Encoding.Unicode.GetString(bytes, offset + 4, length * 2);
        return 4 + length * 2;
    }

    private static void WriteDynamicString(BinaryWriter writer, string value)
    {
        writer.Write(value.Length);
        writer.Write(Encoding.Unicode.GetBytes(value));
    }

    private static int ReadInt32LE(byte[] data, int offset)
    {
        return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
    }

    private static long ReadInt64LE(byte[] data, int offset)
    {
        return (long)data[offset] | ((long)data[offset + 1] << 8) | ((long)data[offset + 2] << 16) | ((long)data[offset + 3] << 24)
             | ((long)data[offset + 4] << 32) | ((long)data[offset + 5] << 40) | ((long)data[offset + 6] << 48) | ((long)data[offset + 7] << 56);
    }
}