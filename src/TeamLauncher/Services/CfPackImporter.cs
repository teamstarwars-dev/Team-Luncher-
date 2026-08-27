using System.IO.Compression;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Import des modpacks CurseForge (.zip export du site / app) :
/// extraction du dossier overrides + téléchargement des fichiers
/// listés dans manifest.json (projectID/fileID) vers une nouvelle instance.
/// </summary>
public static class CfPackImporter
{
    public static async Task<InstanceInfo> ImportAsync(string zipPath,
        Action<string> progress, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        var manifestEntry = zip.GetEntry("manifest.json")
            ?? throw new Exception("Ce fichier n'est pas un modpack CurseForge valide (manifest.json absent).");
        string manifestJson;
        using (var sr = new StreamReader(manifestEntry.Open()))
            manifestJson = await sr.ReadToEndAsync();
        using var manifest = JsonDocument.Parse(manifestJson);
        var root = manifest.RootElement;

        string name = root.TryGetProperty("name", out var n) && !string.IsNullOrWhiteSpace(n.GetString())
            ? n.GetString()! : Path.GetFileNameWithoutExtension(zipPath);
        string mcVersion = "";
        string loader = "Vanilla";
        if (root.TryGetProperty("minecraft", out var mc))
        {
            if (mc.TryGetProperty("version", out var v)) mcVersion = v.GetString() ?? "";
            if (mc.TryGetProperty("modLoaders", out var mls))
            {
                foreach (var ml in mls.EnumerateArray())
                {
                    string id = ml.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                    if (id.StartsWith("forge-")) { loader = "Forge"; break; }
                    if (id.StartsWith("neoforge-")) { loader = "NeoForge"; break; }
                    if (id.StartsWith("fabric-")) { loader = "Fabric"; }
                }
            }
        }

        var inst = new InstanceInfo
        {
            Name = name,
            Description = $"Modpack CurseForge ({loader} {mcVersion})",
            Loader = loader,
            McVersion = mcVersion,
            ImagePath = ""
        };

        string gameDir = Path.Combine(DataStore.InstancesRoot, inst.Id);
        Directory.CreateDirectory(gameDir);

        // 1. overrides/ → racine de l'instance
        progress("Extraction des fichiers du modpack…");
        foreach (var e in zip.Entries.Where(e =>
                     e.FullName.StartsWith("overrides/", StringComparison.Ordinal) && e.Name.Length > 0))
        {
            ct.ThrowIfCancellationRequested();
            string destPath = Path.Combine(gameDir, e.FullName["overrides/".Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            e.ExtractToFile(destPath, overwrite: true);
        }

        // 2. résolution des fichiers listés (projectID + fileID)
        if (!root.TryGetProperty("files", out var files) || files.GetArrayLength() == 0)
        {
            DataStore.Settings.Instances.Add(inst);
            DataStore.Save();
            return inst;
        }

        var entries = files.EnumerateArray()
            .Select(f => (
                ProjectId: f.GetProperty("projectID").GetInt32(),
                FileId: f.GetProperty("fileID").GetInt64()))
            .ToList();

        progress($"Résolution des mods ({entries.Count} fichiers)…");
        var fileDetails = await CurseForgeApi.GetFilesByIdsAsync(entries.Select(e => e.FileId));
        var classes = await CurseForgeApi.GetProjectClassesAsync(entries.Select(e => e.ProjectId));
        var byId = fileDetails.ToDictionary(f => f.FileId);

        using var gate = new SemaphoreSlim(4);
        int done = 0;
        var tasks = entries.Select(async entry =>
        {
            await gate.WaitAsync(ct);
            try
            {
                if (!byId.TryGetValue(entry.FileId, out var file)) return; // introuvable : on saute
                classes.TryGetValue(entry.ProjectId, out int classId);
                string subDir = classId switch
                {
                    CurseForgeApi.ClassResourcePacks => "resourcepacks",
                    CurseForgeApi.ClassShaders => "shaderpacks",
                    _ => "mods"
                };
                await CurseForgeApi.DownloadFileAsync(file, Path.Combine(gameDir, subDir), null, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // un fichier manquant ne doit pas casser tout l'import
                GameLauncher.AppendLog($"modpack CF : échec du fichier {entry.FileId} : {ex.Message}");
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        foreach (var t in tasks)
        {
            await t;
            done++;
            progress($"Téléchargement des mods ({done}/{entries.Count})…");
        }

        DataStore.Settings.Instances.Add(inst);
        DataStore.Save();
        return inst;
    }
}
