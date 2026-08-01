using System.IO;
using System.Security.Cryptography;
using WadForge.Aliases;
using WadForge.Core;
using WadForge.Wad;

namespace TrenchBroom.Companion.Core;

public static class WadRegistrationService
{
    public static WadRegistrationResult Inspect(
        string wadPath)
    {
        if (string.IsNullOrWhiteSpace(wadPath))
        {
            return Invalid(
                wadPath ?? string.Empty,
                "No WAD path was provided.");
        }

        string fullPath;

        try
        {
            fullPath =
                Path.GetFullPath(wadPath);
        }
        catch (Exception exception)
        {
            return Invalid(
                wadPath,
                "Invalid WAD path: " +
                exception.Message);
        }

        if (!File.Exists(fullPath))
        {
            return Invalid(
                fullPath,
                "The WAD file is missing.");
        }

        if (!WadArchiveInspector.TryInspect(
                fullPath,
                out WadInspectionResult? inspection,
                out string inspectionError) ||
            inspection is null)
        {
            return Invalid(
                fullPath,
                "Invalid WAD archive: " +
                inspectionError);
        }

        string format =
            inspection.Format == WadFormat.Wad2
                ? "WAD2"
                : "WAD3";

        string manifestPath =
            fullPath + ".wadforge.json";

        if (!File.Exists(manifestPath))
        {
            return new WadRegistrationResult(
                fullPath,
                format,
                inspection.LumpCount,
                string.Empty,
                0,
                true,
                false,
                false,
                "Valid WAD. No WadForge alias manifest exists.");
        }

        try
        {
            WadAliasManifest manifest =
                WadAliasManifestSerializer.Read(
                    manifestPath);

            string actualHash;

            using (
                FileStream stream =
                    File.OpenRead(fullPath))
            {
                actualHash =
                    System.Convert.ToHexString(
                        SHA256.HashData(stream));
            }

            if (!string.Equals(
                    actualHash,
                    manifest.WadSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new WadRegistrationResult(
                    fullPath,
                    format,
                    inspection.LumpCount,
                    manifestPath,
                    manifest.Textures.Count,
                    true,
                    true,
                    false,
                    "WAD is valid, but the alias manifest hash does not match.");
            }

            if (!string.Equals(
                    Path.GetFileName(fullPath),
                    manifest.WadFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new WadRegistrationResult(
                    fullPath,
                    format,
                    inspection.LumpCount,
                    manifestPath,
                    manifest.Textures.Count,
                    true,
                    true,
                    false,
                    "WAD hash matches, but the manifest names a different WAD file.");
            }

            return new WadRegistrationResult(
                fullPath,
                format,
                inspection.LumpCount,
                manifestPath,
                manifest.Textures.Count,
                true,
                true,
                true,
                "Valid WAD and verified WadForge alias manifest.");
        }
        catch (Exception exception)
        {
            return new WadRegistrationResult(
                fullPath,
                format,
                inspection.LumpCount,
                manifestPath,
                0,
                true,
                true,
                false,
                "WAD is valid, but the alias manifest could not be read: " +
                exception.Message);
        }
    }

    private static WadRegistrationResult Invalid(
        string path,
        string validation)
    {
        return new WadRegistrationResult(
            path,
            "Unknown",
            -1,
            string.Empty,
            -1,
            false,
            false,
            false,
            validation);
    }
}
