using System.IO;

namespace TrenchBroom.Companion.Core;

public sealed record WadRegistrationResult(
    string WadPath,
    string WadFormat,
    int TextureCount,
    string ManifestPath,
    int AliasCount,
    bool WadIsValid,
    bool ManifestExists,
    bool ManifestIsValid,
    string Validation)
{
    public string WadFileName =>
        Path.GetFileName(WadPath);

    public string ManifestFileName =>
        string.IsNullOrWhiteSpace(ManifestPath)
            ? "None"
            : Path.GetFileName(ManifestPath);

    public string TextureCountText =>
        TextureCount < 0
            ? "-"
            : TextureCount.ToString("N0");

    public string AliasCountText =>
        AliasCount < 0
            ? "-"
            : AliasCount.ToString("N0");
}