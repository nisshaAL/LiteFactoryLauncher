using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LiteFactoryLauncher.Models;
using LiteFactoryLauncher.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace LiteFactoryLauncher.Views;

public partial class SettingsView : UserControl
{
    private readonly LiteFactorySession? _session;
    private readonly Action? _logout;
    private bool _isLoading;

    public SettingsView()
        : this(null, null)
    {
    }

    public SettingsView(LiteFactorySession? session, Action? logout)
    {
        _session = session;
        _logout = logout;
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        _isLoading = true;
        try
        {
            var maxRamGb = LauncherSettings.LoadMaxRamMb() / 1024d;
            RamSlider.Value = Math.Clamp(maxRamGb, 2, 16);
            UpdateRamText();

            var javaMode = LauncherSettings.LoadJavaMode();
            JavaModeBox.SelectedIndex = string.Equals(javaMode, "Custom", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            CustomJavaPathBox.Text = LauncherSettings.LoadCustomJavaPath() ?? "";
            UpdateJavaModeControls();

            GameDirectoryBox.Text = LauncherSettings.LoadInstallDirectory();
            SaveStatus.Text = "";
            JavaSettingsStatus.Text = JavaModeBox.SelectedIndex == 0
                ? "Автоматический режим: системная Java 8, затем LiteFactory Runtime."
                : "";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as MainWindow;

        if (window != null)
        {
            window.ShowLauncher(_session, _logout);
        }
    }

    private void RamSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        var ramGb = (int)Math.Round(RamSlider.Value);
        RamSlider.Value = ramGb;
        LauncherSettings.SaveMaxRamMb(ramGb * 1024);
        UpdateRamText();
        ShowSaveStatus("Настройки памяти сохранены.", Brushes.LightGreen);
    }

    private void JavaModeBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        var custom = JavaModeBox.SelectedIndex == 1;
        LauncherSettings.SaveJavaMode(custom ? "Custom" : "Automatic");
        UpdateJavaModeControls();
        JavaSettingsStatus.Foreground = Brushes.LightGreen;
        JavaSettingsStatus.Text = custom
            ? "Выберите java.exe совместимой Java 8."
            : "Автоматический режим включен.";
    }

    private async void SelectJavaButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            ShowJavaStatus("Не удалось открыть выбор файла.", Brushes.IndianRed);
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите java.exe",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Java executable")
                {
                    Patterns = new[] { "java.exe" }
                },
                new("Windows executable")
                {
                    Patterns = new[] { "*.exe" }
                }
            }
        });

        if (files.Count == 0)
        {
            return;
        }

        var selectedPath = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(selectedPath) ||
            !string.Equals(Path.GetFileName(selectedPath), "java.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(selectedPath))
        {
            ShowJavaStatus("Выберите файл java.exe.", Brushes.IndianRed);
            return;
        }

        var runtime = await JavaDetectionService.InspectJavaAsync(selectedPath);
        if (runtime == null || !runtime.IsCompatible)
        {
            ShowJavaStatus("Выбранная Java недоступна или не является Java 8.", Brushes.IndianRed);
            return;
        }

        LauncherSettings.SaveCustomJavaPath(selectedPath);
        LauncherSettings.SaveJavaMode("Custom");
        CustomJavaPathBox.Text = selectedPath;
        JavaModeBox.SelectedIndex = 1;
        UpdateJavaModeControls();
        ShowJavaStatus("Путь к Java сохранен.", Brushes.LightGreen);
    }

    private void OpenGameFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        OpenFolder(LauncherSettings.LoadInstallDirectory(), "Папка игры открыта.");
    }

    private void OpenLogsFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        var logsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiteFactory",
            "logs");
        OpenFolder(logsPath, "Папка логов открыта.");
    }

    private void UpdateRamText()
    {
        RamValueText.Text = $"{(int)Math.Round(RamSlider.Value)} GB";
    }

    private void UpdateJavaModeControls()
    {
        var custom = JavaModeBox.SelectedIndex == 1;
        CustomJavaPathBox.IsEnabled = custom;
        SelectJavaButton.IsEnabled = custom;
    }

    private void OpenFolder(string path, string successMessage)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            ShowSaveStatus(successMessage, Brushes.LightGreen);
        }
        catch
        {
            ShowSaveStatus("Не удалось открыть папку.", Brushes.IndianRed);
        }
    }

    private void ShowSaveStatus(string message, IBrush brush)
    {
        SaveStatus.Foreground = brush;
        SaveStatus.Text = message;
    }

    private void ShowJavaStatus(string message, IBrush brush)
    {
        JavaSettingsStatus.Foreground = brush;
        JavaSettingsStatus.Text = message;
    }
}
