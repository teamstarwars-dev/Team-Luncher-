using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Télécharge les fichiers officiels de Minecraft Java depuis les serveurs Mojang :
/// version JSON, client.jar, bibliothèques, natives et assets.
/// Tout est mis en cache dans %LOCALAPPDATA%\TeamLauncher\runtime pour ne rien retélécharger.
/// </summary>
public static class GameInstaller
{
    // Utilise Http.Shared (client HTTP partagé)

    public static string RuntimeRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TeamLauncher", "runtime");

    private static string VersionsDir => Path.Combine(RuntimeRoot, "versions");
    private static string LibrariesDir => Path.Combine(RuntimeRoot, "libraries");
    private static string AssetsDir => Path.Combine(RuntimeRoot, "assets");
    private static string NativesDir(string version) => Path.Combine(RuntimeRoot, "natives", version);

    /// <summary>progress(stade, fait, total)</summary>
    public static async Task<string> InstallAsync(string versionId, string loader,
        Action<string, int, int> progress, CancellationToken ct, bool forceVerify = false)
    {
        Directory.CreateDirectory(VersionsDir);
        Directory.CreateDirectory(LibrariesDir);
        Directory.CreateDirectory(AssetsDir);

        // Marqueur : cette version a déjà été vérifiée intégralement lors d'un
        // lancement précédent → on saute tous les contrôles de hash (démarrage rapide).
        string markerPath = Path.Combine(VersionsDir, versionId, ".tl-verified");
        if (forceVerify && File.Exists(markerPath)) File.Delete(markerPath);
        bool fast = File.Exists(markerPath);

        // ---- 1. JSON de la version ----
        progress(fast ? Lang.T("Lecture du cache", "Reading cache") : Lang.T("Recherche de la version", "Searching for version"), 0, 1);
        var json = await GetVersionJsonAsync(versionId, ct);
        string vDir = Path.Combine(VersionsDir, versionId);
        Directory.CreateDirectory(vDir);
        string jarPath = Path.Combine(vDir, versionId + ".jar");

        // ---- 2. Client jar ----
        if (!fast)
        {
            progress(Lang.T("Téléchargement du jeu", "Downloading game"), 0, 1);
            if (json.RootElement.TryGetProperty("downloads", out var downloads)
                && downloads.TryGetProperty("client", out var client))
            {
                await DownloadAsync(
                    client.GetProperty("url").GetString()!,
                    jarPath,
                    client.TryGetProperty("sha1", out var csha) ? csha.GetString() : null, ct);
            }
        }
        else if (!File.Exists(jarPath))
        {
            fast = false; // jar disparu : on repasse en mode complet
        }

        // ---- 3. Bibliothèques + natives (téléchargements parallèles) ----
        var classpathEntries = new List<string>();
        if (json.RootElement.TryGetProperty("libraries", out var libraries))
        {
            var libs = libraries.EnumerateArray()
                .Where(l => RulesAllow(l))
                .ToList();
            int done = 0;
            using var gate = new SemaphoreSlim(8);
            var bag = new System.Collections.Concurrent.ConcurrentBag<string>();

            var libTasks = libs.Select(async lib =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await gate.WaitAsync(ct);
                    if (lib.TryGetProperty("downloads", out var ld))
                    {
                        if (ld.TryGetProperty("artifact", out var art))
                        {
                            string relPath = art.GetProperty("path").GetString()!;
                            string dest = Path.Combine(LibrariesDir, relPath.Replace('/', '\\'));
                            if (!fast || !File.Exists(dest)) // en mode rapide : présent = vérifié
                                await DownloadAsync(art.GetProperty("url").GetString()!, dest,
                                    art.TryGetProperty("sha1", out var lsha) ? lsha.GetString() : null, ct, fast: fast);
                            bag.Add(dest);
                        }
                        if (ld.TryGetProperty("classifiers", out var cls)
                            && cls.TryGetProperty("natives-windows", out var nat))
                        {
                            string natDest = Path.Combine(LibrariesDir,
                                nat.GetProperty("path").GetString()!.Replace('/', '\\'));
                            if (!fast || !File.Exists(natDest))
                                await DownloadAsync(nat.GetProperty("url").GetString()!, natDest,
                                    nat.TryGetProperty("sha1", out var nsha) ? nsha.GetString() : null, ct, fast: fast);
                            ExtractNatives(natDest, NativesDir(versionId));
                        }
                    }
                }
                finally
                {
                    Interlocked.Increment(ref done);
                    progress(Lang.T("Bibliothèques", "Libraries"), Volatile.Read(ref done), libs.Count);
                    gate.Release();
                }
            });
            await Task.WhenAll(libTasks);
            classpathEntries.AddRange(bag.OrderBy(x => x));
        }

        // ---- 4. Assets (sons, textures du menu...) — sautés en mode rapide ----
        string assetsIndexName = json.RootElement.TryGetProperty("assets", out var a)
            ? a.GetString()! : versionId;
        string? assetsObjectsDir = Path.Combine(AssetsDir, "objects");
        bool assetsAlreadyThere = fast
            && Directory.Exists(assetsObjectsDir)
            && Directory.EnumerateFileSystemEntries(assetsObjectsDir).Any();
        int failedAssets = 0;
        bool assetsHadFailures = false;
        if (json.RootElement.TryGetProperty("assetIndex", out var assetIndex) && !assetsAlreadyThere)
        {
            string indexPath = Path.Combine(AssetsDir, "indexes", assetsIndexName + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            await DownloadAsync(assetIndex.GetProperty("url").GetString()!, indexPath, null, ct);

            using var index = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath, ct));
            var objects = index.RootElement.GetProperty("objects")
                .EnumerateObject()
                .Select(p => p.Value)
                .ToList();
            int done = 0;
            using var gate = new SemaphoreSlim(16);

            async Task FetchOne(JsonElement obj)
            {
                try
                {
                    string hash = obj.GetProperty("hash").GetString()!;
                    string sub = hash[..2];
                    string dest = Path.Combine(AssetsDir, "objects", sub, hash);
                    if (!File.Exists(dest))
                        await DownloadAsync(
                            $"https://resources.download.minecraft.net/{sub}/{hash}", dest, hash, ct,
                            quiet: true);
                    if (!File.Exists(dest))
                        Interlocked.Increment(ref failedAssets); // toujours absent après tentative
                }
                catch { Interlocked.Increment(ref failedAssets); }
                finally
                {
                    Interlocked.Increment(ref done);
                    progress(Lang.T("Assets du jeu", "Game assets"), Volatile.Read(ref done), objects.Count);
                }
            }

            var tasks = objects.Select(async o =>
            {
                await gate.WaitAsync(ct);
                try { await FetchOne(o); }
                finally { gate.Release(); }
            });
            await Task.WhenAll(tasks);

            int stillMissing = Volatile.Read(ref failedAssets);
            if (stillMissing > 0)
            {
                assetsHadFailures = true;
                // nouvelle passe pour rattraper les ratés du parallélisme
                progress(Lang.T("Seconde passe assets...", "Second assets pass..."), 0, objects.Count);
                foreach (var o in objects)
                {
                    ct.ThrowIfCancellationRequested();
                    string hash = o.GetProperty("hash").GetString()!;
                    string sub = hash[..2];
                    string dest = Path.Combine(AssetsDir, "objects", sub, hash);
                    if (!File.Exists(dest))
                    {
                        try
                        {
                            await DownloadAsync(
                                $"https://resources.download.minecraft.net/{sub}/{hash}", dest, hash, ct,
                                quiet: false);
                        }
                        catch { }
                    }
                }
            }
        }

        // Tout est vérifié : marqueur pour les prochains démarrages rapides
        // (sauf si des assets ont échoué : on retentera au prochain lancement)
        if (!File.Exists(markerPath) && !assetsHadFailures)
            File.WriteAllText(markerPath, DateTime.Now.ToString("O"));

        // ---- 5. NeoForge : installeur officiel depuis le Maven NeoForged ----
        if (loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
        {
            progress(Lang.T("Installation de NeoForge", "Installing NeoForge"), 0, 0);
            string neoId = await EnsureNeoForgeInstalledAsync(versionId, ct);

            string neoJsonPath = Path.Combine(VersionsDir, neoId, neoId + ".json");
            using var nj = JsonDocument.Parse(await File.ReadAllTextAsync(neoJsonPath, ct));
            var nroot = nj.RootElement;

            var neoCp = new List<string>();
            if (nroot.TryGetProperty("libraries", out var nlibs))
            {
                foreach (var lib in nlibs.EnumerateArray())
                {
                    if (!lib.TryGetProperty("name", out var nEl)) continue;
                    string name = nEl.GetString()!;
                    string? rel = MavenNameToPath(name);
                    if (rel == null) continue;
                    string full = Path.Combine(LibrariesDir, rel);
                    if (!File.Exists(full) && lib.TryGetProperty("downloads", out var ndl)
                        && ndl.TryGetProperty("artifact", out var nart))
                    {
                        await DownloadAsync(nart.GetProperty("url").GetString()!, full,
                            nart.TryGetProperty("sha1", out var ns) ? ns.GetString() : null, ct, quiet: true);
                    }
                    if (File.Exists(full)) neoCp.Add(full);
                }
            }

            var neoFinal = new List<string>(neoCp);
            foreach (var vcp in classpathEntries)
                if (!neoFinal.Contains(vcp)) neoFinal.Add(vcp);

            int javaNeed = json.RootElement.TryGetProperty("javaVersion", out var njv)
                ? njv.GetProperty("majorVersion").GetInt32() : 17;
            return JsonSerializer.Serialize(new
            {
                mainClass = nroot.GetProperty("mainClass").GetString(),
                classpath = neoFinal,
                jar = jarPath,
                natives = NativesDir(versionId),
                assetsIndex = assetsIndexName,
                javaMajor = Math.Max(javaNeed, 17),
                minecraftArguments = nroot.TryGetProperty("minecraftArguments", out var nma)
                    ? nma.GetString() : null,
                hasArguments = nroot.TryGetProperty("arguments", out _),
                jvmArgs = ExtractJvmArgs(nroot),
                isForge = false
            });
        }

        // ---- 5ter. Forge : installation officielle silencieuse (comme CurseForge) ----
        if (loader.Equals("Forge", StringComparison.OrdinalIgnoreCase))
        {
            progress(Lang.T("Installation de Forge", "Installing Forge"), 0, 0);
            string forgeId = await EnsureForgeInstalledAsync(versionId, ct);

            string forgeJsonPath = Path.Combine(VersionsDir, forgeId, forgeId + ".json");
            using var fj = JsonDocument.Parse(await File.ReadAllTextAsync(forgeJsonPath, ct));
            var root = fj.RootElement;

            var libElements = new List<JsonElement>();
            void Collect(JsonElement el)
            {
                if (el.TryGetProperty("libraries", out var libs))
                    foreach (var lib in libs.EnumerateArray())
                        if (lib.TryGetProperty("name", out _)) libElements.Add(lib.Clone());
            }
            Collect(root);
            // install_profile.json = liste COMPLÈTE des bibliothèques (le JSON de version est incomplet)
            string profilePath = Path.Combine(VersionsDir, forgeId, "install_profile.json");
            if (File.Exists(profilePath))
                Collect(JsonDocument.Parse(File.ReadAllText(profilePath)).RootElement);

            // Dédoublonnage par nom Maven
            var seen = new HashSet<string>(StringComparer.Ordinal);
            libElements = libElements
                .Where(l => seen.Add(l.GetProperty("name").GetString()!))
                .ToList();

            var forgeCp = new List<string>
            {
                Path.Combine(VersionsDir, forgeId, forgeId + ".jar")
            };
            int done = 0;
            foreach (var lib in libElements)
            {
                // "clientreq": false = bibliothèque réservée au serveur
                if (lib.TryGetProperty("clientreq", out var cr) && !cr.GetBoolean()) continue;
                string name = lib.GetProperty("name").GetString()!;
                string? rel = MavenNameToPath(name);
                if (rel == null) continue;
                string full = Path.Combine(LibrariesDir, rel);
                if (!File.Exists(full))
                {
                    // téléchargement direct depuis le dépôt Maven indiqué par Forge
                    string urlBase = lib.TryGetProperty("url", out var u)
                        ? u.GetString()!.TrimEnd('/') : "https://libraries.minecraft.net";
                    string urlPath = rel.Replace('\\', '/');
                    try { await DownloadAsync($"{urlBase}/{urlPath}", full, null, ct, quiet: true); }
                    catch { }
                }
                if (!File.Exists(full))
                    throw new Exception($"Bibliothèque Forge manquante : {name}");
                forgeCp.Add(full);
                progress(Lang.T("Bibliothèques Forge", "Forge libraries"), ++done, libElements.Count);
            }

            // Forge hérite des bibliothèques vanilla (inheritsFrom) : Guava, Gson, LWJGL...
            // viennent de la liste vanilla, complétées par celles spécifiques à Forge.
            var finalCp = new List<string>(forgeCp);
            foreach (var vcp in classpathEntries)
                if (!finalCp.Contains(vcp)) finalCp.Add(vcp);

            return JsonSerializer.Serialize(new
            {
                mainClass = root.GetProperty("mainClass").GetString(),
                classpath = finalCp,
                jar = jarPath, // jar vanilla 1.12.2 (assets etc. déjà prêts)
                natives = NativesDir(versionId),
                assetsIndex = assetsIndexName,
                javaMajor = 8, // Forge 1.12.2 → Java 8
                minecraftArguments = root.TryGetProperty("minecraftArguments", out var fma)
                    ? fma.GetString() : null,
                hasArguments = root.TryGetProperty("arguments", out _),
                jvmArgs = ExtractJvmArgs(root),
                isForge = true
            });
        }

        // ---- 5bis. Fabric : profil officiel depuis meta.fabricmc.net ----
        if (loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
        {
            progress(Lang.T("Installation de Fabric", "Installing Fabric"), 0, 0);
            string fabricId = await EnsureFabricInstalledAsync(versionId, ct);
            string fabricJsonPath = Path.Combine(VersionsDir, fabricId, fabricId + ".json");
            using var fj2 = JsonDocument.Parse(await File.ReadAllTextAsync(fabricJsonPath, ct));
            var froot = fj2.RootElement;

            var fabricCp = new List<string>();
            if (froot.TryGetProperty("libraries", out var flibs))
            {
                foreach (var lib in flibs.EnumerateArray())
                {
                    if (!lib.TryGetProperty("name", out var nEl)) continue;
                    string name = nEl.GetString()!;
                    string? rel = MavenNameToPath(name);
                    if (rel == null) continue;
                    string full = Path.Combine(LibrariesDir, rel);
                    if (!File.Exists(full))
                    {
                        string urlBase = lib.TryGetProperty("url", out var u)
                            ? u.GetString()!.TrimEnd('/') : "https://libraries.minecraft.net";
                        try
                        {
                            await DownloadAsync($"{urlBase}/{rel.Replace('\\', '/')}", full, null, ct, quiet: true);
                        }
                        catch { }
                    }
                    if (File.Exists(full)) fabricCp.Add(full);
                }
            }

            // Fabric ajoute ses libs PAR-DESSUS celles du vanilla
            var fabricFinal = new List<string> { jarPath };
            fabricFinal.AddRange(fabricCp);
            foreach (var vcp in classpathEntries)
                if (!fabricFinal.Contains(vcp)) fabricFinal.Add(vcp);

            return JsonSerializer.Serialize(new
            {
                mainClass = froot.GetProperty("mainClass").GetString(),
                classpath = fabricFinal,
                jar = jarPath,
                natives = NativesDir(versionId),
                assetsIndex = assetsIndexName,
                javaMajor = json.RootElement.TryGetProperty("javaVersion", out var fjv)
                    ? fjv.GetProperty("majorVersion").GetInt32() : 8,
                minecraftArguments = froot.TryGetProperty("minecraftArguments", out var fma2)
                    ? fma2.GetString() : null,
                hasArguments = froot.TryGetProperty("arguments", out _),
                jvmArgs = ExtractJvmArgs(json.RootElement) ?? ExtractJvmArgs(froot),
                isForge = false
            });
        }

        return JsonSerializer.Serialize(new
        {
            mainClass = json.RootElement.GetProperty("mainClass").GetString(),
            classpath = classpathEntries,
            jar = jarPath,
            natives = NativesDir(versionId),
            assetsIndex = assetsIndexName,
            javaMajor = json.RootElement.TryGetProperty("javaVersion", out var jv)
                ? jv.GetProperty("majorVersion").GetInt32() : 8,
            minecraftArguments = json.RootElement.TryGetProperty("minecraftArguments", out var ma)
                ? ma.GetString() : null,
            hasArguments = json.RootElement.TryGetProperty("arguments", out _),
            jvmArgs = ExtractJvmArgs(json.RootElement)
        });
    }

    /// <summary>Installe NeoForge via l'installeur officiel du Maven NeoForged.</summary>
    private static async Task<string> EnsureNeoForgeInstalledAsync(string mcVersion, CancellationToken ct)
    {
        // 1. Versions disponibles (ex: "21.1.77" → MC 1.21.1)
        string meta = await Http.Shared.GetStringAsync(
            "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge", ct);
        using var listDoc = JsonDocument.Parse(meta);
        string prefix = mcVersion.Length > 2 ? mcVersion.Substring(2) : mcVersion; // "1.20.4" → "20.4"
        string? build = listDoc.RootElement.GetProperty("versions")
            .EnumerateArray()
            .Select(v => v.GetString())
            .Where(v => v != null && v.StartsWith(prefix) && !v.Contains('-'))
            .OrderByDescending(v => v)
            .FirstOrDefault();
        if (build == null)
            throw new Exception($"NeoForge n'est pas disponible pour Minecraft {mcVersion}.");

        string neoId = $"neoforge-{build}";
        string jsonPath = Path.Combine(VersionsDir, neoId, neoId + ".json");
        if (File.Exists(jsonPath)) return neoId;

        // 2. Installeur officiel
        Directory.CreateDirectory(RuntimeRoot);
        string installerJar = Path.Combine(RuntimeRoot, "neoforge-installer-" + build + ".jar");
        string installerUrl =
            $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{build}/" +
            $"neoforge-{build}-installer.jar";
        await DownloadAsync(installerUrl, installerJar, null, ct);

        // 3. Installation silencieuse dans notre runtime (Java 17+ requis)
        string? java = GameLauncher.FindJava(17);
        if (java == null)
            throw new Exception("Java 17+ est requis pour installer NeoForge.");

        string profilesPath = Path.Combine(RuntimeRoot, "launcher_profiles.json");
        if (!File.Exists(profilesPath))
            File.WriteAllText(profilesPath, "{\"profiles\":{},\"settings\":{}}");

        var psi = new ProcessStartInfo
        {
            FileName = java,
            Arguments = $"-jar \"{installerJar}\" --installClient \"{RuntimeRoot}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi)!;
        string log = await proc.StandardOutput.ReadToEndAsync(ct) + await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        File.AppendAllText(Path.Combine(RuntimeRoot, "forge-install.log"),
            $"--- neoforge {build} ---\n{log}\n");

        if (!File.Exists(jsonPath))
            throw new Exception("L'installation de NeoForge a échoué (voir runtime\\forge-install.log).");
        return neoId;
    }

    /// <summary>Installe Fabric via le méta-service officiel (profil JSON standard).</summary>
    private static async Task<string> EnsureFabricInstalledAsync(string mcVersion, CancellationToken ct)
    {
        // 1. Dernière version du loader Fabric compatible
        string loaderList = await Http.Shared.GetStringAsync(
            $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}", ct);
        using var list = JsonDocument.Parse(loaderList);
        if (list.RootElement.GetArrayLength() == 0)
            throw new Exception($"Fabric n'est pas disponible pour Minecraft {mcVersion}.");
        string loaderVer = list.RootElement[0].GetProperty("loader")
            .GetProperty("version").GetString()!;

        string fabricId = $"fabric-loader-{loaderVer}-{mcVersion}";
        string jsonPath = Path.Combine(VersionsDir, fabricId, fabricId + ".json");
        if (File.Exists(jsonPath)) return fabricId;

        // 2. Profil de version officiel (format standard Mojang)
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        string profile = await Http.Shared.GetStringAsync(
            $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{loaderVer}/profile/json", ct);
        await File.WriteAllTextAsync(jsonPath, profile, ct);
        return fabricId;
    }

    /// <summary>
    /// Installe Forge officiellement via l'installeur Mojang/Forge (mode --installClient),
    /// exactement comme le font CurseForge et les autres launchers.
    /// </summary>
    private static async Task<string> EnsureForgeInstalledAsync(string mcVersion, CancellationToken ct)
    {
        // 1. Dernière build de Forge pour cette version (promotions officielles)
        string promosJson = await Http.Shared.GetStringAsync(
            "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json", ct);
        using var promos = JsonDocument.Parse(promosJson);
        var p = promos.RootElement.GetProperty("promos");
        string build =
            p.TryGetProperty($"{mcVersion}-recommended", out var rec) ? rec.GetString()! :
            p.TryGetProperty($"{mcVersion}-latest", out var lat) ? lat.GetString()! :
            throw new Exception($"Aucune version de Forge n'existe pour Minecraft {mcVersion}.");

        string forgeId = $"{mcVersion}-forge-{build}";

        string installerJar = Path.Combine(RuntimeRoot,
            "forge-installer-" + mcVersion + "-" + build + ".jar");
        string forgeJsonPath = Path.Combine(VersionsDir, forgeId, forgeId + ".json");

        // 2. Pas encore installée ? Téléchargement de l'installeur officiel + installation silencieuse
        if (!File.Exists(forgeJsonPath))
        {
            string installerUrl =
                $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mcVersion}-{build}/" +
                $"forge-{mcVersion}-{build}-installer.jar";
            await DownloadAsync(installerUrl, installerJar, null, ct);

            // L'installeur Forge exige de trouver un profil de launcher dans le dossier cible
            // (c'est aussi ce que font CurseForge et les autres launchers tiers).
            Directory.CreateDirectory(RuntimeRoot);
            string profilesPath = Path.Combine(RuntimeRoot, "launcher_profiles.json");
            if (!File.Exists(profilesPath))
                File.WriteAllText(profilesPath, "{\"profiles\":{},\"settings\":{}}");

            // Installation silencieuse (Java 8 requis pour les vieilles versions)
            string? java = GameLauncher.FindJava(8);
            if (java == null)
                throw new Exception("Java 8 est requis pour installer Forge sur les anciennes versions.");

            var psi = new ProcessStartInfo
            {
                FileName = java,
                Arguments = $"-jar \"{installerJar}\" --installClient \"{RuntimeRoot}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi)!;
            string log = await proc.StandardOutput.ReadToEndAsync(ct) + await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            File.AppendAllText(Path.Combine(RuntimeRoot, "forge-install.log"),
                $"--- {forgeId} ---\n{log}\n");

            if (!File.Exists(forgeJsonPath))
                throw new Exception("L'installation de Forge a échoué (voir runtime\\forge-install.log).");
        }

        // 3. Le JSON de version généré par l'installeur est INCOMPLET : la liste complète
        // des bibliothèques se trouve dans install_profile.json, embarqué dans l'installeur.
        // On l'extrait à côté — même quand Forge est déjà installé.
        string profilePath = Path.Combine(VersionsDir, forgeId, "install_profile.json");
        if (!File.Exists(profilePath))
        {
            if (!File.Exists(installerJar))
            {
                string installerUrl =
                    $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mcVersion}-{build}/" +
                    $"forge-{mcVersion}-{build}-installer.jar";
                await DownloadAsync(installerUrl, installerJar, null, ct);
            }
            using var zip = ZipFile.OpenRead(installerJar);
            var entry = zip.GetEntry("install_profile.json");
            if (entry != null)
            {
                using var src = entry.Open();
                using var dst = File.Create(profilePath);
                await src.CopyToAsync(dst, ct);
            }
        }

        return forgeId;
    }

    /// <summary>Convertit une coordonnée Maven (groupe:artefact:version[:classifier]) en chemin relatif de fichier .jar.
    /// Les entrées avec suffixe @zip/@jar (artefacts internes de l'installeur, ex: mcp_config) sont ignorées.</summary>
    private static string? MavenNameToPath(string name)
    {
        if (name.Contains('@')) return null; // artefact processeur, pas une dépendance runtime
        var parts = name.Split(':');
        if (parts.Length < 3) return null;
        string group = parts[0].Replace('.', '\\');
        string artifact = parts[1];
        string ver = parts[2];
        string classifier = parts.Length > 3 ? "-" + parts[3] : "";
        return Path.Combine(group, artifact, ver, $"{artifact}-{ver}{classifier}.jar");
    }
    private static async Task<JsonDocument> GetVersionJsonAsync(string versionId, CancellationToken ct)
    {
        // cache local prioritaire : évite un aller-retour réseau à chaque lancement
        string localPath = Path.Combine(VersionsDir, versionId, versionId + ".json");
        if (File.Exists(localPath))
        {
            await using var fs = File.OpenRead(localPath);
            return await JsonDocument.ParseAsync(fs, cancellationToken: ct);
        }

        string manifestJson = await Http.Shared.GetStringAsync(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", ct);
        using var manifest = JsonDocument.Parse(manifestJson);
        foreach (var v in manifest.RootElement.GetProperty("versions").EnumerateArray())
        {
            if ((v.GetProperty("id").GetString()) != versionId) continue;
            string url = v.GetProperty("url").GetString()!;
            string dest = Path.Combine(VersionsDir, versionId, versionId + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await DownloadAsync(url, dest, null, ct, quiet: true);
            await using var fs2 = File.OpenRead(dest);
            return await JsonDocument.ParseAsync(fs2, cancellationToken: ct);
        }
        throw new Exception($"Version « {versionId} » introuvable chez Mojang.");
    }

    /// <summary>Extrait les arguments JVM officiels de la version (G1GC, etc.) en respectant les règles OS.</summary>
    private static List<string>? ExtractJvmArgs(JsonElement root)
    {
        if (!root.TryGetProperty("arguments", out var args)) return null;
        if (!args.TryGetProperty("jvm", out var jvm) || jvm.ValueKind != JsonValueKind.Array) return null;

        var list = new List<string>();
        foreach (var el in jvm.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                list.Add(el.GetString()!);
                continue;
            }
            if (el.ValueKind == JsonValueKind.Object && RulesAllow(el))
            {
                var v = el.GetProperty("value");
                if (v.ValueKind == JsonValueKind.String) list.Add(v.GetString()!);
                else if (v.ValueKind == JsonValueKind.Array)
                    foreach (var s in v.EnumerateArray())
                        if (s.ValueKind == JsonValueKind.String) list.Add(s.GetString()!);
            }
        }
        return list.Count > 0 ? list : null;
    }

    private static bool RulesAllow(JsonElement lib)
    {
        if (!lib.TryGetProperty("rules", out var rules)) return true;
        bool allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            bool applies = true;
            if (rule.TryGetProperty("os", out var os))
                applies = !os.TryGetProperty("name", out var osName) || osName.GetString() == "windows";
            if (applies)
                allowed = rule.GetProperty("action").GetString() == "allow";
        }
        return allowed;
    }

    private static void ExtractNatives(string jarPath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        using var zip = ZipFile.OpenRead(jarPath);
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            string destFile = Path.Combine(destDir, entry.FullName);
            try
            {
                using var src = entry.Open();
                using var dst = File.Create(destFile);
                src.CopyTo(dst);
            }
            catch (IOException) when (File.Exists(destFile))
            {
                // fichier déjà présent et verrouillé par une partie en cours : on le garde
            }
        }
    }

    private static async Task DownloadAsync(string url, string dest, string? sha1,
        CancellationToken ct, bool quiet = false, bool fast = false)
    {
        if (File.Exists(dest))
        {
            if (fast) return; // mode rapide : si le fichier existe, on fait confiance
            if (sha1 == null || await Sha1Async(dest, ct) == sha1) return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var resp = await Http.Shared.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(dest);
                await resp.Content.CopyToAsync(fs, ct);
                return;
            }
            catch when (attempt < 3) { await Task.Delay(500 * attempt, ct); }
            catch when (quiet) { return; }
        }
    }

    private static async Task<string> Sha1Async(string file, CancellationToken ct)
    {
        await using var fs = File.OpenRead(file);
        var bytes = await SHA1.HashDataAsync(fs, ct);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
