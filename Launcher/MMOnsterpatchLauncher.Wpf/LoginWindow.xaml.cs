using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MMOnsterpatchLauncher.Wpf;

public partial class LoginWindow : Window
{
    private readonly LauncherConfig _initialConfig;

    public bool LoginAccepted { get; private set; }
    public bool RememberMeSelected { get; private set; }
    public string UsernameOrEmail { get; private set; } = "";
    public LauncherAccountLoginResult? AccountLoginResult { get; private set; }

    public LoginWindow(LauncherConfig config)
    {
        InitializeComponent();

        _initialConfig = config;
        UsernameEmailTextBox.Text = config.UsernameOrEmail;
        RememberMeCheckBox.IsChecked = config.RememberMe;
        LoginStatusDot.Fill = config.IsSignedIn
            ? new SolidColorBrush(Color.FromRgb(0x2D, 0xE3, 0x42))
            : new SolidColorBrush(Color.FromRgb(0x7B, 0x80, 0x88));
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseLoginButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ForgotPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl((_initialConfig.AccountBaseUrl ?? "https://account.gamingwithgoose.com").TrimEnd('/') + "/forgot-password");
    }


    private void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl((_initialConfig.AccountBaseUrl ?? "https://account.gamingwithgoose.com").TrimEnd('/') + "/register");
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            ThemedMessageBox.Show(this, url, "Unable to open link", ThemedMessageKind.Warning);
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        UsernameOrEmail = UsernameEmailTextBox.Text.Trim();
        RememberMeSelected = RememberMeCheckBox.IsChecked == true;
        string password = PasswordInputBox.Password ?? "";

        if (string.IsNullOrWhiteSpace(UsernameOrEmail))
        {
            LoginMessageText.Text = "Enter a username or email address first.";
            LoginMessageText.Foreground = new SolidColorBrush(Color.FromRgb(0xDE, 0xC5, 0xFF));
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            LoginMessageText.Text = "Enter your password.";
            LoginMessageText.Foreground = new SolidColorBrush(Color.FromRgb(0xDE, 0xC5, 0xFF));
            return;
        }

        try
        {
            LoginButton.IsEnabled = false;
            LoginMessageText.Text = "Logging in...";
            LoginMessageText.Foreground = new SolidColorBrush(Color.FromRgb(0xDE, 0xC5, 0xFF));

            var runtime = new LauncherRuntimeServices(_ => { });
            AccountLoginResult = await runtime.LoginAsync(_initialConfig, UsernameOrEmail, password);
            if (AccountLoginResult == null || !AccountLoginResult.Success || AccountLoginResult.Account == null || AccountLoginResult.Session == null)
            {
                LoginMessageText.Text = AccountLoginResult?.Message ?? "Login failed.";
                LoginMessageText.Foreground = new SolidColorBrush(Color.FromRgb(0xE3, 0x3A, 0x3A));
                return;
            }

            UsernameOrEmail = AccountLoginResult.Account.Username;
            LoginAccepted = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            LoginMessageText.Text = "Login failed: " + ex.Message;
            LoginMessageText.Foreground = new SolidColorBrush(Color.FromRgb(0xE3, 0x3A, 0x3A));
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }
}
