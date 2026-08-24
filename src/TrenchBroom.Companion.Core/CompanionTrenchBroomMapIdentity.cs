using System;
using System.IO;
using System.Text;

namespace TrenchBroom.Companion.Core;

public sealed record CompanionTrenchBroomMapIdentity(
    string GameName,
    string MapFormat);

public static class CompanionTrenchBroomMapIdentityService
{
    private const string GameHeaderPrefix =
        "// Game:";

    private const string FormatHeaderPrefix =
        "// Format:";

    public static CompanionTrenchBroomMapIdentity GetRequired(
        string gameId)
    {
        if (string.Equals(
                gameId,
                CompanionGameProfiles.Dusk.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            return new CompanionTrenchBroomMapIdentity(
                "DUSK",
                "Valve");
        }

        if (string.Equals(
                gameId,
                CompanionGameProfiles.Quake.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            return new CompanionTrenchBroomMapIdentity(
                "Quake",
                "Valve");
        }

        if (string.Equals(
                gameId,
                CompanionGameProfiles.HalfLife.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            return new CompanionTrenchBroomMapIdentity(
                "Half-Life",
                "Valve");
        }

        throw new ArgumentException(
            $"Unsupported Companion game profile '{gameId}'.",
            nameof(gameId));
    }

    public static string BuildHeader(
        string gameId)
    {
        CompanionTrenchBroomMapIdentity identity =
            GetRequired(
                gameId);

        return
            $"{GameHeaderPrefix} {identity.GameName}\r\n" +
            $"{FormatHeaderPrefix} {identity.MapFormat}\r\n";
    }

    public static bool EnsureMapIdentity(
        string mapPath,
        string gameId)
    {
        if (string.IsNullOrWhiteSpace(
                mapPath))
        {
            throw new ArgumentException(
                "A map path is required.",
                nameof(mapPath));
        }

        string fullMapPath =
            Path.GetFullPath(
                mapPath);

        if (!File.Exists(
                fullMapPath))
        {
            throw new FileNotFoundException(
                "The map file does not exist.",
                fullMapPath);
        }

        CompanionTrenchBroomMapIdentity identity =
            GetRequired(
                gameId);

        byte[] originalBytes =
            File.ReadAllBytes(
                fullMapPath);

        bool changed =
            false;

        try
        {
            MapHeaderState state =
                ReadHeaderState(
                    fullMapPath);

            if (string.Equals(
                    identity.MapFormat,
                    "Valve",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (state.MapFormat is not null &&
                    !string.Equals(
                        state.MapFormat,
                        "Valve",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(
                        state.MapFormat,
                        "Standard",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Map '{Path.GetFileName(fullMapPath)}' declares // Format: {state.MapFormat}. " +
                        "Companion only auto-converts Standard map syntax to Valve 220.");
                }

                if (CompanionMapFormatConversionService.EnsureValve220(
                        fullMapPath))
                {
                    changed =
                        true;
                }
            }
            else
            {
                ValidateExistingHeader(
                    state.MapFormat,
                    identity.MapFormat,
                    "Format",
                    fullMapPath);
            }

            bool needsGame =
                state.GameName is null;

            bool retargetGame =
                state.GameName is not null &&
                !string.Equals(
                    state.GameName,
                    identity.GameName,
                    StringComparison.OrdinalIgnoreCase);

            bool needsFormat =
                state.MapFormat is null;

            bool retargetFormat =
                state.MapFormat is not null &&
                !string.Equals(
                    state.MapFormat,
                    identity.MapFormat,
                    StringComparison.OrdinalIgnoreCase);

            if (retargetFormat)
            {
                RewriteExistingAsciiHeader(
                    fullMapPath,
                    FormatHeaderPrefix,
                    identity.MapFormat);

                changed =
                    true;
            }

            if (retargetGame)
            {
                RewriteExistingAsciiHeader(
                    fullMapPath,
                    GameHeaderPrefix,
                    identity.GameName);

                changed =
                    true;
            }

            StringBuilder header =
                new();

            if (needsGame)
            {
                header.Append(
                    GameHeaderPrefix);

                header.Append(' ');

                header.Append(
                    identity.GameName);

                header.Append("\r\n");
            }

            if (needsFormat)
            {
                header.Append(
                    FormatHeaderPrefix);

                header.Append(' ');

                header.Append(
                    identity.MapFormat);

                header.Append("\r\n");
            }

            if (header.Length > 0)
            {
                PrependAsciiHeader(
                    fullMapPath,
                    header.ToString());

                changed =
                    true;
            }

            return changed;
        }
        catch
        {
            if (changed)
            {
                WriteIdentityBytesAtomically(
                    fullMapPath,
                    originalBytes);
            }

            throw;
        }
    }

    private static void RewriteExistingAsciiHeader(
        string mapPath,
        string headerPrefix,
        string value)
    {
        byte[] sourceBytes =
            File.ReadAllBytes(
                mapPath);

        byte[] prefixBytes =
            Encoding.ASCII.GetBytes(
                headerPrefix);

        byte[] replacementBytes =
            Encoding.ASCII.GetBytes(
                $"{headerPrefix} {value}");

        int scanOffset =
            HasUtf8Bom(
                sourceBytes)
                ? 3
                : 0;

        int copyOffset =
            0;

        bool changed =
            false;

        using MemoryStream output =
            new(
                sourceBytes.Length +
                replacementBytes.Length +
                32);

        for (int lineIndex = 0;
             lineIndex < 64 &&
             scanOffset < sourceBytes.Length;
             lineIndex++)
        {
            int lineStart =
                scanOffset;

            int lineContentEnd =
                lineStart;

            while (lineContentEnd <
                       sourceBytes.Length &&
                   sourceBytes[lineContentEnd] !=
                       (byte)'\r' &&
                   sourceBytes[lineContentEnd] !=
                       (byte)'\n')
            {
                lineContentEnd++;
            }

            int contentStart =
                SkipHeaderLinePrefix(
                    sourceBytes,
                    lineStart,
                    lineContentEnd);

            if (contentStart <
                    lineContentEnd &&
                sourceBytes[contentStart] ==
                    (byte)'{')
            {
                break;
            }

            if (AsciiStartsWithIgnoreCase(
                    sourceBytes,
                    contentStart,
                    lineContentEnd,
                    prefixBytes))
            {
                output.Write(
                    sourceBytes,
                    copyOffset,
                    lineStart -
                    copyOffset);

                output.Write(
                    sourceBytes,
                    lineStart,
                    contentStart -
                    lineStart);

                output.Write(
                    replacementBytes,
                    0,
                    replacementBytes.Length);

                copyOffset =
                    lineContentEnd;

                changed =
                    true;
            }

            int nextLine =
                lineContentEnd;

            if (nextLine <
                    sourceBytes.Length &&
                sourceBytes[nextLine] ==
                    (byte)'\r')
            {
                nextLine++;
            }

            if (nextLine <
                    sourceBytes.Length &&
                sourceBytes[nextLine] ==
                    (byte)'\n')
            {
                nextLine++;
            }

            scanOffset =
                nextLine;
        }

        if (!changed)
        {
            throw new InvalidDataException(
                $"Map '{Path.GetFileName(mapPath)}' declared an existing {headerPrefix} value, " +
                "but Companion could not safely locate that header for retargeting.");
        }

        output.Write(
            sourceBytes,
            copyOffset,
            sourceBytes.Length -
            copyOffset);

        WriteIdentityBytesAtomically(
            mapPath,
            output.ToArray());
    }

    private static int SkipHeaderLinePrefix(
        byte[] sourceBytes,
        int lineStart,
        int lineEnd)
    {
        int offset =
            lineStart;

        while (offset <
                   lineEnd &&
               sourceBytes[offset] is
                   (byte)' ' or
                   (byte)'\t')
        {
            offset++;
        }

        if (offset + 2 <
                lineEnd &&
            sourceBytes[offset] ==
                0xEF &&
            sourceBytes[offset + 1] ==
                0xBB &&
            sourceBytes[offset + 2] ==
                0xBF)
        {
            offset +=
                3;

            while (offset <
                       lineEnd &&
                   sourceBytes[offset] is
                       (byte)' ' or
                       (byte)'\t')
            {
                offset++;
            }
        }

        return offset;
    }

    private static bool AsciiStartsWithIgnoreCase(
        byte[] sourceBytes,
        int contentStart,
        int contentEnd,
        byte[] prefixBytes)
    {
        if (contentEnd -
                contentStart <
            prefixBytes.Length)
        {
            return false;
        }

        for (int index = 0;
             index < prefixBytes.Length;
             index++)
        {
            if (ToAsciiLower(
                    sourceBytes[
                        contentStart +
                        index]) !=
                ToAsciiLower(
                    prefixBytes[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static byte ToAsciiLower(
        byte value)
    {
        return value is >=
                   (byte)'A' and <=
                   (byte)'Z'
            ? (byte)(
                value +
                ((byte)'a' -
                 (byte)'A'))
            : value;
    }

    private static bool HasUtf8Bom(
        byte[] sourceBytes)
    {
        return
            sourceBytes.Length >= 3 &&
            sourceBytes[0] == 0xEF &&
            sourceBytes[1] == 0xBB &&
            sourceBytes[2] == 0xBF;
    }

    private static void WriteIdentityBytesAtomically(
        string mapPath,
        byte[] bytes)
    {
        string temporaryPath =
            mapPath +
            ".tbcompanion-retarget-" +
            Guid.NewGuid().ToString("N");

        try
        {
            using (FileStream output =
                   new(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                output.Write(
                    bytes,
                    0,
                    bytes.Length);

                output.Flush(
                    flushToDisk: true);
            }

            File.Move(
                temporaryPath,
                mapPath,
                overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(
                        temporaryPath))
                {
                    File.Delete(
                        temporaryPath);
                }
            }
            catch
            {
                // Preserve the original retargeting result.
            }
        }
    }

    private static MapHeaderState ReadHeaderState(
        string mapPath)
    {
        const int PrefixByteLimit =
            16384;

        byte[] buffer =
            new byte[PrefixByteLimit];

        int bytesRead;

        using (FileStream stream =
               new(
                   mapPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read))
        {
            bytesRead =
                stream.Read(
                    buffer,
                    0,
                    buffer.Length);
        }

        string prefixText =
            Encoding.UTF8.GetString(
                buffer,
                0,
                bytesRead);

        string? gameName =
            null;

        string? mapFormat =
            null;

        using StringReader reader =
            new(
                prefixText);

        for (int lineIndex = 0;
             lineIndex < 64;
             lineIndex++)
        {
            string? line =
                reader.ReadLine();

            if (line is null)
            {
                break;
            }

            string trimmed =
                line.Trim()
                    .TrimStart('\uFEFF');

            if (trimmed.StartsWith(
                    "{",
                    StringComparison.Ordinal))
            {
                break;
            }

            if (TryReadHeaderValue(
                    trimmed,
                    GameHeaderPrefix,
                    out string? foundGame))
            {
                gameName =
                    MergeHeaderValue(
                        gameName,
                        foundGame,
                        "Game",
                        mapPath);
            }

            if (TryReadHeaderValue(
                    trimmed,
                    FormatHeaderPrefix,
                    out string? foundFormat))
            {
                mapFormat =
                    MergeHeaderValue(
                        mapFormat,
                        foundFormat,
                        "Format",
                        mapPath);
            }
        }

        return new MapHeaderState(
            gameName,
            mapFormat);
    }

    private static string MergeHeaderValue(
        string? existing,
        string incoming,
        string headerName,
        string mapPath)
    {
        if (existing is null)
        {
            return incoming;
        }

        if (!string.Equals(
                existing,
                incoming,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Map '{Path.GetFileName(mapPath)}' contains conflicting // {headerName}: headers.");
        }

        return existing;
    }

    private static void ValidateExistingHeader(
        string? existing,
        string expected,
        string headerName,
        string mapPath)
    {
        if (existing is null)
        {
            return;
        }

        if (!string.Equals(
                existing,
                expected,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Map '{Path.GetFileName(mapPath)}' declares // {headerName}: {existing}, " +
                $"but this Companion project requires {expected}. " +
                "The file was not changed.");
        }
    }

    private static bool TryReadHeaderValue(
        string line,
        string prefix,
        out string value)
    {
        value =
            string.Empty;

        if (!line.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string candidate =
            line[prefix.Length..]
                .Trim();

        if (string.IsNullOrWhiteSpace(
                candidate))
        {
            return false;
        }

        value =
            candidate;

        return true;
    }

    private static void PrependAsciiHeader(
        string mapPath,
        string header)
    {
        string temporaryPath =
            mapPath +
            ".tbcompanion-identity-" +
            Guid.NewGuid().ToString("N");

        try
        {
            byte[] headerBytes =
                Encoding.ASCII.GetBytes(
                    header);

            using (FileStream output =
                   new(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                output.Write(
                    headerBytes,
                    0,
                    headerBytes.Length);

                using (FileStream input =
                       new(
                           mapPath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read))
                {
                    input.CopyTo(
                        output);
                }

                output.Flush(
                    flushToDisk: true);
            }

            File.Move(
                temporaryPath,
                mapPath,
                overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(
                        temporaryPath))
                {
                    File.Delete(
                        temporaryPath);
                }
            }
            catch
            {
                // Preserve the original integration result.
            }
        }
    }

    private sealed record MapHeaderState(
        string? GameName,
        string? MapFormat);
}
