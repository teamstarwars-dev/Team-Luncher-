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
    private readonly FlowLayoutPanel hostedList = new();
    private readonly TextBox hostNameBox = new();
    private readonly ComboBox hostVersionCombo = new();
    private readonly Label createStatus = new();
    private string? _selectedAddress;
    private bool _versionsLoaded;
    private readonly Dictionary<string, string> _statusOverride = new();
    private readonly Dictionary<string, Label> _statusLabels = new();

    public ServersPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        // ================= PAGE EN ONGLETS =================

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(140, 30),
            Font = new Font("Segoe UI", 8.75f),
            Padding = new Point(10, 2)
        };
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

        // ================= ONGLET 1 : MES SERVEURS HÉBERGÉS =================

        var hostedPage = new TabPage("Mes serveurs") { BackColor = Theme.Bg };
        var hostedRoot = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true,
            Padding = new Padding(24, 16, 24, 16)
        };

        hostedRoot.Controls.Add(new Label
        {
            Text = Lang.T(
                "Crée et héberge tes propres serveurs Minecraft depuis le launcher.\n" +
                "Le launcher télécharge le serveur officiel, tu importes ta map, tu partages l'adresse à tes amis !",
                "Create and host your own Minecraft servers from the launcher.\n" +
                "The launcher downloads the official server, you import your map, share the address with your friends!"),
            ForeColor = Theme.TextDim, AutoSize = true
        });

        // ---- ligne de création ----
        var createRow = new Panel { Height = 52, Width = 920, Margin = new Padding(0, 12, 0, 0) };

        hostNameBox.SetBounds(0, 8, 240, 32);
        hostNameBox.Font = new Font("Segoe UI", 10f);
        hostNameBox.BorderStyle = BorderStyle.FixedSingle;
        hostNameBox.BackColor = Theme.Card;
        hostNameBox.ForeColor = Theme.Text;
        hostNameBox.PlaceholderText = Lang.T("Nom du serveur", "Server name");

        hostVersionCombo.SetBounds(250, 8, 150, 32);
        hostVersionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        hostVersionCombo.FlatStyle = FlatStyle.Flat;
        hostVersionCombo.Font = new Font("Consolas", 10f);
        hostVersionCombo.BackColor = Theme.Card;
        hostVersionCombo.ForeColor = Theme.Text;

        var loaderCombo = new ComboBox
        {
            Location = new Point(410, 8), Size = new Size(120, 32),
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f),
            BackColor = Theme.Card, ForeColor = Theme.Text
        };
        loaderCombo.Items.AddRange(new object[] { "Vanilla", "Fabric", "Forge", "NeoForge" });
        loaderCombo.SelectedIndex = 0;

        var rpChk = new CheckBox
        {
            Text = Lang.T("🏘 Ville RP", "🏘 RP city"),
            Location = new Point(540, 12), Size = new Size(130, 24),
            ForeColor = Theme.TextDim, AutoSize = true,
            Font = new Font("Segoe UI", 9f)
        };

        var templateCombo = new ComboBox
        {
            Location = new Point(540, 8), Size = new Size(130, 32),
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            BackColor = Theme.Card, ForeColor = Theme.Text
        };
        templateCombo.Items.AddRange(new object[] {
            "Aucun template",
            "🏠 Survival",
            "🎨 Creative",
            "🏝 SkyBlock",
            "🏘 Ville RP",
            "⚔️ PvP",
            "🎯 Minigames"
        });
        templateCombo.SelectedIndex = 0;
        templateCombo.SelectedIndexChanged += (_, _) =>
        {
            // Ajuster les options selon le template sélectionné
            string template = templateCombo.SelectedItem?.ToString() ?? "";
            if (template.Contains("Creative"))
            {
                rpChk.Checked = false;
            }
            else if (template.Contains("RP"))
            {
                rpChk.Checked = true;
            }
        };

        var createBtn = MkBtn(Lang.T("+ Créer le serveur", "+ Create server"), primary: true, x: 680, w: 210);
        createBtn.Click += async (_, _) => await CreateServerAsync(
            loaderCombo.SelectedItem as string ?? "Vanilla", rpChk.Checked,
            templateCombo.SelectedItem?.ToString() ?? "Aucun template");

        createStatus.SetBounds(900, 14, 160, 26);
        createStatus.ForeColor = Theme.TextDim;
        createStatus.Font = new Font("Segoe UI", 9f);
        createStatus.AutoSize = false;
        createStatus.AutoEllipsis = true;

        createRow.Controls.Add(hostNameBox);
        createRow.Controls.Add(hostVersionCombo);
        createRow.Controls.Add(loaderCombo);
        createRow.Controls.Add(templateCombo);
        createRow.Controls.Add(rpChk);
        createRow.Controls.Add(createBtn);
        createRow.Controls.Add(createStatus);

        hostedList.FlowDirection = FlowDirection.TopDown;
        hostedList.WrapContents = false;
        hostedList.AutoScroll = true;
        hostedList.Margin = new Padding(0, 4, 0, 0);
        hostedList.Width = 940;

        hostedRoot.Controls.Add(createRow);
        hostedRoot.Controls.Add(hostedList);

        // ================= ONGLET 2 : SERVEURS FAVORIS =================

        var favPage = new TabPage("Favoris") { BackColor = Theme.Bg };
        var favRoot = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true,
            Padding = new Padding(24, 16, 24, 16)
        };

        favRoot.Controls.Add(new Label
        {
            Text = Lang.T(
                "Tes serveurs favoris : statut en direct, joueurs connectés, MOTD. Double-clic sur un serveur = rejoindre.",
                "Your favorite servers: live status, connected players, MOTD. Double-click a server to join."),
            ForeColor = Theme.TextDim, AutoSize = true
        });

        serverList.FlowDirection = FlowDirection.TopDown;
        serverList.WrapContents = false;
        serverList.AutoScroll = true;
        serverList.Width = 940;
        serverList.BackColor = Theme.Bg;
        serverList.Margin = new Padding(0, 8, 0, 0);
        var btnRow = new Panel { Height = 52, Width = 920, Margin = new Padding(0, 10, 0, 0) };
        addressBox.SetBounds(0, 8, 360, 32);
        addressBox.Font = new Font("Consolas", 10f);
        addressBox.BorderStyle = BorderStyle.FixedSingle;
        addressBox.BackColor = Theme.Card;
        addressBox.ForeColor = Theme.Text;
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
        var refreshBtn = MkBtn("Actualiser", primary: false, x: 666, w: 160);
        refreshBtn.Click += async (_, _) => await PingAllAsync();

        btnRow.Controls.Add(addressBox);
        btnRow.Controls.Add(addBtn);
        btnRow.Controls.Add(delBtn);
        btnRow.Controls.Add(refreshBtn);

        favRoot.Controls.Add(btnRow);
        favRoot.Controls.Add(serverList);

        // ================= ONGLET 3 : VILLES DE LA TEAM =================

        var cityPage = new TabPage("Villes de la team") { BackColor = Theme.Bg };
        var cityRoot = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true,
            Padding = new Padding(24, 16, 24, 16)
        };

        cityRoot.Controls.Add(new Label
        {
            Text = Lang.T(
                "Les villes RP des membres. Chacun ajoute sa ville ici, puis la partage d'un clic (le destinataire l'importe depuis le presse-papiers). Double-clic sur une ville = rejoindre.",
                "Members' RP cities. Everyone adds their city here, then shares it in one click (the recipient imports it from the clipboard). Double-click a city to join."),
            ForeColor = Theme.TextDim, AutoSize = true
        });

        cityList.FlowDirection = FlowDirection.TopDown;
        cityList.WrapContents = false;
        cityList.AutoScroll = true;
        cityList.Width = 940;
        cityList.BackColor = Theme.Bg;
        cityList.Margin = new Padding(0, 8, 0, 0);

        var cityBtnRow = new Panel { Height = 52, Width = 920, Margin = new Padding(0, 10, 0, 0) };
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

        cityRoot.Controls.Add(cityBtnRow);
        cityRoot.Controls.Add(cityList);

        tabs.TabPages.AddRange(new[] { hostedPage, favPage, cityPage });
        Controls.Add(tabs);

        Resize += (_, _) =>
        {
            btnRow.Width = Math.Max(600, Width - 48);
            createRow.Width = btnRow.Width;
            cityBtnRow.Width = btnRow.Width;
        };

        ServerHost.StateChanged += OnHostStateChanged;
        ServerHost.DownloadProgress += OnDownloadProgress;
        // l'agent playit a annoncé une adresse publique : on la mémorise sur le serveur
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
        _ = LoadVersionsAsync();
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

    private async Task LoadVersionsAsync()
    {
        try
        {
            var versions = await MojangApi.GetReleasesAsync();
            hostVersionCombo.Items.Clear();
            foreach (var v in versions.Take(60)) hostVersionCombo.Items.Add(v);
            if (hostVersionCombo.Items.Count > 0) hostVersionCombo.SelectedIndex = 0;
            _versionsLoaded = true;
        }
        catch
        {
            createStatus.Text = Lang.T("Impossible de charger la liste des versions.", "Cannot load version list.");
        }
    }

    // ---------------- création & cartes hébergées ----------------

    private async Task CreateServerAsync(string loader, bool rpProfile, string template = "Aucun template")
    {
        string name = hostNameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(Lang.T("Donne un nom à ton serveur.", "Give your server a name."), "Team Launcher");
            return;
        }
        if (!_versionsLoaded || hostVersionCombo.SelectedItem is not string version)
        {
            MessageBox.Show(
                Lang.T(
                    "La liste des versions n'est pas encore chargée, réessaie dans un instant.",
                    "The version list hasn't loaded yet, try again in a moment."),
                "Team Launcher");
            return;
        }

        int port = 25565;
        while (DataStore.Settings.HostedServers.Any(h => h.Port == port)) port++;

        var hs = new HostedServer
        {
            Name = name, McVersion = version, Loader = loader, Port = port, Motd = name,
            RpProfile = rpProfile
        };

        // Appliquer les paramètres du template
        ApplyTemplate(hs, template);

        DataStore.Settings.HostedServers.Add(hs);
        DataStore.Save();
        hostNameBox.Text = "";
        _statusOverride[hs.Id] = Lang.T("⬇ Téléchargement du serveur…", "⬇ Downloading server…");
        RefreshData();

        try
        {
            createStatus.Text = Lang.T(
                $"Téléchargement du serveur Minecraft {version}…",
                $"Downloading Minecraft {version} server…");
            await Task.Run(() => ServerHost.DownloadAsync(hs));
            _statusOverride.Remove(hs.Id);
            createStatus.Text = "";
            RefreshData();
            MessageBox.Show(
                Lang.T(
                    $"Serveur « {name} » ({version}) créé !\n\n" +
                    "Clique sur ▶ Démarrer pour le lancer, puis partage l'adresse\n" +
                    "affichée dans la console avec tes amis.",
                    $"Server \"{name}\" ({version}) created!\n\n" +
                    "Click ▶ Start to launch it, then share the address\n" +
                    "shown in the console with your friends."),
                "Team Launcher");
        }
        catch (Exception ex)
        {
            _statusOverride.Remove(hs.Id);
            _statusOverride[hs.Id] = Lang.T("✘ Échec du téléchargement", "✘ Download failed");
            RefreshData();
            MessageBox.Show(
                Lang.T("Échec du téléchargement du serveur :\n", "Server download failed:\n") + ex.Message,
                "Team Launcher");
        }
    }

    private static void ApplyTemplate(HostedServer s, string template)
    {
        switch (template)
        {
            case "🏠 Survival":
                s.Motd = "§a" + s.Name + " §7— Survival";
                break;
            case "🎨 Creative":
                s.Motd = "§b" + s.Name + " §7— Creative";
                s.RpProfile = false;
                break;
            case "🏝 SkyBlock":
                s.Motd = "§e" + s.Name + " §7— SkyBlock";
                break;
            case "🏘 Ville RP":
                s.Motd = "§6" + s.Name + " §7— Ville RP";
                s.RpProfile = true;
                s.WhitelistEnabled = true;
                break;
            case "⚔️ PvP":
                s.Motd = "§c" + s.Name + " §7— PvP";
                break;
            case "🎯 Minigames":
                s.Motd = "§d" + s.Name + " §7— Minigames";
                break;
        }
    }

    private Panel MakeHostedCard(HostedServer s)
    {
        bool running = ServerHost.IsRunning(s);
        var card = new Panel
        {
            Height = 296,
            Width = Math.Max(880, Parent?.Width ?? 900),
            BackColor = running ? ControlPaint.Dark(Theme.Accent, 0.55f) : Theme.Card,
            Margin = new Padding(0, 6, 14, 6)
        };
        Theme.Blockify(card);

        // ================= en-tête de carte =================

        var icon = new Label
        {
            Text = "", Font = new Font("Segoe UI Emoji", 16f),
            Size = new Size(48, 48), Location = new Point(10, 10),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var nameLbl = new Label
        {
            Text = s.Name,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            Location = new Point(64, 8), AutoSize = true
        };

        string addr = $"{ServerHost.GetLocalIp()}:{s.Port}";
        var info = new Label
        {
            Text = running
                ? $"● EN LIGNE  •  {s.Loader} {s.McVersion}  •  adresse à partager : {addr}"
                : $"○ Arrêté  •  {s.Loader} {s.McVersion}  •  port {s.Port}",
            ForeColor = running ? Theme.Accent : Theme.TextDim,
            Font = new Font("Segoe UI", 9f),
            Location = new Point(64, 32), AutoSize = true
        };

        var status = new Label
        {
            Text = _statusOverride.TryGetValue(s.Id, out var ov) ? ov : "",
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(64, 52), AutoSize = true
        };
        _statusLabels[s.Id] = status;
        card.Disposed += (_, _) => { if (_statusLabels.TryGetValue(s.Id, out var l) && ReferenceEquals(l, status)) _statusLabels.Remove(s.Id); };

        // ---- Monitoring CPU/RAM en temps réel ----
        var monitorLabel = new Label
        {
            Text = "",
            ForeColor = Theme.Accent,
            Font = new Font("Consolas", 8.5f),
            Location = new Point(64, 72), AutoSize = true
        };
        if (running)
        {
            var monitorTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            void UpdateMonitor(object? _, EventArgs __)
            {
                if (!ServerHost.IsRunning(s) || card.IsDisposed)
                {
                    monitorTimer.Stop();
                    monitorTimer.Dispose();
                    monitorLabel.Text = "";
                    return;
                }
                try
                {
                    var process = System.Diagnostics.Process.GetProcessById(
                        System.Diagnostics.Process.GetProcessesByName("java")
                            .FirstOrDefault(p =>
                            {
                                try { return p.MainModule?.FileName?.Contains("javaw") == true; }
                                catch { return false; }
                            })?.Id ?? 0);
                    if (process != null)
                    {
                        long ramMb = process.WorkingSet64 / 1024 / 1024;
                        double cpu = process.TotalProcessorTime.TotalMilliseconds / (DateTime.Now - process.StartTime).TotalMilliseconds * 100;
                        monitorLabel.Text = $"📊 RAM: {ramMb} Mo  •  CPU: {cpu:F1}%";
                    }
                }
                catch { monitorLabel.Text = ""; }
            }
            monitorTimer.Tick += UpdateMonitor;
            monitorTimer.Start();
            card.Disposed += (_, _) => { monitorTimer.Stop(); monitorTimer.Dispose(); };
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                if (!card.IsDisposed) card.BeginInvoke(() => UpdateMonitor(null!, EventArgs.Empty));
            });
        }

        var copyQuickBtn = MkBtn("⧉", primary: false, x: 0, w: 44);
        copyQuickBtn.Location = new Point(card.Width - 142, 10);
        copyQuickBtn.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(addr);
                copyQuickBtn.Text = "✓";
                var restore = new System.Windows.Forms.Timer { Interval = 1800 };
                restore.Tick += (_, _) =>
                {
                    copyQuickBtn.Text = "📋";
                    restore.Stop();
                    restore.Dispose();
                };
                restore.Start();
            }
            catch { }
        };

        var folderBtn = MkBtn("↗", primary: false, x: 0, w: 44);
        folderBtn.Location = new Point(card.Width - 94, 10);
        folderBtn.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new ProcessStartInfo(ServerHost.Dir(s))
                { UseShellExecute = true });
            }
            catch { }
        };

        var delBtn = MkBtn("✕", primary: false, x: 0, w: 44);
        delBtn.Location = new Point(card.Width - 46, 10);
        delBtn.Click += (_, _) => DeleteHosted(s);

        // ---- statut joueurs en direct (serveur en ligne uniquement) ----
        if (running)
        {
            string addrCopy = addr;
            var infoRef = info;
            _ = Task.Run(async () =>
            {
                ServerPing.Status? st = null;
                try { st = await ServerPing.QueryAsync("127.0.0.1:" + s.Port); } catch { }
                if (st == null) return;
                card.BeginInvoke(() =>
                {
                    if (card.IsDisposed || infoRef.IsDisposed || !ServerHost.IsRunning(s)) return;
                    infoRef.Text = Lang.T(
                        $"● EN LIGNE  •  {s.Loader} {s.McVersion}  •  👥 {st.Online}/{st.Max} joueurs  •  adresse : {addrCopy}",
                        $"● ONLINE  •  {s.Loader} {s.McVersion}  •  👥 {st.Online}/{st.Max} players  •  address: {addrCopy}");
                });
            });
        }

        // ================= onglets =================

        var tabs = new TabControl
        {
            Location = new Point(8, 68),
            Size = new Size(card.Width - 16, 220),
            Font = new Font("Segoe UI", 8.25f),
            DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(92, 24)
        };
        tabs.DrawItem += (_, e) =>
        {
            bool sel = tabs.SelectedIndex == e.Index;
            using (var b = new SolidBrush(sel ? Theme.Card : Theme.Panel))
                e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text,
                new Font("Segoe UI", 8.25f), e.Bounds,
                sel ? Theme.Text : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        // ---------------- onglet Démarrer ----------------

        var startPage = new TabPage("Démarrer") { BackColor = Theme.Bg };

        var startBtn = MkBtn(running
            ? Lang.T("Arrêter le serveur", "Stop server")
            : Lang.T("Démarrer le serveur", "Start server"),
            primary: true, x: 16, w: 210);
        startBtn.Location = new Point(16, 18);
        startBtn.Height = 42;
        startBtn.Click += async (_, _) => await ToggleStartStop(s);

        var copyBtn = MkBtn(Lang.T($"Copier l'adresse ({addr})", $"Copy address ({addr})"),
            primary: false, x: 236, w: 280);
        copyBtn.Location = new Point(236, 18);
        copyBtn.Height = 42;
        copyBtn.ForeColor = Theme.Accent;
        string shareAddr = string.IsNullOrEmpty(s.PublicAddress) ? addr : s.PublicAddress;
        if (!string.IsNullOrEmpty(s.PublicAddress))
            copyBtn.Text = Lang.T($"🌍 Copier l'adresse publique ({s.PublicAddress})",
                $"🌍 Copy public address ({s.PublicAddress})");
        copyBtn.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(shareAddr);
                copyBtn.Text = Lang.T("✓ Adresse copiée !", "✓ Address copied!");
                var restore = new System.Windows.Forms.Timer { Interval = 1800 };
                restore.Tick += (_, _) =>
                {
                    copyBtn.Text = string.IsNullOrEmpty(s.PublicAddress)
                        ? Lang.T($"Copier l'adresse ({addr})", $"Copy address ({addr})")
                        : Lang.T($"🌍 Copier l'adresse publique ({s.PublicAddress})",
                            $"🌍 Copy public address ({s.PublicAddress})");
                    restore.Stop();
                    restore.Dispose();
                };
                restore.Start();
            }
            catch { }
        };

        var startHint = new Label
        {
            Text = running
                ? Lang.T(
                    "Le serveur tourne ! Donne l'adresse ci-dessus à tes amis (même réseau).",
                    "The server is running! Share the address above with your friends (same network).")
                : Lang.T(
                    "Démarre le serveur puis partage l'adresse à tes amis.\n" +
                    "Pour jouer hors de ton réseau : onglet Réglages → Ouvrir sur Internet.",
                    "Start the server then share the address with your friends.\n" +
                    "To play outside your network: Settings tab → Open to Internet."),
            ForeColor = Theme.TextDim, Font = new Font("Segoe UI", 9f),
            Location = new Point(16, 72), AutoSize = true
        };

        startPage.Controls.AddRange(new Control[] { startBtn, copyBtn, startHint });

        // ---------------- onglet Console ----------------

        var consolePage = new TabPage("Console") { BackColor = Theme.Bg, Padding = new Padding(8) };

        var log = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 14, 10), ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f)
        };
        try
        {
            string logFile = Path.Combine(ServerHost.Dir(s), "console.log");
            if (File.Exists(logFile))
            {
                log.Text = File.ReadAllText(logFile);
                log.SelectionStart = log.TextLength;
                log.ScrollToCaret();
            }
        }
        catch { }

        var cmdRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 44, WrapContents = false, Padding = new Padding(0, 6, 0, 0)
        };
        var cmdBox = new TextBox { Width = 300, Font = new Font("Consolas", 10f) };
        cmdBox.PlaceholderText = Lang.T(
            "Commande : list, say Bonjour, whitelist add Pseudo…",
            "Command: list, say Hello, whitelist add Name…");
        void SendCmd(string c)
        {
            c = c.Trim();
            if (c.Length == 0) return;
            ServerHost.SendCommand(s.Id, c);
            cmdBox.Text = "";
        }
        var sendBtn = new Button {             Text = Lang.T("Envoyer", "Send"), Width = 84, Height = 32 };
        Theme.Apply(sendBtn, primary: true);
        sendBtn.Click += (_, _) => SendCmd(cmdBox.Text);
        cmdBox.KeyPress += (_, e) =>
        {
            if (e.KeyChar == (char)13) { SendCmd(cmdBox.Text); e.Handled = true; }
        };
        Button Quick(string c) => new()
        {
            Text = c, Width = 90, Height = 32, Margin = new Padding(4, 1, 0, 0)
        };
        var qList = Quick("list");
        Theme.Apply(qList);
        qList.Click += (_, _) => SendCmd("list");
        var qSave = Quick("save-all");
        Theme.Apply(qSave);
        qSave.Click += (_, _) => SendCmd("save-all");

        // ---- actions whitelist / OP façon ville RP ----
        void SendToPlayer(string action, string label) =>
            ServerHost.SendCommand(s.Id, $"{action} {label}");
        var qWlAdd = Quick("+ Whitelist");
        Theme.Apply(qWlAdd);
        qWlAdd.Click += (_, _) =>
        {
            string? p = PromptText(Lang.T("Whitelist", "Whitelist"),
                Lang.T("Pseudo à autoriser sur le serveur :", "Player name to allow on the server:"));
            if (!string.IsNullOrWhiteSpace(p)) SendToPlayer("whitelist add", p);
        };
        var qWlDel = Quick("- Whitelist");
        Theme.Apply(qWlDel);
        qWlDel.Click += (_, _) =>
        {
            string? p = PromptText(Lang.T("Whitelist", "Whitelist"),
                Lang.T("Pseudo à retirer du serveur :", "Player name to remove from the server:"));
            if (!string.IsNullOrWhiteSpace(p)) SendToPlayer("whitelist remove", p);
        };
        var qOp = Quick("OP");
        Theme.Apply(qOp);
        qOp.Click += (_, _) =>
        {
            string? p = PromptText("OP",
                Lang.T("Pseudo à promouvoir opérateur :", "Player name to promote as operator:"));
            if (!string.IsNullOrWhiteSpace(p)) SendToPlayer("op", p);
        };

        cmdRow.Controls.Add(cmdBox);
        cmdRow.Controls.Add(sendBtn);
        cmdRow.Controls.Add(qList);
        cmdRow.Controls.Add(qSave);
        cmdRow.Controls.Add(qWlAdd);
        cmdRow.Controls.Add(qWlDel);
        cmdRow.Controls.Add(qOp);

        consolePage.Controls.Add(log);
        consolePage.Controls.Add(cmdRow);

        void OnLine(string id, string line)
        {
            if (id != s.Id) return;
            try
            {
                card.BeginInvoke(() =>
                {
                    if (card.IsDisposed || log.IsDisposed) return;
                    log.AppendText(line + Environment.NewLine);
                    if (log.Lines.Length > 800)
                        log.Lines = log.Lines[^500..];
                });
            }
            catch { }
        }
        ServerHost.LogEmitted += OnLine;
        card.Disposed += (_, _) => ServerHost.LogEmitted -= OnLine;

        // ---------------- onglet Map & Mods ----------------

        var contentPage = new TabPage("Map & mods") { BackColor = Theme.Bg };

        var mapBtn = MkBtn("Importer une map", primary: false, x: 16, w: 200);
        mapBtn.Location = new Point(16, 18);
        mapBtn.Height = 42;
        mapBtn.Click += (_, _) => ImportMap(s);

        var modsBtn = MkBtn("Gérer les mods", primary: false, x: 226, w: 170);
        modsBtn.Location = new Point(226, 18);
        modsBtn.Height = 42;
        modsBtn.Click += (_, _) => ShowModsDialog(s);

        var modpackBtn = MkBtn("📦 Installer un modpack", primary: false, x: 406, w: 200);
        modpackBtn.Location = new Point(406, 18);
        modpackBtn.Height = 42;
        modpackBtn.ForeColor = Theme.Accent;
        modpackBtn.Click += async (_, _) => await InstallModpackOnServerAsync(s);

        var worldLibBtn = MkBtn("📚 Bibliothèque de mondes", primary: false, x: 616, w: 200);
        worldLibBtn.Location = new Point(616, 18);
        worldLibBtn.Height = 42;
        worldLibBtn.Click += (_, _) => ShowWorldLibrary(s);

        var contentHint = new Label
        {
            Text = Lang.T(
                "L'import remplace le monde actuel (l'ancien est sauvegardé dans le dossier du serveur).\n" +
                "Les mods (.jar) s'activent au prochain démarrage — nécessite Fabric, Forge ou NeoForge.\n" +
                "Un modpack CurseForge/Modrinch peut être installé directement sur le serveur.\n" +
                "La bibliothèque de mondes permet de sauvegarder/charger des mondes entre serveurs.",
                "Importing replaces the current world (the old one is backed up in the server folder).\n" +
                "Mods (.jar) are loaded on next start — requires Fabric, Forge or NeoForge.\n" +
                "A CurseForge/Modrinth modpack can be installed directly on the server.\n" +
                "The world library lets you save/load worlds between servers."),
            ForeColor = Theme.TextDim, Font = new Font("Segoe UI", 9f),
            Location = new Point(16, 72), AutoSize = true
        };

        contentPage.Controls.AddRange(new Control[] { mapBtn, modsBtn, modpackBtn, worldLibBtn, contentHint });

        // ---------------- onglet Réglages ----------------

        var settingsPage = new TabPage("Réglages") { BackColor = Theme.Bg };

        var configBtn = MkBtn("server.properties…", primary: false, x: 16, w: 210);
        configBtn.Location = new Point(16, 18);
        configBtn.Height = 42;
        configBtn.Click += (_, _) => ShowConfigDialog(s);

        var iconBtn = MkBtn("Icône…", primary: false, x: 236, w: 150);
        iconBtn.Location = new Point(236, 18);
        iconBtn.Height = 42;
        iconBtn.Click += (_, _) => SetServerIcon(s);

        var tunnelBtn = MkBtn("Ouvrir sur Internet", primary: false, x: 396, w: 200);
        tunnelBtn.Location = new Point(436, 18);
        tunnelBtn.Height = 42;
        tunnelBtn.Click += (_, _) => _ = OpenToInternetAsync(s);

        var playersBtn = MkBtn(Lang.T("Joueurs…", "Players…"), primary: false, x: 656, w: 180);
        playersBtn.Location = new Point(656, 18);
        playersBtn.Height = 42;
        playersBtn.ForeColor = Theme.Accent;
        playersBtn.Click += (_, _) =>
        {
            using var dlg = new ServerPlayersDialog(s);
            dlg.ShowDialog(FindForm());
            RefreshData();
        };

        var settingsHint = new Label
        {
            Text = Lang.T(
                "Configuration du serveur (port, MOTD, difficulté, whitelist…), redémarrage auto,\n" +
                "sauvegardes, icône et tunnel Internet playit.gg (adresse publique sans ouvrir les ports).",
                "Server configuration (port, MOTD, difficulty, whitelist…), auto restart,\n" +
                "backups, icon and the playit.gg Internet tunnel (public address, no port forwarding)."),
            ForeColor = Theme.TextDim, Font = new Font("Segoe UI", 9f),
            Location = new Point(16, 72), AutoSize = true
        };

        settingsPage.Controls.AddRange(new Control[] { configBtn, iconBtn, tunnelBtn, playersBtn, settingsHint });

        // ---------------- assemblage ----------------

        tabs.TabPages.AddRange(new[] { startPage, consolePage, contentPage, settingsPage });

        card.Controls.AddRange(new Control[]
        {
            icon, nameLbl, info, status, monitorLabel, copyQuickBtn, folderBtn, delBtn, tabs
        });
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
            BorderStyle = BorderStyle.FixedSingle
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
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Card, ForeColor = Theme.Text
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
                _statusOverride[s.Id] = Lang.T("⬇ Téléchargement du serveur…", "⬇ Downloading server…");
                RefreshData();
                await Task.Run(() => ServerHost.DownloadAsync(s));
                _statusOverride.Remove(s.Id);
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
            _statusOverride.Remove(s.Id);
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
            BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9.5f)
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

    public void RefreshData()
    {
        // ---- serveurs hébergés ----
        hostedList.SuspendLayout();
        hostedList.Controls.Clear();

        if (DataStore.Settings.HostedServers.Count == 0)
        {
            hostedList.Controls.Add(new Label
            {
                Text = Lang.T(
                    "Aucun serveur hébergé. Donne-lui un nom, choisis une version, clique sur Créer !",
                    "No hosted server yet. Give it a name, pick a version, click Create!"),
                ForeColor = Theme.TextDim, AutoSize = true,
                Margin = new Padding(6, 10, 0, 0)
            });
        }

        foreach (var hs in DataStore.Settings.HostedServers)
            hostedList.Controls.Add(MakeHostedCard(hs));

        hostedList.ResumeLayout();

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
