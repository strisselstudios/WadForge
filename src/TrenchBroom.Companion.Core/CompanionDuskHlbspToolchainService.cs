using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TrenchBroom.Companion.Core;

public static class CompanionDuskHlbspToolchainService
{
    public const string RecommendedVersion =
        "2.0.0-alpha11";

    private const string PackagedFolderName =
        "ericw-tools";

    private const string BinaryArchiveFileName =
        "ericw-tools-2.0.0-alpha11-win64.zip";

    private const string HashesFileName =
        "SHA256SUMS.txt";

    private const string ManagedMarkerFileName =
        ".trenchbroom-companion-managed";

    private const string VersionFileName =
        "VERSION.txt";

    private const string QbspFileName =
        "qbsp.exe";

    private const string VisFileName =
        "vis.exe";

    private const string LightFileName =
        "light.exe";

    public static CompanionEricwToolchainStatus GetStatus(
        string managedDataRoot)
    {
        return GetStatusForRoot(
            GetManagedRootDirectory(
                managedDataRoot));
    }

    public static CompanionEricwToolchainStatus EnsureProvisioned(
        string applicationDirectory,
        string managedDataRoot)
    {
        CompanionEricwToolchainStatus existing =
            GetStatus(
                managedDataRoot);

        if (existing.IsReady)
        {
            return existing;
        }

        string packagedDirectory =
            GetPackagedDirectory(
                applicationDirectory);

        string archivePath =
            Path.Combine(
                packagedDirectory,
                BinaryArchiveFileName);

        string hashesPath =
            Path.Combine(
                packagedDirectory,
                HashesFileName);

        if (!File.Exists(
                archivePath) ||
            !File.Exists(
                hashesPath))
        {
            throw new FileNotFoundException(
                "The Companion installation is missing its packaged DUSK WAD3/Half-Life BSP compiler payload. " +
                "Reinstall or repair TrenchBroom Companion.");
        }

        string expectedArchiveHash =
            ReadExpectedArchiveHash(
                hashesPath);

        string actualArchiveHash =
            ComputeSha256(
                archivePath);

        if (!string.Equals(
                expectedArchiveHash,
                actualArchiveHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The packaged DUSK WAD3/Half-Life BSP compiler archive failed its SHA-256 integrity check. " +
                "Reinstall or repair TrenchBroom Companion.");
        }

        string managedRoot =
            GetManagedRootDirectory(
                managedDataRoot);

        string? managedParent =
            Path.GetDirectoryName(
                managedRoot);

        if (string.IsNullOrWhiteSpace(
                managedParent))
        {
            throw new InvalidDataException(
                "Could not determine the managed compiler parent directory.");
        }

        Directory.CreateDirectory(
            managedParent);

        string extractionDirectory =
            managedRoot +
            ".extract-" +
            Guid.NewGuid().ToString(
                "N");

        string stagingDirectory =
            managedRoot +
            ".staging-" +
            Guid.NewGuid().ToString(
                "N");

        string backupDirectory =
            managedRoot +
            ".backup-" +
            Guid.NewGuid().ToString(
                "N");

        bool backedUp =
            false;

        try
        {
            ZipFile.ExtractToDirectory(
                archivePath,
                extractionDirectory);

            string? toolchainDirectory =
                TryFindToolchainDirectory(
                    extractionDirectory);

            if (toolchainDirectory is null)
            {
                throw new InvalidDataException(
                    "The packaged DUSK WAD3/Half-Life BSP compiler archive does not contain qbsp.exe, vis.exe, and light.exe together.");
            }

            CopyDirectory(
                toolchainDirectory,
                stagingDirectory);

            File.WriteAllText(
                Path.Combine(
                    stagingDirectory,
                    ManagedMarkerFileName),
                "Managed by TrenchBroom Companion for DUSK WAD3/Half-Life BSP compilation.\r\n",
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            File.WriteAllText(
                Path.Combine(
                    stagingDirectory,
                    VersionFileName),
                RecommendedVersion +
                Environment.NewLine,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            CompanionEricwToolchainStatus staged =
                GetStatusForRoot(
                    stagingDirectory);

            if (!staged.IsReady)
            {
                throw new InvalidDataException(
                    staged.Problem ??
                    "The packaged DUSK WAD3/Half-Life BSP compiler payload is incomplete.");
            }

            if (Directory.Exists(
                    managedRoot))
            {
                Directory.Move(
                    managedRoot,
                    backupDirectory);

                backedUp =
                    true;
            }

            Directory.Move(
                stagingDirectory,
                managedRoot);

            if (backedUp &&
                Directory.Exists(
                    backupDirectory))
            {
                Directory.Delete(
                    backupDirectory,
                    recursive: true);
            }
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

            if (backedUp &&
                !Directory.Exists(
                    managedRoot) &&
                Directory.Exists(
                    backupDirectory))
            {
                Directory.Move(
                    backupDirectory,
                    managedRoot);
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(
                    extractionDirectory))
            {
                Directory.Delete(
                    extractionDirectory,
                    recursive: true);
            }
        }

        CompanionEricwToolchainStatus result =
            GetStatus(
                managedDataRoot);

        if (!result.IsReady)
        {
            throw new InvalidDataException(
                result.Problem ??
                "The managed DUSK WAD3/Half-Life BSP compiler installation is incomplete.");
        }

        return result;
    }

    private static CompanionEricwToolchainStatus GetStatusForRoot(
        string rootDirectory)
    {
        string root =
            Path.GetFullPath(
                rootDirectory);

        string qbspPath =
            Path.Combine(
                root,
                QbspFileName);

        string visPath =
            Path.Combine(
                root,
                VisFileName);

        string lightPath =
            Path.Combine(
                root,
                LightFileName);

        List<string> missing =
            new();

        if (!File.Exists(
                qbspPath))
        {
            missing.Add(
                QbspFileName);
        }

        if (!File.Exists(
                visPath))
        {
            missing.Add(
                VisFileName);
        }

        if (!File.Exists(
                lightPath))
        {
            missing.Add(
                LightFileName);
        }

        bool ready =
            missing.Count ==
            0;

        return new CompanionEricwToolchainStatus(
            root,
            qbspPath,
            visPath,
            lightPath,
            RecommendedVersion,
            ready,
            ready
                ? null
                : "Missing compiler files: " +
                  string.Join(
                      ", ",
                      missing));
    }

    private static string GetManagedRootDirectory(
        string managedDataRoot)
    {
        if (string.IsNullOrWhiteSpace(
                managedDataRoot))
        {
            throw new ArgumentException(
                "A Companion managed data root is required.",
                nameof(managedDataRoot));
        }

        return Path.Combine(
            CompanionManagedDataRootService
                .GetCompilersDirectory(
                    managedDataRoot),
            PackagedFolderName,
            RecommendedVersion);
    }

    private static string GetPackagedDirectory(
        string applicationDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                applicationDirectory))
        {
            throw new ArgumentException(
                "An application directory is required.",
                nameof(applicationDirectory));
        }

        return Path.Combine(
            Path.GetFullPath(
                applicationDirectory),
            "ThirdParty",
            PackagedFolderName,
            RecommendedVersion);
    }

    private static string ReadExpectedArchiveHash(
        string hashesPath)
    {
        foreach (string rawLine in
                 File.ReadAllLines(
                     hashesPath))
        {
            string line =
                rawLine.Trim();

            if (line.Length ==
                    0 ||
                line.StartsWith(
                    "#",
                    StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts =
                line.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length <
                2)
            {
                continue;
            }

            string fileName =
                parts[^1]
                    .TrimStart(
                        '*');

            if (!string.Equals(
                    fileName,
                    BinaryArchiveFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string hash =
                parts[0];

            if (hash.Length ==
                    64 &&
                hash.All(
                    character =>
                        Uri.IsHexDigit(
                            character)))
            {
                return hash.ToUpperInvariant();
            }
        }

        throw new InvalidDataException(
            "The DUSK WAD3/Half-Life BSP SHA256SUMS.txt file does not contain a valid binary archive hash.");
    }

    private static string ComputeSha256(
        string filePath)
    {
        using FileStream stream =
            File.OpenRead(
                filePath);

        using SHA256 sha256 =
            SHA256.Create();

        return Convert.ToHexString(
            sha256.ComputeHash(
                stream));
    }

    private static string? TryFindToolchainDirectory(
        string sourceRoot)
    {
        string directQbsp =
            Path.Combine(
                sourceRoot,
                QbspFileName);

        string directVis =
            Path.Combine(
                sourceRoot,
                VisFileName);

        string directLight =
            Path.Combine(
                sourceRoot,
                LightFileName);

        if (File.Exists(
                directQbsp) &&
            File.Exists(
                directVis) &&
            File.Exists(
                directLight))
        {
            return sourceRoot;
        }

        List<string> candidates =
            Directory
                .EnumerateFiles(
                    sourceRoot,
                    QbspFileName,
                    SearchOption.AllDirectories)
                .Select(
                    path =>
                        Path.GetDirectoryName(
                            path))
                .Where(
                    path =>
                        !string.IsNullOrWhiteSpace(
                            path))
                .Select(
                    path =>
                        Path.GetFullPath(
                            path!))
                .Where(
                    directory =>
                        File.Exists(
                            Path.Combine(
                                directory,
                                VisFileName)) &&
                        File.Exists(
                            Path.Combine(
                                directory,
                                LightFileName)))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        return candidates.Count ==
            1
            ? candidates[0]
            : null;
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string destinationDirectory)
    {
        Directory.CreateDirectory(
            destinationDirectory);

        foreach (string filePath in
                 Directory.EnumerateFiles(
                     sourceDirectory))
        {
            File.Copy(
                filePath,
                Path.Combine(
                    destinationDirectory,
                    Path.GetFileName(
                        filePath)),
                overwrite: true);
        }

        foreach (string childDirectory in
                 Directory.EnumerateDirectories(
                     sourceDirectory))
        {
            CopyDirectory(
                childDirectory,
                Path.Combine(
                    destinationDirectory,
                    Path.GetFileName(
                        childDirectory)));
        }
    }
}
