using System.IO;
using System.Text.Json;

namespace TrenchBroom.Companion.Core;

public static class CompanionProjectStore
{
    public const string ProjectExtension = ".tbproject";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static CompanionProjectManifest Create(
        string name,
        string gameId,
        string? modName = null,
        string? preferredTextureArchiveFormat = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Project name cannot be empty.",
                nameof(name));
        }

        if (string.IsNullOrWhiteSpace(gameId))
        {
            throw new ArgumentException(
                "Game ID cannot be empty.",
                nameof(gameId));
        }

        string normalizedGameId =
            gameId.Trim().ToLowerInvariant();

        string textureArchiveFormat =
            ResolveTextureArchiveFormat(
                normalizedGameId,
                preferredTextureArchiveFormat);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        return new CompanionProjectManifest
        {
            ProjectId = Guid.NewGuid(),
            Name = name.Trim(),
            GameId = normalizedGameId,
            ModName = NormalizeOptionalText(modName),
            PreferredTextureArchiveFormat = textureArchiveFormat,
            CreatedUtc = now,
            UpdatedUtc = now
        };
    }

    public static CompanionProjectManifest Load(
        string projectFilePath)
    {
        string fullProjectPath =
            ValidateProjectFilePath(projectFilePath);

        if (!File.Exists(fullProjectPath))
        {
            throw new FileNotFoundException(
                "Companion project file was not found.",
                fullProjectPath);
        }

        string json =
            File.ReadAllText(fullProjectPath);

        CompanionProjectManifest? project =
            JsonSerializer.Deserialize<CompanionProjectManifest>(
                json,
                JsonOptions);

        if (project is null)
        {
            throw new InvalidDataException(
                "Companion project file did not contain a valid project.");
        }

        MigrateToCurrentSchema(project);
        NormalizeAndValidate(project);

        return project;
    }

    public static void Save(
        string projectFilePath,
        CompanionProjectManifest project)
    {
        ArgumentNullException.ThrowIfNull(project);

        string fullProjectPath =
            ValidateProjectFilePath(projectFilePath);

        NormalizeAndValidate(project);
        project.UpdatedUtc = DateTimeOffset.UtcNow;

        string? directory =
            Path.GetDirectoryName(fullProjectPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidDataException(
                "Companion project file must have a parent directory.");
        }

        Directory.CreateDirectory(directory);

        string json =
            JsonSerializer.Serialize(
                project,
                JsonOptions);

        string temporaryPath =
            fullProjectPath + ".temporary";

        try
        {
            File.WriteAllText(
                temporaryPath,
                json);

            File.Move(
                temporaryPath,
                fullProjectPath,
                true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static CompanionProjectMap AddMap(
        CompanionProjectManifest project,
        string projectFilePath,
        string mapFilePath,
        bool makeActive = true)
    {
        ArgumentNullException.ThrowIfNull(project);

        string relativePath =
            MakeRelativeMapPath(
                projectFilePath,
                mapFilePath);

        CompanionProjectMap? existing =
            project.Maps.FirstOrDefault(
                map =>
                    string.Equals(
                        map.Path,
                        relativePath,
                        StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            if (makeActive)
            {
                project.ActiveMapPath = existing.Path;
            }

            return existing;
        }

        CompanionProjectMap entry =
            new()
            {
                Path = relativePath,
                DisplayName =
                    Path.GetFileNameWithoutExtension(relativePath)
            };

        project.Maps.Add(entry);

        if (makeActive)
        {
            project.ActiveMapPath = relativePath;
        }

        NormalizeAndValidate(project);

        return entry;
    }

    public static string MakeRelativeMapPath(
        string projectFilePath,
        string mapFilePath)
    {
        string fullProjectPath =
            ValidateProjectFilePath(projectFilePath);

        if (string.IsNullOrWhiteSpace(mapFilePath))
        {
            throw new ArgumentException(
                "Map file path cannot be empty.",
                nameof(mapFilePath));
        }

        string? projectDirectory =
            Path.GetDirectoryName(fullProjectPath);

        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            throw new InvalidDataException(
                "Companion project file must have a parent directory.");
        }

        string fullMapPath =
            Path.GetFullPath(mapFilePath);

        string relativePath =
            Path.GetRelativePath(
                projectDirectory,
                fullMapPath);

        return NormalizeRelativeMapPath(relativePath);
    }

    public static string ResolveMapPath(
        string projectFilePath,
        string relativeMapPath)
    {
        string fullProjectPath =
            ValidateProjectFilePath(projectFilePath);

        string normalizedRelativePath =
            NormalizeRelativeMapPath(relativeMapPath);

        string? projectDirectory =
            Path.GetDirectoryName(fullProjectPath);

        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            throw new InvalidDataException(
                "Companion project file must have a parent directory.");
        }

        string resolved =
            Path.GetFullPath(
                Path.Combine(
                    projectDirectory,
                    normalizedRelativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

        string projectRoot =
            Path.GetFullPath(projectDirectory);

        string requiredPrefix =
            projectRoot.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? projectRoot
                : projectRoot + Path.DirectorySeparatorChar;

        if (!resolved.StartsWith(
                requiredPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Map path escapes the Companion project directory.");
        }

        return resolved;
    }

    public static void NormalizeAndValidate(
        CompanionProjectManifest project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.SchemaVersion !=
            CompanionProjectManifest.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported Companion project schema version " +
                $"'{project.SchemaVersion}'. " +
                $"Expected '{CompanionProjectManifest.CurrentSchemaVersion}'.");
        }

        if (project.ProjectId == Guid.Empty)
        {
            throw new InvalidDataException(
                "Companion project ID cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(project.Name))
        {
            throw new InvalidDataException(
                "Companion project name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(project.GameId))
        {
            throw new InvalidDataException(
                "Companion project game ID cannot be empty.");
        }

        project.Name = project.Name.Trim();
        project.GameId =
            project.GameId.Trim().ToLowerInvariant();
        project.ModName =
            NormalizeOptionalText(project.ModName);

        project.PreferredTextureArchiveFormat =
            NormalizeProjectTextureArchiveFormat(
                project.GameId,
                project.PreferredTextureArchiveFormat);

        project.DefaultWadAssetIds =
            NormalizeWadAssetIds(
                project.DefaultWadAssetIds,
                "Project default WAD asset");

        if (project.GameBinding is not null)
        {
            project.GameBinding.GameInstallationDirectory =
                NormalizeAbsolutePath(
                    project.GameBinding.GameInstallationDirectory,
                    "Game installation directory");

            project.GameBinding.RuntimeModDirectory =
                NormalizeAbsolutePath(
                    project.GameBinding.RuntimeModDirectory,
                    "Runtime mod directory");
        }

        project.Maps ??= new List<CompanionProjectMap>();

        HashSet<string> mapPaths =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (CompanionProjectMap map in project.Maps)
        {
            if (map is null)
            {
                throw new InvalidDataException(
                    "Companion project contains an empty map entry.");
            }

            map.Path =
                NormalizeRelativeMapPath(map.Path);

            if (!mapPaths.Add(map.Path))
            {
                throw new InvalidDataException(
                    $"Companion project contains duplicate map path " +
                    $"'{map.Path}'.");
            }

            if (string.IsNullOrWhiteSpace(map.DisplayName))
            {
                map.DisplayName =
                    Path.GetFileNameWithoutExtension(map.Path);
            }
            else
            {
                map.DisplayName = map.DisplayName.Trim();
            }

            map.WadAssetIds =
                NormalizeWadAssetIds(
                    map.WadAssetIds,
                    $"Map '{map.DisplayName}' WAD asset");
        }

        if (string.IsNullOrWhiteSpace(project.ActiveMapPath))
        {
            project.ActiveMapPath = null;
        }
        else
        {
            string activeMapPath =
                NormalizeRelativeMapPath(
                    project.ActiveMapPath);

            if (!mapPaths.Contains(activeMapPath))
            {
                throw new InvalidDataException(
                    "Active map must reference a map registered " +
                    "in the Companion project.");
            }

            project.ActiveMapPath = activeMapPath;
        }
    }

    private static List<string> NormalizeWadAssetIds(
        IEnumerable<string>? assetIds,
        string description)
    {
        List<string> normalized =
            new();

        foreach (string assetId in
                 assetIds ??
                 Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(
                    assetId))
            {
                continue;
            }

            string value =
                assetId.Trim().ToUpperInvariant();

            if (value.Length !=
                    64 ||
                value.Any(
                    character =>
                        !Uri.IsHexDigit(
                            character)))
            {
                throw new InvalidDataException(
                    $"{description} ID '{assetId}' is invalid.");
            }

            if (!normalized.Contains(
                    value,
                    StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(
                    value);
            }
        }

        return normalized;
    }

    private static void MigrateToCurrentSchema(
        CompanionProjectManifest project)
    {
        if (project.SchemaVersion ==
            CompanionProjectManifest.CurrentSchemaVersion)
        {
            return;
        }

        if (project.SchemaVersion == 1)
        {
            project.GameBinding =
                null;

            project.SchemaVersion =
                2;
        }

        if (project.SchemaVersion == 2)
        {
            project.PreferredTextureArchiveFormat =
                GetLegacyTextureArchivePreference(
                    project.GameId);

            project.SchemaVersion =
                4;
        }

        if (project.SchemaVersion == 3)
        {
            project.SchemaVersion =
                4;
        }

        if (project.SchemaVersion == 4)
        {
            project.DefaultWadAssetIds ??=
                new List<string>();

            project.Maps ??=
                new List<CompanionProjectMap>();

            foreach (CompanionProjectMap map in
                     project.Maps)
            {
                map.WadAssetIds ??=
                    new List<string>();
            }

            project.SchemaVersion =
                CompanionProjectManifest.CurrentSchemaVersion;

            return;
        }

        throw new InvalidDataException(
            $"Unsupported Companion project schema version " +
            $"'{project.SchemaVersion}'. " +
            $"Expected version 1, 2, 3, 4, or " +
            $"'{CompanionProjectManifest.CurrentSchemaVersion}'.");
    }
    private static string? NormalizeProjectTextureArchiveFormat(
        string gameId,
        string? preferredTextureArchiveFormat)
    {
        if (string.IsNullOrWhiteSpace(
                preferredTextureArchiveFormat))
        {
            if (CompanionGameProfiles.TryGet(
                    gameId,
                    out CompanionGameProfile? profile) &&
                profile is not null)
            {
                if (profile.CanChooseTextureArchiveFormat)
                {
                    return null;
                }

                return profile.DefaultTextureArchiveFormat;
            }

            return null;
        }

        return ResolveTextureArchiveFormat(
            gameId,
            preferredTextureArchiveFormat);
    }

    private static string? GetLegacyTextureArchivePreference(
        string? gameId)
    {
        if (CompanionGameProfiles.TryGet(
                gameId,
                out CompanionGameProfile? profile) &&
            profile is not null)
        {
            return profile.CanChooseTextureArchiveFormat
                ? null
                : profile.DefaultTextureArchiveFormat;
        }

        return null;
    }

    private static string ResolveTextureArchiveFormat(
        string gameId,
        string? preferredTextureArchiveFormat)
    {
        string resolved;

        try
        {
            resolved =
                string.IsNullOrWhiteSpace(
                    preferredTextureArchiveFormat)
                    ? GetDefaultTextureArchiveFormat(
                        gameId)
                    : CompanionTextureArchiveFormats.Normalize(
                        preferredTextureArchiveFormat);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Companion project contains an unsupported texture archive format.",
                exception);
        }

        if (CompanionGameProfiles.TryGet(
                gameId,
                out CompanionGameProfile? profile) &&
            profile is not null &&
            !profile.SupportsTextureArchiveFormat(
                resolved))
        {
            throw new InvalidDataException(
                $"Texture archive format " +
                $"'{CompanionTextureArchiveFormats.GetDisplayName(resolved)}' " +
                $"is not supported by {profile.DisplayName} projects.");
        }

        return resolved;
    }

    private static string GetDefaultTextureArchiveFormat(
        string? gameId)
    {
        if (CompanionGameProfiles.TryGet(
                gameId,
                out CompanionGameProfile? profile) &&
            profile is not null)
        {
            return profile.DefaultTextureArchiveFormat;
        }

        return CompanionTextureArchiveFormats.Wad2;
    }

    private static string NormalizeAbsolutePath(
        string path,
        string description)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException(
                $"{description} cannot be empty.");
        }

        string trimmed =
            path.Trim();

        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new InvalidDataException(
                $"{description} must be an absolute path.");
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(
                $"{description} is not a valid path.",
                exception);
        }
    }

    private static string ValidateProjectFilePath(
        string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            throw new ArgumentException(
                "Companion project file path cannot be empty.",
                nameof(projectFilePath));
        }

        string fullPath =
            Path.GetFullPath(projectFilePath);

        if (!string.Equals(
                Path.GetExtension(fullPath),
                ProjectExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Companion project files must use the " +
                $"'{ProjectExtension}' extension.",
                nameof(projectFilePath));
        }

        return fullPath;
    }

    private static string NormalizeRelativeMapPath(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException(
                "Map path cannot be empty.");
        }

        string normalized =
            path.Trim()
                .Replace('\\', '/');

        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException(
                "Map paths stored in a Companion project " +
                "must be relative.");
        }

        string[] segments =
            normalized.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            throw new InvalidDataException(
                "Map path cannot be empty.");
        }

        if (segments.Any(segment => segment == ".."))
        {
            throw new InvalidDataException(
                "Map path cannot escape the Companion project directory.");
        }

        normalized =
            string.Join(
                '/',
                segments.Where(segment => segment != "."));

        if (!string.Equals(
                Path.GetExtension(normalized),
                ".map",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Companion map entries must reference .map files.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
