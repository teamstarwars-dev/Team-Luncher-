using System.Diagnostics;

namespace TeamLauncher;

public class AccountPage : UserControl, IRefreshable
{
    private readonly RadioButton msRadio = new();
    private readonly RadioButton offRadio = new();
    private readonly TextBox pseudoBox = new();

    public AccountPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(24, 16, 24, 16)
        };

        var title = new Label
        {
            Text = Lang.T("Compte", "Account"),
            ForeColor = Theme.Text,
            Font = Theme.Title,
            AutoSize = true
        };
        var hint = new Label
        {
            Text = Lang.T("Choisis ton mode de connexion. Tu peux aussi le changer au lancement du launcher.", "Choose your login method. You can also change it at launcher startup."),
            ForeColor = Theme.TextDim,
            AutoSize = true
        };

        msRadio.Text = Lang.T("Compte Microsoft officiel (recommandé)", "Official Microsoft account (recommended)");
        msRadio.ForeColor = Theme.Text;
        msRadio.AutoSize = true;
        msRadio.Margin = new Padding(0, 14, 0, 0);

        var msNote = new Label
        {
            Text = "   Connexion officielle par code d'appareil : tu ouvres microsoft.com/link,\n" +
                   "   tu entres le code affiché et ton vrai compte (pseudo + skin automatiques).\n" +
                "   Configuration unique nécessaire : ID client Azure (guide dans docs/auth-microsoft.md).",
            ForeColor = Theme.TextDim,
            AutoSize = true
        };

        var msLoginBtn = new Button { Text = Lang.T("Se connecter maintenant avec Microsoft", "Sign in with Microsoft now"), Width = 320, Height = 40 };
        Theme.Apply(msLoginBtn);
        msLoginBtn.Margin = new Padding(20, 8, 0, 0);
        msLoginBtn.Click += async (_, _) =>
        {
            var session = await MsAuth.LoginAsync(FindForm());
            if (session == null) return;
            DataStore.Settings.AccountMode = "microsoft";
            DataStore.Settings.PlayerName = session.Name;
            DataStore.Save();
            AppEvents.NotifyAccountChanged();
            RefreshData();
                MessageBox.Show(
                    $"Connecté en tant que {session.Name} !\n\n" +
                    Lang.T("Ton vrai pseudo et ton skin seront utilisés en jeu.", "Your real username and skin will be used in-game."),
                    "Team Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        var msLogoutBtn = new Button { Text = "⏻ Se déconnecter de Microsoft", Width = 320, Height = 40 };
        Theme.Apply(msLogoutBtn);
        msLogoutBtn.Margin = new Padding(20, 6, 0, 0);
        msLogoutBtn.Click += (_, _) =>
        {
            MsAuth.Logout();
            DataStore.Settings.AccountMode = "offline";
            DataStore.Save();
            AppEvents.NotifyAccountChanged();
            RefreshData();
            MessageBox.Show(
                Lang.T("Tu es déconnecté de Microsoft (jeton supprimé de ce PC).\n" +
                "Le launcher est repassé en mode hors-ligne : choisis ton pseudo puis Appliquer.\n" +
                "Tu peux te reconnecter à tout moment avec le bouton ci-dessus.",
                "You've been signed out of Microsoft (token removed from this PC).\n" +
                "The launcher has switched to offline mode: choose your username and click Apply.\n" +
                "You can sign back in at any time with the button above."),
                "Team Launcher");
        };

        offRadio.Text = Lang.T("Jouer hors-ligne (pseudo local)", "Play offline (local username)");
        offRadio.ForeColor = Theme.Text;
        offRadio.AutoSize = true;
        offRadio.Margin = new Padding(0, 14, 0, 0);

        Theme.ApplyInput(pseudoBox);
        pseudoBox.PlaceholderText = Lang.T("Pseudo", "Username");
        pseudoBox.Width = 280;
        pseudoBox.Font = new Font("Segoe UI", 11f);
        pseudoBox.Margin = new Padding(20, 8, 0, 0);

        var save = new Button { Text = "Appliquer", Width = 160, Height = 40 };
        Theme.Apply(save, primary: true);
        save.Margin = new Padding(0, 16, 0, 0);
        save.Click += (_, _) =>
        {
            if (msRadio.Checked)
            {
                DataStore.Settings.AccountMode = "microsoft";
            }
            else
            {
                var name = pseudoBox.Text.Trim();
                if (name.Length == 0)
                {
                    MessageBox.Show("Entre un pseudo pour le mode hors-ligne.", "Team Launcher");
                    return;
                }
                DataStore.Settings.AccountMode = "offline";
                DataStore.Settings.PlayerName = name;
            }
            DataStore.Save();
            RefreshData();
            MessageBox.Show("Mode de compte enregistré.", "Team Launcher");
        };

        root.Controls.Add(title);
        root.Controls.Add(hint);
        root.Controls.Add(msRadio);
        root.Controls.Add(msNote);
        root.Controls.Add(msLoginBtn);
        root.Controls.Add(msLogoutBtn);
        root.Controls.Add(offRadio);
        root.Controls.Add(pseudoBox);
        root.Controls.Add(save);

        Controls.Add(root);
    }

    public void RefreshData()
    {
        bool isMs = DataStore.Settings.AccountMode != "offline";
        msRadio.Checked = isMs;
        offRadio.Checked = !isMs;
        pseudoBox.Text = isMs ? "" : DataStore.Settings.PlayerName;
    }
}

public class SettingsPage : UserControl, IRefreshable
{
    private readonly TextBox javaBox = new();
    private readonly NumericUpDown ramBox = new();
    private readonly TextBox dirBox = new();
    private readonly Label colorPreview = new();
    private readonly CheckBox fpsCheck = new();
    private readonly CheckBox discordCheck = new();
    private readonly TextBox discordBox = new();

    public SettingsPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            Padding = new Padding(16, 10, 16, 12)
        };

        var title = new Label
        {
            Text = Lang.T("Paramètres", "Settings"),
            Dock = DockStyle.Top,
            Height = 44,
            ForeColor = Theme.Text,
            Font = Theme.Title,
            TextAlign = ContentAlignment.MiddleLeft
        };

        // onglets dessinés à la main pour rester dans le thème sombre
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            ItemSize = new Size(120, 28),
            SizeMode = TabSizeMode.Fixed,
            Padding = new Point(12, 0)
        };
        Theme.ApplyTab(tabs);
        tabs.DrawItem += (_, e) =>
        {
            bool active = e.Index == tabs.SelectedIndex;
            using var back = new SolidBrush(active ? Theme.Card : Theme.Bg);
            e.Graphics.FillRectangle(back, e.Bounds);
            using var f = new Font("Segoe UI", 8.75f);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, f, e.Bounds,
                active ? Theme.Accent : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        var generalItems = new List<Control>();
        var appearanceItems = new List<Control>();
        var integrationItems = new List<Control>();
        var advancedItems = new List<Control>();

        Theme.ApplyInput(javaBox);
        javaBox.Width = 420;
        javaBox.PlaceholderText = Lang.T("Chemin de Java (vide = détection automatique)", "Java path (empty = auto-detect)");

        Theme.ApplyInput(ramBox);
        ramBox.Minimum = 1;
        ramBox.Maximum = 32;
        ramBox.Value = Math.Clamp(DataStore.Settings.MaxRamGb, 1, 32);
        ramBox.Width = 90;

        Theme.ApplyInput(dirBox);
        dirBox.Width = 420;
        dirBox.ReadOnly = true;

        var save = new Button { Text = Lang.T("Enregistrer les paramètres", "Save settings"), Width = 240, Height = 42 };
        Theme.Apply(save, primary: true);
        save.Margin = new Padding(0, 16, 0, 0);
        save.Click += (_, _) =>
        {
            DataStore.Settings.JavaPath = javaBox.Text.Trim();
            DataStore.Settings.MaxRamGb = (int)ramBox.Value;
            DataStore.Save();
            if (MessageBox.Show("Paramètres enregistrés. Redémarrer pour appliquer ?", "Team Launcher",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Lang.RestartApp();
        };

        generalItems.AddRange(new Control[]
        {
            SettingsLabel("Java (laisser vide pour la détection automatique)"),
            javaBox,
            SettingsLabel("Mémoire maximale allouée à Minecraft (Go)"),
            ramBox,
            SettingsLabel("Dossier des instances"),
            dirBox
        });

        // ---- Personnalisation des couleurs du launcher ----
        colorPreview.AutoSize = false;
        colorPreview.Width = 420;
        colorPreview.Height = 34;
        colorPreview.TextAlign = ContentAlignment.MiddleCenter;
        colorPreview.ForeColor = Color.White;

        var colorsRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0), WrapContents = false };
        colorsRow.Controls.Add(ColorButton("Fond", () => Theme.Bg));
        colorsRow.Controls.Add(ColorButton("Cartes / panneaux", () => Theme.Card));
        colorsRow.Controls.Add(ColorButton("Accent (boutons)", () => Theme.Accent));

        var resetColors = new Button { Text = Lang.T("Couleurs par défaut", "Default colors"), Width = 180, Height = 36, Margin = new Padding(10, 3, 0, 0) };
        Theme.Apply(resetColors);
        resetColors.Click += (_, _) =>
        {
            Theme.Save("", "", "");
            UpdateColorPreview();
            if (MessageBox.Show("Couleurs réinitialisées. Redémarrer pour appliquer ?", "Team Launcher",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Lang.RestartApp();
        };

        appearanceItems.AddRange(new Control[]
        {
            SettingsLabel("Couleurs du launcher"),
            colorPreview,
            colorsRow,
            resetColors
        });

        // ---- Personnalisation — image de fond ----
        root.Controls.Add(SettingsLabel("Personnalisation — image de fond (assombrie automatiquement pour rester lisible)"));
        var bgFileLabel = new Label { ForeColor = Theme.TextDim, AutoSize = true };
        void RefreshBgLabel()
        {
            string p = DataStore.Settings.BackgroundImagePath ?? "";
            bgFileLabel.Text = p.Length == 0 ? "Aucune image de fond." : "Image actuelle : " + Path.GetFileName(p);
        }
        RefreshBgLabel();

        var bgPickBtn = new Button { Text = "🖼 Choisir une image...", Width = 200, Height = 36 };
        Theme.Apply(bgPickBtn);
        bgPickBtn.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Choisir une image de fond",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp"
            };
            if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
            try
            {
                Theme.SetBackground(dlg.FileName);
                RefreshBgLabel();
                if (MessageBox.Show("Image enregistrée ! Redémarrer pour l'appliquer ?", "Team Launcher",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    Lang.RestartApp();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'utiliser cette image :\n" + ex.Message, "Team Launcher");
            }
        };

        var bgClearBtn = new Button { Text = Lang.T("Retirer l'image", "Remove image"), Width = 160, Height = 36, Margin = new Padding(10, 0, 0, 0) };
        Theme.Apply(bgClearBtn);
        bgClearBtn.Click += (_, _) =>
        {
            Theme.ClearBackground();
            RefreshBgLabel();
            if (MessageBox.Show("Image retirée. Redémarrer pour tout appliquer ?", "Team Launcher",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Lang.RestartApp();
        };

        var bgRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        bgRow.Controls.Add(bgPickBtn);
        bgRow.Controls.Add(bgClearBtn);
        appearanceItems.AddRange(new Control[] { bgRow, bgFileLabel });

        // ---- Compteur FPS en jeu (optionnel) ----
        fpsCheck.Text = Lang.T("Compteur FPS/RAM en jeu (optionnel) — installe un petit mod dans chaque instance compatible", "In-game FPS/RAM counter (optional) — installs a small mod in each compatible instance");
        fpsCheck.ForeColor = Theme.Text;
        fpsCheck.AutoSize = true;
        fpsCheck.Margin = new Padding(0, 12, 0, 0);

        var fpsApply = new Button { Text = "Appliquer aux instances", Width = 200, Height = 34 };
        Theme.Apply(fpsApply);
        fpsApply.Margin = new Padding(20, 6, 0, 0);
        fpsApply.Click += (_, _) => ApplyFpsCounter();
        fpsApplyEnabled = enabled => fpsApply.Enabled = enabled;

        advancedItems.AddRange(new Control[] { fpsCheck, fpsApply });

        // ---- Discord Rich Presence ----
        integrationItems.Add(SettingsLabel("Rich Presence Discord — affiche sur ton profil Discord l'instance en cours, la version et le temps de jeu (Discord doit être ouvert)"));
        discordCheck.Text = "Activer la Rich Presence Discord";
        discordCheck.ForeColor = Theme.Text;
        discordCheck.AutoSize = true;
        discordCheck.Margin = new Padding(0, 6, 0, 0);

        Theme.ApplyInput(discordBox);
        discordBox.Width = 420;
        discordBox.PlaceholderText = Lang.T("ID d'application Discord (discord.com/developers/applications)", "Discord Application ID (discord.com/developers/applications)");

        var discordSave = new Button { Text = "Appliquer", Width = 160, Height = 34 };
        Theme.Apply(discordSave);
        discordSave.Margin = new Padding(10, 6, 0, 0);
        discordSave.Click += (_, _) =>
        {
            DataStore.Settings.DiscordEnabled = discordCheck.Checked;
            DataStore.Settings.DiscordAppId = discordBox.Text.Trim();
            DataStore.Save();
            PresenceService.Shutdown();
            if (PresenceService.Enabled) PresenceService.Init();
            MessageBox.Show(PresenceService.Enabled
                ? "Rich Presence activée — ouvre Discord pour voir ton statut !"
                : "Rich Presence désactivée.", "Team Launcher");
        };

        integrationItems.AddRange(new Control[] { discordCheck, discordBox, discordSave });

        // ---- Mises à jour automatiques ----
        integrationItems.Add(SectionHeader("MISES À JOUR AUTOMATIQUES"));
        var updateBox = new TextBox { Width = 420 };
        Theme.ApplyInput(updateBox);
        updateBox.PlaceholderText = "URL du flux de mises à jour (Velopack, optionnel)";
        var updateBtn = new Button { Text = "Enregistrer", Width = 160, Height = 36 };
        Theme.Apply(updateBtn);
        updateBtn.Margin = new Padding(10, 0, 0, 0);
        updateBox.Text = DataStore.Settings.UpdateUrl;
        updateBtn.Click += (_, _) =>
        {
            DataStore.Settings.UpdateUrl = updateBox.Text.Trim();
            DataStore.Save();
            Notifier.Show("Mises à jour", "URL enregistrée.");
        };
        var updateRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        updateRow.Controls.Add(updateBox);
        updateRow.Controls.Add(updateBtn);
        integrationItems.Add(updateRow);

        // version installée + vérification manuelle
        var versionLabel = new Label
        {
            Text = "Version installée : " + Application.ProductVersion,
            ForeColor = Theme.TextDim,
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 2)
        };
        var checkUpdateBtn = new Button { Text = Lang.T("Vérifier maintenant", "Check now"), Width = 180, Height = 36 };
        Theme.Apply(checkUpdateBtn);
        checkUpdateBtn.Click += async (_, _) =>
        {
            checkUpdateBtn.Enabled = false;
            checkUpdateBtn.Text = Lang.T("Vérification...", "Checking...");
            string result = await UpdateChecker.CheckNowAsync();
            checkUpdateBtn.Enabled = true;
            checkUpdateBtn.Text = Lang.T("Vérifier maintenant", "Check now");
            if (result.Length > 0)
                MessageBox.Show(result, "Team Launcher — Mises à jour");
        };
        integrationItems.AddRange(new Control[] { versionLabel, checkUpdateBtn });

        // ---- Actualités + langue ----
        generalItems.Add(SectionHeader("ACTUALITÉS & LANGUE"));
        var newsBox = new TextBox { Width = 420 };
        Theme.ApplyInput(newsBox);
        newsBox.PlaceholderText = Lang.T("URL des actualités (fichier JSON : title, date, tag, text)", "News URL (JSON file: title, date, tag, text)");
        newsBox.Text = DataStore.Settings.NewsUrl;
        var langBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f), BackColor = Theme.Card, ForeColor = Theme.Text
        };
        langBox.Items.AddRange(new object[] { "Français", "English" });
        langBox.SelectedIndex = DataStore.Settings.Language == "en" ? 1 : 0;
        var saveNewsBtn = new Button { Text = "Enregistrer", Width = 160, Height = 36 };
        Theme.Apply(saveNewsBtn);
        saveNewsBtn.Margin = new Padding(10, 0, 0, 0);
        var newsRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        newsRow.Controls.Add(newsBox);
        newsRow.Controls.Add(langBox);
        newsRow.Controls.Add(saveNewsBtn);
        generalItems.Add(newsRow);
        saveNewsBtn.Click += (_, _) =>
        {
            string oldLang = DataStore.Settings.Language;
            DataStore.Settings.NewsUrl = newsBox.Text.Trim();
            DataStore.Settings.Language = langBox.SelectedIndex == 1 ? "en" : "fr";
            DataStore.Save();

            if (DataStore.Settings.Language != oldLang)
            {
                Lang.RestartApp();
                return;
            }

            Notifier.Show("Actualités", "URL des actualités enregistrée.");
        };

        // ---- Clé API CurseForge ----
        integrationItems.Add(SectionHeader("CURSEFORGE"));
        var cfBox = new TextBox { Width = 420 };
        Theme.ApplyInput(cfBox);
        cfBox.PlaceholderText = Lang.T("Clé API CurseForge (console.curseforge.com — gratuite)", "CurseForge API key (console.curseforge.com — free)");
        cfBox.Text = DataStore.Settings.CurseForgeApiKey;
        cfBox.UseSystemPasswordChar = true;
        var cfBtn = new Button { Text = "Enregistrer", Width = 160, Height = 36 };
        Theme.Apply(cfBtn);
        cfBtn.Margin = new Padding(10, 0, 0, 0);
        var cfRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        cfRow.Controls.Add(cfBox);
        cfRow.Controls.Add(cfBtn);
        integrationItems.Add(cfRow);
        cfBtn.Click += (_, _) =>
        {
            DataStore.Settings.CurseForgeApiKey = cfBox.Text.Trim();
            DataStore.Save();
            Notifier.Show("CurseForge", "Clé API enregistrée.");
        };

        // ---- VPS / Pterodactyl ----
        integrationItems.Add(SectionHeader("VPS / PTERODACTYL"));

        var vpsUrlBox = new TextBox { Width = 420 };
        Theme.ApplyInput(vpsUrlBox);
        vpsUrlBox.PlaceholderText = "https://panel.monteam.com";
        vpsUrlBox.Text = DataStore.Settings.VpsUrl;
        var vpsUrlRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        vpsUrlRow.Controls.Add(new Label { Text = "URL du panel :", ForeColor = Theme.TextDim, AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        vpsUrlRow.Controls.Add(vpsUrlBox);
        integrationItems.Add(vpsUrlRow);

        var vpsKeyBox = new TextBox { Width = 420 };
        Theme.ApplyInput(vpsKeyBox);
        vpsKeyBox.PlaceholderText = "Clé API Client Pterodactyl";
        vpsKeyBox.Text = DataStore.Settings.VpsApiKey;
        vpsKeyBox.UseSystemPasswordChar = true;
        var vpsKeyRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        vpsKeyRow.Controls.Add(new Label { Text = "Clé API :", ForeColor = Theme.TextDim, AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        vpsKeyRow.Controls.Add(vpsKeyBox);
        integrationItems.Add(vpsKeyRow);

        var vpsSaveBtn = new Button { Text = "Enregistrer VPS", Width = 160, Height = 36 };
        Theme.Apply(vpsSaveBtn);
        vpsSaveBtn.Margin = new Padding(0, 6, 0, 0);
        var vpsStatus = new Label { Text = "", ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(10, 6, 0, 0) };
        var vpsSaveRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        vpsSaveRow.Controls.Add(vpsSaveBtn);
        vpsSaveRow.Controls.Add(vpsStatus);
        integrationItems.Add(vpsSaveRow);
        vpsSaveBtn.Click += async (_, _) =>
        {
            DataStore.Settings.VpsUrl = vpsUrlBox.Text.Trim();
            DataStore.Settings.VpsApiKey = vpsKeyBox.Text.Trim();
            DataStore.Save();
            try
            {
                var servers = await PterodactylApi.ListServersAsync();
                vpsStatus.ForeColor = Color.FromArgb(80, 200, 120);
                vpsStatus.Text = $"✓ Connecté — {servers.Count} serveur(s) trouvé(s).";
            }
            catch (Exception ex)
            {
                vpsStatus.ForeColor = Color.FromArgb(220, 80, 80);
                vpsStatus.Text = "✕ " + ex.Message.Split('\n')[0];
            }
        };

        // ---- Télémétrie / Logs distants ----
        integrationItems.Add(SectionHeader("TÉLÉMÉTRIE & LOGS DISTANTS"));

        var telemetryCheck = new CheckBox
        {
            Text = "Envoyer les rapports de crash et stats d'utilisation vers Discord",
            ForeColor = Theme.Text,
            AutoSize = true,
            Checked = DataStore.Settings.TelemetryEnabled,
            Margin = new Padding(0, 6, 0, 0)
        };

        var webhookBox = new TextBox { Width = 420 };
        Theme.ApplyInput(webhookBox);
        webhookBox.PlaceholderText = "URL du webhook Discord (clic droit → Copier le lien du webhook)";
        webhookBox.Text = DataStore.Settings.DiscordTelemetryWebhook;
        webhookBox.UseSystemPasswordChar = true;
        var webhookRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        webhookRow.Controls.Add(new Label { Text = "Webhook :", ForeColor = Theme.TextDim, AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        webhookRow.Controls.Add(webhookBox);
        integrationItems.Add(webhookRow);

        var telemetryInfo = new Label
        {
            Text = "Les rapports incluent : crashs Minecraft, crashs du launcher, stats de lancement.\n" +
                   "Aucune donnée personnelle n'est envoyée (pas de pseudo, pas de mots de passe).",
            ForeColor = Theme.TextDim,
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Margin = new Padding(0, 4, 0, 0)
        };

        var telemetrySaveBtn = new Button { Text = "Enregistrer", Width = 160, Height = 36 };
        Theme.Apply(telemetrySaveBtn);
        telemetrySaveBtn.Margin = new Padding(10, 0, 0, 0);
        var telemetrySaveRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        telemetrySaveRow.Controls.Add(telemetrySaveBtn);
        integrationItems.AddRange(new Control[] { telemetryCheck, webhookRow, telemetryInfo, telemetrySaveRow });
        telemetrySaveBtn.Click += (_, _) =>
        {
            DataStore.Settings.TelemetryEnabled = telemetryCheck.Checked;
            DataStore.Settings.DiscordTelemetryWebhook = webhookBox.Text.Trim();
            DataStore.Save();
            Notifier.Show("Télémétrie", telemetryCheck.Checked
                ? "Rapports activés. Les crashs seront envoyés sur Discord."
                : "Rapports désactivés.");
        };

        // ---- Maintenance ----
        var maintenanceRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        var autoUpdateBtn = new Button { Text = $"🔄 Vérifier mise à jour (v{UpdateService.CurrentVersion})", Width = 280, Height = 36 };
        Theme.Apply(autoUpdateBtn);
        autoUpdateBtn.Click += async (_, _) =>
        {
            try { await UpdateService.PromptUpdateAsync(FindForm()!); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
        };
        maintenanceRow.Controls.Add(autoUpdateBtn);
        var cleanBtn = new Button { Text = "🧹 Libérer de l'espace (cache)", Width = 240, Height = 36 };
        Theme.Apply(cleanBtn);
        cleanBtn.Click += (_, _) =>
        {
            try
            {
                var (files, mb) = CleanupService.Run();
                MessageBox.Show($"{files} fichier(s) supprimé(s), {mb:0.##} Mo libérés.", "Team Launcher");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
        };
        var diagBtn = new Button { Text = "🩺 Diagnostic du système", Width = 240, Height = 36, Margin = new Padding(10, 0, 0, 0) };
        Theme.Apply(diagBtn);
        diagBtn.Click += async (_, _) =>
        {
            diagBtn.Enabled = false;
            diagBtn.Text = "Vérification...";
            var checks = await HealthService.RunAllAsync();
            diagBtn.Enabled = true;
            diagBtn.Text = "🩺 Diagnostic du système";

            var dlg = new Form
            {
                Text = "Diagnostic — Team Launcher",
                Size = new Size(560, 120 + checks.Count * 34),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                BackColor = Theme.Card
            };
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(20, 14, 10, 10)
            };
            foreach (var c in checks)
            {
                panel.Controls.Add(new Label
                {
                    Text = (c.Ok ? "✔ " : "✘ ") + c.Name + " — " + c.Detail,
                    ForeColor = c.Ok ? Color.FromArgb(80, 200, 120) : Color.FromArgb(240, 110, 100),
                    Font = new Font("Segoe UI", 10f),
                    AutoSize = true,
                    Margin = new Padding(0, 4, 0, 4)
                });
            }
            dlg.Controls.Add(panel);
            dlg.ShowDialog(FindForm());
        };
        var journalBtn = new Button { Text = "📜 Voir le journal", Width = 180, Height = 36, Margin = new Padding(10, 0, 0, 0) };
        Theme.Apply(journalBtn);
        journalBtn.Click += (_, _) =>
        {
            var dlg = new Form
            {
                Text = "Journal — Team Launcher",
                Size = new Size(720, 480),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Theme.Card
            };
            var box = new TextBox
            {
                Multiline = true, Dock = DockStyle.Fill, ReadOnly = true,
                BackColor = Theme.Bg, ForeColor = Theme.Text,
                Font = new Font("Consolas", 9f), ScrollBars = ScrollBars.Vertical
            };
            try
            {
                box.Text = File.Exists(GameLauncher.LogFile)
                    ? File.ReadAllText(GameLauncher.LogFile) : "Journal vide.";
            }
            catch { box.Text = "Impossible de lire le journal."; }
            dlg.Controls.Add(box);
            dlg.ShowDialog(FindForm());
        };
        maintenanceRow.Controls.Add(cleanBtn);
        maintenanceRow.Controls.Add(diagBtn);
        maintenanceRow.Controls.Add(journalBtn);
        advancedItems.AddRange(new Control[] { SettingsLabel("Maintenance"), maintenanceRow });

        generalItems.Add(save);

        tabs.TabPages.Add(MakeTab("Général", generalItems));
        tabs.TabPages.Add(MakeTab("Apparence", appearanceItems));
        tabs.TabPages.Add(MakeTab("Intégrations", integrationItems));
        tabs.TabPages.Add(MakeTab("Avancé", advancedItems));

        root.Controls.Add(tabs);
        root.Controls.Add(title);
        Controls.Add(root);
    }

    private static Label SectionHeader(string text) => new()
    {
        Text = text,
        ForeColor = Theme.TextDim,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        AutoSize = true,
        Margin = new Padding(0, 20, 0, 4)
    };

    private static TabPage MakeTab(string name, List<Control> items)
    {
        var page = new TabPage(name) { BackColor = Theme.Bg };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(20, 14, 24, 16),
            BackColor = Theme.Bg
        };
        foreach (var c in items) flow.Controls.Add(c);
        page.Controls.Add(flow);
        return page;
    }

    private static Label SettingsLabel(string text) => new()
    {
        Text = text,
        ForeColor = Theme.TextDim,
        AutoSize = true,
        Margin = new Padding(0, 12, 0, 2)
    };

    private Button ColorButton(string label, Func<Color> current)
    {
        var b = new Button { Text = label, Width = 160, Height = 36 };
        Theme.Apply(b);
        b.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = current(), FullOpen = true };
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                UpdateColorPreview(dlg.Color);
        };
        return b;
    }

    private void UpdateColorPreview(Color? bg = null)
    {
        colorPreview.BackColor = bg ?? Theme.Bg;
    }

    private async void ApplyFpsCounter()
    {
        DataStore.Settings.FpsCounterEnabled = fpsCheck.Checked;
        DataStore.Save();

        if (!fpsCheck.Checked)
        {
            MessageBox.Show(
                "Compteur désactivé : les mods déjà installés restent en place.\n" +
                "Tu peux les retirer via l'Explorateur (dossier mods).", "Team Launcher");
            return;
        }

        fpsApplyEnabled?.Invoke(false);
        int ok = 0, skip = 0;
        var errors = new List<string>();
        await Task.Run(() =>
        {
            foreach (var inst in DataStore.Settings.Instances)
            {
                string? loader = inst.Loader.ToLowerInvariant() switch
                {
                    "forge" => "forge",
                    "fabric" => "fabric",
                    "neoforge" => "neoforge",
                    _ => null // vanilla : pas de mods
                };
                if (loader == null) { skip++; continue; }
                try
                {
                    ModrinthApi.DownloadProjectFileAsync("fps-reducer",
                        Path.Combine(DataStore.InstancesRoot, inst.Id, "mods"),
                        loader,
                        inst.McVersion is "latest" or "?" or "" or null ? null : inst.McVersion
                    ).GetAwaiter().GetResult();
                    ok++;
                }
                catch (Exception ex) { errors.Add($"{inst.Name} : {ex.Message}"); }
            }
        });
        fpsApplyEnabled?.Invoke(true);

        MessageBox.Show(
            $"Compteur FPS installé dans {ok} instance(s)" +
            (skip > 0 ? $", {skip} ignorée(s) (vanilla)." : ".") +
            (errors.Count > 0 ? "\nErreurs :\n" + string.Join("\n", errors.Take(5)) : ""),
            "Team Launcher");
    }

    private System.Action<bool>? fpsApplyEnabled;

    public void RefreshData()
    {
        javaBox.Text = DataStore.Settings.JavaPath;
        ramBox.Value = Math.Clamp(DataStore.Settings.MaxRamGb, 1, 32);
        dirBox.Text = DataStore.InstancesRoot;
        fpsCheck.Checked = DataStore.Settings.FpsCounterEnabled;
        discordCheck.Checked = DataStore.Settings.DiscordEnabled;
        discordBox.Text = DataStore.Settings.DiscordAppId;
        UpdateColorPreview();
    }
}
