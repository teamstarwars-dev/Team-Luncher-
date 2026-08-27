namespace TeamLauncher;

/// <summary>
/// Page Skins avec aperçu 3D rotatif et grille de skins modernisée :
/// thumbnails arrondis avec tête découpée, plus de slots type inventaire.
/// </summary>
public class SkinsPage : UserControl, IRefreshable
{
    private readonly FlowLayoutPanel skinGrid = new();
    private readonly SkinPreview preview = new();
    private readonly Button applyBtn;
    private string _selectedSkin = "";

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

        var title = new Label
        {
            Text = "SKINS",
            ForeColor = Theme.Text,
            Font = Theme.Title,
            AutoSize = true,
            Location = new Point(0, 0)
        };

        var hint = new Label
        {
            Text = "Bibliothèque locale + aperçu 3D rotatif. Clic = sélectionner, double-clic = appliquer.",
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            Location = new Point(0, 28)
        };

        // ---- grille de skins (gauche) ----
        skinGrid.AutoScroll = true;
        skinGrid.WrapContents = true;
        skinGrid.FlowDirection = FlowDirection.LeftToRight;
        skinGrid.BackColor = Theme.Bg;
        skinGrid.Location = new Point(0, 62);
        skinGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        skinGrid.Padding = new Padding(0, 8, 0, 0);

        var importBtn = new Button
        {
            Text = "📥 Importer un skin (.png)",
            Height = 36,
            Dock = DockStyle.Bottom,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Theme.TextDim,
            BackColor = Theme.Card,
            Cursor = Cursors.Hand
        };
        importBtn.FlatAppearance.BorderSize = 0;
        importBtn.FlatAppearance.MouseOverBackColor = Theme.Hover;
        importBtn.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = "Skin Minecraft|*.png" };
            if (ofd.ShowDialog(FindForm()) != DialogResult.OK) return;
            var dest = Path.Combine(DataStore.SkinsDir, Path.GetFileName(ofd.FileName));
            File.Copy(ofd.FileName, dest, overwrite: true);
            RefreshData();
        };

        // ---- aperçu 3D (droite) ----
        var previewHost = new Panel
        {
            Width = 260,
            Dock = DockStyle.Right,
            BackColor = Theme.Card,
            Padding = new Padding(8)
        };
        Theme.Round(previewHost, 8);

        preview.Dock = DockStyle.Fill;
        preview.BackColor = Theme.Card;

        var applyLocal = new Button
        {
            Text = "✓ Appliquer ce skin",
            Height = 38,
            Dock = DockStyle.Bottom
        };
        Theme.Apply(applyLocal, primary: true);
        applyLocal.Click += (_, _) => ApplySelectedSkin();
        applyBtn = applyLocal;

        previewHost.Controls.Add(preview);
        previewHost.Controls.Add(applyLocal);
        previewHost.Controls.Add(importBtn);

        root.Controls.Add(skinGrid);
        root.Controls.Add(previewHost);
        root.Controls.Add(title);
        root.Controls.Add(hint);
        Controls.Add(root);

        // Repositionner au resize
        Resize += (_, _) =>
        {
            previewHost.Width = Math.Min(260, Math.Max(200, Width / 3));
        };
    }

    public void RefreshData()
    {
        if (DataStore.Settings.AccountMode == "microsoft")
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

        var files = Directory.GetFiles(DataStore.SkinsDir, "*.png").OrderBy(f => f).ToList();

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
            skinGrid.Controls.Add(MakeSkinCard(f, name, selected));
        }

        if (skinGrid.Controls.Count == 0)
        {
            skinGrid.Controls.Add(new Label
            {
                Text = "Aucun skin. Importe un fichier .png.",
                ForeColor = Theme.TextDim,
                AutoSize = true,
                Margin = new Padding(6, 16, 0, 0)
            });
        }

        skinGrid.ResumeLayout();
        UpdatePreview();
    }

    private Panel MakeSkinCard(string file, string name, bool selected)
    {
        const int cardW = 80, cardH = 96;

        var card = new Panel
        {
            Size = new Size(cardW, cardH),
            Margin = new Padding(6),
            BackColor = selected ? Theme.Hover : Color.Transparent,
            Cursor = Cursors.Hand,
            Tag = file
        };
        Theme.Round(card, 6);

        // Thumbnail : tête découpée du skin
        var thumb = new PictureBox
        {
            Size = new Size(48, 48),
            Location = new Point((cardW - 48) / 2, 6),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.Card,
            Tag = file
        };
        Theme.Round(thumb, 4);

        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
            using var full = Image.FromStream(fs);
            thumb.Image = SkinTools.MakeHead(name, 48) ?? full.GetThumbnailImage(48, 48, null, IntPtr.Zero);
        }
        catch { }

        // Nom
        var nameLabel = new Label
        {
            Text = name,
            ForeColor = selected ? Theme.Accent : Theme.TextDim,
            Font = new Font("Segoe UI", 7.5f),
            Location = new Point(0, 58),
            Size = new Size(cardW, 16),
            TextAlign = ContentAlignment.TopCenter,
            AutoEllipsis = true,
            BackColor = Color.Transparent,
            Tag = file
        };

        // Indicateur sélection
        if (selected)
        {
            var dot = new Label
            {
                Text = "●",
                ForeColor = Theme.Accent,
                Font = new Font("Segoe UI", 6f),
                Location = new Point((cardW - 8) / 2, cardH - 14),
                Size = new Size(8, 10),
                BackColor = Color.Transparent
            };
            card.Controls.Add(dot);
        }

        card.Controls.Add(thumb);
        card.Controls.Add(nameLabel);

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

        var tip = new ToolTip();
        tip.SetToolTip(card, name);
        tip.SetToolTip(thumb, name);

        return card;
    }

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

    private async void ApplySelectedSkin()
    {
        if (_selectedSkin.Length == 0)
        {
            MessageBox.Show("Sélectionne d'abord un skin.", "Team Launcher");
            return;
        }

        applyBtn.Enabled = false;
        applyBtn.Text = "Application...";
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
        applyBtn.Text = "✓ Appliquer ce skin";

        MessageBox.Show(
            errors.Count == 0
                ? $"Skin appliqué à {ok} instance(s).\nVisible au prochain lancement avec le pseudo « {DataStore.Settings.PlayerName} »."
                : $"Appliqué à {ok} instance(s). Erreurs :\n" + string.Join("\n", errors.Take(4)),
            "Team Launcher");
    }
}
