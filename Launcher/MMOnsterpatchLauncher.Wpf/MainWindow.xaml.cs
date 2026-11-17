using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MMOnsterpatchLauncher.Wpf;

public partial class MainWindow : Window
{
    private LauncherConfig _config = new();
    private bool _isSignedInThisSession;
    private readonly LauncherRuntimeServices _runtime;
    private readonly DispatcherTimer _serverStatusTimer = new();

    public MainWindow()
    {
        InitializeComponent();

        _runtime = new LauncherRuntimeServices(Log);
        _config = LauncherConfigService.Load();
        // Do not trust cached launcher state locally. A saved account/session must validate against
        // the Account Website before the launcher shows the signed-in account menu.
        _isSignedInThisSession = false;

        OnlineRadioButton.IsChecked = !string.Equals(_config.LastPlayMode, "Offline", StringComparison.OrdinalIgnoreCase);
        OfflineRadioButton.IsChecked = string.Equals(_config.LastPlayMode, "Offline", StringComparison.OrdinalIgnoreCase);

        ApplyProfileState();
        ApplyServerStatus(false, 0);

        _serverStatusTimer.Interval = TimeSpan.FromSeconds(10);
        _serverStatusTimer.Tick += async (_, _) => await RefreshServerStatusAsync(showBusy: false);
        _serverStatusTimer.Start();
        _ = RefreshServerStatusAsync(showBusy: false);
        if (_config.RememberMe && _config.IsSignedIn && HasCachedLauncherSession())
        {
            _ = ValidateCachedLauncherLoginAsync(showFailureMessage: false);
        }
        else if (!_config.RememberMe)
        {
            // Non-Remember-Me sessions are only for the current launcher process.
            // On a fresh launcher start, do not silently trust old local session tokens.
            ClearLauncherAccountState(preserveUsernameOrEmail: true);
            ApplyProfileState();
        }

        UpdateStatusText.Text = string.IsNullOrWhiteSpace(_config.GameExePath)
            ? "Select Monsterpatch.exe when you click Play or Open Game Folder."
            : $"Ready. Game: {_config.GameExePath}";
    }

    private void ApplyProfileState()
    {
        bool signedIn = _isSignedInThisSession;
        var statusBrush = signedIn
            ? new SolidColorBrush(Color.FromRgb(0x2D, 0xE3, 0x42))
            : new SolidColorBrush(Color.FromRgb(0x7B, 0x80, 0x88));

        ProfileStatusDot.Fill = statusBrush;
        ProfileFlyoutStatusDot.Fill = statusBrush;

        string display = GetSignedInDisplayName();
        string accountLine = GetSignedInAccountLine();

        ProfileFlyoutNameText.Text = signedIn ? display : "Not signed in";
        ProfileFlyoutAccountText.Text = signedIn ? accountLine : "Log in to continue";
        ViewProfileButton.IsEnabled = signedIn;
        ManageAccountButton.IsEnabled = signedIn;
        LogoutButton.IsEnabled = signedIn;

        ProfileButton.ToolTip = signedIn ? $"Signed in as {display}" : "Profile / Log In";
    }

    private string GetSignedInDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(_config.DisplayName)) return _config.DisplayName;
        if (!string.IsNullOrWhiteSpace(_config.AccountUsername)) return _config.AccountUsername;
        if (!string.IsNullOrWhiteSpace(_config.UsernameOrEmail)) return _config.UsernameOrEmail;
        return "MMOnsterpatch Account";
    }

    private string GetSignedInAccountLine()
    {
        if (!string.IsNullOrWhiteSpace(_config.AccountUsername) && !string.IsNullOrWhiteSpace(_config.AccountEmail))
            return $"{_config.AccountUsername} · {_config.AccountEmail}";
        if (!string.IsNullOrWhiteSpace(_config.AccountUsername)) return _config.AccountUsername;
        if (!string.IsNullOrWhiteSpace(_config.AccountEmail)) return _config.AccountEmail;
        return _config.AccountId;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!EnsureGamePath())
                return;

            bool online = OnlineRadioButton.IsChecked == true;

            if (online && !await EnsureLauncherAccountReadyAsync())
                return;

            _config.LastPlayMode = online ? "Online" : "Offline";
            LauncherConfigService.Save(_config);

            SetBusy(true, online ? "Preparing Online Mode..." : "Starting Offline Mode...");

            if (online)
            {
                Hide();
                await _runtime.LaunchOnlineAsync(_config);
                Show();
                Activate();
                SetStatus("Online session ended. Offline mods restored.", 100);
            }
            else
            {
                await _runtime.LaunchOfflineAsync(_config);
                SetStatus("Offline game launched. Your existing BepInEx/mods were not touched.", 100);
            }
        }
        catch (Exception ex)
        {
            Show();
            Activate();
            SetStatus("Launch failed. See launcher log.", 0);
            ThemedMessageBox.Show(this, ex.Message, "MMOnsterpatch Launcher", ThemedMessageKind.Error);
            Log("ERROR: " + ex);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "Checking production patch manifest...");
            string manifest = await _runtime.CheckManifestAsync(_config);
            SetStatus($"Manifest reachable at {_config.PatchManifestUrl} ({manifest.Length} chars).", 100);
            Log("Patch manifest check OK: " + _config.PatchManifestUrl);
        }
        catch (Exception ex)
        {
            SetStatus("Manifest check failed. Server may not be published yet.", 0);
            Log("Manifest check failed: " + ex.Message);
            ThemedMessageBox.Show(this, $"Could not reach patch manifest yet:\n\n{_config.PatchManifestUrl}\n\n{ex.Message}", "Check for Updates", ThemedMessageKind.Warning);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void DiscordButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://discord.gg/vUQH9R5YQc");
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/Gamingwithgoose/MMOnsterpatch");
    }

    private async void ProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isSignedInThisSession)
        {
            if (!await ValidateCachedLauncherLoginAsync(showFailureMessage: true))
            {
                ShowLoginDialog(showSuccessMessage: true);
                return;
            }

            ApplyProfileState();
            ProfileFlyout.IsOpen = !ProfileFlyout.IsOpen;
            return;
        }

        if (HasCachedLauncherSession() && await ValidateCachedLauncherLoginAsync(showFailureMessage: false))
        {
            ApplyProfileState();
            ProfileFlyout.IsOpen = true;
            return;
        }

        ShowLoginDialog(showSuccessMessage: true);
    }

    private void ViewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        ProfileFlyout.IsOpen = false;
        OpenUrl(BuildLauncherAccountUrl("profile"));
    }

    private void ManageAccountButton_Click(object sender, RoutedEventArgs e)
    {
        ProfileFlyout.IsOpen = false;
        OpenUrl(BuildLauncherAccountUrl("manage-account"));
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        ProfileFlyout.IsOpen = false;
        ClearLauncherAccountState(preserveUsernameOrEmail: false);
        ApplyProfileState();
        ThemedMessageBox.Show(this, "You have been logged out of the launcher.", "MMOnsterpatch Account", ThemedMessageKind.Info);
    }

    private string BuildLauncherAccountUrl(string page)
    {
        string baseUrl = (_config.AccountBaseUrl ?? "https://account.gamingwithgoose.com").TrimEnd('/');
        string token = _config.LauncherSessionToken ?? "";
        if (string.IsNullOrWhiteSpace(token))
            return baseUrl + "/" + page;
        return baseUrl + "/launcher/" + page + "?token=" + Uri.EscapeDataString(token);
    }

    private bool ShowLoginDialog(bool showSuccessMessage = false)
    {
        var loginWindow = new LoginWindow(_config)
        {
            Owner = this
        };

        bool? result = loginWindow.ShowDialog();

        if (result != true || !loginWindow.LoginAccepted)
            return false;

        _isSignedInThisSession = true;

        _config.UsernameOrEmail = loginWindow.UsernameOrEmail;
        _config.RememberMe = loginWindow.RememberMeSelected;
        _config.IsSignedIn = loginWindow.RememberMeSelected;

        if (loginWindow.AccountLoginResult?.Account is not null && loginWindow.AccountLoginResult.Session is not null)
        {
            _config.AccountId = loginWindow.AccountLoginResult.Account.AccountId;
            _config.AccountUsername = loginWindow.AccountLoginResult.Account.Username;
            _config.AccountEmail = loginWindow.AccountLoginResult.Account.Email;
            _config.DisplayName = loginWindow.AccountLoginResult.Account.DisplayName;
            _config.PseudoSteamId = loginWindow.AccountLoginResult.Session.PseudoSteamId;
            _config.LauncherSessionToken = loginWindow.AccountLoginResult.Session.SessionToken;
            _config.AioSessionToken = loginWindow.AccountLoginResult.Session.AioSessionToken;
            _config.OfficialSessionExpiresUtc = loginWindow.AccountLoginResult.Session.ExpiresUtc.ToUniversalTime().ToString("o");
        }

        // Password is never saved. Remember Me persists the account session token until the server expires it.
        LauncherConfigService.Save(_config);

        ApplyProfileState();

        if (showSuccessMessage)
        {
            ThemedMessageBox.Show(
                this,
                loginWindow.RememberMeSelected
                    ? "Signed in. The launcher will remember this account session until it expires."
                    : "Signed in for this launcher session.",
                "MMOnsterpatch Account",
                ThemedMessageKind.Info);
        }

        return true;
    }

    private bool HasCachedLauncherSession()
    {
        return !string.IsNullOrWhiteSpace(_config.LauncherSessionToken)
            && !string.IsNullOrWhiteSpace(_config.AioSessionToken);
    }

    private async Task<bool> EnsureLauncherAccountReadyAsync()
    {
        if (_isSignedInThisSession)
        {
            if (await ValidateCachedLauncherLoginAsync(showFailureMessage: true))
                return true;
        }
        else if (HasCachedLauncherSession())
        {
            if (await ValidateCachedLauncherLoginAsync(showFailureMessage: true))
                return true;
        }

        ThemedMessageBox.Show(
            this,
            "Please sign in to the launcher before starting Online Mode.\n\nOnline Play requires the launcher to verify your MMOnsterpatch account session with the account server. Register on the account website if you do not have one yet.",
            "MMOnsterpatch Online Mode",
            ThemedMessageKind.Info);

        return ShowLoginDialog();
    }

    private async Task<bool> ValidateCachedLauncherLoginAsync(bool showFailureMessage)
    {
        if (!HasCachedLauncherSession())
        {
            _isSignedInThisSession = false;
            ApplyProfileState();
            return false;
        }

        try
        {
            var result = await _runtime.ValidateAccountSessionAsync(_config);
            if (result.Success && result.Account is not null && result.Session is not null)
            {
                _isSignedInThisSession = true;
                _config.IsSignedIn = _config.RememberMe;
                _config.AccountId = result.Account.AccountId;
                _config.AccountUsername = result.Account.Username;
                _config.AccountEmail = result.Account.Email;
                _config.DisplayName = result.Account.DisplayName;
                _config.PseudoSteamId = result.Account.PseudoSteamId;
                _config.LauncherSessionToken = result.Session.SessionToken;
                _config.AioSessionToken = result.Session.AioSessionToken;
                _config.OfficialSessionExpiresUtc = result.Session.ExpiresUtc.ToUniversalTime().ToString("o");
                LauncherConfigService.Save(_config);
                ApplyProfileState();
                Log("Cached launcher account session validated with Account Website: " + GetSignedInDisplayName());
                return true;
            }

            Log("Cached launcher account session rejected by Account Website: " + (result.Message ?? "invalid session"));
            ClearLauncherAccountState(preserveUsernameOrEmail: true);
            ApplyProfileState();
            if (showFailureMessage)
            {
                ThemedMessageBox.Show(
                    this,
                    "Your saved launcher account session could not be verified with the account server. Please log in again.",
                    "MMOnsterpatch Account",
                    ThemedMessageKind.Warning);
            }
            return false;
        }
        catch (Exception ex)
        {
            Log("Cached launcher account validation failed: " + ex.Message);
            ClearLauncherAccountState(preserveUsernameOrEmail: true);
            ApplyProfileState();
            if (showFailureMessage)
            {
                ThemedMessageBox.Show(
                    this,
                    "The launcher could not verify your saved account session with the account server. You are shown offline until you log in again.\n\n" + ex.Message,
                    "MMOnsterpatch Account",
                    ThemedMessageKind.Warning);
            }
            return false;
        }
    }

    private void ClearLauncherAccountState(bool preserveUsernameOrEmail)
    {
        string usernameOrEmail = _config.UsernameOrEmail;
        _isSignedInThisSession = false;
        _config.IsSignedIn = false;
        _config.RememberMe = false;
        _config.AccountId = "";
        _config.AccountUsername = "";
        _config.AccountEmail = "";
        _config.DisplayName = "";
        _config.PseudoSteamId = "";
        _config.LauncherSessionToken = "";
        _config.AioSessionToken = "";
        _config.OfficialSessionExpiresUtc = "";
        if (preserveUsernameOrEmail)
            _config.UsernameOrEmail = usernameOrEmail;
        else
            _config.UsernameOrEmail = "";
        LauncherConfigService.Save(_config);
        TryDeleteLauncherSessionFile();
    }

    private void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        ThemedMessageBox.Show(
            this,
            "MMOnsterpatch UnOfficial Server Launcher\n\n" +
            "Build: v0.13.3 - TradingPostLayoutFix\n\n" +
            "Production DNS defaults:\n" +
            $"Account: {_config.AccountBaseUrl}\n" +
            $"MMO: {_config.MmoHost}\n" +
            $"Trading Post: {_config.TradingPostHost}\n" +
            $"Auth: {_config.AuthBaseUrl}",
            "About",
            ThemedMessageKind.Info);
    }

    private async void ServerStatusButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshServerStatusAsync(showBusy: true);
    }

    private async Task RefreshServerStatusAsync(bool showBusy)
    {
        try
        {
            if (showBusy)
                SetBusy(true, "Checking server status...");

            string? statusJson = await _runtime.CheckStatusAsync(_config);
            bool online = false;
            int playersOnline = 0;

            if (!string.IsNullOrWhiteSpace(statusJson))
            {
                using var doc = JsonDocument.Parse(statusJson);
                var root = doc.RootElement;
                online = root.TryGetProperty("online", out var onlineProp) && onlineProp.ValueKind == JsonValueKind.True;
                if (root.TryGetProperty("playersOnline", out var playersProp) && playersProp.TryGetInt32(out int p))
                    playersOnline = p;
            }

            ApplyServerStatus(online, playersOnline);
            if (showBusy)
                SetStatus(online ? "Server status: online." : "Server status: offline.", online ? 100 : 0);
        }
        catch (Exception ex)
        {
            ApplyServerStatus(false, 0);
            if (showBusy)
                SetStatus("Server status unavailable.", 0);
            Log("Server status check failed: " + ex.Message);
        }
        finally
        {
            if (showBusy)
                SetBusy(false, null);
        }
    }

    private void ApplyServerStatus(bool online, int playersOnline)
    {
        var brush = online
            ? new SolidColorBrush(Color.FromRgb(0x2D, 0xE3, 0x42))
            : new SolidColorBrush(Color.FromRgb(0xE3, 0x3A, 0x3A));

        ServerStatusDot.Fill = brush;
        ServerStatusOnlineText.Foreground = brush;
        ServerStatusOnlineText.Text = online ? "ONLINE" : "OFFLINE";
        PlayersOnlineText.Text = online
            ? $"Players Online: {playersOnline}"
            : "Players Online: 0";
    }

    private void ViewAllNewsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_config.AccountBaseUrl.TrimEnd('/') + "/news");
    }

    private void VisitWebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_config.AccountBaseUrl);
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(LauncherConfigService.ConfigDirectory);
        OpenFolder(LauncherConfigService.ConfigDirectory);
    }

    private void OpenGameFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureGamePath())
            return;

        string? dir = Path.GetDirectoryName(_config.GameExePath);
        if (!string.IsNullOrWhiteSpace(dir))
            OpenFolder(dir);
    }

    private bool EnsureGamePath()
    {
        if (File.Exists(_config.GameExePath) && Path.GetFileName(_config.GameExePath).Equals("Monsterpatch.exe", StringComparison.OrdinalIgnoreCase))
            return true;

        var dialog = new OpenFileDialog
        {
            Title = "Select Monsterpatch.exe",
            Filter = "Monsterpatch.exe|Monsterpatch.exe|Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return false;

        _config.GameExePath = dialog.FileName;
        LauncherConfigService.Save(_config);
        SetStatus($"Game path saved: {_config.GameExePath}", 0);
        return true;
    }

    private void SetBusy(bool busy, string? message)
    {
        PlayButton.IsEnabled = !busy;
        CheckForUpdatesButton.IsEnabled = !busy;
        if (!string.IsNullOrWhiteSpace(message))
            UpdateStatusText.Text = message;
    }

    private void SetStatus(string text, int progress)
    {
        UpdateStatusText.Text = text;
        UpdateProgressBar.Value = progress;
        UpdateProgressText.Text = progress + "%";
    }

    private static void OpenUrl(string url)
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
            ThemedMessageBox.Show(Application.Current?.MainWindow, url, "Unable to open link", ThemedMessageKind.Warning);
        }
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(Application.Current?.MainWindow, ex.Message, "Unable to open folder", ThemedMessageKind.Warning);
        }
    }

    private static void TryDeleteLauncherSessionFile()
    {
        try
        {
            if (File.Exists(LauncherConfigService.SessionPath))
                File.Delete(LauncherConfigService.SessionPath);
        }
        catch
        {
            // Non-fatal. Logging out should not fail because the file was locked.
        }
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(LauncherConfigService.ConfigDirectory);
            File.AppendAllText(LauncherConfigService.StageLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging should never break launcher use.
        }
    }
}
