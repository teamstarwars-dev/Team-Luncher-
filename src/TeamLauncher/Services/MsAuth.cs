using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TeamLauncher;

public sealed record McSession(string Name, string Uuid, string AccessToken);

/// <summary>
/// Authentification Microsoft OFFICIELLE (OAuth device code → Xbox Live → XSTS → Minecraft).
/// Session cachée : si le jeton Minecraft est encore valide (< 1h), on saute toute la chaîne.
/// </summary>
public static class MsAuth
{
    private static readonly HttpClient Http = new();
    private static McSession? _cachedSession;

    private static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TeamLauncher");
    private static string TokenFile => Path.Combine(DataDir, "msauth.json");
    private static string SessionFile => Path.Combine(DataDir, "session-cache.json");

    /// <summary>
    /// Client ID officiel du launcher Minecraft (comme le font Numek, TLauncher & co) :
    /// déjà autorisé par Mojang, donc AUCUNE inscription Azure nécessaire.
    /// </summary>
    public const string DefaultClientId = "00000000402b5328";

    private const string ConnectEndpoint = "https://login.live.com/oauth20_connect.srf";
    private const string TokenEndpoint = "https://login.live.com/oauth20_token.srf";
    private const string AuthScope = "service::user.auth.xboxlive.com::MBI_SSL";

    /// <summary>Toujours vrai : l'ID client est intégré.</summary>
    public static bool IsConfigured => true;

    private static string ClientId => DefaultClientId;

    /// <summary>Retourne la session du compte Microsoft, ou null en cas d'annulation.</summary>
    public static async Task<McSession?> LoginAsync(IWin32Window owner)
    {
        // 0. Vérifier le cache en mémoire (instantané)
        if (_cachedSession is McSession cached && !string.IsNullOrEmpty(cached.AccessToken) && cached.AccessToken != "0")
        {
            AuthLog("Session cache mémoire utilisée (instantané).");
            return cached;
        }

        // 1. Vérifier le cache disque (< 24h)
        var disk = TryLoadSessionCache();
        if (disk != null)
        {
            AuthLog("Session cache disque utilisée (rapide).");
            _cachedSession = disk;
            return disk;
        }

        // 2. Auth complète (Xbox Live → XSTS → MC)
        try
        {
            var session = await RunFlowAsync(owner);
            if (session != null)
            {
                _cachedSession = session;
                SaveSessionCache(session);
            }
            return session;
        }
        catch (Exception ex)
        {
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TeamLauncher", "launcher.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] Échec auth Microsoft : {ex}\n\n");
            }
            catch { }
            MessageBox.Show(owner, "Échec de connexion Microsoft :\n" + ex.Message,
                "Team Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }

    /// <summary>Cache disque de la session Minecraft (valide 1h).</summary>
    private static McSession? TryLoadSessionCache()
    {
        try
        {
            if (!File.Exists(SessionFile)) { AuthLog("Cache disque : introuvable."); return null; }
            string json = File.ReadAllText(SessionFile);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Vérifier l'âge (< 24h)
            if (root.TryGetProperty("ts", out var tsEl))
            {
                long ts = tsEl.GetInt64();
                long age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts;
                if (age > 86400) { AuthLog("Cache disque : expiré (>${age}s)."); return null; }
            }

            string name = root.GetProperty("name").GetString()!;
            string uuid = root.GetProperty("uuid").GetString()!;
            string token = root.GetProperty("token").GetString()!;

            if (string.IsNullOrEmpty(token) || token == "0") { AuthLog("Cache disque : token vide."); return null; }
            AuthLog($"Cache disque : OK (nom={name}).");
            return new McSession(name, uuid, token);
        }
        catch (Exception ex) { AuthLog("Cache disque : erreur " + ex.Message); return null; }
    }

    private static void SaveSessionCache(McSession session)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var obj = new
            {
                name = session.Name,
                uuid = session.Uuid,
                token = session.AccessToken,
                ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            File.WriteAllText(SessionFile, JsonSerializer.Serialize(obj));
        }
        catch { }
    }

    public static McSession OfflineSession(string name)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        var uuid = new Guid(hash[..16]).ToString("N");
        return new McSession(name, uuid, "0");
    }

    /// <summary>Se déconnecte : efface le jeton sauvegardé localement.</summary>
    public static void Logout()
    {
        try { if (File.Exists(TokenFile)) File.Delete(TokenFile); } catch { }
        AuthLog("Déconnexion : jeton supprimé.");
    }

    // ---------------- flux complet ----------------

    private static async Task<McSession?> RunFlowAsync(IWin32Window owner)
    {
        string clientId = ClientId;
        string msAccessToken;

        if (TryLoadRefreshToken() is string refreshToken &&
            await TryRefreshAsync(clientId, refreshToken) is string renewed)
        {
            msAccessToken = renewed;
        }
        else
        {
            // 1. Demande de code appareil (endpoint Live Connect historique)
            using var dcdoc = await PostFormAsync(ConnectEndpoint,
                new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["response_type"] = "device_code",
                    ["scope"] = AuthScope
                });

            if (!dcdoc.RootElement.TryGetProperty("user_code", out var codeEl))
            {
                string detail = dcdoc.RootElement.TryGetProperty("error_description", out var errDesc)
                    ? errDesc.GetString() ?? "" : dcdoc.RootElement.GetRawText();
                throw new Exception("Microsoft a refusé la demande de connexion.\nDétail technique : " + detail);
            }
            string userCode = codeEl.GetString()!;
            // Lien avec le code pré-rempli si Microsoft le fournit, sinon la page standard
            string verifyUri = dcdoc.RootElement.TryGetProperty("verification_uri_complete", out var vuc)
                ? vuc.GetString()!
                : dcdoc.RootElement.GetProperty("verification_uri").GetString()!;
            string deviceCode = dcdoc.RootElement.GetProperty("device_code").GetString()!;
            int interval = 5;

            // Ouvre automatiquement le navigateur sur la page de connexion Microsoft
            try
            {
                Process.Start(new ProcessStartInfo(verifyUri) { UseShellExecute = true });
            }
            catch { }

            ShowCodeDialog(owner, verifyUri, userCode);

            // 2. Attente de la connexion du joueur
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval));
                using var tok = await PostFormAsync(TokenEndpoint,
                    new Dictionary<string, string>
                    {
                        ["client_id"] = clientId,
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                        ["device_code"] = deviceCode
                    });
                if (tok.RootElement.TryGetProperty("access_token", out var at))
                {
                    msAccessToken = at.GetString()!;
                    SaveRefreshToken(tok.RootElement.GetProperty("refresh_token").GetString()!);
                    break;
                }
                var err = tok.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "";
                if (err == "authorization_pending") continue;
                if (err == "slow_down") { interval += 5; continue; }
                if (err == "authorization_declined")
                    throw new Exception("Connexion refusée depuis la page Microsoft.");
                if (err == "expired_token")
                    throw new Exception("Le code a expiré avant d'être validé. Relancez la connexion.");
                throw new Exception("Connexion refusée : " + err);
            }
        }

        return await XboxToMinecraftAsync(msAccessToken);
    }

    /// <summary>
    /// Renouvellement silencieux du jeton ; retourne null si le jeton est
    /// absent, expiré ou incompatible (dans ce cas il est supprimé).
    /// </summary>
    private static async Task<string?> TryRefreshAsync(string clientId, string refreshToken)
    {
        try
        {
            var tok = await PostFormAsync(TokenEndpoint,
                new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["scope"] = AuthScope
                });
            string access = tok.RootElement.GetProperty("access_token").GetString()!;
            SaveRefreshToken(tok.RootElement.GetProperty("refresh_token").GetString()!);
            return access;
        }
        catch
        {
            try { File.Delete(TokenFile); } catch { }
            return null;
        }
    }

    private static async Task<McSession> XboxToMinecraftAsync(string msAccessToken)
    {
        void StepLog(string s)
        {
            try
            {
                var p = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TeamLauncher", "launcher.log");
                File.AppendAllText(p, $"[{DateTime.Now:HH:mm:ss}] [Auth] {s}\n");
            }
            catch { }
        }

        // 3. Xbox Live
        // NB : les jetons du flux legacy login.live.com se passent SANS préfixe "d="
        StepLog($"Xbox Live : envoi du jeton Microsoft ({msAccessToken.Length} car.)");
        using var xbl = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate",
            new Dictionary<string, object?>
            {
                ["Properties"] = new Dictionary<string, string>
                {
                    ["AuthMethod"] = "RPS",
                    ["SiteName"] = "user.auth.xboxlive.com",
                    ["RpsTicket"] = msAccessToken
                },
                ["RelyingParty"] = "http://auth.xboxlive.com",
                ["TokenType"] = "JWT"
            });
        string uhs = xbl.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0]
            .GetProperty("uhs").GetString()!;
        string xblToken = xbl.RootElement.GetProperty("Token").GetString()!;
        StepLog("Xbox Live : OK");

        // 4. XSTS
        using var xsts = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize",
            new Dictionary<string, object?>
            {
                ["Properties"] = new Dictionary<string, object?>
                {
                    ["SandboxId"] = "RETAIL",
                    ["UserTokens"] = new[] { xblToken }
                },
                ["RelyingParty"] = "rp://api.minecraftservices.com/",
                ["TokenType"] = "JWT"
            });
        string xstsToken = xsts.RootElement.GetProperty("Token").GetString()!;
        StepLog("XSTS : OK");

        // 5. Jeton Minecraft
        using var mcLogin = await PostJsonAsync(
            "https://api.minecraftservices.com/authentication/login_with_xbox",
            new Dictionary<string, object?>
            {
                ["identityToken"] = $"XBL3.0 x={uhs};{xstsToken}"
            });
        string mcToken;
        if (mcLogin.RootElement.TryGetProperty("access_token", out var atEl))
            mcToken = atEl.GetString()!;
        else
        {
            var brut = mcLogin.RootElement.GetRawText();
            StepLog("login_with_xbox réponse inattendue : " + brut);
            if (brut.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase))
                throw new Exception(
                    "L'ID client Azure du launcher n'est pas encore validé par Mojang.\n\n" +
                    "Depuis 2025, Mojang exige que chaque application soit approuvée manuellement\n" +
                    "(formulaire officiel : https://aka.ms/mce-reviewappid — délai ~3-4 semaines).\n" +
                    "La connexion fonctionnera dès que l'approbation sera accordée ; ce n'est pas un bug du launcher.");
            throw new Exception(
                "Réponse inattendue des serveurs Minecraft :\n" +
                (brut.Length > 300 ? brut[..300] : brut));
        }
        StepLog("Jeton Minecraft : OK");

        // 6. Profil
        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://api.minecraftservices.com/minecraft/profile");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcToken);
        using var profResp = await Http.SendAsync(req);
        StepLog("Profil : HTTP " + (int)profResp.StatusCode);
        var profBody = await profResp.Content.ReadAsStringAsync();
        if ((int)profResp.StatusCode == 404)
            throw new Exception(
                "Ton compte Microsoft ne possède pas encore de profil Minecraft Java.\n" +
                "Lance une fois Minecraft (même en solo) depuis le launcher officiel\n" +
                "pour créer le profil, puis retente.");
        JsonDocument prof;
        try { prof = JsonDocument.Parse(profBody); }
        catch { throw new Exception("Réponse profil invalide : " + profBody); }
        using (prof)
        {
            if (!prof.RootElement.TryGetProperty("name", out var nameEl))
                throw new Exception("Ce compte ne possède pas Minecraft Java.");
            string rawId = prof.RootElement.GetProperty("id").GetString()!;
            string name = nameEl.GetString()!;
            StepLog("Profil trouvé : " + name);

            // Skin officiel : récupéré à la source via les textures du profil
            await SaveOfficialSkinAsync(name, prof.RootElement);

            return new McSession(name, FormatUuid(rawId), mcToken);
        }
    }

    /// <summary>
    /// Décode la propriété « textures » du profil (base64) et télécharge le skin
    /// officiel vers skins\{pseudo}.png. Silencieux en cas d'échec (le jeu s'en fiche).
    /// </summary>
    private static async Task SaveOfficialSkinAsync(string name, JsonElement profile)
    {
        try
        {
            foreach (var p in profile.GetProperty("properties").EnumerateArray())
            {
                if (p.TryGetProperty("name", out var pn) && pn.GetString() != "textures") continue;

                byte[] decoded = Convert.FromBase64String(p.GetProperty("value").GetString()!);
                using var texDoc = JsonDocument.Parse(decoded);
                string url = texDoc.RootElement
                    .GetProperty("textures")
                    .GetProperty("SKIN")
                    .GetProperty("url").GetString()!;

                Directory.CreateDirectory(DataStore.SkinsDir);
                byte[] png = await Http.GetByteArrayAsync(url);
                File.WriteAllBytes(Path.Combine(DataStore.SkinsDir, name + ".png"), png);
                AuthLog("Skin officiel enregistré (" + png.Length + " octets).");
                return;
            }
            AuthLog("Pas de texture de skin dans le profil.");
        }
        catch (Exception ex)
        {
            AuthLog("Récupération du skin impossible : " + ex.Message);
        }
    }

    private static void AuthLog(string text)
    {
        try
        {
            File.AppendAllText(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TeamLauncher", "launcher.log"), $"[{DateTime.Now:HH:mm:ss}] [Auth] {text}\n");
        }
        catch { }
    }

    private static string FormatUuid(string hex)
    {
        if (hex.Length != 32) return hex;
        return $"{hex[..8]}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}-{hex.Substring(16, 4)}-{hex[20..]}";
    }

    // ---------------- helpers ----------------

    private static async Task<JsonDocument> PostFormAsync(string url, Dictionary<string, string> form)
    {
        var resp = await Http.PostAsync(url, new FormUrlEncodedContent(form));
        var content = await resp.Content.ReadAsStringAsync();
        try { return JsonDocument.Parse(content); }
        catch
        {
            throw new Exception(
                $"HTTP {(int)resp.StatusCode} depuis {url}\nRéponse : {(content.Length > 400 ? content[..400] : content)}");
        }
    }

    private static async Task<JsonDocument> PostJsonAsync(string url, Dictionary<string, object?> body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (url.Contains("xboxlive.com"))
        {
            // En-têtes exigés par les services Xbox Live
            req.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");
            req.Headers.Accept.ParseAdd("application/json");
        }
        using var resp = await Http.SendAsync(req);
        var content = await resp.Content.ReadAsStringAsync();
        try { return JsonDocument.Parse(content); }
        catch
        {
            string detail = content.Length > 400 ? content[..400] : content;
            if ((int)resp.StatusCode == 401 && url.Contains("user.auth.xboxlive.com"))
                throw new Exception(
                    "Xbox Live a refusé le jeton Microsoft (HTTP 401).\n" +
                    "Relance la connexion : si l'erreur persiste, déconnecte-toi puis\n" +
                    "reconnecte-toi sur account.microsoft.com avant de réessayer.");
            throw new Exception(
                $"HTTP {(int)resp.StatusCode} depuis {url}\nRéponse : {detail}");
        }
    }

    private static string? TryLoadRefreshToken()
    {
        try
        {
            if (!File.Exists(TokenFile)) return null;
            string raw = File.ReadAllText(TokenFile).Trim();
            if (raw.Length == 0) return null;
            try
            {
                // Format chiffré (DPAPI, lié au compte Windows)
                byte[] clear = System.Security.Cryptography.ProtectedData.Unprotect(
                    Convert.FromBase64String(raw), null,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(clear);
            }
            catch
            {
                // Ancien format en clair (avant chiffrement)
                return raw;
            }
        }
        catch { return null; }
    }

    private static void SaveRefreshToken(string token)
    {
        Directory.CreateDirectory(DataDir);
        byte[] cipher = System.Security.Cryptography.ProtectedData.Protect(
            Encoding.UTF8.GetBytes(token), null,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        // Écriture atomique pour éviter les conflits de verrou (autre instance, antivirus).
        var tmp = TokenFile + ".tmp";
        File.WriteAllText(tmp, Convert.ToBase64String(cipher));
        if (File.Exists(TokenFile)) File.Replace(tmp, TokenFile, null);
        else File.Move(tmp, TokenFile);
    }

    private static void ShowCodeDialog(IWin32Window owner, string uri, string code)
    {
        var dlg = new Form
        {
            Text = "Connexion Microsoft",
            Size = new Size(520, 360),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            BackColor = Theme.Panel
        };

        var top = new Label
        {
            Dock = DockStyle.Top, Height = 64,
            Text = "Ton navigateur s'est ouvert sur la page de connexion Microsoft.\n" +
                   "Entre ce code quand la page te le demande :",
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10.5f)
        };

        // Le code en TRÈS gros, impossible à rater
        var codeLbl = new Label
        {
            Dock = DockStyle.Top, Height = 90,
            Text = code,
            ForeColor = Theme.Accent,
            BackColor = Theme.Card,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Consolas", 34f, FontStyle.Bold)
        };
        Theme.Blockify(codeLbl);
        codeLbl.Margin = new Padding(20, 8, 20, 0);

        var bottom = new Label
        {
            Dock = DockStyle.Bottom, Height = 40,
            Text = $"Si la page ne s'est pas ouverte : {uri}",
            ForeColor = Theme.TextDim,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9f)
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(8, 8, 0, 0)
        };
        var open = new Button { Text = "Rouvrir la page", Width = 170, Height = 38 };
        Theme.Apply(open, primary: true);
        open.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
        };
        var copy = new Button { Text = "Copier le code", Width = 170, Height = 38 };
        Theme.Apply(copy, primary: false);
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(code); copy.Text = "✓ Copié"; } catch { }
        };
        buttons.Controls.Add(open);
        buttons.Controls.Add(copy);

        dlg.Controls.Add(codeLbl);
        dlg.Controls.Add(top);
        dlg.Controls.Add(buttons);
        dlg.Controls.Add(bottom);
        dlg.Show(owner);
    }
}


