using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TrenchBroom.Companion.Core;

public enum CompanionDuskCompileMode
{
    QuakeBsp,
    HalfLifeBsp
}

public sealed record CompanionDuskCompileModeDecision(
    CompanionDuskCompileMode Mode,
    IReadOnlyList<string> Wad3Archives)
{
    public bool UsesHalfLifeBsp =>
        Mode ==
        CompanionDuskCompileMode.HalfLifeBsp;
}

public static class CompanionDuskCompileModeService
{
    private static readonly Regex WadPropertyPattern =
        new(
            @"(?m)^""wad""\s+""(?<value>(?:\\.|[^""])*)""\s*$",
            RegexOptions.CultureInvariant);

    public static CompanionDuskCompileModeDecision Determine(
        string mapPath,
        string projectWadDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                mapPath))
        {
            throw new ArgumentException(
                "A map path is required.",
                nameof(mapPath));
        }

        if (string.IsNullOrWhiteSpace(
                projectWadDirectory))
        {
            throw new ArgumentException(
                "A project WAD directory is required.",
                nameof(projectWadDirectory));
        }

        string fullMapPath =
            Path.GetFullPath(
                mapPath);

        string fullWadDirectory =
            Path.GetFullPath(
                projectWadDirectory);

        if (!File.Exists(
                fullMapPath))
        {
            throw new FileNotFoundException(
                "The active map could not be found.",
                fullMapPath);
        }

        if (!Directory.Exists(
                fullWadDirectory))
        {
            return new CompanionDuskCompileModeDecision(
                CompanionDuskCompileMode.QuakeBsp,
                Array.Empty<string>());
        }

        string mapText =
            File.ReadAllText(
                fullMapPath);

        MatchCollection matches =
            WadPropertyPattern.Matches(
                mapText);

        List<string> candidates =
            new();

        if (matches.Count == 1)
        {
            string raw =
                UnescapeMapValue(
                    matches[0]
                        .Groups["value"]
                        .Value);

            foreach (string reference in
                     raw.Split(
                         ';',
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                string name =
                    Path.GetFileName(
                        reference.Replace(
                            '/',
                            Path.DirectorySeparatorChar));

                if (string.IsNullOrWhiteSpace(
                        name))
                {
                    continue;
                }

                string candidate =
                    Path.Combine(
                        fullWadDirectory,
                        name);

                if (File.Exists(
                        candidate) &&
                    !candidates.Contains(
                        candidate,
                        StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(
                        candidate);
                }
            }
        }

        if (candidates.Count == 0)
        {
            candidates.AddRange(
                Directory
                    .EnumerateFiles(
                        fullWadDirectory,
                        "*.wad",
                        SearchOption.TopDirectoryOnly)
                    .OrderBy(
                        path =>
                            Path.GetFileName(
                                path),
                        StringComparer.OrdinalIgnoreCase));
        }

        List<string> wad3Archives =
            new();

        foreach (string wadPath in
                 candidates)
        {
            string magic =
                ReadWadMagic(
                    wadPath);

            if (string.Equals(
                    magic,
                    "WAD3",
                    StringComparison.Ordinal))
            {
                wad3Archives.Add(
                    Path.GetFileName(
                        wadPath));
            }
        }

        return new CompanionDuskCompileModeDecision(
            wad3Archives.Count > 0
                ? CompanionDuskCompileMode.HalfLifeBsp
                : CompanionDuskCompileMode.QuakeBsp,
            wad3Archives);
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

        if (stream.Length <
            4)
        {
            throw new InvalidDataException(
                $"WAD archive is too small: {wadPath}");
        }

        Span<byte> header =
            stackalloc byte[4];

        int read =
            stream.Read(
                header);

        if (read !=
            4)
        {
            throw new InvalidDataException(
                $"Could not read WAD header: {wadPath}");
        }

        return System.Text.Encoding.ASCII.GetString(
            header);
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
}
