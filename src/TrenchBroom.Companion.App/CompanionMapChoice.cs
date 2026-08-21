namespace TrenchBroom.Companion.App;

internal sealed class CompanionMapChoice
{
    public CompanionMapChoice(
        string displayName,
        string fullPath)
    {
        DisplayName =
            displayName;

        FullPath =
            fullPath;
    }

    public string DisplayName { get; }

    public string FullPath { get; }
}