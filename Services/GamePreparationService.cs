using LiteFactoryLauncher.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LiteFactoryLauncher.Services;

public sealed class GamePreparationService
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiteFactory",
        "logs",
        "game-preparation.log");

    private readonly GameRuntimeService _runtimeService = new();
    private readonly ManagedJavaService _managedJavaService = new();
    private readonly MinecraftRuntimeService _minecraftRuntimeService = new();
    private readonly ForgeRuntimeService _forgeRuntimeService = new();
    private readonly RemoteManifestService _remoteManifestService = new();
    private readonly ModpackDownloadService _downloadService = new();
    private readonly ModpackInstaller _installer = new();

    public async Task<GamePreparationResult> PrepareAsync(
        GameProfile profile,
        IProgress<GamePreparationStatus>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var status = new GamePreparationStatus();
        var manifest = profile.Manifest;
        string? archivePath = null;

        try
        {
            Log($"Preparation started for profile {profile.Id} ({profile.DisplayName}). Minecraft={profile.MinecraftVersion}, Forge={profile.ForgeVersion}");
            Report(progress, status, GamePreparationPhase.CheckingEnvironment, "Checking environment...", 0);

            var runtimeStatus = await _runtimeService.GetRuntimeStatusAsync(profile.MinecraftVersion, profile.ForgeVersion);
            status.JavaStatus = runtimeStatus.CompatibleJavaFound ? "Ready" : "Required";
            status.MinecraftStatus = runtimeStatus.MinecraftInstalled ? "Ready" : "Waiting...";
            status.ForgeStatus = runtimeStatus.ForgeInstalled ? "Ready" : "Waiting...";
            Report(progress, status, GamePreparationPhase.CheckingEnvironment, "Environment checked.", 5);

            if (!runtimeStatus.CompatibleJavaFound)
            {
                Log("Compatible Java 8 was not found. Installing managed Java runtime.");
                status.JavaStatus = "Installing...";
                Report(progress, status, GamePreparationPhase.PreparingJava, "Preparing Java 8...", 5);

                var javaProgress = RangeProgress(progress, status, GamePreparationPhase.PreparingJava, "Preparing Java 8...", 5, 15);
                var javaPhase = new Progress<string>(message =>
                {
                    status.JavaStatus = message;
                    Report(progress, status, GamePreparationPhase.PreparingJava, message, status.OverallProgress);
                });

                var javaResult = await _managedJavaService.InstallJava8Async(javaProgress, javaPhase, cancellationToken);
                if (!javaResult.Success)
                {
                    return Fail(progress, status, GamePreparationPhase.PreparingJava, javaResult.ErrorMessage ?? "Java installation failed.", manifest);
                }

                runtimeStatus = await _runtimeService.GetRuntimeStatusAsync(profile.MinecraftVersion, profile.ForgeVersion);
                if (!runtimeStatus.CompatibleJavaFound)
                {
                    return Fail(progress, status, GamePreparationPhase.PreparingJava, "Java 8 could not be verified after installation.", manifest);
                }
            }
            else
            {
                Log($"Skipping Java installation. Compatible Java found: {runtimeStatus.CompatibleJava?.Path}");
            }

            status.JavaStatus = "Ready";
            Report(progress, status, GamePreparationPhase.PreparingJava, "Java 8 ready.", 15);

            if (!runtimeStatus.MinecraftInstalled)
            {
                Log($"Minecraft {profile.MinecraftVersion} is missing or invalid. Installing runtime.");
                status.MinecraftStatus = "Installing...";
                Report(progress, status, GamePreparationPhase.PreparingMinecraft, $"Preparing Minecraft {profile.MinecraftVersion}...", 15);

                var minecraftProgress = RangeProgress(progress, status, GamePreparationPhase.PreparingMinecraft, $"Preparing Minecraft {profile.MinecraftVersion}...", 15, 55);
                var minecraftStatus = new Progress<string>(message =>
                {
                    status.MinecraftStatus = message;
                    Report(progress, status, GamePreparationPhase.PreparingMinecraft, message, status.OverallProgress);
                });

                var minecraftResult = await _minecraftRuntimeService.InstallAsync(profile.MinecraftVersion, minecraftProgress, minecraftStatus, cancellationToken);
                if (!minecraftResult.Success)
                {
                    return Fail(progress, status, GamePreparationPhase.PreparingMinecraft, minecraftResult.ErrorMessage ?? "Minecraft installation failed.", manifest, minecraftResult.DiagnosticText);
                }

                runtimeStatus = await _runtimeService.GetRuntimeStatusAsync(profile.MinecraftVersion, profile.ForgeVersion);
                if (!runtimeStatus.MinecraftInstalled)
                {
                    return Fail(progress, status, GamePreparationPhase.PreparingMinecraft, "Minecraft runtime could not be verified after installation.", manifest);
                }
            }
            else
            {
                Log($"Skipping Minecraft installation. Minecraft {profile.MinecraftVersion} is already valid.");
            }

            status.MinecraftStatus = "Ready";
            Report(progress, status, GamePreparationPhase.PreparingMinecraft, $"Minecraft {profile.MinecraftVersion} ready.", 55);

            if (!runtimeStatus.ForgeInstalled)
            {
                if (!runtimeStatus.CompatibleJavaFound || runtimeStatus.CompatibleJava == null)
                {
                    return Fail(progress, status, GamePreparationPhase.PreparingForge, "Compatible Java 8 is required before installing Forge.", manifest);
                }

                Log($"Forge {profile.ForgeVersion} is missing or invalid. Installing runtime.");
                status.ForgeStatus = "Installing...";
                Report(progress, status, GamePreparationPhase.PreparingForge, $"Preparing Forge {profile.ForgeVersion}...", 55);

                var forgeProgress = RangeProgress(progress, status, GamePreparationPhase.PreparingForge, $"Preparing Forge {profile.ForgeVersion}...", 55, 70);
                var forgeStatus = new Progress<string>(message =>
                {
                    status.ForgeStatus = message;
                    Report(progress, status, GamePreparationPhase.PreparingForge, message, status.OverallProgress);
                });

                var forgeResult = await _forgeRuntimeService.InstallAsync(
                    profile.MinecraftVersion,
                    profile.ForgeVersion,
                    runtimeStatus.CompatibleJava.Path,
                    forgeProgress,
                    forgeStatus,
                    cancellationToken);

                if (!forgeResult.Success)
                {
                    return Fail(progress, status, GamePreparationPhase.PreparingForge, forgeResult.ErrorMessage ?? "Forge installation failed.", manifest, forgeResult.DiagnosticText);
                }

                runtimeStatus = await _runtimeService.GetRuntimeStatusAsync(profile.MinecraftVersion, profile.ForgeVersion);
                if (!runtimeStatus.ForgeInstalled)
                {
                    return Fail(progress, status, GamePreparationPhase.PreparingForge, "Forge runtime could not be verified after installation.", manifest);
                }
            }
            else
            {
                Log($"Skipping Forge installation. Forge {profile.ForgeVersion} is already valid.");
            }

            status.ForgeStatus = "Ready";
            Report(progress, status, GamePreparationPhase.PreparingForge, $"Forge {profile.ForgeVersion} ready.", 70);

            Report(progress, status, GamePreparationPhase.CheckingModpack, "Checking Light Factory...", 70);
            var remoteManifest = await _remoteManifestService.LoadRemoteManifestAsync(cancellationToken);
            if (remoteManifest.Success && remoteManifest.Manifest != null)
            {
                manifest = remoteManifest.Manifest;
                Log($"Loaded remote manifest. PackVersion={manifest.PackVersion}");
            }
            else
            {
                Log($"Remote manifest unavailable, using bundled manifest. Reason={remoteManifest.ErrorMessage}");
            }

            var installDirectory = LauncherSettings.LoadInstallDirectory();
            LauncherSettings.SaveInstallDirectory(installDirectory);
            var installedVersion = LauncherSettings.LoadInstalledPackVersion();
            var modpackValid = IsModpackInstallationValid(installDirectory);
            var comparison = string.IsNullOrWhiteSpace(installedVersion)
                ? -1
                : ComparePackVersions(installedVersion, manifest.PackVersion);

            if (!modpackValid || string.IsNullOrWhiteSpace(installedVersion) || comparison < 0)
            {
                var updating = modpackValid && !string.IsNullOrWhiteSpace(installedVersion) && comparison < 0;
                status.ModpackStatus = updating ? "Updating..." : "Installing...";
                var actionText = updating ? "Updating Light Factory..." : "Installing Light Factory...";
                Log($"{actionText} InstalledVersion={installedVersion ?? "(none)"}, LatestVersion={manifest.PackVersion}, Valid={modpackValid}");

                Report(progress, status, updating ? GamePreparationPhase.UpdatingModpack : GamePreparationPhase.DownloadingModpack, "Downloading Light Factory...", 70);
                var downloadProgress = RangeProgress(progress, status, GamePreparationPhase.DownloadingModpack, "Downloading Light Factory...", 70, 82);
                var downloadResult = await _downloadService.DownloadArchiveAsync(manifest, downloadProgress, cancellationToken);
                if (!downloadResult.Success || string.IsNullOrWhiteSpace(downloadResult.ArchivePath))
                {
                    return Fail(progress, status, GamePreparationPhase.DownloadingModpack, downloadResult.ErrorMessage ?? "Modpack download failed.", manifest);
                }

                archivePath = downloadResult.ArchivePath;
                Report(progress, status, updating ? GamePreparationPhase.UpdatingModpack : GamePreparationPhase.InstallingModpack, "Verifying Light Factory archive...", 84);
                var hashResult = await VerifyArchiveHashAsync(archivePath, manifest, cancellationToken);
                if (hashResult != null)
                {
                    ModpackDownloadService.TryDeleteFile(archivePath);
                    return Fail(progress, status, GamePreparationPhase.InstallingModpack, hashResult, manifest);
                }

                var installProgress = RangeProgress(progress, status, updating ? GamePreparationPhase.UpdatingModpack : GamePreparationPhase.InstallingModpack, actionText, 84, 95);
                var installResult = await _installer.InstallFromZipAsync(archivePath, installDirectory, installProgress, cancellationToken);
                if (!installResult.Success)
                {
                    ModpackDownloadService.TryDeleteFile(archivePath);
                    return Fail(progress, status, GamePreparationPhase.InstallingModpack, installResult.ErrorMessage ?? "Modpack installation failed.", manifest);
                }

                LauncherSettings.SaveInstalledPackVersion(manifest.PackVersion);
                ModpackDownloadService.TryDeleteFile(archivePath);
                archivePath = null;
                status.ModpackStatus = "Ready";
            }
            else
            {
                status.ModpackStatus = comparison > 0
                    ? $"Ready ({installedVersion})"
                    : "Ready";
                Log($"Skipping modpack installation. InstalledVersion={installedVersion}, LatestVersion={manifest.PackVersion}, Valid={modpackValid}");
            }

            Report(progress, status, GamePreparationPhase.FinalValidation, "Final validation...", 95);
            var finalRuntime = await _runtimeService.GetRuntimeStatusAsync(profile.MinecraftVersion, profile.ForgeVersion);
            var finalInstalledVersion = LauncherSettings.LoadInstalledPackVersion();
            var finalInstallDirectory = LauncherSettings.LoadInstallDirectory();
            var finalVersionComparison = string.IsNullOrWhiteSpace(finalInstalledVersion)
                ? -1
                : ComparePackVersions(finalInstalledVersion, manifest.PackVersion);

            if (!finalRuntime.CompatibleJavaFound)
            {
                return Fail(progress, status, GamePreparationPhase.FinalValidation, "Final validation failed: Java 8 is not ready.", manifest);
            }

            if (!finalRuntime.MinecraftInstalled)
            {
                return Fail(progress, status, GamePreparationPhase.FinalValidation, $"Final validation failed: Minecraft {profile.MinecraftVersion} is not ready.", manifest);
            }

            if (!finalRuntime.ForgeInstalled)
            {
                return Fail(progress, status, GamePreparationPhase.FinalValidation, $"Final validation failed: Forge {profile.ForgeVersion} is not ready.", manifest);
            }

            if (!IsModpackInstallationValid(finalInstallDirectory) || finalVersionComparison < 0)
            {
                return Fail(progress, status, GamePreparationPhase.FinalValidation, "Final validation failed: Light Factory is not current.", manifest);
            }

            status.JavaStatus = "Ready";
            status.MinecraftStatus = "Ready";
            status.ForgeStatus = "Ready";
            status.ModpackStatus = finalVersionComparison > 0 ? $"Ready ({finalInstalledVersion})" : "Ready";
            Report(progress, status, GamePreparationPhase.ReadyToLaunch, "Ready to launch.", 100);
            Log("Preparation completed successfully.");
            return GamePreparationResult.Ready(manifest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or TaskCanceledException or OperationCanceledException)
        {
            ModpackDownloadService.TryDeleteFile(archivePath);
            return Fail(progress, status, GamePreparationPhase.Failed, $"Preparation failed: {ex.Message}", manifest, ex.ToString());
        }
    }

    private static Progress<double> RangeProgress(
        IProgress<GamePreparationStatus>? progress,
        GamePreparationStatus status,
        GamePreparationPhase phase,
        string text,
        double start,
        double end)
    {
        return new Progress<double>(value =>
        {
            status.ComponentProgress = Math.Clamp(value, 0, 100);
            var mapped = start + (end - start) * status.ComponentProgress / 100d;
            Report(progress, status, phase, text, mapped);
        });
    }

    private async Task<string?> VerifyArchiveHashAsync(
        string archivePath,
        ModpackManifest manifest,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            return "Manifest does not contain a SHA-256 hash.";
        }

        var actualHash = await _downloadService.ComputeSha256Async(archivePath, cancellationToken);
        return string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase)
            ? null
            : "Downloaded archive failed SHA-256 verification.";
    }

    private static GamePreparationResult Fail(
        IProgress<GamePreparationStatus>? progress,
        GamePreparationStatus status,
        GamePreparationPhase phase,
        string message,
        ModpackManifest manifest,
        string? diagnosticText = null)
    {
        status.ErrorMessage = message;
        Report(progress, status, GamePreparationPhase.Failed, message, status.OverallProgress);
        Log($"Preparation failed during {phase}: {message}{Environment.NewLine}{diagnosticText}");
        return GamePreparationResult.Failed(message, diagnosticText, manifest);
    }

    private static void Report(
        IProgress<GamePreparationStatus>? progress,
        GamePreparationStatus status,
        GamePreparationPhase phase,
        string text,
        double overallProgress)
    {
        status.Phase = phase;
        status.StatusText = text;
        status.OverallProgress = Math.Clamp(overallProgress, 0, 100);
        progress?.Report(new GamePreparationStatus
        {
            Phase = status.Phase,
            StatusText = status.StatusText,
            OverallProgress = status.OverallProgress,
            ComponentProgress = status.ComponentProgress,
            JavaStatus = status.JavaStatus,
            MinecraftStatus = status.MinecraftStatus,
            ForgeStatus = status.ForgeStatus,
            ModpackStatus = status.ModpackStatus,
            ErrorMessage = status.ErrorMessage
        });

        Log($"{phase}: {text} ({status.OverallProgress:0.#}%)");
    }

    private static bool IsModpackInstallationValid(string installDirectory)
    {
        return Directory.Exists(installDirectory) &&
               Directory.Exists(Path.Combine(installDirectory, "mods")) &&
               Directory.Exists(Path.Combine(installDirectory, "config"));
    }

    private static int ComparePackVersions(string installedVersion, string latestVersion)
    {
        installedVersion = installedVersion.Trim();
        latestVersion = latestVersion.Trim();

        if (TryParseVersionParts(installedVersion, out var installedParts) &&
            TryParseVersionParts(latestVersion, out var latestParts))
        {
            var maxLength = Math.Max(installedParts.Length, latestParts.Length);
            for (var index = 0; index < maxLength; index++)
            {
                var installedPart = index < installedParts.Length ? installedParts[index] : 0;
                var latestPart = index < latestParts.Length ? latestParts[index] : 0;

                if (installedPart != latestPart)
                {
                    return installedPart.CompareTo(latestPart);
                }
            }

            return 0;
        }

        return string.Compare(installedVersion, latestVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersionParts(string version, out int[] parts)
    {
        var splitParts = version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (splitParts.Length == 0)
        {
            parts = Array.Empty<int>();
            return false;
        }

        var parsed = new int[splitParts.Length];
        for (var index = 0; index < splitParts.Length; index++)
        {
            if (!int.TryParse(splitParts[index], out parsed[index]))
            {
                parts = Array.Empty<int>();
                return false;
            }
        }

        parts = parsed;
        return true;
    }

    private static void Log(string message)
    {
        Console.Error.WriteLine(message);
        Debug.WriteLine(message);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to write preparation log: {ex}");
            Debug.WriteLine($"Failed to write preparation log: {ex}");
        }
    }
}
