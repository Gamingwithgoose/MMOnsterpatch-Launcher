using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MMOnsterpatch.RepackManager;

public partial class MainWindow : Window
{
    private readonly string _root;
    private readonly string _configPath;
    private RepackManagerConfig _config = new();

    private ServerProcessController? _auth; // disabled legacy/future placeholder; current auth is inside _world on port 61527.
    private ServerProcessController? _world;
    private ServerProcessController? _website;
    private bool _isShuttingDown;
    private readonly DispatcherTimer _statusTimer = new();

    private ObservableCollection<ManifestFileRow> _manifestRows = new();
    private string? _loadedDecompileZip;
    private string? _loadedAssetsZip;
    private string? _lastCompatibilityReportPath;

    public MainWindow()
    {
        InitializeComponent();

        _root = FindRepackRoot();
        _configPath = Path.Combine(_root, "Config", "repackmanager.json");

        LoadConfig();
        CreateControllers();
        RefreshStatuses();

        ManifestFilesGrid.ItemsSource = _manifestRows;

        Log($"Repack root: {_root}");
        Log("Ready. v0.18.4 prize crystal owner cleanup fix loaded.");
        RefreshOnlineUsersList();

        _statusTimer.Interval = TimeSpan.FromSeconds(5);
        _statusTimer.Tick += (_, _) =>
        {
            RefreshStatuses();
            RefreshOnlineUsersList(logIfChanged: false);
        };
        _statusTimer.Start();
    }

    private string FindRepackRoot()
    {
        // Preferred packaged layout for testing/deployment:
        // Releases\Server\MMOnsterpatchRepackManager.exe
        // Releases\Server\Config
        // Releases\Server\OfficialServer
        // Releases\Server\PatchPayload
        //
        // In that layout, the EXE directory itself is the server root so the
        // whole folder can be copied from the dev PC to the server desktop
        // without relying on parent/sibling folders.
        string exeDir = AppContext.BaseDirectory;
        if (IsServerRoot(exeDir))
            return exeDir;

        // Developer/source fallback: if running from bin/obj during debugging,
        // walk upward until the source Server folder is found.
        var dir = exeDir;
        for (int i = 0; i < 10; i++)
        {
            if (IsServerRoot(dir))
                return dir;

            string siblingServer = Path.Combine(dir, "Server");
            if (IsServerRoot(siblingServer))
                return siblingServer;

            var parent = Directory.GetParent(dir);
            if (parent is null)
                break;

            dir = parent.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    private static bool IsServerRoot(string path)
    {
        return Directory.Exists(Path.Combine(path, "Config")) &&
               Directory.Exists(Path.Combine(path, "PatchPayload")) &&
               Directory.Exists(Path.Combine(path, "OfficialServer"));
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                _config = JsonSerializer.Deserialize<RepackManagerConfig>(File.ReadAllText(_configPath), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new RepackManagerConfig();
            }
        }
        catch (Exception ex)
        {
            Log($"Config load failed: {ex.Message}");
        }

        AutoRestartCheckBox.IsChecked = _config.AutoRestartAll;
        StartMinimizedCheckBox.IsChecked = _config.StartServersMinimized;
    }

    private void CreateControllers()
    {
        // v0.15.5: Do not start inactive placeholder AuthServer; Current Official Server owns live auth/socket flow.
        // Current Steam/OpenID auth is served by the Current Official Server on port 61527.
        _auth = null;
        _world = BuildController(_config.WorldServer);
        _website = BuildController(_config.Website);
    }

    private ServerProcessController BuildController(ServerEntry entry)
    {
        string exe = ResolveExePath(entry.ExePath);
        string wd = Path.GetFullPath(Path.Combine(_root, entry.WorkingDirectory));

        var controller = new ServerProcessController(entry.Name, exe, wd, entry.Arguments);
        controller.Log += Log;
        controller.Exited += OnServerExited;
        return controller;
    }

    private string ResolveExePath(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return exePath;

        bool looksLikePath = exePath.Contains(Path.DirectorySeparatorChar) ||
                             exePath.Contains(Path.AltDirectorySeparatorChar) ||
                             Path.IsPathRooted(exePath);

        return looksLikePath
            ? Path.GetFullPath(Path.Combine(_root, exePath))
            : exePath;
    }

    private void OnServerExited(ServerProcessController controller)
    {
        Dispatcher.Invoke(async () =>
        {
            Log($"{controller.Name} exited.");
            RefreshStatuses();

            if (!_isShuttingDown && AutoRestartCheckBox.IsChecked == true)
            {
                Log($"Auto restart all enabled. Restarting {controller.Name} in 3 seconds...");
                await Task.Delay(3000);
                controller.Start(StartMinimizedCheckBox.IsChecked == true);
                RefreshStatuses();
            }
        });
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isShuttingDown = true;
        _statusTimer.Stop();
        AutoRestartCheckBox.IsChecked = false;
        Log("Server Manager closing. Stopping managed child processes...");
        StopManagedProcesses();
    }

    private void StopManagedProcesses()
    {
        try { _website?.Stop(); } catch (Exception ex) { Log("Website stop during close failed: " + ex.Message); }
        try { _world?.Stop(); } catch (Exception ex) { Log("Official Server stop during close failed: " + ex.Message); }
        try { _auth?.Stop(); } catch (Exception ex) { Log("Auth placeholder stop during close failed: " + ex.Message); }
    }

    private void RefreshOnlineUsers_Click(object sender, RoutedEventArgs e)
    {
        RefreshOnlineUsersList();
    }

    private string RuntimeStatusPath => Path.Combine(_root, "OfficialServer", "data", "runtime_status.json");

    private void RefreshOnlineUsersList(bool logIfChanged = true)
    {
        try
        {
            if (!File.Exists(RuntimeStatusPath))
            {
                OnlineUsersListBox.Items.Clear();
                OnlineUsersCountText.Text = "0 online";
                OnlineUsersListBox.Items.Add("Server status file not available yet.");
                OnlineUsersListBox.Items.Add("Start Current Official Server to populate live users.");
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(RuntimeStatusPath));
            var root = doc.RootElement;
            int count = root.TryGetProperty("playersOnline", out var countProp) && countProp.TryGetInt32(out int c) ? c : 0;
            OnlineUsersCountText.Text = count == 1 ? "1 online" : $"{count} online";

            OnlineUsersListBox.Items.Clear();
            if (root.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Array)
            {
                foreach (var player in players.EnumerateArray())
                {
                    string name = player.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "Unknown" : "Unknown";
                    string scene = player.TryGetProperty("scene", out var sceneProp) ? sceneProp.GetString() ?? "" : "";
                    string cluster = player.TryGetProperty("cluster", out var clusterProp) ? clusterProp.GetString() ?? "" : "";
                    string status = player.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "" : "";
                    string where = string.IsNullOrWhiteSpace(scene) ? cluster : scene;
                    OnlineUsersListBox.Items.Add(string.IsNullOrWhiteSpace(where)
                        ? $"{name} ({status})"
                        : $"{name} - {where} ({status})");
                }
            }

            if (OnlineUsersListBox.Items.Count == 0)
                OnlineUsersListBox.Items.Add("No players online.");
        }
        catch (Exception ex)
        {
            OnlineUsersListBox.Items.Clear();
            OnlineUsersCountText.Text = "0 online";
            OnlineUsersListBox.Items.Add("Could not read runtime_status.json");
            OnlineUsersListBox.Items.Add(ex.Message);
            if (logIfChanged)
                Log("Online users refresh failed: " + ex.Message);
        }
    }

    private void ClearEvents_Click(object sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
    }

    private void SendAdminCommand_Click(object sender, RoutedEventArgs e)
    {
        SendAdminCommandFromTextBox();
    }

    private void AdminCommandTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            SendAdminCommandFromTextBox();
        }
    }

    private void SendAdminCommandFromTextBox()
    {
        string command = AdminCommandTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(command))
            return;

        if (_world?.IsRunning != true)
        {
            Log("Official Server is not running; admin command was not sent.");
            return;
        }

        _world.SendCommand(command);
        AdminCommandTextBox.Clear();
    }

    private void RefreshStatuses()
    {
        bool worldRunning = _world?.IsRunning == true;
        bool websiteRunning = _website?.IsRunning == true;
        bool runtimeFresh = false;
        int playersOnline = 0;

        try
        {
            if (File.Exists(RuntimeStatusPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(RuntimeStatusPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("updatedUnix", out var updatedProp) && updatedProp.TryGetDouble(out double updatedUnix))
                {
                    double nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                    runtimeFresh = nowUnix - updatedUnix <= 15.0;
                }
                if (root.TryGetProperty("playersOnline", out var playersProp) && playersProp.TryGetInt32(out int p))
                    playersOnline = p;
            }
        }
        catch
        {
            runtimeFresh = false;
        }

        // Auth is part of the Current Official Server right now.
        // Fresh runtime_status confirms the Python server is actively ticking.
        bool officialOnline = worldRunning && runtimeFresh;
        SetStatus(AuthStatusText, officialOnline);
        SetStatus(WorldStatusText, officialOnline);
        SetStatus(WebsiteStatusText, websiteRunning);
        OnlineUsersCountText.Text = playersOnline == 1 ? "1 online" : $"{playersOnline} online";

        AuthToggleButton.Content = officialOnline ? "Auth Active" : "Start Server";
        WorldToggleButton.Content = worldRunning ? "Stop Server" : "Start Server";
        WebsiteToggleButton.Content = websiteRunning ? "Stop Website" : "Start Website";
    }

    private static void SetStatus(System.Windows.Controls.TextBlock text, bool running)
    {
        text.Text = running ? "Online" : "Offline";
        text.Foreground = running
            ? new SolidColorBrush(Color.FromRgb(0x2D, 0xE3, 0x42))
            : new SolidColorBrush(Color.FromRgb(0xE3, 0x3A, 0x3A));
    }

    private void Log(string message)
    {
        Dispatcher.Invoke(() =>
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            LogTextBox.AppendText(line + Environment.NewLine);
            LogTextBox.ScrollToEnd();

            try
            {
                Directory.CreateDirectory(Path.Combine(_root, "Logs", "Manager"));
                File.AppendAllText(Path.Combine(_root, "Logs", "Manager", "manager.log"), line + Environment.NewLine);
            }
            catch
            {
                // Avoid logger recursion.
            }
        });
    }

    private string PatchPayloadRoot => Path.GetFullPath(Path.Combine(_root, _config.PatchPayloadRoot));

    private void ToggleAuth_Click(object sender, RoutedEventArgs e)
    {
        // v0.15.5: Steam/OpenID auth is served by the Current Official Server on port 61527.
        // The future AuthServer placeholder is intentionally disabled.
        if (_world?.IsRunning == true)
        {
            MessageBox.Show("Steam/OpenID auth is already active inside the Current Official Server on port 61527.", "Steam Auth");
        }
        else
        {
            Log("Starting Current Official Server because Steam/OpenID auth lives there on port 61527.");
            _world?.Start(StartMinimizedCheckBox.IsChecked == true);
        }

        RefreshStatuses();
    }

    private void RestartAuth_Click(object sender, RoutedEventArgs e)
    {
        Log("Restarting Current Official Server because Steam/OpenID auth lives there on port 61527.");
        _world?.Restart(StartMinimizedCheckBox.IsChecked == true);
        RefreshStatuses();
    }

    private void ToggleWorld_Click(object sender, RoutedEventArgs e)
    {
        if (_world?.IsRunning == true)
            _world.Stop();
        else
            _world?.Start(StartMinimizedCheckBox.IsChecked == true);

        RefreshStatuses();
    }

    private void RestartWorld_Click(object sender, RoutedEventArgs e) { _world?.Restart(StartMinimizedCheckBox.IsChecked == true); RefreshStatuses(); }

    private void ToggleWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (_website?.IsRunning == true)
            _website.Stop();
        else
            _website?.Start(StartMinimizedCheckBox.IsChecked == true);

        RefreshStatuses();
    }

    private void RestartWebsite_Click(object sender, RoutedEventArgs e) { _website?.Restart(StartMinimizedCheckBox.IsChecked == true); RefreshStatuses(); }

    private void OpenWebsite_Click(object sender, RoutedEventArgs e) => OpenUrl(string.IsNullOrWhiteSpace(_config.Website.LocalUrl) ? _config.Website.Url : _config.Website.LocalUrl);
    private void OpenAuthLogs_Click(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(_root, "Logs", "World"));
    private void OpenWorldLogs_Click(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(_root, "Logs", "World"));
    private void OpenWebsiteLogs_Click(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(_root, "Logs", "Website"));

    private void InstallDatabase_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("MariaDB is initialized by the Account Website / Official Server using Server\\Config\\database.ini. Use the migration kit only for first import, then keep MariaDB as source of truth.", "Install Database");
    }

    private void UpdateDatabase_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("MariaDB schema checks run at service startup. Manual SQL updates should be applied with a database backup first.", "Update Database");
    }

    private void BackupDatabase_Click(object sender, RoutedEventArgs e)
    {
        string backupDir = Path.Combine(_root, "Backups", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(backupDir);
        string note = Path.Combine(backupDir, "MARIADB_BACKUP_REQUIRED.txt");
        File.WriteAllText(note,
            "MMOnsterpatch v0.17.1 uses MariaDB as source of truth and launcher-managed online save cache.\r\n" +
            "Use mysqldump on the server MariaDB instance, port 3307, for these databases:\r\n" +
            "- mmonsterpatch_auth\r\n" +
            "- mmonsterpatch_tradingpost\r\n" +
            "- mmonsterpatch_social\r\n" +
            "- mmonsterpatch_files\r\n", Encoding.UTF8);
        Log($"MariaDB backup note created: {note}");
        MessageBox.Show("MariaDB is now the official database. I created a backup note with the required mysqldump database names. SQLite .db backup is intentionally disabled.", "Backup Database");
    }

    private void BuildPatchManifestQuick_Click(object sender, RoutedEventArgs e)
    {
        ScanPayload_Click(sender, e);
        BuildManifest_Click(sender, e);
    }

    private void LoadGameUpdate_Click(object sender, RoutedEventArgs e)
    {
        var decompileDialog = new OpenFileDialog
        {
            Title = "Select Assembly/decompile zip",
            Filter = "Zip files (*.zip)|*.zip|All files (*.*)|*.*"
        };

        if (decompileDialog.ShowDialog(this) != true)
            return;

        var assetsDialog = new OpenFileDialog
        {
            Title = "Select assets zip",
            Filter = "Zip files (*.zip)|*.zip|All files (*.*)|*.*"
        };

        if (assetsDialog.ShowDialog(this) != true)
            return;

        _loadedDecompileZip = decompileDialog.FileName;
        _loadedAssetsZip = assetsDialog.FileName;

        Log($"Loaded game update decompile: {_loadedDecompileZip}");
        Log($"Loaded game update assets: {_loadedAssetsZip}");
    }

    private void AnalyzeGameUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_loadedDecompileZip) || string.IsNullOrWhiteSpace(_loadedAssetsZip))
        {
            MessageBox.Show("Load a decompile zip and assets zip first.", "Analyze Changes");
            return;
        }

        string reportDir = Path.Combine(_root, "Data", "CompatibilityReports");
        Directory.CreateDirectory(reportDir);
        _lastCompatibilityReportPath = Path.Combine(reportDir, $"compatibility_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        string reportText = string.Join(Environment.NewLine, new[]
        {
            "MMOnsterpatch Game Update Compatibility Report Placeholder",
            $"Generated: {DateTime.Now}",
            $"Decompile: {_loadedDecompileZip}",
            $"Assets: {_loadedAssetsZip}",
            "",
            "Future checks to implement:",
            "[TODO] SaveData fields",
            "[TODO] MonScriptableObject fields/catalog",
            "[TODO] EncounterPoolScriptableObject extraction",
            "[TODO] WildMonSpawnManager hook compatibility",
            "[TODO] Battle/catch/save hook signatures",
            "[TODO] Generate Data/Catalog/<game_build>/ artifacts",
            "[TODO] Generate SQL/updates when needed"
        });

        File.WriteAllText(_lastCompatibilityReportPath, reportText);

        Log($"Compatibility report written: {_lastCompatibilityReportPath}");
        OpenFile(_lastCompatibilityReportPath);
    }

    private void ApplyGameUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastCompatibilityReportPath))
        {
            MessageBox.Show("Analyze changes first. Apply Game Update should only run after a compatibility report exists.", "Apply Game Update");
            return;
        }

        MessageBox.Show("Apply placeholder. Later this will import catalogs, create Data/Catalog output, and generate SQL/updates if required.", "Apply Game Update");
        Log("Apply Game Update placeholder acknowledged.");
    }

    private void OpenHeidiSql_Click(object sender, RoutedEventArgs e) => OpenUrl("https://www.heidisql.com/");
    private void OpenConfig_Click(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(_root, "Config"));
    private void OpenPatchPayload_Click(object sender, RoutedEventArgs e) => OpenFolder(PatchPayloadRoot);
    private void OpenRepackRoot_Click(object sender, RoutedEventArgs e) => OpenFolder(_root);

    private void ScanPayload_Click(object sender, RoutedEventArgs e)
    {
        _manifestRows = PatchManifestService.ScanFiles(PatchPayloadRoot);
        ManifestFilesGrid.ItemsSource = _manifestRows;
        Log($"Scanned payload. Files found: {_manifestRows.Count}");
    }

    private void BuildManifest_Click(object sender, RoutedEventArgs e)
    {
        PatchManifestService.BackupManifest(PatchPayloadRoot);
        PatchManifestService.BuildManifest(
            PatchPayloadRoot,
            ChannelTextBox.Text.Trim(),
            PatchVersionTextBox.Text.Trim(),
            RequiredGameBuildTextBox.Text.Trim(),
            MinimumLauncherTextBox.Text.Trim(),
            PatchNotesTextBox.Text.Trim(),
            _manifestRows);

        Log("Manifest built: PatchPayload/manifest.json");
    }

    private void BackupManifest_Click(object sender, RoutedEventArgs e)
    {
        PatchManifestService.BackupManifest(PatchPayloadRoot);
        Log("Current manifest backed up.");
    }

    private void OpenManifest_Click(object sender, RoutedEventArgs e)
    {
        OpenFile(Path.Combine(PatchPayloadRoot, "manifest.json"));
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private static void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show(path, "File not found");
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
}

public sealed class RepackManagerConfig
{
    public bool AutoRestartAll { get; set; }
    public bool StartServersMinimized { get; set; } = true;
    public ServerEntry AuthServer { get; set; } = new() { Name = "Auth Server" };
    public ServerEntry WorldServer { get; set; } = new() { Name = "World Server" };
    public ServerEntry Website { get; set; } = new() { Name = "Account Website" };
    public Dictionary<string, string>? ProductionDns { get; set; }
    public Dictionary<string, int>? CurrentPorts { get; set; }
    public string PatchPayloadRoot { get; set; } = "PatchPayload";
    public string PublishRoot { get; set; } = "C:\\[MMOnsterpatch] UnOfficial Server Repack";
}

public sealed class ServerEntry
{
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string Url { get; set; } = "";
    public string LocalUrl { get; set; } = "";
}
