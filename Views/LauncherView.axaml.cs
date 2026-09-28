using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LiteFactoryLauncher.Models;
using LiteFactoryLauncher.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LiteFactoryLauncher.Views;

public partial class LauncherView : UserControl
{
    private readonly ModpackInstaller _installer = new();
    private readonly ModpackManifest _manifest;
    private LauncherState _state;

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

        if (_state == LauncherState.Installing)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            SetState(LauncherState.Error, "Cannot open archive picker from this window.");
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Light Factory ZIP archive",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("ZIP archive")
                {
                    Patterns = new[] { "*.zip" }
                }
            }
        });

        if (files.Count == 0)
        {
            return;
        }

        var archivePath = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            SetState(LauncherState.Error, "Could not read selected archive path.");
            return;
        }

        await InstallSelectedArchiveAsync(archivePath);
    }

    private async System.Threading.Tasks.Task InstallSelectedArchiveAsync(string archivePath)
    {
        var installDirectory = LauncherSettings.LoadInstallDirectory();
        LauncherSettings.SaveInstallDirectory(installDirectory);

        SetState(LauncherState.Installing, "Installing Light Factory...");
        InstallProgress.Value = 0;

        var progress = new Progress<double>(value =>
        {
            InstallProgress.Value = Math.Clamp(value, 0, 100);
        });

        var result = await _installer.InstallFromZipAsync(archivePath, installDirectory, progress);

        if (!result.Success)
        {
            SetState(LauncherState.Error, result.ErrorMessage ?? "Installation failed.");
            return;
        }

        LauncherSettings.SaveInstalledPackVersion(_manifest.PackVersion);
        SetState(LauncherState.Installed, "Light Factory installed.");
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

        MainActionButton.IsEnabled = state != LauncherState.Installing;
        InstallProgress.Value = state == LauncherState.Installed ? 100 : InstallProgress.Value;

        switch (state)
        {
            case LauncherState.NotInstalled:
                MainActionButton.Content = "УСТАНОВИТЬ";
                InstallProgress.Value = 0;
                ShowStatus(status, Brushes.IndianRed);
                break;
            case LauncherState.Installing:
                MainActionButton.Content = "УСТАНОВКА...";
                ShowStatus(status, Brushes.LightGreen);
                break;
            case LauncherState.Installed:
                MainActionButton.Content = "ИГРАТЬ";
                InstalledVersionText.Text = _manifest.PackVersion;
                InstallProgress.Value = 100;
                ShowStatus(status, Brushes.LightGreen);
                break;
            case LauncherState.UpdateRequired:
                MainActionButton.Content = "УСТАНОВИТЬ";
                ShowStatus(status, Brushes.Gold);
                break;
            case LauncherState.Error:
                MainActionButton.Content = "УСТАНОВИТЬ";
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
                PackVersion = "0.1.0",
                ArchiveFile = "LightFactory-0.1.0.zip"
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
        Installing,
        Installed,
        UpdateRequired,
        Error
    }
}
