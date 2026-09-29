namespace NMSE.IO;

/// <summary>
/// Reads and writes Xbox XGameSaveFiles (xgs) save containers.
///
/// Modern Xbox (PC) / Game Pass saves use the GDK XGameSaveFiles API, which stores plain
/// files instead of the legacy wgs container/blob system:
///
///   xgs/{HexXuid}_{SCID}/AccountData/{data,meta}
///   xgs/{HexXuid}_{SCID}/Slot1Auto/{data,meta}
///   xgs/{HexXuid}_{SCID}/Slot1Manual/{data,meta}
///
/// The slot identity is the folder name (for example "Slot1Manual"), and the
/// <c>data</c> / <c>meta</c> payloads are byte-identical to the corresponding wgs blob
/// files, so the shared <see cref="XboxBlobCodec"/> decompression path applies.
/// </summary>
public static class XgsSaveManager
{
	/// <summary>Name of the save data file inside an xgs slot folder.</summary>
	public const string DataFileName = "data";
	/// <summary>Name of the metadata file inside an xgs slot folder.</summary>
	public const string MetaFileName = "meta";

	/// <summary>
	/// Returns <c>true</c> when the directory is an xgs container: it holds identifier
	/// folders (for example "Slot1Auto" or "AccountData") with a <c>data</c> file inside.
	/// wgs directories (containing containers.index) are excluded.
	/// </summary>
	/// <param name="directory">The directory to inspect.</param>
	public static bool IsXgsContainerDirectory(string directory)
	{
		if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
			return false;

		// wgs containers are identified by containers.index.
		if (File.Exists(Path.Combine(directory, "containers.index")))
			return false;

		foreach (var subDirectory in Directory.EnumerateDirectories(directory))
		{
			if (File.Exists(Path.Combine(subDirectory, DataFileName)))
				return true;
		}
		return false;
	}

	/// <summary>
	/// Enumerates the slot folders of an xgs container.
	/// </summary>
	/// <param name="containerDirectory">The xgs container directory.</param>
	/// <returns>Slot descriptors keyed by folder name (for example "Slot1Manual").</returns>
	public static Dictionary<string, XboxSlotInfo> EnumerateSlots(string containerDirectory)
	{
		var slots = new Dictionary<string, XboxSlotInfo>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrEmpty(containerDirectory) || !Directory.Exists(containerDirectory))
			return slots;

		foreach (var slotDirectory in Directory.EnumerateDirectories(containerDirectory))
		{
			string identifier = Path.GetFileName(slotDirectory);
			string dataPath = Path.Combine(slotDirectory, DataFileName);
			if (!File.Exists(dataPath))
				continue;

			string metaPath = Path.Combine(slotDirectory, MetaFileName);
			slots[identifier] = new XboxSlotInfo
			{
				Identifier = identifier,
				BlobDirectoryPath = slotDirectory,
				DataFilePath = dataPath,
				MetaFilePath = File.Exists(metaPath) ? metaPath : null,
				LastModified = new DateTimeOffset(File.GetLastWriteTimeUtc(dataPath), TimeSpan.Zero),
			};
		}
		return slots;
	}

	/// <summary>
	/// Returns the folder path for a slot identifier inside an xgs container.
	/// </summary>
	/// <param name="containerDirectory">The xgs container directory.</param>
	/// <param name="identifier">The slot identifier (for example "Slot1Manual").</param>
	public static string GetSlotDirectory(string containerDirectory, string identifier)
		=> Path.Combine(containerDirectory, identifier);

	/// <summary>
	/// Loads a save from an xgs slot folder.
	/// </summary>
	/// <param name="slotInfo">A slot descriptor from <see cref="EnumerateSlots"/>.</param>
	/// <returns>The decompressed JSON string, or <c>null</c> if the data file cannot be read.</returns>
	public static string? LoadSave(XboxSlotInfo slotInfo)
	{
		if (slotInfo.DataFilePath == null || !File.Exists(slotInfo.DataFilePath))
			return null;

		try
		{
			return XboxBlobCodec.Decompress(File.ReadAllBytes(slotInfo.DataFilePath));
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Reads the raw meta bytes of an xgs slot.
	/// </summary>
	/// <param name="slotInfo">A slot descriptor from <see cref="EnumerateSlots"/>.</param>
	/// <returns>The meta bytes, or <c>null</c> when no meta file exists.</returns>
	public static byte[]? LoadMeta(XboxSlotInfo slotInfo)
	{
		if (slotInfo.MetaFilePath == null || !File.Exists(slotInfo.MetaFilePath))
			return null;

		return File.ReadAllBytes(slotInfo.MetaFilePath);
	}

	/// <summary>
	/// Writes save data (and optionally meta) back to an xgs slot folder.
	/// The data file is replaced in place, keeping the folder name and the meta identity
	/// untouched so Xbox cloud sync continues to recognise the container.
	/// </summary>
	/// <param name="slotInfo">A slot descriptor from <see cref="EnumerateSlots"/>.</param>
	/// <param name="compressedData">The compressed save payload.</param>
	/// <param name="metaData">The meta bytes to write, or <c>null</c> to leave the meta untouched.</param>
	public static void WriteSave(XboxSlotInfo slotInfo, byte[] compressedData, byte[]? metaData)
	{
		if (slotInfo.DataFilePath == null)
			throw new InvalidOperationException("xgs slot has no data file path.");

		Directory.CreateDirectory(slotInfo.BlobDirectoryPath);
		WriteAtomic(slotInfo.DataFilePath, compressedData);

		if (metaData is { Length: > 0 } && slotInfo.MetaFilePath != null)
		{
			if (!File.Exists(slotInfo.MetaFilePath) ||
				!File.ReadAllBytes(slotInfo.MetaFilePath).AsSpan().SequenceEqual(metaData))
			{
				WriteAtomic(slotInfo.MetaFilePath, metaData);
			}
		}
	}

	/// <summary>
	/// Writes a file via a temporary sibling and an atomic replace, so an interrupted
	/// write cannot leave a truncated save behind.
	/// </summary>
	private static void WriteAtomic(string path, byte[] bytes)
	{
		string tmp = path + ".nmse.tmp";
		File.WriteAllBytes(tmp, bytes);
		File.Move(tmp, path, overwrite: true);
	}
}
