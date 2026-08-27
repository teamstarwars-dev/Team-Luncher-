using System.IO.Compression;

namespace TeamLauncher;

/// <summary>Sauvegardes automatiques des mondes (saves) d'une instance, avec restauration.</summary>
public static class BackupService
{
    private const int MaxBackups = 10;

    public static string BackupDir(string instanceId) =>
        Path.Combine(DataStore.InstancesRoot, instanceId, "backups");

    /// <summary>Zippe le dossier saves de l'instance. Retourne le chemin ou "" si rien à sauvegarder.</summary>
    public static string Create(string instanceId)
    {
        string saves = Path.Combine(DataStore.InstancesRoot, instanceId, "saves");
        if (!Directory.Exists(saves) || !Directory.EnumerateFileSystemEntries(saves).Any())
            return "";

        string dir = BackupDir(instanceId);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"mondes-{DateTime.Now:yyyy-MM-dd_HH-mm}.zip");
        if (File.Exists(path)) File.Delete(path);
        ZipFile.CreateFromDirectory(saves, path, CompressionLevel.Optimal, false);

        // garde uniquement les N plus récentes
        var old = Directory.GetFiles(dir, "*.zip")
            .OrderByDescending(File.GetLastWriteTime)
            .Skip(MaxBackups);
        foreach (var f in old) { try { File.Delete(f); } catch { } }
        return path;
    }

    public static List<(string File, DateTime Date)> List(string instanceId)
    {
        string dir = BackupDir(instanceId);
        if (!Directory.Exists(dir)) return new List<(string, DateTime)>();
        return Directory.GetFiles(dir, "*.zip")
            .Select(f => (f, File.GetLastWriteTime(f)))
            .OrderByDescending(x => x.Item2)
            .ToList();
    }

    /// <summary>Restaure une sauvegarde : remplace le contenu du dossier saves.</summary>
    public static void Restore(string instanceId, string zipPath)
    {
        string saves = Path.Combine(DataStore.InstancesRoot, instanceId, "saves");
        string temp = Path.Combine(Path.GetTempPath(), "TeamLauncher-restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(zipPath, temp);
            if (Directory.Exists(saves)) Directory.Delete(saves, true);
            Directory.CreateDirectory(Path.GetDirectoryName(saves)!);
            Directory.Move(temp, saves);
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
        }
    }

    public static void Delete(string zipPath) => File.Delete(zipPath);
}
