using System.Diagnostics;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Charge une instance Minecraft Java dans un Process, gère l'authentification,
/// la sélection de la version, le téléchargement automatique des fichiers manquants,
/// et la construction de la ligne de commande JVM.
/// </summary>
public static class GameLauncher
{
    private static int _preparingFlag;

    /// <summary>True tant que Minecraft tourne : empêche un second lancement.</summary>
    public static bool GameRunning { get; private set; }

    /// <summary>Adresse du serveur rejoint pendant la partie en cours (null = solo/launcher).</summary>
    public static string? CurrentServer { get; private set; }

    /// <summary>Notifie l'UI quand l'état du jeu change (boutons Jouer).</summary>
    public static event Action? StateChanged;

    private static readonly object ButtonLock = new();
    private static readonly List<WeakReference<Button>> TrackedButtons = new();

    /// <summary>Le bouton « Jouer » d'une carte se désactive tout seul quand le jeu tourne.</summary>
    public static void TrackPlayButton(Button button)
    {
        lock (ButtonLock)
        {
            TrackedButtons.RemoveAll(w => !w.TryGetTarget(out _));
            TrackedButtons.Add(new WeakReference<Button>(button));
        }
        ApplyTo(button);
    }

    private static void RefreshButtons()
    {
        List<Button> alive;
        lock (ButtonLock)
        {
            TrackedButtons.RemoveAll(w => !w.TryGetTarget(out _));
            alive = TrackedButtons.Select(w => w.TryGetTarget(out var b) ? b : null).OfType<Button>().ToList();
        }
        foreach (var b in alive) ApplyTo(b);
        try { StateChanged?.Invoke(); } catch { }
    }

    private static void ApplyTo(Button b)
    {
        try
        {
            b.BeginInvoke(() =>
            {
                b.Enabled = !GameRunning && Interlocked.CompareExchange(ref _preparingFlag, 0, 0) == 0;
                b.Text = GameRunning ? "En jeu..." : Interlocked.CompareExchange(ref _preparingFlag, 0, 0) == 0 ? "▶  Jouer" : "Lancement...";
            });
        }
        catch { }
    }

    public static void Play(InstanceInfo inst, string? joinServer = null, CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _preparingFlag, 1, 0) != 0 || GameRunning)
        {
            MessageBox.Show(
                GameRunning
                    ? "Minecraft est déjà en cours d'exécution.\nFerme le jeu avant de relancer."
                    : "Un lancement est déjà en cours, patiente un instant.",
                "Team Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        CurrentServer = string.IsNullOrWhiteSpace(joinServer) ? null : joinServer;
        inst.Launches++;
        DataStore.Save();

        var start = DateTime.UtcNow;
        var progressForm = new ProgressForm(inst);
        _ = Task.Run(async () =>
        {
            Process? game = null;
            try
            {
                // backup auto des mondes AVANT chaque session (anti-corruption)
                try
                {
                    var pre = BackupService.Create(inst.Id);
                    if (pre.Length > 0) Log("Sauvegarde pré-session : " + pre);
                }
                catch (Exception ex) { Log("Échec sauvegarde pré-session : " + ex); }

                game = await PlayCore(inst, progressForm, joinServer, ct);
            }
            catch (OperationCanceledException)
            {
                Log("Lancement annulé par l'utilisateur.");
                Notifier.Show("Lancement annulé", "Le téléchargement a été interrompu.");
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
                // boîte d'erreur toujours au premier plan (au-dessus de la barre de progression)
                var owner = new Form { TopMost = true, ShowInTaskbar = false, Opacity = 0 };
                owner.Show();
                MessageBox.Show(owner,
                    "Impossible de lancer le jeu :\n" + ex.Message +
                    "\n\nDétails techniques enregistrés dans :\n" + LogFile,
                    "Team Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                owner.Close();
                owner.Dispose();
            }
            finally
            {
                try { progressForm.BeginInvoke(progressForm.Close); } catch { }
                Interlocked.Exchange(ref _preparingFlag, 0);

                if (game != null)
                {
                    // Verrou actif jusqu'à la FERMETURE de Minecraft (pas de double lancement)
                    GameRunning = true;
                    RefreshButtons();
                    Log("Jeu en cours d'exécution...");
                await game.WaitForExitAsync();
                inst.PlaySeconds += (long)(DateTime.UtcNow - start).TotalSeconds;
                inst.LastPlayed = DateTime.Now;
                DataStore.Save();
                Log($"Jeu fermé. Temps de jeu ajouté à « {inst.Name} ».");
                GameRunning = false;
                CurrentServer = null;
                RefreshButtons();
                PresenceService.SetLauncherPresence();

                // analyse de crash : explication claire si le jeu s'est mal fermé
                if (game.ExitCode != 0)
                {
                    string gameDir = Path.Combine(DataStore.InstancesRoot, inst.Id);
                    var advice = CrashAnalyzer.AnalyzeInstance(gameDir);
                    if (advice != null)
                    {
                        Log($"Analyse de crash : {advice.Replace("\n", " ")}");
                        MessageBox.Show(advice + "\n\n(détails complets dans le dossier de l'instance)",
                            "Minecraft s'est arrêté anormalement", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        Notifier.Show("Minecraft s'est arrêté",
                            $"Code de sortie {game.ExitCode}. Voir le dossier de l'instance pour les détails.");
                    }

                    // Télémétrie : envoyer le crash à Discord
                    try
                    {
                        string? logTail = null;
                        string gameLogPath = Path.Combine(gameDir, "game-log.txt");
                        if (File.Exists(gameLogPath))
                        {
                            try
                            {
                                var lines = File.ReadLines(gameLogPath).Reverse().Take(30);
                                logTail = string.Join("\n", lines.Reverse());
                            }
                            catch { }
                        }
                        TelemetryService.ReportCrash(inst, game.ExitCode, logTail);
                    }
                    catch { /* télémétrie non bloquante */ }
                }

                // sauvegarde automatique des mondes après la partie
                try
                {
                    var b = BackupService.Create(inst.Id);
                    if (b.Length > 0) Log("Sauvegarde des mondes : " + b);
                }
                catch (Exception ex) { Log("Échec sauvegarde : " + ex); }
                }
            }
        });
        progressForm.ShowDialog();
    }

    /// <summary>Télécharge et installe automatiquement un JRE Adoptium si la version requise manque.</summary>
    private static async Task<string?> DownloadJavaAsync(int major, ProgressForm ui)
    {
        try
        {
            string dir = Path.Combine(GameInstaller.RuntimeRoot, "jre-" + major);
            string marker = Path.Combine(dir, ".done");
            string? existing = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "javaw.exe", SearchOption.AllDirectories).FirstOrDefault()
                : null;
            if (existing != null && File.Exists(marker)) return existing;

            string url = $"https://api.adoptium.net/v3/binary/latest/{major}/ga/windows/x64/jre/hotspot/normal/eclipse";
            string zipPath = Path.Combine(GameInstaller.RuntimeRoot, $"adoptium-jre-{major}.zip");
            Directory.CreateDirectory(GameInstaller.RuntimeRoot);

            using (var resp = await Http.Shared.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(zipPath);
                await resp.Content.CopyToAsync(fs);
            }
            ui.Status("Extraction de Java...");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, dir);
            File.WriteAllText(marker, "ok");

            return Directory.GetFiles(dir, "javaw.exe", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log("Téléchargement Java impossible : " + ex.Message);
            return null;
        }
    }

    /// <summary>Note dans le journal les mods dont le nom mentionne une autre version (simple info, aucun blocage).</summary>
    private static bool WarnIncompatibleMods(InstanceInfo inst, string version)
    {
        try
        {
            string modsDir = Path.Combine(DataStore.InstancesRoot, inst.Id, "mods");
            if (!Directory.Exists(modsDir)) return true;

            var m = System.Text.RegularExpressions.Regex.Match(version, @"^1\.(\d+)");
            if (!m.Success) return true;
            string target = "1." + m.Groups[1].Value;

            var suspicious = Directory.GetFiles(modsDir, "*.jar")
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(n =>
                {
                    var found = System.Text.RegularExpressions.Regex.Matches(n,
                            @"(?<![\d.])1\.\d{1,2}(?:\.\d{1,3})*")
                        .Cast<System.Text.RegularExpressions.Match>()
                        .Select(mm => mm.Value)
                        .ToList();

                    if (found.Count == 0) return false;
                    return !found.Any(v => v.StartsWith(target));
                })
                .ToList();

            if (suspicious.Count > 0)
                Log($"Info : mods dont le nom évoque une autre version (non bloquant) : " +
                    string.Join(", ", suspicious));
            return true;
        }
        catch { return true; }
    }

    public static string LogFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TeamLauncher", "launcher.log");

    private static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss}] {text}\n\n");
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public int dwLength;
        public int dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    /// <summary>Mémoire RAM disponible sur le système (en Mo) — via Win32, sans WMI.</summary>
    private static long GetAvailableRamMb()
    {
        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref mem))
                return (long)(mem.ullAvailPhys / (1024 * 1024));
        }
        catch { }
        return -1;
    }

    /// <summary>Ajoute une ligne au journal depuis les autres services.</summary>
    public static void AppendLog(string text) => Log(text);

    private static async Task<Process?> PlayCore(InstanceInfo inst, ProgressForm ui,
        string? joinServer, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var stepSw = Stopwatch.StartNew();

        ui.Status("Préparation...");
        ct.ThrowIfCancellationRequested();

        // ---- 1+2 en PARALLÈLE : Session + Résolution version ----
        string version = inst.McVersion is "latest" or "?" or "" or null
            ? "" : inst.McVersion;

        McSession? session = null;
        Task<McSession?> authTask = Task.Run(async () =>
        {
            if (DataStore.Settings.AccountMode == "microsoft")
            {
                var s = await MsAuth.LoginAsync(ui);
                if (s != null && DataStore.Settings.PlayerName != s.Name)
                {
                    DataStore.Settings.PlayerName = s.Name;
                    DataStore.Save();
                    AppEvents.NotifyAccountChanged();
                }
                return s;
            }
            return MsAuth.OfflineSession(DataStore.Settings.PlayerName);
        });

        Task<string> versionTask = Task.Run(async () =>
        {
            if (version.Length == 0)
                return await LatestReleaseAsync();
            return version;
        });

        // Attendre les deux en parallèle
        await Task.WhenAll(authTask, versionTask);
        session = authTask.Result;
        version = versionTask.Result;
        Log($"[Timing] Session+Version (parallèle): {stepSw.ElapsedMilliseconds}ms");
        Log($"Instance « {inst.Name} » → lancement de Minecraft {inst.Loader} {version}");
        stepSw.Restart();

        // ---- 3. Téléchargement des fichiers officiels (+ Forge si besoin) ----
        var infoJson = await GameInstaller.InstallAsync(version, inst.Loader,
            (stage, done, total) => ui.Progress(stage, done, total),
            ct);
        Log($"[Timing] Fichiers: {stepSw.ElapsedMilliseconds}ms");
        stepSw.Restart();

        using var infoDoc = JsonDocument.Parse(infoJson);
        var info = infoDoc.RootElement;
        bool isForge = info.TryGetProperty("isForge", out _);

        // ---- 4. Java (version requise selon la version de Minecraft) ----
        int requiredJava = info.TryGetProperty("javaMajor", out var jm) ? jm.GetInt32() : 8;
        Log($"Recherche d'un Java {requiredJava}+...");
        string? java = FindJava(requiredJava);
        if (java == null)
        {
            ui.Status($"Téléchargement de Java {requiredJava}...");
            java = await DownloadJavaAsync(requiredJava, ui);
        }
        Log($"[Timing] Java: {stepSw.ElapsedMilliseconds}ms");
        stepSw.Restart();
        if (java == null)
        {
            MessageBox.Show(
                $"Aucun Java {requiredJava}+ trouvé pour lancer Minecraft {version},\n" +
                "et le téléchargement automatique a échoué. Vérifie la connexion ou installe\n" +
                $"Java {requiredJava} manuellement depuis adoptium.net.",
                "Team Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        Log($"Java sélectionné : {java}");

        // ---- 4bis. Vérification mémoire disponible ----
        int ramWanted = Math.Clamp(inst.MaxRamGb > 0 ? inst.MaxRamGb : DataStore.Settings.MaxRamGb, 1, 32);
        long availMb = GetAvailableRamMb();
        Log($"Mémoire système : {availMb} Mo disponibles, {ramWanted} Go demandés.");
        if (availMb < ramWanted * 1024L * 0.5)
        {
            Log($"⚠ Mémoire insuffisante ! {availMb}Mo dispo < {ramWanted * 1024}Mo requis.");
            ui.Status($"Mémoire faible ({availMb}Mo) — risque de crash...");
        }

        // ---- 4bis. Info mods (journal uniquement, plus de popup trompeur) ----
        WarnIncompatibleMods(inst, version);

        // ---- 5. Ligne de commande ----
        string gameDir = Path.Combine(DataStore.InstancesRoot, inst.Id);
        Directory.CreateDirectory(gameDir);

        var psi = new ProcessStartInfo
        {
            FileName = java,
            WorkingDirectory = gameDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true, // journal du jeu pour diagnostiquer un crash
            RedirectStandardError = true
        };

        string classpath = info.GetProperty("jar").GetString() + ";" +
                           string.Join(";", info.GetProperty("classpath")
                               .EnumerateArray().Select(e => e.GetString()));
        string natives = info.GetProperty("natives").GetString()!;
        string mainClass = info.GetProperty("mainClass").GetString()!;

        // Arguments JVM : officiels Mojang (optimisés, G1GC...) si dispo, sinon minimal
        List<string>? officialJvm = null;
        if (info.TryGetProperty("jvmArgs", out var ja) && ja.ValueKind == JsonValueKind.Array)
        {
            officialJvm = new List<string>();
            foreach (var e in ja.EnumerateArray())
                officialJvm.Add(e.GetString()!);
        }

        foreach (var a in BuildJvmArgs(classpath, natives, isForge, officialJvm,
            inst.MaxRamGb > 0 ? inst.MaxRamGb : DataStore.Settings.MaxRamGb, inst.JvmArgs))
            psi.ArgumentList.Add(a);
        psi.ArgumentList.Add(mainClass);
        foreach (var a in BuildGameArgs(version, session, assetsIndex: info.GetProperty("assetsIndex").GetString()!, legacyArgs: info.GetProperty("minecraftArguments").GetString(), hasModernArgs: info.GetProperty("hasArguments").GetBoolean(), joinServer))
            psi.ArgumentList.Add(a);

        ui.Status("Démarrage de Minecraft...");
        Log($"[Timing] Préparation JVM/args: {stepSw.ElapsedMilliseconds}ms");
        Log($"Démarrage : {java} " + string.Join(" ", psi.ArgumentList.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
        var game = Process.Start(psi);
        if (game == null) throw new Exception("Le processus Java n'a pas pu être démarré.");
        sw.Stop();
        Log($"[Timing] ⏱ TEMPS TOTAL DE LANCEMENT: {sw.ElapsedMilliseconds}ms ({sw.Elapsed.TotalSeconds:F1}s)");
        Log($"Processus Java lancé (PID {game.Id}).");
        PresenceService.UpdateGame(inst);

        // Journal de sortie du jeu (crashs, erreurs de mods...) dans le dossier de l'instance
        string gameLog = Path.Combine(gameDir, "game-log.txt");
        _ = Task.Run(async () =>
        {
            try
            {
                using var sw = new StreamWriter(gameLog, append: true);
                await sw.WriteLineAsync($"===== Lancement {DateTime.Now:G} — {inst.Loader} {version} =====");
                while (!game.HasExited)
                {
                    var line = await game.StandardOutput.ReadLineAsync();
                    if (line != null) await sw.WriteLineAsync(line);
                }
                while (!game.StandardError.EndOfStream)
                    await sw.WriteLineAsync(await game.StandardError.ReadLineAsync());
            }
            catch { }
        });

        return game;
    }

    // ---------------- construction des arguments ----------------

    private static List<string> BuildJvmArgs(string classpath, string natives, bool isForge,
        List<string>? officialJvm, int ramGb, string? extraJvmArgs = null)
    {
        var args = new List<string> { $"-Xmx{Math.Clamp(ramGb, 1, 32)}G" };

        if (officialJvm is { Count: > 0 })
        {
            // arguments officiels de la version : on remplace les variables
            var subs = new Dictionary<string, string>
            {
                ["${natives_directory}"] = natives,
                ["${library_directory}"] = GameInstaller.RuntimeRoot + "\\libraries",
                ["${classpath_separator}"] = ";",
                ["${classpath}"] = classpath,
                ["${launcher_name}"] = "TeamLauncher",
                ["${launcher_version}"] = "1.0"
            };
            foreach (var token in officialJvm)
            {
                var t = subs.Aggregate(token, (cur, kv) => cur.Replace(kv.Key, kv.Value));
                if (t.Length > 0) args.Add(t);
            }
        }
        else
        {
            args.Add($"-Djava.library.path={natives}");
            args.Add("-cp");
            args.Add(classpath);
        }

        // Arguments JVM personnalisés de l'instance (Options de lancement)
        if (!string.IsNullOrWhiteSpace(extraJvmArgs))
        {
            foreach (var a in extraJvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                args.Add(a);
        }

        if (isForge)
        {
            // MODE COMMANDO : Compatibilité maximale GTX 1000 / Pilotes anciens
            args.Add("-Dsun.java2d.d3d=false"); 
            args.Add("-Dsun.java2d.noddraw=true");
            args.Add("-Dsun.java2d.opengl=true");
            args.Add("-XX:+UseG1GC");
            args.Add("-XX:MaxGCPauseMillis=200");
            args.Add("-XX:+UseStringDeduplication");
            args.Add("-Xss512k");
        }
        if (isForge)
        {
            // options classiques recommandées pour Forge (launchwrapper)
            args.Add("-Dfml.ignoreInvalidMinecraftCertificates=true");
            args.Add("-Dfml.ignorePatchDiscrepancies=true");
        }
        return args;
    }

    private static List<string> BuildGameArgs(string version, McSession s,
        string assetsIndex, string? legacyArgs, bool hasModernArgs, string? joinServer = null)
    {
        var args = hasModernArgs || string.IsNullOrEmpty(legacyArgs)
            ? new List<string>
            {
                "--username", s.Name,
                "--version", version,
                "--gameDir", ".",
                "--assetsDir", Path.Combine(GameInstaller.RuntimeRoot, "assets"),
                "--assetIndex", assetsIndex,
                "--uuid", s.Uuid,
                "--accessToken", s.AccessToken,
                "--userType", "msa"
            }
            : legacyArgs
                .Replace("${auth_player_name}", s.Name)
                .Replace("${auth_uuid}", s.Uuid)
                .Replace("${auth_access_token}", s.AccessToken)
                .Replace("${auth_session}", s.AccessToken)
                .Replace("${user_type}", "legacy")
                .Replace("${user_properties}", "{}")
                .Replace("${version_name}", version)
                // variables de dossiers : sinon elles restent littérales et Minecraft
                // joue dans un sous-dossier fantôme au lieu du dossier de l'instance
                .Replace("${game_directory}", ".")
                .Replace("${game_dir}", ".")
                .Replace("${assets_root}", Path.Combine(GameInstaller.RuntimeRoot, "assets"))
                .Replace("${assets_index_name}", assetsIndex)
                .Split(' ')
                .ToList();

        if (!string.IsNullOrEmpty(joinServer))
        {
            var parts = joinServer.Split(':');
            args.Add("--server"); args.Add(parts[0]);
            if (parts.Length > 1) { args.Add("--port"); args.Add(parts[1]); }
        }
        return args;
    }

    // ---------------- divers ----------------

    private static async Task<string> LatestReleaseAsync()
    {
        using var doc = JsonDocument.Parse(await Http.Shared.GetStringAsync(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"));
        return doc.RootElement.GetProperty("latest").GetProperty("release").GetString()!;
    }

    public static string? FindJava(int requiredMajor = 8)
    {
        var settingsPath = DataStore.Settings.JavaPath;
        if (File.Exists(settingsPath)) return settingsPath;

        Log("Recherche des Java installés...");
        var candidates = new List<string>();
        void Scan(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    candidates.AddRange(Directory.GetFiles(dir, "javaw.exe", SearchOption.AllDirectories));
            }
            catch { }
        }

        Scan(@"C:\Program Files\Java");
        Scan(@"C:\Program Files\Eclipse Adoptium");
        Scan(@"C:\Program Files\Amazon Corretto");
        Scan(@"C:\Program Files\Zulu");
        Scan(@"C:\Program Files (x86)\Java");
        Log($"{candidates.Count} candidat(s) trouvé(s).");
        Scan(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Packages\Microsoft.4297127D64EC6_8wekyb3d8bbwe\LocalCache\Local\runtime")); // runtimes du launcher MC officiel

        return candidates
            .Select(c => (path: c, major: DetectJavaMajor(c)))
            .Where(c => c.major >= requiredMajor)
            // la version de Java LA PLUS PROCHE de celle requise (les vieux MC/Forge
            // refusent les Java trop récents, ex : Forge 1.12.2 exige Java 8)
            .OrderBy(c => c.major - requiredMajor)
            .Select(c => c.path)
            .FirstOrDefault();
    }

    /// <summary>Détecte la version majeure de Java en interrogeant l'exécutable (avec cache).</summary>
    private static readonly Dictionary<string, int> JavaMajorCache = new();

    private static int DetectJavaMajor(string javawPath)
    {
        if (JavaMajorCache.TryGetValue(javawPath, out var cached)) return cached;
        int result = 0;
        try
        {
            var javaExe = Path.Combine(Path.GetDirectoryName(javawPath)!, "java.exe");
            var psi = new ProcessStartInfo
            {
                FileName = File.Exists(javaExe) ? javaExe : javawPath,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            if (!p.HasExited) { try { p.Kill(); } catch { } }
            // "version 17.0.2" ou "1.8.0_401"
            var m = System.Text.RegularExpressions.Regex.Match(output, @"version ""(\d+)(?:\.(\d+))?");
            if (m.Success)
            {
                int first = int.Parse(m.Groups[1].Value);
                result = first == 1 ? int.Parse(m.Groups[2].Value) : first;
            }
        }
        catch { }
        JavaMajorCache[javawPath] = result;
        Log($"Java détecté : {javawPath} → version {result}");
        return result;
    }

    private sealed class ProgressForm : Form
    {
        private readonly Label titleLabel = new();
        private readonly Label statusLabel = new();
        private readonly Panel track = new();
        private readonly Panel fill = new();
        private readonly System.Windows.Forms.Timer marqueeTimer = new() { Interval = 25 };
        private int marqueePos = -60;
        private bool indeterminate = true;

        public ProgressForm(InstanceInfo inst)
        {
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(470, 140);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Theme.Card;

            // coins arrondis
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int r = 14;
            path.AddArc(0, 0, r, r, 180, 90);
            path.AddArc(Width - r, 0, r, r, 270, 90);
            path.AddArc(Width - r, Height - r, r, r, 0, 90);
            path.AddArc(0, Height - r, r, r, 90, 90);
            Region = new Region(path);

            titleLabel.Text = "Lancement de " + inst.Name;
            titleLabel.ForeColor = Theme.Text;
            titleLabel.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            titleLabel.SetBounds(28, 22, Width - 56, 26);

            statusLabel.Text = "Préparation...";
            statusLabel.ForeColor = Theme.TextDim;
            statusLabel.Font = new Font("Segoe UI", 9.5f);
            statusLabel.SetBounds(28, 52, Width - 110, 22);

            track.BackColor = ControlPaint.Dark(Theme.Card, 0.04f);
            track.SetBounds(28, 92, Width - 56, 6);

            fill.BackColor = Theme.Accent;
            fill.SetBounds(0, 0, 70, 6);
            track.Controls.Add(fill);

            Controls.Add(titleLabel);
            Controls.Add(statusLabel);
            Controls.Add(track);

            marqueeTimer.Tick += (_, _) =>
            {
                marqueePos += 7;
                if (marqueePos > track.Width) marqueePos = -70;
                if (indeterminate) fill.Width = 70;
                fill.Left = Math.Clamp(marqueePos, 0, track.Width - fill.Width);
            };
            marqueeTimer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            marqueeTimer.Stop();
            marqueeTimer.Dispose();
            base.OnFormClosed(e);
        }

        public void Status(string text)
        {
            try { BeginInvoke(() => { indeterminate = true; statusLabel.Text = text; }); } catch { }
        }

        public void Progress(string stage, int done, int total)
        {
            try
            {
                BeginInvoke(() =>
                {
                    if (total <= 0) return;
                    int pct = Math.Min(100, done * 100 / total);
                    indeterminate = false;
                    statusLabel.Text = $"{stage} — {pct} %";
                    fill.Width = Math.Max(2, track.Width * pct / 100);
                    fill.Left = 0;
                });
            }
            catch { }
        }
    }
}
