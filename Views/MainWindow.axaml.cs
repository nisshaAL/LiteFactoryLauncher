using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using LiteFactoryLauncher.Models;
using LiteFactoryLauncher.Services;
using System;
using System.Threading;

namespace LiteFactoryLauncher.Views;

public partial class MainWindow : Window
{
    private readonly LiteFactoryAuthService _authService = new();
    private readonly CredentialStorageService _credentialStorage = new();
    private LiteFactorySession? _session;
    private AuthState _authState = AuthState.LoggedOut;
    private CancellationTokenSource? _authCancellation;

    public MainWindow()
    {
        InitializeComponent();
        LoadRememberedCredentials();
    }

    private async void LoginButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_authState == AuthState.SigningIn)
        {
            return;
        }

        var login = LoginBox.Text?.Trim() ?? "";
        var password = PasswordBox.Text ?? "";

        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            ShowAuthStatus("Enter nickname and password.", false);
            return;
        }

        SetAuthBusy(true, "Signing in...");
        try
        {
            var result = await _authService.LoginAsync(login, password, _authCancellation!.Token);
            if (!result.Success || result.Session == null)
            {
                ShowAuthStatus(result.ErrorMessage ?? "Could not sign in.", false);
                return;
            }

            UpdateRememberedCredentials(login, password);
            EnterLauncher(result.Session);
        }
        finally
        {
            if (_authState != AuthState.LoggedIn)
            {
                SetAuthBusy(false);
            }
        }
    }

    private async void RegisterButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_authState == AuthState.SigningIn)
        {
            return;
        }

        var email = RegisterEmailBox.Text?.Trim() ?? "";
        var nickname = RegisterNicknameBox.Text?.Trim() ?? "";
        var password = RegisterPasswordBox.Text ?? "";
        var confirmPassword = RegisterConfirmPasswordBox.Text ?? "";

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(nickname) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(confirmPassword))
        {
            ShowAuthStatus("Fill in all registration fields.", false);
            return;
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            ShowAuthStatus("Passwords do not match.", false);
            return;
        }

        SetAuthBusy(true, "Creating account...");
        try
        {
            var result = await _authService.RegisterAsync(email, nickname, password, _authCancellation!.Token);
            if (!result.Success || result.Session == null)
            {
                ShowAuthStatus(result.ErrorMessage ?? "Could not create account.", false);
                return;
            }

            EnterLauncher(result.Session);
        }
        finally
        {
            if (_authState != AuthState.LoggedIn)
            {
                SetAuthBusy(false);
            }
        }
    }

    private void CreateAccountButton_Click(object? sender, RoutedEventArgs e)
    {
        LoginPanel.IsVisible = false;
        RegisterPanel.IsVisible = true;
        AuthTitle.Text = "Регистрация";
        AuthSubtitle.Text = "Создайте аккаунт LiteFactory";
        ShowAuthStatus("", true);
    }

    private void CancelRegistrationButton_Click(object? sender, RoutedEventArgs e)
    {
        RegisterPanel.IsVisible = false;
        LoginPanel.IsVisible = true;
        AuthTitle.Text = "Вход";
        AuthSubtitle.Text = "Войдите в свой аккаунт LiteFactory";
        ShowAuthStatus("", true);
    }

    private void EnterLauncher(LiteFactorySession session)
    {
        _session = session;
        _authState = AuthState.LoggedIn;
        PasswordBox.Text = "";
        RegisterPasswordBox.Text = "";
        RegisterConfirmPasswordBox.Text = "";
        ShowLauncher(session, Logout);
        LauncherHost.IsVisible = true;
        LoginRoot.IsVisible = false;
    }

    public void ShowLauncher(LiteFactorySession? session, Action? logout)
    {
        LauncherHost.Content = new LauncherView(session, logout);
    }

    public void ShowSettings(LiteFactorySession? session, Action? logout)
    {
        LauncherHost.Content = new SettingsView(session, logout);
    }

    private void Logout()
    {
        _session = null;
        _authState = AuthState.LoggedOut;
        LauncherHost.Content = null;
        LauncherHost.IsVisible = false;
        LoginRoot.IsVisible = true;
        LoginPanel.IsVisible = true;
        RegisterPanel.IsVisible = false;
        AuthTitle.Text = "Вход";
        AuthSubtitle.Text = "Войдите в свой аккаунт LiteFactory";
        LoginBox.Text = "";
        PasswordBox.Text = "";
        ShowAuthStatus("Signed out.", true);
        SetAuthBusy(false);
    }

    private void SetAuthBusy(bool busy, string? status = null)
    {
        if (busy)
        {
            _authState = AuthState.SigningIn;
            _authCancellation = new CancellationTokenSource();
        }
        else
        {
            _authState = AuthState.LoggedOut;
            _authCancellation?.Dispose();
            _authCancellation = null;
        }

        LoginButton.IsEnabled = !busy;
        CreateAccountButton.IsEnabled = !busy;
        RegisterButton.IsEnabled = !busy;
        CancelRegistrationButton.IsEnabled = !busy;
        LoginBox.IsEnabled = !busy;
        PasswordBox.IsEnabled = !busy;
        RegisterEmailBox.IsEnabled = !busy;
        RegisterNicknameBox.IsEnabled = !busy;
        RegisterPasswordBox.IsEnabled = !busy;
        RegisterConfirmPasswordBox.IsEnabled = !busy;
        RememberPasswordCheckBox.IsEnabled = !busy;

        if (!string.IsNullOrWhiteSpace(status))
        {
            ShowAuthStatus(status, true);
        }
    }

    private void ShowAuthStatus(string message, bool success)
    {
        AuthStatus.Foreground = success ? Brushes.LightGreen : Brushes.IndianRed;
        AuthStatus.Text = message;
    }

    private void LoadRememberedCredentials()
    {
        try
        {
            var credentials = _credentialStorage.Load();
            if (credentials == null)
            {
                return;
            }

            LoginBox.Text = credentials.Login;
            PasswordBox.Text = credentials.Password;
            RememberPasswordCheckBox.IsChecked = true;
        }
        catch
        {
            try
            {
                _credentialStorage.Delete();
            }
            catch
            {
                // Remembered credentials are optional; login must stay usable.
            }
        }
    }

    private void UpdateRememberedCredentials(string login, string password)
    {
        try
        {
            if (RememberPasswordCheckBox.IsChecked == true)
            {
                _credentialStorage.Save(login, password);
                return;
            }

            _credentialStorage.Delete();
        }
        catch
        {
            // Remembering credentials must not block successful login.
        }
    }

    private enum AuthState
    {
        LoggedOut,
        SigningIn,
        LoggedIn
    }
}
