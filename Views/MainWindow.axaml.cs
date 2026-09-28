using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LiteFactoryLauncher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void LoginButton_Click(object? sender, RoutedEventArgs e)
    {
        string username = UsernameBox.Text ?? "";
        string password = PasswordBox.Text ?? "";

        if (username == "admin" && password == "1234")
{
    Content = new LauncherView();
}
        else
        {
            LoginError.Foreground = Avalonia.Media.Brushes.IndianRed;
            LoginError.Text = "Неверный логин или пароль";
        }
    }
}