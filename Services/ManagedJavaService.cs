using LiteFactoryLauncher.Models;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace LiteFactoryLauncher.Services;

public sealed class ManagedJavaService
{
    private const string JavaDownloadUrl =
        "https://corretto.aws/downloads/latest/amazon-corretto-8-x64-windows-jre.zip";

    private const string JavaSha256Url =
        "https://corretto.aws/downloads/latest_sha256/amazon-corretto-8-x64-windows-jre.zip";

    private static readonly HttpClient HttpClient = new();

    private static readonly string RuntimeDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteFactory", "runtime", "java8");

    private static readonly string StagingDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteFactory", "runtime", "java8-staging");

    private static readonly string BackupDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteFactory", "runtime", "java8-backup");

    private static readonly string TempArchivePath =
        Path.Combine(Path.GetTempPath(), "LiteFactoryLauncher", "java8-runtime.zip");

    public string ManagedRuntimeDirectory => RuntimeDirectory;

    public async Task<JavaRuntimeInfo?> GetInstalledRuntimeAsync()
    {
        var javaPath = FindJavaExecutable(RuntimeDirectory);
        return javaPath == null
            ? null
            : await JavaDetectionService.InspectJavaAsync(javaPath, isManaged: true);
    }

    public async Task<JavaInstallResult> InstallJava8Async(
        IProgress<double>? progress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TempArchivePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(RuntimeDirectory)!);
            DeleteDirectory(StagingDirectory);

            status?.Report("Downloading Java...");
            var expectedHash = await DownloadExpectedSha256Async(cancellationToken);
            if (string.IsNullOrWhiteSpace(expectedHash))
            {
                return JavaInstallResult.Error("Could not download Java checksum.");
            }

            await DownloadArchiveAsync(TempArchivePath, progress, cancellationToken);

            status?.Report("Verifying Java...");
            var actualHash = await ComputeSha256Async(TempArchivePath, cancellationToken);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(TempArchivePath);
                return JavaInstallResult.Error("Downloaded Java runtime failed SHA-256 verification.");
            }

            status?.Report("Installing Java...");
            var extractionRoot = Path.Combine(StagingDirectory, "extract");
            Directory.CreateDirectory(extractionRoot);
            var extractionResult = ExtractZipSafely(TempArchivePath, extractionRoot);
            if (!extractionResult.Success)
            {
                TryDeleteFile(TempArchivePath);
                DeleteDirectory(StagingDirectory);
                return JavaInstallResult.Error(extractionResult.ErrorMessage ?? "Java extraction failed.");
            }

            var stagedJavaPath = FindJavaExecutable(extractionRoot);
            if (stagedJavaPath == null)
            {
                TryDeleteFile(TempArchivePath);
                DeleteDirectory(StagingDirectory);
                return JavaInstallResult.Error("Java runtime archive did not contain bin\\java.exe.");
            }

            status?.Report("Checking Java...");
            var runtime = await JavaDetectionService.InspectJavaAsync(stagedJavaPath, isManaged: true);
            if (runtime == null || !runtime.IsCompatible)
            {
                TryDeleteFile(TempArchivePath);
                DeleteDirectory(StagingDirectory);
                return JavaInstallResult.Error("Installed Java runtime is not compatible Java 8.");
            }

            ReplaceRuntimeDirectory(extractionRoot);
            TryDeleteFile(TempArchivePath);
            DeleteDirectory(StagingDirectory);

            var installedRuntime = await GetInstalledRuntimeAsync();
            return installedRuntime != null && installedRuntime.IsCompatible
                ? JavaInstallResult.Ok(installedRuntime)
                : JavaInstallResult.Error("Managed Java runtime could not be verified after installation.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or TaskCanceledException)
        {
            TryDeleteFile(TempArchivePath);
            DeleteDirectory(StagingDirectory);
            return JavaInstallResult.Error($"Java installation failed: {ex.Message}");
        }
    }

    private static async Task<string> DownloadExpectedSha256Async(CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(JavaSha256Url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var checksumText = await response.Content.ReadAsStringAsync(cancellationToken);
        return checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
    }

    private static async Task DownloadArchiveAsync(string archivePath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(JavaDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalRead += read;

            if (contentLength.HasValue && contentLength.Value > 0)
            {
                progress?.Report(totalRead * 100d / contentLength.Value);
            }
        }

        progress?.Report(100);
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static InstallResult ExtractZipSafely(string archivePath, string targetDirectory)
    {
        var root = Path.GetFullPath(targetDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return InstallResult.Error("Java archive contains an unsafe file path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            entry.ExtractToFile(destinationPath, true);
        }

        return InstallResult.Ok();
    }

    private static string? FindJavaExecutable(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return null;
        }

        foreach (var javaPath in Directory.EnumerateFiles(rootDirectory, "java.exe", SearchOption.AllDirectories))
        {
            var parentDirectory = Path.GetDirectoryName(javaPath);
            if (string.Equals(Path.GetFileName(parentDirectory), "bin", StringComparison.OrdinalIgnoreCase))
            {
                return javaPath;
            }
        }

        return null;
    }

    private static void ReplaceRuntimeDirectory(string verifiedRuntimeRoot)
    {
        DeleteDirectory(BackupDirectory);

        if (Directory.Exists(RuntimeDirectory))
        {
            Directory.Move(RuntimeDirectory, BackupDirectory);
        }

        try
        {
            Directory.Move(verifiedRuntimeRoot, RuntimeDirectory);
            DeleteDirectory(BackupDirectory);
        }
        catch
        {
            if (!Directory.Exists(RuntimeDirectory) && Directory.Exists(BackupDirectory))
            {
                Directory.Move(BackupDirectory, RuntimeDirectory);
            }

            throw;
        }
    }

    private static void TryDeleteFile(string filePath)
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
            // Cleanup should not hide the real Java installation error.
        }
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
