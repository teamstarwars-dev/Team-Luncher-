using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TeamLauncher;

/// <summary>
/// Hébergement de serveurs Minecraft dédiés depuis le launcher :
/// téléchargement du serveur officiel Mojang, eula/server.properties,
/// démarrage/arrêt du process Java, import de map, console en direct.
/// </summary>
public static class ServerHost
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Process> Running = new();

    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TeamLauncher", "servers");

    public static string Dir(HostedServer s) => Path.Combine(Root, s.Id);
    public static string JarPath(HostedServer s) => Path.Combine(Dir(s), "server.jar");
    public static string WorldDir(HostedServer s) => Path.Combine(Dir(s), "world");
    public static bool IsInstalled(HostedServer s) => File.Exists(JarPath(s));
    public static bool IsRunning(HostedServer s) =>
        Running.TryGetValue(s.Id, out var p) && !p.HasExited;

    /// <summary>Ligne de console émise par un serveur : (serverId, ligne).</summary>
    public static event Action<string, string>? LogEmitted;
    public static event Action? StateChanged;
    /// <summary>Progression d'un téléchargement de serveur : (serverId, pourcentage 0-100).</summary>
    public static event Action<string, int>? DownloadProgress;

    /// <summary>Télécharge url vers dest en signalant la progression en pourcentage.</summary>
    private static async Task DownloadToFileAsync(string id, string url, string dest)
    {
        using var resp = await Http.Shared.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;
        await using var fs = File.Create(dest);
        await using var stream = await resp.Content.ReadAsStreamAsync();
        var buffer = new byte[81920];
        long done = 0;
        int lastPct = -1;
        int n;
        while ((n = await stream.ReadAsync(buffer)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, n));
            done += n;
            if (total > 0)
            {
                int pct = (int)(done * 100 / total.Value);
                if (pct != lastPct)
                {
                    lastPct = pct;
                    DownloadProgress?.Invoke(id, pct);
                }
            }
        }
        DownloadProgress?.Invoke(id, 100);
    }

    // ---------------- création ----------------

    public static async Task DownloadAsync(HostedServer s)
    {
        Directory.CreateDirectory(Dir(s));

        switch (s.Loader)
        {
            case "Fabric":
                await DownloadFabricAsync(s);
                break;
            case "Forge":
            case "NeoForge":
                await DownloadForgeLikeAsync(s);
                break;
            default:
                await DownloadVanillaAsync(s);
                break;
        }

        File.WriteAllText(Path.Combine(Dir(s), "eula.txt"),
            $"# EULA acceptée via Team Launcher le {DateTime.Now:u}\neula=true\n");
        WriteProperties(s);
        DataStore.Save();
    }

    private static async Task DownloadVanillaAsync(HostedServer s)
    {
        using var manifest = JsonDocument.Parse(await Http.Shared.GetStringAsync(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"));
        string? versionUrl = null;
        foreach (var v in manifest.RootElement.GetProperty("versions").EnumerateArray())
        {
            if (v.GetProperty("id").GetString() == s.McVersion)
            {
                versionUrl = v.GetProperty("url").GetString();
                break;
            }
        }
        if (versionUrl == null) throw new Exception("Version introuvable : " + s.McVersion);

        using var vdoc = JsonDocument.Parse(await Http.Shared.GetStringAsync(versionUrl));
        string jarUrl = vdoc.RootElement.GetProperty("downloads")
            .GetProperty("server").GetProperty("url").GetString()!;
        if (vdoc.RootElement.TryGetProperty("javaVersion", out var jv) &&
            jv.TryGetProperty("majorVersion", out var mj))
        {
            s.JavaMajor = mj.GetInt32();
        }

        await DownloadToFileAsync(s.Id, jarUrl, JarPath(s));
    }

    /// <summary>Serveur Fabric : jar exécutable « tout-en-un » fourni par meta.fabricmc.net.</summary>
    private static async Task DownloadFabricAsync(HostedServer s)
    {
        var installers = JsonDocument.Parse(
            await Http.Shared.GetStringAsync("https://meta.fabricmc.net/v2/versions/installer")).RootElement;
        string installer = installers.EnumerateArray().First().GetProperty("version").GetString()!;

        var loaders = JsonDocument.Parse(
            await Http.Shared.GetStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{s.McVersion}")).RootElement;
        string loader = loaders.EnumerateArray().First().GetProperty("loader")
            .GetProperty("version").GetString()!;

        string url = $"https://meta.fabricmc.net/v2/versions/loader/{s.McVersion}/{loader}/{installer}/server/jar";
        await DownloadToFileAsync(s.Id, url, JarPath(s));

        s.JavaMajor = EstimateJavaMajor(s.McVersion);
    }

    /// <summary>
    /// Serveurs Forge et NeoForge : téléchargement de l'installeur officiel puis
    /// exécution silencieuse (--installServer), comme le font les autres launchers.
    /// </summary>
    private static async Task DownloadForgeLikeAsync(HostedServer s)
    {
        bool neo = s.Loader == "NeoForge";
        string installerUrl;

        if (!neo)
        {
            var promo = JsonDocument.Parse(await Http.Shared.GetStringAsync(
                "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json")).RootElement;
            string key = $"{s.McVersion}-recommended";
            if (!promo.GetProperty("promos").TryGetProperty(key, out var fv))
                throw new Exception(
                    $"Pas de version Forge recommandée pour Minecraft {s.McVersion}.\n" +
                    "Choisis une autre version ou un autre modloader.");
            string forgeVer = fv.GetString()!;
            installerUrl =
                $"https://maven.minecraftforge.net/net/minecraftforge/forge/{s.McVersion}-{forgeVer}/forge-{s.McVersion}-{forgeVer}-installer.jar";
        }
        else
        {
            string prefix = NeoPrefix(s.McVersion);
            var meta = JsonDocument.Parse(await Http.Shared.GetStringAsync(
                "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge")).RootElement;
            string? ver = meta.EnumerateArray()
                .Select(x => x.GetString())
                .Where(v => v != null && v.StartsWith(prefix + "."))
                .OrderByDescending(v => int.Parse(v!.Split('.')[^1]))
                .FirstOrDefault();
            if (ver == null)
                throw new Exception($"Pas de version NeoForge pour Minecraft {s.McVersion}.");
            installerUrl =
                $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{ver}/neoforge-{ver}-installer.jar";
        }

        Emit(s.Id, "Téléchargement de l'installeur officiel…");
        string installerPath = Path.Combine(Dir(s), "installer.jar");
        await DownloadToFileAsync(s.Id, installerUrl, installerPath);

        s.JavaMajor = Math.Max(EstimateJavaMajor(s.McVersion), 8);
        string java = GameLauncher.FindJava(s.JavaMajor)
            ?? throw new Exception(
                $"Aucun Java {s.JavaMajor}+ trouvé : il faut Java pour installer {s.Loader}.\n" +
                "Installe-le depuis adoptium.net puis relance la création du serveur.");

        Emit(s.Id, $"Installation de {s.Loader} (peut prendre quelques minutes)…");
        var psi = new ProcessStartInfo
        {
            FileName = java,
            Arguments = "\"installer.jar\" --installServer",
            WorkingDirectory = Dir(s),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var p = Process.Start(psi) ?? throw new Exception("Impossible de lancer l'installeur.");
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Emit(s.Id, e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Emit(s.Id, e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync();

        try { File.Delete(installerPath); } catch { }
        if (p.ExitCode != 0)
            throw new Exception($"L'installeur {s.Loader} a échoué (code {p.ExitCode}).");
        Emit(s.Id, $"{s.Loader} installé !");
    }

    /// <summary>Java requis selon la version de Minecraft.</summary>
    private static int EstimateJavaMajor(string mcVersion) =>
        VersionUnder(mcVersion, 1, 18) ? 8 :
        VersionUnder(mcVersion, 1, 20) ? 17 : 21;

    /// <summary>Préfixe de version NeoForge pour une version de Minecraft (1.21.1 → « 21.1 »).</summary>
    private static string NeoPrefix(string mcVersion)
    {
        var m = Regex.Match(mcVersion, @"^1\.(\d+)(?:\.(\d+))?");
        int mi = m.Success ? int.Parse(m.Groups[1].Value) : 0;
        int pa = m.Success && m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        return $"{mi}.{pa}";
    }

    private static bool VersionUnder(string v, int minor, int patch) =>
        !VersionAtLeast(v, minor, patch);

    private static bool VersionAtLeast(string v, int minor, int patch = 0)
    {
        var m = Regex.Match(v, @"^1\.(\d+)(?:\.(\d+))?");
        if (!m.Success) return false;
        int mi = int.Parse(m.Groups[1].Value);
        int pa = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        if (mi != minor) return mi > minor;
        return pa >= patch;
    }

    private static void WriteProperties(HostedServer s)
    {
        bool rp = s.RpProfile;
        bool wl = rp || s.WhitelistEnabled;
        File.WriteAllText(Path.Combine(Dir(s), "server.properties"),
            "#Propriétés générées par Team Launcher\n" +
            $"server-port={s.Port}\n" +
            $"motd={s.Motd}\n" +
            "level-name=world\n" +
            $"gamemode={(rp ? "adventure" : "survival")}\n" +
            "difficulty=normal\n" +
            $"max-players={(rp ? 40 : 20)}\n" +
            "online-mode=true\n" +
            "view-distance=8\n" +
            $"white-list={(wl ? "true" : "false")}\n" +
            (wl ? "enforce-whitelist=true\n" : "") +
            (rp ? "spawn-protection=0\n" : "spawn-protection=8\n") +
            (rp ? "enable-command-block=true\n" : "enable-command-block=false\n") +
            (rp ? "function-permission-level=2\n" : ""));
    }

    /// <summary>Applique port/motd au properties si le serveur existe déjà.</summary>
    public static void ApplyProperties(HostedServer s)
    {
        if (!IsInstalled(s)) return;
        string path = Path.Combine(Dir(s), "server.properties");
        if (!File.Exists(path)) { WriteProperties(s); return; }
        var lines = File.ReadAllLines(path).ToList();
        bool wl = s.RpProfile || s.WhitelistEnabled;
        Set(lines, "server-port", s.Port.ToString());
        Set(lines, "motd", s.Motd);
        Set(lines, "white-list", wl ? "true" : "false");
        if (s.RpProfile)
        {
            Set(lines, "enforce-whitelist", "true");
            Set(lines, "enable-command-block", "true");
        }
        File.WriteAllLines(path, lines);
    }

    /// <summary>
    /// Écrit whitelist.json à partir de la liste de pseudos du serveur
    /// (UUID résolu via l'API Mojang). Les pseudos introuvables sont ignorés.
    /// </summary>
    public static async Task SyncWhitelistAsync(HostedServer s)
    {
        Directory.CreateDirectory(Dir(s));
        var entries = new List<string>();
        foreach (var name in s.Whitelist.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string? uuid = await MojangApi.GetUuidAsync(name);
            if (uuid == null) continue;
            entries.Add(JsonSerializer.Serialize(new { uuid, name }));
        }
        File.WriteAllText(Path.Combine(Dir(s), "whitelist.json"),
            "[" + string.Join(",", entries) + "]");
    }

    /// <summary>Message de bienvenue automatique quand un joueur rejoint le serveur.</summary>
    private static void DetectJoinAndWelcome(string id, string line)
    {
        try
        {
            int idx = line.IndexOf("joined the game", StringComparison.Ordinal);
            if (idx < 0) return;
            var s = DataStore.Settings.HostedServers.FirstOrDefault(h => h.Id == id);
            if (s == null) return;
            var name = line[..idx].TrimEnd().Split(' ').LastOrDefault()?.Trim() ?? "";
            if (name.Length == 0 || name.Length > 16 || !name.All(c => char.IsLetterOrDigit(c) || c == '_')) return;

            // Message de bienvenue
            if (!string.IsNullOrWhiteSpace(s.WelcomeMessage))
                SendCommand(id, "say " + s.WelcomeMessage.Replace("{joueur}", name));

            // Notification Discord
            if (!string.IsNullOrWhiteSpace(s.DiscordWebhookUrl))
                _ = SendDiscordWebhookAsync(s.DiscordWebhookUrl, $"🟢 **{name}** a rejoint **{s.Name}** !");
        }
        catch { }
    }

    /// <summary>Détection de déconnexion pour Discord.</summary>
    private static void DetectLeaveAndNotify(string id, string line)
    {
        try
        {
            int idx = line.IndexOf("left the game", StringComparison.Ordinal);
            if (idx < 0) return;
            var s = DataStore.Settings.HostedServers.FirstOrDefault(h => h.Id == id);
            if (s == null || string.IsNullOrWhiteSpace(s.DiscordWebhookUrl)) return;
            var name = line[..idx].TrimEnd().Split(' ').LastOrDefault()?.Trim() ?? "";
            if (name.Length > 0 && name.Length <= 16)
                _ = SendDiscordWebhookAsync(s.DiscordWebhookUrl, $"🔴 **{name}** a quitté **{s.Name}**.");
        }
        catch { }
    }

    /// <summary>Envoie un message via un webhook Discord.</summary>
    private static async Task SendDiscordWebhookAsync(string webhookUrl, string message)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var payload = new { content = message };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await http.PostAsync(webhookUrl, content);
        }
        catch { }
    }

    private static void Set(List<string> lines, string key, string value)
    {
        int idx = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal));
        if (idx >= 0) lines[idx] = $"{key}={value}";
        else lines.Add($"{key}={value}");
    }

    // ---------------- démarrage / arrêt ----------------

    public static void Start(HostedServer s)
    {
        if (IsRunning(s)) return;
        if (!IsInstalled(s)) throw new Exception("Serveur non téléchargé.");

        string? java = GameLauncher.FindJava(Math.Max(8, s.JavaMajor));
        if (java == null)
            throw new Exception($"Aucun Java ≥ {Math.Max(8, s.JavaMajor)} trouvé sur ce PC.");

        string arguments;
        if (s.Loader is "Forge" or "NeoForge")
        {
            // Forge/NeoForge modernes : lancement via le fichier d'arguments généré
            // par l'installeur (win_args.txt) ; anciens : jar « universal ».
            string? argsFile = Directory
                .GetFiles(Dir(s), "win_args.txt", SearchOption.AllDirectories)
                .OrderByDescending(f => f.Length) // le plus profond = la bonne version
                .FirstOrDefault();
            if (argsFile != null)
            {
                arguments = $"-Xmx{s.MaxRamGb}G -Xms512M \"@{argsFile}\" nogui";
            }
            else
            {
                string? universal = Directory.GetFiles(Dir(s), "*universal.jar")
                    .FirstOrDefault();
                if (universal == null)
                    throw new Exception(
                        $"Installation {s.Loader} incomplète : relance la création du serveur.");
                arguments = $"-Xmx{s.MaxRamGb}G -Xms512M -jar \"{Path.GetFileName(universal)}\" nogui";
            }
        }
        else
        {
            arguments = $"-Xmx{s.MaxRamGb}G -Xms512M -jar \"server.jar\" nogui";
        }

        var psi = new ProcessStartInfo
        {
            FileName = java,
            Arguments = arguments,
            WorkingDirectory = Dir(s),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
        var p = Process.Start(psi) ?? throw new Exception("Impossible de démarrer le processus Java.");
        p.StandardInput.AutoFlush = true;
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            DetectJoinAndWelcome(s.Id, e.Data);
            DetectLeaveAndNotify(s.Id, e.Data);
            Emit(s.Id, e.Data);
        };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Emit(s.Id, e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.EnableRaisingEvents = true;
        p.Exited += (_, _) =>
        {
            // Sauvegarde automatique du monde à l'arrêt du serveur
            try { BackupWorld(s); } catch { }

            if (AutoStart.TryRemove(s.Id, out _))
            {
                // redémarrage planifié : on relance directement
                Emit(s.Id, "Relance du serveur…");
                Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    try { Start(s); } catch (Exception ex) { Emit(s.Id, "Échec de relance : " + ex.Message); }
                });
            }
            else if (!ManualStop.TryRemove(s.Id, out _) && s.AutoRestart)
            {
                // arrêt inattendu : relance automatique
                Emit(s.Id, "Le serveur s'est arrêté de façon inattendue. Relance automatique dans 10 s…");
                Notifier.Show(s.Name, "Crash détecté — relance automatique dans 10 secondes…");
                Task.Run(async () =>
                {
                    await Task.Delay(10000);
                    try
                    {
                        Start(s);
                        Notifier.Show(s.Name, "Serveur relancé après le crash.");
                    }
                    catch (Exception ex) { Emit(s.Id, "Échec de relance auto : " + ex.Message); }
                });
            }

            StateChanged?.Invoke();
        };
        Running[s.Id] = p;
        EnsureScheduleWatchdog();
        StateChanged?.Invoke();
    }

    public static void Stop(HostedServer s)
    {
        if (!IsRunning(s)) return;
        ManualStop.TryAdd(s.Id, true);
        try
        {
            Running[s.Id].StandardInput.WriteLine("stop");
            Running[s.Id].StandardInput.Flush();
        }
        catch { }
    }

    /// <summary>Envoie une commande console au serveur (list, say, whitelist…).</summary>
    public static void SendCommand(string id, string command)
    {
        if (!IsRunningId(id)) return;
        try
        {
            Running[id].StandardInput.WriteLine(command);
            Running[id].StandardInput.Flush();
        }
        catch { }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> ManualStop = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> AutoStart = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> LastDailyRun = new();
    private static System.Threading.Timer? _scheduleWatchdog;

    public static bool IsRunningId(string id) =>
        Running.TryGetValue(id, out var p) && !p.HasExited;

    /// <summary>Liste des sauvegardes du monde (la plus récente en premier).</summary>
    public static List<FileInfo> ListBackups(HostedServer s)
    {
        string dir = Path.Combine(Dir(s), "backups");
        if (!Directory.Exists(dir)) return new List<FileInfo>();
        return Directory.GetFiles(dir, "world-*.zip")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();
    }

    /// <summary>Restaure une sauvegarde du monde (le serveur doit être arrêté).</summary>
    public static void RestoreBackup(HostedServer s, string zipPath)
    {
        if (IsRunning(s))
            throw new Exception("Arrête le serveur avant de restaurer une sauvegarde.");

        // le monde actuel est mis de côté, au cas où
        if (Directory.Exists(WorldDir(s)))
        {
            string keep = Path.Combine(Dir(s), "world.avant-restauration-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.Move(WorldDir(s), keep);
        }

        string temp = Path.Combine(Path.GetTempPath(), "tl-restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, temp);
            CopyDir(temp, WorldDir(s));
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    /// <summary>Chien de garde : relance après crash et applique les redémarrages planifiés.</summary>
    private static void EnsureScheduleWatchdog()
    {
        if (_scheduleWatchdog != null) return;
        _scheduleWatchdog = new System.Threading.Timer(_ =>
        {
            try
            {
                foreach (var s in DataStore.Settings.HostedServers.ToList())
                {
                    // redémarrage quotidien programmé
                    if (!string.IsNullOrWhiteSpace(s.RestartAt)
                        && TimeSpan.TryParse(s.RestartAt, out var t)
                        && IsRunning(s))
                    {
                        var now = DateTime.Now;
                        string today = now.ToString("yyyyMMdd");
                        if (now.Hour == t.Hours && now.Minute == t.Minutes
                            && (!LastDailyRun.TryGetValue(s.Id, out var last) || last != today))
                        {
                            LastDailyRun[s.Id] = today;
                            Emit(s.Id, "Redémarrage quotidien programmé…");
                            AutoStart[s.Id] = true;
                            Stop(s);
                        }
                    }
                }
            }
            catch { }
        }, null, 20000, 30000);
    }

    public static void Delete(HostedServer s)
    {
        Stop(s);
        StopTunnel(s.Id);
        Running.TryRemove(s.Id, out _);
        try { if (Directory.Exists(Dir(s))) Directory.Delete(Dir(s), recursive: true); } catch { }
    }

    private static void Emit(string id, string line)
    {
        LogEmitted?.Invoke(id, line);
        try
        {
            File.AppendAllText(Path.Combine(Root, id, "console.log"),
                $"[{DateTime.Now:HH:mm:ss}] {line}\n");
        }
        catch { }
    }

    // ---------------- sauvegarde du monde ----------------

    /// <summary>Zippe le monde dans backups\ (les 10 dernières sauvegardes sont conservées).</summary>
    public static string BackupWorld(HostedServer s)
    {
        if (!Directory.Exists(WorldDir(s))) return "";
        string backupsDir = Path.Combine(Dir(s), "backups");
        Directory.CreateDirectory(backupsDir);
        string dest = Path.Combine(backupsDir, $"world-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(WorldDir(s), dest,
            System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);

        // ne garde que les 10 plus récentes
        var olds = Directory.GetFiles(backupsDir, "world-*.zip")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .Skip(10);
        foreach (var f in olds) { try { f.Delete(); } catch { } }
        return dest;
    }

    // ---------------- import de map ----------------

    /// <summary>
    /// Importe une map (dossier contenant level.dat ou .zip) comme monde du serveur.
    /// L'ancien monde est sauvegardé en world.backup-<date>.
    /// Retourne le nom de la map importée.
    /// </summary>
    public static string ImportWorld(HostedServer s, string sourcePath)
    {
        string staging = Path.Combine(Path.GetTempPath(), "TeamLauncher-map-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);

            if (File.Exists(sourcePath) && sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(sourcePath, staging);
            }
            else if (Directory.Exists(sourcePath))
            {
                CopyDir(sourcePath, staging);
            }
            else throw new Exception("Fichier ou dossier introuvable.");

            // Trouve le dossier qui contient level.dat (racine ou sous-dossier)
            string worldSrc = FindWorldDir(staging)
                ?? throw new Exception("Aucun level.dat trouvé dans cette map.\n" +
                                       "Vérifie que c'est bien un monde Minecraft (solo ou carte téléchargée).");

            if (IsRunning(s))
                throw new Exception("Arrête le serveur avant d'importer une map.");

            // Sauvegarde de l'ancien monde
            if (Directory.Exists(WorldDir(s)))
            {
                string backup = Path.Combine(Dir(s), "world.backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.Move(WorldDir(s), backup);
            }

            CopyDir(worldSrc, WorldDir(s));
            return Path.GetFileName(worldSrc.TrimEnd('\\', '/'))!;
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    private static string? FindWorldDir(string dir)
    {
        if (File.Exists(Path.Combine(dir, "level.dat"))) return dir;
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var found = FindWorldDir(sub);
            if (found != null) return found;
        }
        return null;
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    // ---------------- IP locale ----------------

    /// <summary>IP locale du PC (à donner aux amis sur le même réseau).</summary>
    public static string GetLocalIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is System.Net.IPEndPoint ep) return ep.Address.ToString();
        }
        catch { }
        return "127.0.0.1";
    }

    // ---------------- tunnel Internet (playit.gg) ----------------
    // Permet à des amis n'importe où sur Internet de rejoindre le serveur SANS
    // ouvrir de ports sur la box : l'agent playit crée une adresse publique
    // (ex. quelque-chose.playit.gg) qui redirige vers ce serveur.

    public static string TunnelExe => Path.Combine(Root, "playit-agent", "playit.exe");

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Process> Tunnels = new();

    /// <summary>Ligne émise par l'agent tunnel : (serverId, ligne).</summary>
    public static event Action<string, string>? TunnelEmitted;

    public static bool IsTunnelRunning(string id) =>
        Tunnels.TryGetValue(id, out var p) && !p.HasExited;

    public static bool IsTunnelInstalled => File.Exists(TunnelExe);

    public static async Task DownloadTunnelAsync()
    {
        if (IsTunnelInstalled) return;
        Directory.CreateDirectory(Path.GetDirectoryName(TunnelExe)!);

        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/playit-cloud/playit-agent/releases/latest");
        req.Headers.UserAgent.ParseAdd("TeamLauncher");
        using var resp = await Http.Shared.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        string? url = null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            string name = a.GetProperty("name").GetString() ?? "";
            if (name.Contains("windows", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                url = a.GetProperty("browser_download_url").GetString();
                break;
            }
        }
        if (url == null) throw new Exception("Agent tunnel introuvable sur GitHub.");

        var bytes = await Http.Shared.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(TunnelExe, bytes);
    }

    public static void StartTunnel(string id)
    {
        if (IsTunnelRunning(id)) return;
        var psi = new ProcessStartInfo
        {
            FileName = TunnelExe,
            WorkingDirectory = Path.GetDirectoryName(TunnelExe)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var p = Process.Start(psi) ?? throw new Exception("Impossible de lancer l'agent tunnel.");
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            DetectTunnelAddress(id, e.Data);
            TunnelEmitted?.Invoke(id, e.Data);
        };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) TunnelEmitted?.Invoke(id, e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        Tunnels[id] = p;
    }

    /// <summary>Adresses publiques détectées dans la sortie de l'agent : serverId -> hôte .playit.gg</summary>
    public static readonly Dictionary<string, string> TunnelAddresses = new();

    /// <summary>Une adresse publique a été détectée pour ce serveur : (serverId, adresse).</summary>
    public static event Action<string, string>? TunnelAddressFound;

    private static void DetectTunnelAddress(string id, string line)
    {
        foreach (var raw in line.Split(' ', '\t', '"', ',', ';', '(', ')'))
        {
            var tok = raw.Trim().TrimEnd('.', ':');
            // un domaine du type xxx-yyy.playit.gg[:port] — pas l'URL de claim https://playit.gg/...
            int at = tok.IndexOf(".playit.gg", StringComparison.OrdinalIgnoreCase);
            if (at <= 0 || tok.Contains("//", StringComparison.Ordinal)) continue;
            string host = tok;
            int colon = host.IndexOf(':', at); // port éventuel après le domaine
            if (colon > at) host = host[..colon];
            if (host.Length == 0) continue;
            if (TunnelAddresses.TryGetValue(id, out var cur) && cur == host) return;
            TunnelAddresses[id] = host;
            try { TunnelAddressFound?.Invoke(id, host); } catch { }
            return;
        }
    }

    public static void StopTunnel(string id)
    {
        if (!Tunnels.TryGetValue(id, out var p)) return;
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
        Tunnels.TryRemove(id, out _);
    }
}
