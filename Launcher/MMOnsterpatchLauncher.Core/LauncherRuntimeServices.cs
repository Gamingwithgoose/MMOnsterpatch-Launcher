using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace MMOnsterpatchLauncher.Core;

public sealed class LauncherRuntimeServices
{
    private readonly Action<string> _log;

    public LauncherRuntimeServices(Action<string> log)
    {
        _log = log;
    }

    public async Task<LauncherAccountLoginResult> LoginAsync(LauncherConfig config, string usernameOrEmail, string password)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        var payload = JsonSerializer.Serialize(new { usernameOrEmail, password });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(config.AccountBaseUrl.TrimEnd('/') + "/api/account/login", content);
        string json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LauncherAccountLoginResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new LauncherAccountLoginResult { Success = false, Message = "Login response was empty." };
        if (!response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(result.Message))
            result.Message = "Login failed: HTTP " + (int)response.StatusCode;
        return result;
    }

    public async Task<LauncherAccountLoginResult> ValidateAccountSessionAsync(LauncherConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.LauncherSessionToken))
            return new LauncherAccountLoginResult { Success = false, Message = "No saved launcher session token." };

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var req = new HttpRequestMessage(HttpMethod.Get, config.AccountBaseUrl.TrimEnd('/') + "/api/account/me");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.LauncherSessionToken);

        using var response = await http.SendAsync(req);
        string json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return new LauncherAccountLoginResult
            {
                Success = false,
                Message = "Account session validation failed: HTTP " + (int)response.StatusCode
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            bool online = root.TryGetProperty("online", out var onlineProp) && onlineProp.ValueKind == JsonValueKind.True;
            var account = root.TryGetProperty("account", out var accountProp)
                ? accountProp.Deserialize<LauncherAccountInfo>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null;
            var session = root.TryGetProperty("session", out var sessionProp)
                ? sessionProp.Deserialize<LauncherAccountSessionInfo>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null;

            return new LauncherAccountLoginResult
            {
                Success = online && account is not null && session is not null,
                Message = online ? "Account session validated." : "Account session is offline or invalid.",
                Account = account,
                Session = session
            };
        }
        catch (Exception ex)
        {
            return new LauncherAccountLoginResult
            {
                Success = false,
                Message = "Account session validation response could not be parsed: " + ex.Message
            };
        }
    }

    public async Task<string?> CheckStatusAsync(LauncherConfig config)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        string json = await http.GetStringAsync(config.LauncherStatusUrl);
        return json;
    }

    public async Task<string> CheckManifestAsync(LauncherConfig config)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        string json = await http.GetStringAsync(config.PatchManifestUrl);
        return json;
    }

    public async Task LaunchOfflineAsync(LauncherConfig config)
    {
        ValidateGameExe(config.GameExePath);
        TryDeleteFile(LauncherConfigService.SessionPath);

        string gameDir = Path.GetDirectoryName(config.GameExePath)
            ?? throw new InvalidOperationException("Could not resolve Monsterpatch game folder.");

        _log("Offline / vanilla launch selected.");

        if (LauncherPlatform.IsLinux && config.LinuxRestoreRuntimeOnOfflineLaunch)
        {
            _log("Linux offline launch: restoring any launcher-staged online runtime before starting vanilla/offline.");
            var stager = new OnlineRuntimeStager(_log);
            await stager.RestoreOnlineRuntimeAsync(gameDir);
        }

        if (LauncherPlatform.IsLinux && config.LinuxUseSteamLaunch)
        {
            if (config.LinuxManageSteamLaunchOptions)
            {
                SteamLaunchOptionsManager.RestorePreviousLaunchOptions(config, _log);
                LauncherConfigService.Save(config);
            }

            StartSteamGame(config);
            return;
        }

        _log("Leaving user's BepInEx/mods untouched.");
        StartGame(config.GameExePath, null);
        await Task.CompletedTask;
    }

    public async Task LaunchOnlineAsync(LauncherConfig config)
    {
        ValidateGameExe(config.GameExePath);

        string gameDir = Path.GetDirectoryName(config.GameExePath)
            ?? throw new InvalidOperationException("Could not resolve Monsterpatch game folder.");

        if (string.IsNullOrWhiteSpace(config.AioSessionToken))
            throw new InvalidOperationException("Online Mode requires a valid launcher account session. Please log in again.");

        await RequestGameTicketAsync(config);

        _log($"Online launch selected. Extracting {LauncherPlatform.PayloadRid} server payload...");
        string payloadRoot = await EmbeddedPayloadExtractor.ExtractToCacheAsync(_log);

        var stager = new OnlineRuntimeStager(_log);
        await stager.StageOnlineRuntimeAsync(gameDir, payloadRoot);

        PrepareOfficialSessionConfig(config, gameDir);

        string onlineSaveCacheDir = GetOnlineSaveCacheAccountDirectory(gameDir, config);
        await UploadPersistentDirtyOnlineSaveOutboxAsync(config);
        await PrefetchOnlineSaveCacheAsync(config, onlineSaveCacheDir);

        WriteSession(config, stagedRuntime: true);

        var environment = new Dictionary<string, string>
        {
            ["MMONSTERPATCH_LAUNCHER"] = "1",
            ["MMONSTERPATCH_LAUNCHER_SESSION"] = LauncherConfigService.SessionPath,
            ["MMONSTERPATCH_ONLINE_RUNTIME"] = "1",
            ["MMONSTERPATCH_ACCOUNT_BASE_URL"] = config.AccountBaseUrl,
            ["MMONSTERPATCH_PATCH_MANIFEST_URL"] = config.PatchManifestUrl,
            ["MMONSTERPATCH_STATUS_URL"] = config.LauncherStatusUrl,
            ["MMONSTERPATCH_MMO_HOST"] = config.MmoHost,
            ["MMONSTERPATCH_TRADING_POST_HOST"] = config.TradingPostHost,
            ["MMONSTERPATCH_AUTH_BASE_URL"] = config.AuthBaseUrl,
            ["MMONSTERPATCH_ACCOUNT_ID"] = config.AccountId ?? "",
            ["MMONSTERPATCH_ACCOUNT_SESSION"] = config.LauncherSessionToken ?? "",
            ["MMONSTERPATCH_AIO_SESSION"] = config.AioSessionToken ?? "",
            ["MMONSTERPATCH_ONLINE_SAVE_CACHE"] = onlineSaveCacheDir,
        };

        if (LauncherPlatform.IsLinux && config.LinuxUseSteamLaunch)
        {
            if (config.LinuxManageSteamLaunchOptions)
            {
                SteamLaunchOptionsManager.EnableOnlineLaunchOptions(config, _log);
                LauncherConfigService.Save(config);
            }

            _log("Starting Monsterpatch through Steam for Linux/Steam Deck online mode...");
            StartSteamGame(config);
            _log("Steam launch requested. Online runtime is intentionally left staged until Offline/Vanilla restore is used.");
            return;
        }

        if (LauncherPlatform.IsLinux)
            _log("Linux direct online launch selected: staged runtime is already in the game folder; launching run_bepinex.sh now, like the Windows launcher flow.");

        _log("Starting Monsterpatch in Online Mode...");
        using Process process = StartGame(config.GameExePath, environment);
        await process.WaitForExitAsync();
        _log($"Monsterpatch exited with code {process.ExitCode}.");

        await UploadDirtyOnlineSaveCacheAsync(config, onlineSaveCacheDir, preserveFailedToOutbox: true);

        if (config.RestoreOnlineRuntimeOnExit)
        {
            _log("Restoring user's offline runtime/mod folder...");
            await stager.RestoreOnlineRuntimeAsync(gameDir);
        }
        else
        {
            _log("RestoreOnExit disabled in launcher config. Staged online runtime left in place.");
        }

        TryDeleteFile(LauncherConfigService.SessionPath);
    }


    private static string GetOnlineSaveCacheAccountDirectory(string gameDir, LauncherConfig config)
    {
        string account = SanitizePathPart(!string.IsNullOrWhiteSpace(config.AccountId) ? config.AccountId : (!string.IsNullOrWhiteSpace(config.AccountUsername) ? config.AccountUsername : "unknown"));
        return Path.Combine(gameDir, "BepInEx", "MMOnsterpatch", "OnlineCache", "account_" + account);
    }

    private static string GetPersistentOnlineSaveOutboxRoot()
    {
        return Path.Combine(LauncherConfigService.ConfigDirectory, "OnlineSaveCacheOutbox");
    }

    private static string SanitizePathPart(string value)
    {
        var sb = new StringBuilder();
        foreach (char ch in value ?? string.Empty)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.') sb.Append(ch);
            else sb.Append('_');
        }
        string s = sb.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(s) ? "unknown" : s;
    }

    private static string PreferredOnlineToken(LauncherConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.AccountId)) return config.AccountId.Trim();
        if (!string.IsNullOrWhiteSpace(config.AioSessionToken)) return config.AioSessionToken.Trim();
        if (!string.IsNullOrWhiteSpace(config.LauncherSessionToken)) return config.LauncherSessionToken.Trim();
        return string.Empty;
    }

    private static string B64(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private static string FromB64(string value)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty)); }
        catch { return value ?? string.Empty; }
    }

    private async Task PrefetchOnlineSaveCacheAsync(LauncherConfig config, string cacheDir)
    {
        await Task.Run(() =>
        {
            try
            {
            string token = PreferredOnlineToken(config);
            if (string.IsNullOrWhiteSpace(token))
            {
                _log("Online save cache prefetch skipped: no account/session token.");
                return;
            }

            Directory.CreateDirectory(cacheDir);
            foreach (string old in Directory.EnumerateFiles(cacheDir, "slot_*.json", SearchOption.TopDirectoryOnly))
            {
                string dirty = Path.ChangeExtension(old, ".dirty");
                if (!File.Exists(dirty)) TryDeleteFile(old);
            }

            List<string> lines = SendOfficialServerRequestLines(config.MmoHost, 61529, 12000, "OFFICIAL_SAVE_SLOTS2_REQ|" + B64(token), "OFFICIAL_SAVE_SLOTS_DONE", "OFFICIAL_SAVE_ERROR");
            if (lines.Count == 0 || lines.Any(l => l.StartsWith("OFFICIAL_SAVE_ERROR|", StringComparison.OrdinalIgnoreCase)))
            {
                _log("Online save cache prefetch failed or returned an error. The client will fall back to server load if cache is missing.");
                return;
            }

            int occupied = 0;
            var manifest = new List<string>
            {
                "MMON_SAVE_CACHE_V1",
                "ACCOUNT|" + (config.AccountId ?? ""),
                "FETCHED_UTC|" + DateTime.UtcNow.ToString("o")
            };

            foreach (string line in lines)
            {
                if (!line.StartsWith("OFFICIAL_SAVE_SLOT|", StringComparison.OrdinalIgnoreCase))
                    continue;
                manifest.Add(line);
                string[] parts = line.Split('|');
                if (parts.Length < 6) continue;
                if (!int.TryParse(parts[1], out int slot)) continue;
                slot = Math.Max(0, Math.Min(5, slot));
                int occ = 0; int.TryParse(parts[2], out occ);
                if (occ != 0)
                {
                    string json = FromB64(parts[5]);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        File.WriteAllText(Path.Combine(cacheDir, "slot_" + slot + ".json"), json, Encoding.UTF8);
                        occupied++;
                    }
                }
            }

            File.WriteAllLines(Path.Combine(cacheDir, "manifest.txt"), manifest, Encoding.UTF8);
            _log("Prefetched online save cache for " + occupied + " occupied slot(s): " + cacheDir);
            }
            catch (Exception ex)
            {
                _log("Online save cache prefetch failed: " + ex.Message + ". The client will fall back if no cache is available.");
            }
        });
    }

    private async Task UploadPersistentDirtyOnlineSaveOutboxAsync(LauncherConfig config)
    {
        string outboxRoot = GetPersistentOnlineSaveOutboxRoot();
        if (!Directory.Exists(outboxRoot)) return;
        foreach (string accountDir in Directory.EnumerateDirectories(outboxRoot, "account_*", SearchOption.TopDirectoryOnly).ToList())
        {
            await UploadDirtyOnlineSaveCacheAsync(config, accountDir, preserveFailedToOutbox: false);
            try
            {
                if (!Directory.EnumerateFiles(accountDir, "*.dirty", SearchOption.TopDirectoryOnly).Any())
                    Directory.Delete(accountDir, recursive: true);
            }
            catch { }
        }
    }

    private async Task UploadDirtyOnlineSaveCacheAsync(LauncherConfig config, string cacheDir, bool preserveFailedToOutbox)
    {
        await Task.Run(() =>
        {
            try
            {
            if (string.IsNullOrWhiteSpace(cacheDir) || !Directory.Exists(cacheDir)) return;
            string token = PreferredOnlineToken(config);
            if (string.IsNullOrWhiteSpace(token))
            {
                _log("Dirty online save cache upload skipped: no account/session token.");
                if (preserveFailedToOutbox) PreserveDirtyOnlineSaveCache(cacheDir);
                return;
            }

            int uploaded = 0;
            int failed = 0;
            foreach (string dirtyPath in Directory.EnumerateFiles(cacheDir, "slot_*.dirty", SearchOption.TopDirectoryOnly).ToList())
            {
                string name = Path.GetFileNameWithoutExtension(dirtyPath); // slot_N
                if (!name.StartsWith("slot_", StringComparison.OrdinalIgnoreCase) || !int.TryParse(name.Substring(5), out int slot))
                    continue;
                string jsonPath = Path.Combine(cacheDir, "slot_" + slot + ".json");
                if (!File.Exists(jsonPath))
                {
                    TryDeleteFile(dirtyPath);
                    continue;
                }
                string json = File.ReadAllText(jsonPath, Encoding.UTF8);
                string response = SendOfficialServerRequestLine(config.MmoHost, 61529, 15000, "OFFICIAL_SAVE_WRITE|" + B64(token) + "|" + Math.Max(0, Math.Min(5, slot)) + "|" + B64(json), "OFFICIAL_SAVE_WRITE_OK", "OFFICIAL_SAVE_ERROR");
                if (!string.IsNullOrEmpty(response) && response.StartsWith("OFFICIAL_SAVE_WRITE_OK|", StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(dirtyPath);
                    uploaded++;
                }
                else
                {
                    failed++;
                    _log("Dirty online save slot " + slot + " upload failed during launcher cleanup: " + (response ?? "no response"));
                }
            }

            if (uploaded > 0 || failed > 0)
                _log("Launcher online save cleanup upload complete. uploaded=" + uploaded + ", failed=" + failed + ".");
            if (failed > 0 && preserveFailedToOutbox)
                PreserveDirtyOnlineSaveCache(cacheDir);
            }
            catch (Exception ex)
            {
                _log("Dirty online save cache upload failed: " + ex.Message);
                if (preserveFailedToOutbox) PreserveDirtyOnlineSaveCache(cacheDir);
            }
        });
    }

    private void PreserveDirtyOnlineSaveCache(string cacheDir)
    {
        try
        {
            if (!Directory.Exists(cacheDir)) return;
            if (!Directory.EnumerateFiles(cacheDir, "*.dirty", SearchOption.TopDirectoryOnly).Any()) return;
            string target = Path.Combine(GetPersistentOnlineSaveOutboxRoot(), Path.GetFileName(cacheDir));
            Directory.CreateDirectory(target);
            CopyDirectoryForLauncher(cacheDir, target, overwrite: true);
            _log("Preserved failed dirty online save cache for next launcher upload attempt: " + target);
        }
        catch (Exception ex)
        {
            _log("Could not preserve dirty online save cache: " + ex.Message);
        }
    }

    private static void CopyDirectoryForLauncher(string sourceDir, string targetDir, bool overwrite)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string directory in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(targetDir, relative));
        }
        foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            string target = Path.Combine(targetDir, relative);
            string? parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.Copy(file, target, overwrite);
        }
    }

    private static List<string> SendOfficialServerRequestLines(string host, int port, int timeoutMs, string request, string donePrefix, string errorPrefix)
    {
        var lines = new List<string>();
        using var tcp = new TcpClient();
        tcp.ReceiveTimeout = timeoutMs;
        tcp.SendTimeout = timeoutMs;
        IAsyncResult ar = tcp.BeginConnect(host, port, null, null);
        if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
            throw new TimeoutException("Timed out connecting to " + host + ":" + port);
        tcp.EndConnect(ar);
        using NetworkStream ns = tcp.GetStream();
        using var reader = new StreamReader(ns, Encoding.UTF8);
        using var writer = new StreamWriter(ns, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        try { reader.ReadLine(); } catch { }
        writer.WriteLine(request);
        DateTime until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            string? line = reader.ReadLine();
            if (line == null) break;
            lines.Add(line);
            if ((!string.IsNullOrEmpty(donePrefix) && line.StartsWith(donePrefix, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(errorPrefix) && line.StartsWith(errorPrefix, StringComparison.OrdinalIgnoreCase)))
                break;
        }
        return lines;
    }

    private static string SendOfficialServerRequestLine(string host, int port, int timeoutMs, string request, string okPrefix, string errorPrefix)
    {
        var lines = SendOfficialServerRequestLines(host, port, timeoutMs, request, okPrefix, errorPrefix);
        return lines.FirstOrDefault(l => (!string.IsNullOrEmpty(okPrefix) && l.StartsWith(okPrefix, StringComparison.OrdinalIgnoreCase)) ||
                                         (!string.IsNullOrEmpty(errorPrefix) && l.StartsWith(errorPrefix, StringComparison.OrdinalIgnoreCase))) ?? string.Empty;
    }

    private async Task RequestGameTicketAsync(LauncherConfig config)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        using var req = new HttpRequestMessage(HttpMethod.Post, config.AccountBaseUrl.TrimEnd('/') + "/api/account/game-ticket");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.LauncherSessionToken ?? "");
        using var response = await http.SendAsync(req);
        string json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Could not issue game ticket/account session for Online Play. HTTP " + (int)response.StatusCode + ": " + json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("gameTicket", out var ticket))
            {
                if (ticket.TryGetProperty("aioSessionToken", out var aio))
                {
                    string? value = aio.GetString();
                    if (!string.IsNullOrEmpty(value)) config.AioSessionToken = value;
                }
                if (ticket.TryGetProperty("ticketToken", out var tt))
                {
                    string? value = tt.GetString();
                    if (!string.IsNullOrEmpty(value)) config.AioSessionToken = value;
                }
                if (ticket.TryGetProperty("sessionToken", out var st))
                {
                    string? value = st.GetString();
                    if (!string.IsNullOrEmpty(value)) config.LauncherSessionToken = value;
                }
                if (ticket.TryGetProperty("expiresUtc", out var ex))
                {
                    string? value = ex.GetString();
                    if (!string.IsNullOrEmpty(value)) config.OfficialSessionExpiresUtc = value;
                }
            }
            _log("Account website issued an Online Play game ticket/session.");
        }
        catch
        {
            _log("Game ticket response received but could not be parsed. Continuing with existing launcher session token.");
        }
    }

    private const string OfficialTradingPostLayoutDefaults = @"[Server]
Host = mon.gamingwithgoose.com
Port = 61526

[Steam OpenID]
AutoOpenBrowser = true

[Official Server Auth]
# Base64-obfuscated AIO session token cache. Server still validates token, expiry, ban state, and source IP.
CachedSessionToken = 
CachedSessionExpiresUtc = 
CachedSteamID64 = 
CachedAccountUUID = 

[UI]
WindowScale = 1.0
ShowBoxOverlayButton = false
BoxOverlayX = 1180
BoxOverlayY = 78
ShowNativeBoxButton = false
# Positive move Right - Negative move Left
NativeButtonX = 12
# Positive move Up - Negative move Down
NativeButtonY = -40

[Trading Post Create Layout]
# Create-listing layout controls. Change these if the Offered/Requested row needs tuning.
# Width keys affect spacing without needing a rebuild.
CreateLabelWidth = 82
OfferedTypeButtonWidth = 125
OfferedFieldWidth = 220
OfferedSatsFieldWidth = 176
OfferedArrowWidth = 28
RequestTypeButtonWidth = 125
RequestFieldWidth = 220
RequestSatsFieldWidth = 176
RequestArrowWidth = 28
ListPostButtonWidth = 118
CreateRowSpacing = 6
CreateColumnGap = 4
# Positive row/field offsets add extra spacing to the right.
OfferedRowOffsetX = 51
RequestedRowOffsetX = 0
RequestFieldOffsetX = 0
ListPostOffsetX = 0
# Separate Create Listing window. Positive X/Y move the popup right/down.
CreateWindowX = 120
CreateWindowY = 120
CreateWindowWidth = 620
CreateWindowHeight = 410
# Side drawer mode keeps Create Listing attached to the right of Trading Post instead of floating over the table.
CreateUseSideDrawer = true
# Positive gap separates it from the main window. Negative gap slightly overlaps borders so it feels attached.
CreateDrawerGap = -2
# Positive offset moves the drawer down relative to the main Trading Post top.
CreateDrawerOffsetY = 48
# true adds a subtle dark overlay over the main Trading Post while the drawer is open.
CreateDimMainWindow = false
CreateTitleOffsetX = 0
CreateTitleOffsetY = 0
OfferedTypeButtonOffsetX = 0
OfferedTypeButtonOffsetY = 0
OfferedFieldOffsetX = 0
OfferedFieldOffsetY = 0
OfferedArrowOffsetX = 0
OfferedArrowOffsetY = 0
OfferedSatsFieldOffsetX = 0
OfferedSatsFieldOffsetY = 0
RequestTypeButtonOffsetX = 0
RequestTypeButtonOffsetY = 0
RequestFieldOffsetY = 0
RequestArrowOffsetX = 0
RequestArrowOffsetY = 0
RequestSatsFieldOffsetX = 0
RequestSatsFieldOffsetY = 0
ListPostOffsetY = 0
CreateHelpTextOffsetX = 0
CreateHelpTextOffsetY = 0
CreateOpenButtonWidth = 170
CreateOpenButtonHeight = 30
CreateWindowPaddingTop = 6
CreateWindowPaddingLeft = 0
# Dropdown placement/sizing inside the Create Listing popup.
# OfferedKindDropdownOffsetX moves only the small MoN/SATS menu under [Offered: ...].
OfferedKindDropdownOffsetX = 0
OfferedKindDropdownWidth = 150
# OfferedDropdownOffsetX controls the full offered MoN list offset relative to the offered value box.
# 0 = line up with [MON Choose an Offered MoN].
OfferedDropdownOffsetX = 2
OfferedDropdownWidth = 420
OfferedDropdownHeight = 170
RequestKindDropdownOffsetX = 0
RequestKindDropdownWidth = 150
# RequestSpeciesDropdownOffsetX controls the full requested MoN list offset relative to the requested value box.
# 0 = line up with [MON Choose Requested MoN].
RequestSpeciesDropdownOffsetX = 40
RequestSpeciesDropdownWidth = 250
RequestSpeciesDropdownHeight = 170

[Rich Listing Display]
ShowRichListingInfo = true
RichListingUseMonIcon = true
# Offered MoN icon. Offered art uses shiny sprites when the listed MoN is shiny.
RichListingIconSize = 58
# Positive X moves offered icon right. Positive Y moves offered icon down.
RichListingIconOffsetX = 0
RichListingIconOffsetY = 0
# Requested MoN icon shown on the right side of browse/listing cards. Requested art always uses normal/non-shiny sprites.
RichListingShowRequestedMonIcon = true
RichListingRequestedIconSize = 58
RichListingRequestedIconOffsetX = 50
RichListingRequestedIconOffsetY = 0
RichListingRequestedIconColumnWidth = 66
# Draw HP/ATK/DEF/MAG/MDF/SPD in one row instead of two rows.
RichListingStatsSingleLine = true
# Hex RGB color for the vibe/stat name boosted by the MoN vibe. Default matches the green sample provided.
RichListingVibePlusColorHex = A2BA9C
# Hex RGB color for the vibe/stat name reduced by the MoN vibe. Default matches the blue sample provided.
RichListingVibeMinusColorHex = 85ACBD
# v0.5 compact stat widths. These are repeated here so existing v0.4 configs pick up the thinner one-line layout.
RichListingStatCellWidth = 64
RichListingStatLabelWidth = 34
RichListingStatGradeWidth = 18
# Optional manual vibe index display-name mapping. Leave blank to let the mod try to resolve the game's internal name first.
RichListingVibeIndex0Name =
RichListingVibeIndex1Name =
RichListingVibeIndex2Name =
RichListingVibeIndex3Name =
RichListingVibeIndex4Name =
RichListingVibeIndex5Name =
RichListingVibeIndex6Name =
RichListingVibeIndex7Name =
RichListingVibeIndex8Name =
RichListingVibeIndex9Name =
RichListingVibeIndex10Name =
RichListingVibeIndex11Name =
RichListingVibeIndex12Name =
RichListingVibeIndex13Name =
RichListingVibeIndex14Name =
RichListingVibeIndex15Name =
RichListingVibeIndex16Name =
RichListingVibeIndex17Name =
RichListingVibeIndex18Name =
RichListingVibeIndex19Name =
# Space between listing cards and small inner padding before content.
RichListingCardSpacing = 6
RichListingCardInnerPadding = 4
# Listing-card width. 0 = auto-fit. Auto base shrinks/grows with RequestedIconOffsetX if enabled.
RichListingCardWidth = 0
RichListingCardAutoBaseWidth = 500
RichListingCardAutoFollowRequestedIconOffsetX = false
RichListingCardAutoExtraWidth = 0
RichListingCardOffsetX = -30
RichListingCardAlign = Left
# Header/name/meta/stat element offsets. Positive X moves right. Positive Y moves down.
RichListingHeaderOffsetX = 0
RichListingHeaderOffsetY = 0
RichListingNameOffsetX = 0
RichListingNameOffsetY = 0
RichListingMetaOffsetX = 0
RichListingMetaOffsetY = 0
RichListingStatsOffsetX = 0
RichListingStatsOffsetY = 0
RichListingStatsRowSpacing = 2
# Widths used by each stat cell.
RichListingStatCellWidth = 64
RichListingStatLabelWidth = 34
RichListingStatGradeWidth = 18
RichListingButtonHeight = 28
# Width/position for listing action buttons like Offer Chosen MoN / Cancel Listing / Your Listing.
# Set RichListingButtonWidth = 0 to stretch full card width. Align supports Left, Center, or Right.
RichListingButtonWidth = 260
RichListingButtonOffsetX = 0
RichListingButtonAlign = Center
RichListingScrollHeight = 260
# Table/listing icons shown in the Browse and My Listings auction-house table.
TradingPostTableIconSize = 28
TradingPostTableIconTextGap = 6
TradingPostTableShowOfferedIcon = true
TradingPostTableShowRequestedIcon = true

[Trading Post Browse Grid]
# Positional controls for the auction-house style Browse/My Listings table.
GridOffsetX = 0
GridOffsetY = 0
FilterRowOffsetX = 0
FilterRowOffsetY = 0
RefreshButtonOffsetX = 0
RefreshButtonOffsetY = 0
PagerOffsetX = 0
PagerOffsetY = 0
HeaderOffsetX = 0
HeaderOffsetY = 0
ScrollHeightOffset = 0
TableRowHeight = 58
# The first column now displays ""Offer"" instead of ""Name"". NameColumnWidth remains accepted as an old alias.
OfferColumnWidth = 115
RequestedColumnWidth = 100
TimeLeftColumnWidth = 80
SellerColumnWidth = 105
# v0.10.4 hides the Price / Offer column by default. Set true only for debugging/old layout testing.
ShowPriceOfferColumn = false
PriceOfferColumnWidth = 110
ActionColumnWidth = 150
ActionButtonWidth = 145
ActionButtonHeight = 34
RowOffsetX = 0
RowOffsetY = 0
HeaderOfferOffsetX = 0
HeaderOfferOffsetY = 0
HeaderRequestedOffsetX = 0
HeaderRequestedOffsetY = 0
HeaderTimeLeftOffsetX = 0
HeaderTimeLeftOffsetY = 0
HeaderSellerOffsetX = 0
HeaderSellerOffsetY = 0
HeaderPriceOfferOffsetX = 0
HeaderPriceOfferOffsetY = 0
HeaderActionOffsetX = 0
HeaderActionOffsetY = 0
OfferCellOffsetX = 0
OfferCellOffsetY = 0
RequestedCellOffsetX = 0
RequestedCellOffsetY = 0
TimeLeftCellOffsetX = 0
TimeLeftCellOffsetY = 0
SellerCellOffsetX = 0
SellerCellOffsetY = 0
PriceOfferCellOffsetX = 0
PriceOfferCellOffsetY = 0
ActionCellOffsetX = 0
ActionCellOffsetY = 0
RichListingHeaderFontSize = 16
RichListingNameFontSize = 14
RichListingMetaFontSize = 14
RichListingStatsFontSize = 13
# Shiny marker next to offered MoN in the listing header.
# v1.1.8 disables the loose internal sprite search because it could grab wrong blue UI art.
RichListingShinyMarkerText = ✦
RichListingShinyMarkerColorHex = D6B24A
RichListingShinyMarkerFontSize = 28
RichListingShinyMarkerUseChatIcon = true
RichListingShinyMarkerSpriteName = sparklyIcon
# Positive X moves shiny marker right. Positive Y moves shiny marker down.
RichListingShinyMarkerOffsetX = 0
RichListingShinyMarkerOffsetY = -5
# Logs reflected Mon fields used for vibe/stat debugging. Leave false for normal play.
RichListingMetadataDebugLogging = false


[OfferedMON]
# Slightly taller selected-preview and dropdown rows for the Offered MoN selector.
RowHeight = 52
SelectedPreviewHeight = 44
ShowIcon = true
IconSize = 36
# Positive X moves right. Positive Y moves down.
IconOffsetX = 0
IconOffsetY = 0
# Text offsets inside the selector row/preview box.
TextOffsetX = 44
TextOffsetY = 0

[RequestedMON]
# Separate tuning for the Requested MoN selector.
RowHeight = 52
SelectedPreviewHeight = 44
ShowIcon = true
IconSize = 36
# Positive X moves right. Positive Y moves down.
IconOffsetX = 0
IconOffsetY = 0
# Text offsets inside the selector row/preview box.
TextOffsetX = 44
TextOffsetY = 0

[Debug]
DebugLogging = true

[CreateListingGroups]
# v0.10.5 grouped movement controls for the Create Listing drawer.
# These move a complete control group together so the button and its dropdown stay aligned.
# OfferedTypeGroupOffset moves [Offered: MoN/SATS] plus the MoN/SATS dropdown below it.
OfferedTypeGroupOffsetX = -50
OfferedTypeGroupOffsetY = 0
# OfferedValueGroupOffset moves the offered value box plus its arrow and full offered-MoN dropdown list.
OfferedValueGroupOffsetX = 37
OfferedValueGroupOffsetY = 0
# RequestedTypeGroupOffset moves [Requested: MoN/SATS] plus the MoN/SATS dropdown below it.
RequestedTypeGroupOffsetX = 0
RequestedTypeGroupOffsetY = 0
# RequestedValueGroupOffset moves the requested value box plus its arrow and full requested-MoN dropdown list.
RequestedValueGroupOffsetX = 0
RequestedValueGroupOffsetY = 0
# ListPostButtonOffset moves only the [List Post] button.
ListPostButtonOffsetX = 0
ListPostButtonOffsetY = 0

[CreateListingDropdowns]
# Fine tuning for dropdowns after the grouped offsets above are set.
# OfferedTypeDropdownOffset moves only the small MoN/SATS dropdown under [Offered: ...].
OfferedKindDropdownOffsetX = 0
OfferedKindDropdownOffsetY = 0
OfferedKindDropdownWidth = 150
# OfferedValueDropdownOffset moves only the full offered-MoN list after the offered value group position.
OfferedValueDropdownOffsetX = 0
OfferedValueDropdownOffsetY = 0
OfferedDropdownWidth = 420
OfferedDropdownHeight = 170
# RequestedTypeDropdownOffset moves only the small MoN/SATS dropdown under [Requested: ...].
RequestKindDropdownOffsetX = 0
RequestKindDropdownOffsetY = 0
RequestKindDropdownWidth = 150
# RequestedValueDropdownOffset moves only the full requested-MoN list after the requested value group position.
RequestedValueDropdownOffsetX = 0
RequestedValueDropdownOffsetY = 0
RequestSpeciesDropdownWidth = 250
RequestSpeciesDropdownHeight = 170

[ListingBoardColumns]
# Header/column controls for the listing board. Widths move following columns because this is a table layout.
OfferColumnWidth = 125
HeaderOfferOffsetX = 0
HeaderOfferOffsetY = 0
RequestedColumnWidth = 135
HeaderRequestedOffsetX = 0
HeaderRequestedOffsetY = 0
TimeLeftColumnWidth = 80
HeaderTimeLeftOffsetX = 0
HeaderTimeLeftOffsetY = 0
SellerColumnWidth = 105
HeaderSellerOffsetX = 0
HeaderSellerOffsetY = 0
ActionColumnWidth = 150
HeaderActionOffsetX = 0
HeaderActionOffsetY = 0
ShowPriceOfferColumn = false

[ListingView]
# Controls for the actual listing rows under the column headers.
TableRowHeight = 58
RowOffsetX = 0
RowOffsetY = 0
# OfferedMonOrSat controls the first row value, whether it is a MoN or SATS amount.
OfferCellOffsetX = 0
OfferCellOffsetY = 0
OfferIconSize = 45
OfferIconOffsetX = 0
OfferIconOffsetY = 0
# RequestedMonOrSat controls the requested row value, whether it is a MoN or SATS amount.
RequestedCellOffsetX = 0
RequestedCellOffsetY = 0
RequestedIconSize = 45
RequestedIconOffsetX = 0
RequestedIconOffsetY = 0
# SATS icons shown in the listing board when the offer/request is currency instead of a MoN.
OfferSatsIconSize = 45
OfferSatsIconOffsetX = 0
OfferSatsIconOffsetY = 0
RequestedSatsIconSize = 45
RequestedSatsIconOffsetX = 0
RequestedSatsIconOffsetY = 0
TimeLeftCellOffsetX = 0
TimeLeftCellOffsetY = 0
SellerCellOffsetX = 0
SellerCellOffsetY = 0
ActionCellOffsetX = 0
ActionCellOffsetY = 0
ActionButtonWidth = 145
ActionButtonHeight = 34

[Trading Post Window]
RememberWindowPosition = true
RememberWindowSize = true
WindowX = 60
WindowY = 60
WindowWidth = 560
WindowHeight = 620

[Trading Post Filters]
SearchText =
OfferedFilter = All
RequestedFilter = All
TypeFilter = All
TimeLeftFilter = All
SellerFilter =

[Trading Post Filter Bar]
# Legacy shared heights remain accepted; per-control values below are the new defaults.
FilterButtonHeight = 30
FilterFieldHeight = 30
SearchFieldHeight = 30
RefreshButtonHeight = 32
# Search name field.
SearchOffsetX = 0
SearchOffsetY = 0
SearchWidth = 120
SearchHeight = 32
# Offered filter button.
OfferedButtonOffsetX = 0
OfferedButtonOffsetY = 0
OfferedButtonWidth = 92
OfferedButtonHeight = 32
# Requested filter button.
RequestedButtonOffsetX = 0
RequestedButtonOffsetY = 0
RequestedButtonWidth = 105
RequestedButtonHeight = 32
# Type filter button.
TypeButtonOffsetX = 0
TypeButtonOffsetY = 0
TypeButtonWidth = 110
TypeButtonHeight = 32
# Time Left filter button.
TimeLeftButtonOffsetX = 0
TimeLeftButtonOffsetY = 0
TimeLeftButtonWidth = 100
TimeLeftButtonHeight = 32
# Seller text field.
SellerOffsetX = 0
SellerOffsetY = 0
SellerWidth = 90
SellerHeight = 32
# Refresh button.
RefreshButtonOffsetX = 0
RefreshButtonOffsetY = 0
RefreshButtonWidth = 90

[Mailbox Compose Drawer]
# Compose Mail opens as an attached side drawer like Create Listing.
ComposeButtonWidth = 120
ComposeButtonHeight = 30
DrawerWidth = 440
DrawerHeight = 430
DrawerGap = -2
DrawerOffsetY = 48
TitleOffsetX = 0
TitleOffsetY = 0
ToLabelOffsetX = 0
ToLabelOffsetY = 0
ToLabelWidth = 54
ToLabelHeight = 24
ToFieldOffsetX = 0
ToFieldOffsetY = 0
ToFieldWidth = 260
ToFieldHeight = 28
SubjectLabelOffsetX = 0
SubjectLabelOffsetY = 0
SubjectLabelWidth = 54
SubjectLabelHeight = 24
SubjectFieldOffsetX = 0
SubjectFieldOffsetY = 0
SubjectFieldWidth = 300
SubjectFieldHeight = 28
BodyLabelOffsetX = 0
BodyLabelOffsetY = 0
BodyLabelWidth = 54
BodyLabelHeight = 24
BodyFieldOffsetX = 0
BodyFieldOffsetY = 0
BodyFieldWidth = 360
BodyFieldHeight = 160
SendButtonOffsetX = 0
SendButtonOffsetY = 0
SendButtonWidth = 90
SendButtonHeight = 30
CancelButtonOffsetX = 0
CancelButtonOffsetY = 0
CancelButtonWidth = 90
CancelButtonHeight = 30
";

    private void PrepareOfficialSessionConfig(LauncherConfig config, string gameDir)
    {
        string configDir = Path.Combine(gameDir, "BepInEx", "config");
        Directory.CreateDirectory(configDir);
        string path = Path.Combine(configDir, "goose.monsterpatch.gts.client.cfg");
        string tokenB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(config.AioSessionToken ?? ""));
        string expires = string.IsNullOrWhiteSpace(config.OfficialSessionExpiresUtc)
            ? DateTime.UtcNow.AddHours(12).ToString("o")
            : config.OfficialSessionExpiresUtc;

        EnsureOfficialTradingPostConfig(path);

        var lines = File.ReadAllLines(path).ToList();

        UpsertIniValue(lines, "Server", "Host", "mon.gamingwithgoose.com");
        UpsertIniValue(lines, "Server", "Port", "61526");

        // Keep both names during the account-transition builds. Older code looks for [Steam OpenID],
        // while launcher-written configs intentionally document that browser Steam auth is disabled.
        UpsertIniValue(lines, "Steam OpenID", "AutoOpenBrowser", "false");
        UpsertIniValue(lines, "Disabled Steam OpenID", "AutoOpenBrowser", "false");
        UpsertIniValue(lines, "Disabled Steam OpenID", "AutoOpenSteamLogin", "false");

        UpsertIniValue(lines, "Official Server Auth", "CachedOfficialSessionTokenBase64", tokenB64);
        UpsertIniValue(lines, "Official Server Auth", "CachedOfficialSessionExpiresUtc", expires);
        UpsertIniValue(lines, "Official Server Auth", "CachedOfficialSteamID64", config.PseudoSteamId ?? "");
        UpsertIniValue(lines, "Official Server Auth", "CachedOfficialAccountUUID", config.AccountId ?? "");
        UpsertIniValue(lines, "Official Server Auth", "AutoOpenSteamLogin", "false");

        // Backward/diagnostic aliases retained for humans and older interim builds.
        UpsertIniValue(lines, "Official Server Auth", "CachedSessionToken", tokenB64);
        UpsertIniValue(lines, "Official Server Auth", "CachedSessionExpiresUtc", expires);
        UpsertIniValue(lines, "Official Server Auth", "CachedSteamID64", config.PseudoSteamId ?? "");
        UpsertIniValue(lines, "Official Server Auth", "CachedAccountUUID", config.AccountId ?? "");

        UpsertIniValue(lines, "Launcher Account", "AccountBaseUrl", config.AccountBaseUrl);
        UpsertIniValue(lines, "Launcher Account", "AccountId", config.AccountId ?? "");
        UpsertIniValue(lines, "Launcher Account", "DisplayName", config.DisplayName ?? "");
        UpsertIniValue(lines, "Launcher Account", "LauncherSessionToken", config.LauncherSessionToken ?? "");
        UpsertIniValue(lines, "Launcher Account", "AioSessionToken", config.AioSessionToken ?? "");
        UpsertIniValue(lines, "Launcher Account", "PseudoSteamId", config.PseudoSteamId ?? "");

        UpsertIniValue(lines, "Official Server Network", "OnlineSaveCacheEnabled", "true");
        UpsertIniValue(lines, "Official Server Network", "LauncherUploadsOnlineSaveCache", "true");
        UpsertIniValue(lines, "Official Server Network", "LazyLoadOnlineSaveSlotPayloads", "false");
        UpsertIniValue(lines, "Official Server Network", "FinalSaveFlushTimeoutMs", "250");
        UpsertIniValue(lines, "Official Server Network", "SkipFinalDisconnectSaveIfLastUploadWithinSeconds", "60");

        if (!ContainsIniKey(lines, "UI", "WindowScale"))
            UpsertIniValue(lines, "UI", "WindowScale", "1.0");

        File.WriteAllLines(path, lines);
        _log("Updated account-backed Official Server session config without replacing Trading Post layout: " + path);
    }

    private void EnsureOfficialTradingPostConfig(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            File.WriteAllText(path, OfficialTradingPostLayoutDefaults);
            _log("Seeded Official Server config with legacy Trading Post layout defaults: " + path);
            return;
        }

        var lines = File.ReadAllLines(path).ToList();
        bool repaired = ApplyLegacyTradingPostLayoutDefaults(lines);
        if (repaired)
        {
            File.WriteAllLines(path, lines);
            _log("Repaired/preserved Trading Post layout defaults in launcher-staged config.");
        }
    }

    private static bool ApplyLegacyTradingPostLayoutDefaults(List<string> lines)
    {
        bool changed = false;
        var defaultEntries = ParseIniEntries(OfficialTradingPostLayoutDefaults).ToList();
        var layoutSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Trading Post Create Layout",
            "Rich Listing Display",
            "Trading Post Browse Grid",
            "OfferedMON",
            "RequestedMON",
            "Debug",
            "CreateListingGroups",
            "CreateListingDropdowns",
            "ListingBoardColumns",
            "ListingView",
            "Trading Post Filters",
            "Trading Post Filter Bar",
            "Mailbox Compose Drawer"
        };

        bool missingCoreLayout = !ContainsIniSection(lines, "Trading Post Create Layout")
            || !ContainsIniSection(lines, "CreateListingGroups")
            || !ContainsIniSection(lines, "CreateListingDropdowns")
            || !ContainsIniSection(lines, "ListingBoardColumns")
            || !ContainsIniSection(lines, "ListingView");

        bool knownLauncherFallbackLayout =
            HasDuplicateIniSection(lines, "Trading Post Create Layout") ||
            HasDuplicateIniSection(lines, "CreateListingGroups") ||
            HasDuplicateIniSection(lines, "CreateListingDropdowns") ||
            HasDuplicateIniSection(lines, "ListingBoardColumns") ||
            HasDuplicateIniSection(lines, "ListingView") ||
            HasIniValueInAnyMatchingSection(lines, "Trading Post Create Layout", "OfferedDropdownOffsetX", "115") ||
            HasIniValueInAnyMatchingSection(lines, "Trading Post Create Layout", "RequestSpeciesDropdownOffsetX", "115") ||
            HasIniValueInAnyMatchingSection(lines, "CreateListingGroups", "OfferedTypeGroupOffsetX", "0") ||
            HasIniValueInAnyMatchingSection(lines, "CreateListingGroups", "OfferedValueGroupOffsetX", "0") ||
            HasIniValueInAnyMatchingSection(lines, "CreateListingDropdowns", "OfferedDropdownOffsetX", "0") ||
            HasIniValueInAnyMatchingSection(lines, "CreateListingDropdowns", "RequestSpeciesDropdownOffsetX", "0") ||
            HasIniValueInAnyMatchingSection(lines, "ListingBoardColumns", "OfferColumnWidth", "190") ||
            HasIniValueInAnyMatchingSection(lines, "ListingView", "OfferIconSize", "28") ||
            HasIniValueInAnyMatchingSection(lines, "ListingView", "RequestedIconSize", "28");

        bool forceLegacyLayout = missingCoreLayout || knownLauncherFallbackLayout;

        if (forceLegacyLayout)
        {
            return RebuildLegacyTradingPostLayoutSections(lines, defaultEntries);
        }

        foreach (var entry in defaultEntries)
        {
            if (!layoutSections.Contains(entry.Section))
                continue;

            if (!ContainsIniKey(lines, entry.Section, entry.Key))
                changed |= UpsertIniValue(lines, entry.Section, entry.Key, entry.Value);
        }

        // Preserve user-saved window location/size, but make sure the remembering behavior exists/enabled.
        changed |= UpsertIniValue(lines, "Trading Post Window", "RememberWindowPosition", "true");
        changed |= UpsertIniValue(lines, "Trading Post Window", "RememberWindowSize", "true");
        foreach (var entry in defaultEntries.Where(e => e.Section.Equals("Trading Post Window", StringComparison.OrdinalIgnoreCase)))
        {
            if (entry.Key.Equals("RememberWindowPosition", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Equals("RememberWindowSize", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!ContainsIniKey(lines, entry.Section, entry.Key))
                changed |= UpsertIniValue(lines, entry.Section, entry.Key, entry.Value);
        }

        // Remove bad duplicate fallback aliases that were appended under [CreateListingDropdowns].
        // Those aliases are parsed after [Trading Post Create Layout] and can override the tuned old values.
        changed |= RemoveIniKeyFromAllMatchingSections(lines, "CreateListingDropdowns", "OfferedDropdownOffsetX");
        changed |= RemoveIniKeyFromAllMatchingSections(lines, "CreateListingDropdowns", "RequestSpeciesDropdownOffsetX");

        return changed;
    }

    private static bool RebuildLegacyTradingPostLayoutSections(List<string> lines, List<(string Section, string Key, string Value)> defaultEntries)
    {
        string? oldWindowX = GetLastIniValue(lines, "Trading Post Window", "WindowX");
        string? oldWindowY = GetLastIniValue(lines, "Trading Post Window", "WindowY");
        string? oldWindowWidth = GetLastIniValue(lines, "Trading Post Window", "WindowWidth");
        string? oldWindowHeight = GetLastIniValue(lines, "Trading Post Window", "WindowHeight");

        var rebuildSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Trading Post Create Layout",
            "Rich Listing Display",
            "Trading Post Browse Grid",
            "OfferedMON",
            "RequestedMON",
            "Debug",
            "CreateListingGroups",
            "CreateListingDropdowns",
            "ListingBoardColumns",
            "ListingView",
            "Trading Post Window",
            "Trading Post Filters",
            "Trading Post Filter Bar",
            "Mailbox Compose Drawer"
        };

        RemoveIniSections(lines, rebuildSections);

        string previousSection = "";
        foreach (var entry in defaultEntries.Where(e => rebuildSections.Contains(e.Section)))
        {
            if (!entry.Section.Equals(previousSection, StringComparison.OrdinalIgnoreCase))
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                    lines.Add("");
                lines.Add("[" + entry.Section + "]");
                previousSection = entry.Section;
            }
            lines.Add(entry.Key + " = " + entry.Value);
        }

        UpsertIniValue(lines, "Trading Post Window", "RememberWindowPosition", "true");
        UpsertIniValue(lines, "Trading Post Window", "RememberWindowSize", "true");
        if (!string.IsNullOrWhiteSpace(oldWindowX)) UpsertIniValue(lines, "Trading Post Window", "WindowX", oldWindowX);
        if (!string.IsNullOrWhiteSpace(oldWindowY)) UpsertIniValue(lines, "Trading Post Window", "WindowY", oldWindowY);
        if (!string.IsNullOrWhiteSpace(oldWindowWidth)) UpsertIniValue(lines, "Trading Post Window", "WindowWidth", oldWindowWidth);
        if (!string.IsNullOrWhiteSpace(oldWindowHeight)) UpsertIniValue(lines, "Trading Post Window", "WindowHeight", oldWindowHeight);
        return true;
    }

    private static IEnumerable<(string Section, string Key, string Value)> ParseIniEntries(string text)
    {
        string section = "";
        using var reader = new StringReader(text);
        while (reader.ReadLine() is string raw)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line[1..^1].Trim();
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0 || string.IsNullOrWhiteSpace(section))
                continue;
            yield return (section, line[..eq].Trim(), line[(eq + 1)..].Trim());
        }
    }

    private static bool ContainsIniSection(List<string> lines, string section)
    {
        return lines.Any(line => line.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsIniKey(List<string> lines, string section, string key)
    {
        return TryFindIniKey(lines, section, key, out _, out _);
    }

    private static bool HasIniValue(List<string> lines, string section, string key, string value)
    {
        return TryFindIniKey(lines, section, key, out int index, out _) &&
            string.Equals(GetIniValue(lines[index]), value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasIniValueInAnyMatchingSection(List<string> lines, string section, string key, string value)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (!lines[i].Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase))
                continue;
            int sectionEnd = FindIniSectionEnd(lines, i);
            for (int j = i + 1; j < sectionEnd; j++)
            {
                string trimmed = lines[j].Trim();
                int eq = trimmed.IndexOf('=');
                if (eq <= 0)
                    continue;
                if (trimmed[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase) &&
                    trimmed[(eq + 1)..].Trim().Equals(value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static bool HasDuplicateIniSection(List<string> lines, string section)
    {
        int count = 0;
        string wanted = "[" + section + "]";
        foreach (string line in lines)
        {
            if (line.Trim().Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                count++;
                if (count > 1)
                    return true;
            }
        }
        return false;
    }

    private static bool UpsertIniValue(List<string> lines, string section, string key, string value)
    {
        if (TryFindIniKey(lines, section, key, out int keyIndex, out _))
        {
            string replacement = key + " = " + value;
            if (lines[keyIndex] == replacement)
                return false;
            lines[keyIndex] = replacement;
            return true;
        }

        int sectionStart = FindIniSection(lines, section);
        if (sectionStart < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add("");
            lines.Add("[" + section + "]");
            lines.Add(key + " = " + value);
            return true;
        }

        int sectionEnd = FindIniSectionEnd(lines, sectionStart);
        lines.Insert(sectionEnd, key + " = " + value);
        return true;
    }

    private static bool RemoveIniKey(List<string> lines, string section, string key)
    {
        bool removed = false;
        while (TryFindIniKey(lines, section, key, out int keyIndex, out _))
        {
            lines.RemoveAt(keyIndex);
            removed = true;
        }
        return removed;
    }

    private static bool RemoveIniKeyFromAllMatchingSections(List<string> lines, string section, string key)
    {
        bool removed = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (!lines[i].Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase))
                continue;
            int sectionEnd = FindIniSectionEnd(lines, i);
            for (int j = sectionEnd - 1; j > i; j--)
            {
                string trimmed = lines[j].Trim();
                int eq = trimmed.IndexOf('=');
                if (eq <= 0)
                    continue;
                if (trimmed[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    lines.RemoveAt(j);
                    removed = true;
                }
            }
        }
        return removed;
    }

    private static string? GetLastIniValue(List<string> lines, string section, string key)
    {
        string? value = null;
        for (int i = 0; i < lines.Count; i++)
        {
            if (!lines[i].Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase))
                continue;
            int sectionEnd = FindIniSectionEnd(lines, i);
            for (int j = i + 1; j < sectionEnd; j++)
            {
                string trimmed = lines[j].Trim();
                int eq = trimmed.IndexOf('=');
                if (eq <= 0)
                    continue;
                if (trimmed[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    value = trimmed[(eq + 1)..].Trim();
            }
        }
        return value;
    }

    private static void RemoveIniSections(List<string> lines, HashSet<string> sections)
    {
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            string trimmed = lines[i].Trim();
            if (!trimmed.StartsWith("[") || !trimmed.EndsWith("]"))
                continue;
            string section = trimmed[1..^1].Trim();
            if (!sections.Contains(section))
                continue;
            int sectionEnd = FindIniSectionEnd(lines, i);
            lines.RemoveRange(i, sectionEnd - i);
        }
    }

    private static bool TryFindIniKey(List<string> lines, string section, string key, out int keyIndex, out int sectionStart)
    {
        keyIndex = -1;
        sectionStart = FindIniSection(lines, section);
        if (sectionStart < 0)
            return false;
        int sectionEnd = FindIniSectionEnd(lines, sectionStart);
        for (int i = sectionStart + 1; i < sectionEnd; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#") || trimmed.StartsWith(";"))
                continue;
            int eq = trimmed.IndexOf('=');
            if (eq <= 0)
                continue;
            string existingKey = trimmed[..eq].Trim();
            if (existingKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                keyIndex = i;
                return true;
            }
        }
        return false;
    }

    private static int FindIniSection(List<string> lines, string section)
    {
        string wanted = "[" + section + "]";
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().Equals(wanted, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static int FindIniSectionEnd(List<string> lines, int sectionStart)
    {
        for (int i = sectionStart + 1; i < lines.Count; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                return i;
        }
        return lines.Count;
    }

    private static string GetIniValue(string line)
    {
        int eq = line.IndexOf('=');
        return eq < 0 ? "" : line[(eq + 1)..].Trim();
    }

    private void WriteSession(LauncherConfig config, bool stagedRuntime)
    {
        Directory.CreateDirectory(LauncherConfigService.ConfigDirectory);
        var session = new
        {
            mode = "online",
            launcherVersion = "v0.13.3_TradingPostLayoutFix",
            createdUtc = DateTime.UtcNow,
            accountId = config.AccountId,
            usernameOrEmail = config.UsernameOrEmail,
            displayName = config.DisplayName,
            launcherSessionToken = config.LauncherSessionToken,
            aioSessionToken = config.AioSessionToken,
            officialSessionExpiresUtc = config.OfficialSessionExpiresUtc,
            pseudoSteamId = config.PseudoSteamId,
            accountBaseUrl = config.AccountBaseUrl,
            patchManifestUrl = config.PatchManifestUrl,
            launcherStatusUrl = config.LauncherStatusUrl,
            mmoHost = config.MmoHost,
            tradingPostHost = config.TradingPostHost,
            authBaseUrl = config.AuthBaseUrl,
            stagedRuntime
        };
        File.WriteAllText(LauncherConfigService.SessionPath, JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true }));
        _log($"Session written: {LauncherConfigService.SessionPath}");
    }

    private static void ValidateGameExe(string gameExePath)
    {
        LauncherPlatform.ValidateGameExecutable(gameExePath);
    }

    private Process StartGame(string gameExePath, IReadOnlyDictionary<string, string>? environment)
    {
        string fileName = gameExePath;
        string workingDirectory = Path.GetDirectoryName(gameExePath)!;
        string? linuxScriptGameArgument = null;

        if (LauncherPlatform.IsLinux && Path.GetFileName(gameExePath).Equals(LauncherPlatform.LinuxGameExeName, StringComparison.OrdinalIgnoreCase))
        {
            string runScript = Path.Combine(workingDirectory, "run_bepinex.sh");
            if (File.Exists(runScript))
            {
                LauncherPlatform.TryChmodExecutable(runScript, _log);
                fileName = runScript;
                // Pass the actual executable explicitly instead of relying only on the edited
                // executable_name field. This mirrors the known-good manual script launch flow
                // and makes failures easier to diagnose in the stage log.
                linuxScriptGameArgument = gameExePath;
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };

        if (!string.IsNullOrWhiteSpace(linuxScriptGameArgument))
            psi.ArgumentList.Add(linuxScriptGameArgument);

        if (environment is not null)
        {
            foreach (var kv in environment)
                psi.Environment[kv.Key] = kv.Value;
        }

        _log("Process start file: " + fileName);
        _log("Process working directory: " + workingDirectory);
        if (!string.IsNullOrWhiteSpace(linuxScriptGameArgument))
            _log("Process argument[0]: " + linuxScriptGameArgument);

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Monsterpatch.");
        _log($"Monsterpatch started. PID {process.Id}");
        return process;
    }

    private void StartSteamGame(LauncherConfig config)
    {
        string appId = string.IsNullOrWhiteSpace(config.SteamAppId) ? LauncherPlatform.SteamAppId : config.SteamAppId.Trim();
        string url = "steam://rungameid/" + appId;

        try
        {
            var psi = LauncherPlatform.IsWindows
                ? new ProcessStartInfo("cmd", "/c start " + url) { UseShellExecute = false, CreateNoWindow = true }
                : new ProcessStartInfo("steam", "-applaunch " + appId) { UseShellExecute = false };
            Process.Start(psi);
            _log("Steam launch requested for app " + appId + ".");
        }
        catch (Exception first)
        {
            _log("Primary Steam launch failed: " + first.Message);
            try
            {
                var fallback = LauncherPlatform.IsWindows
                    ? new ProcessStartInfo(url) { UseShellExecute = true }
                    : new ProcessStartInfo("xdg-open", url) { UseShellExecute = false };
                Process.Start(fallback);
                _log("Steam URL fallback launch requested for app " + appId + ".");
            }
            catch (Exception second)
            {
                throw new InvalidOperationException("Could not launch Steam app " + appId + ". " + second.Message, second);
            }
        }
    }


    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Non-fatal.
        }
    }
}

public static class EmbeddedPayloadExtractor
{
    public static async Task<string> ExtractToCacheAsync(Action<string> log)
    {
        string rid = LauncherPlatform.PayloadRid;
        string cacheRoot = Path.Combine(LauncherConfigService.EmbeddedPayloadCacheDirectory, rid);
        if (Directory.Exists(cacheRoot))
            Directory.Delete(cacheRoot, recursive: true);
        Directory.CreateDirectory(cacheRoot);

        int embeddedCount = await ExtractEmbeddedPayloadAsync(rid, cacheRoot);
        if (embeddedCount > 0)
        {
            log($"Embedded payload {rid} extracted to {cacheRoot} ({embeddedCount} files). No external Payloads folder required.");
            return cacheRoot;
        }

        // Developer/source-tree fallback only. Release builds are expected to use embedded payloads.
        string? payloadSource = LauncherPlatform.FindPayloadSourceDirectory(rid);
        if (payloadSource is null)
        {
            throw new DirectoryNotFoundException(
                "Could not find embedded launcher payload for " + rid +
                " and no developer Payloads/" + rid + " folder was found beside the launcher/source tree.");
        }

        CopyDirectory(payloadSource, cacheRoot, overwrite: true);
        int count = Directory.EnumerateFiles(cacheRoot, "*", SearchOption.AllDirectories).Count();
        log($"Developer payload {rid} copied from {payloadSource} to {cacheRoot} ({count} files). Release builds should normally use embedded payload resources.");
        return cacheRoot;
    }

    private static async Task<int> ExtractEmbeddedPayloadAsync(string rid, string cacheRoot)
    {
        Assembly assembly = typeof(EmbeddedPayloadExtractor).Assembly;
        string prefix = "Payloads/" + rid + "/";
        int count = 0;

        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            string normalized = resourceName.Replace('\\', '/');
            int index = normalized.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;

            string relative = normalized[(index + prefix.Length)..];
            if (string.IsNullOrWhiteSpace(relative) || relative.EndsWith("/", StringComparison.Ordinal))
                continue;

            string[] parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            string target = Path.Combine(new[] { cacheRoot }.Concat(parts).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            await using Stream? input = assembly.GetManifestResourceStream(resourceName);
            if (input is null)
                continue;

            await using FileStream output = File.Create(target);
            await input.CopyToAsync(output);
            count++;
        }

        return count;
    }

    private static void CopyDirectory(string sourceDir, string targetDir, bool overwrite)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string directory in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(targetDir, relative));
        }
        foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            string target = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite);
        }
    }
}


public sealed class OnlineRuntimeStager
{
    private const string BackupDirName = "BepInEx.MMOnsterpatchOfflineBackup";
    private const string RuntimeMarkerFileName = ".mmonsterpatch_online_runtime";
    private const string RootBackupSuffix = ".MMOnsterpatchOfflineBackup";

    private readonly Action<string> _log;

    public OnlineRuntimeStager(Action<string> log)
    {
        _log = log;
    }

    public async Task StageOnlineRuntimeAsync(string gameDir, string payloadRoot)
    {
        string liveBepInEx = Path.Combine(gameDir, "BepInEx");
        string backupBepInEx = Path.Combine(gameDir, BackupDirName);
        string marker = Path.Combine(liveBepInEx, RuntimeMarkerFileName);

        string payloadBepInEx = Path.Combine(payloadRoot, "BepInEx");
        string payloadRootFiles = Path.Combine(payloadRoot, "Root");

        if (!Directory.Exists(payloadBepInEx))
            throw new DirectoryNotFoundException("Payload is missing BepInEx folder: " + payloadBepInEx);
        if (!Directory.Exists(payloadRootFiles))
            throw new DirectoryNotFoundException("Payload is missing Root folder: " + payloadRootFiles);

        if (Directory.Exists(liveBepInEx))
        {
            if (File.Exists(marker))
            {
                _log("Removing leftover launcher-staged BepInEx runtime.");
                Directory.Delete(liveBepInEx, recursive: true);
            }
            else
            {
                if (Directory.Exists(backupBepInEx))
                {
                    throw new InvalidOperationException(
                        "BepInEx backup already exists, but the live BepInEx is not launcher-staged. " +
                        "Restore/check the game folder manually before online staging.");
                }

                _log("Backing up user's current BepInEx folder.");
                Directory.Move(liveBepInEx, backupBepInEx);
            }
        }

        Directory.CreateDirectory(liveBepInEx);
        File.WriteAllText(Path.Combine(liveBepInEx, RuntimeMarkerFileName), "MMOnsterpatch launcher staged online runtime\n");
        CopyDirectory(payloadBepInEx, liveBepInEx, overwrite: true);

        string launcherConfig = LauncherConfigService.OnlineConfigDirectory;
        string stagedConfig = Path.Combine(liveBepInEx, "config");
        Directory.CreateDirectory(stagedConfig);
        if (Directory.Exists(launcherConfig))
        {
            _log("Copying launcher-owned online config into staged runtime.");
            CopyDirectory(launcherConfig, stagedConfig, overwrite: true);
        }

        EnsureRootBootstrapFiles(gameDir, payloadRootFiles);
        if (LauncherPlatform.IsLinux)
        {
            LauncherPlatform.PatchLinuxRunBepInEx(gameDir, _log);
            LauncherPlatform.TryChmodExecutable(Path.Combine(gameDir, "run_bepinex.sh"), _log);
            LauncherPlatform.TryChmodExecutable(Path.Combine(gameDir, LauncherPlatform.LinuxGameExeName), _log);
        }
        else
        {
            EnsureDoorstopConfigEnabled(Path.Combine(gameDir, "doorstop_config.ini"));
        }
        ValidateOnlineRuntime(gameDir, _log);
        await Task.CompletedTask;
    }

    public async Task RestoreOnlineRuntimeAsync(string gameDir)
    {
        string liveBepInEx = Path.Combine(gameDir, "BepInEx");
        string backupBepInEx = Path.Combine(gameDir, BackupDirName);
        string marker = Path.Combine(liveBepInEx, RuntimeMarkerFileName);

        if (Directory.Exists(liveBepInEx) && File.Exists(marker))
        {
            string stagedConfig = Path.Combine(liveBepInEx, "config");
            if (Directory.Exists(stagedConfig))
            {
                Directory.CreateDirectory(LauncherConfigService.OnlineConfigDirectory);
                CopyDirectory(stagedConfig, LauncherConfigService.OnlineConfigDirectory, overwrite: true);
                _log("Saved staged online config back to launcher-owned config folder.");
            }

            Directory.Delete(liveBepInEx, recursive: true);
            _log("Deleted launcher-staged BepInEx runtime.");
        }

        if (!Directory.Exists(liveBepInEx) && Directory.Exists(backupBepInEx))
        {
            Directory.Move(backupBepInEx, liveBepInEx);
            _log("Restored user's original BepInEx folder.");
        }

        RestoreRootBootstrapFiles(gameDir);
        await Task.CompletedTask;
    }

    private void EnsureRootBootstrapFiles(string gameDir, string payloadRootFiles)
    {
        foreach (string source in Directory.EnumerateFiles(payloadRootFiles, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(source);
            string target = Path.Combine(gameDir, name);
            string backup = target + RootBackupSuffix;

            if (File.Exists(target) && !File.Exists(backup))
            {
                File.Copy(target, backup, overwrite: false);
                _log($"Backed up root file: {name}");
            }

            File.Copy(source, target, overwrite: true);
            _log($"Staged root file: {name}");
        }
    }

    private void RestoreRootBootstrapFiles(string gameDir)
    {
        foreach (string backup in Directory.EnumerateFiles(gameDir, "*" + RootBackupSuffix, SearchOption.TopDirectoryOnly))
        {
            string original = backup[..^RootBackupSuffix.Length];
            try
            {
                if (File.Exists(original))
                    File.Delete(original);
                File.Move(backup, original);
                _log($"Restored root file: {Path.GetFileName(original)}");
            }
            catch (Exception ex)
            {
                _log($"Root restore warning for {Path.GetFileName(original)}: {ex.Message}");
            }
        }
    }

    private static void EnsureDoorstopConfigEnabled(string path)
    {
        if (!File.Exists(path))
            return;

        string[] lines = File.ReadAllLines(path);
        bool sawEnabled = false;
        bool sawTarget = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith("enabled=", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = "enabled=true";
                sawEnabled = true;
            }
            else if (trimmed.StartsWith("targetAssembly=", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = "targetAssembly=BepInEx\\core\\BepInEx.Preloader.dll";
                sawTarget = true;
            }
        }

        var output = lines.ToList();
        if (!sawEnabled)
            output.Add("enabled=true");
        if (!sawTarget)
            output.Add("targetAssembly=BepInEx\\core\\BepInEx.Preloader.dll");

        File.WriteAllLines(path, output);
    }

    private static void ValidateOnlineRuntime(string gameDir, Action<string> log)
    {
        string liveBepInEx = Path.Combine(gameDir, "BepInEx");
        var required = new List<string>
        {
            Path.Combine(liveBepInEx, RuntimeMarkerFileName),
            Path.Combine(liveBepInEx, "core", "BepInEx.Preloader.dll"),
        };

        if (LauncherPlatform.IsLinux)
        {
            required.Add(Path.Combine(gameDir, "run_bepinex.sh"));
            required.Add(Path.Combine(gameDir, "libdoorstop.so"));
        }
        else
        {
            required.Add(Path.Combine(gameDir, "winhttp.dll"));
            required.Add(Path.Combine(gameDir, "doorstop_config.ini"));
        }

        string mainPatcher = Path.Combine(liveBepInEx, "patchers", "MMOnsterpatchOfficialServerPatcher.dll");
        required.Add(mainPatcher);

        foreach (string path in required)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Online runtime validation failed. Missing: " + path + ". The game was not launched because it would likely start vanilla/offline.");
        }

        try
        {
            string diag = Path.Combine(gameDir, "MMOnsterpatch_STAGING_DIAGNOSTICS.txt");
            var lines = new List<string>
            {
                "MMOnsterpatch online staging diagnostics",
                "UTC: " + DateTime.UtcNow.ToString("O"),
                "Platform payload RID: " + LauncherPlatform.PayloadRid,
                "Game folder: " + gameDir,
                "BepInEx marker: " + Path.Combine(liveBepInEx, RuntimeMarkerFileName),
                "Main patcher: " + mainPatcher,
                "Linux run_bepinex: " + Path.Combine(gameDir, "run_bepinex.sh"),
                "Linux libdoorstop: " + Path.Combine(gameDir, "libdoorstop.so"),
                "Windows winhttp: " + Path.Combine(gameDir, "winhttp.dll"),
                "Windows doorstop_config: " + Path.Combine(gameDir, "doorstop_config.ini"),
                "",
                "Validated files:",
            };
            foreach (string path in required)
                lines.Add("  OK " + path);
            File.WriteAllLines(diag, lines, new UTF8Encoding(false));
            log("Wrote staging diagnostics: " + diag);
        }
        catch (Exception ex)
        {
            log("Staging diagnostics warning: " + ex.Message);
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir, bool overwrite)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string directory in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(targetDir, relative));
        }
        foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            string target = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite);
        }
    }
}
