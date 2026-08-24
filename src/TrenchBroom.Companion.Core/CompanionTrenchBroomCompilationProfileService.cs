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

public sealed record CompanionDuskCompilationProfileContext(
    CompanionDuskCompileMode Mode,
    CompanionEricwToolchainStatus? HalfLifeToolchain,
    string? CompanionExecutablePath,
    string? DuskPalettePath)
{
    public static CompanionDuskCompilationProfileContext CreateQuake() =>
        new(
            CompanionDuskCompileMode.QuakeBsp,
            null,
            null,
            null);
}

public static class CompanionTrenchBroomCompilationProfileService
{
    private const string ProfileName =
        "Companion - DUSK";

    private const string CompilationProfilesFileName =
        "CompilationProfiles.cfg";

    private static readonly HashSet<string>
        HalfLifeIgnoredQbspOptionIds =
            new(
                new[]
                {
                    "qbsp.bsp2",
                    "qbsp.2psb",
                    "qbsp.transsky",
                    "qbsp.bspleak",
                    "qbsp.oldleak"
                },
                StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string>
        HalfLifeIgnoredLightOptionIds =
            new(
                new[]
                {
                    "light.lit",
                    "light.lit2",
                    "light.lux",
                    "light.bspxlit",
                    "light.bspx",
                    "light.novanilla",
                    "light.phongdebug",
                    "light.bouncedebug"
                },
                StringComparer.OrdinalIgnoreCase);

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
            CompanionBuildSettingsService.CreateDefaults(
                schema),
            CompanionDuskCompilationProfileContext.CreateQuake());
    }

    public static CompanionTrenchBroomCompilationProfileResult EnsureDuskProfile(
        string trenchBroomExecutablePath,
        CompanionEricwToolchainStatus toolchain,
        string runtimeModDirectory,
        CompanionBuildSettings buildSettings)
    {
        return EnsureDuskProfile(
            trenchBroomExecutablePath,
            toolchain,
            runtimeModDirectory,
            buildSettings,
            CompanionDuskCompilationProfileContext.CreateQuake());
    }

    public static CompanionTrenchBroomCompilationProfileResult EnsureDuskProfile(
        string trenchBroomExecutablePath,
        CompanionEricwToolchainStatus toolchain,
        string runtimeModDirectory,
        CompanionBuildSettings buildSettings,
        CompanionDuskCompilationProfileContext context)
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

        ArgumentNullException.ThrowIfNull(
            buildSettings);

        ArgumentNullException.ThrowIfNull(
            context);

        if (string.IsNullOrWhiteSpace(
                runtimeModDirectory))
        {
            throw new ArgumentException(
                "A DUSK runtime project directory is required.",
                nameof(runtimeModDirectory));
        }

        bool halfLifeMode =
            context.Mode ==
            CompanionDuskCompileMode.HalfLifeBsp;

        if (halfLifeMode)
        {
            if (context.HalfLifeToolchain is null ||
                !context.HalfLifeToolchain.IsReady)
            {
                throw new InvalidOperationException(
                    "DUSK WAD3/Half-Life BSP mode requires the managed ericw-tools 2.0 toolchain.");
            }

            if (string.IsNullOrWhiteSpace(
                    context.CompanionExecutablePath) ||
                !File.Exists(
                    context.CompanionExecutablePath))
            {
                throw new FileNotFoundException(
                    "DUSK WAD3/Half-Life BSP mode requires the Companion executable for compile preparation.",
                    context.CompanionExecutablePath);
            }

            if (string.IsNullOrWhiteSpace(
                    context.DuskPalettePath) ||
                !File.Exists(
                    context.DuskPalettePath))
            {
                throw new FileNotFoundException(
                    "DUSK WAD3/Half-Life BSP mode requires the managed DUSK project palette.",
                    context.DuskPalettePath);
            }
        }

        CompanionCompilerOptionSchema schema =
            CompanionCompilerOptionSchemaService.GetRequired(
                CompanionGameProfiles.Dusk.Id,
                toolchain.Version);

        CompanionBuildSettingsService.ValidateForSave(
            buildSettings,
            schema);

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
                runtimeMapsDirectory,
                buildSettings,
                schema,
                context));

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

        try
        {
            JsonNode? parsed =
                JsonNode.Parse(
                    File.ReadAllText(
                        profilePath));

            if (parsed is
                JsonObject root)
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

        if (root["profiles"] is
            JsonArray profiles)
        {
            return profiles;
        }

        throw new InvalidDataException(
            "The managed TrenchBroom compilation profile file contains an invalid profiles value. Companion left it untouched.");
    }

    private static void RemoveManagedProfile(
        JsonArray profiles)
    {
        for (int index =
                 profiles.Count -
                 1;
             index >=
                 0;
             index--)
        {
            if (profiles[index] is
                    JsonObject profile &&
                profile["name"] is
                    JsonValue nameValue &&
                nameValue.TryGetValue<string>(
                    out string? name) &&
                string.Equals(
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
        CompanionEricwToolchainStatus stableToolchain,
        string runtimeMapsDirectory,
        CompanionBuildSettings settings,
        CompanionCompilerOptionSchema schema,
        CompanionDuskCompilationProfileContext context)
    {
        bool halfLifeMode =
            context.Mode ==
            CompanionDuskCompileMode.HalfLifeBsp;

        CompanionEricwToolchainStatus compileToolchain =
            halfLifeMode
                ? context.HalfLifeToolchain!
                : stableToolchain;

        string qbspPath =
            ToTrenchBroomPath(
                compileToolchain.QbspPath);

        string visPath =
            ToTrenchBroomPath(
                compileToolchain.VisPath);

        string lightPath =
            ToTrenchBroomPath(
                compileToolchain.LightPath);

        string runtimeMapsPath =
            ToTrenchBroomPath(
                runtimeMapsDirectory);

        const string buildMap =
            "${MAP_DIR_PATH}/../build/${MAP_BASE_NAME}-compile.map";

        const string buildBsp =
            "${MAP_DIR_PATH}/../build/${MAP_BASE_NAME}.bsp";

        const string buildLit =
            "${MAP_DIR_PATH}/../build/${MAP_BASE_NAME}.lit";

        const string buildLit2 =
            "${MAP_DIR_PATH}/../build/${MAP_BASE_NAME}.lit2";

        const string buildLux =
            "${MAP_DIR_PATH}/../build/${MAP_BASE_NAME}.lux";

        const string projectWads =
            "${MAP_DIR_PATH}/../wads";

        const string halfLifeCacheWads =
            "${MAP_DIR_PATH}/../build/.dusk-hlbsp/${MAP_BASE_NAME}/wads";

        const string prepLog =
            "${MAP_DIR_PATH}/../build/${MAP_BASE_NAME}-dusk-compile-prep.log";

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

        if (halfLifeMode)
        {
            string companionPath =
                ToTrenchBroomPath(
                    context.CompanionExecutablePath!);

            string palettePath =
                ToTrenchBroomPath(
                    context.DuskPalettePath!);

            tasks.Add(
                new JsonObject
                {
                    ["parameters"] =
                        "--dusk-compile-prep " +
                        $"--map \"{buildMap}\" " +
                        $"--project-wads \"{projectWads}\" " +
                        $"--cache-wads \"{halfLifeCacheWads}\" " +
                        $"--palette \"{palettePath}\" " +
                        $"--log \"{prepLog}\"",

                    ["tool"] =
                        companionPath,

                    ["treatNonZeroResultCodeAsError"] =
                        true,

                    ["type"] =
                        "tool"
                });
        }

        tasks.Add(
            new JsonObject
            {
                ["parameters"] =
                    BuildQbspParameters(
                        settings,
                        schema,
                        halfLifeMode
                            ? halfLifeCacheWads
                            : projectWads,
                        buildMap,
                        buildBsp,
                        halfLifeMode),

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
                    BuildVisParameters(
                        settings,
                        schema,
                        buildBsp),

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
                    BuildLightParameters(
                        settings,
                        schema,
                        buildBsp,
                        halfLifeMode),

                ["tool"] =
                    lightPath,

                ["treatNonZeroResultCodeAsError"] =
                    true,

                ["type"] =
                    "tool"
            });

        tasks.Add(
            CopyTask(
                buildBsp,
                runtimeMapsPath,
                enabled: true));

        if (!halfLifeMode)
        {
            tasks.Add(
                CopyTask(
                    buildLit,
                    runtimeMapsPath,
                    settings.IsEnabled(
                        "light.lit")));

            tasks.Add(
                CopyTask(
                    buildLit2,
                    runtimeMapsPath,
                    settings.IsEnabled(
                        "light.lit2")));

            tasks.Add(
                CopyTask(
                    buildLux,
                    runtimeMapsPath,
                    settings.IsEnabled(
                        "light.lux")));
        }

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

    private static JsonObject CopyTask(
        string source,
        string target,
        bool enabled)
    {
        return new JsonObject
        {
            ["enabled"] =
                enabled,

            ["source"] =
                source,

            ["target"] =
                target,

            ["type"] =
                "copy"
        };
    }

    private static string BuildQbspParameters(
        CompanionBuildSettings settings,
        CompanionCompilerOptionSchema schema,
        string wadPath,
        string buildMap,
        string buildBsp,
        bool halfLifeMode)
    {
        List<string> arguments =
            BuildOptionArguments(
                settings,
                schema,
                CompanionCompilerTool.Qbsp,
                halfLifeMode);

        if (halfLifeMode)
        {
            arguments.Insert(
                0,
                "-hlbsp");
        }

        arguments.Add(
            $"-wadpath \"{wadPath}\"");

        arguments.Add(
            $"\"{buildMap}\"");

        arguments.Add(
            $"\"{buildBsp}\"");

        return string.Join(
            " ",
            arguments);
    }

    private static string BuildVisParameters(
        CompanionBuildSettings settings,
        CompanionCompilerOptionSchema schema,
        string buildBsp)
    {
        List<string> arguments =
            BuildOptionArguments(
                settings,
                schema,
                CompanionCompilerTool.Vis,
                halfLifeMode: false);

        arguments.Add(
            $"\"{buildBsp}\"");

        return string.Join(
            " ",
            arguments);
    }

    private static string BuildLightParameters(
        CompanionBuildSettings settings,
        CompanionCompilerOptionSchema schema,
        string buildBsp,
        bool halfLifeMode)
    {
        List<string> arguments =
            BuildOptionArguments(
                settings,
                schema,
                CompanionCompilerTool.Light,
                halfLifeMode);

        arguments.AddRange(
            BuildOptionArguments(
                settings,
                schema,
                CompanionCompilerTool.LightGlobal,
                halfLifeMode));

        arguments.Add(
            $"\"{buildBsp}\"");

        return string.Join(
            " ",
            arguments);
    }

    private static List<string> BuildOptionArguments(
        CompanionBuildSettings settings,
        CompanionCompilerOptionSchema schema,
        CompanionCompilerTool tool,
        bool halfLifeMode)
    {
        List<string> arguments =
            new();

        foreach (CompanionCompilerOptionDefinition definition in
                 schema.Options.Where(
                     option =>
                         option.Tool ==
                             tool &&
                         option.Available))
        {
            if (halfLifeMode &&
                ShouldSuppressForHalfLife(
                    definition))
            {
                continue;
            }

            if (!settings.Options.TryGetValue(
                    definition.Id,
                    out CompanionCompilerOptionSetting? setting) ||
                !setting.Enabled)
            {
                continue;
            }

            arguments.Add(
                definition.Flag);

            if (definition.ValueKind ==
                CompanionCompilerOptionValueKind.Flag)
            {
                continue;
            }

            string value =
                setting.Value.Trim();

            if (definition.ValueKind ==
                    CompanionCompilerOptionValueKind.Threads &&
                string.Equals(
                    value,
                    CompanionBuildSettingValues.AutomaticThreads,
                    StringComparison.OrdinalIgnoreCase))
            {
                value =
                    "${CPU_COUNT - 1}";
            }

            if (definition.ValueKind ==
                CompanionCompilerOptionValueKind.Text)
            {
                arguments.Add(
                    $"\"{value.Replace("\"", "\\\"")}\"");
            }
            else
            {
                arguments.Add(
                    value);
            }
        }

        return arguments;
    }

    private static bool ShouldSuppressForHalfLife(
        CompanionCompilerOptionDefinition definition)
    {
        if (definition.Tool ==
            CompanionCompilerTool.Qbsp)
        {
            return HalfLifeIgnoredQbspOptionIds.Contains(
                definition.Id);
        }

        if (definition.Tool ==
            CompanionCompilerTool.Light)
        {
            return HalfLifeIgnoredLightOptionIds.Contains(
                definition.Id);
        }

        return false;
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
            Guid.NewGuid().ToString(
                "N");

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
