using System.IO;

namespace TrenchBroom.Companion.Core;

public sealed record TrenchBroomInstallationResolution(
    TrenchBroomInstallationInfo? Installation,
    string Source,
    string Status);

public static class TrenchBroomInstallationResolver
{
    public const string BundledSource = "bundled";
    public const string SavedSource = "saved";
    public const string ManagedSource = "managed";
    public const string DiscoveredSource = "discovered";
    public const string StandardFallbackSource = "standard";
    public const string MissingSource = "missing";

    public static TrenchBroomInstallationResolution Resolve(
        string? savedExecutablePath,
        string applicationBaseDirectory)
    {
        return Resolve(
            savedExecutablePath,
            applicationBaseDirectory,
            GetDefaultManagedExecutablePath(),
            EnumerateDefaultDiscoveryCandidates());
    }

    public static TrenchBroomInstallationResolution Resolve(
        string? savedExecutablePath,
        string applicationBaseDirectory,
        string managedExecutablePath,
        IEnumerable<string> additionalCandidatePaths)
    {
        if (string.IsNullOrWhiteSpace(applicationBaseDirectory))
        {
            throw new ArgumentException(
                "Application base directory cannot be empty.",
                nameof(applicationBaseDirectory));
        }

        ArgumentNullException.ThrowIfNull(additionalCandidatePaths);

        string bundledExecutablePath =
            Path.GetFullPath(
                Path.Combine(
                    applicationBaseDirectory,
                    "..",
                    "TrenchBroom",
                    "TrenchBroom.exe"));

        TrenchBroomInstallationInfo? standardFallback = null;

        if (!string.IsNullOrWhiteSpace(savedExecutablePath))
        {
            TrenchBroomInstallationInfo saved =
                TrenchBroomInstallationService.Inspect(
                    savedExecutablePath);

            if (saved.IsValid &&
                saved.IsWadForgeCompatible)
            {
                return Compatible(
                    saved,
                    SavedSource,
                    "Saved WadForge-compatible TrenchBroom selected.");
            }

            if (saved.IsValid)
            {
                standardFallback = saved;
            }
        }

        TrenchBroomInstallationInfo bundled =
            TrenchBroomInstallationService.Inspect(
                bundledExecutablePath);

        if (bundled.IsValid &&
            bundled.IsWadForgeCompatible)
        {
            return Compatible(
                bundled,
                BundledSource,
                "Bundled WadForge-compatible TrenchBroom selected automatically.");
        }

        if (!string.IsNullOrWhiteSpace(managedExecutablePath))
        {
            TrenchBroomInstallationInfo managed =
                TrenchBroomInstallationService.Inspect(
                    managedExecutablePath);

            if (managed.IsValid &&
                managed.IsWadForgeCompatible)
            {
                return Compatible(
                    managed,
                    ManagedSource,
                    "Managed WadForge-compatible TrenchBroom selected automatically.");
            }

            if (standardFallback is null &&
                managed.IsValid)
            {
                standardFallback = managed;
            }
        }

        HashSet<string> seen =
            new(
                StringComparer.OrdinalIgnoreCase);

        seen.Add(
            bundledExecutablePath);

        if (!string.IsNullOrWhiteSpace(savedExecutablePath))
        {
            TryAddNormalized(
                seen,
                savedExecutablePath);
        }

        if (!string.IsNullOrWhiteSpace(managedExecutablePath))
        {
            TryAddNormalized(
                seen,
                managedExecutablePath);
        }

        foreach (string candidatePath in additionalCandidatePaths)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                continue;
            }

            string fullPath;

            try
            {
                fullPath =
                    Path.GetFullPath(
                        candidatePath);
            }
            catch
            {
                continue;
            }

            if (!seen.Add(fullPath))
            {
                continue;
            }

            TrenchBroomInstallationInfo candidate =
                TrenchBroomInstallationService.Inspect(
                    fullPath);

            if (candidate.IsValid &&
                candidate.IsWadForgeCompatible)
            {
                return Compatible(
                    candidate,
                    DiscoveredSource,
                    "Existing WadForge-compatible TrenchBroom detected automatically.");
            }

            if (standardFallback is null &&
                candidate.IsValid)
            {
                standardFallback = candidate;
            }
        }

        if (standardFallback is not null)
        {
            return new TrenchBroomInstallationResolution(
                standardFallback,
                StandardFallbackSource,
                "A standard TrenchBroom installation was found, but the WadForge-compatible build is not installed. Long texture aliases are unavailable until the compatible build is set up.");
        }

        return new TrenchBroomInstallationResolution(
            null,
            MissingSource,
            "No TrenchBroom installation was found. Set up the WadForge-compatible TrenchBroom build to continue.");
    }

    public static string GetDefaultManagedExecutablePath()
    {
        CompanionSettings settings =
            CompanionSettingsStore.Load();

        string managedDataRoot =
            CompanionManagedDataRootService.GetRequiredRoot(
                settings);

        return CompanionManagedDataRootService
            .GetTrenchBroomExecutablePath(
                managedDataRoot);
    }

    public static IReadOnlyList<string> EnumerateDefaultDiscoveryCandidates()
    {
        List<string> candidates = new();

        AddCommonCandidate(
            candidates,
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles));

        AddCommonCandidate(
            candidates,
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86));

        string localApplicationData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrWhiteSpace(localApplicationData))
        {
            candidates.Add(
                Path.Combine(
                    localApplicationData,
                    "Programs",
                    "TrenchBroom",
                    "TrenchBroom.exe"));

            candidates.Add(
                Path.Combine(
                    localApplicationData,
                    "TrenchBroom",
                    "TrenchBroom.exe"));
        }

        string? pathEnvironment =
            Environment.GetEnvironmentVariable(
                "PATH");

        if (!string.IsNullOrWhiteSpace(pathEnvironment))
        {
            foreach (string pathEntry in
                     pathEnvironment.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                try
                {
                    candidates.Add(
                        Path.Combine(
                            pathEntry,
                            "TrenchBroom.exe"));
                }
                catch
                {
                    // Ignore malformed PATH entries.
                }
            }
        }

        return candidates;
    }

    private static void AddCommonCandidate(
        List<string> candidates,
        string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        candidates.Add(
            Path.Combine(
                root,
                "TrenchBroom",
                "TrenchBroom.exe"));
    }

    private static void TryAddNormalized(
        HashSet<string> paths,
        string path)
    {
        try
        {
            paths.Add(
                Path.GetFullPath(
                    path));
        }
        catch
        {
            // Invalid saved/candidate paths are ignored here.
        }
    }

    private static TrenchBroomInstallationResolution Compatible(
        TrenchBroomInstallationInfo installation,
        string source,
        string status)
    {
        return new TrenchBroomInstallationResolution(
            installation,
            source,
            status);
    }
}
