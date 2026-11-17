using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MMOnsterpatchLauncher.Core;

public static class LauncherPlatform
{
    public const string SteamAppId = "2495320";
    public const string WindowsGameExeName = "Monsterpatch.exe";
    public const string LinuxGameExeName = "Monsterpatch.x86_64";
    public const string LinuxSteamLaunchOption = "./run_bepinex.sh %command%";

    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static string PayloadRid => IsLinux ? "linux-x64" : "win-x64";

    public static string[] ValidGameExecutableNames => IsLinux
        ? new[] { LinuxGameExeName, "Monsterpatch" }
        : new[] { WindowsGameExeName };

    public static void ValidateGameExecutable(string gameExePath)
    {
        if (string.IsNullOrWhiteSpace(gameExePath) || !File.Exists(gameExePath))
            throw new FileNotFoundException("Monsterpatch game executable path is not set or does not exist.", gameExePath);

        string name = Path.GetFileName(gameExePath);
        if (!ValidGameExecutableNames.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            string expected = string.Join(" or ", ValidGameExecutableNames);
            throw new InvalidOperationException("Please select the real Monsterpatch game executable for this platform: " + expected);
        }

        string gameDir = Path.GetDirectoryName(gameExePath) ?? "";
        if (!Directory.Exists(Path.Combine(gameDir, "Monsterpatch_Data")))
            throw new InvalidOperationException("Selected executable does not appear to be in the Monsterpatch game folder. Missing Monsterpatch_Data.");
    }

    public static string? FindPayloadSourceDirectory(string rid)
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDir, "Payloads", rid),
            Path.Combine(baseDir, "..", "Payloads", rid),
            Path.Combine(baseDir, "..", "..", "Payloads", rid),
            Path.Combine(baseDir, "..", "..", "..", "Payloads", rid),
            Path.Combine(baseDir, "..", "..", "..", "..", "Payloads", rid),
            Path.Combine(Directory.GetCurrentDirectory(), "Payloads", rid),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "Payloads", rid),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Payloads", rid),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "Payloads", rid),
        };

        foreach (string raw in candidates)
        {
            string full = Path.GetFullPath(raw);
            if (Directory.Exists(Path.Combine(full, "BepInEx")) && Directory.Exists(Path.Combine(full, "Root")))
                return full;
        }

        return null;
    }

    public static void PatchLinuxRunBepInEx(string gameDir, Action<string> log)
    {
        string script = Path.Combine(gameDir, "run_bepinex.sh");
        if (!File.Exists(script))
            throw new FileNotFoundException("Linux BepInEx run script is missing after staging.", script);

        string text = File.ReadAllText(script);
        string original = text;

        text = Regex.Replace(text, "^\\s*executable_name=\\\".*?\\\"", "executable_name=\"" + LinuxGameExeName + "\"", RegexOptions.Multiline);
        text = Regex.Replace(text, "^\\s*executable_name='.*?'", "executable_name='" + LinuxGameExeName + "'", RegexOptions.Multiline);

        if (text == original && !text.Contains("executable_name=\"" + LinuxGameExeName + "\"", StringComparison.Ordinal))
        {
            text = "executable_name=\"" + LinuxGameExeName + "\"\n" + text;
        }

        File.WriteAllText(script, text, new UTF8Encoding(false));
        log("Patched run_bepinex.sh executable_name=" + LinuxGameExeName + ".");
    }

    public static void TryChmodExecutable(string path, Action<string>? log = null)
    {
        if (!IsLinux || !File.Exists(path))
            return;

        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                ArgumentList = { "+x", path },
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            });
            p?.WaitForExit(5000);
            log?.Invoke("chmod +x " + path);
        }
        catch (Exception ex)
        {
            log?.Invoke("chmod warning for " + path + ": " + ex.Message);
        }
    }
}

public sealed class GameInstallCandidate
{
    public string ExecutablePath { get; init; } = "";
    public string InstallDirectory { get; init; } = "";
    public string Source { get; init; } = "";
}

public static class GameInstallDetector
{
    public static GameInstallCandidate? Detect(LauncherConfig config, Action<string>? log = null)
    {
        if (!string.IsNullOrWhiteSpace(config.GameExePath) && File.Exists(config.GameExePath))
        {
            try
            {
                LauncherPlatform.ValidateGameExecutable(config.GameExePath);
                return new GameInstallCandidate
                {
                    ExecutablePath = config.GameExePath,
                    InstallDirectory = Path.GetDirectoryName(config.GameExePath) ?? "",
                    Source = "saved config"
                };
            }
            catch (Exception ex)
            {
                log?.Invoke("Saved game path rejected: " + ex.Message);
            }
        }

        foreach (string steamRoot in EnumerateSteamRoots())
        {
            foreach (string steamApps in EnumerateSteamAppsFolders(steamRoot))
            {
                string manifest = Path.Combine(steamApps, "appmanifest_" + LauncherPlatform.SteamAppId + ".acf");
                if (!File.Exists(manifest))
                    continue;

                string installDirName = ParseAcfValue(File.ReadAllText(manifest), "installdir");
                if (string.IsNullOrWhiteSpace(installDirName))
                    installDirName = "Monsterpatch";

                string installDir = Path.Combine(steamApps, "common", installDirName);
                var found = FindExecutableInInstallDir(installDir, "Steam manifest " + manifest);
                if (found is not null)
                    return found;
            }
        }

        foreach (string candidate in EnumerateFallbackInstallDirs())
        {
            var found = FindExecutableInInstallDir(candidate, "fallback scan");
            if (found is not null)
                return found;
        }

        return null;
    }

    private static GameInstallCandidate? FindExecutableInInstallDir(string installDir, string source)
    {
        if (!Directory.Exists(installDir))
            return null;

        foreach (string exeName in LauncherPlatform.ValidGameExecutableNames)
        {
            string exe = Path.Combine(installDir, exeName);
            if (File.Exists(exe) && Directory.Exists(Path.Combine(installDir, "Monsterpatch_Data")))
            {
                return new GameInstallCandidate { ExecutablePath = exe, InstallDirectory = installDir, Source = source };
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSteamRoots()
    {
        var roots = new List<string>();
        if (LauncherPlatform.IsWindows)
        {
            string? programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
            if (!string.IsNullOrWhiteSpace(programFilesX86)) roots.Add(Path.Combine(programFilesX86, "Steam"));
            if (!string.IsNullOrWhiteSpace(programFiles)) roots.Add(Path.Combine(programFiles, "Steam"));
        }
        else
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            roots.Add(Path.Combine(home, ".local", "share", "Steam"));
            roots.Add(Path.Combine(home, ".steam", "steam"));
            roots.Add("/home/deck/.local/share/Steam");
        }

        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateSteamAppsFolders(string steamRoot)
    {
        var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string primary = Path.Combine(steamRoot, "steamapps");
        if (Directory.Exists(primary)) apps.Add(primary);

        string libraryFolders = Path.Combine(primary, "libraryfolders.vdf");
        if (File.Exists(libraryFolders))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(libraryFolders), "\\\"path\\\"\\s+\\\"(?<path>.*?)\\\""))
            {
                string path = Regex.Unescape(m.Groups["path"].Value.Replace("\\\\", "\\"));
                string steamApps = Path.Combine(path, "steamapps");
                if (Directory.Exists(steamApps)) apps.Add(steamApps);
            }
        }

        return apps;
    }

    private static IEnumerable<string> EnumerateFallbackInstallDirs()
    {
        foreach (string steamRoot in EnumerateSteamRoots())
        {
            string common = Path.Combine(steamRoot, "steamapps", "common");
            if (Directory.Exists(common))
            {
                foreach (string dir in Directory.EnumerateDirectories(common, "*Monsterpatch*", SearchOption.TopDirectoryOnly))
                    yield return dir;
            }
        }
    }

    private static string ParseAcfValue(string text, string key)
    {
        var m = Regex.Match(text, "\\\"" + Regex.Escape(key) + "\\\"\\s+\\\"(?<value>.*?)\\\"");
        return m.Success ? m.Groups["value"].Value : "";
    }
}

public static class SteamLaunchOptionsManager
{
    public static void EnableOnlineLaunchOptions(LauncherConfig config, Action<string> log)
    {
        string appId = string.IsNullOrWhiteSpace(config.SteamAppId) ? LauncherPlatform.SteamAppId : config.SteamAppId.Trim();
        string options = string.IsNullOrWhiteSpace(config.LinuxSteamLaunchOptions)
            ? LauncherPlatform.LinuxSteamLaunchOption
            : config.LinuxSteamLaunchOptions.Trim();

        string? localConfig = ResolveLocalConfig(config, log, preferAppBlock: true);
        if (localConfig is null)
        {
            throw new FileNotFoundException("Could not find Steam localconfig.vdf. The launcher cannot safely enable Linux online launch options. Set this manually in Steam: " + options);
        }

        string text = File.ReadAllText(localConfig);
        string current = GetLaunchOptions(text, appId);
        if (!config.LinuxSteamLaunchOptionsManaged)
        {
            config.PreviousLinuxSteamLaunchOptions = current;
            config.LinuxSteamLocalConfigPath = localConfig;
        }

        BackupLocalConfig(localConfig);
        string updated = SetLaunchOptions(text, appId, options);
        File.WriteAllText(localConfig, updated, new UTF8Encoding(false));

        string verify = GetLaunchOptions(File.ReadAllText(localConfig), appId);
        if (!string.Equals(verify, options, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Steam launch option write verification failed. Expected: " + options + " Actual: " + verify +
                ". The game was not launched because it would likely start vanilla/offline.");
        }

        config.LinuxSteamLaunchOptionsManaged = true;
        config.LinuxSteamLocalConfigPath = localConfig;
        log("Steam localconfig selected: " + localConfig);
        log("Steam launch options set for Monsterpatch: " + options);
        log("Previous launch options saved in launcher config: " + (string.IsNullOrEmpty(config.PreviousLinuxSteamLaunchOptions) ? "<empty>" : config.PreviousLinuxSteamLaunchOptions));
        log("NOTE: If Steam was already running and the game still launches vanilla, fully exit Steam and run Online Play once more so Steam reloads localconfig.vdf.");
    }

    public static void RestorePreviousLaunchOptions(LauncherConfig config, Action<string> log)
    {
        string appId = string.IsNullOrWhiteSpace(config.SteamAppId) ? LauncherPlatform.SteamAppId : config.SteamAppId.Trim();
        string? localConfig = !string.IsNullOrWhiteSpace(config.LinuxSteamLocalConfigPath) && File.Exists(config.LinuxSteamLocalConfigPath)
            ? config.LinuxSteamLocalConfigPath
            : ResolveLocalConfig(config, log, preferAppBlock: true);

        if (localConfig is null)
        {
            log("WARNING: Could not find Steam localconfig.vdf to restore launch options.");
            return;
        }

        BackupLocalConfig(localConfig);
        string text = File.ReadAllText(localConfig);
        string restoreValue = config.PreviousLinuxSteamLaunchOptions ?? "";
        string updated = SetLaunchOptions(text, appId, restoreValue);
        File.WriteAllText(localConfig, updated, new UTF8Encoding(false));
        string verify = GetLaunchOptions(File.ReadAllText(localConfig), appId);
        log("Steam launch options restored for Monsterpatch: " + (string.IsNullOrEmpty(restoreValue) ? "<empty>" : restoreValue));
        if (!string.Equals(verify, restoreValue, StringComparison.Ordinal))
            log("WARNING: Restore verification mismatch. Current Steam launch options read back as: " + verify);
        config.LinuxSteamLaunchOptionsManaged = false;
    }

    public static string GetCurrentLaunchOptions(LauncherConfig config)
    {
        string appId = string.IsNullOrWhiteSpace(config.SteamAppId) ? LauncherPlatform.SteamAppId : config.SteamAppId.Trim();
        string? localConfig = ResolveLocalConfig(config, null, preferAppBlock: true);
        if (localConfig is null) return "";
        return GetLaunchOptions(File.ReadAllText(localConfig), appId);
    }

    private static string? ResolveLocalConfig(LauncherConfig config, Action<string>? log, bool preferAppBlock)
    {
        string appId = string.IsNullOrWhiteSpace(config.SteamAppId) ? LauncherPlatform.SteamAppId : config.SteamAppId.Trim();

        if (!string.IsNullOrWhiteSpace(config.LinuxSteamLocalConfigPath) && File.Exists(config.LinuxSteamLocalConfigPath))
        {
            try
            {
                string savedText = File.ReadAllText(config.LinuxSteamLocalConfigPath);
                if (FindVdfBlock(savedText, appId, ignoreCase: false) is not null)
                    return config.LinuxSteamLocalConfigPath;
                log?.Invoke("Saved Steam localconfig path exists, but it does not contain the Monsterpatch app block. Re-scanning userdata folders: " + config.LinuxSteamLocalConfigPath);
            }
            catch
            {
                log?.Invoke("Saved Steam localconfig path could not be read. Re-scanning userdata folders: " + config.LinuxSteamLocalConfigPath);
            }
        }

        var candidates = EnumerateLocalConfigCandidates().ToList();
        if (candidates.Count == 0)
        {
            log?.Invoke("No Steam localconfig.vdf found under standard Linux Steam userdata roots.");
            return null;
        }

        if (preferAppBlock)
        {
            foreach (string file in candidates)
            {
                try
                {
                    string text = File.ReadAllText(file);
                    if (FindVdfBlock(text, appId, ignoreCase: false) is not null)
                    {
                        log?.Invoke("Selected Steam localconfig containing Monsterpatch app block: " + file);
                        return file;
                    }
                }
                catch { }
            }
        }

        foreach (string file in candidates)
        {
            try
            {
                string text = File.ReadAllText(file);
                if (FindVdfBlock(text, "apps", ignoreCase: true) is not null)
                {
                    log?.Invoke("Selected Steam localconfig containing apps block: " + file);
                    return file;
                }
            }
            catch { }
        }

        log?.Invoke("Selected first Steam localconfig candidate: " + candidates[0]);
        return candidates[0];
    }

    private static IEnumerable<string> EnumerateLocalConfigCandidates()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[]
        {
            Path.Combine(home, ".local", "share", "Steam", "userdata"),
            Path.Combine(home, ".steam", "steam", "userdata"),
            "/home/deck/.local/share/Steam/userdata"
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots.Where(Directory.Exists))
        {
            foreach (string file in Directory.EnumerateFiles(root, "localconfig.vdf", SearchOption.AllDirectories))
            {
                string full = Path.GetFullPath(file);
                if (seen.Add(full))
                    yield return full;
            }
        }
    }

    private static void BackupLocalConfig(string localConfig)
    {
        string dir = Path.Combine(LauncherConfigService.ConfigDirectory, "SteamLaunchOptionsBackups");
        Directory.CreateDirectory(dir);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        string backup = Path.Combine(dir, "localconfig_" + stamp + ".vdf");
        File.Copy(localConfig, backup, overwrite: false);
    }

    private static string GetLaunchOptions(string text, string appId)
    {
        var block = FindVdfBlock(text, appId, ignoreCase: false);
        if (block is null) return "";
        var m = Regex.Match(block.Value.BlockText, "\\\"LaunchOptions\\\"\\s+\\\"(?<value>(?:\\\\.|[^\\\"])*)\\\"", RegexOptions.IgnoreCase);
        return m.Success ? UnescapeVdf(m.Groups["value"].Value) : "";
    }

    private static string SetLaunchOptions(string text, string appId, string value)
    {
        var appBlock = FindVdfBlock(text, appId, ignoreCase: false);
        string escaped = EscapeVdf(value ?? "");
        if (appBlock is not null)
        {
            string blockText = appBlock.Value.BlockText;
            string newBlock;
            if (Regex.IsMatch(blockText, "\\\"LaunchOptions\\\"\\s+\\\"(?:\\\\.|[^\\\"])*\\\"", RegexOptions.IgnoreCase))
            {
                newBlock = Regex.Replace(
                    blockText,
                    "\\\"LaunchOptions\\\"\\s+\\\"(?:\\\\.|[^\\\"])*\\\"",
                    "\"LaunchOptions\"\t\t\"" + escaped + "\"",
                    RegexOptions.IgnoreCase);
            }
            else
            {
                int insert = blockText.LastIndexOf('}');
                newBlock = blockText.Insert(insert, "\t\t\"LaunchOptions\"\t\t\"" + escaped + "\"\n");
            }
            return text[..appBlock.Value.Start] + newBlock + text[appBlock.Value.End..];
        }

        string appText = "\t\t\"" + appId + "\"\n\t\t{\n\t\t\t\"LaunchOptions\"\t\t\"" + escaped + "\"\n\t\t}\n";

        // Steam stores per-app launch options under a lower-case \"apps\" block in localconfig.vdf.
        // Older launcher drafts looked only for capital \"Apps\", which can append an ignored top-level block and launch vanilla.
        var appsBlock = FindVdfBlock(text, "apps", ignoreCase: true);
        if (appsBlock is not null)
        {
            int insert = appsBlock.Value.End - 1;
            return text[..insert] + appText + text[insert..];
        }

        // If the apps block is absent, place it inside the Steam block when possible.
        string appsText = "\t\"apps\"\n\t{\n" + appText + "\t}\n";
        var steamBlock = FindVdfBlock(text, "Steam", ignoreCase: true);
        if (steamBlock is not null)
        {
            int insert = steamBlock.Value.End - 1;
            return text[..insert] + appsText + text[insert..];
        }

        // Last resort: append a valid apps block rather than the old capital Apps block.
        return text + "\n\"apps\"\n{\n" + appText + "}\n";
    }

    private static (int Start, int End, string BlockText)? FindVdfBlock(string text, string key, bool ignoreCase)
    {
        var options = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var m = Regex.Match(text, "\\\"" + Regex.Escape(key) + "\\\"", options);
        while (m.Success)
        {
            int brace = text.IndexOf('{', m.Index + m.Length);
            if (brace < 0)
            {
                m = m.NextMatch();
                continue;
            }

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int i = brace; i < text.Length; i++)
            {
                char ch = text[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (ch == '\\') escaped = true;
                    else if (ch == '\"') inString = false;
                    continue;
                }

                if (ch == '\"') inString = true;
                else if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        int start = m.Index;
                        int end = i + 1;
                        return (start, end, text[start..end]);
                    }
                }
            }

            m = m.NextMatch();
        }
        return null;
    }

    private static string EscapeVdf(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string UnescapeVdf(string value)
    {
        var sb = new StringBuilder();
        bool escaped = false;
        foreach (char ch in value)
        {
            if (escaped)
            {
                sb.Append(ch);
                escaped = false;
            }
            else if (ch == '\\')
            {
                escaped = true;
            }
            else
            {
                sb.Append(ch);
            }
        }
        if (escaped) sb.Append('\\');
        return sb.ToString();
    }
}
