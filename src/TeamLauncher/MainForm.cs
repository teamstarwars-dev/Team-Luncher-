using System.Drawing.Drawing2D;

namespace TeamLauncher;

public class MainForm : Form
{
    private readonly Panel sideNav = new();
    private readonly ContentPanel content = new();
    private readonly List<Button> navButtons = new();
    private readonly Dictionary<string, Control> pages = new();
    private readonly NotifyIcon trayIcon = new();
    private readonly ToolTip toolTip = new();
    private Panel accountChip = new();
    private readonly Panel titleBar = new();
    private bool isMaximized = false;

    /// <summary>Zone centrale : peint l'image de fond personnalisée (assombrie) sous les pages.</summary>
    private class ContentPanel : Panel
    {
        public ContentPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);

            var img = Theme.BgImage;
            if (img == null) return;

            float scale = Math.Max((float)Width / img.Width, (float)Height / img.Height);
            int w = (int)(img.Width * scale), h = (int)(img.Height * scale);
            g.DrawImage(img, (Width - w) / 2, (Height - h) / 2, w, h);

            // voile sombre pour garder le texte lisible
            using var veil = new SolidBrush(Color.FromArgb(195, Theme.Bg));
            g.FillRectangle(veil, ClientRectangle);
        }
    }

    public MainForm()
    {
        Notifier.Init();
        Text = "Team Launcher";
        Size = new Size(1150, 720);
        MinimumSize = new Size(950, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        Font = new Font("Segoe UI", 9f);

        // ---- Barre de titre custom sombre (remplace la barre Windows) ----
        FormBorderStyle = FormBorderStyle.None;
        titleBar.Dock = DockStyle.Top;
        titleBar.Height = 32;
        titleBar.BackColor = Color.FromArgb(18, 18, 22);
        titleBar.Padding = new Padding(0);

        var titleDragArea = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        titleDragArea.MouseDown += (_, _) =>
        {
            if (isMaximized)
            {
                // restaurer avant de drag si maximisé
                WindowState = FormWindowState.Normal;
                isMaximized = false;
                UpdateMaximizeButton();
            }
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, NativeMethods.HT_CAPTION, 0);
        };
        titleDragArea.DoubleClick += (_, _) =>
        {
            ToggleMaximize();
        };
        titleBar.Controls.Add(titleDragArea);

        var btnHeight = 32;
        var btnWidth = 46;

        // Bouton minimiser
        var minBtn = new Button
        {
            Text = "─",
            Dock = DockStyle.Right,
            Width = btnWidth,
            Height = btnHeight,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(160, 170, 180),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        minBtn.FlatAppearance.BorderSize = 0;
        minBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 44, 52);
        minBtn.Click += (_, _) => { WindowState = FormWindowState.Minimized; };
        titleBar.Controls.Add(minBtn);

        // Bouton maximiser / restaurer
        var maxBtn = new Button
        {
            Text = "□",
            Dock = DockStyle.Right,
            Width = btnWidth,
            Height = btnHeight,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(160, 170, 180),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
            Tag = "max"
        };
        maxBtn.FlatAppearance.BorderSize = 0;
        maxBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 44, 52);
        maxBtn.Click += (_, _) => ToggleMaximize();
        titleBar.Controls.Add(maxBtn);

        // Bouton fermer
        var closeBtn = new Button
        {
            Text = "✕",
            Dock = DockStyle.Right,
            Width = btnWidth,
            Height = btnHeight,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(160, 170, 180),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        closeBtn.FlatAppearance.BorderSize = 0;
        closeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 50, 50);
        closeBtn.MouseEnter += (_, _) => closeBtn.ForeColor = Color.White;
        closeBtn.MouseLeave += (_, _) => closeBtn.ForeColor = Color.FromArgb(160, 170, 180);
        closeBtn.Click += (_, _) =>
        {
            trayIcon.Visible = false;
            Close();
        };
        titleBar.Controls.Add(closeBtn);

        Controls.Add(titleBar);

        // Logo partout : fenêtre, barre des tâches, Alt-Tab, tray
        try
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ??
                   System.Drawing.Icon.ExtractAssociatedIcon(AppContext.BaseDirectory);
            trayIcon.Icon = Icon;
        }
        catch { }

        sideNav.Dock = DockStyle.Left;
        sideNav.Width = 56;
        sideNav.BackColor = Theme.Panel;
        sideNav.Padding = new Padding(0);

        // Logo compact : icône seule
        var logoHost = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Panel };
        var logo = new Label
        {
            Text = "🚀",
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 14f, FontStyle.Regular),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(0)
        };
        logoHost.Controls.Add(logo);
        sideNav.Controls.Add(logoHost);

        // Séparateur 1px sous le header
        var headerSep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };
        sideNav.Controls.Add(headerSep);

        // Espacement entre le header et les boutons
        var navSpacer = new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Panel };
        sideNav.Controls.Add(navSpacer);

        AddNav("home", "🏠", "Accueil", () => GetOrCreate("home", () => new HomePage()));
        AddNav("news", "📰", "Actualités", () => GetOrCreate("news", () => new NewsPage()));
        AddNav("instances", "📦", "Instances", () => GetOrCreate("instances", () => new InstancesPage()));
        AddNav("explorer", "🗂️", "Explorateur", () => GetOrCreate("explorer", () => new ExplorerPage()));
        AddNav("skins", "👕", "Skins", () => GetOrCreate("skins", () => new SkinsPage()));
        AddNav("explore", "🔍", "Découvrir", () => GetOrCreate("explore", () => new ExplorePage()));
        AddNav("servers", "🌐", "Serveurs", () => GetOrCreate("servers", () => new ServersPage()));
        AddNav("bedrock", "🪨", "Bedrock", () => GetOrCreate("bedrock", () => new BedrockPage()));
        AddNav("edit", "🗺️", "Éditeur de carte", () => GetOrCreate("edit", () => new MapEditorPage()));
        AddNav("moddev", "🔨", "Développement de mods", () => GetOrCreate("moddev", () => new ModDevPage()));
        AddNav("model3d", "🎨", "Modélisation 3D", () => GetOrCreate("model3d", () => new ModelViewerPage()));

        // Chip de compte (avatar + pseudo) — ajouté AVANT bottomNav pour s'afficher au-dessus
        accountChip = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = Theme.Bg };
        var chipSep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };
        accountChip.Controls.Add(chipSep);
        var chipAvatar = new PictureBox
        {
            Size = new Size(24, 24), Location = new Point(16, 10),
            SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Card
        };
        var chipName = new Label
        {
            Text = DataStore.Settings.PlayerName,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(46, 8),
            AutoSize = true
        };
        var chipMode = new Label
        {
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 7f),
            Location = new Point(46, 24),
            AutoSize = true
        };
        void RefreshAccountChip()
        {
            bool isMs = DataStore.Settings.AccountMode == "microsoft";
            chipName.Text = DataStore.Settings.PlayerName;
            chipMode.Text = isMs ? "Microsoft" : "Hors-ligne";
            if (!isMs)
            {
                var old = chipAvatar.Image;
                chipAvatar.Image = null;
                old?.Dispose();
                return;
            }

            var head = SkinTools.MakeHead(DataStore.Settings.PlayerName);
            if (head != null)
            {
                var oldImg = chipAvatar.Image;
                chipAvatar.Image = head;
                oldImg?.Dispose();
                return;
            }

            string name = DataStore.Settings.PlayerName;
            Task.Run(async () =>
            {
                if (await SkinTools.EnsureOfficialSkinAsync(name))
                    BeginInvoke(RefreshAccountChip);
            });
        }
        RefreshAccountChip();
        AppEvents.AccountChanged += () => { if (IsHandleCreated) BeginInvoke(RefreshAccountChip); };
        accountChip.Controls.Add(chipAvatar);
        accountChip.Controls.Add(chipMode);
        accountChip.Controls.Add(chipName);
        sideNav.Controls.Add(accountChip);

        // Panel pour les boutons du bas (Compte et Paramètres) — sous le profil
        var bottomNav = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Panel };
        var bottomSep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };
        bottomNav.Controls.Add(bottomSep);
        AddNav("settings", "⚙️", "Paramètres", () => GetOrCreate("settings", () => new SettingsPage()), bottomNav);
        AddNav("account", "👤", "Compte", () => GetOrCreate("account", () => new AccountPage()), bottomNav);
        sideNav.Controls.Add(bottomNav);

        // navigation demandée depuis les cartes (clic droit → Modifier, œil → Détails)
        AppEvents.NavigationRequested += key =>
        {
            // page Détails : toujours fraîche, liée à l'instance sélectionnée
            if (key == "detail")
            {
                Show(new InstanceDetailPage(), null);
                return;
            }

            // Panneau de gestion serveur : toujours fraîche, lié au serveur sélectionné
            if (key == "server_panel")
            {
                string? serverId = AppEvents.PendingServerId;
                AppEvents.PendingServerId = null;
                var hs = DataStore.Settings.HostedServers.FirstOrDefault(h => h.Id == serverId);
                if (hs != null)
                {
                    Show(new ServerPanel(hs), null);
                    return;
                }
            }

            int idx = navKeys.IndexOf(key);
            if (idx >= 0 && idx < navButtons.Count)
                Show(GetOrCreate(key, () => key switch
                {
                    "home" => new HomePage(),
                    "news" => new NewsPage(),
                    "instances" => new InstancesPage(),
                    "explorer" => new ExplorerPage(),
                    "skins" => new SkinsPage(),
                    "explore" => new ExplorePage(),
                    "servers" => new ServersPage(),
                    "bedrock" => new BedrockPage(),
                    "edit" => new MapEditorPage(),
                    "moddev" => new ModDevPage(),
                    "model3d" => new ModelViewerPage(),
                    "account" => new AccountPage(),
                    _ => new SettingsPage()
                }), navButtons[idx]);
        };

        content.Dock = DockStyle.Fill;
        content.BackColor = Theme.Bg;

        // ---- panneau des tâches de fond (imports, installations... annulables) ----
        var taskPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = ControlPaint.Dark(Theme.Card, 0.03f),
            Padding = new Padding(10, 4, 10, 6),
            Visible = false
        };
        void RefreshTaskPanel()
        {
            if (IsDisposed) return;
            try { BeginInvoke(() => RebuildTaskPanel(taskPanel)); } catch { }
        }
        AppTasks.Changed += RefreshTaskPanel;
        FormClosed += (_, _) => AppTasks.Changed -= RefreshTaskPanel;

        Controls.Add(content);
        Controls.Add(sideNav);
        Controls.Add(taskPanel);

        Show(GetOrCreate("home", () => new HomePage()), navButtons[0]);

        // ---- glisser-déposer : .mrpack / .zip = importer, .jar = mod pour une instance ----
        AllowDrop = true;
        DragEnter += (_, e) =>
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
            foreach (var file in files)
            {
                switch (Path.GetExtension(file).ToLowerInvariant())
                {
                    case ".mrpack":
                        string mrpackFile = file;
                        _ = AppTasks.Run($"Import du modpack « {Path.GetFileName(file)} »",
                            async (ct, status) =>
                            {
                                var inst = await MrPackImporter.ImportAsync(mrpackFile,
                                    s => status(s), ct);
                                Notifier.Show("Modpack importé", $"« {inst.Name} » est prêt !");
                                AppEvents.NavigateTo("instances");
                            },
                            onError: ex => MessageBox.Show(
                                "Échec de l'import :\n" + ex.Message, "Team Launcher"));
                        break;

                    case ".zip":
                        try
                        {
                            PackService.Import(file);
                            Notifier.Show("Instance importée", "Le .zip a été importé dans tes instances.");
                            AppEvents.NavigateTo("instances");
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Échec de l'import :\n" + ex.Message, "Team Launcher");
                        }
                        break;

                    case ".jar":
                        using (var pick = new InstancePickDialog(
                            $"Installer le mod « {Path.GetFileName(file)} » sur quelle instance ?", "Installer"))
                        {
                            if (pick.ShowDialog(this) == DialogResult.OK && pick.Selected != null)
                            {
                                string modsDir = Path.Combine(DataStore.InstancesRoot, pick.Selected.Id, "mods");
                                Directory.CreateDirectory(modsDir);
                                File.Copy(file, Path.Combine(modsDir, Path.GetFileName(file)), overwrite: true);
                                Notifier.Show("Mod installé",
                                    $"Ajouté à « {pick.Selected.Name} ». Vérifie la compatibilité de version !");
                            }
                        }
                        break;
                }
            }
        };

        // ---- Mode zéro RAM : pendant que Minecraft tourne, le launcher se met en veille ----
        trayIcon.Text = "Team Launcher";
        trayIcon.Visible = true;
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Ouvrir", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("Quitter", null, (_, _) =>
        {
            trayIcon.Visible = false;
            Application.Exit();
        });
        trayIcon.ContextMenuStrip = trayMenu;

        GameLauncher.StateChanged += OnGameStateChanged;
        OnGameStateChanged();

        // ---- vérification de santé silencieuse + mise à jour auto ----
        Shown += async (_, _) =>
        {
            _ = UpdateService.CheckOnStartupAsync();
            _ = TelemetryService.ReportStartupAsync();
            try
            {
                var checks = await HealthService.RunAllAsync();
                var problems = checks.Where(c => !c.Ok).ToList();
                if (problems.Count > 0)
                {
                    var msg = "Quelques points à surveiller :\n\n" +
                              string.Join("\n", problems.Select(p => $"• {p.Name} : {p.Detail}"));
                    BeginInvoke(() => MessageBox.Show(this, msg,
                        "Team Launcher — Diagnostic", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                }
            }
            catch { }

            // Proposition d'import des mondes CurseForge plus récents (silencieux si rien à importer).
            BeginInvoke(OfferWorldImport);
        };

        PresenceService.Init(); // Rich Presence Discord (si activée)

        FormClosed += (_, _) =>
        {
            trayIcon.Visible = false;
            GameLauncher.StateChanged -= OnGameStateChanged;
            PresenceService.Shutdown();
            DataStore.SaveNow();
        };

        // ---- raccourci bureau automatique (première ouverture) ----
        if (!DataStore.Settings.AutoShortcut)
        {
            EnsureDesktopShortcut();
            DataStore.Settings.AutoShortcut = true;
            DataStore.Save();
        }
    }

    private static void RebuildTaskPanel(FlowLayoutPanel panel)
    {
        panel.SuspendLayout();
        panel.Controls.Clear();
        var tasks = AppTasks.Snapshot();
        foreach (var t in tasks)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0, 2, 0, 0)
            };
            row.Controls.Add(new Label
            {
                Text = $"⏳ {t.Title} — {t.Status}",
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 9f),
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0)
            });
            var cancelBtn = new Button
            {
                Text = "✕ Annuler",
                Width = 90,
                Height = 26
            };
            Theme.Apply(cancelBtn);
            cancelBtn.Font = new Font("Segoe UI", 8.5f);
            int id = t.Id;
            cancelBtn.Click += (_, _) => AppTasks.Cancel(id);
            row.Controls.Add(cancelBtn);
            panel.Controls.Add(row);
        }
        panel.Visible = tasks.Count > 0;
        panel.ResumeLayout();
    }

    private void EnsureDesktopShortcut()    {
        try
        {
            string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string lnk = Path.Combine(desk, "Team Launcher.lnk");
            if (File.Exists(lnk)) return;
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            var shortcut = shell.CreateShortcut(lnk);
            shortcut.TargetPath = Application.ExecutablePath;
            shortcut.WorkingDirectory = AppContext.BaseDirectory;
            shortcut.Description = "Lanceur Minecraft léger";
            shortcut.Save();
        }
        catch { /* pas bloquant */ }
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Visible = true;
        WindowState = FormWindowState.Normal;
        isMaximized = false;
        UpdateMaximizeButton();
        Activate();
    }

    private void ToggleMaximize()
    {
        if (isMaximized)
        {
            WindowState = FormWindowState.Normal;
            isMaximized = false;
        }
        else
        {
            WindowState = FormWindowState.Maximized;
            isMaximized = true;
        }
        UpdateMaximizeButton();
    }

    private void UpdateMaximizeButton()
    {
        foreach (Control c in titleBar.Controls)
        {
            if (c is Button b && b.Tag as string == "max")
            {
                b.Text = isMaximized ? "❐" : "□";
                return;
            }
        }
    }

    /// <summary>Bordure dessinée autour de la fenêtre quand elle n'est pas maximisée.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (WindowState == FormWindowState.Normal)
        {
            using var pen = new Pen(Color.FromArgb(30, 255, 255, 255), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    private void OnGameStateChanged()
    {
        if (InvokeRequired) { BeginInvoke(OnGameStateChanged); return; }
        if (GameLauncher.GameRunning)
        {
            // libère la RAM et la barre des tâches pendant la partie
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            Hide();
            trayIcon.ShowBalloonTip(2500, "Team Launcher",
                "Minecraft est lancé !\nLe launcher est en veille pour libérer de la RAM.",
                ToolTipIcon.Info);
        }
        else if (!Visible)
        {
            RestoreFromTray();
        }
    }

    private Control GetOrCreate(string key, Func<Control> create)
    {
        if (!pages.TryGetValue(key, out var page))
        {
            page = create(); // chargement paresseux : une page n'existe qu'à sa première visite
            pages[key] = page;
        }
        return page;
    }

    private readonly List<string> navKeys = new();

    private void AddNav(string key, string icon, string label, Func<Control> pageFactory, Panel? parent = null)
    {
        navKeys.Add(key);
        var b = new Button
        {
            Text = icon,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 34,
            Margin = new Padding(0, 1, 0, 1),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { MouseOverBackColor = Color.Transparent, BorderSize = 0 },
            Font = new Font("Segoe UI", 12f),
            ForeColor = Theme.TextDim,
            BackColor = Color.Transparent
        };
        toolTip.SetToolTip(b, label);
        b.Paint += (_, e) =>
        {
            if (b.Tag is not bool active || !active) return;
            using var brush = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(brush, 0, 6, 2, b.Height - 12);
        };
        b.MouseEnter += (_, _) => { if (b.Tag is not bool active || !active) b.BackColor = Theme.Hover; };
        b.MouseLeave += (_, _) => { if (b.Tag is not bool active || !active) b.BackColor = Color.Transparent; };
        Control? boundPage = null;
        b.Click += (_, _) =>
        {
            boundPage ??= pageFactory();
            Show(boundPage, b);
        };
        navButtons.Add(b);
        (parent ?? sideNav).Controls.Add(b);
        b.BringToFront();
    }

    private void Show(Control page, Button? activeButton)
    {
        foreach (var btn in navButtons)
        {
            bool active = ReferenceEquals(btn, activeButton);
            btn.Tag = active;
            btn.BackColor = Color.Transparent;
            btn.ForeColor = active ? Theme.Text : Theme.TextDim;
            btn.Invalidate();
        }

        if (page is IRefreshable r) r.RefreshData();
        Lang.Apply(page);

        // image de fond : la page devient transparente pour laisser voir l'image peinte derrière
        if (Theme.HasBgImage)
        {
            typeof(Control)
                .GetMethod("SetStyle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(page, new object[] { ControlStyles.SupportsTransparentBackColor, true });
            page.BackColor = Color.Transparent;
        }

        content.SuspendLayout();
        content.Controls.Clear();
        page.Dock = DockStyle.Fill;
        content.Controls.Add(page);
        content.ResumeLayout();
    }

    /// <summary>
    /// Détecte les mondes CurseForge plus récents que ceux du launcher et propose
    /// de les importer. Silencieux si rien à importer ou si aucune instance CurseForge détectée.
    /// </summary>
    private void OfferWorldImport()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            var newer = WorldSyncService.DetectNewerWorldsFromCurseForge();
            if (newer.Count == 0) return;

            // Regroupe par instance du launcher pour proposer le bon dialog
            var perInstance = newer
                .GroupBy(w => w.InstanceName)
                .Where(g => DataStore.Settings.Instances.Any(i =>
                    string.Equals(i.Name, g.Key, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (perInstance.Count == 0) return;

            int total = newer.Count;
            var firstInstance = DataStore.Settings.Instances.First(i =>
                string.Equals(i.Name, perInstance[0].Key, StringComparison.OrdinalIgnoreCase));

            var msg = $"Team Launcher a détecté {total} monde(s) modifié(s) dans CurseForge\n" +
                      $"pour {(perInstance.Count == 1 ? $"« {firstInstance.Name} »" : $"{perInstance.Count} instance(s)")}.\n\n" +
                      "Tu veux les importer maintenant ?";
            var result = MessageBox.Show(this, msg, "Team Launcher",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;

            // Ouvre le dialog du premier pack, l'utilisateur pourra enchaîner manuellement
            using var dlg = new WorldImportDialog(firstInstance);
            dlg.ShowDialog(this);
        }
        catch (Exception ex)
        {
            // pas bloquant : si la détection plante, on continue normalement
            try { File.AppendAllText(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TeamLauncher", "launcher.log"),
                $"[{DateTime.Now:HH:mm:ss}] Erreur détection mondes CF : {ex}\n"); } catch { }
        }
    }

    /// <summary>Navigate to the instances page (for deep link support).</summary>
    public void NavigateToInstances()
    {
        for (int i = 0; i < navButtons.Count; i++)
        {
            if (navKeys[i] == "instances")
            {
                var inst = GetOrCreate("instances", () => new InstancesPage());
                Show(inst, navButtons[i]);
                return;
            }
        }
    }

    /// <summary>Import a shared pack by code (for deep link support).</summary>
    public void ImportSharedPackByCode(string code)
    {
        if (pages.TryGetValue("instances", out var page) && page is InstancesPage instPage)
            instPage.ImportByCode(code);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        const int HTBOTTOMRIGHT = 17;
        const int RESIZE_GRIP = 10;

        if (m.Msg == WM_NCHITTEST)
        {
            int x = (int)(m.LParam.ToInt64() & 0xFFFF);
            int y = (int)((m.LParam.ToInt64() >> 16) & 0xFFFF);
            var pt = PointToClient(new Point(x, y));

            if (WindowState == FormWindowState.Normal)
            {
                if (pt.X >= Width - RESIZE_GRIP && pt.Y >= Height - RESIZE_GRIP)
                {
                    m.Result = (IntPtr)HTBOTTOMRIGHT;
                    return;
                }
            }
        }
        base.WndProc(ref m);
    }
}

internal static class NativeMethods
{
    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HT_CAPTION = 0x2;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool ReleaseCapture();
}
