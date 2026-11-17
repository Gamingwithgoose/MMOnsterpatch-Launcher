using System;
using System.IO;
using System.Text.Json;

namespace MMOnsterpatchLauncher.Wpf;

public sealed class LauncherConfig
{
    public bool RememberMe { get; set; }
    public bool IsSignedIn { get; set; }
    public string UsernameOrEmail { get; set; } = "";

    public string AccountId { get; set; } = "";
    public string AccountUsername { get; set; } = "";
    public string AccountEmail { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string LauncherSessionToken { get; set; } = "";
    public string AioSessionToken { get; set; } = "";
    public string OfficialSessionExpiresUtc { get; set; } = "";
    public string PseudoSteamId { get; set; } = "";

    public string GameExePath { get; set; } = "";
    public string LastPlayMode { get; set; } = "Online";

    public string AccountBaseUrl { get; set; } = "https://account.gamingwithgoose.com";
    public string PatchManifestUrl { get; set; } = "https://account.gamingwithgoose.com/patch/manifest.json";
    public string LauncherStatusUrl { get; set; } = "https://account.gamingwithgoose.com/api/launcher/status";
    public string MmoHost { get; set; } = "mmo.gamingwithgoose.com";
    public string TradingPostHost { get; set; } = "mon.gamingwithgoose.com";
    public string AuthBaseUrl { get; set; } = "https://mon-auth.gamingwithgoose.com";

    public bool RestoreOnlineRuntimeOnExit { get; set; } = true;
    public bool UseEmbeddedPayloadFallback { get; set; } = true;
}

public sealed class LauncherAccountLoginResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public LauncherAccountInfo? Account { get; set; }
    public LauncherAccountSessionInfo? Session { get; set; }
}

public sealed class LauncherAccountInfo
{
    public string AccountId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PseudoSteamId { get; set; } = "";
    public bool SteamVerified { get; set; }
}

public sealed class LauncherAccountSessionInfo
{
    public string SessionToken { get; set; } = "";
    public string AioSessionToken { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PseudoSteamId { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
}

public static class LauncherConfigService
{
    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MMOnsterpatch");

    public static string ConfigPath =>
        Path.Combine(ConfigDirectory, "launcher_config.json");

    public static string SessionPath =>
        Path.Combine(ConfigDirectory, "launcher_session.json");

    public static string StageLogPath =>
        Path.Combine(ConfigDirectory, "launcher_stage_log.txt");

    public static string EmbeddedPayloadCacheDirectory =>
        Path.Combine(ConfigDirectory, "EmbeddedPayload");

    public static string OnlineConfigDirectory =>
        Path.Combine(ConfigDirectory, "Config");

    public static LauncherConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return new LauncherConfig();

            string json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<LauncherConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new LauncherConfig();

            ApplyDefaultUrls(config);
            return config;
        }
        catch
        {
            return new LauncherConfig();
        }
    }

    public static void Save(LauncherConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        ApplyDefaultUrls(config);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, options));
    }

    private static void ApplyDefaultUrls(LauncherConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.AccountBaseUrl))
            config.AccountBaseUrl = "https://account.gamingwithgoose.com";
        if (string.IsNullOrWhiteSpace(config.PatchManifestUrl))
            config.PatchManifestUrl = "https://account.gamingwithgoose.com/patch/manifest.json";
        if (string.IsNullOrWhiteSpace(config.LauncherStatusUrl))
            config.LauncherStatusUrl = "https://account.gamingwithgoose.com/api/launcher/status";
        if (string.IsNullOrWhiteSpace(config.MmoHost))
            config.MmoHost = "mmo.gamingwithgoose.com";
        if (string.IsNullOrWhiteSpace(config.TradingPostHost))
            config.TradingPostHost = "mon.gamingwithgoose.com";
        if (string.IsNullOrWhiteSpace(config.AuthBaseUrl))
            config.AuthBaseUrl = "https://mon-auth.gamingwithgoose.com";
    }
}
