# IO Layer

## Overview

The IO layer (`IO/`) handles everything between a save file on disk and the in-memory
`JsonObject` tree the rest of the application works with. This includes platform detection,
LZ4 compression, TEA/XXTEA encryption, binary I/O, backup and restore, and multi-platform
file layout management. Nothing in this folder depends on WinForms, p/invoke or external
packages: the LZ4 compressor, TEA cipher and SpookyHash implementation are all written in
native C#.

### Save File Pipeline

Every NMS save, regardless of platform, ultimately contains the same JSON payload. The
platforms differ only in how they wrap it:

```
Steam / GOG:
  saveN.hg    -->  [16-byte LZ4 block headers]  -->  JSON (Latin-1)
  mf_saveN.hg -->  TEA/XXTEA-encrypted metadata

Xbox Game Pass (xgs, current format):
  {Identifier}/data  -->  HGSAVEV2, NMS streaming (0xE5A1EDFE) or raw LZ4  -->  JSON
  {Identifier}/meta  -->  game metadata blob (360 bytes) or a 20-byte account meta blob

Xbox Game Pass (wgs, legacy mirror):
  containers.index  -->  GUID blob directories  -->  container.N  -->  blobs
  save blob         -->  same payload formats as xgs

PlayStation 4:
  memory.dat        -->  11-container slot table
    homebrew:       meta at 0x00 (32-byte entries), data at 0x20000, raw LZ4 block per slot
    SaveWizard:     64-byte (0x40) NOMANSKY preamble, meta at 0x40 (48-byte entries),
                    pre-decompressed JSON packed from 0x1040
  savedataNN.hg     -->  0x70-byte NOMANSKY header (UTF-8 JSON) or plain JSON (HTOS)
  manifestNN.hg     -->  plaintext metadata companion

Nintendo Switch:
  savedataNN.hg     -->  [16-byte LZ4 block headers]  -->  JSON
  manifestNN.hg     -->  plaintext metadata companion
```

**Loading flow:** `LoadSaveFile` is the file-based loader used for Steam/GOG, PS4 streaming
and Switch saves. It sniffs the file header and picks one of three paths:

- NOMANSKY magic (first 8 bytes): JSON after the 0x70-byte header, decoded as UTF-8.
- NMS LZ4 magic `0xE5A1EDFE` (first 4 bytes): multi-block LZ4, decoded as Latin-1.
- Anything else: plain uncompressed JSON, decoded as Latin-1.

It does not call `DetectPlatform` itself. Platform selection happens in the UI layer
(`MainForm.PopulateSaveSlots` calls `SaveFileManager.DetectPlatform(directory)`), and Xbox
saves are loaded with `LoadXboxSave` while `memory.dat` saves use `LoadPS4MemoryDatSave`.
After parsing, context transforms are registered so that `PlayerStateData` and
`SpawnStateData` resolve dynamically for contextual saves.

**Saving flow:** Panels write changes directly to the in-memory `JsonObject` slots. There
are three write paths:

- **Data file (`SaveToFile`):** serialise the tree to compact JSON, append a NUL
  terminator, then either LZ4-compress with `Lz4CompressorStream` or write plain bytes.
  Optionally writes a platform-appropriate meta/companion file.
- **Xbox (`SaveXboxSave`):** compress with NMS LZ4 streaming and replace the payload in
  place through `XgsSaveManager` (xgs) or `ContainersIndexManager` (wgs), preserving the
  blob GUIDs, container extension and cloud container identity. xgs writes are mirrored to
  a sibling wgs container when one exists.
- **PS4 NOMANSKY (`SaveNomanSkyFile`):** preserve the existing 0x70-byte header and write
  the JSON back after it.

The NUL terminator is always appended. PS4 HTOS `savedata*.hg` files and `accountdata.hg`
are written uncompressed (`compress: false`); everything else is compressed by default.

---

## Classes

### SaveFileManager

| | |
|---|---|
| File | `IO/SaveFileManager.cs` |
| Purpose | High-level save file load/save operations with platform abstraction |

The main entry point for all file operations. Detects the platform from a save directory,
loads and decompresses data, writes back with optional compression and metadata, provides
fast header-only detection helpers, and implements the backup/restore subsystem. Also
contains the nested `SaveSlot` class (`Index`, `FilePath`, `MetadataPath`, `IsEmpty`,
`LastModified`, `Platform`) and the `Platform` enum (`Steam`, `XboxGamePass`, `PS4`, `GOG`,
`Switch`, `Unknown`).

**Platform detection rules** (`DetectPlatform(directory)`, in order):

1. `containers.index` present -> Xbox Game Pass (wgs).
2. `SlotNAuto`/`SlotNManual`/`AccountData` folders containing a `data` file -> Xbox
   Game Pass (xgs). See `SaveFileManager.DetectXboxSaveFormat`.
3. `manifestaccountdata.hg` present, or any `manifest*.dat` file -> Switch. Switch is
   checked before PS4 so its `savedata*.hg` files do not match the PS4 rule first.
4. `memory.dat` present, or any `savedata*.hg` file -> PS4.
5. Any `save*.hg` file, or `accountdata.hg` -> GOG when the directory name is exactly
   `DefaultUser` (case-insensitive), otherwise Steam.
6. Otherwise -> Unknown.

Edge case: a Switch export that only has `manifestNN.hg` companions (no
`manifestaccountdata.hg` and no `manifest*.dat`) is not recognised as Switch and falls
through to PS4 because it also has `savedata*.hg` files.

**Load methods:**

| Method | Description |
|--------|-------------|
| `DetectPlatform(directory)` | Detect save format from directory contents (rules above) |
| `FindDefaultSaveDirectory()` | OS-specific default NMS save location (first profile found) |
| `LoadSaveFile(filePath)` | File-based loader: NOMANSKY (UTF-8), LZ4 (Latin-1), or plain (Latin-1) |
| `LoadXboxSave(savePath, saveIdentifier)` | Resolve the slot in an xgs container or wgs index directory and parse it |
| `EnumerateXboxSlots(savePath)` | Enumerate identifiers for both Xbox formats (`Dictionary<string, XboxSlotInfo>`) |
| `DetectXboxSaveFormat(directory)` | `Wgs`, `Xgs` or `None` |
| `LoadPS4MemoryDatSave(memoryDatPath, slotIndex)` | Extract and parse one `memory.dat` slot |
| `IsNomanSkyFile(filePath)` | True when the file starts with the NOMANSKY magic |
| `RegisterContextTransforms(result)` | Internal; registers `PlayerStateData` / `SpawnStateData` dynamic resolution |

**Save methods:**

| Method | Description |
|--------|-------------|
| `SaveToFile(filePath, data, compress, writeMeta, platform, slotIndex)` | Serialise, append NUL, compress or write plain, optionally write meta. `platform` defaults to auto-detection from the containing directory; the Steam/GOG meta encryption storage slot is derived from the file name, not from `slotIndex` |
| `SaveNomanSkyFile(filePath, data)` | Preserve the 0x70-byte NOMANSKY header, update the JSON size at offset 0x5C, write Latin-1 JSON after it |
| `SaveXboxSave(savePath, slotIdentifier, data)` | Compress with NMS streaming and write the payload in place for both xgs and wgs, preserving cloud container identity; xgs writes are mirrored to a sibling wgs container. Returns the updated `XboxSlotInfo` |
| `SaveXboxAccountData(savePath, accountData)` | Raw LZ4 block compression plus a 20-byte account meta blob (size field updated); mirrored for xgs |

**Backup and restore methods:**

| Method | Description |
|--------|-------------|
| `ResolveBackupRoot()` | Backup root priority: configured path -> EXE-relative `Save Backups` -> `%TEMP%\NMSE\Save Backups`. Creates the directory, falling through on failure |
| `FindExistingBackupRoots()` | Existing backup roots in priority order, de-duplicated by normalised path; does not create directories |
| `BackupSaveDirectory(saveDirectory)` | Timestamped ZIP `{dirName}_{yyyyMMdd_HHmmss}.zip`, retaining ten backups |
| `FindBackupZips(saveDirectory)` | All matching ZIPs across every existing root, newest first, each returned once |
| `GetBackupEntryNames(zipPath)` | Full entry names (including directory prefixes) in archive order |
| `BackupContainsFile(zipPath, fileName)` | Case-insensitive match on the final path segment of any entry |
| `RestoreFileFromBackup(zipPath, fileName, destinationPath)` | Restore a single file, overwriting the destination |
| `RestoreBackupToDirectory(zipPath, destinationDirectory)` | Restore every entry, preserving directory structure, skipping entries that would escape the destination; returns the written paths |

**Fast detection helpers:**

| Method | Description |
|--------|-------------|
| `FormatPlayTime(seconds)` | Format as `MM:SS` or `H:MM:SS` |
| `DetectGameModeFast(filePath)` | Scan the first JSON block only. Tries `PresetGameMode` then obfuscated `pwt`; then every occurrence of `GameMode` / obfuscated `idA`; then `DifficultyState` / obfuscated `LyC` with `DifficultyPresetType` / obfuscated `7ND` |
| `DetectSaveNameFast(filePath)` | Scan the first JSON block for `SaveName` or obfuscated `Pk4` |
| `DetectGameModeFromJson(json)` | Same game-mode scan for an already-extracted JSON string (used for PS4 `memory.dat` slots) |
| `DetectSaveNameFromJson(json)` | Same save-name scan for an already-extracted JSON string |
| `DetectActiveContextFast(filePath, out bool isExpedition)` | Scan the first JSON block for `ActiveContext` / obfuscated `XTp` equal to `"Season"` |
| `DetectActiveContextFromJson(json, out bool isExpedition)` | Same check for an already-extracted JSON string |
| `TryDetectActiveContext(JsonObject data)` | Set `SaveContext.IsExpeditionSave` from `ActiveContext` on a parsed tree |

`FindDefaultSaveDirectories` returns every detected default save directory, in preference
order, and `FindDefaultSaveDirectory` returns the first (or null):

- Windows Steam/GOG: `%APPDATA%\HelloGames\NMS\{profile}` - all valid profiles (`st_*`,
  `DefaultUser`, or a folder holding save data), most recently written first.
- Windows Xbox Game Pass: `%LOCALAPPDATA%\Packages\HelloGames*\SystemAppData\xgs\{SaveId}\`
  containers that hold at least one `SlotN...` folder (preferred, current format), then the
  legacy `...\SystemAppData\wgs\{SaveId}\` containers containing `containers.index`, and only
  then slot-less xgs containers.
- macOS: `~/Library/Application Support/HelloGames/NMS/{profile}`.
- Linux: Steam/Proton `~/.local/share/Steam/steamapps/compatdata/275850/...` and the
  Flatpak `~/.var/app/com.valvesoftware.Steam/...` equivalent.

`EnumerateSteamProfiles(nmsRoot)` and `EnumerateXboxContainers(root)` expose the per-store
enumeration for the UI and tests, and `ResolveSaveDirectory(directory)` drills a user-selected
parent folder (the Packages folder, a package folder, `SystemAppData`, `xgs`/`wgs`, or the NMS
profile root) down to the first valid profile or container.

**Design choices:**

- **Latin-1 encoding** is used instead of UTF-8 for all formats except NOMANSKY to preserve
  bytes >= 0x80 that appear in NMS JSON strings. These become `BinaryData` objects in the
  model layer. The NOMANSKY path decodes UTF-8 and trims trailing NUL padding.
- **ArrayPool** is used for temporary decompression buffers to avoid long-lived allocations.
- **Two-pass decompression:** the first pass scans block headers to calculate the total
  decompressed size, the second pass decompresses into a single allocation. This avoids
  resizable buffers. `Lz4DecompressorStream` does not use this pattern; it decompresses
  block by block on demand.
- **Obfuscated keys:** newer NMS saves rename metadata fields, so the fast detectors accept
  both the readable and obfuscated spellings.

---

### SaveSlotManager

| | |
|---|---|
| File | `IO/SaveSlotManager.cs` |
| Purpose | Slot-level operations - copy, move, swap, delete, cross-platform transfer |

All methods are `static`. Provides platform-aware file path resolution and slot-level file
operations. Each NMS save "slot" in the game UI contains two files: an auto save and a
manual save. Slot indices are 0-based (slot 0 = game "Slot 1").

| Method | Description |
|--------|-------------|
| `GetAllSlotFiles(saveDirectory, slotIndex, platform)` | Returns both file pairs (index 0 auto, index 1 manual) as `SlotFiles[]` |
| `GetSlotFiles(saveDirectory, slotIndex, platform)` | Returns the manual save pair (index 1), falling back to the auto pair or an empty `SlotFiles` |
| `GetExistingSlotIndices(saveDirectory, platform)` | Returns the 0-based indices of slots that contain data (used to list copy sources) |
| `IsSlotOperationSupported(saveDirectory, platform)` | False for save formats without slot operations (PS4 `memory.dat`, unknown directories) |
| `CopySlot(saveDirectory, sourceSlotIndex, destSlotIndex, platform)` | Copy both pairs, overwriting the destination (no backup is taken); meta files are re-keyed. Xbox Game Pass copies the matching xgs slot folders or wgs `containers.index` entries, creating them when missing |
| `MoveSlot(saveDirectory, sourceSlotIndex, destSlotIndex, platform)` | Copy then delete source |
| `SwapSlots(saveDirectory, slotA, slotB, platform)` | Swap via a temp directory; meta files are re-keyed to their new storage slots. Xbox Game Pass swaps the xgs slot folders or the wgs entries and their blobs |
| `DeleteSlot(saveDirectory, slotIndex, platform)` | Delete data + meta files for both pairs (Xbox Game Pass: removes the xgs slot folders or the wgs index entries and their blob directories) |
| `TransferCrossPlatform(sourceFilePath, destDirectory, destSlotIndex, destPlatform, transferOptions)` | Load, rewrite ownership and platform token, write to the destination format |
| `SlotIndexToStorageSlot(slotIndex)` | Internal; 0 -> 0, otherwise 2 + slotIndex (the gap at 1 is reserved for Settings) |
| `StorageSlotFromFileName(filePath)` | Internal; derive the TEA storage slot from the file name (see below) |

`SlotFiles` holds `DataFile` and `MetaFile`.

**File naming conventions** (slot N, 0-based):

| Platform | Auto | Manual | Meta |
|----------|------|--------|------|
| Steam/GOG slot 0 | `save.hg` | `save2.hg` | `mf_save.hg`, `mf_save2.hg` |
| Steam/GOG slot N | `save{2N+1}.hg` | `save{2N+2}.hg` | `mf_` + data file name |
| Switch/PS4 game slot N | `savedata{2N+2:D2}.hg` | `savedata{2N+3:D2}.hg` | `manifest{2N+2:D2}.hg`, `manifest{2N+3:D2}.hg` |
| Switch settings / PS4 account | `savedata00.hg` | - | `manifest00.hg` |
| Account data | `accountdata.hg` | - | (account manifests for streaming platforms) |

Game slots therefore start at `savedata02.hg`; index 00 is the settings file on Switch and
the account data file on PS4.

`TransferOptions` controls selective transfer of bases, discoveries, settlements and
ByteBeat data (all four default to `true`), plus `SourceUID`, `DestUID`, `DestLID`,
`DestUSN` and `DestPTK`. `TransferCrossPlatform` sets the `Platform` token (`PC`, `XBX`,
`PS4`, `NX`), rewrites ownership references, then writes via `SaveToFile` plus
`WriteMetaForPlatform` for file-based platforms. Xbox Game Pass destinations are routed to
`SaveToXboxGamePass`, which resolves the manual slot identifier in either Xbox format by slot
number and calls `SaveXboxSave`.

**Storage slot mapping** (`StorageSlotFromFileName`): `accountdata.hg` -> 0, `save.hg` -> 2,
`saveN.hg` (N >= 2) -> N + 1, anything unrecognised -> 2 (safe default). The meta
encryption key depends on this value, so using the wrong slot produces a garbled meta file.

**Meta re-keying:** for Steam/GOG the meta file is decrypted with the source storage slot
and re-encrypted with the destination storage slot (falling back to a verbatim copy if
decryption fails). For Switch, `CopyMetaFile` patches the manifest index field at byte
offset 12. Other platforms copy the meta verbatim.

**Slot operation errors:** `SlotEmptyException` carries the 0-based `SlotIndex` plus a
1-based `SlotNumber` for display; `SlotOperationUnsupportedException` marks formats
without slot operations (`Platform`, `IsMemoryDat`). The UI translates both into localised
messages instead of showing the raw exception text.

**Xbox slot operations:** `CopyXboxSlot`, `SwapXboxSlots` and `DeleteXboxSlot` operate
through `ContainersIndexManager` (wgs) and `CopyXgsSlot`, `SwapXgsSlots` and `DeleteXgsSlot`
through `XgsSaveManager` (xgs). Data and meta blobs are copied byte-for-byte (the save
payload is never re-encoded), missing wgs destination entries are created with new GUID blob
directories, and a new slot never reuses another slot's cloud sync GUIDs. xgs operations are
mirrored to a sibling wgs container when one exists. Blob directories and xgs slot folders
are deleted together with their entries.

---

### ContainersIndexManager

| | |
|---|---|
| File | `IO/ContainersIndexManager.cs` |
| Purpose | Parse and write Xbox Game Pass `containers.index` files and blob directories |

Xbox saves use an indirection layer:

```
containers.index      - maps identifiers (Slot1Auto, Slot1Manual, AccountData, Settings)
                        to GUID-named blob directories
{GUID}/container.N    - blob container pointing at the data and meta files
{GUID}/{GUID}         - the actual data or meta blob file
```

Blob container files are exactly **328 bytes**: a 4-byte header, a 4-byte blob count (2),
then two entries of a 128-byte UTF-16LE identifier plus a 16-byte cloud/sync GUID and a
16-byte local GUID. The container extension is incremented each write (`255` wraps to `1`);
older `container.*` files are deleted.

GUID directory names are resolved by trying uppercase no-hyphen, lowercase no-hyphen and
hyphenated forms, defaulting to uppercase no-hyphen.

**Compression formats read from data blobs:**

- **HGSAVEV2:** `"HGSAVEV2\0"` (9 bytes) followed by frames of
  `decompressedSize(4) + compressedSize(4) + LZ4 data` (post-Omega Xbox 4.52-5.00).
- **NMS LZ4 streaming:** `0xE5A1EDFE` magic plus 16-byte block headers, multi-block
  (Worlds Part I 5.00+ writes use this format).
- **Plain/single-block:** uncompressed JSON, or a raw LZ4 block as used by AccountData and
  Settings blobs.

`XboxSlotInfo` holds: `Identifier`, `SecondIdentifier`, `SyncTime`, `BlobContainerExtension`,
`SyncState`, `DirectoryGuid`, `BlobDirectoryPath`, `LastModified`, `DataFilePath`,
`MetaFilePath`, `DataSyncGuid` and `MetaSyncGuid`. `ContainersIndexData` holds the global
header fields needed to rewrite the file (`ProcessIdentifier`, `AccountGuid`,
`LastWriteTime`, `SyncState`, `Slots`) and is populated by `ParseContainersIndexFull`.

| Method | Description |
|--------|-------------|
| `IsXboxSaveDirectory(directory)` | Check for `containers.index` |
| `ParseContainersIndex(path)` | Parse the index into a `Dictionary<string, XboxSlotInfo>` |
| `ParseContainersIndexFull(path)` | Parse the index plus the global header (`ContainersIndexData`) |
| `LoadXboxSave(slotInfo)` | Load and decompress a data blob to a JSON string |
| `LoadXboxMeta(slotInfo)` | Return the raw metadata blob bytes |
| `WriteXboxSave(slotInfo, compressedData, metaData)` | Replace the blob contents in place, preserving the blob GUIDs, container file and cloud identity. Inputs are already-compressed bytes; the meta file is only rewritten when it actually changed. Slots with no existing blob (new copies) create fresh GUID blobs and a container |
| `WriteContainersIndex(path, slots, processIdentifier, accountGuid, lastWriteTime)` | Rewrite the index, writing sync state 2 (modified) and the footer |
| `IsSaveSlot(identifier)` | True unless the identifier is `AccountData` or `Settings` |
| `ExtractSlotNumber(identifier)` | `"Slot1Auto"` -> 1, `"Slot2Manual"` -> 2; 0 if the pattern does not match |
| `IsAutoSave(identifier)` | True when the identifier contains `Auto` |

**Write contract:** `WriteXboxSave` overwrites the existing data blob bytes at their current
GUID path and leaves `container.N` and the containers.index entry identity untouched, so
cached `DataFilePath` / `MetaFilePath` values remain valid. Only slots with no existing blob
(new copies) create fresh GUID files, incrementing `BlobContainerExtension`.
`SaveFileManager.SaveXboxSave` returns the updated `XboxSlotInfo` for callers that cache
paths.

`AccountDataIdentifier` (`"AccountData"`) and `SettingsIdentifier` (`"Settings"`) are the
special non-save entries.

---

### XgsSaveManager

| | |
|---|---|
| File | `IO/XgsSaveManager.cs` |
| Purpose | Read and write Xbox Game Pass `xgs` (XGameSaveFiles) save containers |

Modern Xbox (PC) / Game Pass titles use the GDK XGameSaveFiles API, which stores plain files
instead of the legacy wgs container/blob indirection:

```
xgs\{HexXuid}_{SCID}\AccountData\{data,meta}
xgs\{HexXuid}_{SCID}\Slot1Auto\{data,meta}
xgs\{HexXuid}_{SCID}\Slot1Manual\{data,meta}
```

The slot identity is the folder name and the `data` / `meta` payloads are byte-identical to
the wgs blob files, so both formats share the `XboxBlobCodec` decompression path. No
`containers.index` exists for xgs containers.

Session behaviour observed from real saves: the game writes `xgs` live during play and
rebuilds the `wgs` mirror at exit. Blob GUIDs stay stable across writes (the cloud container
identity) and the wgs `container.N` extension is a monotonically increasing write counter.

| Method | Description |
|--------|-------------|
| `IsXgsContainerDirectory(directory)` | True when identifier folders with a `data` file exist and no `containers.index` is present |
| `EnumerateSlots(containerDirectory)` | Slot descriptors keyed by folder name (`Slot1Auto`, `AccountData`, ...) |
| `GetSlotDirectory(containerDirectory, identifier)` | Folder path for an identifier |
| `LoadSave(slotInfo)` | Decompress `data` through `XboxBlobCodec` |
| `LoadMeta(slotInfo)` | Raw `meta` bytes |
| `WriteSave(slotInfo, compressedData, metaData)` | Replace `data` (and `meta` when changed) in place via a temp file and atomic replace, preserving the folder name and cloud identity |

---

### XboxBlobCodec

| | |
|---|---|
| File | `IO/XboxBlobCodec.cs` |
| Purpose | Shared Xbox blob decompression (wgs blob files and xgs `data` files) |

`DetectFormat(bytes)` returns `Hgsv2`, `NmsLz4` or `Raw`; `Decompress(bytes)` supports:

- **HGSAVEV2:** `"HGSAVEV2\0"` header followed by `decompressedSize(4) + compressedSize(4) +
  LZ4 data` frames.
- **NMS LZ4 streaming:** `0xE5A1EDFE` magic plus 16-byte block headers, multi-block.
- **Plain/single-block:** uncompressed JSON, or a raw LZ4 block as used by AccountData blobs.

---

### MemoryDatManager

| | |
|---|---|
| File | `IO/MemoryDatManager.cs` |
| Purpose | Read and write PlayStation monolithic `memory.dat` files |

`memory.dat` packs all PS4 containers into a single file: 1 account slot plus 5 game slots
with an auto and a manual save each, so **11 containers** in total, not 31. Metadata sits at
a fixed offset followed by a data region. Two variants exist:

| Variant | Preamble | Meta region | Meta entry | Data region | Total size |
|---------|----------|-------------|------------|-------------|------------|
| Homebrew (Apollo, Save Mounter) | none | `0x00` | 32 bytes | `0x20000` | 32 MB |
| SaveWizard export | 64 bytes (`0x40`) NOMANSKY | `0x40` | 48 bytes | `0x1040` | 48 MB |

The SaveWizard preamble is: `"NOMANSKY"` at `0x00`, meta format `1` at `0x08`, meta offset
`0x40` at `0x0C`, slot count `11` at `0x10` and total length at `0x14`. SaveWizard slots are
pre-decompressed JSON packed sequentially from `0x1040`, while homebrew slots hold a raw LZ4
block. Slot existence is determined solely by a non-zero `ChunkOffset`; the meta header word
varies across tools (`0x000007D0` for older homebrew dumps, `0xCA55E77E` for newer ones) and
is not required for detection.

The constants `0x40000` (256 KB account data) and `0x300000` (3 MB per save slot) are
write-time allocation sizes for homebrew output, not assumptions made when reading. Reads
always use the per-slot `ChunkOffset` and `CompressedSize` from the metadata, and
`WriteMemoryDat` requires the caller to supply offsets in `slotMeta`.

`MemoryDatSlot` holds: `Index`, `Exists`, `MetaFormat`, `CompressedSize`, `ChunkOffset`,
`ChunkSize`, `MetaIndex`, `Timestamp`, `DecompressedSize`, `IsSaveWizard` and
`SaveWizardDataOffset` (the absolute data offset read from the 48-byte SaveWizard entry,
zero for homebrew slots).

| Method | Description |
|--------|-------------|
| `IsMemoryDat(filePath)` | Check the file name (only `memory.dat` matches; the header is not inspected) |
| `IsSaveWizardFormat(filePath)` | True when the file starts with the NOMANSKY magic |
| `ReadSlots(filePath)` | Parse all 11 slot metadata entries |
| `ExtractSlotData(filePath, slotIndex)` | Return the JSON for one slot. Homebrew: raw LZ4 block decompressed with `Lz4Compressor`; SaveWizard: pre-decompressed Latin-1 JSON with trailing NULs trimmed |
| `WriteMemoryDat(outputPath, slotData, slotMeta)` | Write a complete 32 MB homebrew file from LZ4-compressed slot bytes |
| `WriteMemoryDatSaveWizard(outputPath, slotData, slotMeta)` | Write a complete 48 MB SaveWizard file from raw (pre-decompressed) JSON bytes, packing data sequentially from `0x1040` |

---

### MetaCrypto

| | |
|---|---|
| File | `IO/MetaCrypto.cs` |
| Purpose | TEA/XXTEA encryption for Steam/GOG meta files, plus SpookyHash integrity hashing |

Steam and GOG meta files are encrypted with XXTEA using a 4-uint32 key derived from the
storage slot index. The key words embed the NMS developers' names (SEAN, DAVE, RYAN, GRANT)
packed as little-endian uints; `DeriveKey0(storageSlot)` (private) replaces the first word
with `RotateLeft(storageSlot ^ 0x1422CB8C, 13) * 5 + 0xE6546B64`.

| Method | Description |
|--------|-------------|
| `Encrypt(data, storageSlot, iterations)` | XXTEA encrypt a uint array with the slot-dependent key |
| `Decrypt(encrypted, storageSlot, iterations)` | Try the primary slot, then brute-force slots 0-31; returns the input unchanged if nothing decrypts |
| `ComputeMetaHashes(data)` | 16 bytes SpookyHash (two 64-bit words) + 32 bytes SHA256 = 48 bytes |
| `DeriveKey0(storageSlot)` | Private; compute the slot-dependent first key word |

`SpookyHashV2` is an internal implementation of Bob Jenkins' SpookyHash V2 used for the
first 16 bytes of the integrity hash.

**Iteration rule:** 8 rounds for vanilla (format 2001) meta files, 6 rounds for everything
else. The rounds are chosen by `MetaFileWriter` when writing and by file size (104 bytes =
vanilla) when reading or re-keying.

**Hash policy:** only the pre-Frontiers format 2001 stores real SpookyHash + SHA256 values
of the compressed data; Frontiers and later formats write 48 zero placeholder bytes.

**Design choice:** `Decrypt` tries all possible slot keys when the primary fails, which
handles files that were copied between slots manually.

---

### MetaFileWriter

| | |
|---|---|
| File | `IO/MetaFileWriter.cs` |
| Purpose | Write platform-specific meta/companion files alongside save data |

Each platform has a different meta file format and header:

| Platform | Header | Encryption | Meta file name |
|----------|--------|------------|----------------|
| Steam/GOG | `0xEEEEEEBE` | XXTEA (slot-keyed) | `mf_` + data file name (`save.hg` -> `mf_save.hg`, `saveN.hg` -> `mf_saveN.hg`) |
| Switch | `0xCA55E77E` | None | `manifest{NN}.hg` |
| PS4 streaming | `0xCA55E77E` | None | `manifest{NN}.hg` |

Meta format identifiers are `2001`-`2004` and are derived from the save base version, not
from the byte length. The byte lengths differ per platform:

| Format | Identifier | Base version | Steam/GOG size | Switch/PS4 size |
|--------|------------|--------------|----------------|-----------------|
| Vanilla (pre-Frontiers) | 2001 | < 4115 | 104 | 100 |
| Frontiers 3.60+ (Waypoint) | 2002 | >= 4115 | 360 | 356 |
| Worlds Part I 5.00+ | 2003 | >= 4135 | 384 | 372 |
| Worlds Part II 5.50+ | 2004 | >= 4145 | 432 | 380 |

The `0x000007D0` value sometimes seen in PS4 files is an older homebrew `memory.dat`
per-slot meta header, not a meta file header.

`SaveMetaInfo` holds: `BaseVersion`, `GameMode`, `Season`, `TotalPlayTime`, `SaveName`,
`SaveSummary`, `DifficultyPreset`, `DifficultyPresetTag`.

| Method | Description |
|--------|-------------|
| `WriteSteamMeta(saveFilePath, compressedData, decompressedSize, info, storageSlot)` | Write the encrypted Steam/GOG meta. Format 2001 writes real hashes; 2002+ writes zero hashes and the extended fields. 2003 adds slot ID/timestamp/format copy, 2004 adds the difficulty tag string |
| `WriteSwitchMeta(saveFilePath, decompressedSize, info, metaIndex)` | Write a plaintext Switch manifest. The index is re-derived from a `savedataNN.hg` file name when possible |
| `WritePlaystationStreamingMeta(saveFilePath, decompressedSize, info, metaIndex)` | Write a plaintext PS4 HTOS manifest, with the same index derivation |
| `ReadSteamMeta(saveFilePath, storageSlot)` | Read and decrypt a Steam/GOG meta into a uint array, or null when missing |
| `ExtractMetaInfo(saveData)` | Extract metadata fields from the save JSON |
| `GetSteamMetaPath(saveFilePath)` | Internal; prepend `mf_` to the data file name |
| `GetSwitchMetaPath(saveFilePath, metaIndex)` | Internal; `manifest{metaIndex:D2}.hg` |
| `BytesToUInts(bytes)` / `UIntsToBytes(uints)` | Internal conversion helpers used by meta re-keying |

**Steam/GOG base version:** `WriteSteamMeta` preserves the existing meta base version
(uint index 17) when the meta decrypts cleanly. For a new slot with no meta, it calls the
private `TryReadSiblingBaseVersion`, which decrypts sibling `save*.hg` meta files in the
same directory and inherits their game build version. This prevents the
"Cross-Save Version Incompatible" error caused by writing the save-format version (from the
JSON `Version` field) into the meta.

**Switch/PS4 manifest derivation:** both writers re-derive the manifest index from the
`savedataNN.hg` data file name, so the correct companion is written even if the caller
passes a different `metaIndex`. Game-save manifests preserve or inherit the base version
from an existing file or a sibling manifest, and take the higher of the existing and
derived format. The account manifest (index 0) is special:

- Switch account manifest: only the decompressed size is patched, at offsets 8 and 36; all
  other bytes are preserved.
- PS4 account manifest: the format is read from a sibling game-save manifest (the account
  JSON uses a different version numbering) and only the header fields plus the sizes at
  offsets 8 and 36 are updated.

Worlds Part I/II extensions are also written for PS4 manifests (slot ID at 300, timestamp
at 308, format copy at 312, difficulty tag at 316 for format 2004).

**`ExtractMetaInfo` game-mode fallback chain:** when `ActiveContext` is `"Season"`, read
`ExpeditionContext.GameMode`; otherwise read `BaseContext.GameMode`. If neither yields a
value, read `PlayerStateData.PresetGameMode` (string or integer, ignoring `"Unspecified"`).
Finally, derive the game mode from `DifficultyState.Preset.DifficultyPresetType` (for
example `Normal` -> 2 -> Normal mode 1). `SaveName` comes from `CommonStateData.SaveName`,
`SaveSummary` from `PlayerStateData.SaveSummary`, and `TotalPlayTime` from
`CommonStateData.TotalPlayTime`.

---

### BinaryIO

| | |
|---|---|
| File | `IO/BinaryIO.cs` |
| Purpose | Low-level binary I/O utilities for little-endian integers and Base64 |

A static helper class used throughout the IO layer.

| Method | Description |
|--------|-------------|
| `ReadInt32LE(stream)` | Read 32-bit little-endian integer |
| `WriteInt32LE(stream, value)` | Write 32-bit little-endian integer |
| `ReadInt64LE(stream)` | Read 64-bit little-endian integer |
| `WriteInt64LE(stream, value)` | Write 64-bit little-endian integer |
| `ReadAllBytes(stream)` | Read all remaining bytes |
| `ReadFileBytes(path)` | Read entire file |
| `ReadFully(stream, span)` | Read exactly N bytes into a span; throws on short read |
| `ReadFully(stream, buffer, offset, count)` | Array/offset/count overload; throws on short read |
| `Base64Encode(bytes)` / `Base64Decode(string)` | Base64 codec |

---

### LZ4 Compression Classes

NMS uses LZ4 fast compression for save data. The project includes a native C# LZ4
implementation (no external dependencies) with multiple stream wrappers for different use
cases.

#### Lz4Compressor

| | |
|---|---|
| File | `IO/Lz4Compressor.cs` |
| Purpose | Core LZ4 compress/decompress algorithm |

Static class with the raw compression engine, used directly for raw LZ4 blocks by Xbox
AccountData/Settings and PS4 `memory.dat` slots. Constants: `MinMatch = 4`,
`HashLog = 16`, `HashTableSize = 65536`, `MaxInputSize = 0x7E000000`.

| Method | Description |
|--------|-------------|
| `MaxCompressedLength(inputLength)` | Calculate worst-case output buffer size |
| `Compress(source, sourceOffset, sourceLength, dest, destOffset, maxDestLength)` | Compress; returns bytes written |
| `Decompress(source, sourceOffset, sourceLength, dest, destOffset, destLength)` | Decompress; returns bytes written |

#### Lz4CompressorStream

| | |
|---|---|
| File | `IO/Lz4CompressorStream.cs` |
| Purpose | Write-only stream that outputs NMS LZ4 streaming blocks with 16-byte headers |

Header format: `magic(4, 0xE5A1EDFE) + compressedLen(4) + uncompressedLen(4) + padding(4)`.
Uses a 512 KB internal buffer and flushes a compressed block whenever the buffer fills.
Exposes `UncompressedSize` and `CompressedSize` properties. This is the production writer
used by `SaveFileManager.SaveToFile` and `SaveXboxSave`.

#### Lz4BufferedCompressorStream

| | |
|---|---|
| File | `IO/Lz4BufferedCompressorStream.cs` |
| Purpose | Buffered compression - accumulates all data, compresses in one raw block on dispose |

Starts with a 64 KB buffer that grows on demand. All data is held in memory until
`Dispose()` is called, at which point it is compressed as a single raw LZ4 block. The
output has no 16-byte header. Currently unused: it has no production or test callers.

#### Lz4ChunkedCompressorStream

| | |
|---|---|
| File | `IO/Lz4ChunkedCompressorStream.cs` |
| Purpose | Chunked LZ4 compression with 1 MB blocks and 8-byte headers (HGSAVEV2) |

Header format: `uncompressedLen(4) + compressedLen(4)`. Uses 1 MB blocks. This is the
HGSAVEV2 format used by Xbox/Microsoft saves between versions 4.52 and 5.00. The caller must
prepend the `"HGSAVEV2\0"` header before the first block, and for strict compatibility the
trailing NUL terminator should be compressed as a separate 1-byte chunk; this class does
not do that automatically. Worlds Part I 5.00+ Xbox uses standard NMS LZ4 streaming
(`Lz4CompressorStream`) instead. Currently unused: no production or test callers.

#### Lz4DecompressorStream

| | |
|---|---|
| File | `IO/Lz4DecompressorStream.cs` |
| Purpose | Read-only stream that decompresses raw LZ4 blocks on the fly |

Constructor takes an inner stream and optional expected uncompressed size (0 = dynamic
sizing). Dynamic buffers start at 1 MB and grow in 1 MB steps. Has a safety limit of 256 MB
maximum decompressed output. Reads and decompresses blocks incrementally as callers read
from the stream. Used by the fast detection helpers and by Xbox AccountData/Settings and
plain-block reads.

---

## Platform Abstraction Summary

| Concern | Steam/GOG | Xbox Game Pass | PlayStation 4 | Switch |
|---------|-----------|---------------|---------------|--------|
| File layout | Individual `.hg` files | `containers.index` + GUID blob dirs | `memory.dat` monolith, or `savedata{NN}.hg` streaming files | Individual `savedata{NN}.hg` files |
| Compression | LZ4 (16-byte header blocks) | Read: HGSAVEV2 (8-byte frames), NMS streaming (16-byte header blocks) or raw/plain LZ4. Write: NMS streaming; AccountData/Settings raw LZ4 | Homebrew `memory.dat`: raw LZ4 block. SaveWizard: pre-decompressed JSON. HTOS/NOMANSKY `.hg`: plain JSON | LZ4 (16-byte header blocks) |
| Encryption | XXTEA meta files | None (platform handles it) | None | None |
| Meta format | `mf_saveN.hg` (encrypted), one per data file | Blob meta plus per-slot entries in `containers.index` | Per-slot entries embedded in `memory.dat`, plus `manifest{NN}.hg` companions for streaming saves | `manifest{NN}.hg` plaintext |
| Manager class | `SaveFileManager` | `ContainersIndexManager` | `MemoryDatManager` | `SaveFileManager` |

---

## Xbox Game Pass Pipeline

Xbox saves are the most involved platform because all data lives in GUID-named blob files
tracked by `containers.index`.

**Read:** `SaveFileManager.LoadXboxSave(containersIndexPath, saveIdentifier)` parses the
index, resolves the slot blob with `ContainersIndexManager.LoadXboxSave(slotInfo)` and
parses the resulting JSON. `ContainersIndexManager` detects the blob format from its first
bytes:

- `HGSAVEV2` (post-Omega 4.52-5.00): 9-byte header then frames of
  `decompressedSize(4) + compressedSize(4) + LZ4 data`. Both passes collect the frames
  before decompressing.
- `0xE5A1EDFE` NMS streaming (Worlds Part I 5.00+): 16-byte block headers, two-pass
  decompression.
- Otherwise: plain JSON (returned as-is when the content starts with `{`) or a raw LZ4
  block (AccountData/Settings) decompressed through `Lz4DecompressorStream`.

**Write:** `SaveFileManager.SaveXboxSave(containersIndexPath, slotIdentifier, data)`:

1. Parses the full index with `ParseContainersIndexFull`.
2. Serialises the JSON, appends a NUL terminator and compresses with `Lz4CompressorStream`
   (NMS streaming format).
3. Reads the existing meta blob, or creates a 24-byte placeholder.
4. Calls `ContainersIndexManager.WriteXboxSave(slotInfo, compressedData, metaData)`, which
   deletes the old blobs, writes new GUID-named data/meta files (preserving the cloud/sync
   GUIDs), writes a new `container.N` and updates the cached paths on `slotInfo`.
5. Updates `LastModified` and rewrites `containers.index` with sync state 2.
6. Returns the updated `XboxSlotInfo`; the caller must refresh any cached blob paths.

**AccountData and Settings:** `SaveFileManager.SaveXboxAccountData` compresses the JSON as a
raw LZ4 block with `Lz4Compressor.Compress` (not NMS streaming) and updates the account meta
blob. Account meta is 20 bytes: version 1 at offset 0, 12 bytes of padding, and the
decompressed size at offset 16. When no meta exists, a minimal one is created.

In the UI, `MainForm.OnSave` routes Xbox saves through `SaveXboxSave` (for the selected
auto or manual entry) and `SaveXboxAccountData`, then updates its cached file path from the
returned `XboxSlotInfo` before repopulating the file combo.

---

## PS4 and Switch Streaming Files

PS4 HTOS and Switch saves use per-file layouts rather than the monolith:

- `savedata00.hg` is the settings file on Switch and the account data file on PS4;
  `savedata02.hg` is slot 0 auto, `savedata03.hg` is slot 0 manual, and so on.
- Each data file has a `manifestNN.hg` companion whose index is derived from the data file
  name by `MetaFileWriter.WriteSwitchMeta` / `WritePlaystationStreamingMeta`.
- PS4 SaveWizard exports use a `NOMANSKY` header: a 0x70-byte preamble with the JSON size
  at offset 0x5C and UTF-8 JSON after it. `SaveNomanSkyFile` preserves the original header
  and updates only the size field, so the editor can write these files back safely.
- PS4 HTOS files (no `memory.dat`) are plain, uncompressed JSON. `MainForm.OnSave` detects
  this case and passes `compress: false`; writing LZ4 there would produce a file the PS4
  game cannot read.
- `accountdata.hg` is always written uncompressed with a NUL terminator. For PS4 HTOS the
  account manifest is rewritten whenever the account file changes, because the PS4 system
  reads the size from the manifest and can reject a stale one.

`MainForm.OnSave` tracks the original NOMANSKY path separately (`_ps4NomanSkyPath`) so that
Save As can copy the updated file to its new location without losing the header template.

---

## Backup and Restore

The backup subsystem lives in `SaveFileManager` and is driven by the UI.

- `ResolveBackupRoot` picks the first usable root: the configured
  `AppConfig.BackupDirectory`, then an EXE-relative `Save Backups` folder, then
  `%TEMP%\NMSE\Save Backups`. Directory creation failures fall through to the next option,
  so backups are never silently skipped.
- `BackupSaveDirectory` creates `{dirName}_{yyyyMMdd_HHmmss}.zip` and keeps at most ten
  backups per directory name. The file set depends on the detected platform: Xbox backs up
  all files (GUID blobs and `containers.index`); PS4 with a monolithic `memory.dat` backs
  up `.hg`, `.dat` and `meta.json` files; every other platform backs up `.hg` files and
  `meta.json`. Subdirectories that cannot be enumerated are skipped so a backup never
  fails on protected folders.
- `FindExistingBackupRoots` returns existing roots in priority order without creating them
  and de-duplicates normalised paths (Windows comparisons are case-insensitive).
  `FindBackupZips` scans every root, returns each ZIP at most once and sorts newest first.
- `GetBackupEntryNames` lists full entry names. `BackupContainsFile` matches the final path
  segment of every entry case-insensitively, so nested layouts are found.
- `RestoreFileFromBackup` restores one entry over a destination file.
  `RestoreBackupToDirectory` restores every entry into a destination directory, preserving
  structure and skipping any entry whose resolved path escapes the destination (a zip
  traversal guard). Both ignore missing entries safely.

UI flow: `MainForm.OnRestoreBackup` ("Restore (All)") and `OnRestoreBackupSingle`
("Restore (Single)") both call `FindExistingBackupRoots` and `FindBackupZips`, then show
`BackupPickerDialog`. The dialog lists backups newest first with creation time, size and
location in a non-selectable-header list, preselects the newest and exposes the chosen path
as `SelectedZipPath`. After a restore, `ReloadCurrentSave` reloads using the correct
pipeline for the platform (Xbox blob, `memory.dat` sub-slot, or file). Backups are only
taken in `MainForm.OnSave`; slot copy, move and swap operations do not create backups.

---

## Save As and Reload

- `OnSaveAs` shows a save dialog, updates `_currentFilePath` and then runs the normal
  `OnSave` flow, so all platform-specific handling (Xbox blobs excepted, since those are
  tracked by identifier) applies to the new path.
- `OnReload` repopulates the slot lists and reloads the currently tracked file.
- `ReloadCurrentSave` is used after a backup restore and dispatches to `LoadXboxSaveData`,
  `LoadPS4MemoryDatSaveData` or `LoadSaveData` based on the detected platform and the
  tracked container/`memory.dat` paths.

---

## Cross-Platform Design Goals

The IO layer is deliberately portable and AOT-friendly:

- No p/invoke and no Windows-only APIs in `IO/`.
- No external NuGet packages: LZ4 (`Lz4Compressor`), TEA/XXTEA (`MetaCrypto`) and
  SpookyHash V2 (`SpookyHashV2`) are native C# implementations.
- Encoding is handled explicitly per format (Latin-1 for NMS JSON payloads, UTF-8 for
  NOMANSKY files and meta file strings).

---

## Context Transform System

After loading a save, `SaveFileManager.RegisterContextTransforms` registers dynamic path
transforms on the root `JsonObject`, but only when the root does not already contain a
literal `PlayerStateData` / `SpawnStateData` key. Saves without a context split are left
alone.

When application code accesses `root.GetValue("PlayerStateData")`, the transform resolves
in this order:

1. `ExpeditionContext.PlayerStateData`, if it exists and either
   `BaseContext.PlayerStateData` is absent or `ActiveContext` equals `"Season"`.
2. `BaseContext.PlayerStateData`, if it exists.
3. The literal `PlayerStateData` key.

The same rules apply to `SpawnStateData`. Note that the transform is not keyed off
`ActiveContext` containing `"BaseContext"` or `"ExpeditionContext"`; it decides from the
presence of the context objects and the `"Season"` value. Logic and Panel code can use
simple paths like `"PlayerStateData.Health"` without knowing which context is active; the
transform is transparent and applied automatically by `JsonObject.GetValue`.

### SaveContext

`SaveFileManager.TryDetectActiveContext(JsonObject)` sets
`Core.SaveContext.IsExpeditionSave` to true when the parsed save's `ActiveContext` is
`"Season"`, and resets it otherwise. Panels and Core helpers read
`SaveContext.IsExpeditionSave` to decide whether to use `BaseContext` or
`ExpeditionContext` as the parent key. `MainForm` calls it after every load and resets the
flag when the form closes.
