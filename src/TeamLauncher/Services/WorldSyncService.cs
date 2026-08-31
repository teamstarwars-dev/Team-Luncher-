using System.IO.Compression;

namespace TeamLauncher;

/// <summary>
/// Synchronisation des mondes entre CurseForge et Team Launcher.
/// Détecte les mondes plus récents côté CurseForge et permet de les
/// importer dans l'instance équivalente du launcher (par nom de pack).
/// </summary>
public static class WorldSyncService
{
    /// <summary>Un monde trouvé, avec sa source (CurseForge ou Team Launcher) et sa date.</summary>
    public record WorldSnapshot(
        string InstanceName,       // nom du pack / dossier parent
        string WorldName,          // nom du dossier du monde
        string DisplayName,        // nom affiché (LevelName ou WorldName)
        string FullPath,           // chemin complet du dossier du monde
        string Origin,             // "CurseForge" ou "Launcher"
        DateTime LastModified,     // date du fichier (modifs en jeu)
        DateTime? LevelLastPlayed, // date du level.dat si lisible
        long SizeBytes);

    /// <summary>Résultat de comparaison entre une instance CF et une instance launcher.</summary>
    public record CompareResult(
        InstanceInfo? LauncherInstance,
        string CurseForgeInstancePath,
        string CurseForgeInstanceName,
        List<WorldSnapshot> NewerWorlds,        // mondes plus récents côté CF
        List<WorldSnapshot> OnlyInLauncher,    // mondes absents côté CF
        List<WorldSnapshot> OnlyInCurseForge); // mondes absents côté launcher

    /// <summary>
    /// Compare toutes les instances CurseForge détectées avec les instances du launcher
    /// qui ont le même nom, et retourne la liste des mondes plus récents côté CurseForge
    /// (donc candidats à un import).
    /// </summary>
    public static List<WorldSnapshot> DetectNewerWorldsFromCurseForge()
    {
        var results = new List<WorldSnapshot>();
        foreach (var res in CompareAll())
        {
            // mondes plus récents côté CF qui ne sont pas déjà présents dans l'instance launcher
            foreach (var w in res.NewerWorlds)
            {
                bool alreadyPresent = false;
                if (res.LauncherInstance != null)
                {
                    string launcherSaves = Path.Combine(DataStore.InstancesRoot, res.LauncherInstance.Id, "saves", w.WorldName);
                    if (Directory.Exists(launcherSaves)) alreadyPresent = true;
                }
                if (!alreadyPresent) results.Add(w);
            }
        }
        return results;
    }

    /// <summary>Compare toutes les instances CurseForge avec leurs équivalentes launcher (par nom).</summary>
    public static List<CompareResult> CompareAll()
    {
        var cfInstances = InstanceTools.DetectCurseForgeInstances();
        var results = new List<CompareResult>();
        foreach (var (cfPath, cfName) in cfInstances)
        {
            var launcherInst = DataStore.Settings.Instances
                .FirstOrDefault(i => string.Equals(i.Name, cfName, StringComparison.OrdinalIgnoreCase));

            var cfWorlds = ListWorldsInInstance(cfPath, "CurseForge");
            var launcherWorlds = launcherInst == null
                ? new List<WorldSnapshot>()
                : ListWorldsInInstance(Path.Combine(DataStore.InstancesRoot, launcherInst.Id), "Launcher");

            // Worlds présents des deux côtés : on compare la date.
            // Si CF est plus récent, on signale "à importer".
            var newerWorlds = new List<WorldSnapshot>();
            foreach (var cfw in cfWorlds)
            {
                var match = launcherWorlds.FirstOrDefault(lw =>
                    string.Equals(lw.WorldName, cfw.WorldName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(lw.DisplayName, cfw.DisplayName, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    // monde présent uniquement côté CF
                    // On le met dans NewerWorlds pour proposer l'import (il est nouveau pour le launcher)
                    newerWorlds.Add(cfw);
                }
                else if (cfw.LastModified > match.LastModified)
                {
                    newerWorlds.Add(cfw);
                }
            }

            var onlyInLauncher = launcherWorlds
                .Where(lw => !cfWorlds.Any(cfw =>
                    string.Equals(cfw.WorldName, lw.WorldName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cfw.DisplayName, lw.DisplayName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var onlyInCurseForge = cfWorlds
                .Where(cfw => !launcherWorlds.Any(lw =>
                    string.Equals(cfw.WorldName, lw.WorldName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cfw.DisplayName, lw.DisplayName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            results.Add(new CompareResult(launcherInst, cfPath, cfName,
                newerWorlds, onlyInLauncher, onlyInCurseForge));
        }
        return results;
    }

    /// <summary>
    /// Liste tous les mondes Minecraft d'une instance (dossier 'saves' direct ou
    /// 'minecraft/saves' selon que le launcher cible est CurseForge ou Launcher).
    /// </summary>
    private static List<WorldSnapshot> ListWorldsInInstance(string instanceRoot, string origin)
    {
        var list = new List<WorldSnapshot>();
        if (!Directory.Exists(instanceRoot)) return list;

        // CurseForge stocke tout dans instanceRoot/minecraft/saves
        // Team Launcher stocke directement dans instanceRoot/saves
        string[] candidates =
        {
            Path.Combine(instanceRoot, "minecraft", "saves"),
            Path.Combine(instanceRoot, "saves")
        };

        foreach (var savesDir in candidates)
        {
            if (!Directory.Exists(savesDir)) continue;
            foreach (var worldDir in Directory.GetDirectories(savesDir))
            {
                // un monde Minecraft a forcément level.dat
                if (!File.Exists(Path.Combine(worldDir, "level.dat"))) continue;

                var (name, played) = WorldTools.ReadLevelDat(worldDir);
                var info = new DirectoryInfo(worldDir);
                long size = info.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

                list.Add(new WorldSnapshot(
                    InstanceName: Path.GetFileName(instanceRoot),
                    WorldName: Path.GetFileName(worldDir),
                    DisplayName: name ?? Path.GetFileName(worldDir),
                    FullPath: worldDir,
                    Origin: origin,
                    LastModified: info.LastWriteTime,
                    LevelLastPlayed: played,
                    SizeBytes: size));
            }
        }
        return list;
    }

    /// <summary>
    /// Importe un monde CurseForge dans une instance du launcher.
    /// Crée un backup du monde existant côté launcher (s'il existe) puis copie
    /// le contenu du dossier du monde.
    /// </summary>
    public static string ImportWorld(WorldSnapshot world, InstanceInfo targetInstance)
    {
        if (!Directory.Exists(world.FullPath))
            throw new DirectoryNotFoundException("Dossier source introuvable : " + world.FullPath);

        string savesDir = Path.Combine(DataStore.InstancesRoot, targetInstance.Id, "saves");
        string targetDir = Path.Combine(savesDir, world.WorldName);

        // Backup du monde existant côté launcher avant d'écraser
        if (Directory.Exists(targetDir))
        {
            string backupZip = Path.Combine(savesDir,
                $"_backup_{world.WorldName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            try
            {
                if (File.Exists(backupZip)) File.Delete(backupZip);
                ZipFile.CreateFromDirectory(targetDir, backupZip, CompressionLevel.Fastest, false);
            }
            catch { /* pas bloquant */ }
            try { Directory.Delete(targetDir, true); } catch { }
        }

        // Copie du monde CurseForge vers le launcher
        Directory.CreateDirectory(savesDir);
        CopyDirectory(world.FullPath, targetDir);

        // Marque la date du launcher comme étant celle du monde CurseForge (pour la synchro)
        try { Directory.SetLastWriteTime(targetDir, world.LastModified); } catch { }

        return targetDir;
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(src, file);
            string targetFile = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }
}
