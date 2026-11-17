using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace MMOnsterpatch.RepackManager;

public sealed class ManifestFileRow
{
    public string Path { get; set; } = "";
    public string Version { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class PatchManifest
{
    public int ManifestVersion { get; set; } = 1;
    public string Channel { get; set; } = "release";
    public string PatchVersion { get; set; } = "0.12.4";
    public string RequiredGameBuild { get; set; } = "0.182";
    public string MinimumLauncherVersion { get; set; } = "0.12.4";
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public string Notes { get; set; } = "";
    public List<PatchManifestFile> Files { get; set; } = new();
}

public sealed class PatchManifestFile
{
    public string Path { get; set; } = "";
    public string Version { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public bool Required { get; set; } = true;
    public string Url { get; set; } = "";
}

public static class PatchManifestService
{
    public static ObservableCollection<ManifestFileRow> ScanFiles(string payloadRoot)
    {
        var rows = new ObservableCollection<ManifestFileRow>();
        string filesRoot = System.IO.Path.Combine(payloadRoot, "files");

        if (!Directory.Exists(filesRoot))
            return rows;

        foreach (string file in Directory.GetFiles(filesRoot, "*", SearchOption.AllDirectories)
                     .Where(f => ShouldIncludePayloadFile(f)))
        {
            string rel = System.IO.Path.GetRelativePath(filesRoot, file).Replace('\\', '/');
            rows.Add(new ManifestFileRow
            {
                Path = rel,
                Version = TryReadVersion(file),
                Size = new FileInfo(file).Length,
                Sha256 = ComputeSha256(file)
            });
        }

        return rows;
    }

    public static void BuildManifest(string payloadRoot, string channel, string patchVersion, string requiredGameBuild, string minLauncher, string notes, IEnumerable<ManifestFileRow> rows)
    {
        Directory.CreateDirectory(payloadRoot);

        var manifest = new PatchManifest
        {
            ManifestVersion = 1,
            Channel = channel,
            PatchVersion = patchVersion,
            RequiredGameBuild = requiredGameBuild,
            MinimumLauncherVersion = minLauncher,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            Notes = notes,
            Files = rows.Select(r => new PatchManifestFile
            {
                Path = r.Path,
                Version = r.Version,
                Size = r.Size,
                Sha256 = r.Sha256,
                Required = true,
                Url = $"patch/files/{r.Path}"
            }).ToList()
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        File.WriteAllText(System.IO.Path.Combine(payloadRoot, "manifest.json"), JsonSerializer.Serialize(manifest, options));
    }

    public static void BackupManifest(string payloadRoot)
    {
        string manifest = System.IO.Path.Combine(payloadRoot, "manifest.json");
        if (!File.Exists(manifest))
            return;

        string backupDir = System.IO.Path.Combine(payloadRoot, "manifest_backups");
        Directory.CreateDirectory(backupDir);

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        File.Copy(manifest, System.IO.Path.Combine(backupDir, $"manifest_{stamp}.json"), overwrite: true);
    }


    private static bool ShouldIncludePayloadFile(string file)
    {
        string name = System.IO.Path.GetFileName(file);

        if (string.Equals(name, "_KEEP_FOLDER.txt", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(name, ".gitkeep", StringComparison.OrdinalIgnoreCase))
            return false;

        if (name.StartsWith("PUT_", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static string ComputeSha256(string file)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string TryReadVersion(string file)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(file);
            return info.ProductVersion ?? info.FileVersion ?? "";
        }
        catch
        {
            return "";
        }
    }
}
