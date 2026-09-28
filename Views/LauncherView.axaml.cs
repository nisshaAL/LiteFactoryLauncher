using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using LiteFactoryLauncher.Models;
using LiteFactoryLauncher.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LiteFactoryLauncher.Views;

public partial class LauncherView : UserControl
{
    private const string InstallButtonText = "\u0423\u0421\u0422\u0410\u041d\u041e\u0412\u0418\u0422\u042c";
    private const string InstallingButtonText = "\u0423\u0421\u0422\u0410\u041d\u041e\u0412\u041a\u0410...";
    private const string PlayButtonText = "\u0418\u0413\u0420\u0410\u0422\u042c";

    private readonly ModpackDownloadService _downloadService = new();
    private readonly ModpackInstaller _installer = new();
    private readonly ModpackManifest _manifest;
    private LauncherState _state;
    private bool _isInstallRunning;

    public LauncherView()
    {
        InitializeComponent();

        _manifest = LoadManifest();
        RefreshLauncherState();
    }

    private async void PlayButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_state == LauncherState.Installed)
        {
            LaunchGame();
            return;
        }

        if (_isInstallRunning)
        {
            return;
        }

        await InstallFromInternetAsync();
    }

    private async System.Threading.Tasks.Task InstallFromInternetAsync()
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

    private void RefreshLauncherState()
    {
        var installedVersion = LauncherSettings.LoadInstalledPackVersion();
        InstalledVersionText.Text = installedVersion ?? "-";

        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            SetState(LauncherState.NotInstalled, "Light Factory is not installed.");
            return;
        }

        if (string.Equals(installedVersion, _manifest.PackVersion, StringComparison.OrdinalIgnoreCase))
        {
            SetState(LauncherState.Installed, "Light Factory is installed.");
            return;
        }

        SetState(LauncherState.UpdateRequired, $"Update required: {installedVersion} -> {_manifest.PackVersion}.");
    }

    private void SetState(LauncherState state, string status)
    {
        _state = state;

        MainActionButton.IsEnabled =
            state != LauncherState.Downloading &&
            state != LauncherState.Verifying &&
            state != LauncherState.Installing;

        switch (state)
        {
            case LauncherState.NotInstalled:
                MainActionButton.Content = InstallButtonText;
                InstallProgress.Value = 0;
                ShowStatus(status, Brushes.IndianRed);
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
            case LauncherState.Installed:
                MainActionButton.Content = PlayButtonText;
                InstalledVersionText.Text = _manifest.PackVersion;
                InstallProgress.Value = 100;
                ShowStatus(status, Brushes.LightGreen);
                break;
            case LauncherState.UpdateRequired:
                MainActionButton.Content = InstallButtonText;
                ShowStatus(status, Brushes.Gold);
                break;
            case LauncherState.Error:
                MainActionButton.Content = InstallButtonText;
                ShowStatus(status, Brushes.IndianRed);
                break;
        }
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

    private static ModpackManifest LoadManifest()
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

    private enum LauncherState
    {
        NotInstalled,
        Downloading,
        Verifying,
        Installing,
        Installed,
        UpdateRequired,
        Error
    }
}
