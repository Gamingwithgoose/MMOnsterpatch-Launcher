using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MMOnsterpatchLauncher.Core;

namespace MMOnsterpatchLauncher.Avalonia;

public partial class LoginWindow : Window
{
    private readonly LauncherConfig _config;
    private readonly LauncherRuntimeServices _runtime;

    public LoginWindow()
        : this(new LauncherConfig(), new LauncherRuntimeServices(_ => { }))
    {
    }

    public LoginWindow(LauncherConfig config, LauncherRuntimeServices runtime)
    {
        InitializeComponent();
        _config = config;
        _runtime = runtime;
        UsernameTextBox.Text = _config.UsernameOrEmail;
        RememberCheckBox.IsChecked = _config.RememberMe;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void LoginButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string username = UsernameTextBox.Text?.Trim() ?? "";
            string password = PasswordTextBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                StatusText.Text = "Enter username/email and password.";
                return;
            }

            LoginButton.IsEnabled = false;
            StatusText.Text = "Signing in...";
            var result = await _runtime.LoginAsync(_config, username, password);
            if (!result.Success || result.Account is null || result.Session is null)
            {
                StatusText.Text = string.IsNullOrWhiteSpace(result.Message) ? "Login failed." : result.Message;
                return;
            }

            _config.UsernameOrEmail = username;
            _config.RememberMe = RememberCheckBox.IsChecked == true;
            _config.IsSignedIn = true;
            _config.AccountId = result.Account.AccountId;
            _config.AccountEmail = result.Account.Email;
            _config.AccountUsername = result.Account.Username;
            _config.DisplayName = result.Account.DisplayName;
            _config.PseudoSteamId = result.Account.PseudoSteamId;
            _config.LauncherSessionToken = result.Session.SessionToken;
            _config.AioSessionToken = result.Session.AioSessionToken;
            _config.OfficialSessionExpiresUtc = result.Session.ExpiresUtc.ToString("O");
            LauncherConfigService.Save(_config);
            Close(true);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }
}
