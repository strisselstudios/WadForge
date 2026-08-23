using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private const string ProfileName = "Companion - DUSK";
    private const string CompilationProfilesFileName = "CompilationProfiles.cfg";

    public static CompanionTrenchBroomCompilationProfileResult EnsureDuskProfile(
        string trenchBroomExecutablePath,
        CompanionEricwToolchainStatus toolchain,
        string runtimeModDirectory)
    {
        CompanionCompilerOptionSchema schema =
            CompanionCompilerOptionSchemaService.GetRequired(
                CompanionGameProfiles.Dusk.Id,
                toolchain.Version);

        return EnsureDuskProfile(
            trenchBroomExecutablePath,
            toolchain,
            runtimeModDirectory,
            CompanionBuildSettingsService.CreateDefaults(schema));
    }

    public static CompanionTrenchBroomCompilationProfileResult EnsureDuskProfile(
        string trenchBroomExecutablePath,
        CompanionEricwToolchainStatus toolchain,
        string runtimeModDirectory,
        CompanionBuildSettings buildSettings)
    {
        if (string.IsNullOrWhiteSpace(trenchBroomExecutablePath))
        {
            throw new ArgumentException("A TrenchBroom executable path is required.", nameof(trenchBroomExecutablePath));
        }
        if (toolchain is null || !toolchain.IsReady)
        {
            throw new InvalidOperationException(
                "A complete managed ericw-tools installation is required before creating a compile profile.");
        }
        ArgumentNullException.ThrowIfNull(buildSettings);
        if (string.IsNullOrWhiteSpace(runtimeModDirectory))
        {
            throw new ArgumentException("A DUSK runtime project directory is required.", nameof(runtimeModDirectory));
        }

        CompanionCompilerOptionSchema schema =
            CompanionCompilerOptionSchemaService.GetRequired(
                CompanionGameProfiles.Dusk.Id,
                toolchain.Version);
        CompanionBuildSettingsService.ValidateForSave(buildSettings, schema);

        string trenchBroomPath = Path.GetFullPath(trenchBroomExecutablePath);
        if (!File.Exists(trenchBroomPath))
        {
            throw new FileNotFoundException("The TrenchBroom executable does not exist.", trenchBroomPath);
        }

        string installationDirectory =
            Path.GetDirectoryName(trenchBroomPath) ??
            throw new InvalidDataException("Could not determine the TrenchBroom installation directory.");

        string portableDuskConfigDirectory = Path.Combine(installationDirectory, "games", "DUSK");
        Directory.CreateDirectory(portableDuskConfigDirectory);

        string profilePath = Path.Combine(portableDuskConfigDirectory, CompilationProfilesFileName);
        string runtimeMapsDirectory = Path.Combine(Path.GetFullPath(runtimeModDirectory), "maps");
        Directory.CreateDirectory(runtimeMapsDirectory);

        JsonObject root = LoadOrCreateRoot(profilePath);
        JsonArray profiles = EnsureProfilesArray(root);
        RemoveManagedProfile(profiles);
        profiles.Add(CreateDuskProfile(toolchain, runtimeMapsDirectory, buildSettings, schema));
        root["version"] = 1;

        WriteAtomic(
            profilePath,
            root.ToJsonString(new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            }));

        return new CompanionTrenchBroomCompilationProfileResult(
            profilePath,
            ProfileName,
            runtimeMapsDirectory);
    }

    private static JsonObject LoadOrCreateRoot(string profilePath)
    {
        if (!File.Exists(profilePath))
        {
            return new JsonObject { ["profiles"] = new JsonArray(), ["version"] = 1 };
        }

        try
        {
            JsonNode? parsed = JsonNode.Parse(File.ReadAllText(profilePath));
            if (parsed is JsonObject root)
            {
                return root;
            }
            throw new InvalidDataException(
                "The managed TrenchBroom compilation profile file does not contain a JSON object. Companion left it untouched.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The managed TrenchBroom compilation profile file is not valid JSON. Companion left it untouched.",
                exception);
        }
    }

    private static JsonArray EnsureProfilesArray(JsonObject root)
    {
        if (root["profiles"] is null)
        {
            JsonArray created = new();
            root["profiles"] = created;
            return created;
        }
        if (root["profiles"] is JsonArray profiles)
        {
            return profiles;
        }
        throw new InvalidDataException(
            "The managed TrenchBroom compilation profile file contains an invalid profiles value. Companion left it untouched.");
    }

    private static void RemoveManagedProfile(JsonArray profiles)
    {
        for (int index = profiles.Count - 1; index >= 0; index--)
        {
            if (profiles[index] is JsonObject profile &&
                profile["name"] is JsonValue nameValue &&
                nameValue.TryGetValue<string>(out string? name) &&
                string.Equals(name, ProfileName, StringComparison.OrdinalIgnoreCase))
            {
                profiles.RemoveAt(index);
            }
        }
    }

    private static JsonObject CreateDuskProfile(
        CompanionEricwToolchainStatus toolchain,
        string runtimeMapsDirectory,
        CompanionBuildSettings settings,
        CompanionCompilerOptionSchema schema)
    {
        string qbspPath = ToTrenchBroomPath(toolchain.QbspPath);
        string visPath = ToTrenchBroomPath(toolchain.VisPath);
        string lightPath = ToTrenchBroomPath(toolchain.LightPath);
        string runtimeMapsPath = ToTrenchBroomPath(runtimeMapsDirectory);

        const string buildMap = "${MAP_DIR_PATH}/../build/${MAP_BASE_NAME}-compile.map";
        const string buildBsp = "${MAP_DIR_PATH}/../build/${MAP_BASE_NAME}.bsp";
        const string buildLit = "${MAP_DIR_PATH}/../build/${MAP_BASE_NAME}.lit";
        const string buildLit2 = "${MAP_DIR_PATH}/../build/${MAP_BASE_NAME}.lit2";
        const string buildLux = "${MAP_DIR_PATH}/../build/${MAP_BASE_NAME}.lux";
        const string projectWads = "${MAP_DIR_PATH}/../wads";

        JsonArray tasks = new();
        tasks.Add(new JsonObject { ["target"] = buildMap, ["type"] = "export" });
        tasks.Add(new JsonObject
        {
            ["parameters"] = BuildQbspParameters(settings, schema, projectWads, buildMap, buildBsp),
            ["tool"] = qbspPath,
            ["treatNonZeroResultCodeAsError"] = true,
            ["type"] = "tool"
        });
        tasks.Add(new JsonObject
        {
            ["enabled"] = false,
            ["parameters"] = BuildVisParameters(settings, schema, buildBsp),
            ["tool"] = visPath,
            ["treatNonZeroResultCodeAsError"] = true,
            ["type"] = "tool"
        });
        tasks.Add(new JsonObject
        {
            ["parameters"] = BuildLightParameters(settings, schema, buildBsp),
            ["tool"] = lightPath,
            ["treatNonZeroResultCodeAsError"] = true,
            ["type"] = "tool"
        });
        tasks.Add(CopyTask(buildBsp, runtimeMapsPath, true));
        tasks.Add(CopyTask(buildLit, runtimeMapsPath, settings.IsEnabled("light.lit")));
        tasks.Add(CopyTask(buildLit2, runtimeMapsPath, settings.IsEnabled("light.lit2")));
        tasks.Add(CopyTask(buildLux, runtimeMapsPath, settings.IsEnabled("light.lux")));

        return new JsonObject
        {
            ["name"] = ProfileName,
            ["tasks"] = tasks,
            ["workdir"] = "${MAP_DIR_PATH}"
        };
    }

    private static JsonObject CopyTask(string source, string target, bool enabled) =>
        new() { ["enabled"] = enabled, ["source"] = source, ["target"] = target, ["type"] = "copy" };

    private static string BuildQbspParameters(
        CompanionBuildSettings settings,
        CompanionCompilerOptionSchema schema,
        string projectWads,
        string buildMap,
        string buildBsp)
    {
        List<string> arguments = BuildOptionArguments(settings, schema, CompanionCompilerTool.Qbsp);
        arguments.Add($"-wadpath \"{projectWads}\"");
        arguments.Add($"\"{buildMap}\"");
        arguments.Add($"\"{buildBsp}\"");
        return string.Join(" ", arguments);
    }

    private static string BuildVisParameters(
        CompanionBuildSettings settings,
        CompanionCompilerOptionSchema schema,
        string buildBsp)
    {
        List<string> arguments = BuildOptionArguments(settings, schema, CompanionCompilerTool.Vis);
        arguments.Add($"\"{buildBsp}\"");
        return string.Join(" ", arguments);
    }

    private static string BuildLightParameters(
        CompanionBuildSettings settings,
        CompanionCompilerOptionSchema schema,
        string buildBsp)
    {
        List<string> arguments = BuildOptionArguments(settings, schema, CompanionCompilerTool.Light);
        arguments.AddRange(BuildOptionArguments(settings, schema, CompanionCompilerTool.LightGlobal));
        arguments.Add($"\"{buildBsp}\"");
        return string.Join(" ", arguments);
    }

    private static List<string> BuildOptionArguments(
        CompanionBuildSettings settings,
        CompanionCompilerOptionSchema schema,
        CompanionCompilerTool tool)
    {
        List<string> arguments = new();

        foreach (CompanionCompilerOptionDefinition definition in
                 schema.Options.Where(option => option.Tool == tool && option.Available))
        {
            if (!settings.Options.TryGetValue(definition.Id, out CompanionCompilerOptionSetting? setting) ||
                !setting.Enabled)
            {
                continue;
            }

            arguments.Add(definition.Flag);
            if (definition.ValueKind == CompanionCompilerOptionValueKind.Flag)
            {
                continue;
            }

            string value = setting.Value.Trim();
            if (definition.ValueKind == CompanionCompilerOptionValueKind.Threads &&
                string.Equals(value, CompanionBuildSettingValues.AutomaticThreads, StringComparison.OrdinalIgnoreCase))
            {
                value = "${CPU_COUNT - 1}";
            }

            if (definition.ValueKind == CompanionCompilerOptionValueKind.Text)
            {
                arguments.Add($"\"{value.Replace("\"", "\\\"")}\"");
            }
            else
            {
                arguments.Add(value);
            }
        }

        return arguments;
    }

    private static string ToTrenchBroomPath(string path) =>
        Path.GetFullPath(path).Replace('\\', '/');

    private static void WriteAtomic(string destinationPath, string content)
    {
        string temporaryPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(
                temporaryPath,
                content + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
