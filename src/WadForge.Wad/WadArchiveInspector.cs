using System.Buffers.Binary;
using System.Text;
using WadForge.Core;

namespace WadForge.Wad;

public static class WadArchiveInspector
{
    private const int HeaderSize = 12;
    private const int DirectoryEntrySize = 32;

    public static bool IsSupportedPath(string path)
    {
        return string.Equals(
            Path.GetExtension(path),
            ".wad",
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryInspect(
        string path,
        out WadInspectionResult? result,
        out string error)
    {
        result = null;
        error = string.Empty;

        try
        {
            if (!File.Exists(path))
            {
                error = "The WAD file does not exist.";
                return false;
            }

            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length < HeaderSize)
            {
                error = "The file is too small to contain a valid WAD header.";
                return false;
            }

            Span<byte> header = stackalloc byte[HeaderSize];
            stream.ReadExactly(header);

            string magic = Encoding.ASCII.GetString(header[..4]);

            WadFormat format = magic switch
            {
                "WAD2" => WadFormat.Wad2,
                "WAD3" => WadFormat.Wad3,
                _ => throw new InvalidDataException(
                    $"Unsupported WAD signature: {magic}")
            };

            int lumpCount = BinaryPrimitives.ReadInt32LittleEndian(header[4..8]);
            int directoryOffset = BinaryPrimitives.ReadInt32LittleEndian(header[8..12]);

            if (lumpCount < 0)
            {
                error = "The WAD directory contains a negative lump count.";
                return false;
            }

            if (directoryOffset < HeaderSize)
            {
                error = "The WAD directory offset is invalid.";
                return false;
            }

            long directorySize = checked((long)lumpCount * DirectoryEntrySize);
            long directoryEnd = checked((long)directoryOffset + directorySize);

            if (directoryEnd > stream.Length)
            {
                error = "The WAD directory extends beyond the end of the file.";
                return false;
            }

            result = new WadInspectionResult(
                format,
                lumpCount,
                directoryOffset,
                stream.Length);

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
