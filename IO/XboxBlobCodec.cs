using System.Text;

namespace NMSE.IO;

/// <summary>
/// Compression envelope used by an Xbox save blob. The legacy wgs blob files and
/// the XGameSaveFiles (xgs) <c>data</c> files share the same formats.
/// </summary>
internal enum XboxBlobFormat
{
	/// <summary>HGSAVEV2 multi-frame LZ4 ("HGSAVEV2\0" header, post-Omega Xbox saves).</summary>
	Hgsv2,
	/// <summary>NMS streaming LZ4 (0xE5A1EDFE magic per chunk).</summary>
	NmsLz4,
	/// <summary>Plain JSON or a single raw LZ4 block (AccountData/Settings blobs).</summary>
	Raw
}

/// <summary>
/// Decompression helpers shared by the wgs (containers.index) and xgs
/// (XGameSaveFiles) Xbox save readers.
/// </summary>
internal static class XboxBlobCodec
{
	// HGSAVEV2 header: "HGSAVEV2\0" (9 bytes), used by post-Omega Xbox/Microsoft saves
	private static readonly byte[] Hgsv2Header = Encoding.ASCII.GetBytes("HGSAVEV2").Concat(new byte[] { 0x00 }).ToArray();

	private static readonly byte[] Lz4Magic = { 0xE5, 0xA1, 0xED, 0xFE };

	/// <summary>
	/// Detects the compression envelope of a blob from its leading bytes.
	/// </summary>
	/// <param name="data">The blob bytes (or at least the leading bytes).</param>
	/// <returns>The detected <see cref="XboxBlobFormat"/>.</returns>
	public static XboxBlobFormat DetectFormat(ReadOnlySpan<byte> data)
	{
		if (IsHgsv2Header(data)) return XboxBlobFormat.Hgsv2;
		if (data.Length >= 4 && IsNmsLz4Header(data)) return XboxBlobFormat.NmsLz4;
		return XboxBlobFormat.Raw;
	}

	/// <summary>
	/// Decompresses blob bytes to the stored Latin-1 JSON text.
	/// </summary>
	/// <param name="data">The compressed (or plain) blob bytes.</param>
	/// <returns>The decompressed JSON text.</returns>
	public static string Decompress(byte[] data)
	{
		return DetectFormat(data) switch
		{
			XboxBlobFormat.Hgsv2 => DecompressHgsv2(data),
			XboxBlobFormat.NmsLz4 => DecompressNmsLz4(data),
			_ => ReadPlainOrSingleLz4(data)
		};
	}

	private static bool IsNmsLz4Header(ReadOnlySpan<byte> header)
	{
		return header.Length >= 4 &&
			   header[0] == Lz4Magic[0] && header[1] == Lz4Magic[1] &&
			   header[2] == Lz4Magic[2] && header[3] == Lz4Magic[3];
	}

	private static bool IsHgsv2Header(ReadOnlySpan<byte> header)
	{
		if (header.Length < Hgsv2Header.Length) return false;
		for (int i = 0; i < Hgsv2Header.Length; i++)
		{
			if (header[i] != Hgsv2Header[i]) return false;
		}
		return true;
	}

	/// <summary>
	/// Decompress HGSAVEV2 format: "HGSAVEV2\0" header followed by multi-frame LZ4.
	/// Each frame: [decompressedSize(4 LE)] [compressedSize(4 LE)] [LZ4 data].
	/// </summary>
	private static string DecompressHgsv2(byte[] data)
	{
		var latin1 = Encoding.GetEncoding(28591);

		// First pass: calculate total decompressed size
		int totalSize = 0;
		int pos = Hgsv2Header.Length;
		while (pos + 8 <= data.Length)
		{
			int decompressedLen = ReadInt32LE(data, pos);
			int compressedLen = ReadInt32LE(data, pos + 4);
			if (decompressedLen < 0 || compressedLen < 0 ||
				decompressedLen > 256 * 1024 * 1024 || compressedLen > 256 * 1024 * 1024 ||
				pos + 8 + compressedLen > data.Length) break;

			totalSize += decompressedLen;
			pos += 8 + compressedLen;
		}

		// Second pass: decompress all frames
		byte[] result = new byte[totalSize];
		int writePos = 0;
		pos = Hgsv2Header.Length;

		while (pos + 8 <= data.Length && writePos < totalSize)
		{
			int decompressedLen = ReadInt32LE(data, pos);
			int compressedLen = ReadInt32LE(data, pos + 4);
			if (decompressedLen <= 0 || compressedLen <= 0 || pos + 8 + compressedLen > data.Length) break;

			int decompressed = Lz4Compressor.Decompress(data, pos + 8, compressedLen, result, writePos, decompressedLen);
			writePos += decompressed;
			pos += 8 + compressedLen;
		}

		return latin1.GetString(result, 0, writePos);
	}

	/// <summary>
	/// Decompress NMS streaming LZ4: [magic(4)] [compressedSize(4 LE)] [uncompressedSize(4 LE)]
	/// [4 bytes reserved] [LZ4 data], repeated per chunk.
	/// </summary>
	private static string DecompressNmsLz4(byte[] data)
	{
		var latin1 = Encoding.GetEncoding(28591);

		// First pass: calculate total size
		int totalSize = 0;
		int pos = 0;
		while (pos + 16 <= data.Length && IsNmsLz4Header(data.AsSpan(pos, 4)))
		{
			int compressedLen = ReadInt32LE(data, pos + 4);
			int uncompressedLen = ReadInt32LE(data, pos + 8);
			if (compressedLen < 0 || uncompressedLen < 0 || pos + 16 + compressedLen > data.Length) break;

			totalSize += uncompressedLen;
			pos += 16 + compressedLen;
		}

		// Second pass: decompress
		byte[] result = new byte[totalSize];
		int writePos = 0;
		pos = 0;

		while (pos + 16 <= data.Length && IsNmsLz4Header(data.AsSpan(pos, 4)))
		{
			int compressedLen = ReadInt32LE(data, pos + 4);
			int uncompressedLen = ReadInt32LE(data, pos + 8);
			if (compressedLen < 0 || uncompressedLen < 0 || pos + 16 + compressedLen > data.Length) break;

			int decompressed = Lz4Compressor.Decompress(data, pos + 16, compressedLen, result, writePos, uncompressedLen);
			writePos += decompressed;
			pos += 16 + compressedLen;
		}

		return latin1.GetString(result, 0, writePos);
	}

	/// <summary>
	/// Reads a blob stored as plain JSON or as a single raw LZ4 block
	/// (used by Xbox AccountData/Settings blobs).
	/// </summary>
	private static string ReadPlainOrSingleLz4(byte[] data)
	{
		var latin1 = Encoding.GetEncoding(28591);

		// If data looks like plain JSON (starts with '{' or whitespace + '{'), return as-is
		for (int i = 0; i < data.Length; i++)
		{
			byte b = data[i];
			if (b == '{') return latin1.GetString(data, 0, data.Length);
			if (b != ' ' && b != '\t' && b != '\r' && b != '\n' && b != 0) break;
		}

		// Try raw LZ4 block decompression (Xbox AccountData/Settings blobs).
		// These blobs are stored as raw LZ4 without the NMS streaming header (0xE5A1EDFE).
		try
		{
			using var ms = new MemoryStream(data, 0, data.Length, writable: false);
			using var decompressor = new Lz4DecompressorStream(ms, uncompressedSize: 0);
			using var result = new MemoryStream();
			decompressor.CopyTo(result);
			return latin1.GetString(result.GetBuffer(), 0, (int)result.Length);
		}
		catch
		{
			// LZ4 decompression failed - return as uncompressed
			return latin1.GetString(data, 0, data.Length);
		}
	}

	private static int ReadInt32LE(byte[] data, int offset)
	{
		return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
	}
}
