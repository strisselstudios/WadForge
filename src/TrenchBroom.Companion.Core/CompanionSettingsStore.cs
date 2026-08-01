using System.IO;
using System.Text.Json;

namespace TrenchBroom.Companion.Core;

public static class CompanionSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string SettingsDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "WadForgeSuite",
            "TrenchBroom-Companion");

    public static string SettingsPath { get; } =
        Path.Combine(
            SettingsDirectory,
            "settings.json");

    public static CompanionSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new CompanionSettings();
        }

        string json = File.ReadAllText(SettingsPath);

        CompanionSettings? settings =
            JsonSerializer.Deserialize<CompanionSettings>(
                json,
                JsonOptions);

        return settings ?? new CompanionSettings();
    }

    public static void Save(
        CompanionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(
            SettingsDirectory);

        string json =
            JsonSerializer.Serialize(
                settings,
                JsonOptions);

        string temporaryPath =
            SettingsPath + ".temporary";

        File.WriteAllText(
            temporaryPath,
            json);

        File.Move(
            temporaryPath,
            SettingsPath,
            true);
    }
}
