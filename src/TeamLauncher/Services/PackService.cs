using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>Export/import d'instances en .zip partageable (format CurseForge-compatible : dossier overrides).</summary>
public static class PackService
{
    internal static readonly string[] Excluded =
    {
        "game-log.txt", "logs", "crash-reports", "screenshots", "${game_directory}",
        "usercache.json", "usernamecache.json", "essential"
    };

    public static void Export(InstanceInfo inst, string zipPath)
    {
        string dir = Path.Combine(DataStore.InstancesRoot, inst.Id);
        if (!Directory.Exists(dir)) throw new Exception("Dossier d'introuvable.");

        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(dir, file);
            var segments = rel.Split('\\');
            if (segments.Any(s => PackService.Excluded.Contains(s))) continue;
            zip.CreateEntryFromFile(file, "overrides/" + rel.Replace('\\', '/'), CompressionLevel.Optimal);
        }
    }

    public static InstanceInfo Import(string zipPath)
    {
        string id = Guid.NewGuid().ToString("N");
        string target = Path.Combine(DataStore.InstancesRoot, id);
        string temp = Path.Combine(Path.GetTempPath(), "TeamLauncher-import-" + id);
        if (Directory.Exists(temp)) Directory.Delete(temp, true);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, temp);

            // packs CurseForge : le contenu réel est dans overrides/
            string source = Directory.Exists(Path.Combine(temp, "overrides"))
                ? Path.Combine(temp, "overrides") : temp;

            Directory.CreateDirectory(target);
            foreach (var src in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var dst = Path.Combine(target, Path.GetRelativePath(source, src));
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }

            // détecte le loader et les mods importés
            bool forge = File.Exists(Path.Combine(target, "config", "forge.cfg"));
            string loader = forge ? "Forge" : Directory.Exists(Path.Combine(target, "mods")) ? "Fabric" : "?";

            var inst = new InstanceInfo
            {
                Id = id,
                Name = Path.GetFileNameWithoutExtension(zipPath),
                Description = "Modpack importé",
                Loader = loader,
                McVersion = "?"
            };
            DataStore.Settings.Instances.Add(inst);
            DataStore.Save();
            return inst;
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }
}

/// <summary>Services utilitaires sur les instances.</summary>
public static class InstanceTools
{
    /// <summary>Détecte les instances CurseForge installées sur le PC (dossier curseforge\minecraft\Instances).</summary>
    public static List<(string Path, string Name)> DetectCurseForgeInstances()
    {
        var list = new List<(string, string)>();
        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "curseforge", "minecraft", "Instances");
        if (!Directory.Exists(baseDir)) return list;
        foreach (var dir in Directory.GetDirectories(baseDir))
            if (!PackService.Excluded.Contains(Path.GetFileName(dir)))
                list.Add((dir, Path.GetFileName(dir)!));
        return list;
    }

    /// <summary>Copie un dossier externe vers une nouvelle instance du launcher.</summary>
    public static InstanceInfo ImportDirectory(string sourcePath, string displayName)
    {
        var inst = new InstanceInfo
        {
            Name = displayName,
            Description = "Instance importée",
            Loader = "?",
            McVersion = "?"
        };
        string target = Path.Combine(DataStore.InstancesRoot, inst.Id);
        Directory.CreateDirectory(target);

        foreach (var src in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var segments = Path.GetRelativePath(sourcePath, src).Split('\\');
            if (segments.Any(s => PackService.Excluded.Contains(s))) continue;
            var dst = Path.Combine(target, Path.GetRelativePath(sourcePath, src));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }

        // détection basique du loader
        if (File.Exists(Path.Combine(target, "config", "forge.cfg"))
            || File.Exists(Path.Combine(target, "config", "forgeClient.toml")))
            inst.Loader = "Forge";
        else if (Directory.GetFiles(target, "*.jar")
                     .Any(f => Path.GetFileName(f).Contains("fabric", StringComparison.OrdinalIgnoreCase)))
            inst.Loader = "Fabric";

        DataStore.Settings.Instances.Add(inst);
        DataStore.Save();
        return inst;
    }

    /// <summary>Copie complète d'une instance (fichiers + carte), avec un nouvel identifiant.</summary>
    public static InstanceInfo Duplicate(InstanceInfo source)
    {
        string srcDir = Path.Combine(DataStore.InstancesRoot, source.Id);
        var clone = new InstanceInfo
        {
            Name = source.Name + " (copie)",
            Description = source.Description,
            ImagePath = source.ImagePath,
            Loader = source.Loader,
            McVersion = source.McVersion,
            Launches = 0,
            PlaySeconds = 0,
            Notes = source.Notes,
            MaxRamGb = source.MaxRamGb
        };
        string dstDir = Path.Combine(DataStore.InstancesRoot, clone.Id);
        Directory.CreateDirectory(dstDir);
        foreach (var f in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            var d = Path.Combine(dstDir, Path.GetRelativePath(srcDir, f));
            Directory.CreateDirectory(Path.GetDirectoryName(d)!);
            File.Copy(f, d, overwrite: true);
        }
        DataStore.Settings.Instances.Add(clone);
        DataStore.Save();
        return clone;
    }
}
/// <summary>Mise à jour automatique des mods d'une instance via l'API Modrinth (par hash de fichier).</summary>
public static class ModUpdaterService
{
    private static readonly HttpClient Http = new();

    public static async Task<string> UpdateModsAsync(InstanceInfo inst)
    {
        string modsDir = Path.Combine(DataStore.InstancesRoot, inst.Id, "mods");
        if (!Directory.Exists(modsDir)) return "Aucun dossier mods dans cette instance.";

        var jars = Directory.GetFiles(modsDir, "*.jar");
        if (jars.Length == 0) return "Aucun mod installé.";

        string mcVersion = inst.McVersion is "latest" or "?" or "" or null
            ? await MojangApi.LatestReleaseAsync() : inst.McVersion;
        string gameLoader = inst.Loader is "Forge" or "Fabric" ? inst.Loader.ToLowerInvariant() : "forge";

        // hash de chaque mod installé
        var hashes = new Dictionary<string, string>(); // sha1 → chemin actuel
        foreach (var jar in jars)
        {
            await using var fs = File.OpenRead(jar);
            hashes.Add(Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant(), jar);
        }

        // demande à Modrinth les dernières versions compatibles pour ces fichiers exacts
        var body = JsonSerializer.Serialize(new
        {
            hashes = hashes.Keys.ToArray(),
            algorithm = "sha1",
            loaders = new[] { gameLoader },
            game_versions = new[] { mcVersion }
        });
        using var resp = await Http.PostAsync("https://api.modrinth.com/v2/version_files/update",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        int updated = 0, upToDate = 0;
        var errors = new List<string>();

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!hashes.TryGetValue(prop.Name, out var currentPath)) continue;
            var version = prop.Value;
            JsonElement primary = default;
            if (version.TryGetProperty("files", out var files))
                foreach (var f in files.EnumerateArray())
                    if (!f.TryGetProperty("primary", out var p) || p.GetBoolean()) { primary = f.Clone(); break; }
            if (primary.ValueKind == JsonValueKind.Undefined) continue;

            string newName = primary.GetProperty("filename").GetString()!;
            string currentName = Path.GetFileName(currentPath);
            if (string.Equals(newName, currentName, StringComparison.OrdinalIgnoreCase))
            {
                upToDate++;
                continue; // déjà la dernière version
            }

            try
            {
                string newPath = Path.Combine(modsDir, newName);
                using (var dl = await Http.GetAsync(primary.GetProperty("url").GetString()!,
                           HttpCompletionOption.ResponseHeadersRead))
                {
                    dl.EnsureSuccessStatusCode();
                    await using var fs2 = File.Create(newPath);
                    await dl.Content.CopyToAsync(fs2);
                }
                File.Delete(currentPath); // remplace l'ancien jar
                updated++;
            }
            catch (Exception ex) { errors.Add($"{currentName} : {ex.Message}"); }
        }

        return $"Mods à jour : {upToDate}\nMis à jour : {updated}" +
               (errors.Count > 0 ? "\nErreurs :\n" + string.Join("\n", errors) : "");
    }
}

