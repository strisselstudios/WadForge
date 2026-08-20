using System.IO;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionProjectSession
{
    internal CompanionProjectSession(
        string projectFilePath,
        CompanionProjectManifest project)
    {
        ArgumentNullException.ThrowIfNull(project);

        ProjectFilePath =
            Path.GetFullPath(projectFilePath);

        ProjectDirectory =
            Path.GetDirectoryName(ProjectFilePath) ??
            throw new InvalidDataException(
                "Companion project file must have a parent directory.");

        Project = project;

        CompanionProjectStore.NormalizeAndValidate(
            Project);
    }

    public string ProjectFilePath { get; }

    public string ProjectDirectory { get; }

    public CompanionProjectManifest Project { get; }

    public CompanionProjectMap AddMap(
        string mapFilePath,
        bool makeActive = true)
    {
        return CompanionProjectStore.AddMap(
            Project,
            ProjectFilePath,
            mapFilePath,
            makeActive);
    }

    public void SetActiveMap(
        string mapFilePath)
    {
        string relativePath =
            CompanionProjectStore.MakeRelativeMapPath(
                ProjectFilePath,
                mapFilePath);

        CompanionProjectMap? map =
            Project.Maps.FirstOrDefault(
                candidate =>
                    string.Equals(
                        candidate.Path,
                        relativePath,
                        StringComparison.OrdinalIgnoreCase));

        if (map is null)
        {
            throw new InvalidOperationException(
                $"Map '{relativePath}' is not registered in this Companion project.");
        }

        Project.ActiveMapPath =
            map.Path;
    }

    public string? GetActiveMapFullPath()
    {
        if (string.IsNullOrWhiteSpace(
                Project.ActiveMapPath))
        {
            return null;
        }

        return CompanionProjectStore.ResolveMapPath(
            ProjectFilePath,
            Project.ActiveMapPath);
    }

    public void Save()
    {
        CompanionProjectStore.Save(
            ProjectFilePath,
            Project);
    }
}
