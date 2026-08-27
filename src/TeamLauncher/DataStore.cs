using System.Text.Json;

namespace TeamLauncher;

public static class DataStore
{
    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TeamLauncher");

    private static string FilePath => Path.Combine(Dir, "config.json");

    public static AppSettings Settings { get; private set; } = new();

    public static string InstancesRoot => Settings.InstancesDir;
    public static string SkinsDir => Path.Combine(Dir, "skins");
    public static string ImagesDir => Path.Combine(Dir, "images");

    public static void Load()
    {
        if (!Settings.InstancesDir.Contains("TeamLauncher"))
            Settings.InstancesDir = Path.Combine(Dir, "instances");

        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (loaded != null) Settings = loaded;
            }
        }
        catch { /* config corrompue : on garde les valeurs par défaut */ }

        Directory.CreateDirectory(Settings.InstancesDir);
        Directory.CreateDirectory(SkinsDir);
        Directory.CreateDirectory(ImagesDir);
    }

    public static void Save()
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });

        // Écriture atomique : on écrit dans un .tmp puis on remplace le fichier final.
        // Ça évite les verrous parasites (antivirus, OneDrive, autre instance) et garantit
        // qu'on ne se retrouve jamais avec un fichier config.json corrompu à mi-écriture.
        var tmp = FilePath + ".tmp";
        const int maxAttempts = 6;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sw = new StreamWriter(fs))
                {
                    sw.Write(json);
                }
                // File.Move avec overwrite=true remplace la cible atomiquement
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                // Fichier temporaire encore verrouillé par l'ancienne instance ?
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                Thread.Sleep(150);
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                Thread.Sleep(150);
            }
        }

        // Dernier recours : si tout échoue, on retente un WriteAllText simple (meilleur comportement
        // pour un fichier non-verrouillé que l'utilisateur vient de fermer).
        File.WriteAllText(FilePath, json);
    }
}
