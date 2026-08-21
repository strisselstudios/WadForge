using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace TrenchBroom.Companion.Core;

public static class CompanionGameProfiles
{
    public static CompanionGameProfile Dusk { get; } =
        new(
            id: "dusk",
            displayName: "DUSK",
            steamAppId: "519860",
            steamCommonDirectoryName: "Dusk",
            runtimeModsRelativePath:
                Path.Combine(
                    "SDK",
                    "mnt",
                    "local"),
            defaultTextureArchiveFormat:
                CompanionTextureArchiveFormats.Wad2,
            supportedTextureArchiveFormats:
                new[]
                {
                    CompanionTextureArchiveFormats.Wad2,
                    CompanionTextureArchiveFormats.Wad3
                });

    public static CompanionGameProfile Quake { get; } =
        new(
            id: "quake",
            displayName: "Quake",
            steamAppId: "2310",
            steamCommonDirectoryName: "Quake",
            runtimeModsRelativePath:
                string.Empty,
            defaultTextureArchiveFormat:
                CompanionTextureArchiveFormats.Wad2,
            supportedTextureArchiveFormats:
                new[]
                {
                    CompanionTextureArchiveFormats.Wad2
                });

    public static CompanionGameProfile HalfLife { get; } =
        new(
            id: "halflife",
            displayName: "Half-Life",
            steamAppId: "70",
            steamCommonDirectoryName: "Half-Life",
            runtimeModsRelativePath:
                string.Empty,
            defaultTextureArchiveFormat:
                CompanionTextureArchiveFormats.Wad3,
            supportedTextureArchiveFormats:
                new[]
                {
                    CompanionTextureArchiveFormats.Wad3
                });

    private static readonly IReadOnlyDictionary<string, CompanionGameProfile>
        ProfilesById =
            new ReadOnlyDictionary<string, CompanionGameProfile>(
                new Dictionary<string, CompanionGameProfile>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [Dusk.Id] = Dusk,
                    [Quake.Id] = Quake,
                    [HalfLife.Id] = HalfLife
                });

    public static IEnumerable<CompanionGameProfile> All =>
        ProfilesById.Values;

    public static bool TryGet(
        string? gameId,
        out CompanionGameProfile? profile)
    {
        profile = null;

        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        return ProfilesById.TryGetValue(
            gameId.Trim(),
            out profile);
    }

    public static CompanionGameProfile GetRequired(
        string gameId)
    {
        if (!TryGet(
                gameId,
                out CompanionGameProfile? profile) ||
            profile is null)
        {
            throw new ArgumentException(
                $"Unsupported Companion game profile '{gameId}'.",
                nameof(gameId));
        }

        return profile;
    }
}
