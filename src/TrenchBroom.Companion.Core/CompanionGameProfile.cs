using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionGameProfile
{
    public CompanionGameProfile(
        string id,
        string displayName,
        string steamAppId,
        string steamCommonDirectoryName,
        string runtimeModsRelativePath,
        string defaultTextureArchiveFormat,
        IEnumerable<string> supportedTextureArchiveFormats)
    {
        Id =
            RequireValue(
                id,
                nameof(id));

        DisplayName =
            RequireValue(
                displayName,
                nameof(displayName));

        SteamAppId =
            RequireValue(
                steamAppId,
                nameof(steamAppId));

        SteamCommonDirectoryName =
            RequireValue(
                steamCommonDirectoryName,
                nameof(steamCommonDirectoryName));

        RuntimeModsRelativePath =
            runtimeModsRelativePath?.Trim() ??
            string.Empty;

        ArgumentNullException.ThrowIfNull(
            supportedTextureArchiveFormats);

        List<string> formats =
            supportedTextureArchiveFormats
                .Select(
                    CompanionTextureArchiveFormats.Normalize)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (formats.Count == 0)
        {
            throw new ArgumentException(
                "At least one texture archive format must be supported.",
                nameof(supportedTextureArchiveFormats));
        }

        DefaultTextureArchiveFormat =
            CompanionTextureArchiveFormats.Normalize(
                defaultTextureArchiveFormat);

        if (!formats.Contains(
                DefaultTextureArchiveFormat,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The default texture archive format must be supported by the game profile.",
                nameof(defaultTextureArchiveFormat));
        }

        SupportedTextureArchiveFormats =
            new ReadOnlyCollection<string>(
                formats);
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string SteamAppId { get; }

    public string SteamCommonDirectoryName { get; }

    public string RuntimeModsRelativePath { get; }

    public string DefaultTextureArchiveFormat { get; }

    public IReadOnlyList<string> SupportedTextureArchiveFormats { get; }

    public bool CanChooseTextureArchiveFormat =>
        SupportedTextureArchiveFormats.Count > 1;

    public bool SupportsTextureArchiveFormat(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return false;
        }

        string normalized;

        try
        {
            normalized =
                CompanionTextureArchiveFormats.Normalize(
                    value);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return SupportedTextureArchiveFormats.Contains(
            normalized,
            StringComparer.OrdinalIgnoreCase);
    }

    public string GetRuntimeModsRoot(
        string gameInstallationDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                gameInstallationDirectory))
        {
            throw new ArgumentException(
                "Game installation directory cannot be empty.",
                nameof(gameInstallationDirectory));
        }

        string installationDirectory =
            Path.GetFullPath(
                gameInstallationDirectory.Trim());

        if (string.IsNullOrWhiteSpace(
                RuntimeModsRelativePath))
        {
            return installationDirectory;
        }

        return Path.GetFullPath(
            Path.Combine(
                installationDirectory,
                RuntimeModsRelativePath));
    }

    private static string RequireValue(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Value cannot be empty.",
                parameterName);
        }

        return value.Trim();
    }
}
