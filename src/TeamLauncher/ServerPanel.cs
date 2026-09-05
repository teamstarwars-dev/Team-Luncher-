using System.Diagnostics;

namespace TeamLauncher;

/// <summary>
/// Panneau de gestion plein écran d'un serveur hébergé, style Pterodactyl.
/// S'ouvre quand on clique sur un serveur dans l'onglet "Mes serveurs".
/// Console temps réel, joueurs, map/mods, réglages — tout intégré.
/// </summary>
public class ServerPanel : UserControl
{
    private readonly HostedServer _server;
    private readonly Label _statusLbl;
    private readonly Label _monitorLbl;
    private readonly TextBox _logBox;
    private readonly TextBox _cmdBox;
    private readonly System.Windows.Forms.Timer _monitorTimer;
    private readonly Panel _content;
    private readonly Button _startBtn;
    private Button? _activeNav;
    private int _cachedJavaPid;

    public ServerPanel(HostedServer server)
    {
        _server = server;
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        // ============ HEADER ============
        var header = new Panel
        {
            Dock = DockStyle.Top, Height = 56,
            BackColor = ControlPaint.Dark(Theme.Card, 0.02f)
        };

        var backBtn = new Button
        {
            Text = "←", Location = new Point(12, 10), Size = new Size(36, 36),
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 14f),
            ForeColor = Theme.TextDim, BackColor = Color.Transparent, Cursor = Cursors.Hand
        };
        backBtn.FlatAppearance.BorderSize = 0;
        backBtn.Click += (_, _) =>
        {
            var form = FindForm();
            if (form is MainForm mf)
            {
                AppEvents.NavigateTo("servers");
            }
        };

        _statusLbl = new Label
        {
            Text = server.Name,
            ForeColor = Theme.Text, Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            Location = new Point(56, 12), AutoSize = true
        };

        _monitorLbl = new Label
        {
            Text = $"{server.Loader} {server.McVersion}  •  port {server.Port}",
            ForeColor = Theme.TextDim, Font = new Font("Segoe UI", 9f),
            Location = new Point(56, 34), AutoSize = true
        };

        _startBtn = new Button
        {
            Text = ServerHost.IsRunning(server)
                ? Lang.T("⏹ Arrêter", "⏹ Stop")
                : Lang.T("▶ Démarrer", "▶ Start"),
            Location = new Point(header.Width - 200, 10), Width = 180, Height = 36,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = ServerHost.IsRunning(server)
                ? Color.FromArgb(200, 60, 60) : Theme.Accent,
            Cursor = Cursors.Hand, Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _startBtn.FlatAppearance.BorderSize = 0;
        _startBtn.Click += async (_, _) => await ToggleStartStop();

        header.Controls.AddRange(new Control[] { backBtn, _statusLbl, _monitorLbl, _startBtn });

        // ============ SIDEBAR ============
        var sidebar = new Panel
        {
            Dock = DockStyle.Left, Width = 180,
            BackColor = ControlPaint.Dark(Theme.Panel, 0.03f),
            Padding = new Padding(0, 8, 0, 0)
        };

        var navItems = new (string icon, string text, Action show)[]
        {
            ("🖥", Lang.T("Console", "Console"), ShowConsole),
            ("👥", Lang.T("Joueurs", "Players"), ShowPlayers),
            ("📦", Lang.T("Map & Mods", "Map & Mods"), ShowContent),
            ("⚙", Lang.T("Réglages", "Settings"), ShowSettings),
            ("📁", Lang.T("Dossier", "Folder"), () =>
            {
                try { Process.Start(new ProcessStartInfo(ServerHost.Dir(_server)) { UseShellExecute = true }); }
                catch { }
            }),
        };

        int ny = 8;
        foreach (var (icon, text, show) in navItems)
        {
            var btn = new Button
            {
                Text = $"  {icon}  {text}",
                Location = new Point(0, ny), Size = new Size(180, 40),
                FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Theme.TextDim, BackColor = Color.Transparent,
                Padding = new Padding(12, 0, 0, 0), Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Tag = show;
            btn.Click += (_, _) =>
            {
                if (_activeNav != null) { _activeNav.BackColor = Color.Transparent; _activeNav.ForeColor = Theme.TextDim; }
                _activeNav = btn;
                btn.BackColor = Theme.Accent;
                btn.ForeColor = Color.White;
                show();
            };
            sidebar.Controls.Add(btn);
            ny += 42;
        }

        // ============ CONTENT ============
        _content = new Panel
        {
            Dock = DockStyle.Fill, BackColor = Theme.Bg,
            Padding = new Padding(16)
        };

        // ============ ASSEMBLAGE ============
        Controls.Add(_content);
        Controls.Add(sidebar);
        Controls.Add(header);

        // ============ CONSOLE (par défaut) ============
        _logBox = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 14, 10),
            ForeColor = Color.FromArgb(200, 220, 200),
            BorderStyle = BorderStyle.None,
            Padding = new Padding(4),
            Font = new Font("Consolas", 9.5f),
            WordWrap = false
        };

        _cmdBox = new TextBox
        {
            Dock = DockStyle.Bottom, Height = 36,
            Font = new Font("Consolas", 10f),
            BackColor = Theme.Card, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            Padding = new Padding(4)
        };
        _cmdBox.PlaceholderText = Lang.T(
            "Commande : list, say Bonjour, whitelist add, op, ban…",
            "Command: list, say Hello, whitelist add, op, ban…");
        _cmdBox.KeyPress += (_, e) =>
        {
            if (e.KeyChar == (char)13) { SendCommand(); e.Handled = true; }
        };

        var cmdRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 44, WrapContents = false,
            Padding = new Padding(0, 4, 0, 0)
        };
        var sendBtn = new Button
        {
            Text = Lang.T("Envoyer", "Send"), Width = 100, Height = 34,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9f),
            ForeColor = Color.White, BackColor = Theme.Accent, Cursor = Cursors.Hand
        };
        sendBtn.FlatAppearance.BorderSize = 0;
        sendBtn.Click += (_, _) => SendCommand();

        void AddQuickCmd(string text, int x)
        {
            var b = new Button
            {
                Text = text, Width = 80, Height = 34, Location = new Point(x, 4),
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8f),
                ForeColor = Theme.TextDim, BackColor = Theme.Card, Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (_, _) => { _cmdBox.Text = text; SendCommand(); };
            cmdRow.Controls.Add(b);
        }

        AddQuickCmd("list", 0);
        AddQuickCmd("save-all", 0);
        AddQuickCmd("stop", 0);

        // Charger le log existant
        try
        {
            string logFile = Path.Combine(ServerHost.Dir(_server), "console.log");
            if (File.Exists(logFile))
            {
                _logBox.Text = File.ReadAllText(logFile);
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.ScrollToCaret();
            }
        }
        catch { }

        // Écouter les lignes de console en temps réel
        ServerHost.LogEmitted += OnLogLine;

        // ============ MONITOR CPU/RAM ============
        _monitorTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _monitorTimer.Tick += UpdateMonitor;
        if (ServerHost.IsRunning(_server)) _monitorTimer.Start();

        // Afficher console par défaut
        ShowConsole();

        // Activer le premier bouton sidebar
        if (sidebar.Controls.Count > 0 && sidebar.Controls[0] is Button first)
        {
            _activeNav = first;
            first.BackColor = Theme.Accent;
            first.ForeColor = Color.White;
        }
    }

    private void OnLogLine(string id, string line)
    {
        if (id != _server.Id) return;
        try
        {
            if (IsDisposed || _logBox.IsDisposed) return;
            BeginInvoke(() =>
            {
                if (_logBox.IsDisposed) return;
                _logBox.AppendText(line + Environment.NewLine);
                if (_logBox.Lines.Length > 1000)
                    _logBox.Lines = _logBox.Lines[^800..];
            });
        }
        catch { }
    }

    private void UpdateMonitor(object? _, EventArgs __)
    {
        if (!ServerHost.IsRunning(_server) || IsDisposed)
        {
            _monitorTimer.Stop();
            _cachedJavaPid = 0;
            _monitorLbl.Text = $"{_server.Loader} {_server.McVersion}  •  port {_server.Port}  •  ○ Arrêté";
            _startBtn.Text = Lang.T("▶ Démarrer", "▶ Start");
            _startBtn.BackColor = Theme.Accent;
            return;
        }

        _startBtn.Text = Lang.T("⏹ Arrêter", "⏹ Stop");
        _startBtn.BackColor = Color.FromArgb(200, 60, 60);

        string addr = $"{ServerHost.GetLocalIp()}:{_server.Port}";
        _monitorLbl.Text = $"● EN LIGNE  •  {_server.Loader} {_server.McVersion}  •  {addr}";

        try
        {
            // Cache the PID: only rescan if cached process is dead
            if (_cachedJavaPid == 0 || IsProcessDead(_cachedJavaPid))
            {
                _cachedJavaPid = 0;
                foreach (var p in Process.GetProcessesByName("java"))
                {
                    try
                    {
                        if (p.MainModule?.FileName?.Contains("javaw") == true)
                        { _cachedJavaPid = p.Id; break; }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            if (_cachedJavaPid != 0)
            {
                using var proc = Process.GetProcessById(_cachedJavaPid);
                long ramMb = proc.WorkingSet64 / 1024 / 1024;
                double cpu = proc.TotalProcessorTime.TotalMilliseconds
                    / (DateTime.Now - proc.StartTime).TotalMilliseconds * 100;
                _monitorLbl.Text += $"  •  📊 {ramMb} Mo  •  CPU {cpu:F1}%";
            }
        }
        catch { }
    }

    private static bool IsProcessDead(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return false; }
        catch { return true; }
    }

    // ============ NAV PAGES ============

    private void ShowConsole()
    {
        _content.SuspendLayout();
        _content.Controls.Clear();

        var cmdRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 44, WrapContents = false,
            Padding = new Padding(0, 4, 0, 0)
        };

        _cmdBox.Dock = DockStyle.Bottom;

        var sendBtn = new Button
        {
            Text = Lang.T("Envoyer", "Send"), Width = 100, Height = 34,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9f),
            ForeColor = Color.White, BackColor = Theme.Accent, Cursor = Cursors.Hand
        };
        sendBtn.FlatAppearance.BorderSize = 0;
        sendBtn.Click += (_, _) => SendCommand();

        void QuickBtn(string text)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, Height = 34,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8f),
                ForeColor = Theme.TextDim, BackColor = Theme.Card, Cursor = Cursors.Hand,
                Margin = new Padding(4, 1, 0, 0)
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (_, _) => { _cmdBox.Text = text; SendCommand(); };
            cmdRow.Controls.Add(b);
        }

        QuickBtn("list");
        QuickBtn("save-all");
        QuickBtn("say Bonjour");
        QuickBtn("whitelist add");
        QuickBtn("whitelist remove");
        QuickBtn("op");
        QuickBtn("deop");
        QuickBtn("ban");
        QuickBtn("pardon");
        QuickBtn("kick");
        QuickBtn("difficulty");
        QuickBtn("gamemode");

        cmdRow.Controls.Add(sendBtn);

        _content.Controls.Add(_logBox);
        _content.Controls.Add(_cmdBox);
        _content.Controls.Add(cmdRow);
        _content.ResumeLayout();
    }

    private void ShowPlayers()
    {
        _content.SuspendLayout();
        _content.Controls.Clear();

        var topBar = new Panel { Dock = DockStyle.Top, Height = 50 };
        var refreshBtn = new Button
        {
            Text = Lang.T("🔄 Actualiser", "🔄 Refresh"), Width = 160, Height = 36,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9f),
            ForeColor = Theme.Text, BackColor = Theme.Card, Cursor = Cursors.Hand,
            Location = new Point(0, 6)
        };
        refreshBtn.FlatAppearance.BorderSize = 0;

        var playerList = new ListBox
        {
            Dock = DockStyle.Fill, BackColor = Theme.Card, ForeColor = Theme.Text,
            Font = new Font("Consolas", 10f), BorderStyle = BorderStyle.None,
            IntegralHeight = false
        };

        refreshBtn.Click += async (_, _) =>
        {
            playerList.Items.Clear();
            if (!ServerHost.IsRunning(_server))
            {
                playerList.Items.Add(Lang.T("Serveur arrêté", "Server stopped"));
                return;
            }
            playerList.Items.Add(Lang.T("Chargement…", "Loading…"));
            await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("java", $"-jar server.jar list")
                    {
                        WorkingDirectory = ServerHost.Dir(_server),
                        RedirectStandardOutput = true, UseShellExecute = false,
                        CreateNoWindow = true
                    };
                }
                catch { }
            });
            // Utiliser la commande list via la console
            ServerHost.SendCommand(_server.Id, "list");
            playerList.Items.Clear();
            playerList.Items.Add(Lang.T("Rafraîchis la console pour voir les joueurs.", "Refresh the console to see players."));
        };

        // Actions rapides
        var actionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 44, WrapContents = false,
            Padding = new Padding(0, 4, 0, 0)
        };

        void PlayerAction(string label, string cmd)
        {
            var b = new Button
            {
                Text = label, AutoSize = true, Height = 34,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9f),
                ForeColor = Theme.Accent, BackColor = Theme.Card, Cursor = Cursors.Hand,
                Margin = new Padding(4, 1, 0, 0)
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (_, _) =>
            {
                string? p = PromptText(label, Lang.T("Pseudo du joueur :", "Player name:"));
                if (!string.IsNullOrWhiteSpace(p))
                    ServerHost.SendCommand(_server.Id, $"{cmd} {p}");
            };
            actionRow.Controls.Add(b);
        }

        PlayerAction("+ Whitelist", "whitelist add");
        PlayerAction("- Whitelist", "whitelist remove");
        PlayerAction("OP", "op");
        PlayerAction("Deop", "deop");
        PlayerAction("Kick", "kick");
        PlayerAction("Ban", "ban");
        PlayerAction("Pardon", "pardon");

        topBar.Controls.Add(refreshBtn);

        _content.Controls.Add(playerList);
        _content.Controls.Add(actionRow);
        _content.Controls.Add(topBar);
        _content.ResumeLayout();

        // Auto-load
        refreshBtn.PerformClick();
    }

    private void ShowContent()
    {
        _content.SuspendLayout();
        _content.Controls.Clear();

        var topBar = new Panel { Dock = DockStyle.Top, Height = 50 };

        var mapBtn = new Button
        {
            Text = Lang.T("🗺 Importer une map", "🗺 Import world"),
            Width = 200, Height = 36, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f), ForeColor = Theme.Text,
            BackColor = Theme.Card, Cursor = Cursors.Hand, Location = new Point(0, 6)
        };
        mapBtn.FlatAppearance.BorderSize = 0;
        mapBtn.Click += (_, _) => ImportMap();

        var modsBtn = new Button
        {
            Text = Lang.T("📦 Gérer les mods", "📦 Manage mods"),
            Width = 200, Height = 36, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f), ForeColor = Theme.Text,
            BackColor = Theme.Card, Cursor = Cursors.Hand, Location = new Point(210, 6)
        };
        modsBtn.FlatAppearance.BorderSize = 0;
        modsBtn.Click += (_, _) => ShowModsDialog();

        var worldLibBtn = new Button
        {
            Text = Lang.T("📚 Bibliothèque de mondes", "📚 World library"),
            Width = 220, Height = 36, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f), ForeColor = Theme.Text,
            BackColor = Theme.Card, Cursor = Cursors.Hand, Location = new Point(420, 6)
        };
        worldLibBtn.FlatAppearance.BorderSize = 0;
        worldLibBtn.Click += (_, _) => ShowWorldLibrary();

        topBar.Controls.AddRange(new Control[] { mapBtn, modsBtn, worldLibBtn });

        // Liste des fichiers du monde
        var fileList = new ListBox
        {
            Dock = DockStyle.Fill, BackColor = Theme.Card, ForeColor = Theme.Text,
            Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None,
            IntegralHeight = false
        };

        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Text = Lang.T(
                "L'import remplace le monde actuel (backup automatique).\n" +
                "Les mods (.jar) s'activent au prochain démarrage.",
                "Importing replaces the current world (auto-backup).\n" +
                "Mods (.jar) activate on next start."),
            ForeColor = Theme.TextDim, Font = new Font("Segoe UI", 9f),
            Height = 40, Padding = new Padding(0, 4, 0, 0)
        };

        // Remplir la liste
        string worldDir = ServerHost.WorldDir(_server);
        if (Directory.Exists(worldDir))
        {
            foreach (var f in Directory.GetFiles(worldDir, "*", SearchOption.AllDirectories).Take(100))
            {
                string rel = Path.GetRelativePath(worldDir, f);
                long size = new FileInfo(f).Length;
                string sizeStr = size > 1_000_000 ? $"{size / 1_000_000.0:F1} Mo" : $"{size / 1_000.0:F0} Ko";
                fileList.Items.Add($"  {rel}  ({sizeStr})");
            }
        }
        if (fileList.Items.Count == 0)
            fileList.Items.Add(Lang.T("  Aucun fichier dans le dossier world/", "  No files in world/ directory"));

        _content.Controls.Add(fileList);
        _content.Controls.Add(hint);
        _content.Controls.Add(topBar);
        _content.ResumeLayout();
    }

    private void ShowSettings()
    {
        _content.SuspendLayout();
        _content.Controls.Clear();

        ServerHost.ApplyProperties(_server);

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, BackColor = Theme.Bg,
            Padding = new Padding(8)
        };

        void AddSetting(string label, Control ctrl)
        {
            var row = new Panel { Height = 40, Width = 700 };
            var lbl = new Label
            {
                Text = label, ForeColor = Theme.TextDim,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(0, 10), AutoSize = true
            };
            ctrl.Location = new Point(220, 6);
            if (ctrl is TextBox tb) { tb.Width = 300; tb.BackColor = Theme.Card; tb.ForeColor = Theme.Text; tb.BorderStyle = BorderStyle.None; tb.Padding = new Padding(4); }
            if (ctrl is ComboBox cb) { cb.Width = 300; cb.BackColor = Theme.Card; cb.ForeColor = Theme.Text; cb.FlatStyle = FlatStyle.Flat; }
            if (ctrl is NumericUpDown nud) { nud.Width = 120; nud.BackColor = Theme.Card; nud.ForeColor = Theme.Text; }
            row.Controls.Add(lbl);
            row.Controls.Add(ctrl);
            panel.Controls.Add(row);
        }

        // Port
        var portBox = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = _server.Port };
        portBox.ValueChanged += (_, _) => _server.Port = (int)portBox.Value;
        AddSetting("Port :", portBox);

        // MOTD
        var motdBox = new TextBox { Text = _server.Motd };
        motdBox.TextChanged += (_, _) => _server.Motd = motdBox.Text;
        AddSetting("MOTD :", motdBox);

        // Whitelist
        var wlChk = new CheckBox { Text = Lang.T("Activée", "Enabled"), Checked = _server.WhitelistEnabled };
        wlChk.CheckedChanged += (_, _) => _server.WhitelistEnabled = wlChk.Checked;
        AddSetting("Whitelist :", wlChk);

        // Max RAM
        var ramNum = new NumericUpDown { Minimum = 1, Maximum = 16, Value = _server.MaxRamGb };
        ramNum.ValueChanged += (_, _) => _server.MaxRamGb = (int)ramNum.Value;
        AddSetting("RAM max (Go) :", ramNum);

        // Auto restart
        var autoChk = new CheckBox { Text = Lang.T("Relancer si crash", "Restart on crash"), Checked = _server.AutoRestart };
        autoChk.CheckedChanged += (_, _) => _server.AutoRestart = autoChk.Checked;
        AddSetting("Redémarrage :", autoChk);

        // Restart at
        var restartBox = new TextBox { Text = _server.RestartAt, PlaceholderText = "HH:mm (vide = désactivé)" };
        restartBox.TextChanged += (_, _) => _server.RestartAt = restartBox.Text;
        AddSetting("Restart quotidien :", restartBox);

        // Welcome message
        var welcomeBox = new TextBox { Text = _server.WelcomeMessage, PlaceholderText = "{joueur} = pseudo" };
        welcomeBox.TextChanged += (_, _) => _server.WelcomeMessage = welcomeBox.Text;
        AddSetting("Message d'accueil :", welcomeBox);

        // Discord webhook
        var webhookBox = new TextBox { Text = _server.DiscordWebhookUrl, PlaceholderText = "https://discord.com/api/webhooks/..." };
        webhookBox.TextChanged += (_, _) => _server.DiscordWebhookUrl = webhookBox.Text;
        AddSetting("Discord webhook :", webhookBox);

        // Bouton sauvegarder
        var saveRow = new Panel { Height = 50, Width = 700 };
        var saveBtn = new Button
        {
            Text = Lang.T("💾 Sauvegarder", "💾 Save"),
            Width = 200, Height = 38, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White, BackColor = Theme.Accent,
            Cursor = Cursors.Hand, Location = new Point(220, 4)
        };
        saveBtn.FlatAppearance.BorderSize = 0;
        saveBtn.Click += (_, _) =>
        {
            DataStore.Save();
            Notifier.Show(Lang.T("Réglages sauvegardés", "Settings saved"),
                Lang.T("Les modifications ont été appliquées.", "Changes have been applied."));
        };
        saveRow.Controls.Add(saveBtn);
        panel.Controls.Add(saveRow);

        // Tunnel Internet
        var tunnelRow = new Panel { Height = 50, Width = 700 };
        var tunnelBtn = new Button
        {
            Text = Lang.T("🌍 Ouvrir sur Internet (playit.gg)", "🌍 Open to Internet (playit.gg)"),
            Width = 300, Height = 38, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Theme.Accent, BackColor = Theme.Card,
            Cursor = Cursors.Hand, Location = new Point(220, 4)
        };
        tunnelBtn.FlatAppearance.BorderSize = 0;
        tunnelBtn.Click += async (_, _) =>
        {
            try
            {
                if (!ServerHost.IsTunnelInstalled)
                {
                    tunnelBtn.Text = Lang.T("⬇ Installation playit.gg…", "⬇ Installing playit.gg…");
                    await ServerHost.DownloadTunnelAsync();
                }
                if (!ServerHost.IsTunnelRunning(_server.Id))
                {
                    tunnelBtn.Text = Lang.T("⏳ Démarrage du tunnel…", "⏳ Starting tunnel…");
                    ServerHost.StartTunnel(_server.Id);
                    await Task.Delay(2000);
                }
                string addr = string.IsNullOrEmpty(_server.PublicAddress) ? "en attente…" : _server.PublicAddress;
                MessageBox.Show(
                    Lang.T($"Adresse publique : {addr}\n\nPartage cette adresse avec tes amis !",
                        $"Public address: {addr}\n\nShare this address with your friends!"),
                    "Team Launcher");
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lang.T("Erreur tunnel :\n", "Tunnel error:\n") + ex.Message, "Team Launcher");
            }
        };
        tunnelRow.Controls.Add(tunnelBtn);
        panel.Controls.Add(tunnelRow);

        _content.Controls.Add(panel);
        _content.ResumeLayout();
    }

    // ============ ACTIONS ============

    private async Task ToggleStartStop()
    {
        if (ServerHost.IsRunning(_server))
        {
            ServerHost.Stop(_server);
            _monitorTimer.Stop();
            _startBtn.Text = Lang.T("▶ Démarrer", "▶ Start");
            _startBtn.BackColor = Theme.Accent;
            _monitorLbl.Text = $"{_server.Loader} {_server.McVersion}  •  port {_server.Port}  •  ○ Arrêté";
        }
        else
        {
            _startBtn.Text = Lang.T("⏳ Démarrage…", "⏳ Starting…");
            _startBtn.BackColor = Color.FromArgb(180, 140, 40);
            try
            {
                await Task.Run(() => ServerHost.Start(_server));
                _monitorTimer.Start();
                _startBtn.Text = Lang.T("⏹ Arrêter", "⏹ Stop");
                _startBtn.BackColor = Color.FromArgb(200, 60, 60);
            }
            catch (Exception ex)
            {
                _startBtn.Text = Lang.T("▶ Démarrer", "▶ Start");
                _startBtn.BackColor = Theme.Accent;
                MessageBox.Show(
                    Lang.T("Échec du démarrage :\n", "Failed to start:\n") + ex.Message,
                    "Team Launcher");
            }
        }
    }

    private void SendCommand()
    {
        string cmd = _cmdBox.Text.Trim();
        if (cmd.Length == 0) return;
        ServerHost.SendCommand(_server.Id, cmd);
        _cmdBox.Text = "";
    }

    private void ImportMap()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Fichiers Minecraft|*.zip;*.rar;*.tar.gz;*.mcworld|" +
                     "Dossiers de monde|server.properties",
            Title = Lang.T("Importer une map serveur", "Import server world")
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            string dest = ServerHost.WorldDir(_server);
            if (File.Exists(dlg.FileName))
            {
                string backup = ServerHost.BackupWorld(_server);
                System.IO.Compression.ZipFile.ExtractToDirectory(dlg.FileName, dest, true);
                Notifier.Show(Lang.T("Map importée", "World imported"),
                    Lang.T($"Backup dans : {Path.GetFileName(backup)}", $"Backup at: {Path.GetFileName(backup)}"));
            }
            else if (Directory.Exists(dlg.FileName))
            {
                ServerHost.ImportWorld(_server, dlg.FileName);
                Notifier.Show(Lang.T("Map importée", "World imported"), "");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(Lang.T("Erreur d'import :\n", "Import error:\n") + ex.Message, "Team Launcher");
        }
    }

    private void ShowModsDialog()
    {
        string modsDir = Path.Combine(ServerHost.Dir(_server), "mods");
        Directory.CreateDirectory(modsDir);
        try { Process.Start(new ProcessStartInfo(modsDir) { UseShellExecute = true }); }
        catch { }
    }

    private void ShowWorldLibrary()
    {
        string libDir = Path.Combine(ServerHost.Root, "world-library");
        Directory.CreateDirectory(libDir);
        try { Process.Start(new ProcessStartInfo(libDir) { UseShellExecute = true }); }
        catch { }
    }

    private string? PromptText(string title, string prompt)
    {
        using var dlg = new InputDialog(title, prompt);
        return dlg.ShowDialog(FindForm()) == DialogResult.OK ? dlg.Value : null;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ServerHost.LogEmitted -= OnLogLine;
        _monitorTimer.Stop();
        _monitorTimer.Dispose();
        base.OnHandleDestroyed(e);
    }
}
