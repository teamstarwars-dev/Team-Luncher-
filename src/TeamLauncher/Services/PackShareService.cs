using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Partage d'instance « à la main » entre membres de la team :
/// l'export liste les mods du dossier mods avec leur empreinte SHA1, résolue
/// sur Modrinth pour retrouver l'URL officielle de chaque fichier.
/// Le JSON obtenu tient dans un message Discord et se réimporte en un clic.
/// </summary>
public static class PackShareService
{
    public sealed record SharedMod(string ProjectId, string Filename, string Url, string Sha1);
    public sealed record SharedPack(string Format, string Name, string Description,
        string Loader, string McVersion, List<SharedMod> Mods);

    private static readonly HttpClient Http = new();
    private const string FormatId = "teamlauncher-pack";

    /// <summary>Analyse une instance et produit son descriptif partageable.</summary>
    public static async Task<(SharedPack Pack, int Recognized)> ExportAsync(InstanceInfo inst,
        Action<string> progress, CancellationToken ct = default)
    {
        string modsDir = Path.Combine(DataStore.InstancesRoot, inst.Id, "mods");
        var files = Directory.Exists(modsDir)
            ? Directory.GetFiles(modsDir, "*.jar")
            : Array.Empty<string>();
        if (files.Length == 0)
            throw new Exception("Cette instance n'a aucun mod dans son dossier mods.");

        // 1. empreinte SHA1 de chaque mod
        var hashes = new Dictionary<string, string>(); // sha1 -> filename
        for (int i = 0; i < files.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress($"Analyse des mods ({i + 1}/{files.Length})…");
            hashes[Convert.ToHexString(await SHA1.HashDataAsync(File.OpenRead(files[i]))).ToLowerInvariant()]
                = Path.GetFileName(files[i]);
        }

        // 2. résolution groupée sur Modrinth (un seul appel API)
        progress("Recherche des mods sur Modrinth…");
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
        catch { /* hors ligne : les mods resteront non résolus */ }

        var mods = new List<SharedMod>();
        int recognized = 0;
        foreach (var (sha1, filename) in hashes)
        {
            if (!byHash.TryGetValue(sha1, out var v))
            {
                mods.Add(new SharedMod("", filename, "", sha1));
                continue;
            }
            recognized++;
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
            mods.Add(new SharedMod(v.GetProperty("project_id").GetString() ?? "", filename, url, sha1));
        }

        return (new SharedPack(FormatId, inst.Name, inst.Description,
            inst.Loader, inst.McVersion, mods), recognized);
    }

    /// <summary>Crée une nouvelle instance depuis un descriptif partagé.</summary>
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
        if (pack.Mods.Count == 0)
            throw new Exception("Ce pack ne contient aucun mod.");

        var inst = new InstanceInfo
        {
            Name = string.IsNullOrWhiteSpace(pack.Name) ? "Pack partagé" : pack.Name,
            Description = string.IsNullOrWhiteSpace(pack.Description)
                ? $"Pack partagé ({pack.Loader} {pack.McVersion})" : pack.Description,
            Loader = string.IsNullOrWhiteSpace(pack.Loader) ? "Vanilla" : pack.Loader,
            McVersion = string.IsNullOrWhiteSpace(pack.McVersion) ? "latest" : pack.McVersion
        };

        string modsDir = Path.Combine(DataStore.InstancesRoot, inst.Id, "mods");
        Directory.CreateDirectory(modsDir);

        int total = pack.Mods.Count, done = 0, failed = 0;
        foreach (var m in pack.Mods)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            progress($"Téléchargement des mods ({done}/{total})…");
            if (m.Url.Length == 0) { failed++; continue; }
            try
            {
                byte[] data = await Http.GetByteArrayAsync(m.Url, ct);
                await File.WriteAllBytesAsync(Path.Combine(modsDir, m.Filename), data);
            }
            catch
            {
                failed++;
                try
                {
                    File.AppendAllText(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "TeamLauncher", "launcher.log"),
                        $"[{DateTime.Now:HH:mm:ss}] partage : échec de {m.Filename}\n");
                }
                catch { }
            }
        }
        if (failed == total)
            throw new Exception("Aucun mod n'a pu être téléchargé (connexion ?).");

        DataStore.Settings.Instances.Add(inst);
        DataStore.Save();
        if (failed > 0)
            Notifier.Show("Pack importé", $"{failed} mod(s) n'ont pas pu être téléchargés.");
        return inst;
    }

    public static string Serialize(SharedPack pack) =>
        JsonSerializer.Serialize(pack, new JsonSerializerOptions { WriteIndented = true });
}
