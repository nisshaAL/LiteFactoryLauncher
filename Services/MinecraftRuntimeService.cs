using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LiteFactoryLauncher.Services;

public sealed class MinecraftRuntimeService
{
    private const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private static readonly HttpClient HttpClient = new();
    private static readonly string MinecraftRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiteFactory",
        "runtime",
        "minecraft");
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiteFactory",
        "logs",
        "minecraft-install.log");

    public string RuntimeDirectory => MinecraftRoot;

    public async Task<bool> ValidateInstalledAsync(string minecraftVersion)
    {
        try
        {
            var versionJsonPath = GetVersionJsonPath(minecraftVersion);
            if (!File.Exists(versionJsonPath))
            {
                return false;
            }

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(versionJsonPath));
            var root = document.RootElement;

            if (!await ValidateClientAsync(minecraftVersion, root))
            {
                return false;
            }

            if (!await ValidateLibrariesAsync(root))
            {
                return false;
            }

            if (!await ValidateAssetsAsync(root))
            {
                return false;
            }

            return Directory.Exists(GetNativesDirectory(minecraftVersion)) &&
                   Directory.EnumerateFiles(GetNativesDirectory(minecraftVersion), "*.dll", SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            return false;
        }
    }

    public async Task<MinecraftInstallResult> InstallAsync(
        string minecraftVersion,
        IProgress<double>? progress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(MinecraftRoot);

            status?.Report("Preparing Minecraft...");
            progress?.Report(0);

            var versionMetadata = await RunWithContextAsync(
                $"Preparing Minecraft {minecraftVersion}: downloading Mojang metadata",
                () => DownloadVersionMetadataAsync(minecraftVersion, cancellationToken));
            await RunWithContextAsync(
                $"Preparing Minecraft {minecraftVersion}: saving version JSON",
                () => SaveVersionJsonAsync(minecraftVersion, versionMetadata, cancellationToken));
            progress?.Report(10);

            status?.Report($"Downloading Minecraft {minecraftVersion}...");
            await RunWithContextAsync(
                $"Downloading Minecraft client {minecraftVersion}",
                () => DownloadClientAsync(minecraftVersion, versionMetadata, cancellationToken));
            progress?.Report(20);

            status?.Report("Downloading libraries...");
            var nativeArchives = await RunWithContextAsync(
                "Downloading Minecraft libraries",
                () => DownloadLibrariesAsync(versionMetadata, progress, cancellationToken));

            status?.Report("Downloading assets...");
            await RunWithContextAsync(
                "Downloading Minecraft assets",
                () => DownloadAssetsAsync(versionMetadata, progress, cancellationToken));

            status?.Report("Extracting natives...");
            RunWithContext(
                $"Extracting native libraries for Minecraft {minecraftVersion}",
                () => ExtractNativesSafely(minecraftVersion, nativeArchives));
            progress?.Report(98);

            status?.Report("Verifying Minecraft...");
            if (!await RunWithContextAsync(
                    $"Validating Minecraft runtime {minecraftVersion}",
                    () => ValidateInstalledAsync(minecraftVersion)))
            {
                return MinecraftInstallResult.Error("Minecraft runtime validation failed.");
            }

            progress?.Report(100);
            status?.Report($"Minecraft {minecraftVersion} ready");
            return MinecraftInstallResult.Ok();
        }
        catch (Exception ex)
        {
            var diagnosticText = FormatException(ex);
            WriteDiagnosticLog(diagnosticText);
            return MinecraftInstallResult.Error($"Minecraft installation failed: {ex.Message}", diagnosticText);
        }
    }

    private static async Task<JsonDocument> DownloadVersionMetadataAsync(string minecraftVersion, CancellationToken cancellationToken)
    {
        using var manifest = await DownloadJsonAsync(VersionManifestUrl, cancellationToken);
        string? versionUrl = null;

        foreach (var version in manifest.RootElement.GetProperty("versions").EnumerateArray())
        {
            if (version.GetProperty("id").GetString() == minecraftVersion)
            {
                versionUrl = version.GetProperty("url").GetString();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(versionUrl))
        {
            throw new InvalidDataException($"Minecraft {minecraftVersion} was not found in Mojang version manifest.");
        }

        return await DownloadJsonAsync(versionUrl, cancellationToken);
    }

    private static async Task<JsonDocument> DownloadJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static async Task SaveVersionJsonAsync(string minecraftVersion, JsonDocument versionMetadata, CancellationToken cancellationToken)
    {
        var versionJsonPath = GetVersionJsonPath(minecraftVersion);
        Directory.CreateDirectory(Path.GetDirectoryName(versionJsonPath)!);
        await File.WriteAllTextAsync(versionJsonPath, versionMetadata.RootElement.GetRawText(), cancellationToken);
    }

    private static async Task DownloadClientAsync(string minecraftVersion, JsonDocument versionMetadata, CancellationToken cancellationToken)
    {
        var client = versionMetadata.RootElement.GetProperty("downloads").GetProperty("client");
        await DownloadFileWithSha1Async(
            client.GetProperty("url").GetString() ?? throw new InvalidDataException("Missing client URL."),
            GetClientJarPath(minecraftVersion),
            client.GetProperty("sha1").GetString(),
            cancellationToken);
    }

    private static async Task<List<string>> DownloadLibrariesAsync(JsonDocument versionMetadata, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var nativeArchives = new List<string>();
        var libraries = versionMetadata.RootElement.GetProperty("libraries").EnumerateArray().Where(IsLibraryAllowed).ToList();
        var completed = 0;

        foreach (var library in libraries)
        {
            var libraryName = library.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? "unknown"
                : "unknown";

            try
            {
                if (library.TryGetProperty("downloads", out var downloads))
                {
                    if (downloads.TryGetProperty("artifact", out var artifact))
                    {
                        await DownloadLibraryArtifactAsync(artifact, cancellationToken);
                    }

                    var nativeArtifact = GetWindowsNativeArtifact(library, downloads);
                    if (nativeArtifact.HasValue)
                    {
                        nativeArchives.Add(await DownloadLibraryArtifactAsync(nativeArtifact.Value, cancellationToken));
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Downloading library {libraryName}: {ex.Message}", ex);
            }

            completed++;
            progress?.Report(20 + completed * 25d / Math.Max(libraries.Count, 1));
        }

        return nativeArchives;
    }

    private static async Task<string> DownloadLibraryArtifactAsync(JsonElement artifact, CancellationToken cancellationToken)
    {
        var path = artifact.GetProperty("path").GetString() ?? throw new InvalidDataException("Missing library path.");
        var destination = Path.Combine(MinecraftRoot, "libraries", path.Replace('/', Path.DirectorySeparatorChar));

        await DownloadFileWithSha1Async(
            artifact.GetProperty("url").GetString() ?? throw new InvalidDataException("Missing library URL."),
            destination,
            artifact.TryGetProperty("sha1", out var sha1) ? sha1.GetString() : null,
            cancellationToken);

        return destination;
    }

    private static JsonElement? GetWindowsNativeArtifact(JsonElement library, JsonElement downloads)
    {
        if (!library.TryGetProperty("natives", out var natives) ||
            !natives.TryGetProperty("windows", out var windowsClassifier) ||
            !downloads.TryGetProperty("classifiers", out var classifiers))
        {
            return null;
        }

        var classifier = (windowsClassifier.GetString() ?? "").Replace("${arch}", "64");
        return classifiers.TryGetProperty(classifier, out var artifact) ? artifact : null;
    }

    private static async Task DownloadAssetsAsync(JsonDocument versionMetadata, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var assetIndex = versionMetadata.RootElement.GetProperty("assetIndex");
        var assetIndexId = assetIndex.GetProperty("id").GetString() ?? "legacy";
        var assetIndexPath = Path.Combine(MinecraftRoot, "assets", "indexes", $"{assetIndexId}.json");

        await DownloadFileWithSha1Async(
            assetIndex.GetProperty("url").GetString() ?? throw new InvalidDataException("Missing asset index URL."),
            assetIndexPath,
            assetIndex.TryGetProperty("sha1", out var sha1) ? sha1.GetString() : null,
            cancellationToken);

        using var indexDocument = JsonDocument.Parse(await File.ReadAllTextAsync(assetIndexPath, cancellationToken));
        var assets = indexDocument.RootElement.GetProperty("objects").EnumerateObject().Select(asset => asset.Value).ToList();
        var completed = 0;
        using var semaphore = new SemaphoreSlim(8);
        var tasks = assets.Select(async asset =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var hash = asset.GetProperty("hash").GetString() ?? throw new InvalidDataException("Asset hash missing.");
                var prefix = hash[..2];
                var destination = Path.Combine(MinecraftRoot, "assets", "objects", prefix, hash);
                var url = $"https://resources.download.minecraft.net/{prefix}/{hash}";
                try
                {
                    await DownloadFileWithSha1Async(url, destination, hash, cancellationToken);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Downloading asset {hash}: {ex.Message}", ex);
                }

                var done = Interlocked.Increment(ref completed);
                progress?.Report(45 + done * 45d / Math.Max(assets.Count, 1));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private static void ExtractNativesSafely(string minecraftVersion, IEnumerable<string> nativeArchives)
    {
        var nativesDirectory = GetNativesDirectory(minecraftVersion);
        if (Directory.Exists(nativesDirectory))
        {
            Directory.Delete(nativesDirectory, recursive: true);
        }

        Directory.CreateDirectory(nativesDirectory);
        var root = Path.GetFullPath(nativesDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        foreach (var archivePath in nativeArchives)
        {
            try
            {
                using var archive = ZipFile.OpenRead(archivePath);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name) || entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("Native archive contains an unsafe path.");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, true);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Extracting native {archivePath}: {ex.Message}", ex);
            }
        }
    }

    private static async Task<bool> ValidateClientAsync(string minecraftVersion, JsonElement metadata)
    {
        var client = metadata.GetProperty("downloads").GetProperty("client");
        return await FileHasSha1Async(GetClientJarPath(minecraftVersion), client.GetProperty("sha1").GetString());
    }

    private static async Task<bool> ValidateLibrariesAsync(JsonElement metadata)
    {
        foreach (var library in metadata.GetProperty("libraries").EnumerateArray().Where(IsLibraryAllowed))
        {
            if (!library.TryGetProperty("downloads", out var downloads))
            {
                continue;
            }

            var libraryName = library.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? "unknown"
                : "unknown";

            if (downloads.TryGetProperty("artifact", out var artifact) && !await ArtifactIsValidAsync(artifact, $"library {libraryName}"))
            {
                return false;
            }

            var nativeArtifact = GetWindowsNativeArtifact(library, downloads);
            if (nativeArtifact.HasValue && !await ArtifactIsValidAsync(nativeArtifact.Value, $"native library {libraryName}"))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> ValidateAssetsAsync(JsonElement metadata)
    {
        var assetIndex = metadata.GetProperty("assetIndex");
        var assetIndexId = assetIndex.GetProperty("id").GetString() ?? "legacy";
        var assetIndexPath = Path.Combine(MinecraftRoot, "assets", "indexes", $"{assetIndexId}.json");

        if (!await FileHasSha1Async(assetIndexPath, assetIndex.TryGetProperty("sha1", out var sha1) ? sha1.GetString() : null))
        {
            return false;
        }

        using var indexDocument = JsonDocument.Parse(await File.ReadAllTextAsync(assetIndexPath));
        foreach (var asset in indexDocument.RootElement.GetProperty("objects").EnumerateObject())
        {
            var hash = asset.Value.GetProperty("hash").GetString() ?? "";
            var path = Path.Combine(MinecraftRoot, "assets", "objects", hash[..2], hash);
            if (!await FileHasSha1Async(path, hash))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> ArtifactIsValidAsync(JsonElement artifact, string context)
    {
        var path = artifact.GetProperty("path").GetString() ?? "";
        var destination = Path.Combine(MinecraftRoot, "libraries", path.Replace('/', Path.DirectorySeparatorChar));
        var sha1 = artifact.TryGetProperty("sha1", out var sha1Element) ? sha1Element.GetString() : null;
        try
        {
            return await FileHasSha1Async(destination, sha1);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Validating {context} at {destination}: {ex.Message}", ex);
        }
    }

    private static bool IsLibraryAllowed(JsonElement library)
    {
        if (!library.TryGetProperty("rules", out var rules))
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            if (!RuleAppliesToWindows(rule))
            {
                continue;
            }

            allowed = string.Equals(rule.GetProperty("action").GetString(), "allow", StringComparison.OrdinalIgnoreCase);
        }

        return allowed;
    }

    private static bool RuleAppliesToWindows(JsonElement rule)
    {
        if (!rule.TryGetProperty("os", out var os))
        {
            return true;
        }

        return os.TryGetProperty("name", out var name) &&
               string.Equals(name.GetString(), "windows", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task DownloadFileWithSha1Async(string url, string destination, string? expectedSha1, CancellationToken cancellationToken)
    {
        if (await FileHasSha1Async(destination, expectedSha1))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var tempPath = destination + ".tmp";

        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        if (!await FileHasSha1Async(tempPath, expectedSha1))
        {
            TryDeleteFile(tempPath);
            throw new InvalidDataException($"SHA-1 verification failed for {Path.GetFileName(destination)}.");
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

    private static string GetVersionJsonPath(string minecraftVersion)
    {
        return Path.Combine(MinecraftRoot, "versions", minecraftVersion, $"{minecraftVersion}.json");
    }

    private static string GetClientJarPath(string minecraftVersion)
    {
        return Path.Combine(MinecraftRoot, "versions", minecraftVersion, $"{minecraftVersion}.jar");
    }

    private static string GetNativesDirectory(string minecraftVersion)
    {
        return Path.Combine(MinecraftRoot, "versions", minecraftVersion, "natives");
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
            // Cleanup should not hide the original runtime error.
        }
    }

    private static async Task<T> RunWithContextAsync<T>(string context, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{context}: {ex.Message}", ex);
        }
    }

    private static async Task RunWithContextAsync(string context, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{context}: {ex.Message}", ex);
        }
    }

    private static void RunWithContext(string context, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{context}: {ex.Message}", ex);
        }
    }

    private static string FormatException(Exception exception)
    {
        return exception.ToString();
    }

    private static void WriteDiagnosticLog(string diagnosticText)
    {
        Console.Error.WriteLine(diagnosticText);
        Debug.WriteLine(diagnosticText);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(
                LogPath,
                $"[{DateTimeOffset.Now:O}] Minecraft install failure{Environment.NewLine}{diagnosticText}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logException)
        {
            Console.Error.WriteLine($"Failed to write Minecraft install log: {logException}");
            Debug.WriteLine($"Failed to write Minecraft install log: {logException}");
        }
    }
}
