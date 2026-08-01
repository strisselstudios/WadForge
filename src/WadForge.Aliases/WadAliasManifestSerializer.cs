using System.Text.Json;

namespace WadForge.Aliases;

public static class WadAliasManifestSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy =
            JsonNamingPolicy.CamelCase
    };

    public static void Write(
        string path,
        WadAliasManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(manifest);

        string json = JsonSerializer.Serialize(
            manifest,
            Options);

        File.WriteAllText(
            path,
            json);
    }

    public static WadAliasManifest Read(
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json = File.ReadAllText(path);

        WadAliasManifest? manifest =
            JsonSerializer.Deserialize<WadAliasManifest>(
                json,
                Options);

        return manifest ??
            throw new InvalidDataException(
                "The alias manifest is empty or invalid.");
    }
}
