using System.IO.Compression;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Import des modpacks Modrinth (.mrpack) :
/// extraction du dossier overrides + téléchargement des fichiers listés
/// dans modrinth.index.json vers une nouvelle instance.
/// </summary>
public static class MrPackImporter
{
    public static async Task<InstanceInfo> ImportAsync(string mrpackPath, Action<string> progress,
        CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(mrpackPath);

        var indexEntry = zip.GetEntry("modrinth.index.json")
            ?? throw new Exception("Ce fichier n'est pas un modpack Modrinth valide (modrinth.index.json absent).");
        string indexJson;
        using (var sr = new StreamReader(indexEntry.Open()))
            indexJson = await sr.ReadToEndAsync();
        using var index = JsonDocument.Parse(indexJson);
        var root = index.RootElement;

        string name = root.TryGetProperty("name", out var n) && !string.IsNullOrWhiteSpace(n.GetString())
            ? n.GetString()! : Path.GetFileNameWithoutExtension(mrpackPath);
        string gameVersion = root.TryGetProperty("gameVersion", out var gv) ? gv.GetString() ?? "" : "";
        string loader = "Vanilla";
        if (root.TryGetProperty("dependencies", out var deps))
        {
            foreach (var d in deps.EnumerateObject())
            {
                if (d.Name == "fabric-loader") { loader = "Fabric"; break; }
                if (d.Name == "forge") { loader = "Forge"; break; }
                if (d.Name == "neoforge") { loader = "NeoForge"; break; }
            }
        }

        var inst = new InstanceInfo
        {
            Name = name,
            Description = $"Modpack Modrinth ({loader} {gameVersion})",
            Loader = loader,
            McVersion = gameVersion,
            ImagePath = ""
        };

        string gameDir = Path.Combine(DataStore.InstancesRoot, inst.Id);
        Directory.CreateDirectory(gameDir);

        // 1. overrides/ → racine de l'instance
        progress("Extraction des fichiers du modpack…");
        foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("overrides/", StringComparison.Ordinal) && e.Name.Length > 0))
        {
            string destPath = Path.Combine(gameDir, e.FullName["overrides/".Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            e.ExtractToFile(destPath, overwrite: true);
        }

        // 2. téléchargement des fichiers listés (mods, shaders, config…)
        if (root.TryGetProperty("files", out var files))
        {
            int total = files.GetArrayLength(), done = 0;
            using var http = new HttpClient();
            foreach (var f in files.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                done++;
                string relPath = f.GetProperty("path").GetString()!;
                progress($"Mods et fichiers ({done}/{total})…");
                string dest = Path.Combine(gameDir, relPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                try
                {
                    string url = f.GetProperty("downloads").EnumerateArray().First().GetString()!;
                    byte[] data = await http.GetByteArrayAsync(url, ct);
                    await File.WriteAllBytesAsync(dest, data);
                }
                catch (Exception ex)
                {
                    // un fichier manquant ne doit pas casser tout l'import
                    try
                    {
                        File.AppendAllText(Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "TeamLauncher", "launcher.log"),
                            $"[{DateTime.Now:HH:mm:ss}] mrpack : échec de {relPath} : {ex.Message}\n");
                    }
                    catch { }
                }
            }
        }

        DataStore.Settings.Instances.Add(inst);
        DataStore.Save();
        return inst;
    }
}
