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

        MapHeaderState state =
            ReadHeaderState(
                fullMapPath);

        ValidateExistingHeader(
            state.GameName,
            identity.GameName,
            "Game",
            fullMapPath);

        ValidateExistingHeader(
            state.MapFormat,
            identity.MapFormat,
            "Format",
            fullMapPath);

        bool needsGame =
            state.GameName is null;

        bool needsFormat =
            state.MapFormat is null;

        if (!needsGame &&
            !needsFormat)
        {
            return false;
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

        PrependAsciiHeader(
            fullMapPath,
            header.ToString());

        return true;
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
