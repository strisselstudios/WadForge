using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TrenchBroom.Companion.Core;

public sealed record CompanionDuskCompilePreparationResult(
    int Wad2ConvertedCount,
    int Wad3PreservedCount,
    IReadOnlyList<string> OrderedWadNames,
    string CacheWadDirectory);

public static class CompanionDuskCompilePreparationService
{
    private static readonly Regex WadPropertyPattern =
        new(
            @"(?m)^""wad""\s+""(?<value>(?:\\.|[^""])*)""\s*$",
            RegexOptions.CultureInvariant);

    private sealed record WadEntry(
        int FilePosition,
        int DiskSize,
        int FullSize,
        byte Type,
        byte Compression,
        byte[] Name);

    private sealed record OutputWadEntry(
        int FilePosition,
        int DiskSize,
        int FullSize,
        byte[] Name);

    public static CompanionDuskCompilePreparationResult PrepareHalfLifeCompile(
        string compileMapPath,
        string projectWadDirectory,
        string cacheWadDirectory,
        string palettePath)
    {
        string mapPath =
            RequireFile(
                compileMapPath,
                "The exported compile map could not be found.");

        string projectWads =
            RequireDirectory(
                projectWadDirectory,
                "The project WAD directory could not be found.");

        string paletteFile =
            RequireFile(
                palettePath,
                "The DUSK project palette could not be found.");

        byte[] palette =
            File.ReadAllBytes(
                paletteFile);

        if (palette.Length !=
            768)
        {
            throw new InvalidDataException(
                $"The DUSK project palette must contain exactly 768 bytes: {paletteFile}");
        }

        string mapText =
            File.ReadAllText(
                mapPath);

        MatchCollection matches =
            WadPropertyPattern.Matches(
                mapText);

        if (matches.Count !=
            1)
        {
            throw new InvalidDataException(
                $"Expected exactly one worldspawn WAD property in the exported compile map, but found {matches.Count}.");
        }

        string rawWads =
            UnescapeMapValue(
                matches[0]
                    .Groups["value"]
                    .Value);

        List<string> orderedSourceWads =
            ResolveProjectWads(
                rawWads,
                projectWads);

        if (orderedSourceWads.Count ==
            0)
        {
            throw new InvalidDataException(
                "The exported compile map does not reference any managed project WADs.");
        }

        string cacheDirectory =
            Path.GetFullPath(
                cacheWadDirectory);

        string stagingDirectory =
            cacheDirectory +
            ".staging-" +
            Guid.NewGuid().ToString(
                "N");

        int wad2Converted =
            0;

        int wad3Preserved =
            0;

        List<string> orderedNames =
            new();

        try
        {
            Directory.CreateDirectory(
                stagingDirectory);

            StringBuilder manifest =
                new();

            manifest.AppendLine(
                "TrenchBroom Companion DUSK HLBSP compile cache");

            manifest.AppendLine(
                "WAD3 archives are preserved byte-for-byte.");

            manifest.AppendLine(
                "WAD2 miptextures keep their indexed pixels and receive the DUSK project global palette.");

            manifest.AppendLine();

            foreach (string sourceWad in
                     orderedSourceWads)
            {
                string name =
                    Path.GetFileName(
                        sourceWad);

                string destination =
                    Path.Combine(
                        stagingDirectory,
                        name);

                string magic =
                    ReadWadMagic(
                        sourceWad);

                if (string.Equals(
                        magic,
                        "WAD3",
                        StringComparison.Ordinal))
                {
                    File.Copy(
                        sourceWad,
                        destination,
                        overwrite: true);

                    wad3Preserved++;

                    manifest.AppendLine(
                        $"{name}: WAD3 preserved");

                    orderedNames.Add(
                        name);

                    continue;
                }

                if (string.Equals(
                        magic,
                        "WAD2",
                        StringComparison.Ordinal))
                {
                    int convertedTextures =
                        ConvertWad2ToWad3(
                            sourceWad,
                            destination,
                            palette);

                    wad2Converted++;

                    manifest.AppendLine(
                        $"{name}: WAD2 cached as WAD3-compatible ({convertedTextures} miptextures)");

                    orderedNames.Add(
                        name);

                    continue;
                }

                throw new InvalidDataException(
                    $"Unsupported WAD signature '{magic}' in '{sourceWad}'.");
            }

            File.WriteAllText(
                Path.Combine(
                    stagingDirectory,
                    "manifest.txt"),
                manifest.ToString(),
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            if (Directory.Exists(
                    cacheDirectory))
            {
                Directory.Delete(
                    cacheDirectory,
                    recursive: true);
            }

            string? cacheParent =
                Path.GetDirectoryName(
                    cacheDirectory);

            if (!string.IsNullOrWhiteSpace(
                    cacheParent))
            {
                Directory.CreateDirectory(
                    cacheParent);
            }

            Directory.Move(
                stagingDirectory,
                cacheDirectory);
        }
        catch
        {
            if (Directory.Exists(
                    stagingDirectory))
            {
                Directory.Delete(
                    stagingDirectory,
                    recursive: true);
            }

            throw;
        }

        string shortWadValue =
            string.Join(
                ";",
                orderedNames);

        string replacement =
            "\"wad\" \"" +
            EscapeMapValue(
                shortWadValue) +
            "\"";

        string rewrittenMap =
            WadPropertyPattern.Replace(
                mapText,
                replacement,
                count: 1);

        WriteTextAtomically(
            mapPath,
            rewrittenMap);

        return new CompanionDuskCompilePreparationResult(
            wad2Converted,
            wad3Preserved,
            orderedNames,
            cacheDirectory);
    }

    private static List<string> ResolveProjectWads(
        string wadValue,
        string projectWadDirectory)
    {
        List<string> ordered =
            new();

        foreach (string reference in
                 wadValue.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            string platformReference =
                reference
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar)
                    .Replace(
                        '\\',
                        Path.DirectorySeparatorChar);

            string name =
                Path.GetFileName(
                    platformReference);

            if (string.IsNullOrWhiteSpace(
                    name))
            {
                throw new InvalidDataException(
                    $"Could not determine a WAD filename from '{reference}'.");
            }

            string managedPath =
                Path.Combine(
                    projectWadDirectory,
                    name);

            if (!File.Exists(
                    managedPath))
            {
                throw new FileNotFoundException(
                    $"The exported map references '{name}', but the managed project copy is missing.",
                    managedPath);
            }

            string fullManagedPath =
                Path.GetFullPath(
                    managedPath);

            if (!ordered.Contains(
                    fullManagedPath,
                    StringComparer.OrdinalIgnoreCase))
            {
                ordered.Add(
                    fullManagedPath);
            }
        }

        return ordered;
    }

    private static int ConvertWad2ToWad3(
        string sourcePath,
        string destinationPath,
        byte[] palette)
    {
        using FileStream input =
            File.Open(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        using BinaryReader reader =
            new(
                input,
                Encoding.ASCII,
                leaveOpen: true);

        string magic =
            Encoding.ASCII.GetString(
                reader.ReadBytes(
                    4));

        if (!string.Equals(
                magic,
                "WAD2",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Expected WAD2 input but found '{magic}': {sourcePath}");
        }

        int lumpCount =
            reader.ReadInt32();

        int directoryOffset =
            reader.ReadInt32();

        if (lumpCount <
                0 ||
            lumpCount >
                1_000_000)
        {
            throw new InvalidDataException(
                $"Invalid WAD2 lump count in '{sourcePath}'.");
        }

        long directoryLength =
            (long)lumpCount *
            32L;

        if (directoryOffset <
                12 ||
            directoryOffset +
                directoryLength >
            input.Length)
        {
            throw new InvalidDataException(
                $"Invalid WAD2 directory in '{sourcePath}'.");
        }

        input.Position =
            directoryOffset;

        List<WadEntry> sourceEntries =
            new();

        for (int index = 0;
             index < lumpCount;
             index++)
        {
            int filePosition =
                reader.ReadInt32();

            int diskSize =
                reader.ReadInt32();

            int fullSize =
                reader.ReadInt32();

            byte type =
                reader.ReadByte();

            byte compression =
                reader.ReadByte();

            reader.ReadUInt16();

            byte[] name =
                reader.ReadBytes(
                    16);

            if (name.Length !=
                16)
            {
                throw new InvalidDataException(
                    $"Unexpected end of WAD2 directory in '{sourcePath}'.");
            }

            if (filePosition <
                    0 ||
                diskSize <
                    0 ||
                fullSize <
                    0 ||
                (long)filePosition +
                    diskSize >
                input.Length)
            {
                throw new InvalidDataException(
                    $"Invalid WAD2 lump bounds in '{sourcePath}'.");
            }

            sourceEntries.Add(
                new WadEntry(
                    filePosition,
                    diskSize,
                    fullSize,
                    type,
                    compression,
                    name));
        }

        List<WadEntry> mipTextures =
            sourceEntries
                .Where(
                    entry =>
                        entry.Type ==
                        0x44)
                .ToList();

        if (mipTextures.Count ==
            0)
        {
            throw new InvalidDataException(
                $"No WAD2 miptextures were found in '{sourcePath}'.");
        }

        string temporaryPath =
            destinationPath +
            ".temporary-" +
            Guid.NewGuid().ToString(
                "N");

        try
        {
            List<OutputWadEntry> outputs =
                new();

            using (
                FileStream output =
                    File.Open(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
            using (
                BinaryWriter writer =
                    new(
                        output,
                        Encoding.ASCII,
                        leaveOpen: true))
            {
                writer.Write(
                    Encoding.ASCII.GetBytes(
                        "WAD3"));

                writer.Write(
                    0);

                writer.Write(
                    0);

                foreach (WadEntry entry in
                         mipTextures)
                {
                    if (entry.Compression !=
                        0)
                    {
                        throw new InvalidDataException(
                            $"Compressed WAD2 miptextures are not supported in '{sourcePath}'.");
                    }

                    input.Position =
                        entry.FilePosition;

                    byte[] lump =
                        reader.ReadBytes(
                            entry.DiskSize);

                    if (lump.Length !=
                            entry.DiskSize ||
                        lump.Length <
                            40)
                    {
                        throw new InvalidDataException(
                            $"A WAD2 miptexture is truncated in '{sourcePath}'.");
                    }

                    uint width =
                        BitConverter.ToUInt32(
                            lump,
                            16);

                    uint height =
                        BitConverter.ToUInt32(
                            lump,
                            20);

                    uint mip0 =
                        BitConverter.ToUInt32(
                            lump,
                            24);

                    uint mip1 =
                        BitConverter.ToUInt32(
                            lump,
                            28);

                    uint mip2 =
                        BitConverter.ToUInt32(
                            lump,
                            32);

                    uint mip3 =
                        BitConverter.ToUInt32(
                            lump,
                            36);

                    if (width ==
                            0 ||
                        height ==
                            0 ||
                        width >
                            65536 ||
                        height >
                            65536)
                    {
                        throw new InvalidDataException(
                            $"A WAD2 miptexture has invalid dimensions in '{sourcePath}'.");
                    }

                    if (mip0 <
                            40 ||
                        mip1 <=
                            mip0 ||
                        mip2 <=
                            mip1 ||
                        mip3 <=
                            mip2)
                    {
                        throw new InvalidDataException(
                            $"A WAD2 miptexture has invalid mip offsets in '{sourcePath}'.");
                    }

                    long mip3Width =
                        Math.Max(
                            1L,
                            (long)width >>
                            3);

                    long mip3Height =
                        Math.Max(
                            1L,
                            (long)height >>
                            3);

                    long mipDataEnd =
                        (long)mip3 +
                        mip3Width *
                        mip3Height;

                    if (mipDataEnd >
                        lump.Length)
                    {
                        throw new InvalidDataException(
                            $"A WAD2 miptexture extends beyond its lump in '{sourcePath}'.");
                    }

                    int filePosition =
                        checked(
                            (int)output.Position);

                    writer.Write(
                        lump,
                        0,
                        checked(
                            (int)mipDataEnd));

                    writer.Write(
                        (ushort)256);

                    writer.Write(
                        palette);

                    writer.Write(
                        (ushort)0);

                    int diskSize =
                        checked(
                            (int)output.Position -
                            filePosition);

                    outputs.Add(
                        new OutputWadEntry(
                            filePosition,
                            diskSize,
                            diskSize,
                            entry.Name));
                }

                int outputDirectoryOffset =
                    checked(
                        (int)output.Position);

                foreach (OutputWadEntry entry in
                         outputs)
                {
                    writer.Write(
                        entry.FilePosition);

                    writer.Write(
                        entry.DiskSize);

                    writer.Write(
                        entry.FullSize);

                    writer.Write(
                        (byte)0x43);

                    writer.Write(
                        (byte)0);

                    writer.Write(
                        (ushort)0);

                    writer.Write(
                        entry.Name);
                }

                output.Position =
                    4;

                writer.Write(
                    outputs.Count);

                writer.Write(
                    outputDirectoryOffset);

                writer.Flush();

                output.Flush(
                    flushToDisk: true);
            }

            File.Move(
                temporaryPath,
                destinationPath,
                overwrite: true);

            return mipTextures.Count;
        }
        finally
        {
            if (File.Exists(
                    temporaryPath))
            {
                File.Delete(
                    temporaryPath);
            }
        }
    }

    private static string ReadWadMagic(
        string wadPath)
    {
        using FileStream stream =
            File.Open(
                wadPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        byte[] header =
            new byte[4];

        int read =
            stream.Read(
                header,
                0,
                header.Length);

        if (read !=
            4)
        {
            throw new InvalidDataException(
                $"Could not read WAD header: {wadPath}");
        }

        return Encoding.ASCII.GetString(
            header);
    }

    private static string RequireFile(
        string path,
        string message)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            throw new ArgumentException(
                message,
                nameof(path));
        }

        string fullPath =
            Path.GetFullPath(
                path);

        if (!File.Exists(
                fullPath))
        {
            throw new FileNotFoundException(
                message,
                fullPath);
        }

        return fullPath;
    }

    private static string RequireDirectory(
        string path,
        string message)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            throw new ArgumentException(
                message,
                nameof(path));
        }

        string fullPath =
            Path.GetFullPath(
                path);

        if (!Directory.Exists(
                fullPath))
        {
            throw new DirectoryNotFoundException(
                $"{message} '{fullPath}'");
        }

        return fullPath;
    }

    private static string EscapeMapValue(
        string value)
    {
        return value
            .Replace(
                "\\",
                "\\\\",
                StringComparison.Ordinal)
            .Replace(
                "\"",
                "\\\"",
                StringComparison.Ordinal);
    }

    private static string UnescapeMapValue(
        string value)
    {
        return value
            .Replace(
                "\\\\",
                "\\",
                StringComparison.Ordinal)
            .Replace(
                "\\\"",
                "\"",
                StringComparison.Ordinal);
    }

    private static void WriteTextAtomically(
        string destinationPath,
        string content)
    {
        string temporaryPath =
            destinationPath +
            ".temporary-" +
            Guid.NewGuid().ToString(
                "N");

        try
        {
            File.WriteAllText(
                temporaryPath,
                content,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: true));

            File.Move(
                temporaryPath,
                destinationPath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(
                    temporaryPath))
            {
                File.Delete(
                    temporaryPath);
            }
        }
    }
}
