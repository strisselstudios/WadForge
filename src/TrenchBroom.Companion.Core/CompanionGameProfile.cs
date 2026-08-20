using System.IO;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionGameProfile
{
    public CompanionGameProfile(
        string id,
        string displayName,
        string steamAppId,
        string steamCommonDirectoryName,
        string runtimeModsRelativePath)
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
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string SteamAppId { get; }

    public string SteamCommonDirectoryName { get; }

    public string RuntimeModsRelativePath { get; }

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
