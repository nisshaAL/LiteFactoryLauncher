using Avalonia.Controls;
using Avalonia.Interactivity;
using LiteFactoryLauncher.Models;
using System.Diagnostics;
using System.IO;

namespace LiteFactoryLauncher.Views;

public partial class LauncherView : UserControl
{
    public LauncherView()
    {
        InitializeComponent();
    }

    private void PlayButton_Click(object? sender, RoutedEventArgs e)
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

    private void ShowLaunchStatus(string message, bool success)
    {
        LaunchStatus.Foreground = success
            ? Avalonia.Media.Brushes.LightGreen
            : Avalonia.Media.Brushes.IndianRed;

        LaunchStatus.Text = message;
    }
}
