using System.Diagnostics;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Page Serveurs façon écran multijoueur de Minecraft :
/// • « Mes serveurs » : serveurs Minecraft hébergés depuis le launcher
///   (téléchargement officiel Mojang, démarrage/arrêt, console, import de map)
/// • « Villes de la team » : les villes RP des membres (ping en direct, partage)
/// • « Serveurs favoris » : liste d'adresses avec ping en direct.
/// </summary>
public class ServersPage : UserControl, IRefreshable
{
    private readonly FlowLayoutPanel serverList = new();
    private readonly TextBox addressBox = new();
    private readonly FlowLayoutPanel cityList = new();
    private string? _selectedCityId;
    private FlowLayoutPanel? _hostedList;
    private string? _selectedAddress;
    private readonly Dictionary<string, Label> _statusLabels = new();
    private List<PterodactylApi.PtServer> _vpsServers = new();

    public ServersPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        // ================= PAGE EN ONGLETS =================

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            ItemSize = new Size(140, 30),
            Font = new Font("Segoe UI", 8.75f),
            Padding = new Point(10, 2)
        };
        Theme.ApplyTab(tabs);
        tabs.DrawItem += (_, e) =>
        {
            bool sel = tabs.SelectedIndex == e.Index;
            using (var b = new SolidBrush(sel ? Theme.Card : Theme.Bg))
                e.Graphics.FillRectangle(b, e.Bounds);
            if (sel)
                using (var b = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Bottom - 2, e.Bounds.Width, 2);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text,
                new Font("Segoe UI", 8.75f), e.Bounds,
                sel ? Theme.Text : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        // ================= ONGLET 1 : MES SERVEURS (PTERODACTYL) =================

        var hostedPage = new TabPage("Mes serveurs") { BackColor = Theme.Bg };

        // --- Barre de boutons ---
        var btnBar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = ControlPaint.Dark(Theme.Card, 0.02f), Padding = new Padding(12, 6, 0, 0) };

        var openPanelBtn = new Button
        {
            Text = Lang.T("🌐 Ouvrir le panel", "🌐 Open panel"),
            Height = 32, AutoSize = true, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Theme.Accent, BackColor = Color.Transparent, Cursor = Cursors.Hand
        };
        openPanelBtn.FlatAppearance.BorderSize = 0;
        openPanelBtn.Location = new Point(12, 6);
        openPanelBtn.Click += (_, _) =>
        {
            string url = DataStore.Settings.VpsUrl.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(
                    Lang.T("Configure l'URL du panel Pterodactyl dans les paramètres.", "Configure the Pterodactyl panel URL in settings."),
                    "Team Launcher");
                return;
            }
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        };

        var refreshBtn = new Button
        {
            Text = Lang.T("🔄 Rafraîchir", "🔄 Refresh"),
            Height = 32, AutoSize = true, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Theme.TextDim, BackColor = Color.Transparent, Cursor = Cursors.Hand
        };
        refreshBtn.FlatAppearance.BorderSize = 0;
        refreshBtn.Location = new Point(220, 6);
        refreshBtn.Click += async (_, _) => await LoadVpsServersAsync();

        btnBar.Controls.AddRange(new Control[] { openPanelBtn, refreshBtn });

        // --- Liste des serveurs VPS ---
        _hostedList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, BackColor = Theme.Bg,
            Padding = new Padding(12, 8, 12, 12)
        };

        hostedPage.Controls.Add(_hostedList);
        hostedPage.Controls.Add(btnBar);

        // ================= ONGLET 2 : SERVEURS FAVORIS =================

        var favPage = new TabPage("Favoris") { BackColor = Theme.Bg };
        var favPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = Theme.Bg, Padding = new Padding(24, 12, 24, 8)
        };
        favPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        favPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        favPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        favPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        favPanel.Controls.Add(new Label
        {
            Text = Lang.T(
                "Tes serveurs favoris : statut en direct, joueurs connectés, MOTD. Double-clic pour rejoindre.",
                "Your favorite servers: live status, connected players, MOTD. Double-click to join."),
            ForeColor = Theme.TextDim, AutoSize = true, Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);

        var btnRow = new Panel { Height = 52, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 8) };
        addressBox.SetBounds(0, 8, 360, 32);
        addressBox.Font = new Font("Consolas", 10f);
        addressBox.BorderStyle = BorderStyle.None;
        addressBox.BackColor = Theme.Card;
        addressBox.ForeColor = Theme.Text;
        addressBox.Padding = new Padding(4);
        addressBox.PlaceholderText = Lang.T("adresse.du.serveur.fr", "address.of.the.server.com");

        var addBtn = MkBtn("+ Ajouter", primary: true, x: 370, w: 150);
        addBtn.Click += (_, _) => AddServer();
        addressBox.KeyPress += (_, e) =>
        {
            if (e.KeyChar == (char)13) { AddServer(); e.Handled = true; }
        };
        var delBtn = MkBtn("Retirer", primary: false, x: 528, w: 130);
        delBtn.Click += (_, _) =>
        {
            if (_selectedAddress == null) return;
            DataStore.Settings.Servers.Remove(_selectedAddress);
            _selectedAddress = null;
            DataStore.Save();
            RefreshData();
        };
        var favRefreshBtn = MkBtn("Actualiser", primary: false, x: 666, w: 160);
        favRefreshBtn.Click += async (_, _) => await PingAllAsync();

        btnRow.Controls.Add(addressBox);
        btnRow.Controls.Add(addBtn);
        btnRow.Controls.Add(delBtn);
        btnRow.Controls.Add(favRefreshBtn);

        favPanel.Controls.Add(btnRow, 0, 1);

        serverList.FlowDirection = FlowDirection.TopDown;
        serverList.WrapContents = false;
        serverList.AutoScroll = true;
        serverList.BackColor = Theme.Bg;

        favPanel.Controls.Add(serverList, 0, 2);
        favPage.Controls.Add(favPanel);

        // ================= ONGLET 3 : VILLES DE LA TEAM =================

        var cityPage = new TabPage("Villes de la team") { BackColor = Theme.Bg };
        var cityPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = Theme.Bg, Padding = new Padding(24, 12, 24, 8)
        };
        cityPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cityPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cityPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cityPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        cityPanel.Controls.Add(new Label
        {
            Text = Lang.T(
                "Les villes RP des membres. Ajoute ta ville, partage-la d'un clic. Double-clic pour rejoindre.",
                "Members' RP cities. Add your city, share it in one click. Double-click to join."),
            ForeColor = Theme.TextDim, AutoSize = true, Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);

        var cityBtnRow = new Panel { Height = 52, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 8) };
        var addCityBtn = MkBtn("+ Ajouter ma ville", primary: true, x: 0, w: 200);
        addCityBtn.Click += (_, _) => EditCity(null);
        var editCityBtn = MkBtn("Modifier", primary: false, x: 208, w: 130);
        editCityBtn.Click += (_, _) =>
        {
            var c = SelectedCity();
            if (c != null) EditCity(c);
        };
        var shareCityBtn = MkBtn("Copier", primary: false, x: 346, w: 140);
        shareCityBtn.Click += (_, _) => ShareCity();
        var importCityBtn = MkBtn("Importer", primary: false, x: 494, w: 150);
        importCityBtn.Click += (_, _) => ImportCity();
        var delCityBtn = MkBtn("Retirer", primary: false, x: 652, w: 140);
        delCityBtn.Click += (_, _) =>
        {
            var c = SelectedCity();
            if (c == null) return;
            DataStore.Settings.Cities.Remove(c);
            _selectedCityId = null;
            DataStore.Save();
            RefreshData();
        };

        cityBtnRow.Controls.Add(addCityBtn);
        cityBtnRow.Controls.Add(editCityBtn);
        cityBtnRow.Controls.Add(shareCityBtn);
        cityBtnRow.Controls.Add(importCityBtn);
        cityBtnRow.Controls.Add(delCityBtn);

        cityPanel.Controls.Add(cityBtnRow, 0, 1);

        cityList.FlowDirection = FlowDirection.TopDown;
        cityList.WrapContents = false;
        cityList.AutoScroll = true;
        cityList.BackColor = Theme.Bg;

        cityPanel.Controls.Add(cityList, 0, 2);
        cityPage.Controls.Add(cityPanel);

        tabs.TabPages.AddRange(new[] { hostedPage, favPage, cityPage });
        Controls.Add(tabs);

        ServerHost.StateChanged += OnHostStateChanged;
        ServerHost.DownloadProgress += OnDownloadProgress;
        ServerHost.TunnelAddressFound += (id, address) =>
        {
            var hs = DataStore.Settings.HostedServers.FirstOrDefault(h => h.Id == id);
            if (hs == null) return;
            hs.PublicAddress = address;
            try { DataStore.Save(); } catch { }
            Notifier.Show(hs.Name, Lang.T(
                $"Adresse publique : {address} 🎉",
                $"Public address: {address} 🎉"));
            if (IsHandleCreated) BeginInvoke(RefreshData);
        };
    }

    private void OnDownloadProgress(string id, int pct)
    {
        if (!IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (_statusLabels.TryGetValue(id, out var lbl) && !lbl.IsDisposed)
                    lbl.Text = Lang.T($"⬇ Téléchargement… {pct} %", $"⬇ Downloading… {pct}%");
            });
        }
        catch { }
    }

    private void OnHostStateChanged()
    {
        if (!IsHandleCreated) return;
        BeginInvoke(() => RefreshData());
    }

    // ---------------- serveurs VPS (Pterodactyl) ----------------

    private async Task LoadVpsServersAsync()
    {
        if (_hostedList == null) return;
        _hostedList.SuspendLayout();
        _hostedList.Controls.Clear();

        if (!PterodactylApi.IsConfigured)
        {
            _hostedList.Controls.Add(new Label
            {
                Text = Lang.T(
                    "Aucun VPS configuré.\n\n" +
                    "1. Installe Pterodactyl sur ton VPS\n" +
                    "2. Génère une clé API Client\n" +
                    "3. Configure dans Paramètres → VPS / Pterodactyl",
                    "No VPS configured.\n\n" +
                    "1. Install Pterodactyl on your VPS\n" +
                    "2. Generate a Client API key\n" +
                    "3. Configure in Settings → VPS / Pterodactyl"),
                ForeColor = Theme.TextDim, AutoSize = true, Margin = new Padding(6, 10, 0, 0)
            });
            _hostedList.ResumeLayout();
            return;
        }

        _hostedList.Controls.Add(new Label
        {
            Text = Lang.T("Chargement des serveurs…", "Loading servers…"),
            ForeColor = Theme.TextDim, AutoSize = true, Margin = new Padding(6, 10, 0, 0)
        });

        _hostedList.ResumeLayout();
        try
        {
            _vpsServers = await PterodactylApi.ListServersAsync();
        }
        catch (Exception ex)
        {
            _hostedList.SuspendLayout();
            _hostedList.Controls.Clear();
            _hostedList.Controls.Add(new Label
            {
                Text = Lang.T("Erreur de connexion au panel :\n", "Panel connection error:\n") + ex.Message,
                ForeColor = Color.FromArgb(220, 80, 80), AutoSize = true, Margin = new Padding(6, 10, 0, 0)
            });
            _hostedList.ResumeLayout();
            return;
        }

        _hostedList.SuspendLayout();
        _hostedList.Controls.Clear();

        if (_vpsServers.Count == 0)
        {
            _hostedList.Controls.Add(new Label
            {
                Text = Lang.T(
                    "Aucun serveur trouvé sur le panel.\nCrée un serveur depuis le panel Pterodactyl.",
                    "No servers found on the panel.\nCreate a server from the Pterodactyl panel."),
                ForeColor = Theme.TextDim, AutoSize = true, Margin = new Padding(6, 10, 0, 0)
            });
        }
        else
        {
            foreach (var srv in _vpsServers)
                _hostedList.Controls.Add(MakeVpsServerCard(srv));
        }

        _hostedList.ResumeLayout();
    }

    private Panel MakeVpsServerCard(PterodactylApi.PtServer srv)
    {
        bool running = srv.Status == "running";
        var card = new Panel
        {
            Height = 80,
            Width = Math.Max(880, Parent?.Width ?? 900),
            BackColor = running ? ControlPaint.Dark(Theme.Accent, 0.55f) : Theme.Card,
            Margin = new Padding(0, 6, 14, 6)
        };
        Theme.Blockify(card);

        var nameLbl = new Label
        {
            Text = srv.Name,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Location = new Point(14, 8), AutoSize = true
        };

        var info = new Label
        {
            Text = running
                ? $"● EN LIGNE  •  Node: {srv.Node}"
                : $"○ {srv.Status}",
            ForeColor = running ? Theme.Accent : Theme.TextDim,
            Font = new Font("Segoe UI", 9f),
            Location = new Point(14, 30), AutoSize = true
        };

        // Adresse du serveur (IP:port) — récupérée depuis les allocations
        var addrLabel = new Label
        {
            Text = Lang.T("Adresse : chargement…", "Address: loading…"),
            ForeColor = Theme.Accent,
            Font = new Font("Consolas", 9.5f),
            Location = new Point(14, 50), AutoSize = true
        };

        _ = Task.Run(async () =>
        {
            try
            {
                var allocs = await PterodactylApi.GetAllocationsAsync(srv.Id);
                if (allocs.Count > 0)
                {
                    var a = allocs[0];
                    string addr = $"{a.Ip}:{a.Port}";
                    card.BeginInvoke(() =>
                    {
                        if (card.IsDisposed) return;
                        addrLabel.Text = $"🔗 {addr}";
                    });
                }
                else
                {
                    card.BeginInvoke(() =>
                    {
                        if (card.IsDisposed) return;
                        addrLabel.Text = Lang.T("⚠ Aucune allocation", "⚠ No allocation");
                    });
                }
            }
            catch
            {
                card.BeginInvoke(() =>
                {
                    if (card.IsDisposed) return;
                    addrLabel.Text = Lang.T("⚠ Erreur de connexion", "⚠ Connection error");
                });
            }
        });

        // Monitoring CPU/RAM
        var monitorLabel = new Label
        {
            Text = "",
            ForeColor = Theme.Accent,
            Font = new Font("Consolas", 8.5f),
            Location = new Point(350, 30), AutoSize = true
        };

        if (running)
        {
            var monitorTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            async void UpdateMonitor(object? _, EventArgs __)
            {
                if (!running || card.IsDisposed)
                {
                    monitorTimer.Stop();
                    monitorTimer.Dispose();
                    return;
                }
                try
                {
                    var state = await PterodactylApi.GetServerStateAsync(srv.Id);
                    if (!card.IsDisposed)
                        monitorLabel.Text = $"📊 CPU: {state.CpuPercent}%  •  RAM: {state.MemUsedBytes / 1024 / 1024} Mo";
                }
                catch { }
            }
            monitorTimer.Tick += UpdateMonitor;
            monitorTimer.Start();
            card.Disposed += (_, _) => { monitorTimer.Stop(); monitorTimer.Dispose(); };
        }

        // Boutons d'action
        int btnX = card.Width - 46;

        var panelBtn = MkBtn("🌐", primary: false, x: 0, w: 44);
        panelBtn.Location = new Point(btnX, 10);
        panelBtn.Click += (_, _) =>
        {
            string url = DataStore.Settings.VpsUrl.Trim();
            if (!string.IsNullOrWhiteSpace(url))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        };

        var copyBtn = MkBtn("📋", primary: false, x: 0, w: 44);
        copyBtn.Location = new Point(btnX - 50, 10);
        copyBtn.Click += (_, _) =>
        {
            try
            {
                string addrText = addrLabel.Text.Replace("🔗 ", "").Trim();
                if (!addrText.Contains("Erreur") && !addrText.Contains("chargement") && !addrText.Contains("allocation"))
                {
                    Clipboard.SetText(addrText);
                    copyBtn.Text = "✓";
                    var restore = new System.Windows.Forms.Timer { Interval = 1800 };
                    restore.Tick += (_, _) => { copyBtn.Text = "📋"; restore.Stop(); restore.Dispose(); };
                    restore.Start();
                }
            }
            catch { }
        };

        card.Controls.AddRange(new Control[] { nameLbl, info, addrLabel, monitorLabel, panelBtn, copyBtn });
        return card;
    }

    /// <summary>Éditeur du server.properties (port, MOTD, difficulté, whitelist…) + options de redémarrage.</summary>
    private void ShowConfigDialog(HostedServer s)
    {
        if (ServerHost.IsRunning(s))
        {
            MessageBox.Show("Arrête le serveur avant de modifier sa configuration.", "Team Launcher");
            return;
        }
        ServerHost.ApplyProperties(s);

        var dlg = new Form
        {
            Text = $"Configuration — {s.Name}",
            Size = new Size(660, 640), StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Bg
        };

        // ---- options de démarrage / sauvegardes ----
        var top = new Panel { Dock = DockStyle.Top, Height = 128, BackColor = Theme.Card, Padding = new Padding(12, 10, 12, 8) };
        Theme.Blockify(top);
        var autoChk = new CheckBox
        {
            Text = Lang.T("Relancer automatiquement si le serveur s'arrête anormalement (crash)", "Auto-restart if server stops abnormally (crash)"),
            ForeColor = Theme.Text, AutoSize = true,
            Checked = s.AutoRestart
        };
        var restartLbl = new Label
        {
            Text = Lang.T("Redémarrage quotidien à (HH:mm, vide = aucun) :", "Daily restart at (HH:mm, empty = none):"),
            ForeColor = Theme.TextDim, AutoSize = true, Location = new Point(4, 34)
        };
        var restartBox = new TextBox
        {
            Text = s.RestartAt, Width = 90,
            Location = new Point(280, 30), Font = new Font("Consolas", 10f)
        };
        Theme.ApplyInput(restartBox);
        var backupsBtn = new Button { Text = "Sauvegardes du monde…", Width = 240, Height = 38 };
        Theme.Apply(backupsBtn);
        backupsBtn.Location = new Point(4, 62);
        backupsBtn.Click += (_, _) => ShowBackupsDialog(s);

        var backupNowBtn = new Button { Text = "Sauvegarder maintenant", Width = 230, Height = 38 };
        Theme.Apply(backupNowBtn);
        backupNowBtn.Location = new Point(252, 62);
        backupNowBtn.Click += (_, _) =>
        {
            try
            {
                string zip = ServerHost.BackupWorld(s);
                Notifier.Show(s.Name, string.IsNullOrEmpty(zip)
                    ? Lang.T("Aucun monde à sauvegarder.", "No world to back up.")
                    : Lang.T("Sauvegarde du monde créée !", "World backup created!"));
                if (!string.IsNullOrEmpty(zip)) RefreshData();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
        };

        // ---- Discord Webhook ----
        var webhookLabel = new Label
        {
            Text = Lang.T("Webhook Discord (notifications joueurs) :", "Discord Webhook (player notifications):"),
            ForeColor = Theme.TextDim, AutoSize = true,
            Location = new Point(4, 100)
        };
        var webhookBox = new TextBox
        {
            Text = s.DiscordWebhookUrl, Width = 400,
            Location = new Point(250, 96),
            Font = new Font("Consolas", 9f),
            PlaceholderText = "https://discord.com/api/webhooks/..."
        };
        Theme.ApplyInput(webhookBox);
        var webhookTestBtn = new Button { Text = "Tester", Width = 80, Height = 28 };
        Theme.Apply(webhookTestBtn);
        webhookTestBtn.Location = new Point(660, 96);
        webhookTestBtn.Click += async (_, _) =>
        {
            string url = webhookBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("Entre une URL de webhook Discord.", "Team Launcher");
                return;
            }
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var payload = new { content = $"✅ **Team Launcher** — Test de webhook pour **{s.Name}** !" };
                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                await http.PostAsync(url, content);
                MessageBox.Show("Message de test envoyé ! Vérifie ton canal Discord.", "Team Launcher");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur :\n" + ex.Message, "Team Launcher");
            }
        };

        top.Controls.Add(autoChk);
        top.Controls.Add(restartLbl);
        top.Controls.Add(restartBox);
        top.Controls.Add(backupsBtn);
        top.Controls.Add(backupNowBtn);
        top.Controls.Add(webhookLabel);
        top.Controls.Add(webhookBox);
        top.Controls.Add(webhookTestBtn);

        var box = new TextBox
        {
            Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(12, 14, 10), ForeColor = Theme.Text,
            Font = new Font("Consolas", 9.5f)
        };
        string path = Path.Combine(ServerHost.Dir(s), "server.properties");
        try { box.Text = File.Exists(path) ? File.ReadAllText(path) : ""; } catch { }
        var save = new Button { Dock = DockStyle.Bottom, Height = 42, Text = "💾 Enregistrer" };
        Theme.Apply(save, primary: true);
        save.Click += (_, _) =>
        {
            try
            {
                File.WriteAllText(path, box.Text);
                s.AutoRestart = autoChk.Checked;
                s.RestartAt = restartBox.Text.Trim();
                s.DiscordWebhookUrl = webhookBox.Text.Trim();
                DataStore.Save();
                dlg.Close();
                Notifier.Show(s.Name, "Configuration enregistrée.");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
        };
        dlg.Controls.Add(box);
        dlg.Controls.Add(top);
        dlg.Controls.Add(save);
        dlg.Show(FindForm());
    }

    /// <summary>Gestion des mods serveur (dossier mods\ — nécessite un serveur Fabric).</summary>
    private void ShowModsDialog(HostedServer s)
    {
        if (s.Loader == "Vanilla")
        {
            MessageBox.Show(
                "Les mods ne fonctionnent que sur un serveur moddé.\n" +
                "Choisis Fabric, Forge ou NeoForge à la création du serveur pour y mettre des mods.",
                "Team Launcher");
            return;
        }
        string modsDir = Path.Combine(ServerHost.Dir(s), "mods");
        Directory.CreateDirectory(modsDir);

        var dlg = new Form
        {
            Text = $"Mods serveur — {s.Name}",
            Size = new Size(600, 460), StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Bg
        };
        var list = new CheckedListBox
        {
            Dock = DockStyle.Fill, CheckOnClick = true,
            BackColor = Theme.Card, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None
        };
        foreach (var f in Directory.GetFiles(modsDir, "*.jar"))
            list.Items.Add(Path.GetFileName(f));

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 96, FlowDirection = FlowDirection.LeftToRight, WrapContents = true
        };
        var add = new Button { Text = "➕ Ajouter des .jar", Width = 200, Height = 38 };
        Theme.Apply(add, primary: true);
        add.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Mod serveur (.jar)|*.jar",
                Multiselect = true
            };
            if (ofd.ShowDialog(dlg) != DialogResult.OK) return;
            foreach (var f in ofd.FileNames)
                File.Copy(f, Path.Combine(modsDir, Path.GetFileName(f)), overwrite: true);
            foreach (var f in ofd.FileNames)
                list.Items.Add(Path.GetFileName(f));
        };
        var del = new Button { Text = "🗑 Supprimer cochés", Width = 200, Height = 38 };
        Theme.Apply(del);
        del.Click += (_, _) =>
        {
            for (int i = list.Items.Count - 1; i >= 0; i--)
            {
                if (list.GetItemChecked(i))
                {
                    try { File.Delete(Path.Combine(modsDir, list.Items[i]!.ToString()!)); } catch { }
                    list.Items.RemoveAt(i);
                }
            }
        };
        var note = new Label
        {
            Text = "Les mods s'activent au prochain démarrage du serveur.\n" +
                   "Vérifie qu'ils sont compatibles avec la version " + s.McVersion + ".",
            ForeColor = Theme.TextDim, AutoSize = true,
            Margin = new Padding(6, 8, 0, 0)
        };
        buttons.Controls.Add(add);
        buttons.Controls.Add(del);
        buttons.Controls.Add(note);
        dlg.Controls.Add(list);
        dlg.Controls.Add(buttons);
        dlg.Show(FindForm());
    }

    /// <summary>Installe un modpack CurseForge/Modrinth sur le serveur.</summary>
    private async Task InstallModpackOnServerAsync(HostedServer s)
    {
        if (s.Loader == "Vanilla")
        {
            MessageBox.Show(
                Lang.T(
                    "Les mods ne fonctionnent que sur un serveur moddé.\n" +
                    "Choisis Fabric, Forge ou NeoForge à la création du serveur.",
                    "Mods only work on a modded server.\n" +
                    "Choose Fabric, Forge or NeoForge when creating the server."),
                "Team Launcher");
            return;
        }

        var dlg = new Form
        {
            Text = Lang.T($"Installer un modpack — {s.Name}", $"Install a modpack — {s.Name}"),
            Size = new Size(600, 400), StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Bg
        };

        var searchBox = new TextBox
        {
            Width = 350, Font = new Font("Segoe UI", 10f),
            Location = new Point(16, 16),
            PlaceholderText = Lang.T("Rechercher un modpack sur Modrinth...", "Search for a modpack on Modrinth...")
        };
        Theme.ApplyInput(searchBox);

        var searchBtn = new Button
        {
            Text = Lang.T("Rechercher", "Search"), Width = 100, Height = 32,
            Location = new Point(376, 16)
        };
        Theme.Apply(searchBtn, primary: true);

        var resultsList = new ListBox
        {
            Location = new Point(16, 56), Size = new Size(550, 240),
            BackColor = Theme.Card, ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9.5f)
        };

        var installBtn = new Button
        {
            Text = Lang.T("📦 Installer ce modpack", "📦 Install this modpack"),
            Width = 200, Height = 38,
            Location = new Point(16, 310),
            Enabled = false
        };
        Theme.Apply(installBtn, primary: true);

        var statusLabel = new Label
        {
            Location = new Point(230, 316), AutoSize = true,
            ForeColor = Theme.TextDim, Font = new Font("Segoe UI", 9f)
        };

        searchBtn.Click += async (_, _) =>
        {
            string query = searchBox.Text.Trim();
            if (query.Length < 2) return;

            statusLabel.Text = Lang.T("Recherche en cours...", "Searching...");
            resultsList.Items.Clear();
            installBtn.Enabled = false;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = await http.GetStringAsync(
                    $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&facets=%5B%5B%22project_type%3Amodpack%22%5D%5D&limit=10");
                var json = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(response);
                var hits = json.GetProperty("hits");

                foreach (var hit in hits.EnumerateArray())
                {
                    string title = hit.GetProperty("title").GetString() ?? "";
                    string slug = hit.GetProperty("slug").GetString() ?? "";
                    string desc = hit.GetProperty("description").GetString() ?? "";
                    int downloads = hit.GetProperty("downloads").GetInt32();
                    var item = new ModpackListItem
                    {
                        Slug = slug,
                        Display = $"{title} ({slug}) — {downloads:N0} dl — {desc[..Math.Min(60, desc.Length)]}..."
                    };
                    resultsList.Items.Add(item);
                }

                statusLabel.Text = resultsList.Items.Count > 0
                    ? Lang.T($"{resultsList.Items.Count} résultat(s)", $"{resultsList.Items.Count} result(s)")
                    : Lang.T("Aucun résultat", "No results");
            }
            catch (Exception ex)
            {
                statusLabel.Text = Lang.T("Erreur : " + ex.Message, "Error: " + ex.Message);
            }
        };

        searchBox.KeyPress += (_, e) =>
        {
            if (e.KeyChar == (char)13) { searchBtn.PerformClick(); e.Handled = true; }
        };

        resultsList.SelectedIndexChanged += (_, _) =>
        {
            installBtn.Enabled = resultsList.SelectedIndex >= 0;
        };

        installBtn.Click += async (_, _) =>
        {
            if (resultsList.SelectedItem is not ModpackListItem selected) return;
            string slug = selected.Slug;
            installBtn.Enabled = false;
            statusLabel.Text = Lang.T("Installation en cours...", "Installing...");

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

                // Get modpack versions
                var versionsResponse = await http.GetStringAsync(
                    $"https://api.modrinth.com/v2/project/{slug}/version?loaders=%5B%22{s.Loader.ToLower()}%22%5D&game_versions=%5B%22{s.McVersion}%22%5D");
                var versions = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(versionsResponse);

                if (versions.GetArrayLength() == 0)
                {
                    statusLabel.Text = Lang.T("Aucune version compatible", "No compatible version");
                    installBtn.Enabled = true;
                    return;
                }

                var firstVersion = versions[0];
                var files = firstVersion.GetProperty("files");
                if (files.GetArrayLength() == 0)
                {
                    statusLabel.Text = Lang.T("Aucun fichier", "No files");
                    installBtn.Enabled = true;
                    return;
                }

                string downloadUrl = files[0].GetProperty("url").GetString()!;
                string fileName = files[0].GetProperty("filename").GetString() ?? $"{slug}.mrpack";

                // Download the modpack
                string tempFile = Path.Combine(Path.GetTempPath(), fileName);
                var bytes = await http.GetByteArrayAsync(downloadUrl);
                await File.WriteAllBytesAsync(tempFile, bytes);

                // Extract mods from mrpack
                string modsDir = Path.Combine(ServerHost.Dir(s), "mods");
                Directory.CreateDirectory(modsDir);

                if (fileName.EndsWith(".mrpack"))
                {
                    // Modrinth modpack format
                    string tempDir = Path.Combine(Path.GetTempPath(), "tl-modpack-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        System.IO.Compression.ZipFile.ExtractToDirectory(tempFile, tempDir);

                        // Read modrinth.index.json
                        string indexFile = Path.Combine(tempDir, "modrinth.index.json");
                        if (File.Exists(indexFile))
                        {
                            var index = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                                await File.ReadAllTextAsync(indexFile));
                            var modFiles = index.GetProperty("files");

                            foreach (var modFile in modFiles.EnumerateArray())
                            {
                                string modUrl = modFile.GetProperty("url").GetString()!;
                                string modPath = modFile.GetProperty("path").GetString()!;
                                string destPath = Path.Combine(ServerHost.Dir(s), "mods", Path.GetFileName(modPath));

                                var modBytes = await http.GetByteArrayAsync(modUrl);
                                await File.WriteAllBytesAsync(destPath, modBytes);
                            }
                        }

                        // Copy overrides
                        string overrides = Path.Combine(tempDir, "overrides");
                        if (Directory.Exists(overrides))
                        {
                            CopyDirRecursive(overrides, ServerHost.Dir(s));
                        }
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, recursive: true); } catch { }
                    }
                }
                else
                {
                    // Regular zip - extract mods
                    string tempDir = Path.Combine(Path.GetTempPath(), "tl-modpack-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        System.IO.Compression.ZipFile.ExtractToDirectory(tempFile, tempDir);
                        string modsSource = Path.Combine(tempDir, "mods");
                        if (Directory.Exists(modsSource))
                        {
                            foreach (var f in Directory.GetFiles(modsSource, "*.jar"))
                                File.Copy(f, Path.Combine(modsDir, Path.GetFileName(f)), overwrite: true);
                        }
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, recursive: true); } catch { }
                    }
                }

                try { File.Delete(tempFile); } catch { }

                statusLabel.Text = Lang.T("Modpack installé !", "Modpack installed!");
                Notifier.Show(s.Name, Lang.T(
                    $"Modpack « {slug} » installé ! Redémarre le serveur.",
                    $"Modpack \"{slug}\" installed! Restart the server."));
                dlg.Close();
            }
            catch (Exception ex)
            {
                statusLabel.Text = Lang.T("Erreur : " + ex.Message, "Error: " + ex.Message);
                installBtn.Enabled = true;
            }
        };

        dlg.Controls.Add(searchBox);
        dlg.Controls.Add(searchBtn);
        dlg.Controls.Add(resultsList);
        dlg.Controls.Add(installBtn);
        dlg.Controls.Add(statusLabel);
        dlg.Show(FindForm());
    }

    private static void CopyDirRecursive(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.GetDirectories(src))
            CopyDirRecursive(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    /// <summary>Bibliothèque de mondes : sauvegarder un monde serveur, en charger un existant.</summary>
    private void ShowWorldLibrary(HostedServer s)
    {
        string libraryDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TeamLauncher", "world-library");
        Directory.CreateDirectory(libraryDir);

        var dlg = new Form
        {
            Text = Lang.T($"Bibliothèque de mondes — {s.Name}", $"World Library — {s.Name}"),
            Size = new Size(600, 440), StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Bg
        };

        var worldList = new ListBox
        {
            Dock = DockStyle.Fill, BackColor = Theme.Card, ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9.5f)
        };

        void RefreshWorldList()
        {
            worldList.Items.Clear();
            foreach (var dir in Directory.GetDirectories(libraryDir))
            {
                string name = Path.GetFileName(dir);
                long size = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
                var levelDat = Path.Combine(dir, "level.dat");
                string info = File.Exists(levelDat)
                    ? $" — {size / 1024.0 / 1024.0:0.#} Mo"
                    : $" — (pas de level.dat) — {size / 1024.0 / 1024.0:0.#} Mo";
                worldList.Items.Add(name + info);
            }
            if (worldList.Items.Count == 0)
                worldList.Items.Add("(Aucun monde sauvegardé)");
        }
        RefreshWorldList();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 80, FlowDirection = FlowDirection.LeftToRight, WrapContents = true
        };

        var saveCurrentBtn = new Button
        {
            Text = Lang.T("💾 Sauvegarder le monde actuel", "💾 Save current world"),
            Width = 250, Height = 38
        };
        Theme.Apply(saveCurrentBtn, primary: true);
        saveCurrentBtn.Click += (_, _) =>
        {
            string worldDir = Path.Combine(ServerHost.Dir(s), "world");
            if (!Directory.Exists(worldDir))
            {
                MessageBox.Show("Aucun monde sur ce serveur.", "Team Launcher");
                return;
            }

            string name = Microsoft.VisualBasic.Interaction.InputBox(
                "Nom pour cette sauvegardé dans la bibliothèque :",
                "Sauvegarder le monde",
                $"{s.Name}-{DateTime.Now:yyyyMMdd}");
            if (string.IsNullOrWhiteSpace(name)) return;

            string dest = Path.Combine(libraryDir, name);
            if (Directory.Exists(dest))
            {
                if (MessageBox.Show($"« {name} » existe déjà. Remplacer ?",
                    "Team Launcher", MessageBoxButtons.YesNo) != DialogResult.Yes)
                    return;
                Directory.Delete(dest, recursive: true);
            }

            try
            {
                CopyDirRecursive(worldDir, dest);
                Notifier.Show(s.Name, $"Monde « {name} » sauvegardé dans la bibliothèque !");
                RefreshWorldList();
            }
            catch (Exception ex) { MessageBox.Show("Erreur :\n" + ex.Message, "Team Launcher"); }
        };

        var loadSelectedBtn = new Button
        {
            Text = Lang.T("📥 Charger ce monde sur le serveur", "📥 Load this world on server"),
            Width = 260, Height = 38
        };
        Theme.Apply(loadSelectedBtn);
        loadSelectedBtn.Click += (_, _) =>
        {
            int idx = worldList.SelectedIndex;
            if (idx < 0 || idx >= worldList.Items.Count)
            {
                MessageBox.Show("Sélectionne un monde dans la liste.", "Team Launcher");
                return;
            }

            string selected = worldList.Items[idx].ToString()!;
            string name = selected.Split(" — ")[0];
            string srcDir = Path.Combine(libraryDir, name);
            if (!Directory.Exists(srcDir))
            {
                MessageBox.Show("Dossier introuvable.", "Team Launcher");
                return;
            }

            if (ServerHost.IsRunning(s))
            {
                MessageBox.Show("Arrête le serveur avant de charger un monde.", "Team Launcher");
                return;
            }

            if (MessageBox.Show(
                $"Remplacer le monde actuel du serveur par « {name} » ?\n" +
                "(L'ancien monde sera sauvegardé dans le dossier du serveur)",
                "Team Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try
            {
                string worldDir = Path.Combine(ServerHost.Dir(s), "world");
                if (Directory.Exists(worldDir))
                {
                    string backup = Path.Combine(ServerHost.Dir(s), "world.backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                    Directory.Move(worldDir, backup);
                }
                CopyDirRecursive(srcDir, worldDir);
                Notifier.Show(s.Name, $"Monde « {name} » chargé ! Redémarre le serveur.");
                dlg.Close();
            }
            catch (Exception ex) { MessageBox.Show("Erreur :\n" + ex.Message, "Team Launcher"); }
        };

        var deleteBtn = new Button
        {
            Text = Lang.T("🗑 Supprimer de la bibliothèque", "🗑 Delete from library"),
            Width = 230, Height = 38
        };
        Theme.Apply(deleteBtn);
        deleteBtn.Click += (_, _) =>
        {
            int idx = worldList.SelectedIndex;
            if (idx < 0 || idx >= worldList.Items.Count) return;
            string selected = worldList.Items[idx].ToString()!;
            string name = selected.Split(" — ")[0];
            if (MessageBox.Show($"Supprimer « {name} » de la bibliothèque ?",
                "Team Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            try
            {
                Directory.Delete(Path.Combine(libraryDir, name), recursive: true);
                RefreshWorldList();
            }
            catch (Exception ex) { MessageBox.Show("Erreur :\n" + ex.Message, "Team Launcher"); }
        };

        buttons.Controls.Add(saveCurrentBtn);
        buttons.Controls.Add(loadSelectedBtn);
        buttons.Controls.Add(deleteBtn);
        dlg.Controls.Add(worldList);
        dlg.Controls.Add(buttons);
        dlg.Show(FindForm());
    }

    /// <summary>Définit l'icône du serveur (server-icon.png 64×64).</summary>
    private void SetServerIcon(HostedServer s)
    {
        using var ofd = new OpenFileDialog { Filter = "Image (.png)|*.png" };
        if (ofd.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            using var original = new Bitmap(ofd.FileName);
            using var resized = new Bitmap(original, 64, 64);
            resized.Save(Path.Combine(ServerHost.Dir(s), "server-icon.png"));
            MessageBox.Show(
                "Icône appliquée ! Elle apparaîtra dans la liste de serveurs de tes amis\n" +
                "au prochain démarrage.", "Team Launcher");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Impossible d'appliquer l'icône :\n" + ex.Message, "Team Launcher");
        }
    }

    /// <summary>
    /// Rend le serveur joignable depuis Internet via un tunnel playit.gg :
    /// aucune manipulation de box ni de ports. L'agent donne une adresse
    /// publique (quelque-chose.playit.gg) à partager avec les amis.
    /// </summary>
    /// <summary>Petit champ de saisie modal (pseudo à whitelister, op…).</summary>
    private static string? PromptText(string title, string label)
    {
        string? result = null;
        using var dlg = new Form
        {
            Text = title, Size = new Size(430, 200),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, BackColor = Theme.Panel
        };
        var box = new TextBox
        {
            Width = 370, Font = new Font("Consolas", 11f),
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Card, ForeColor = Theme.Text,
            Padding = new Padding(4)
        };
        var lbl = new Label
        {
            Text = label, ForeColor = Theme.TextDim, AutoSize = true,
            Margin = new Padding(0, 12, 0, 6)
        };
        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(16, 8, 16, 8)
        };
        root.Controls.Add(lbl);
        root.Controls.Add(box);
        var ok = new Button { Text = "OK", Dock = DockStyle.Bottom, Height = 40 };
        Theme.Apply(ok, primary: true);
        ok.Click += (_, _) => { result = box.Text.Trim(); dlg.Close(); };
        dlg.Controls.Add(root);
        dlg.Controls.Add(ok);
        dlg.AcceptButton = ok;
        dlg.ShowDialog();
        return result;
    }

    private async Task OpenToInternetAsync(HostedServer s)
    {
        try
        {
            if (!ServerHost.IsRunning(s) &&
                MessageBox.Show(
                    "Le serveur ne tourne pas encore.\nOuvrir quand même le tunnel Internet ?",
                    "Team Launcher", MessageBoxButtons.YesNo) == DialogResult.No)
                return;

            if (!ServerHost.IsTunnelInstalled)
            {
                MessageBox.Show(
                    "Première fois : téléchargement de l'agent « playit » (~10 Mo).\n" +
                    "Cet outil gratuit crée une adresse publique pour ton serveur,\n" +
                    "sans ouvrir de ports sur ta box.",
                    "Team Launcher");
                await Task.Run(() => ServerHost.DownloadTunnelAsync());
            }

            ServerHost.StartTunnel(s.Id);
            ShowTunnelDialog(s);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Impossible d'ouvrir le tunnel Internet :\n" + ex.Message, "Team Launcher");
        }
    }

    private void ShowTunnelDialog(HostedServer s)
    {
        var dlg = new Form
        {
            Text = $"Tunnel Internet — {s.Name}",
            Size = new Size(780, 500),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Bg
        };
        var log = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 14, 10), ForeColor = Theme.Text,
            Font = new Font("Consolas", 9f)
        };
        string? tunnelClaimUrl = null;
        var openLink = new Button
        {
            Dock = DockStyle.Bottom, Height = 40, Text = "Ouvrir la page playit.gg",
            Visible = false
        };
        var footer = new Label
        {
            Dock = DockStyle.Bottom, Height = 110,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(10, 6, 0, 0),
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 9f),
            Text = "1. Si un lien s'affiche ci-dessous/au-dessus, ouvre-le et connecte-toi (gratuit).\n" +
                   "2. Dans playit : « Create tunnel » → Minecraft Java → adresse locale 127.0.0.1:" + s.Port + "\n" +
                   "3. Donne à tes amis l'adresse affichée ici (elle finit par .playit.gg) — elle marche partout en France comme ailleurs !" +
                   (string.IsNullOrEmpty(s.PublicAddress) ? "" :
                       $"\n✔ Adresse déjà enregistrée pour ce serveur : {s.PublicAddress}")
        };
        openLink.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new ProcessStartInfo(tunnelClaimUrl!) { UseShellExecute = true });
            }
            catch { }
        };
        dlg.Controls.Add(log);
        dlg.Controls.Add(openLink);
        dlg.Controls.Add(footer);

        void OnLine(string id, string line)
        {
            if (id != s.Id) return;
            bool hasClaim = line.Contains("playit.gg/mk/", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("claim", StringComparison.OrdinalIgnoreCase) &&
                            line.Contains("http", StringComparison.OrdinalIgnoreCase);
            try
            {
                dlg.BeginInvoke(() =>
                {
                    log.AppendText(line + Environment.NewLine);
                    int idx = line.IndexOf("https://", StringComparison.Ordinal);
                    if (idx >= 0 && hasClaim && tunnelClaimUrl == null)
                    {
                        string url = line[idx..].TrimEnd('.', ' ');
                        tunnelClaimUrl = url;
                        openLink.Text = "Ouvrir : " + url;
                        openLink.Visible = true;
                    }
                });
            }
            catch { }
        }
        ServerHost.TunnelEmitted += OnLine;
        dlg.FormClosed += (_, _) => ServerHost.TunnelEmitted -= OnLine;
        dlg.Show(FindForm());
    }

    private async Task ToggleStartStop(HostedServer s)
    {
        try
        {
            if (ServerHost.IsRunning(s))
            {
                ServerHost.Stop(s);
                Notifier.Show(s.Name, Lang.T("Arrêt du serveur…", "Stopping server…"));
                return;
            }
            if (!ServerHost.IsInstalled(s))
            {
                RefreshData();
                await Task.Run(() => ServerHost.DownloadAsync(s));
            }
            // backup auto du monde avant chaque session (anti-crash / anti-grief)
            try
            {
                string zip = ServerHost.BackupWorld(s);
                if (zip.Length > 0)
                    Notifier.Show(s.Name, Lang.T(
                        "Monde sauvegardé avant le démarrage ✓",
                        "World backed up before start ✓"));
            }
            catch { }

            ServerHost.Start(s);

            // tunnel playit déjà configuré : on le relance avec le serveur
            if (!string.IsNullOrEmpty(s.PublicAddress))
            {
                try { ServerHost.StartTunnel(s.Id); } catch { }
            }

            Notifier.Show(s.Name, Lang.T(
                $"Serveur démarré sur le port {s.Port} !",
                $"Server started on port {s.Port}!"));
            RefreshData();
        }
        catch (Exception ex)
        {
            RefreshData();
            MessageBox.Show(
                Lang.T("Impossible de démarrer le serveur :\n", "Could not start the server:\n") + ex.Message,
                "Team Launcher");
        }
    }

    private void DeleteHosted(HostedServer s)
    {
        if (MessageBox.Show(
                $"Supprimer définitivement « {s.Name} » ?\n\nLe monde, la config et le serveur seront effacés du PC.",
                "Team Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        ServerHost.Delete(s);
        DataStore.Settings.HostedServers.Remove(s);
        DataStore.Save();
        RefreshData();
    }

    private void ImportMap(HostedServer s)
    {
        var choice = MessageBox.Show(
            "Oui = importer un fichier .ZIP de map\nNon = importer un DOSSIER de map",
            "Importer une map", MessageBoxButtons.YesNoCancel);
        if (choice == DialogResult.Cancel) return;

        string path;
        if (choice == DialogResult.Yes)
        {
            using var dlg = new OpenFileDialog { Filter = "Map Minecraft (.zip)|*.zip" };
            if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
            path = dlg.FileName;
        }
        else
        {
            using var dlg = new FolderBrowserDialog { Description = "Choisis le dossier de la map (contenant level.dat)" };
            if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
            path = dlg.SelectedPath;
        }

        try
        {
            string worldName = ServerHost.ImportWorld(s, path);
            MessageBox.Show(
                $"Map « {worldName} » importée sur « {s.Name} » !\n" +
                "(l'ancien monde a été sauvegardé dans le dossier du serveur)\n\n" +
                "Démarre (ou redémarre) le serveur pour jouer dessus.",
                "Team Launcher");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Échec de l'import :\n" + ex.Message, "Team Launcher");
        }
    }

    /// <summary>Restauration des sauvegardes du monde.</summary>
    private void ShowBackupsDialog(HostedServer s)
    {
        var backups = ServerHost.ListBackups(s);
        var dlg = new Form
        {
            Text = $"Sauvegardes du monde — {s.Name}",
            Size = new Size(560, 440), StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Bg
        };
        var list = new ListBox
        {
            Dock = DockStyle.Fill, BackColor = Theme.Card, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9.5f)
        };
        foreach (var b in backups)
            list.Items.Add($"{b.LastWriteTime:dd/MM/yyyy HH:mm}   {b.Length / 1024.0 / 1024.0:0.#} Mo   ({b.Name})");

        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 52, WrapContents = false, Padding = new Padding(8, 8, 0, 0)
        };
        var restore = new Button { Text = "↩ Restaurer cette sauvegarde", Width = 260, Height = 36 };
        Theme.Apply(restore, primary: true);
        restore.Click += (_, _) =>
        {
            int idx = list.SelectedIndex;
            if (idx < 0)
            {
                MessageBox.Show("Sélectionne d'abord une sauvegarde.", "Team Launcher");
                return;
            }
            if (MessageBox.Show(
                    "Le monde actuel sera remplacé par cette sauvegarde.\n" +
                    "(l'actuel est mis de côté dans le dossier du serveur)\n\nConfirmer ?",
                    "Team Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            try
            {
                ServerHost.RestoreBackup(s, backups[idx].FullName);
                Notifier.Show(s.Name, "Sauvegarde restaurée !");
                dlg.Close();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
        };
        var hint = new Label
        {
            Text = Lang.T("Serveur arrêté requis.", "Server stop required."), ForeColor = Theme.TextDim,
            AutoSize = true, Margin = new Padding(12, 10, 0, 0)
        };
        row.Controls.Add(restore);
        row.Controls.Add(hint);
        dlg.Controls.Add(list);
        dlg.Controls.Add(row);
        dlg.Show(FindForm());
    }

    // ---------------- favoris (existant) ----------------

    private static Button MkBtn(string text, bool primary, int x, int w)
    {
        var b = new Button { Text = text, Size = new Size(w, 28), Location = new Point(x, 7) };
        Theme.Apply(b, primary);
        b.Font = new Font("Segoe UI", 8.75f);
        return b;
    }

    /// <summary>Ajoute un champ "Label + Control" dans le formulaire de création.</summary>
    private static void AddCreateField(Control parent, string labelText, out TextBox box)
    {
        var lbl = new Label
        {
            Text = labelText, ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 9.5f), AutoSize = true,
            Margin = new Padding(0, 12, 0, 4)
        };
        parent.Controls.Add(lbl);
        box = new TextBox();
        parent.Controls.Add(box);
    }

    /// <summary>Ajoute un champ "Label + Control (non-TextBox)" dans le formulaire de création.</summary>
    private static void AddCreateFieldRaw(Control parent, string labelText, Control ctrl)
    {
        var lbl = new Label
        {
            Text = labelText, ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 9.5f), AutoSize = true,
            Margin = new Padding(0, 12, 0, 4)
        };
        parent.Controls.Add(lbl);
        ctrl.Margin = new Padding(0, 0, 0, 0);
        parent.Controls.Add(ctrl);
    }

    public void RefreshData()
    {
        // ---- serveurs VPS (Pterodactyl) ----
        _ = LoadVpsServersAsync();

        // ---- favoris ----
        serverList.SuspendLayout();
        serverList.Controls.Clear();

        if (DataStore.Settings.Servers.Count == 0)
        {
            serverList.Controls.Add(new Label
            {
                Text = Lang.T(
                    "Aucun serveur favori. Ajoute une adresse ci-dessus !",
                    "No favorite server. Add an address above!"),
                ForeColor = Theme.TextDim, AutoSize = true,
                Margin = new Padding(6, 16, 0, 0)
            });
        }

        foreach (var addr in DataStore.Settings.Servers)
            serverList.Controls.Add(MakeServerRow(addr));

        serverList.ResumeLayout();
        _ = PingAllAsync();

        // ---- villes de la team ----
        cityList.SuspendLayout();
        cityList.Controls.Clear();

        if (DataStore.Settings.Cities.Count == 0)
        {
            cityList.Controls.Add(new Label
            {
                Text = Lang.T(
                    "Aucune ville pour l'instant. Clique sur « Ajouter ma ville » !",
                    "No cities yet. Click \"Add my city\"!"),
                ForeColor = Theme.TextDim, AutoSize = true,
                Margin = new Padding(6, 16, 0, 0)
            });
        }

        foreach (var city in DataStore.Settings.Cities)
            cityList.Controls.Add(MakeCityRow(city));

        cityList.ResumeLayout();
    }

    // ---------------- villes de la team ----------------

    private TeamCity? SelectedCity() =>
        DataStore.Settings.Cities.FirstOrDefault(c => c.Id == _selectedCityId);

    private void EditCity(TeamCity? existing)
    {
        using var dlg = new CityEditDialog(existing);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        if (existing == null) DataStore.Settings.Cities.Add(dlg.City);
        _selectedCityId = dlg.City.Id;
        DataStore.Save();
        RefreshData();
    }

    private void ShareCity()
    {
        var c = SelectedCity();
        if (c == null)
        {
            MessageBox.Show(
                Lang.T("Sélectionne d'abord une ville à partager.", "Select a city to share first."),
                "Team Launcher");
            return;
        }
        try
        {
            Clipboard.SetText(JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true }));
            Notifier.Show(Lang.T("Ville partagée", "City shared"),
                string.Format(Lang.T("« {0} » copiée ! Colle-la dans Discord.", "\"{0}\" copied! Paste it in Discord."), c.Name));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Team Launcher");
        }
    }

    private void ImportCity()
    {
        string text;
        try { text = Clipboard.GetText().Trim(); } catch { return; }
        if (text.Length == 0)
        {
            MessageBox.Show(
                Lang.T("Presse-papiers vide : copie d'abord une ville partagée.", "Empty clipboard: copy a shared city first."),
                "Team Launcher");
            return;
        }
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            List<TeamCity> imported;
            if (text.StartsWith('['))
                imported = JsonSerializer.Deserialize<List<TeamCity>>(text, options) ?? new();
            else
                imported = new() { JsonSerializer.Deserialize<TeamCity>(text, options) ?? throw new InvalidOperationException() };

            int added = 0;
            foreach (var c in imported)
            {
                if (c.Address.Length == 0) continue;
                if (DataStore.Settings.Cities.Any(x => x.Address.Equals(c.Address, StringComparison.OrdinalIgnoreCase))) continue;
                c.Id = Guid.NewGuid().ToString("N"); // évite les collisions d'identifiants
                DataStore.Settings.Cities.Add(c);
                added++;
            }
            DataStore.Save();
            RefreshData();
            Notifier.Show(Lang.T("Import", "Import"),
                added == 1 ? Lang.T("Ville importée !", "City imported!")
                           : string.Format(Lang.T("{0} villes importées !", "{0} cities imported!"), added));
        }
        catch
        {
            MessageBox.Show(
                Lang.T("Le presse-papiers ne contient pas une ville valide.", "Clipboard does not contain a valid city."),
                "Team Launcher");
        }
    }

    private Panel MakeCityRow(TeamCity city)
    {
        bool selected = _selectedCityId == city.Id;
        var row = new Panel
        {
            Height = 84,
            Width = Math.Max(600, Parent?.Width ?? 900),
            BackColor = selected ? ControlPaint.Dark(Theme.Accent, 0.55f) : Theme.Card,
            Margin = new Padding(0, 4, 14, 4),
            Tag = city.Id,
            Cursor = Cursors.Hand
        };
        Theme.Blockify(row);

        var icon = new Label
        {
            Text = "", Font = new Font("Segoe UI Emoji", 20f),
            Size = new Size(64, 70), Location = new Point(10, 6),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var nameLbl = new Label
        {
            Text = city.Name,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Location = new Point(84, 12),
            AutoSize = true
        };

        var sub = new Label
        {
            Text = string.IsNullOrWhiteSpace(city.Owner)
                ? city.Address
                : $"{city.Owner} • {city.Address}",
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 9f),
            Location = new Point(84, 40),
            AutoSize = true
        };

        var status = new Label
        {
            Text = "…",
            ForeColor = Theme.TextDim,
            Font = new Font("Consolas", 9f),
            Location = new Point(row.Width - 260, 30),
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        row.Controls.Add(icon);
        row.Controls.Add(nameLbl);
        row.Controls.Add(sub);
        row.Controls.Add(status);

        EventHandler select = (_, _) => { _selectedCityId = city.Id; RefreshData(); };
        row.Click += select; icon.Click += select; nameLbl.Click += select; sub.Click += select;
        row.DoubleClick += (_, _) => Join(city.Address);

        // ping en direct
        Task.Run(async () =>
        {
            ServerPing.Status? st = null;
            try { st = await ServerPing.QueryAsync(city.Address); } catch { }
            BeginInvoke(() =>
            {
                if (row.IsDisposed) return;
                if (st == null)
                {
                    status.Text = Lang.T("✘ Hors ligne", "✘ Offline");
                    status.ForeColor = Color.IndianRed;
                    return;
                }
                status.Text = $"✔ {st.Online}/{st.Max}  •  {st.Version}";
                status.ForeColor = Color.LimeGreen;
            });
        });

        return row;
    }

    private Panel MakeServerRow(string addr)
    {
        bool selected = _selectedAddress == addr;
        var row = new Panel
        {
            Height = 84,
            Width = Math.Max(600, Parent?.Width ?? 900),
            BackColor = selected ? ControlPaint.Dark(Theme.Accent, 0.55f) : Theme.Card,
            Margin = new Padding(0, 4, 14, 4),
            Tag = addr,
            Cursor = Cursors.Hand
        };
        Theme.Blockify(row);

        var icon = new Label
        {
            Text = "", Font = new Font("Segoe UI Emoji", 20f),
            Size = new Size(64, 70), Location = new Point(10, 6),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var motd = new Label
        {
            Text = addr,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Location = new Point(84, 12),
            AutoSize = true
        };

        var status = new Label
        {
            Text = "…",
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(84, 40),
            AutoSize = true
        };

        var players = new Label
        {
            Text = "", ForeColor = Theme.TextDim,
            Font = new Font("Consolas", 9f),
            Location = new Point(row.Width - 220, 30),
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        row.Controls.Add(icon);
        row.Controls.Add(motd);
        row.Controls.Add(status);
        row.Controls.Add(players);

        EventHandler select = (_, _) => { _selectedAddress = addr; RefreshData(); };
        row.Click += select; motd.Click += select; status.Click += select; icon.Click += select;
        row.DoubleClick += (_, _) => Join(addr);

        // requête de statut asynchrone
        Task.Run(async () =>
        {
            ServerPing.Status? st = null;
            try { st = await ServerPing.QueryAsync(addr); } catch { }
            BeginInvoke(() =>
            {
                if (row.IsDisposed) return;
                if (st == null)
                {
                    status.Text = Lang.T("✘ Hors ligne", "✘ Offline");
                    status.ForeColor = Color.IndianRed;
                    players.Text = "";
                    return;
                }
                status.Text = st.Motd.Length > 60 ? st.Motd[..60] : st.Motd;
                players.Text = $"{st.Online}/{st.Max}  •  {st.Version}";
                players.ForeColor = Theme.TextDim;
            });
        });

        return row;
    }

    private async Task PingAllAsync()
    {
        foreach (Control c in serverList.Controls)
        {
            if (c.Tag is not string addr) continue;
            var row = c;
            _ = Task.Run(async () =>
            {
                ServerPing.Status? st = null;
                try { st = await ServerPing.QueryAsync(addr); } catch { }
                BeginInvoke(() =>
                {
                    if (row.IsDisposed || st == null) return;
                    var labels = row.Controls.OfType<Label>().ToList();
                    labels.Last().Text = $"{st.Online}/{st.Max}  •  {st.Version}";
                });
            });
        }
    }

    private void AddServer()
    {
        try
        {
            var addr = addressBox.Text.Trim();
            if (addr.Length == 0)
            {
                MessageBox.Show(
                    Lang.T("Tape d'abord l'adresse du serveur dans le champ à gauche (ex : mc.hypixel.net).", "Enter the server address in the field on the left first (e.g. mc.hypixel.net)."),
                    "Team Launcher");
                addressBox.Focus();
                return;
            }
            if (!DataStore.Settings.Servers.Contains(addr)) DataStore.Settings.Servers.Add(addr);
            DataStore.Save();
            addressBox.Text = "";
            _selectedAddress = addr;
            RefreshData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Lang.T("Erreur en ajoutant le serveur :\n", "Error adding server:\n") + ex.Message, "Team Launcher");
        }
    }

    private void Join(string address)
    {
        using var pick = new InstancePickDialog(Lang.T("Rejoindre avec quelle instance ?", "Join with which instance?"), Lang.T("Rejoindre", "Join"));
        if (pick.ShowDialog(FindForm()) != DialogResult.OK || pick.Selected == null) return;
        GameLauncher.Play(pick.Selected, address);
    }
}

/// <summary>Élément de liste pour les modpacks Modrinth.</summary>
internal class ModpackListItem
{
    public string Slug { get; set; } = "";
    public string Display { get; set; } = "";
    public override string ToString() => Display;
}
