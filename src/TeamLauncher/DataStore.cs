using System.Reflection;
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

    private static bool _dirty;
    private static System.Threading.Timer? _saveTimer;

    /// <summary>Paramètres par défaut embarqués dans l'exe (default.env).</summary>
    private static readonly Dictionary<string, string> Defaults = new(StringComparer.OrdinalIgnoreCase);

    public static void Load()
    {
        LoadDefaults();

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

        // Appliquer les valeurs par défaut embarquées si le champ est vide
        ApplyDefaults();

        Directory.CreateDirectory(Settings.InstancesDir);
        Directory.CreateDirectory(SkinsDir);
        Directory.CreateDirectory(ImagesDir);
    }

    /// <summary>
    /// Charge les paramètres par défaut depuis le fichier default.env embarqué dans l'exe.
    /// Format : CLE=valeur (une par ligne, # = commentaire).
    /// </summary>
    private static void LoadDefaults()
    {
        Defaults.Clear();
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("TeamLauncher.default.env");
            if (stream == null) return;

            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();
                Defaults[key] = value;
            }
        }
        catch { }
    }

    /// <summary>Applique les defaults embarqués sur les champs vides de Settings.</summary>
    private static void ApplyDefaults()
    {
        if (string.IsNullOrEmpty(Settings.DiscordAppId) && Defaults.TryGetValue("DISCORD_APP_ID", out var appId))
            Settings.DiscordAppId = appId;

        if (!Settings.DiscordEnabled && Defaults.TryGetValue("DISCORD_ENABLED", out var discEnabled))
            Settings.DiscordEnabled = discEnabled.Equals("true", StringComparison.OrdinalIgnoreCase);

        if (!Settings.TelemetryEnabled && Defaults.TryGetValue("TELEMETRY_ENABLED", out var telEnabled))
            Settings.TelemetryEnabled = telEnabled.Equals("true", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(Settings.DiscordTelemetryWebhook) && Defaults.TryGetValue("DISCORD_TELEMETRY_WEBHOOK", out var webhook))
            Settings.DiscordTelemetryWebhook = webhook;

        if (string.IsNullOrEmpty(Settings.UpdateUrl) && Defaults.TryGetValue("UPDATE_URL", out var updateUrl))
            Settings.UpdateUrl = updateUrl;

        if (string.IsNullOrEmpty(Settings.Language) && Defaults.TryGetValue("LANGUAGE", out var lang))
            Settings.Language = lang;

        if (string.IsNullOrEmpty(Settings.CurseForgeApiKey) && Defaults.TryGetValue("CURSEFORGE_API_KEY", out var cfKey))
            Settings.CurseForgeApiKey = cfKey;

        if (!Settings.FpsCounterEnabled && Defaults.TryGetValue("FPS_COUNTER_ENABLED", out var fpsEnabled))
            Settings.FpsCounterEnabled = fpsEnabled.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public static void Save()
    {
        _dirty = true;
        _saveTimer ??= new System.Threading.Timer(_ =>
        {
            if (!_dirty) return;
            _dirty = false;
            DoSave();
        }, null, 500, System.Threading.Timeout.Infinite);
        _saveTimer.Change(500, System.Threading.Timeout.Infinite);
    }

    /// <summary>Écriture immédiate (utiliser à la fermeture de l'app).</summary>
    public static void SaveNow()
    {
        _dirty = false;
        _saveTimer?.Dispose();
        _saveTimer = null;
        DoSave();
    }

    private static void DoSave()
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
