using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LiteFactoryLauncher.Models;
using System.Collections.Generic;
using System.IO;

namespace LiteFactoryLauncher.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        GamePathBox.Text = LauncherSettings.LoadGamePath() ?? "";
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as Window;

        if (window != null)
        {
            window.Content = new LauncherView();
        }
    }

    private async void SelectGameButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);

        if (topLevel == null)
        {
            SaveStatus.Foreground = Avalonia.Media.Brushes.IndianRed;
            SaveStatus.Text = "Cannot open file picker from this window.";
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select game executable",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
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
            !string.Equals(Path.GetExtension(selectedPath), ".exe", System.StringComparison.OrdinalIgnoreCase))
        {
            SaveStatus.Foreground = Avalonia.Media.Brushes.IndianRed;
            SaveStatus.Text = "Please select a Windows .exe file.";
            return;
        }

        LauncherSettings.SaveGamePath(selectedPath);
        GamePathBox.Text = selectedPath;
        SaveStatus.Foreground = Avalonia.Media.Brushes.LightGreen;
        SaveStatus.Text = "Game executable saved.";
    }
}
