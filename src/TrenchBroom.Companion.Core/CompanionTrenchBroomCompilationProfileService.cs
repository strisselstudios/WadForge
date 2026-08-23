using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TrenchBroom.Companion.Core;

public sealed record CompanionTrenchBroomCompilationProfileResult(
    string ProfilePath,
    string ProfileName,
    string RuntimeMapsDirectory);

public static class CompanionTrenchBroomCompilationProfileService
{
    private const string ProfileName =
        "Companion - DUSK";

    private const string CompilationProfilesFileName =
        "CompilationProfiles.cfg";

    public static CompanionTrenchBroomCompilationProfileResult EnsureDuskProfile(
        string trenchBroomExecutablePath,
        CompanionEricwToolchainStatus toolchain,
        string runtimeModDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                trenchBroomExecutablePath))
        {
            throw new ArgumentException(
                "A TrenchBroom executable path is required.",
                nameof(trenchBroomExecutablePath));
        }

        if (toolchain is null ||
            !toolchain.IsReady)
        {
            throw new InvalidOperationException(
                "A complete managed ericw-tools installation is required before creating a compile profile.");
        }

        if (string.IsNullOrWhiteSpace(
                runtimeModDirectory))
        {
            throw new ArgumentException(
                "A DUSK runtime project directory is required.",
                nameof(runtimeModDirectory));
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
                CompilationProfilesFileName);

        string runtimeMapsDirectory =
            Path.Combine(
                Path.GetFullPath(
                    runtimeModDirectory),
                "maps");

        Directory.CreateDirectory(
            runtimeMapsDirectory);

        JsonObject root =
            LoadOrCreateRoot(
                profilePath);

        JsonArray profiles =
            EnsureProfilesArray(
                root);

        RemoveManagedProfile(
            profiles);

        profiles.Add(
            CreateDuskProfile(
                toolchain,
                runtimeMapsDirectory));

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

        return new CompanionTrenchBroomCompilationProfileResult(
            profilePath,
            ProfileName,
            runtimeMapsDirectory);
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
                "The managed TrenchBroom compilation profile file is not valid JSON. " +
                "Companion left it untouched.",
                exception);
        }

        if (parsed is not JsonObject root)
        {
            throw new InvalidDataException(
                "The managed TrenchBroom compilation profile file does not contain a JSON object. " +
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
            "The managed TrenchBroom compilation profile file contains an invalid profiles value. " +
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

    private static JsonObject CreateDuskProfile(
        CompanionEricwToolchainStatus toolchain,
        string runtimeMapsDirectory)
    {
        string qbspPath =
            ToTrenchBroomPath(
                toolchain.QbspPath);

        string visPath =
            ToTrenchBroomPath(
                toolchain.VisPath);

        string lightPath =
            ToTrenchBroomPath(
                toolchain.LightPath);

        string runtimeMapsPath =
            ToTrenchBroomPath(
                runtimeMapsDirectory);

        const string buildMap =
            "${MAP_DIR_PATH}/../build/${MAP_BASE_NAME}-compile.map";

        const string buildBsp =
            "${MAP_DIR_PATH}/../build/${MAP_BASE_NAME}.bsp";

        const string buildLit =
            "${MAP_DIR_PATH}/../build/${MAP_BASE_NAME}.lit";

        const string projectWads =
            "${MAP_DIR_PATH}/../wads";

        JsonArray tasks =
            new();

        tasks.Add(
            new JsonObject
            {
                ["target"] =
                    buildMap,

                ["type"] =
                    "export"
            });

        tasks.Add(
            new JsonObject
            {
                ["parameters"] =
                    $"-wadpath \"{projectWads}\" \"{buildMap}\" \"{buildBsp}\"",

                ["tool"] =
                    qbspPath,

                ["treatNonZeroResultCodeAsError"] =
                    true,

                ["type"] =
                    "tool"
            });

        tasks.Add(
            new JsonObject
            {
                ["enabled"] =
                    false,

                ["parameters"] =
                    $"-fast -threads ${{CPU_COUNT - 1}} \"{buildBsp}\"",

                ["tool"] =
                    visPath,

                ["treatNonZeroResultCodeAsError"] =
                    true,

                ["type"] =
                    "tool"
            });

        tasks.Add(
            new JsonObject
            {
                ["parameters"] =
                    $"-lit -threads ${{CPU_COUNT - 1}} \"{buildBsp}\"",

                ["tool"] =
                    lightPath,

                ["treatNonZeroResultCodeAsError"] =
                    true,

                ["type"] =
                    "tool"
            });

        tasks.Add(
            new JsonObject
            {
                ["source"] =
                    buildBsp,

                ["target"] =
                    runtimeMapsPath,

                ["type"] =
                    "copy"
            });

        tasks.Add(
            new JsonObject
            {
                ["source"] =
                    buildLit,

                ["target"] =
                    runtimeMapsPath,

                ["type"] =
                    "copy"
            });

        return new JsonObject
        {
            ["name"] =
                ProfileName,

            ["tasks"] =
                tasks,

            ["workdir"] =
                "${MAP_DIR_PATH}"
        };
    }

    private static string ToTrenchBroomPath(
        string path)
    {
        return Path.GetFullPath(
                path)
            .Replace(
                '\\',
                '/');
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
