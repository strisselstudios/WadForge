using System.Diagnostics;
using System.IO;

namespace TrenchBroom.Companion.Core;

public static class TrenchBroomInstallationService
{
    public const string CompatibilityMarkerFileName =
        "wadforge-companion-build.json";

    public static TrenchBroomInstallationInfo Inspect(
        string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return Invalid(
                executablePath ?? string.Empty,
                "No TrenchBroom executable was selected.");
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(executablePath);
        }
        catch (Exception exception)
        {
            return Invalid(
                executablePath,
                "The executable path is invalid: " +
                exception.Message);
        }

        if (!File.Exists(fullPath))
        {
            return Invalid(
                fullPath,
                "The selected executable does not exist.");
        }

        string fileName =
            Path.GetFileName(fullPath);

        if (!fileName.Contains(
                "TrenchBroom",
                StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(
                fullPath,
                "The selected file is not named like a TrenchBroom executable.");
        }

        if (!string.Equals(
                Path.GetExtension(fullPath),
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(
                fullPath,
                "The selected file is not a Windows executable.");
        }

        string version = "Unknown";

        try
        {
            FileVersionInfo versionInfo =
                FileVersionInfo.GetVersionInfo(fullPath);

            version =
                versionInfo.ProductVersion ??
                versionInfo.FileVersion ??
                "Unknown";
        }
        catch
        {
            version = "Unknown";
        }

        string directory =
            Path.GetDirectoryName(fullPath) ??
            string.Empty;

        string markerPath =
            Path.Combine(
                directory,
                CompatibilityMarkerFileName);

        bool compatible =
            File.Exists(markerPath);

        string status = compatible
            ? "Valid WadForge-compatible TrenchBroom build."
            : "Valid TrenchBroom executable. WadForge long-name patch not detected.";

        return new TrenchBroomInstallationInfo(
            fullPath,
            version,
            true,
            compatible,
            status);
    }

    private static TrenchBroomInstallationInfo Invalid(
        string path,
        string status)
    {
        return new TrenchBroomInstallationInfo(
            path,
            "Unknown",
            false,
            false,
            status);
    }
}