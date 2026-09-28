using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LiteFactoryLauncher.Services;

public sealed class ForgeRuntimeService
{
    private static readonly HttpClient HttpClient = new();
    private static readonly string MinecraftRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiteFactory",
        "runtime",
        "minecraft");
    private static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "LiteFactoryLauncher");
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiteFactory",
        "logs",
        "forge-install.log");

    public async Task<bool> ValidateInstalledAsync(string minecraftVersion, string forgeVersion)
    {
        try
        {
            var profilePath = FindForgeProfilePath(minecraftVersion, forgeVersion);
            if (profilePath == null)
            {
                Log("Validation failed: Forge profile JSON was not found.");
                return false;
            }

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(profilePath));
            var root = document.RootElement;
            var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? "" : "";
            if (!id.Contains(forgeVersion, StringComparison.OrdinalIgnoreCase) ||
                !id.Contains("forge", StringComparison.OrdinalIgnoreCase))
            {
                Log($"Validation failed: Forge profile id '{id}' does not match Forge {forgeVersion}.");
                return false;
            }

            if (!root.TryGetProperty("libraries", out var libraries))
            {
                Log("Validation failed: Forge profile does not contain libraries.");
                return false;
            }

            foreach (var library in libraries.EnumerateArray())
            {
                var name = library.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var libraryPath = GetLibraryPath(library, name);
                if (!File.Exists(libraryPath))
                {
                    Log($"Validation failed: required Forge library is missing: {name} -> {libraryPath}");
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Log($"Validation exception:{Environment.NewLine}{ex}");
            return false;
        }
    }

    public async Task<ForgeInstallResult> InstallAsync(
        string minecraftVersion,
        string forgeVersion,
        string javaPath,
        IProgress<double>? progress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        var installerPath = GetInstallerPath(minecraftVersion, forgeVersion);

        try
        {
            Directory.CreateDirectory(MinecraftRoot);
            Directory.CreateDirectory(TempDirectory);

            var logHeader = new StringBuilder()
                .AppendLine($"Forge install started: {DateTimeOffset.Now:O}")
                .AppendLine($"Selected Java executable: {javaPath}")
                .AppendLine($"Forge installer path: {installerPath}")
                .AppendLine($"Target Minecraft runtime: {MinecraftRoot}")
                .AppendLine("Installation strategy: metadata-driven isolated install from Forge installer contents.")
                .AppendLine("Legacy installer --installClient is not used because it requires launcher_profiles.json.");
            Log(logHeader.ToString());

            if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
            {
                return ForgeInstallResult.Error("Compatible Java 8 is required before installing Forge.");
            }

            if (!Directory.Exists(Path.Combine(MinecraftRoot, "versions", minecraftVersion)))
            {
                return ForgeInstallResult.Error("Minecraft runtime must be installed before installing Forge.");
            }

            if (await ValidateInstalledAsync(minecraftVersion, forgeVersion))
            {
                progress?.Report(100);
                status?.Report("Forge ready");
                return ForgeInstallResult.Ok();
            }

            status?.Report("Downloading Forge...");
            progress?.Report(0);
            await DownloadInstallerAsync(minecraftVersion, forgeVersion, installerPath, cancellationToken);
            progress?.Report(35);

            status?.Report("Preparing Forge...");
            if (!File.Exists(installerPath))
            {
                return ForgeInstallResult.Error("Forge installer download did not produce a file.");
            }

            status?.Report("Installing Forge...");
            await InstallFromInstallerMetadataAsync(minecraftVersion, forgeVersion, installerPath, cancellationToken);
            progress?.Report(85);

            status?.Report("Verifying Forge...");
            if (!await ValidateInstalledAsync(minecraftVersion, forgeVersion))
            {
                return ForgeInstallResult.Error("Forge validation failed after metadata install completed.");
            }

            progress?.Report(100);
            status?.Report("Forge ready");
            return ForgeInstallResult.Ok();
        }
        catch (Exception ex)
        {
            var diagnosticText = ex.ToString();
            Log(diagnosticText);
            return ForgeInstallResult.Error($"Forge installation failed: {ex.Message}", diagnosticText);
        }
    }

    private static async Task DownloadInstallerAsync(string minecraftVersion, string forgeVersion, string installerPath, CancellationToken cancellationToken)
    {
        var forgeCoordinate = $"{minecraftVersion}-{forgeVersion}";
        var url = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{forgeCoordinate}/forge-{forgeCoordinate}-installer.jar";
        var tempPath = installerPath + ".tmp";

        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        File.Move(tempPath, installerPath, overwrite: true);
    }

    private static async Task InstallFromInstallerMetadataAsync(
        string minecraftVersion,
        string forgeVersion,
        string installerPath,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(installerPath);
        var installProfile = await ReadJsonEntryAsync(archive, "install_profile.json", cancellationToken);
        var versionJson = await ReadJsonEntryAsync(archive, "version.json", cancellationToken);
        var forgeId = versionJson.RootElement.GetProperty("id").GetString() ??
                      $"{minecraftVersion}-forge-{forgeVersion}";

        Log($"Parsed Forge install profile. Profile={GetOptionalString(installProfile.RootElement, "profile")}, Version={GetOptionalString(installProfile.RootElement, "version")}, Minecraft={GetOptionalString(installProfile.RootElement, "minecraft")}");
        Log($"Parsed Forge version JSON. Id={forgeId}");

        if (installProfile.RootElement.TryGetProperty("processors", out var processors) &&
            processors.ValueKind == JsonValueKind.Array &&
            processors.GetArrayLength() > 0)
        {
            throw new InvalidOperationException("This Forge installer requires processors, which are not implemented for the isolated runtime installer yet.");
        }

        var versionDirectory = Path.Combine(MinecraftRoot, "versions", forgeId);
        Directory.CreateDirectory(versionDirectory);
        var versionJsonPath = Path.Combine(versionDirectory, $"{forgeId}.json");
        await File.WriteAllTextAsync(versionJsonPath, versionJson.RootElement.GetRawText(), cancellationToken);
        Log($"Wrote Forge version profile: {versionJsonPath}");

        var installProfilePath = Path.Combine(versionDirectory, "install_profile.json");
        await File.WriteAllTextAsync(installProfilePath, installProfile.RootElement.GetRawText(), cancellationToken);
        Log($"Wrote Forge install profile copy: {installProfilePath}");

        await InstallLibrariesFromMetadataAsync(archive, installProfile.RootElement, "install_profile.json", cancellationToken);
        await InstallLibrariesFromMetadataAsync(archive, versionJson.RootElement, "version.json", cancellationToken);
    }

    private static async Task InstallLibrariesFromMetadataAsync(
        ZipArchive archive,
        JsonElement metadata,
        string sourceName,
        CancellationToken cancellationToken)
    {
        if (!metadata.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Array)
        {
            Log($"{sourceName} does not contain libraries.");
            return;
        }

        foreach (var library in libraries.EnumerateArray())
        {
            var name = library.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var artifact = GetArtifact(library);
            if (!artifact.HasValue)
            {
                Log($"Skipping Forge library without artifact metadata: {name}");
                continue;
            }

            var path = artifact.Value.TryGetProperty("path", out var pathElement)
                ? pathElement.GetString()
                : GetMavenPath(name).Replace(Path.DirectorySeparatorChar, '/');
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidDataException($"Forge library {name} does not contain an artifact path.");
            }

            var destination = Path.Combine(MinecraftRoot, "libraries", path.Replace('/', Path.DirectorySeparatorChar));
            var sha1 = artifact.Value.TryGetProperty("sha1", out var sha1Element) ? sha1Element.GetString() : null;
            var url = artifact.Value.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;

            Log($"Installing Forge library {name} -> {destination}");

            if (await FileHasSha1Async(destination, sha1))
            {
                Log($"Reusing valid Forge library: {name}");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                Log($"Downloading Forge library {name} from {url}");
                await DownloadFileWithSha1Async(url, destination, sha1, cancellationToken);
                continue;
            }

            ExtractEmbeddedLibrary(archive, path, destination, sha1);
        }
    }

    private static JsonElement? GetArtifact(JsonElement library)
    {
        return library.TryGetProperty("downloads", out var downloads) &&
               downloads.TryGetProperty("artifact", out var artifact)
            ? artifact
            : null;
    }

    private static async Task<JsonDocument> ReadJsonEntryAsync(ZipArchive archive, string entryName, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(entryName) ??
                    throw new InvalidDataException($"Forge installer is missing {entryName}.");
        await using var stream = entry.Open();
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static void ExtractEmbeddedLibrary(ZipArchive archive, string artifactPath, string destination, string? expectedSha1)
    {
        var entryName = artifactPath.StartsWith("maven/", StringComparison.OrdinalIgnoreCase)
            ? artifactPath
            : $"maven/{artifactPath}";
        var entry = archive.GetEntry(entryName) ??
                    throw new InvalidDataException($"Forge installer is missing embedded artifact {entryName}.");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var tempPath = destination + ".tmp";
        entry.ExtractToFile(tempPath, true);

        if (!FileHasSha1(tempPath, expectedSha1))
        {
            TryDeleteFile(tempPath);
            throw new InvalidDataException($"SHA-1 verification failed for embedded Forge artifact {artifactPath}.");
        }

        File.Move(tempPath, destination, overwrite: true);
        Log($"Extracted embedded Forge artifact {entryName} -> {destination}");
    }

    private static string? FindForgeProfilePath(string minecraftVersion, string forgeVersion)
    {
        var versionsDirectory = Path.Combine(MinecraftRoot, "versions");
        if (!Directory.Exists(versionsDirectory))
        {
            return null;
        }

        foreach (var profilePath in Directory.EnumerateFiles(versionsDirectory, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(profilePath));
                var root = document.RootElement;
                var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? "" : "";
                if (id.Contains("forge", StringComparison.OrdinalIgnoreCase) &&
                    id.Contains(forgeVersion, StringComparison.OrdinalIgnoreCase) &&
                    (id.Contains(minecraftVersion, StringComparison.OrdinalIgnoreCase) ||
                     root.TryGetProperty("inheritsFrom", out var inheritsFrom) &&
                     string.Equals(inheritsFrom.GetString(), minecraftVersion, StringComparison.OrdinalIgnoreCase)))
                {
                    return profilePath;
                }
            }
            catch (Exception ex)
            {
                Log($"Skipping invalid Forge profile candidate {profilePath}: {ex}");
            }
        }

        return null;
    }

    private static string GetLibraryPath(JsonElement library, string name)
    {
        if (library.TryGetProperty("downloads", out var downloads) &&
            downloads.TryGetProperty("artifact", out var artifact) &&
            artifact.TryGetProperty("path", out var pathElement) &&
            !string.IsNullOrWhiteSpace(pathElement.GetString()))
        {
            return Path.Combine(MinecraftRoot, "libraries", pathElement.GetString()!.Replace('/', Path.DirectorySeparatorChar));
        }

        return Path.Combine(MinecraftRoot, "libraries", GetMavenPath(name));
    }

    private static string GetMavenPath(string name)
    {
        var parts = name.Split(':');
        if (parts.Length < 3)
        {
            return name.Replace(':', Path.DirectorySeparatorChar);
        }

        var groupPath = parts[0].Replace('.', Path.DirectorySeparatorChar);
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? $"-{parts[3]}" : "";
        return Path.Combine(groupPath, artifact, version, $"{artifact}-{version}{classifier}.jar");
    }

    private static string GetInstallerPath(string minecraftVersion, string forgeVersion)
    {
        var forgeCoordinate = $"{minecraftVersion}-{forgeVersion}";
        return Path.Combine(TempDirectory, $"forge-{forgeCoordinate}-installer.jar");
    }

    private static void Log(string message)
    {
        Console.Error.WriteLine(message);
        Debug.WriteLine(message);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{message}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to write Forge install log: {ex}");
            Debug.WriteLine($"Failed to write Forge install log: {ex}");
        }
    }

    private static async Task DownloadFileWithSha1Async(string url, string destination, string? expectedSha1, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var tempPath = destination + ".tmp";

        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        if (!FileHasSha1(tempPath, expectedSha1))
        {
            TryDeleteFile(tempPath);
            throw new InvalidDataException($"SHA-1 verification failed for Forge artifact {Path.GetFileName(destination)}.");
        }

        File.Move(tempPath, destination, overwrite: true);
    }

    private static async Task<bool> FileHasSha1Async(string path, string? expectedSha1)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(expectedSha1))
        {
            return true;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var hash = await SHA1.HashDataAsync(stream);
        return string.Equals(Convert.ToHexString(hash), expectedSha1, StringComparison.OrdinalIgnoreCase);
    }

    private static bool FileHasSha1(string path, string? expectedSha1)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(expectedSha1))
        {
            return true;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = SHA1.HashData(stream);
        return string.Equals(Convert.ToHexString(hash), expectedSha1, StringComparison.OrdinalIgnoreCase);
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
            // Cleanup should not hide the original Forge install error.
        }
    }

    private static string GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value.GetString() ?? "" : "";
    }
}
