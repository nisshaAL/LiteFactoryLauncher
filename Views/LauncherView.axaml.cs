using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using LiteFactoryLauncher.Models;
using LiteFactoryLauncher.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace LiteFactoryLauncher.Views;

public partial class LauncherView : UserControl
{
    private const string InstallButtonText = "\u0423\u0421\u0422\u0410\u041d\u041e\u0412\u0418\u0422\u042c";
    private const string InstallingButtonText = "\u0423\u0421\u0422\u0410\u041d\u041e\u0412\u041a\u0410...";
    private const string UpdateButtonText = "\u041e\u0411\u041d\u041e\u0412\u0418\u0422\u042c";
    private const string PlayButtonText = "\u0418\u0413\u0420\u0410\u0422\u042c";
    private const string PreparingButtonText = "\u041f\u041e\u0414\u0413\u041e\u0422\u041e\u0412\u041a\u0410...";

    private readonly RemoteManifestService _remoteManifestService = new();
    private readonly ModpackDownloadService _downloadService = new();
    private readonly ModpackInstaller _installer = new();
    private readonly GameRuntimeService _runtimeService = new();
    private readonly ManagedJavaService _managedJavaService = new();
    private readonly MinecraftRuntimeService _minecraftRuntimeService = new();
    private readonly ForgeRuntimeService _forgeRuntimeService = new();
    private readonly GamePreparationService _preparationService = new();
    private ModpackManifest _manifest;
    private GameRuntimeStatus? _runtimeStatus;
    private LauncherState _state;
    private bool _isInstallRunning;
    private bool _isPreparationRunning;
    private bool _isJavaInstallRunning;
    private bool? _minecraftRuntimeReady;
    private bool _isMinecraftInstallRunning;
    private bool? _forgeRuntimeReady;
    private bool _isForgeInstallRunning;
    private CancellationTokenSource? _preparationCancellation;

    public LauncherView()
    {
        InitializeComponent();

        _manifest = LoadBundledManifest();
        RefreshLauncherState();
        _ = LoadRuntimeStatusAsync();
        _ = CheckRemoteManifestAsync();
    }

    private async void PlayButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isPreparationRunning)
        {
            return;
        }

        await PrepareGameAsync();
    }

    private async System.Threading.Tasks.Task CheckRemoteManifestAsync()
    {
        if (_isInstallRunning)
        {
            return;
        }

        ShowStatus("\u041f\u0440\u043e\u0432\u0435\u0440\u043a\u0430 \u043e\u0431\u043d\u043e\u0432\u043b\u0435\u043d\u0438\u0439...", Brushes.LightGreen);

        var result = await _remoteManifestService.LoadRemoteManifestAsync();
        if (_isInstallRunning)
        {
            return;
        }

        if (result.Success && result.Manifest != null)
        {
            _manifest = result.Manifest;
            RefreshLauncherState();
            return;
        }

        RefreshLauncherState("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043f\u0440\u043e\u0432\u0435\u0440\u0438\u0442\u044c \u043e\u0431\u043d\u043e\u0432\u043b\u0435\u043d\u0438\u044f");
    }

    private async System.Threading.Tasks.Task InstallOrUpdateFromInternetAsync()
    {
        _isInstallRunning = true;
        string? archivePath = null;

        try
        {
            var installDirectory = LauncherSettings.LoadInstallDirectory();
            LauncherSettings.SaveInstallDirectory(installDirectory);

            SetState(LauncherState.Downloading, "\u0421\u043a\u0430\u0447\u0438\u0432\u0430\u043d\u0438\u0435...");
            InstallProgress.Value = 0;

            var downloadProgress = new Progress<double>(value =>
            {
                InstallProgress.Value = Math.Clamp(value, 0, 100);
            });

            var downloadResult = await _downloadService.DownloadArchiveAsync(_manifest, downloadProgress);
            if (!downloadResult.Success || string.IsNullOrWhiteSpace(downloadResult.ArchivePath))
            {
                SetState(LauncherState.Error, downloadResult.ErrorMessage ?? "Download failed.");
                return;
            }

            archivePath = downloadResult.ArchivePath;

            SetState(LauncherState.Verifying, "\u041f\u0440\u043e\u0432\u0435\u0440\u043a\u0430 \u0444\u0430\u0439\u043b\u043e\u0432...");
            InstallProgress.Value = 100;

            if (string.IsNullOrWhiteSpace(_manifest.Sha256))
            {
                ModpackDownloadService.TryDeleteFile(archivePath);
                SetState(LauncherState.Error, "Manifest does not contain a SHA-256 hash.");
                return;
            }

            string actualHash;
            try
            {
                actualHash = await _downloadService.ComputeSha256Async(archivePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ModpackDownloadService.TryDeleteFile(archivePath);
                SetState(LauncherState.Error, $"Hash verification failed: {ex.Message}");
                return;
            }

            if (!string.Equals(actualHash, _manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                ModpackDownloadService.TryDeleteFile(archivePath);
                SetState(LauncherState.Error, "Downloaded archive failed SHA-256 verification.");
                return;
            }

            SetState(LauncherState.Installing, "\u0423\u0441\u0442\u0430\u043d\u043e\u0432\u043a\u0430...");
            InstallProgress.Value = 0;

            var installProgress = new Progress<double>(value =>
            {
                InstallProgress.Value = Math.Clamp(value, 0, 100);
            });

            var installResult = await _installer.InstallFromZipAsync(archivePath, installDirectory, installProgress);
            if (!installResult.Success)
            {
                ModpackDownloadService.TryDeleteFile(archivePath);
                SetState(LauncherState.Error, installResult.ErrorMessage ?? "Installation failed.");
                return;
            }

            LauncherSettings.SaveInstalledPackVersion(_manifest.PackVersion);
            ModpackDownloadService.TryDeleteFile(archivePath);
            SetState(LauncherState.Installed, "Light Factory installed.");
        }
        catch (Exception ex)
        {
            ModpackDownloadService.TryDeleteFile(archivePath);
            SetState(LauncherState.Error, $"Installation failed: {ex.Message}");
        }
        finally
        {
            _isInstallRunning = false;
        }
    }

    private async System.Threading.Tasks.Task PrepareGameAsync()
    {
        _isPreparationRunning = true;
        _isInstallRunning = true;
        _preparationCancellation = new CancellationTokenSource();
        SetTechnicalActionsEnabled(false);
        SetState(LauncherState.Preparing, "Preparing Light Factory...");
        InstallProgress.Value = 0;
        ToolTip.SetTip(LaunchStatus, null);

        try
        {
            var profile = new GameProfile
            {
                Id = "light-factory",
                DisplayName = "Light Factory",
                MinecraftVersion = string.IsNullOrWhiteSpace(_manifest.MinecraftVersion) ? "1.12.2" : _manifest.MinecraftVersion,
                ForgeVersion = string.IsNullOrWhiteSpace(_manifest.ForgeVersion) ? "14.23.5.2860" : _manifest.ForgeVersion,
                Manifest = _manifest
            };

            JavaStatusText.Text = "Waiting...";
            JavaStatusText.Foreground = Brushes.LightGreen;
            MinecraftVersionText.Text = $"{profile.MinecraftVersion} - Waiting";
            ForgeVersionText.Text = $"{profile.ForgeVersion} - Waiting";

            var progress = new Progress<GamePreparationStatus>(status =>
            {
                InstallProgress.Value = Math.Clamp(status.OverallProgress, 0, 100);
                ShowStatus(status.StatusText, status.Phase == GamePreparationPhase.Failed ? Brushes.IndianRed : Brushes.LightGreen);
                JavaStatusText.Text = FormatComponentStatus("Java 8", status.JavaStatus);
                JavaStatusText.Foreground = status.JavaStatus.Contains("Required", StringComparison.OrdinalIgnoreCase)
                    ? Brushes.Gold
                    : Brushes.LightGreen;
                MinecraftVersionText.Text = FormatComponentStatus(profile.MinecraftVersion, status.MinecraftStatus);
                ForgeVersionText.Text = FormatComponentStatus(profile.ForgeVersion, status.ForgeStatus);

                if (!string.IsNullOrWhiteSpace(status.ModpackStatus))
                {
                    LaunchStatus.Text = $"{status.StatusText} Light Factory: {status.ModpackStatus}";
                }
            });

            var result = await _preparationService.PrepareAsync(profile, progress, _preparationCancellation.Token);
            if (result.Manifest != null)
            {
                _manifest = result.Manifest;
            }

            if (!result.Success)
            {
                SetState(LauncherState.Error, $"Preparation failed: {result.ErrorMessage ?? "Unknown error."}");
                ToolTip.SetTip(LaunchStatus, result.DiagnosticText ?? result.ErrorMessage);
                await LoadRuntimeStatusAsync();
                return;
            }

            ToolTip.SetTip(LaunchStatus, null);
            await LoadRuntimeStatusAsync();
            RefreshLauncherState("Ready to launch.");
        }
        finally
        {
            _isPreparationRunning = false;
            _isInstallRunning = false;
            _preparationCancellation.Dispose();
            _preparationCancellation = null;
            SetTechnicalActionsEnabled(true);
        }
    }

    private void LaunchGame()
    {
        var gamePath = LauncherSettings.LoadGamePath();

        if (string.IsNullOrWhiteSpace(gamePath))
        {
            ShowLaunchStatus("Select the game executable in Settings.", false);
            return;
        }

        if (!File.Exists(gamePath))
        {
            ShowLaunchStatus("The saved game executable no longer exists. Select it again in Settings.", false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = gamePath,
                WorkingDirectory = Path.GetDirectoryName(gamePath),
                UseShellExecute = true
            });

            ShowLaunchStatus("Game launched.", true);
        }
        catch
        {
            ShowLaunchStatus("Could not launch the saved game executable.", false);
        }
    }

    private void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as Window;

        if (window != null)
        {
            window.Content = new SettingsView();
        }
    }

    private void RefreshLauncherState(string? statusOverride = null)
    {
        var installedVersion = LauncherSettings.LoadInstalledPackVersion();
        var installationValid = IsInstallationValid();
        InstalledVersionText.Text = installedVersion ?? "-";
        LatestVersionText.Text = string.IsNullOrWhiteSpace(_manifest.PackVersion) ? "-" : _manifest.PackVersion;
        UpdateMinecraftRuntimeText();
        UpdateForgeRuntimeText();

        if (string.IsNullOrWhiteSpace(installedVersion) || !installationValid)
        {
            SetState(LauncherState.NotInstalled, statusOverride ?? "Light Factory is not installed.");
            return;
        }

        var comparison = ComparePackVersions(installedVersion, _manifest.PackVersion);
        if (comparison < 0)
        {
            SetState(LauncherState.UpdateAvailable, statusOverride ?? $"\u0414\u043e\u0441\u0442\u0443\u043f\u043d\u043e \u043e\u0431\u043d\u043e\u0432\u043b\u0435\u043d\u0438\u0435: {_manifest.PackVersion}");
            return;
        }

        if (comparison == 0)
        {
            SetState(LauncherState.Installed, statusOverride ?? $"Light Factory {_manifest.PackVersion} is installed.");
            return;
        }

        SetState(LauncherState.Installed, statusOverride ?? $"Installed version {installedVersion} is newer than latest {_manifest.PackVersion}.");
    }

    private bool IsInstallationValid()
    {
        var installDirectory = LauncherSettings.LoadInstallDirectory();
        return Directory.Exists(installDirectory) &&
               Directory.Exists(Path.Combine(installDirectory, "mods")) &&
               Directory.Exists(Path.Combine(installDirectory, "config"));
    }

    private async System.Threading.Tasks.Task LoadRuntimeStatusAsync()
    {
        JavaStatusText.Text = "Checking...";
        JavaStatusText.Foreground = Brushes.LightGreen;
        ToolTip.SetTip(JavaStatusText, null);
        InstallJavaButton.IsVisible = false;
        JavaInstallProgress.IsVisible = false;
        JavaInstallProgress.Value = 0;
        InstallMinecraftButton.IsVisible = false;
        MinecraftInstallProgress.IsVisible = false;
        MinecraftInstallProgress.Value = 0;
        InstallForgeButton.IsVisible = false;
        ForgeInstallProgress.IsVisible = false;
        ForgeInstallProgress.Value = 0;

        var status = await _runtimeService.GetRuntimeStatusAsync(
            string.IsNullOrWhiteSpace(_manifest.MinecraftVersion) ? "1.12.2" : _manifest.MinecraftVersion,
            string.IsNullOrWhiteSpace(_manifest.ForgeVersion) ? "14.23.5.2860" : _manifest.ForgeVersion);

        _runtimeStatus = status;
        _minecraftRuntimeReady = status.MinecraftInstalled;
        UpdateMinecraftRuntimeText();
        InstallMinecraftButton.IsVisible = !status.MinecraftInstalled;
        _forgeRuntimeReady = status.ForgeInstalled;
        UpdateForgeRuntimeText();
        InstallForgeButton.IsVisible = status.MinecraftInstalled && !status.ForgeInstalled;

        if (status.CompatibleJavaFound && status.CompatibleJava != null)
        {
            JavaStatusText.Text = status.CompatibleJava.IsManaged
                ? "Java 8 (LiteFactory Runtime)"
                : "Java 8 detected";
            JavaStatusText.Foreground = Brushes.LightGreen;
            ToolTip.SetTip(JavaStatusText, status.CompatibleJava.Path);
            return;
        }

        if (status.JavaFound)
        {
            var detectedJava = status.JavaRuntimes[0];
            var version = string.IsNullOrWhiteSpace(detectedJava.Version)
                ? "unknown version"
                : $"Java {detectedJava.Version}";

            JavaStatusText.Text = $"{version} detected; Java 8 not found";
            JavaStatusText.Foreground = Brushes.Gold;
            ToolTip.SetTip(JavaStatusText, detectedJava.Path);
            InstallJavaButton.IsVisible = true;
            return;
        }

        JavaStatusText.Text = "Java 8 required";
        JavaStatusText.Foreground = Brushes.IndianRed;
        ToolTip.SetTip(JavaStatusText, null);
        InstallJavaButton.IsVisible = true;
    }

    private async void InstallJavaButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isPreparationRunning || _isJavaInstallRunning)
        {
            return;
        }

        _isJavaInstallRunning = true;
        InstallJavaButton.IsEnabled = false;
        JavaInstallProgress.IsVisible = true;
        JavaInstallProgress.Value = 0;

        try
        {
            JavaStatusText.Text = "Downloading Java...";
            JavaStatusText.Foreground = Brushes.LightGreen;

            var progress = new Progress<double>(value =>
            {
                JavaInstallProgress.Value = Math.Clamp(value, 0, 100);
            });

            var phase = new Progress<string>(message =>
            {
                JavaStatusText.Text = message;
            });

            var result = await _managedJavaService.InstallJava8Async(progress, phase);
            if (!result.Success)
            {
                JavaStatusText.Text = result.ErrorMessage ?? "Java installation failed.";
                JavaStatusText.Foreground = Brushes.IndianRed;
                InstallJavaButton.IsVisible = true;
                return;
            }

            JavaStatusText.Text = "Checking Java...";
            JavaStatusText.Foreground = Brushes.LightGreen;
            await LoadRuntimeStatusAsync();
        }
        finally
        {
            _isJavaInstallRunning = false;
            InstallJavaButton.IsEnabled = true;
        }
    }

    private async void InstallMinecraftButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isPreparationRunning || _isMinecraftInstallRunning)
        {
            return;
        }

        _isMinecraftInstallRunning = true;
        InstallMinecraftButton.IsEnabled = false;
        MinecraftInstallProgress.IsVisible = true;
        MinecraftInstallProgress.Value = 0;

        try
        {
            var minecraftVersion = string.IsNullOrWhiteSpace(_manifest.MinecraftVersion) ? "1.12.2" : _manifest.MinecraftVersion;
            MinecraftVersionText.Text = $"{minecraftVersion} - Installing";
            ToolTip.SetTip(MinecraftVersionText, null);

            var progress = new Progress<double>(value =>
            {
                MinecraftInstallProgress.Value = Math.Clamp(value, 0, 100);
            });

            var status = new Progress<string>(message =>
            {
                MinecraftVersionText.Text = message;
            });

            var result = await _minecraftRuntimeService.InstallAsync(minecraftVersion, progress, status);
            if (!result.Success)
            {
                _minecraftRuntimeReady = false;
                MinecraftVersionText.Text = result.ErrorMessage ?? "Minecraft installation failed.";
                ToolTip.SetTip(MinecraftVersionText, result.DiagnosticText ?? result.ErrorMessage);
                InstallMinecraftButton.IsVisible = true;
                return;
            }

            _minecraftRuntimeReady = true;
            ToolTip.SetTip(MinecraftVersionText, null);
            UpdateMinecraftRuntimeText();
            InstallMinecraftButton.IsVisible = false;
        }
        finally
        {
            _isMinecraftInstallRunning = false;
            InstallMinecraftButton.IsEnabled = true;
        }
    }

    private async void InstallForgeButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isPreparationRunning || _isForgeInstallRunning)
        {
            return;
        }

        var runtimeStatus = _runtimeStatus ?? await _runtimeService.GetRuntimeStatusAsync(
            string.IsNullOrWhiteSpace(_manifest.MinecraftVersion) ? "1.12.2" : _manifest.MinecraftVersion,
            string.IsNullOrWhiteSpace(_manifest.ForgeVersion) ? "14.23.5.2860" : _manifest.ForgeVersion);

        if (!runtimeStatus.CompatibleJavaFound || runtimeStatus.CompatibleJava == null)
        {
            ForgeVersionText.Text = "Java 8 required";
            InstallForgeButton.IsVisible = true;
            return;
        }

        if (!runtimeStatus.MinecraftInstalled)
        {
            ForgeVersionText.Text = "Minecraft required";
            InstallForgeButton.IsVisible = true;
            return;
        }

        _isForgeInstallRunning = true;
        InstallForgeButton.IsEnabled = false;
        ForgeInstallProgress.IsVisible = true;
        ForgeInstallProgress.Value = 0;

        try
        {
            var minecraftVersion = string.IsNullOrWhiteSpace(_manifest.MinecraftVersion) ? "1.12.2" : _manifest.MinecraftVersion;
            var forgeVersion = string.IsNullOrWhiteSpace(_manifest.ForgeVersion) ? "14.23.5.2860" : _manifest.ForgeVersion;
            ForgeVersionText.Text = $"{forgeVersion} - Installing";
            ToolTip.SetTip(ForgeVersionText, null);

            var progress = new Progress<double>(value =>
            {
                ForgeInstallProgress.Value = Math.Clamp(value, 0, 100);
            });

            var status = new Progress<string>(message =>
            {
                ForgeVersionText.Text = message;
            });

            var result = await _forgeRuntimeService.InstallAsync(
                minecraftVersion,
                forgeVersion,
                runtimeStatus.CompatibleJava.Path,
                progress,
                status);

            if (!result.Success)
            {
                _forgeRuntimeReady = false;
                ForgeVersionText.Text = result.ErrorMessage ?? "Forge installation failed.";
                ToolTip.SetTip(ForgeVersionText, result.DiagnosticText ?? result.ErrorMessage);
                InstallForgeButton.IsVisible = true;
                return;
            }

            _forgeRuntimeReady = true;
            ToolTip.SetTip(ForgeVersionText, null);
            UpdateForgeRuntimeText();
            InstallForgeButton.IsVisible = false;
            await LoadRuntimeStatusAsync();
        }
        finally
        {
            _isForgeInstallRunning = false;
            InstallForgeButton.IsEnabled = true;
        }
    }

    private void UpdateMinecraftRuntimeText()
    {
        var minecraftVersion = string.IsNullOrWhiteSpace(_manifest.MinecraftVersion) ? "1.12.2" : _manifest.MinecraftVersion;
        MinecraftVersionText.Text = _minecraftRuntimeReady switch
        {
            true => $"{minecraftVersion} - Ready",
            false => $"{minecraftVersion} - Not installed",
            _ => minecraftVersion
        };
    }

    private void UpdateForgeRuntimeText()
    {
        var forgeVersion = string.IsNullOrWhiteSpace(_manifest.ForgeVersion) ? "14.23.5.2860" : _manifest.ForgeVersion;
        ForgeVersionText.Text = _forgeRuntimeReady switch
        {
            true => $"{forgeVersion} - Ready",
            false => $"{forgeVersion} - Not installed",
            _ => forgeVersion
        };
    }

    private void SetState(LauncherState state, string status)
    {
        _state = state;

        MainActionButton.IsEnabled =
            state != LauncherState.Downloading &&
            state != LauncherState.Verifying &&
            state != LauncherState.Installing &&
            state != LauncherState.Preparing;

        switch (state)
        {
            case LauncherState.NotInstalled:
                MainActionButton.Content = PlayButtonText;
                InstallProgress.Value = 0;
                ShowStatus(status, Brushes.IndianRed);
                break;
            case LauncherState.UpdateAvailable:
                MainActionButton.Content = PlayButtonText;
                ShowStatus(status, Brushes.Gold);
                break;
            case LauncherState.Downloading:
                MainActionButton.Content = InstallingButtonText;
                ShowStatus(status, Brushes.LightGreen);
                break;
            case LauncherState.Verifying:
                MainActionButton.Content = InstallingButtonText;
                ShowStatus(status, Brushes.LightGreen);
                break;
            case LauncherState.Installing:
                MainActionButton.Content = InstallingButtonText;
                ShowStatus(status, Brushes.LightGreen);
                break;
            case LauncherState.Preparing:
                MainActionButton.Content = PreparingButtonText;
                ShowStatus(status, Brushes.LightGreen);
                break;
            case LauncherState.Installed:
                MainActionButton.Content = PlayButtonText;
                InstallProgress.Value = 100;
                ShowStatus(status, Brushes.LightGreen);
                break;
            case LauncherState.Error:
                MainActionButton.Content = PlayButtonText;
                ShowStatus(status, Brushes.IndianRed);
                break;
        }
    }

    private void SetTechnicalActionsEnabled(bool enabled)
    {
        InstallJavaButton.IsEnabled = enabled;
        InstallMinecraftButton.IsEnabled = enabled;
        InstallForgeButton.IsEnabled = enabled;
    }

    private static string FormatComponentStatus(string label, string status)
    {
        return string.IsNullOrWhiteSpace(status) || status == "Waiting..."
            ? $"{label} - Waiting"
            : $"{label} - {status}";
    }

    private void ShowLaunchStatus(string message, bool success)
    {
        ShowStatus(message, success ? Brushes.LightGreen : Brushes.IndianRed);
    }

    private void ShowStatus(string message, IBrush brush)
    {
        LaunchStatus.Foreground = brush;
        LaunchStatus.Text = message;
    }

    private static ModpackManifest LoadBundledManifest()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            manifestPath = Path.Combine(Environment.CurrentDirectory, "manifest.json");
        }

        if (!File.Exists(manifestPath))
        {
            return new ModpackManifest
            {
                ProjectName = "Light Factory",
                MinecraftVersion = "1.12.2",
                ForgeVersion = "14.23.5.2860",
                PackVersion = "0.1.0",
                ArchiveFile = "LightFactory-0.1.0.zip",
                DownloadUrl = "https://github.com/nisshaAL/LiteFactoryLauncher/releases/download/v0.1.0/LightFactory-0.1.0.zip",
                Sha256 = "3B5C8F68D33E1D7FE6474747CD5B935D489B5489D30F84D92BE44EE81A8F05FC"
            };
        }

        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<ModpackManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new ModpackManifest();
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

        if (splitParts.Any(part => !int.TryParse(part, out _)))
        {
            parts = Array.Empty<int>();
            return false;
        }

        parts = splitParts.Select(int.Parse).ToArray();
        return true;
    }

    private enum LauncherState
    {
        NotInstalled,
        UpdateAvailable,
        Downloading,
        Verifying,
        Installing,
        Preparing,
        Installed,
        Error
    }
}
