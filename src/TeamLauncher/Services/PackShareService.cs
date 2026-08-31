using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeamLauncher;

/// <summary>
/// Partage complet d'instance entre membres de la team :
/// - Code texte (Discord) : mods + shaders résolus sur Modrinth
/// - Export .zip : tout le modpack (mods + shaders + configs + saves + resourcepacks)
/// </summary>
public static class PackShareService
{
    public sealed record SharedItem(string ProjectId, string Filename, string Url, string Sha1, long Size);
    public sealed record SharedPack(
        string Format,
        string Name,
        string Description,
        string Loader,
        string McVersion,
        List<SharedItem> Mods,
        List<SharedItem> Shaders,
        List<SharedFileInfo> ResourcePacks,
        List<SharedFileInfo> Configs,
        List<SharedFileInfo> Worlds);

    public sealed record SharedFileInfo(string Path, string Sha1, long Size);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private const string FormatId = "teamlauncher-pack-v2";

    // ======================== EXPORT ========================

    /// <summary>Export complet : résout mods + shaders sur Modrinth, liste configs/saves/resourcepacks.</summary>
    public static async Task<(SharedPack Pack, int RecognizedMods, int RecognizedShaders)> ExportAsync(
        InstanceInfo inst, Action<string> progress, CancellationToken ct = default)
    {
        string instDir = Path.Combine(DataStore.InstancesRoot, inst.Id);

        // ---- Mods ----
        progress("Analyse des mods…");
        var mods = await ScanAndResolveAsync(instDir, "mods", "*.jar", ct, progress);

        // ---- Shaders ----
        progress("Analyse des shaders…");
        var shaders = await ScanAndResolveAsync(instDir, "shaderpacks", "*.zip", ct, progress);

        // ---- Resource Packs ----
        progress("Analyse des resource packs…");
        var resourcePacks = ScanFiles(instDir, "resourcepacks", "*.*");

        // ---- Configs ----
        progress("Analyse des configs…");
        var configs = ScanFiles(instDir, "config", "*.*");

        // ---- Worlds / Saves ----
        progress("Analyse des mondes…");
        var worlds = ScanFiles(instDir, "saves", "*.*", recursive: true);

        int recognizedMods = mods.Count(m => m.Url.Length > 0);
        int recognizedShaders = shaders.Count(s => s.Url.Length > 0);

        var pack = new SharedPack(
            FormatId, inst.Name, inst.Description,
            inst.Loader, inst.McVersion,
            mods, shaders, resourcePacks, configs, worlds);

        return (pack, recognizedMods, recognizedShaders);
    }

    /// <summary>Crée un .zip complet du modpack pour partage direct.</summary>
    public static async Task ExportZipAsync(InstanceInfo inst, string zipPath,
        Action<string>? progress = null, CancellationToken ct = default)
    {
        string instDir = Path.Combine(DataStore.InstancesRoot, inst.Id);
        if (!Directory.Exists(instDir))
            throw new Exception("Dossier d'instance introuvable.");

        progress?.Invoke("Création de l'archive…");

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create, Encoding.UTF8);

        // Copier chaque dossier important
        string[] dirsToInclude = { "mods", "shaderpacks", "config", "saves", "resourcepacks" };
        foreach (var dir in dirsToInclude)
        {
            string fullPath = Path.Combine(instDir, dir);
            if (!Directory.Exists(fullPath)) continue;

            ct.ThrowIfCancellationRequested();
            progress?.Invoke($"Ajout de {dir}…");

            foreach (var file in Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                string entryName = Path.GetRelativePath(instDir, file).Replace('\\', '/');
                zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
            }
        }

        // Ajouter les fichiers racine (server.properties, etc.)
        foreach (var f in Directory.GetFiles(instDir, "*.*"))
        {
            string name = Path.GetFileName(f);
            if (name is "manifest.json" or "instance.json" or ".gitignore") continue;
            zip.CreateEntryFromFile(f, name, CompressionLevel.Optimal);
        }

        // Manifest du pack
        var manifest = new
        {
            format = FormatId,
            name = inst.Name,
            description = inst.Description,
            loader = inst.Loader,
            mcVersion = inst.McVersion,
            exportedAt = DateTime.UtcNow.ToString("o")
        };
        var manifestEntry = zip.CreateEntry("teamlauncher-pack.json");
        using (var writer = new StreamWriter(manifestEntry.Open()))
            await writer.WriteAsync(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        progress?.Invoke($"Archive créée : {Path.GetFileName(zipPath)}");
    }

    /// <summary>Importe un .zip de modpack.</summary>
    public static async Task<InstanceInfo> ImportZipAsync(string zipPath, Action<string> progress,
        CancellationToken ct = default)
    {
        if (!File.Exists(zipPath))
            throw new Exception("Fichier zip introuvable.");

        progress("Lecture de l'archive…");

        // Lire le manifest si présent
        string packName = Path.GetFileNameWithoutExtension(zipPath);
        string packLoader = "Vanilla", packVersion = "latest";

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var manifestEntry = zip.GetEntry("teamlauncher-pack.json");
            if (manifestEntry != null)
            {
                using var reader = new StreamReader(manifestEntry.Open());
                var doc = JsonDocument.Parse(await reader.ReadToEndAsync());
                if (doc.RootElement.TryGetProperty("name", out var n)) packName = n.GetString() ?? packName;
                if (doc.RootElement.TryGetProperty("loader", out var l)) packLoader = l.GetString() ?? packLoader;
                if (doc.RootElement.TryGetProperty("mcVersion", out var v)) packVersion = v.GetString() ?? packVersion;
            }
        }

        var inst = new InstanceInfo
        {
            Name = packName,
            Description = $"Pack importé ({packLoader} {packVersion})",
            Loader = packLoader,
            McVersion = packVersion
        };

        string instDir = Path.Combine(DataStore.InstancesRoot, inst.Id);
        progress("Extraction des fichiers…");

        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, instDir, overwriteFiles: true));

        DataStore.Settings.Instances.Add(inst);
        DataStore.Save();
        return inst;
    }

    // ======================== IMPORT (texte / JSON) ========================

    /// <summary>Importe depuis un JSON partagé (code texte Discord).</summary>
    public static async Task<InstanceInfo> ImportAsync(string json, Action<string> progress,
        CancellationToken ct = default)
    {
        SharedPack pack;
        try
        {
            pack = JsonSerializer.Deserialize<SharedPack>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException();
        }
        catch
        {
            throw new Exception("Ce texte n'est pas un pack Team Launcher valide.");
        }

        if (pack.Mods.Count == 0 && pack.Shaders.Count == 0)
            throw new Exception("Ce pack ne contient ni mod ni shader.");

        var inst = new InstanceInfo
        {
            Name = string.IsNullOrWhiteSpace(pack.Name) ? "Pack partagé" : pack.Name,
            Description = string.IsNullOrWhiteSpace(pack.Description)
                ? $"Pack partagé ({pack.Loader} {pack.McVersion})" : pack.Description,
            Loader = string.IsNullOrWhiteSpace(pack.Loader) ? "Vanilla" : pack.Loader,
            McVersion = string.IsNullOrWhiteSpace(pack.McVersion) ? "latest" : pack.McVersion
        };

        string instDir = Path.Combine(DataStore.InstancesRoot, inst.Id);
        int failed = 0;

        // ---- Mods ----
        if (pack.Mods.Count > 0)
        {
            string modsDir = Path.Combine(instDir, "mods");
            Directory.CreateDirectory(modsDir);
            failed += await DownloadItemsAsync(pack.Mods, modsDir, "mods", ct, progress);
        }

        // ---- Shaders ----
        if (pack.Shaders.Count > 0)
        {
            string shadersDir = Path.Combine(instDir, "shaderpacks");
            Directory.CreateDirectory(shadersDir);
            failed += await DownloadItemsAsync(pack.Shaders, shadersDir, "shaders", ct, progress);
        }

        if (failed > 0)
            Notifier.Show("Pack importé", $"{failed} fichier(s) non téléchargé(s).");

        DataStore.Settings.Instances.Add(inst);
        DataStore.Save();
        return inst;
    }

    // ======================== HELPERS ========================

    private static async Task<List<SharedItem>> ScanAndResolveAsync(
        string instDir, string subDir, string pattern,
        CancellationToken ct, Action<string> progress)
    {
        string fullPath = Path.Combine(instDir, subDir);
        if (!Directory.Exists(fullPath)) return new();

        var files = Directory.GetFiles(fullPath, pattern);
        if (files.Length == 0) return new();

        // 1. Hash SHA1 de chaque fichier
        var hashes = new Dictionary<string, string>(); // sha1 -> filepath
        for (int i = 0; i < files.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress($"Hash {subDir} ({i + 1}/{files.Length})…");
            string sha1 = Convert.ToHexString(await SHA1.HashDataAsync(File.OpenRead(files[i]))).ToLowerInvariant();
            hashes[sha1] = files[i];
        }

        // 2. Résolution groupée sur Modrinth
        progress($"Recherche {subDir} sur Modrinth…");
        var byHash = new Dictionary<string, JsonElement>();
        try
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(new { hashes = hashes.Keys.ToArray(), algorithm = "sha1" }),
                Encoding.UTF8, "application/json");
            var resp = await Http.PostAsync("https://api.modrinth.com/v2/version_files", content, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            foreach (var p in doc.RootElement.EnumerateObject())
                byHash[p.Name.ToLowerInvariant()] = p.Value.Clone();
        }
        catch { }

        // 3. Construire la liste
        var items = new List<SharedItem>();
        foreach (var (sha1, filePath) in hashes)
        {
            string filename = Path.GetFileName(filePath);
            long size = new FileInfo(filePath).Length;

            if (!byHash.TryGetValue(sha1, out var v))
            {
                items.Add(new SharedItem("", filename, "", sha1, size));
                continue;
            }

            string url = "";
            var fileElems = v.GetProperty("files").EnumerateArray().ToList();
            foreach (var f in fileElems)
            {
                if (f.TryGetProperty("primary", out var pr) && pr.GetBoolean())
                {
                    url = f.GetProperty("url").GetString() ?? "";
                    break;
                }
            }
            if (url.Length == 0 && fileElems.Count > 0)
                url = fileElems[0].GetProperty("url").GetString() ?? "";

            items.Add(new SharedItem(v.GetProperty("project_id").GetString() ?? "", filename, url, sha1, size));
        }

        return items;
    }

    private static List<SharedFileInfo> ScanFiles(string instDir, string subDir, string pattern, bool recursive = false)
    {
        string fullPath = Path.Combine(instDir, subDir);
        if (!Directory.Exists(fullPath)) return new();

        var result = new List<SharedFileInfo>();
        var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var file in Directory.GetFiles(fullPath, pattern, searchOpt))
        {
            try
            {
                string relPath = Path.GetRelativePath(instDir, file).Replace('\\', '/');
                string sha1 = Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
                long size = new FileInfo(file).Length;
                result.Add(new SharedFileInfo(relPath, sha1, size));
            }
            catch { }
        }
        return result;
    }

    private static async Task<int> DownloadItemsAsync(List<SharedItem> items, string destDir,
        string label, CancellationToken ct, Action<string> progress)
    {
        int total = items.Count, done = 0, failed = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            progress($"Téléchargement {label} ({done}/{total})…");
            if (item.Url.Length == 0) { failed++; continue; }
            try
            {
                byte[] data = await Http.GetByteArrayAsync(item.Url, ct);
                await File.WriteAllBytesAsync(Path.Combine(destDir, item.Filename), data);
            }
            catch
            {
                failed++;
                try
                {
                    string logDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    File.AppendAllText(Path.Combine(logDir, "TeamLauncher", "launcher.log"),
                        $"[{DateTime.Now:HH:mm:ss}] partage {label} : échec de {item.Filename}\n");
                }
                catch { }
            }
        }
        if (failed == total && total > 0)
            throw new Exception($"Aucun {label} n'a pu être téléchargé (connexion ?).");
        return failed;
    }

    public static string Serialize(SharedPack pack) =>
        JsonSerializer.Serialize(pack, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
}
