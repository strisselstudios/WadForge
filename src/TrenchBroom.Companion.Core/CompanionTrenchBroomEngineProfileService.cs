using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TrenchBroom.Companion.Core;

public sealed record CompanionTrenchBroomEngineProfileResult(
    string ProfilePath,
    string ProfileName,
    string LauncherPath);

public static class CompanionTrenchBroomEngineProfileService
{
    private const string ProfileName =
        "DUSK Moddable";

    private const string GameEngineProfilesFileName =
        "GameEngineProfiles.cfg";

    public static CompanionTrenchBroomEngineProfileResult EnsureDuskProfile(
        string trenchBroomExecutablePath,
        string gameInstallationDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                trenchBroomExecutablePath))
        {
            throw new ArgumentException(
                "A TrenchBroom executable path is required.",
                nameof(trenchBroomExecutablePath));
        }

        if (string.IsNullOrWhiteSpace(
                gameInstallationDirectory))
        {
            throw new ArgumentException(
                "A DUSK installation directory is required.",
                nameof(gameInstallationDirectory));
        }

        string trenchBroomPath =
            Path.GetFullPath(
                trenchBroomExecutablePath);

        if (!File.Exists(
                trenchBroomPath))
        {
            throw new FileNotFoundException(
                "The TrenchBroom executable does not exist.",
                trenchBroomPath);
        }

        string installationDirectory =
            Path.GetDirectoryName(
                trenchBroomPath) ??
            throw new InvalidDataException(
                "Could not determine the TrenchBroom installation directory.");

        string duskInstallationDirectory =
            Path.GetFullPath(
                gameInstallationDirectory);

        string launcherPath =
            Path.Combine(
                duskInstallationDirectory,
                "SDK",
                "dusk_win.bat");

        if (!File.Exists(
                launcherPath))
        {
            throw new FileNotFoundException(
                "Companion could not find DUSK Moddable's SDK launcher. " +
                "Verify the selected DUSK installation contains SDK\\dusk_win.bat.",
                launcherPath);
        }

        string portableDuskConfigDirectory =
            Path.Combine(
                installationDirectory,
                "games",
                "DUSK");

        Directory.CreateDirectory(
            portableDuskConfigDirectory);

        string profilePath =
            Path.Combine(
                portableDuskConfigDirectory,
                GameEngineProfilesFileName);

        JsonObject root =
            LoadOrCreateRoot(
                profilePath);

        JsonArray profiles =
            EnsureProfilesArray(
                root);

        RemoveManagedProfile(
            profiles);

        profiles.Add(
            new JsonObject
            {
                ["name"] =
                    ProfileName,

                ["parameters"] =
                    string.Empty,

                ["path"] =
                    launcherPath
            });

        root["version"] =
            1;

        WriteAtomic(
            profilePath,
            root.ToJsonString(
                new JsonSerializerOptions
                {
                    Encoder =
                        JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

                    WriteIndented =
                        true
                }));

        return new CompanionTrenchBroomEngineProfileResult(
            profilePath,
            ProfileName,
            launcherPath);
    }

    private static JsonObject LoadOrCreateRoot(
        string profilePath)
    {
        if (!File.Exists(
                profilePath))
        {
            return new JsonObject
            {
                ["profiles"] =
                    new JsonArray(),

                ["version"] =
                    1
            };
        }

        string existingText =
            File.ReadAllText(
                profilePath);

        JsonNode? parsed;

        try
        {
            parsed =
                JsonNode.Parse(
                    existingText);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The managed TrenchBroom game-engine profile file is not valid JSON. " +
                "Companion left it untouched.",
                exception);
        }

        if (parsed is not JsonObject root)
        {
            throw new InvalidDataException(
                "The managed TrenchBroom game-engine profile file does not contain a JSON object. " +
                "Companion left it untouched.");
        }

        return root;
    }

    private static JsonArray EnsureProfilesArray(
        JsonObject root)
    {
        if (root["profiles"] is null)
        {
            JsonArray created =
                new();

            root["profiles"] =
                created;

            return created;
        }

        if (root["profiles"] is JsonArray profiles)
        {
            return profiles;
        }

        throw new InvalidDataException(
            "The managed TrenchBroom game-engine profile file contains an invalid profiles value. " +
            "Companion left it untouched.");
    }

    private static void RemoveManagedProfile(
        JsonArray profiles)
    {
        for (int index =
                 profiles.Count - 1;
             index >= 0;
             index--)
        {
            if (profiles[index] is not
                    JsonObject profile ||
                profile["name"] is not
                    JsonValue nameValue ||
                !nameValue.TryGetValue<string>(
                    out string? name))
            {
                continue;
            }

            if (string.Equals(
                    name,
                    ProfileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                profiles.RemoveAt(
                    index);
            }
        }
    }

    private static void WriteAtomic(
        string destinationPath,
        string content)
    {
        string temporaryPath =
            destinationPath +
            ".tmp-" +
            Guid.NewGuid().ToString("N");

        try
        {
            File.WriteAllText(
                temporaryPath,
                content +
                Environment.NewLine,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            File.Move(
                temporaryPath,
                destinationPath,
                overwrite: true);
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
}
