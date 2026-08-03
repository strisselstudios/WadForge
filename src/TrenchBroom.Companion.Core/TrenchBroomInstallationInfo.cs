namespace TrenchBroom.Companion.Core;

public sealed record TrenchBroomInstallationInfo(
    string ExecutablePath,
    string Version,
    bool IsValid,
    bool IsWadForgeCompatible,
    string Status);