using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TrenchBroom.Companion.Core;

public sealed record CompanionDuskTrenchBroomEnvironment(
    string GamePath,
    string Id1Directory,
    string PreferencesPath);

public static class CompanionDuskTrenchBroomEnvironmentService
{
    public const string DuskGamePathPreferenceKey =
        "Games/DUSK/Path";

    private const string DefaultGameDirectoryName =
        "id1";

    private const string ManagedMarkerFileName =
        ".trenchbroom-companion-managed";

    public static CompanionDuskTrenchBroomEnvironment Ensure(
        string trenchBroomExecutablePath,
        string managedDataRoot)
    {
        if (string.IsNullOrWhiteSpace(
                trenchBroomExecutablePath))
        {
            throw new ArgumentException(
                "A TrenchBroom executable path is required.",
                nameof(trenchBroomExecutablePath));
        }

        string executablePath =
            Path.GetFullPath(
                trenchBroomExecutablePath);

        if (!File.Exists(
                executablePath))
        {
            throw new FileNotFoundException(
                "The Companion-managed TrenchBroom executable does not exist.",
                executablePath);
        }

        string trenchBroomDirectory =
            Path.GetDirectoryName(
                executablePath) ??
            throw new InvalidOperationException(
                "The Companion-managed TrenchBroom directory could not be resolved.");

        string expectedTrenchBroomDirectory =
            CompanionManagedDataRootService
                .GetTrenchBroomDirectory(
                    managedDataRoot);

        if (!string.Equals(
                Path.GetFullPath(
                    trenchBroomDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                Path.GetFullPath(
                    expectedTrenchBroomDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected TrenchBroom executable is not inside Companion's configured managed data root.");
        }

        string gamePath =
            CompanionManagedDataRootService
                .GetDuskAuthoringDirectory(
                    managedDataRoot);

        string id1Directory =
            Path.Combine(
                gamePath,
                DefaultGameDirectoryName);

        Directory.CreateDirectory(
            id1Directory);

        string markerPath =
            Path.Combine(
                gamePath,
                ManagedMarkerFileName);

        if (!File.Exists(
                markerPath))
        {
            File.WriteAllText(
                markerPath,
                "Managed DUSK authoring environment for TrenchBroom Companion." +
                Environment.NewLine,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        false));
        }

        string configDirectory =
            Path.Combine(
                trenchBroomDirectory,
                "config");

        Directory.CreateDirectory(
            configDirectory);

        string preferencesPath =
            Path.Combine(
                configDirectory,
                "Preferences.json");

        JsonObject preferences =
            ReadPreferences(
                preferencesPath);

        string normalizedGamePath =
            Path.GetFullPath(
                gamePath);

        bool requiresWrite =
            true;

        if (preferences[DuskGamePathPreferenceKey] is
                JsonValue existingValue &&
            existingValue.TryGetValue(
                out string? existingGamePath) &&
            !string.IsNullOrWhiteSpace(
                existingGamePath))
        {
            string? normalizedExistingPath =
                TryNormalizePath(
                    existingGamePath);

            requiresWrite =
                !string.Equals(
                    normalizedExistingPath,
                    normalizedGamePath,
                    StringComparison.OrdinalIgnoreCase);
        }

        if (requiresWrite)
        {
            preferences[DuskGamePathPreferenceKey] =
                normalizedGamePath;

            WritePreferencesAtomically(
                preferencesPath,
                preferences);
        }

        return new CompanionDuskTrenchBroomEnvironment(
            normalizedGamePath,
            Path.GetFullPath(
                id1Directory),
            preferencesPath);
    }

    private static JsonObject ReadPreferences(
        string preferencesPath)
    {
        if (!File.Exists(
                preferencesPath))
        {
            return new JsonObject();
        }

        string json =
            File.ReadAllText(
                preferencesPath,
                Encoding.UTF8);

        JsonNode? root;

        try
        {
            root =
                JsonNode.Parse(
                    json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The Companion-managed TrenchBroom Preferences.json file is not valid JSON. " +
                "Companion will not overwrite it.",
                exception);
        }

        if (root is not
            JsonObject preferences)
        {
            throw new InvalidOperationException(
                "The Companion-managed TrenchBroom Preferences.json file does not contain a JSON object. " +
                "Companion will not overwrite it.");
        }

        return preferences;
    }

    private static void WritePreferencesAtomically(
        string preferencesPath,
        JsonObject preferences)
    {
        string directory =
            Path.GetDirectoryName(
                preferencesPath) ??
            throw new InvalidOperationException(
                "The TrenchBroom preferences directory could not be resolved.");

        string temporaryPath =
            Path.Combine(
                directory,
                $"Preferences.json.companion-{Guid.NewGuid():N}.tmp");

        try
        {
            string json =
                preferences.ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented =
                            true
                    }) +
                Environment.NewLine;

            File.WriteAllText(
                temporaryPath,
                json,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        false));

            _ =
                JsonNode.Parse(
                    File.ReadAllText(
                        temporaryPath,
                        Encoding.UTF8)) ??
                throw new InvalidOperationException(
                    "The temporary TrenchBroom preferences file could not be validated.");

            File.Move(
                temporaryPath,
                preferencesPath,
                overwrite:
                    true);
        }
        finally
        {
            if (File.Exists(
                    temporaryPath))
            {
                File.Delete(
                    temporaryPath);
            }
        }
    }

    private static string? TryNormalizePath(
        string path)
    {
        try
        {
            return Path.GetFullPath(
                path);
        }
        catch
        {
            return null;
        }
    }
}
