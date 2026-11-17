using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MMOnsterpatchLauncher.Core;

namespace MMOnsterpatchLauncher.Avalonia;

public partial class MainWindow : Window
{
    private LauncherConfig _config = new();
    private readonly LauncherRuntimeServices _runtime;
    private readonly DispatcherTimer _serverStatusTimer = new();
    private bool _signedInThisSession;
    private readonly StringBuilder _logBuffer = new();

    public MainWindow()
    {
        InitializeComponent();
        _runtime = new LauncherRuntimeServices(Log);
        _config = LauncherConfigService.Load();
        _signedInThisSession = false;

        PlatformText.Text = LauncherPlatform.IsLinux
            ? "Linux / Steam Deck build · payload linux-x64 · executable Monsterpatch.x86_64"
            : "Windows build · payload win-x64 · executable Monsterpatch.exe";

        OnlineRadioButton.IsChecked = !string.Equals(_config.LastPlayMode, "Offline", StringComparison.OrdinalIgnoreCase);
        OfflineRadioButton.IsChecked = string.Equals(_config.LastPlayMode, "Offline", StringComparison.OrdinalIgnoreCase);
        LinuxSteamText.IsVisible = LauncherPlatform.IsLinux;

        RefreshConfigUi();
        ApplyAccountUi();

        _serverStatusTimer.Interval = TimeSpan.FromSeconds(10);
        _serverStatusTimer.Tick += async (_, _) => await RefreshServerStatusAsync(false);
        _serverStatusTimer.Start();

        Opened += async (_, _) =>
        {
            TryAutoDetectIfNeeded();
            await RefreshServerStatusAsync(false);
            if (_config.RememberMe && _config.IsSignedIn && HasCachedLauncherSession())
                await ValidateCachedLauncherLoginAsync(false);
        };
    }

    private void TryAutoDetectIfNeeded()
    {
        if (!_config.AutoDetectGamePath)
            return;

        if (!string.IsNullOrWhiteSpace(_config.GameExePath) && File.Exists(_config.GameExePath))
            return;

        var found = GameInstallDetector.Detect(_config, Log);
        if (found is null)
        {
            SetStatus("Game was not autodetected yet. Use Browse if needed.");
            return;
        }

        _config.GameExePath = found.ExecutablePath;
        _config.GameInstallPath = found.InstallDirectory;
        _config.GamePlatform = LauncherPlatform.IsLinux ? "linux" : "windows";
        LauncherConfigService.Save(_config);
        RefreshConfigUi();
        SetStatus("Autodetected Monsterpatch: " + found.ExecutablePath);
        Log("Autodetected game path via " + found.Source + ": " + found.ExecutablePath);
    }

    private void RefreshConfigUi()
    {
        GamePathText.Text = _config.GameExePath;
        LogoutButton.IsEnabled = _signedInThisSession;
    }

    private void ApplyAccountUi()
    {
        if (_signedInThisSession)
        {
            string display = !string.IsNullOrWhiteSpace(_config.DisplayName)
                ? _config.DisplayName
                : (!string.IsNullOrWhiteSpace(_config.AccountUsername) ? _config.AccountUsername : _config.UsernameOrEmail);
            AccountText.Text = "Signed in as " + display;
            LoginButton.Content = "Account";
            LogoutButton.IsEnabled = true;
        }
        else
        {
            AccountText.Text = "Not signed in";
            LoginButton.Content = "Log In";
            LogoutButton.IsEnabled = false;
        }
    }

    private async void LoginButton_Click(object? sender, RoutedEventArgs e)
    {
        var login = new LoginWindow(_config, _runtime);
        bool? ok = await login.ShowDialog<bool?>(this);
        if (ok == true)
        {
            _config = LauncherConfigService.Load();
            _signedInThisSession = true;
            ApplyAccountUi();
            SetStatus("Signed in.");
        }
    }

    private void LogoutButton_Click(object? sender, RoutedEventArgs e)
    {
        ClearLauncherAccountState(preserveUsernameOrEmail: true);
        LauncherConfigService.Save(_config);
        _signedInThisSession = false;
        ApplyAccountUi();
        SetStatus("Signed out.");
    }

    private void AutoDetectButton_Click(object? sender, RoutedEventArgs e)
    {
        var found = GameInstallDetector.Detect(_config, Log);
        if (found is null)
        {
            SetStatus("Could not autodetect Monsterpatch from Steam libraries.");
            return;
        }

        _config.GameExePath = found.ExecutablePath;
        _config.GameInstallPath = found.InstallDirectory;
        _config.GamePlatform = LauncherPlatform.IsLinux ? "linux" : "windows";
        LauncherConfigService.Save(_config);
        RefreshConfigUi();
        SetStatus("Autodetected Monsterpatch: " + found.ExecutablePath);
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var patterns = LauncherPlatform.IsLinux
            ? new[] { "Monsterpatch.x86_64", "Monsterpatch", "*" }
            : new[] { "Monsterpatch.exe", "*.exe", "*" };

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LauncherPlatform.IsLinux ? "Select Monsterpatch.x86_64" : "Select Monsterpatch.exe",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Monsterpatch executable") { Patterns = patterns }
            }
        });

        if (files.Count == 0)
            return;

        string? path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            LauncherPlatform.ValidateGameExecutable(path);
            _config.GameExePath = path;
            _config.GameInstallPath = Path.GetDirectoryName(path) ?? "";
            _config.GamePlatform = LauncherPlatform.IsLinux ? "linux" : "windows";
            LauncherConfigService.Save(_config);
            RefreshConfigUi();
            SetStatus("Game path saved: " + path);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Invalid game path", ex.Message);
        }
    }

    private async void PlayButton_Click(object? sender, RoutedEventArgs e)
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
            SetBusy(true, online ? "Preparing Online Mode..." : "Starting Vanilla / Offline Mode...");

            if (online)
            {
                await _runtime.LaunchOnlineAsync(_config);
                SetStatus("Online session ended.");
            }
            else
            {
                await _runtime.LaunchOfflineAsync(_config);
                SetStatus("Vanilla / Offline launch requested.");
            }
        }
        catch (Exception ex)
        {
            SetStatus("Launch failed. See launcher log.");
            Log("ERROR: " + ex);
            await ShowMessageAsync("MMOnsterpatch Launcher", ex.Message);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void CopyLogButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(_logBuffer.ToString());
                SetStatus("Launcher log copied to clipboard.");
            }
            else
            {
                SetStatus("Clipboard is unavailable on this desktop session. Open the log file instead.");
            }
        }
        catch (Exception ex)
        {
            Log("Copy log failed: " + ex.Message);
            SetStatus("Copy log failed. Open the log file instead.");
        }
    }

    private void OpenLogButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(LauncherConfigService.ConfigDirectory);
            if (!File.Exists(LauncherConfigService.StageLogPath))
                File.WriteAllText(LauncherConfigService.StageLogPath, _logBuffer.ToString());

            if (LauncherPlatform.IsWindows)
                Process.Start(new ProcessStartInfo("notepad.exe", LauncherConfigService.StageLogPath) { UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo("xdg-open", LauncherConfigService.StageLogPath) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log("Open log failed: " + ex.Message);
            SetStatus("Open log failed. Log path: " + LauncherConfigService.StageLogPath);
        }
    }

    private async void CheckManifestButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "Checking patch manifest...");
            string manifest = await _runtime.CheckManifestAsync(_config);
            SetStatus($"Manifest reachable ({manifest.Length} chars). URL: {_config.PatchManifestUrl}");
        }
        catch (Exception ex)
        {
            SetStatus("Manifest check failed.");
            Log("Manifest check failed: " + ex.Message);
            await ShowMessageAsync("Check manifest", ex.Message);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void RestoreSteamButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            SteamLaunchOptionsManager.RestorePreviousLaunchOptions(_config, Log);
            LauncherConfigService.Save(_config);
            SetStatus("Steam launch options restore attempted. Check log for details.");
        }
        catch (Exception ex)
        {
            Log("Steam restore error: " + ex);
            SetStatus("Steam restore failed. See log.");
        }
    }

    private void OpenGameFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!EnsureGamePath())
            return;

        string? dir = Path.GetDirectoryName(_config.GameExePath);
        if (!string.IsNullOrWhiteSpace(dir))
            OpenFolder(dir);
    }

    private bool EnsureGamePath()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_config.GameExePath) && File.Exists(_config.GameExePath))
            {
                LauncherPlatform.ValidateGameExecutable(_config.GameExePath);
                return true;
            }
        }
        catch (Exception ex)
        {
            Log("Saved path invalid: " + ex.Message);
        }

        var found = GameInstallDetector.Detect(_config, Log);
        if (found is null)
        {
            SetStatus("Game path is not set. Click Auto Detect or Browse.");
            return false;
        }

        _config.GameExePath = found.ExecutablePath;
        _config.GameInstallPath = found.InstallDirectory;
        _config.GamePlatform = LauncherPlatform.IsLinux ? "linux" : "windows";
        LauncherConfigService.Save(_config);
        RefreshConfigUi();
        return true;
    }

    private async Task<bool> EnsureLauncherAccountReadyAsync()
    {
        if (_signedInThisSession)
            return true;

        if (HasCachedLauncherSession() && await ValidateCachedLauncherLoginAsync(false))
            return true;

        var login = new LoginWindow(_config, _runtime);
        bool? ok = await login.ShowDialog<bool?>(this);
        if (ok == true)
        {
            _config = LauncherConfigService.Load();
            _signedInThisSession = true;
            ApplyAccountUi();
            return true;
        }

        return false;
    }

    private bool HasCachedLauncherSession()
        => !string.IsNullOrWhiteSpace(_config.LauncherSessionToken)
           && !string.IsNullOrWhiteSpace(_config.AioSessionToken);

    private async Task<bool> ValidateCachedLauncherLoginAsync(bool showFailureMessage)
    {
        try
        {
            var result = await _runtime.ValidateAccountSessionAsync(_config);
            if (!result.Success || result.Account is null || result.Session is null)
            {
                ClearLauncherAccountState(preserveUsernameOrEmail: true);
                LauncherConfigService.Save(_config);
                _signedInThisSession = false;
                ApplyAccountUi();
                if (showFailureMessage)
                    await ShowMessageAsync("Login expired", result.Message);
                return false;
            }

            ApplyLoginResult(result);
            _signedInThisSession = true;
            LauncherConfigService.Save(_config);
            ApplyAccountUi();
            return true;
        }
        catch (Exception ex)
        {
            Log("Cached login validation failed: " + ex.Message);
            if (showFailureMessage)
                await ShowMessageAsync("Login validation failed", ex.Message);
            return false;
        }
    }

    private void ApplyLoginResult(LauncherAccountLoginResult result)
    {
        if (result.Account is not null)
        {
            _config.AccountId = result.Account.AccountId;
            _config.AccountEmail = result.Account.Email;
            _config.AccountUsername = result.Account.Username;
            _config.DisplayName = result.Account.DisplayName;
            _config.PseudoSteamId = result.Account.PseudoSteamId;
        }

        if (result.Session is not null)
        {
            _config.LauncherSessionToken = result.Session.SessionToken;
            _config.AioSessionToken = result.Session.AioSessionToken;
            _config.OfficialSessionExpiresUtc = result.Session.ExpiresUtc.ToString("O");
            if (!string.IsNullOrWhiteSpace(result.Session.AccountId)) _config.AccountId = result.Session.AccountId;
            if (!string.IsNullOrWhiteSpace(result.Session.Username)) _config.AccountUsername = result.Session.Username;
            if (!string.IsNullOrWhiteSpace(result.Session.DisplayName)) _config.DisplayName = result.Session.DisplayName;
            if (!string.IsNullOrWhiteSpace(result.Session.PseudoSteamId)) _config.PseudoSteamId = result.Session.PseudoSteamId;
        }
    }

    private void ClearLauncherAccountState(bool preserveUsernameOrEmail)
    {
        string savedUser = preserveUsernameOrEmail ? _config.UsernameOrEmail : "";
        _config.IsSignedIn = false;
        _config.UsernameOrEmail = savedUser;
        _config.AccountId = "";
        _config.AccountUsername = "";
        _config.AccountEmail = "";
        _config.DisplayName = "";
        _config.LauncherSessionToken = "";
        _config.AioSessionToken = "";
        _config.OfficialSessionExpiresUtc = "";
        _config.PseudoSteamId = "";
    }

    private async Task RefreshServerStatusAsync(bool showBusy)
    {
        try
        {
            if (showBusy) SetBusy(true, "Checking server status...");
            string? json = await _runtime.CheckStatusAsync(_config);
            if (string.IsNullOrWhiteSpace(json))
            {
                ServerStatusText.Text = "Server status: unknown";
                return;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            bool online = root.TryGetProperty("online", out var onlineProp) && onlineProp.ValueKind == JsonValueKind.True;
            int players = root.TryGetProperty("playersOnline", out var playersProp) && playersProp.TryGetInt32(out int p) ? p : 0;
            ServerStatusText.Text = online ? $"Server status: ONLINE · Players online: {players}" : "Server status: OFFLINE";
        }
        catch (Exception ex)
        {
            ServerStatusText.Text = "Server status: unavailable";
            Log("Status check failed: " + ex.Message);
        }
        finally
        {
            if (showBusy) SetBusy(false, null);
        }
    }

    private void SetBusy(bool busy, string? message)
    {
        PlayButton.IsEnabled = !busy;
        BusyProgress.IsVisible = busy;
        if (!string.IsNullOrWhiteSpace(message))
            SetStatus(message);
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        Log(message);
    }

    private void Log(string message)
    {
        string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
        _logBuffer.AppendLine(line);
        if (_logBuffer.Length > 20000)
            _logBuffer.Remove(0, _logBuffer.Length - 20000);
        Dispatcher.UIThread.Post(() => LogText.Text = _logBuffer.ToString());

        try
        {
            Directory.CreateDirectory(LauncherConfigService.ConfigDirectory);
            File.AppendAllText(LauncherConfigService.StageLogPath, line + Environment.NewLine);
        }
        catch { }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            if (LauncherPlatform.IsWindows)
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo("xdg-open", path) { UseShellExecute = false });
        }
        catch { }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new SimpleMessageWindow(title, message);
        await dialog.ShowDialog(this);
    }
}
