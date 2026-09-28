using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LiteFactoryLauncher.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is Window window)
        {
            window.Content = new LauncherView();
        }
    }

    private void SelectGameButton_Click(object? sender, RoutedEventArgs e)
    {
        SaveStatus.Text = "Выбор файла подключим следующим шагом.";
    }
}