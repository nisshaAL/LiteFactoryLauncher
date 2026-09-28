using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace LiteFactoryLauncher.Services;

public sealed class ModpackInstaller
{
    public Task<InstallResult> InstallFromZipAsync(
        string archivePath,
        string installationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                return InstallFromZip(archivePath, installationDirectory, progress, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return InstallResult.Error(ex.Message);
            }
        }, cancellationToken);
    }

    private static InstallResult InstallFromZip(
        string archivePath,
        string installationDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return InstallResult.Error("Selected archive does not exist.");
        }

        if (!string.Equals(Path.GetExtension(archivePath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return InstallResult.Error("Selected file must be a .zip archive.");
        }

        if (string.IsNullOrWhiteSpace(installationDirectory))
        {
            return InstallResult.Error("Installation directory is not configured.");
        }

        Directory.CreateDirectory(installationDirectory);

        var installRoot = Path.GetFullPath(installationDirectory);
        if (!installRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            installRoot += Path.DirectorySeparatorChar;
        }

        using var archive = ZipFile.OpenRead(archivePath);
        var entryCount = Math.Max(archive.Entries.Count, 1);
        var processedEntries = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destinationPath = Path.GetFullPath(Path.Combine(installRoot, entry.FullName));
            if (!destinationPath.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
            {
                return InstallResult.Error("Archive contains an unsafe file path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
            }
            else
            {
                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                entry.ExtractToFile(destinationPath, true);
            }

            processedEntries++;
            progress?.Report(processedEntries * 100d / entryCount);
        }

        progress?.Report(100);
        return InstallResult.Ok();
    }
}
