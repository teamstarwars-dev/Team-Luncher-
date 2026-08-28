using System.Drawing.Drawing2D;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Page Skins améliorée : recherche en ligne, onglets, favoris, context menu, import multiple,
/// aperçu 3D avec animations (idle / marche), grille de cards modernes.
/// </summary>
public class SkinsPage : UserControl, IRefreshable
{
    private readonly FlowLayoutPanel skinGrid = new();
    private readonly SkinPreview preview = new();
    private readonly Button applyBtn;
    private readonly TextBox searchBox;
    private readonly FlowLayoutPanel tabRow;
    private readonly Label statusLabel;

    private string _selectedSkin = "";
    private string _activeTab = "all"; // all, official, favorites, online
    private List<string> _favorites = new();
    private readonly List<OnlineSkin> _onlineSkins = new();
    private bool _loadingOnline;
    private string _hoverFile = "";

    private static readonly string FavFile = Path.Combine(
        Path.GetDirectoryName(typeof(SkinsPage).Assembly.Location)!,
        "..", "..", "..", "skins-favorites.json");

    private sealed class OnlineSkin
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
    }

    public SkinsPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            Padding = new Padding(24, 16, 24, 16)
        };

        // --- Titre ---
        var title = new Label
        {
            Text = Lang.T("SKINS", "SKINS"),
            ForeColor = Theme.Text,
            Font = Theme.Title,
            AutoSize = true,
            Location = new Point(0, 0)
        };

        // --- Barre de recherche ---
        searchBox = new TextBox
        {
            Location = new Point(0, 36),
            Width = 320,
            Height = 28,
            Font = new Font("Segoe UI", 9.5f),
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = Lang.T("🔍 Rechercher un skin ou un pseudo...", "🔍 Search skin or username..."),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        Theme.Round(searchBox, 6);
        searchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (_activeTab == "online")
                    _ = LoadOnlineSkinsAsync(searchBox.Text.Trim());
                else
                    RefreshData();
            }
        };

        // --- Onglets ---
        tabRow = new FlowLayoutPanel
        {
            Location = new Point(0, 72),
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        MakeTab(Lang.T("📦 Tous", "📦 All"), "all");
        MakeTab(Lang.T("⭐ Officiels", "⭐ Official"), "official");
        MakeTab(Lang.T("❤ Favoris", "❤ Favorites"), "favorites");
        MakeTab(Lang.T("🌐 En ligne", "🌐 Online"), "online");

        // --- Grille ---
        skinGrid.AutoScroll = true;
        skinGrid.WrapContents = true;
        skinGrid.FlowDirection = FlowDirection.LeftToRight;
        skinGrid.BackColor = Theme.Bg;
        skinGrid.Location = new Point(0, 108);
        skinGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        skinGrid.Padding = new Padding(0, 8, 0, 0);

        // --- Panneau de droite (preview + actions) ---
        var previewHost = new Panel
        {
            Width = 280,
            Dock = DockStyle.Right,
            BackColor = Theme.Card,
            Padding = new Padding(8)
        };
        Theme.Round(previewHost, 8);

        preview.Dock = DockStyle.Fill;
        preview.BackColor = Theme.Card;

        // Boutons d'action
        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 140,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Padding = new Padding(4, 8, 4, 4)
        };

        var importBtn = MakeActionBtn("📥 Importer", "📥 Import");
        importBtn.Click += (_, _) => ImportSkins();
        btnPanel.Controls.Add(importBtn);

        var importMultiBtn = MakeActionBtn("📁 Importer plusieurs", "📁 Batch import");
        importMultiBtn.Click += (_, _) => ImportSkinsMulti();
        btnPanel.Controls.Add(importMultiBtn);

        var exportBtn = MakeActionBtn("📤 Exporter", "📤 Export");
        exportBtn.Click += (_, _) => ExportSelectedSkin();
        btnPanel.Controls.Add(exportBtn);

        applyBtn = MakeActionBtn("✓ Appliquer ce skin", "✓ Apply skin", primary: true);
        applyBtn.Click += (_, _) => ApplySelectedSkin();
        btnPanel.Controls.Add(applyBtn);

        var favBtn = MakeActionBtn("❤ Favori", "❤ Favorite");
        favBtn.Click += (_, _) => ToggleFavorite();
        btnPanel.Controls.Add(favBtn);

        // Status
        statusLabel = new Label
        {
            Text = "",
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8f),
            Dock = DockStyle.Bottom,
            Height = 18,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };

        previewHost.Controls.Add(preview);
        previewHost.Controls.Add(btnPanel);
        previewHost.Controls.Add(statusLabel);

        // --- Bouton en bas (import) ---
        var bottomBar = new Panel
        {
            Height = 0,
            Dock = DockStyle.Bottom,
            BackColor = Color.Transparent
        };

        root.Controls.Add(skinGrid);
        root.Controls.Add(previewHost);
        root.Controls.Add(bottomBar);
        root.Controls.Add(tabRow);
        root.Controls.Add(searchBox);
        root.Controls.Add(title);
        Controls.Add(root);

        Resize += (_, _) =>
        {
            previewHost.Width = Math.Min(280, Math.Max(220, Width / 3));
        };

        LoadFavorites();
    }

    private void MakeTab(string label, string key)
    {
        var btn = new Button
        {
            Text = label,
            Height = 30,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            ForeColor = _activeTab == key ? Theme.Accent : Theme.TextDim,
            BackColor = _activeTab == key ? Theme.Hover : Color.Transparent,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 4, 0),
            Tag = key
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Theme.Hover;
        btn.Click += (_, _) =>
        {
            _activeTab = key;
            // Rebuild tabs visually
            foreach (Control c in tabRow.Controls)
            {
                if (c is Button b && b.Tag is string k)
                {
                    b.ForeColor = k == key ? Theme.Accent : Theme.TextDim;
                    b.BackColor = k == key ? Theme.Hover : Color.Transparent;
                }
            }
            RefreshData();
        };
        tabRow.Controls.Add(btn);
    }

    private static Button MakeActionBtn(string fr, string en, bool primary = false)
    {
        var btn = new Button
        {
            Text = Lang.T(fr, en),
            Height = 32,
            AutoSize = true,
            Padding = new Padding(10, 0, 10, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.5f),
            Margin = new Padding(2)
        };
        if (primary)
            Theme.Apply(btn, primary: true);
        else
        {
            btn.ForeColor = Theme.TextDim;
            btn.BackColor = Theme.Bg;
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Theme.Hover;
        }
        return btn;
    }

    // ---- Favoris ----

    private void LoadFavorites()
    {
        try
        {
            if (File.Exists(FavFile))
                _favorites = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FavFile)) ?? new();
        }
        catch { _favorites = new(); }
    }

    private void SaveFavorites()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FavFile)!);
            File.WriteAllText(FavFile, JsonSerializer.Serialize(_favorites));
        }
        catch { }
    }

    private void ToggleFavorite()
    {
        if (string.IsNullOrEmpty(_selectedSkin)) return;
        string name = Path.GetFileNameWithoutExtension(_selectedSkin);
        if (_favorites.Contains(name))
            _favorites.Remove(name);
        else
            _favorites.Add(name);
        SaveFavorites();
        RefreshData();
    }

    // ---- Import / Export ----

    private void ImportSkins()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Skin Minecraft|*.png",
            Title = Lang.T("Importer un skin", "Import a skin")
        };
        if (ofd.ShowDialog(FindForm()) != DialogResult.OK) return;
        var dest = Path.Combine(DataStore.SkinsDir, Path.GetFileName(ofd.FileName));
        File.Copy(ofd.FileName, dest, overwrite: true);
        _selectedSkin = dest;
        RefreshData();
    }

    private void ImportSkinsMulti()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Skin Minecraft|*.png",
            Title = Lang.T("Importer plusieurs skins", "Import multiple skins"),
            Multiselect = true
        };
        if (ofd.ShowDialog(FindForm()) != DialogResult.OK) return;
        int count = 0;
        foreach (var f in ofd.FileNames)
        {
            var dest = Path.Combine(DataStore.SkinsDir, Path.GetFileName(f));
            File.Copy(f, dest, overwrite: true);
            count++;
        }
        _selectedSkin = Path.Combine(DataStore.SkinsDir, Path.GetFileName(ofd.FileNames[0]));
        RefreshData();
        statusLabel.Text = Lang.T($"{count} skin(s) importé(s)", $"{count} skin(s) imported");
    }

    private void ExportSelectedSkin()
    {
        if (string.IsNullOrEmpty(_selectedSkin) || !File.Exists(_selectedSkin)) return;
        using var sfd = new SaveFileDialog
        {
            Filter = "PNG|*.png",
            FileName = Path.GetFileName(_selectedSkin),
            Title = Lang.T("Exporter le skin", "Export skin")
        };
        if (sfd.ShowDialog(FindForm()) != DialogResult.OK) return;
        File.Copy(_selectedSkin, sfd.FileName, overwrite: true);
        statusLabel.Text = Lang.T("Skin exporté !", "Skin exported!");
    }

    // ---- Online search ----

    private async Task LoadOnlineSkinsAsync(string query)
    {
        if (_loadingOnline) return;
        _loadingOnline = true;
        _onlineSkins.Clear();
        statusLabel.Text = Lang.T("Chargement...", "Loading...");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TeamLauncher/1.0");

            if (!string.IsNullOrWhiteSpace(query))
            {
                // Search by username via mineskin.eu
                string name = query.Trim();
                _onlineSkins.Add(new OnlineSkin
                {
                    Name = name,
                    DownloadUrl = $"https://mineskin.eu/download/{Uri.EscapeDataString(name)}",
                    Url = $"https://namemc.com/profile/{Uri.EscapeDataString(name)}"
                });
            }
            else
            {
                // Trending skins from namemc (scrape top skins page)
                try
                {
                    string html = await http.GetStringAsync("https://namemc.com/minecraft-skins/trending");
                    // Parse skin names from trending page (simplified extraction)
                    var matches = System.Text.RegularExpressions.Regex.Matches(html,
                        @"/profile/([a-zA-Z0-9_]{3,16})");
                    var seen = new HashSet<string>();
                    foreach (System.Text.RegularExpressions.Match m in matches)
                    {
                        string n = m.Groups[1].Value;
                        if (seen.Add(n))
                        {
                            _onlineSkins.Add(new OnlineSkin
                            {
                                Name = n,
                                DownloadUrl = $"https://mineskin.eu/download/{Uri.EscapeDataString(n)}",
                                Url = $"https://namemc.com/profile/{Uri.EscapeDataString(n)}"
                            });
                        }
                        if (_onlineSkins.Count >= 30) break;
                    }
                }
                catch
                {
                    // Fallback: use skinmc or just show error
                    statusLabel.Text = Lang.T("Erreur de chargement", "Loading error");
                }
            }
        }
        catch (Exception ex)
        {
            statusLabel.Text = Lang.T($"Erreur: {ex.Message}", $"Error: {ex.Message}");
        }

        _loadingOnline = false;
        RefreshData();
    }

    // ---- Refresh / Grid ----

    public void RefreshData()
    {
        // Auto-download official skin if needed
        if (DataStore.Settings.AccountMode == "microsoft" && !string.IsNullOrEmpty(DataStore.Settings.PlayerName))
        {
            string official = Path.Combine(DataStore.SkinsDir, DataStore.Settings.PlayerName + ".png");
            if (!File.Exists(official))
            {
                string name = DataStore.Settings.PlayerName;
                Task.Run(async () =>
                {
                    try
                    {
                        using var http = new HttpClient();
                        byte[] data = await http.GetByteArrayAsync(
                            $"https://mc-heads.net/skin/{Uri.EscapeDataString(name)}");
                        await File.WriteAllBytesAsync(official, data);
                        BeginInvoke(RefreshData);
                    }
                    catch { }
                });
            }
        }

        skinGrid.SuspendLayout();
        skinGrid.Controls.Clear();

        if (_activeTab == "online")
        {
            // Online skins
            foreach (var s in _onlineSkins)
                skinGrid.Controls.Add(MakeOnlineSkinCard(s));
            if (_onlineSkins.Count == 0 && !_loadingOnline)
            {
                skinGrid.Controls.Add(new Label
                {
                    Text = Lang.T("Recherche un pseudo ou clique « En ligne » pour voir les skins tendance.",
                        "Search a username or click « Online » to see trending skins."),
                    ForeColor = Theme.TextDim,
                    AutoSize = true,
                    Margin = new Padding(6, 24, 0, 0)
                });
            }
        }
        else
        {
            // Local skins
            var files = Directory.GetFiles(DataStore.SkinsDir, "*.png").OrderBy(f => f).ToList();

            // Filter
            string filter = searchBox.Text.Trim().ToLowerInvariant();
            if (_activeTab == "official")
            {
                string officialName = DataStore.Settings.PlayerName?.ToLowerInvariant() ?? "";
                files = files.Where(f =>
                    Path.GetFileNameWithoutExtension(f).ToLowerInvariant() == officialName).ToList();
            }
            else if (_activeTab == "favorites")
            {
                files = files.Where(f =>
                    _favorites.Contains(Path.GetFileNameWithoutExtension(f))).ToList();
            }
            else if (!string.IsNullOrEmpty(filter))
            {
                files = files.Where(f =>
                    Path.GetFileNameWithoutExtension(f).ToLowerInvariant().Contains(filter)).ToList();
            }

            if (_selectedSkin.Length == 0)
            {
                string official = Path.Combine(DataStore.SkinsDir, DataStore.Settings.PlayerName + ".png");
                _selectedSkin = File.Exists(official)
                    ? official
                    : files.FirstOrDefault() ?? "";
            }

            foreach (var f in files)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                bool selected = _selectedSkin == f;
                bool isFav = _favorites.Contains(name);
                skinGrid.Controls.Add(MakeSkinCard(f, name, selected, isFav));
            }

            if (skinGrid.Controls.Count == 0)
            {
                string msg = _activeTab == "favorites"
                    ? Lang.T("Aucun favori. Clic droit → Favori pour en ajouter.", "No favorites. Right-click → Favorite to add.")
                    : Lang.T("Aucun skin. Importe un fichier .png.", "No skin. Import a .png file.");
                skinGrid.Controls.Add(new Label
                {
                    Text = msg,
                    ForeColor = Theme.TextDim,
                    AutoSize = true,
                    Margin = new Padding(6, 16, 0, 0)
                });
            }
        }

        skinGrid.ResumeLayout();
        UpdatePreview();
    }

    private Panel MakeSkinCard(string file, string name, bool selected, bool isFav)
    {
        const int cardW = 96, cardH = 112;

        var card = new Panel
        {
            Size = new Size(cardW, cardH),
            Margin = new Padding(6),
            BackColor = selected ? Theme.Hover : Theme.Card,
            Cursor = Cursors.Hand,
            Tag = file
        };
        Theme.Round(card, 8);

        // Head thumbnail
        var thumb = new PictureBox
        {
            Size = new Size(56, 56),
            Location = new Point((cardW - 56) / 2, 8),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.Bg,
            Tag = file
        };
        Theme.Round(thumb, 6);

        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
            using var full = Image.FromStream(fs);
            thumb.Image = SkinTools.MakeHead(name, 56) ?? full.GetThumbnailImage(56, 56, null, IntPtr.Zero);
        }
        catch { }

        // Nom
        var nameLabel = new Label
        {
            Text = name,
            ForeColor = selected ? Theme.Accent : Theme.Text,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            Location = new Point(4, 68),
            Size = new Size(cardW - 8, 18),
            TextAlign = ContentAlignment.TopCenter,
            AutoEllipsis = true,
            BackColor = Color.Transparent,
            Tag = file
        };

        // Favorite star
        if (isFav)
        {
            var star = new Label
            {
                Text = "❤",
                ForeColor = Color.FromArgb(231, 76, 60),
                Font = new Font("Segoe UI", 9f),
                Location = new Point(cardW - 22, 4),
                Size = new Size(18, 18),
                BackColor = Color.Transparent
            };
            card.Controls.Add(star);
        }

        // Selected indicator
        if (selected)
        {
            var dot = new Label
            {
                Text = "●",
                ForeColor = Theme.Accent,
                Font = new Font("Segoe UI", 7f),
                Location = new Point((cardW - 8) / 2, cardH - 16),
                Size = new Size(8, 12),
                BackColor = Color.Transparent
            };
            card.Controls.Add(dot);
        }

        card.Controls.Add(thumb);
        card.Controls.Add(nameLabel);

        // Right-click context menu
        var menu = new ContextMenuStrip();
        menu.BackColor = Theme.Card;
        menu.ForeColor = Theme.Text;
        menu.Renderer = new DarkMenuRenderer();
        menu.Items.Add(Lang.T("❤ Favori / Retirer", "❤ Favorite / Remove"), null, (_, _) => ToggleFavorite());
        menu.Items.Add(Lang.T("✏ Renommer", "✏ Rename"), null, (_, _) => RenameSkin(file));
        menu.Items.Add(Lang.T("📤 Exporter", "📤 Export"), null, (_, _) =>
        {
            _selectedSkin = file;
            ExportSelectedSkin();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Lang.T("🗑 Supprimer", "🗑 Delete"), null, (_, _) => DeleteSkin(file));
        card.ContextMenuStrip = menu;
        thumb.ContextMenuStrip = menu;
        nameLabel.ContextMenuStrip = menu;

        // Events
        EventHandler select = (_, _) =>
        {
            _selectedSkin = file;
            RefreshData();
        };
        EventHandler applyDouble = (_, _) => { _selectedSkin = file; ApplySelectedSkin(); };

        card.Click += select;
        thumb.Click += select;
        nameLabel.Click += select;
        card.DoubleClick += applyDouble;
        thumb.DoubleClick += applyDouble;

        // Hover preview: enlarge on mouse enter
        card.MouseEnter += (_, _) =>
        {
            _hoverFile = file;
            card.BackColor = Theme.Accent;
        };
        card.MouseLeave += (_, _) =>
        {
            _hoverFile = "";
            card.BackColor = selected ? Theme.Hover : Theme.Card;
        };

        var tip = new ToolTip();
        tip.SetToolTip(card, name + (isFav ? " ❤" : ""));
        tip.SetToolTip(thumb, name);

        return card;
    }

    private Panel MakeOnlineSkinCard(OnlineSkin skin)
    {
        const int cardW = 96, cardH = 112;

        var card = new Panel
        {
            Size = new Size(cardW, cardH),
            Margin = new Padding(6),
            BackColor = Theme.Card,
            Cursor = Cursors.Hand,
            Tag = skin
        };
        Theme.Round(card, 8);

        // Head thumbnail
        var thumb = new PictureBox
        {
            Size = new Size(56, 56),
            Location = new Point((cardW - 56) / 2, 8),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.Bg,
            Tag = skin
        };
        Theme.Round(thumb, 6);

        // Load skin from mineskin.eu (head view)
        Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                byte[] data = await http.GetByteArrayAsync(
                    $"https://mineskin.eu/avatar/{Uri.EscapeDataString(skin.Name)}/100");
                var ms = new MemoryStream(data);
                var img = Image.FromStream(ms);
                thumb.BeginInvoke(() => thumb.Image = img);
            }
            catch { }
        });

        // Name
        var nameLabel = new Label
        {
            Text = skin.Name,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            Location = new Point(4, 68),
            Size = new Size(cardW - 8, 18),
            TextAlign = ContentAlignment.TopCenter,
            AutoEllipsis = true,
            BackColor = Color.Transparent
        };

        // Online badge
        var badge = new Label
        {
            Text = "🌐",
            ForeColor = Theme.Accent,
            Font = new Font("Segoe UI", 8f),
            Location = new Point(4, 4),
            Size = new Size(18, 18),
            BackColor = Color.Transparent
        };

        card.Controls.Add(thumb);
        card.Controls.Add(nameLabel);
        card.Controls.Add(badge);

        // Click: download skin and select
        EventHandler downloadAndSelect = async (_, _) =>
        {
            statusLabel.Text = Lang.T($"Téléchargement de {skin.Name}...", $"Downloading {skin.Name}...");
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                byte[] data = await http.GetByteArrayAsync(skin.DownloadUrl);
                string dest = Path.Combine(DataStore.SkinsDir, skin.Name + ".png");
                await File.WriteAllBytesAsync(dest, data);
                _selectedSkin = dest;
                // Switch to local tab
                _activeTab = "all";
                foreach (Control c in tabRow.Controls)
                {
                    if (c is Button b && b.Tag is string k)
                    {
                        b.ForeColor = k == "all" ? Theme.Accent : Theme.TextDim;
                        b.BackColor = k == "all" ? Theme.Hover : Color.Transparent;
                    }
                }
                RefreshData();
                statusLabel.Text = Lang.T($"Skin « {skin.Name} » téléchargé !", $"Skin « {skin.Name} » downloaded!");
            }
            catch (Exception ex)
            {
                statusLabel.Text = Lang.T($"Erreur: {ex.Message}", $"Error: {ex.Message}");
            }
        };
        card.Click += downloadAndSelect;
        thumb.Click += downloadAndSelect;
        nameLabel.Click += downloadAndSelect;

        var tip = new ToolTip();
        tip.SetToolTip(card, Lang.T($"Cliquer pour télécharger « {skin.Name} »", $"Click to download « {skin.Name} »"));

        return card;
    }

    // ---- Skin management ----

    private void RenameSkin(string file)
    {
        string oldName = Path.GetFileNameWithoutExtension(file);
        var dlg = new InputDialog(
            Lang.T("Renommer le skin", "Rename skin"),
            Lang.T("Nouveau nom :", "New name:"),
            oldName);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        string newName = dlg.Value.Trim();
        if (string.IsNullOrEmpty(newName) || newName == oldName) return;
        string newFile = Path.Combine(DataStore.SkinsDir, newName + ".png");
        if (File.Exists(newFile))
        {
            MessageBox.Show(Lang.T("Un skin avec ce nom existe déjà.", "A skin with this name already exists."),
                "Team Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        File.Move(file, newFile);
        // Update favorites
        int idx = _favorites.IndexOf(oldName);
        if (idx >= 0) _favorites[idx] = newName;
        SaveFavorites();
        if (_selectedSkin == file) _selectedSkin = newFile;
        RefreshData();
    }

    private void DeleteSkin(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        var result = MessageBox.Show(
            Lang.T($"Supprimer le skin « {name} » ?", $"Delete skin « {name} »?"),
            "Team Launcher",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;
        File.Delete(file);
        _favorites.Remove(name);
        SaveFavorites();
        if (_selectedSkin == file) _selectedSkin = "";
        RefreshData();
    }

    // ---- Preview ----

    private void UpdatePreview()
    {
        if (_selectedSkin.Length == 0 || !File.Exists(_selectedSkin))
        {
            preview.SetSkin(null);
            return;
        }
        try
        {
            using var fs = new FileStream(_selectedSkin, FileMode.Open, FileAccess.Read);
            using var tmp = Image.FromStream(fs);
            preview.SetSkin(new Bitmap(tmp));
        }
        catch { preview.SetSkin(null); }
    }

    // ---- Apply ----

    private async void ApplySelectedSkin()
    {
        if (_selectedSkin.Length == 0)
        {
            MessageBox.Show(Lang.T("Sélectionne d'abord un skin.", "Select a skin first."), "Team Launcher");
            return;
        }

        applyBtn.Enabled = false;
        applyBtn.Text = Lang.T("Application...", "Applying...");
        int ok = 0;
        var errors = new List<string>();
        await Task.Run(() =>
        {
            foreach (var inst in DataStore.Settings.Instances)
            {
                try
                {
                    SkinService.ApplyAsync(inst, _selectedSkin).GetAwaiter().GetResult();
                    ok++;
                }
                catch (Exception ex) { errors.Add($"{inst.Name} : {ex.Message}"); }
            }
        });
        applyBtn.Enabled = true;
        applyBtn.Text = Lang.T("✓ Appliquer ce skin", "✓ Apply skin");

        MessageBox.Show(
            errors.Count == 0
                ? Lang.T($"Skin appliqué à {ok} instance(s).\nVisible au prochain lancement avec le pseudo « {DataStore.Settings.PlayerName} ».",
                    $"Skin applied to {ok} instance(s).\nVisible on next launch with username « {DataStore.Settings.PlayerName} ».")
                : Lang.T($"Appliqué à {ok} instance(s). Erreurs :\n" + string.Join("\n", errors.Take(4)),
                    $"Applied to {ok} instance(s). Errors:\n" + string.Join("\n", errors.Take(4))),
            "Team Launcher");
    }

    // ---- Dark menu renderer ----
    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            using var br = new SolidBrush(e.Item.Selected ? Theme.Hover : Theme.Card);
            e.Graphics.FillRectangle(br, e.Item.Bounds);
        }
    }
}
