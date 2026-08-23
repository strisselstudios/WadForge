using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionBuildSettingsDocument
{
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string GameId { get; set; } = string.Empty;
    public string ToolchainVersion { get; set; } = string.Empty;
    public CompanionBuildSettings Settings { get; set; } = new();
}

public static class CompanionBuildSettingsService
{
    public const string FileName = "build-settings.json";

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

    public static CompanionBuildSettings Load(string projectDirectory, string gameId, string toolchainVersion)
    {
        string path = GetSettingsPath(projectDirectory);
        CompanionCompilerOptionSchema schema =
            CompanionCompilerOptionSchemaService.GetRequired(gameId, toolchainVersion);

        if (!File.Exists(path))
        {
            return CreateDefaults(schema);
        }

        string json = File.ReadAllText(path);

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            int schemaVersion =
                root.TryGetProperty("schemaVersion", out JsonElement versionElement)
                    ? versionElement.GetInt32()
                    : 1;

            CompanionBuildSettings settings;

            if (schemaVersion == 1)
            {
                settings = MigrateVersion1(root, schema);
            }
            else if (schemaVersion == CompanionBuildSettingsDocument.CurrentSchemaVersion)
            {
                CompanionBuildSettingsDocument? parsed =
                    JsonSerializer.Deserialize<CompanionBuildSettingsDocument>(json, SerializerOptions);

                if (parsed is null)
                {
                    throw new InvalidDataException("This project's build-settings.json file is empty or invalid.");
                }

                if (!string.Equals(parsed.GameId, gameId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"This project's build settings belong to '{parsed.GameId}', not '{gameId}'.");
                }

                settings = parsed.Settings ?? CreateDefaults(schema);
            }
            else
            {
                throw new InvalidDataException(
                    $"Unsupported build settings schema version {schemaVersion}. Companion currently supports version {CompanionBuildSettingsDocument.CurrentSchemaVersion}.");
            }

            ValidateAndNormalize(settings, schema);
            return settings;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "This project's build-settings.json file is not valid JSON. Companion left it untouched.",
                exception);
        }
    }

    public static void Save(
        string projectDirectory,
        string gameId,
        string toolchainVersion,
        CompanionBuildSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        CompanionCompilerOptionSchema schema =
            CompanionCompilerOptionSchemaService.GetRequired(gameId, toolchainVersion);

        CompanionBuildSettings normalized = settings.Clone();
        ValidateAndNormalize(normalized, schema);

        CompanionBuildSettingsDocument document =
            new()
            {
                GameId = gameId,
                ToolchainVersion = toolchainVersion,
                Settings = normalized
            };

        string destinationPath = GetSettingsPath(projectDirectory);
        string temporaryPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(document, SerializerOptions) + Environment.NewLine,
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

    public static CompanionBuildSettings CreateDefaults(CompanionCompilerOptionSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        CompanionBuildSettings settings = new();

        foreach (CompanionCompilerOptionDefinition definition in schema.Options)
        {
            settings.Options[definition.Id] =
                new CompanionCompilerOptionSetting
                {
                    Enabled = definition.Available && definition.EnabledByDefault,
                    Value = definition.DefaultValue
                };
        }

        return settings;
    }

    public static void ValidateForSave(
        CompanionBuildSettings settings,
        CompanionCompilerOptionSchema schema)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(schema);
        ValidateAndNormalize(settings, schema);
    }

    public static string GetSettingsPath(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            throw new ArgumentException("A project directory is required.", nameof(projectDirectory));
        }

        string fullProjectDirectory = Path.GetFullPath(projectDirectory);
        if (!Directory.Exists(fullProjectDirectory))
        {
            throw new DirectoryNotFoundException($"Project directory does not exist: {fullProjectDirectory}");
        }

        return Path.Combine(fullProjectDirectory, FileName);
    }

    private static CompanionBuildSettings MigrateVersion1(
        JsonElement root,
        CompanionCompilerOptionSchema schema)
    {
        CompanionBuildSettings migrated = CreateDefaults(schema);
        if (!root.TryGetProperty("settings", out JsonElement legacy))
        {
            return migrated;
        }

        string bspFormat = GetString(legacy, "bspFormat", "standard");
        SetEnabled(migrated, "qbsp.bsp2",
            string.Equals(bspFormat, "bsp2", StringComparison.OrdinalIgnoreCase));
        SetEnabled(migrated, "qbsp.leaktest", GetBoolean(legacy, "leakTest", false));
        SetEnabled(migrated, "light.lit", GetBoolean(legacy, "generateLit", true));

        string sampling = GetString(legacy, "lightSampling", "normal");
        SetEnabled(migrated, "light.extra",
            string.Equals(sampling, "extra", StringComparison.OrdinalIgnoreCase));
        SetEnabled(migrated, "light.extra4",
            string.Equals(sampling, "extra4", StringComparison.OrdinalIgnoreCase));

        string threadMode = GetString(legacy, "threadMode", CompanionBuildSettingValues.AutomaticThreads);
        CompanionCompilerOptionSetting threadSetting = migrated.GetOrCreate("light.threads");
        threadSetting.Enabled = true;

        if (string.Equals(threadMode, "custom", StringComparison.OrdinalIgnoreCase))
        {
            int count = Math.Clamp(GetInteger(legacy, "customThreadCount", 1), 1, 256);
            threadSetting.Value = count.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            threadSetting.Value = CompanionBuildSettingValues.AutomaticThreads;
        }

        return migrated;
    }

    private static void ValidateAndNormalize(
        CompanionBuildSettings settings,
        CompanionCompilerOptionSchema schema)
    {
        settings.Options ??= new Dictionary<string, CompanionCompilerOptionSetting>(StringComparer.OrdinalIgnoreCase);

        foreach (CompanionCompilerOptionDefinition definition in schema.Options)
        {
            CompanionCompilerOptionSetting setting = settings.GetOrCreate(definition.Id);
            setting.Value ??= definition.DefaultValue;

            if (!definition.Available)
            {
                setting.Enabled = false;
            }

            if (setting.Enabled)
            {
                ValidateValue(definition, setting.Value);
            }
        }

        foreach (IGrouping<string, CompanionCompilerOptionDefinition> group in
                 schema.Options
                     .Where(option => !string.IsNullOrWhiteSpace(option.ExclusiveGroup))
                     .GroupBy(option => option.ExclusiveGroup!, StringComparer.OrdinalIgnoreCase))
        {
            List<CompanionCompilerOptionDefinition> enabled =
                group.Where(option => settings.IsEnabled(option.Id)).ToList();

            if (enabled.Count > 1)
            {
                throw new InvalidDataException(
                    $"Build options '{enabled[0].DisplayName}' and '{enabled[1].DisplayName}' cannot be enabled together.");
            }
        }
    }

    private static void ValidateValue(
        CompanionCompilerOptionDefinition definition,
        string value)
    {
        if (definition.ValueKind == CompanionCompilerOptionValueKind.Flag)
        {
            return;
        }

        string normalized = value?.Trim() ?? string.Empty;

        if (definition.ValueKind == CompanionCompilerOptionValueKind.Threads)
        {
            if (string.Equals(normalized, CompanionBuildSettingValues.AutomaticThreads, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ||
                count < 1 || count > 256)
            {
                throw new InvalidDataException(
                    $"{definition.DisplayName} must use Automatic or a whole number from 1 through 256.");
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidDataException($"{definition.DisplayName} requires a value.");
        }

        if (definition.ValueKind == CompanionCompilerOptionValueKind.Text)
        {
            return;
        }

        if (definition.ValueKind == CompanionCompilerOptionValueKind.Integer)
        {
            if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integerValue))
            {
                throw new InvalidDataException($"{definition.DisplayName} requires a whole-number value.");
            }
            ValidateRange(definition, integerValue);
            return;
        }

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double numberValue))
        {
            throw new InvalidDataException(
                $"{definition.DisplayName} requires a numeric value. Use a period as the decimal separator.");
        }

        ValidateRange(definition, numberValue);
    }

    private static void ValidateRange(CompanionCompilerOptionDefinition definition, double value)
    {
        if (definition.Minimum.HasValue && value < definition.Minimum.Value)
        {
            throw new InvalidDataException(
                $"{definition.DisplayName} must be at least {definition.Minimum.Value.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (definition.Maximum.HasValue && value > definition.Maximum.Value)
        {
            throw new InvalidDataException(
                $"{definition.DisplayName} must be no greater than {definition.Maximum.Value.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static void SetEnabled(CompanionBuildSettings settings, string optionId, bool enabled) =>
        settings.GetOrCreate(optionId).Enabled = enabled;

    private static string GetString(JsonElement element, string propertyName, string fallback) =>
        element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;

    private static bool GetBoolean(JsonElement element, string propertyName, bool fallback)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return fallback;
        }
        return property.ValueKind == JsonValueKind.True
            ? true
            : property.ValueKind == JsonValueKind.False
                ? false
                : fallback;
    }

    private static int GetInteger(JsonElement element, string propertyName, int fallback) =>
        element.TryGetProperty(propertyName, out JsonElement property) && property.TryGetInt32(out int value)
            ? value
            : fallback;
}
