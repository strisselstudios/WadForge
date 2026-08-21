using System.IO;

namespace TrenchBroom.Companion.Core;

public sealed class CompanionProjectCreationService
{
    private readonly CompanionProjectProvisioner _provisioner;
    private readonly CompanionProjectManager _projectManager;

    public CompanionProjectCreationService()
        : this(
            new CompanionProjectProvisioner(),
            new CompanionProjectManager())
    {
    }

    public CompanionProjectCreationService(
        CompanionProjectProvisioner provisioner,
        CompanionProjectManager projectManager)
    {
        _provisioner =
            provisioner ??
            throw new ArgumentNullException(
                nameof(provisioner));

        _projectManager =
            projectManager ??
            throw new ArgumentNullException(
                nameof(projectManager));
    }

    public CompanionProjectCreationResult Create(
        CompanionGameProfile gameProfile,
        string selectedDrivePath,
        string gameInstallationDirectory,
        string projectName,
        string? preferredTextureArchiveFormat = null)
    {
        CompanionProvisionedProject provisioned =
            _provisioner.Provision(
                gameProfile,
                selectedDrivePath,
                gameInstallationDirectory,
                projectName);

        return FinishCreation(
            provisioned,
            preferredTextureArchiveFormat);
    }

    public CompanionProjectCreationResult CreateAtWorkspaceRoot(
        CompanionGameProfile gameProfile,
        string workspaceRoot,
        string gameInstallationDirectory,
        string projectName,
        string? preferredTextureArchiveFormat = null)
    {
        CompanionProvisionedProject provisioned =
            _provisioner.ProvisionAtWorkspaceRoot(
                gameProfile,
                workspaceRoot,
                gameInstallationDirectory,
                projectName);

        return FinishCreation(
            provisioned,
            preferredTextureArchiveFormat);
    }

    private CompanionProjectCreationResult FinishCreation(
        CompanionProvisionedProject provisioned,
        string? preferredTextureArchiveFormat)
    {
        CompanionProjectSession? session =
            null;

        try
        {
            session =
                _projectManager.Create(
                    provisioned.ProjectDirectory,
                    provisioned.ProjectName,
                    provisioned.GameProfile.Id,
                    provisioned.ProjectDirectoryName,
                    preferredTextureArchiveFormat);

            session.Project.GameBinding =
                new CompanionProjectGameBinding
                {
                    GameInstallationDirectory =
                        provisioned.GameInstallationDirectory,

                    RuntimeModDirectory =
                        provisioned.RuntimeModDirectory
                };

            session.Save();

            return new CompanionProjectCreationResult(
                session,
                provisioned);
        }
        catch
        {
            if (session is not null)
            {
                TryDeleteFile(
                    session.ProjectFilePath);
            }

            CleanupEmptyProvisioning(
                provisioned);

            throw;
        }
    }

    private static void CleanupEmptyProvisioning(
        CompanionProvisionedProject provisioned)
    {
        TryDeleteDirectoryIfEmpty(
            provisioned.RuntimeMapsDirectory);

        TryDeleteDirectoryIfEmpty(
            provisioned.RuntimeModDirectory);

        if (Directory.Exists(
                provisioned.ProjectDirectory))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(
                        provisioned.ProjectDirectory)
                    .Any(
                        entry =>
                            File.Exists(entry)))
                {
                    Directory.Delete(
                        provisioned.ProjectDirectory,
                        recursive: true);
                }
            }
            catch
            {
                // The original project-creation exception is more useful.
            }
        }

        TryDeleteDirectoryIfEmpty(
            provisioned.WorkspaceRoot);
    }

    private static void TryDeleteFile(
        string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Cleanup is best-effort only.
        }
    }

    private static void TryDeleteDirectoryIfEmpty(
        string directory)
    {
        try
        {
            if (!Directory.Exists(
                    directory))
            {
                return;
            }

            if (Directory.EnumerateFileSystemEntries(
                    directory)
                .Any())
            {
                return;
            }

            Directory.Delete(
                directory);
        }
        catch
        {
            // Cleanup is best-effort only.
        }
    }
}
