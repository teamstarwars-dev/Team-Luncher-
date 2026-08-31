using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Compression;

namespace TeamLauncher;

/// <summary>
/// Éditeur de mondes avec onglets : édition 2D chunks (MCA), viewer 3D isométrique, et viewer de modèles.
/// </summary>
public class MapEditorPage : UserControl, IRefreshable
{
    private readonly ComboBox instanceBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly FlowLayoutPanel worldsPanel = new();
    private readonly EditorCanvas canvas = new();
    private readonly WorldViewer3D worldViewer3D = new();
    private readonly ModelViewer3D modelViewer3D = new();
    private readonly Label statsLabel = new();
    private string? _selectedWorld;

    public MapEditorPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(24, 16, 24, 10)
        };

        root.Controls.Add(new Label
        {
            Text = "Édition de carte", ForeColor = Theme.Text,
            Font = Theme.Title,
            AutoSize = true
        });
        root.Controls.Add(new Label
        {
            Text = Lang.T("Sélectionne une instance, puis un monde. Glisse la souris pour sélectionner des chunks,\n" +
                   "puis supprime-les (terres abandonnées, chunks corrompus, reset de zones...).", "Select an instance, then a world. Drag to select chunks,\n" +
                   "then delete them (abandoned terrain, corrupted chunks, zone resets...)."),
            ForeColor = Theme.TextDim, AutoSize = true
        });

        // ---- Instance selector ----
        var row1 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
        var instLabel = new Label { Text = Lang.T("Instance :", "Instance:"), ForeColor = Theme.TextDim, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        Theme.ApplyInput(instanceBox);
        instanceBox.Width = 280; instanceBox.Font = new Font("Segoe UI", 10f);
        row1.Controls.Add(instLabel);
        row1.Controls.Add(instanceBox);
        root.Controls.Add(row1);

        // ---- World selector (cards) ----
        root.Controls.Add(new Label
        {
            Text = Lang.T("Monde :", "World:"), ForeColor = Theme.TextDim,
            AutoSize = true, Margin = new Padding(0, 12, 0, 4)
        });

        worldsPanel.AutoSize = true;
        worldsPanel.WrapContents = true;
        worldsPanel.FlowDirection = FlowDirection.LeftToRight;
        worldsPanel.Margin = new Padding(0, 0, 0, 8);
        worldsPanel.MaximumSize = new Size(900, 200);
        root.Controls.Add(worldsPanel);

        // barre d'outils
        var tools = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 4) };
        tools.Controls.Add(MkBtn("🗑 Supprimer la sélection", true, (_, _) => DeleteSelection()));
        tools.Controls.Add(MkBtn("✖ Tout désélectionner", false, (_, _) => { canvas.ClearSelection(); UpdateStats(); }));
        tools.Controls.Add(MkBtn("💾 Sauvegarder le monde", false, (_, _) => BackupWorld()));
        tools.Controls.Add(MkBtn("🏙 Générer une ville (OSM)", false, (_, _) => GenerateCityNative()));
        tools.Controls.Add(MkBtn("⟳ Actualiser", false, async (_, _) => { LoadWorldList(); await LoadWorldChunksAsync(); }));

        // ---- WorldEdit toolbar ----
        var wePanel = new Panel { Height = 42, Margin = new Padding(0, 4, 0, 0) };

        var weBlockBox = new TextBox
        {
            Width = 220, Font = new Font("Consolas", 10f),
            PlaceholderText = "minecraft:stone",
            Location = new Point(0, 9)
        };
        Theme.ApplyInput(weBlockBox);

        var mkWeBtn = (string text, Func<Task> action) =>
        {
            var b = new Button { Text = text, Width = 120, Height = 32, Font = new Font("Segoe UI", 8.5f) };
            Theme.Apply(b);
            b.Click += async (_, _) =>
            {
                try { await action(); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
            };
            return b;
        };

        int weX = 230;
        var weButtons = new (string Text, Func<Task> Action)[]
        {
            ("//set", async () =>
            {
                int c = await canvas.SetBlocksAsync(weBlockBox.Text.Trim().Length > 0 ? weBlockBox.Text.Trim() : "minecraft:stone");
                UpdateStats($"//set : {c} blocs placés.");
            }),
            ("//replace", async () =>
            {
                string from = Microsoft.VisualBasic.Interaction.InputBox(
                    "Remplacer quel bloc ? (* = tous les blocs non-air)", "//replace", "*");
                if (string.IsNullOrWhiteSpace(from)) return;
                int c = await canvas.ReplaceBlocksAsync(from.Trim(), weBlockBox.Text.Trim().Length > 0 ? weBlockBox.Text.Trim() : "minecraft:stone");
                UpdateStats($"//replace : {c} blocs remplacés.");
            }),
            ("//copy", async () =>
            {
                await canvas.CopyAsync();
                UpdateStats("//copy : sélection copiée.");
            }),
            ("//paste", async () =>
            {
                int c = await canvas.PasteAsync();
                UpdateStats($"//paste : {c} blocs collés.");
            }),
            ("//undo", async () =>
            {
                await canvas.UndoAsync();
                UpdateStats("//undo : annulé.");
            }),
            ("//redo", async () =>
            {
                await canvas.RedoAsync();
                UpdateStats("//redo : rétabli.");
            }),
        };

        foreach (var (text, action) in weButtons)
        {
            var btn = mkWeBtn(text, action);
            btn.Location = new Point(weX, 5);
            wePanel.Controls.Add(btn);
            weX += 126;
        }

        canvas.OnWorldEditStatus += msg => UpdateStats(msg);

        statsLabel.ForeColor = Theme.Text;
        statsLabel.Font = new Font("Consolas", 9.5f);
        statsLabel.AutoSize = true;
        statsLabel.Margin = new Padding(4, 2, 0, 2);

        // ---- Onglets pour les vues ----
        var viewTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(160, 30),
            Font = new Font("Segoe UI", 8.75f),
            Padding = new Point(10, 2),
            Height = 440
        };
        viewTabs.DrawItem += (_, e) =>
        {
            bool sel = viewTabs.SelectedIndex == e.Index;
            using (var b = new SolidBrush(sel ? Theme.Card : Theme.Bg))
                e.Graphics.FillRectangle(b, e.Bounds);
            if (sel)
                using (var b = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Bottom - 2, e.Bounds.Width, 2);
            TextRenderer.DrawText(e.Graphics, viewTabs.TabPages[e.Index].Text,
                new Font("Segoe UI", 8.75f), e.Bounds,
                sel ? Theme.Text : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        // Onglet 1 : Éditeur 2D (chunks)
        var editorPage = new TabPage("🗺 Éditeur 2D") { BackColor = Theme.Bg };
        canvas.Dock = DockStyle.Fill;
        canvas.BackColor = Color.FromArgb(18, 20, 24);
        canvas.OnStatsChanged += () => UpdateStats();
        editorPage.Controls.Add(canvas);

        // Onglet 2 : Vue 3D isométrique
        var viewer3dPage = new TabPage("🌍 Vue 3D") { BackColor = Theme.Bg };
        worldViewer3D.Dock = DockStyle.Fill;
        worldViewer3D.BackColor = Color.FromArgb(18, 20, 24);
        worldViewer3D.OnStatusChanged += msg => UpdateStats(msg);
        viewer3dPage.Controls.Add(worldViewer3D);

        // Onglet 3 : Viewer de modèles
        var modelPage = new TabPage("📐 Modèles 3D") { BackColor = Theme.Bg };
        var modelPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };

        var modelBtnRow = new FlowLayoutPanel { Height = 40, Dock = DockStyle.Top, AutoSize = true };
        var loadModelBtn = MkBtn("📂 Ouvrir un modèle (.bbmodel / .json)", false, (_, _) =>
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Modèles Minecraft|*.bbmodel;*.json|Tous|*.*"
            };
            if (ofd.ShowDialog(FindForm()) == DialogResult.OK)
            {
                if (modelViewer3D.LoadModel(ofd.FileName))
                    UpdateStats($"Modèle chargé : {Path.GetFileName(ofd.FileName)}");
                else
                    UpdateStats("Impossible de charger le modèle.");
            }
        });
        modelBtnRow.Controls.Add(loadModelBtn);
        modelBtnRow.Controls.Add(new Label
        {
            Text = Lang.T("Glisser pour tourner, molette pour zoomer", "Drag to rotate, scroll to zoom"),
            ForeColor = Theme.TextDim, AutoSize = true, Margin = new Padding(12, 8, 0, 0)
        });

        modelViewer3D.Dock = DockStyle.Fill;
        modelViewer3D.BackColor = Color.FromArgb(18, 20, 24);
        modelPanel.Controls.Add(modelViewer3D);
        modelPanel.Controls.Add(modelBtnRow);
        modelPage.Controls.Add(modelPanel);

        viewTabs.TabPages.AddRange(new[] { editorPage, viewer3dPage, modelPage });

        root.Controls.Add(tools);
        root.Controls.Add(wePanel);
        root.Controls.Add(statsLabel);
        root.Controls.Add(viewTabs);
        Controls.Add(root);

        instanceBox.SelectedIndexChanged += (_, _) => LoadWorldList();
    }

    private static Button MkBtn(string text, bool primary, EventHandler onClick)
    {
        var b = new Button { Text = text, Width = 230, Height = 38 };
        Theme.Apply(b, primary);
        b.Click += onClick;
        return b;
    }

    // ---- Outil natif : Générateur de ville OSM ----
    private async void GenerateCityNative()
    {
        string? worldPath = _selectedWorld;
        if (worldPath == null)
        {
            MessageBox.Show(Lang.T("Sélectionne d'abord un monde.", "Select a world first."), "Team Launcher");
            return;
        }

        var result = PromptArnisLocation();
        if (result == null) return;

        var (name, bbox) = result.Value;

        try
        {
            UpdateStats(Lang.T($"Récupération des données OSM pour « {name} »...", $"Fetching OSM data for \"{name}\"..."));

            var progress = new Progress<string>(msg => UpdateStats(msg));
            var osmData = await CityGenerator.FetchOsmDataAsync(bbox, progress);

            UpdateStats(Lang.T($"Placement de {osmData.Entities.Count} entités dans le monde...", $"Placing {osmData.Entities.Count} entities in world..."));

            int placed = await CityGenerator.GenerateInWorldAsync(worldPath, osmData, baseY: 64, progress);

            UpdateStats(Lang.T($"Ville « {name} » générée ! {placed} blocs placés.", $"City \"{name}\" generated! {placed} blocks placed."));
            MessageBox.Show(
                Lang.T($"Ville « {name} » générée avec succès !\n{placed} blocs placés dans le monde.", $"City \"{name}\" generated successfully!\n{placed} blocks placed in the world."),
                "Team Launcher");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erreur :\n" + ex.Message, "Team Launcher");
            UpdateStats("Erreur génération ville.");
        }
    }

    private static (string Name, string BBox)? PromptArnisLocation()
    {
        string? result = null;
        string? bbox = null;
        using var dlg = new Form
        {
            Text = "Arnis — Générer une ville réelle",
            Size = new Size(500, 220),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, BackColor = Theme.Panel
        };
        var nameBox = new TextBox
        {
            Width = 400, Font = new Font("Segoe UI", 10f),
            Location = new Point(40, 16),
            PlaceholderText = "Nom de la ville (ex: Paris, Lyon, Marseille)"
        };
        Theme.ApplyInput(nameBox);
        var bboxBox = new TextBox
        {
            Width = 400, Font = new Font("Consolas", 10f),
            Location = new Point(40, 56),
            PlaceholderText = "Bounding box (min_lon,min_lat,max_lon,max_lat)"
        };
        Theme.ApplyInput(bboxBox);
        var hint = new Label
        {
            Text = "Ex: Paris = 2.35,48.86,2.35,48.86  |  Trouve les coordonnées sur openstreetmap.org",
            ForeColor = Theme.TextDim, AutoSize = true,
            Location = new Point(40, 90)
        };
        var ok = new Button { Text = "Générer", Width = 120, Height = 36, Location = new Point(180, 120) };
        Theme.Apply(ok, primary: true);
        ok.Click += (_, _) => { result = nameBox.Text.Trim(); bbox = bboxBox.Text.Trim(); dlg.Close(); };
        dlg.Controls.Add(nameBox);
        dlg.Controls.Add(bboxBox);
        dlg.Controls.Add(hint);
        dlg.Controls.Add(ok);
        dlg.ShowDialog();
        if (string.IsNullOrWhiteSpace(result) || string.IsNullOrWhiteSpace(bbox)) return null;
        return (result, bbox);
    }

    private static string FindJava()
    {
        string[] paths = [
            @"C:\Program Files\Java\jre*\bin\javaw.exe",
            @"C:\Program Files\Java\jdk*\bin\javaw.exe",
            @"C:\Program Files\Eclipse Adoptium\*\bin\javaw.exe",
            @"C:\Program Files\Microsoft\*\bin\javaw.exe"
        ];

        foreach (var pattern in paths)
        {
            var found = Directory.GetFiles(Path.GetDirectoryName(pattern)!, Path.GetFileName(pattern))
                .OrderByDescending(f => f).FirstOrDefault();
            if (found != null) return found;
        }

        try
        {
            var psi = new ProcessStartInfo("java", "-version") { RedirectStandardError = true, UseShellExecute = false };
            var p = Process.Start(psi);
            p?.WaitForExit(3000);
            return "java";
        }
        catch { }

        return null;
    }

    public void RefreshData()
    {
        instanceBox.Items.Clear();
        foreach (var i in DataStore.Settings.Instances)
            instanceBox.Items.Add(i.Name);
        if (instanceBox.Items.Count > 0 && instanceBox.SelectedIndex < 0)
            instanceBox.SelectedIndex = 0;
    }

    private InstanceInfo? SelectedInstance =>
        DataStore.Settings.Instances.ElementAtOrDefault(Math.Max(0, instanceBox.SelectedIndex));

    private string? WorldsDir
    {
        get
        {
            var inst = SelectedInstance;
            return inst == null ? null : Path.Combine(DataStore.InstancesRoot, inst.Id, "saves");
        }
    }

    private void LoadWorldList()
    {
        worldsPanel.SuspendLayout();
        worldsPanel.Controls.Clear();
        _selectedWorld = null;

        string? dir = WorldsDir;
        if (dir == null || !Directory.Exists(dir))
        {
            UpdateStats(Lang.T("Aucune instance sélectionnée.", "No instance selected."));
            worldsPanel.ResumeLayout();
            return;
        }

        var dirs = Directory.GetDirectories(dir);
        foreach (var w in dirs)
        {
            string name = Path.GetFileName(w);
            string worldPath = w;

            // Compter les régions .mca
            string regionDir = Path.Combine(worldPath, "region");
            int regionCount = Directory.Exists(regionDir)
                ? Directory.GetFiles(regionDir, "*.mca").Length : 0;

            var card = new Panel
            {
                Size = new Size(160, 72),
                Margin = new Padding(6),
                BackColor = Theme.Card,
                Cursor = Cursors.Hand,
                Tag = worldPath
            };

            var nameLabel = new Label
            {
                Text = name,
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(10, 8),
                AutoSize = true,
                MaximumSize = new Size(140, 0)
            };

            var infoLabel = new Label
            {
                Text = $"{regionCount} région(s) .mca",
                ForeColor = Theme.TextDim,
                Font = new Font("Segoe UI", 8f),
                Location = new Point(10, 30),
                AutoSize = true
            };

            card.Controls.Add(nameLabel);
            card.Controls.Add(infoLabel);

            card.Click += (_, _) => SelectWorld(worldPath, card);
            nameLabel.Click += (_, _) => SelectWorld(worldPath, card);
            infoLabel.Click += (_, _) => SelectWorld(worldPath, card);

            worldsPanel.Controls.Add(card);
        }

        worldsPanel.ResumeLayout();
        UpdateStats($"{dirs.Length} monde(s).");
    }

    private async void SelectWorld(string worldPath, Panel card)
    {
        _selectedWorld = worldPath;

        // Mettre à jour le style visuel
        foreach (Control c in worldsPanel.Controls)
        {
            if (c is Panel p)
                p.BackColor = ReferenceEquals(c, card) ? Theme.Accent : Theme.Card;
            // Mettre à jour la couleur du texte
            foreach (Control child in c.Controls)
                if (child is Label l)
                    l.ForeColor = ReferenceEquals(c, card) ? Color.White : Theme.Text;
        }

        await LoadWorldChunksAsync();
    }

    private async Task LoadWorldChunksAsync()
    {
        string? worldPath = _selectedWorld;
        if (worldPath == null)
        {
            canvas.LoadRegions(null);
            worldViewer3D.LoadWorldClear();
            UpdateStats("Sélectionne un monde.");
            return;
        }
        try
        {
            UpdateStats($"Chargement de « {Path.GetFileName(worldPath)} »...");
            Cursor = Cursors.WaitCursor;

            await Task.Run(() => canvas.LoadRegions(worldPath));
            await worldViewer3D.LoadWorldAsync(worldPath,
                step => BeginInvoke(() => UpdateStats(step)));

            string name = Path.GetFileName(worldPath);
            UpdateStats($"« {name} » chargé — {canvas.TotalChunks:N0} chunks trouvés. Glisse pour sélectionner.");
        }
        catch (Exception ex) { UpdateStats("Erreur : " + ex.Message); }
        finally { Cursor = Cursors.Default; }
    }

    private void UpdateStats(string? text = null)
    {
        if (text != null) { statsLabel.Text = text; return; }
        statsLabel.Text = $"Chunks : {canvas.TotalChunks:N0} | Sélectionnés : {canvas.SelectedCount:N0}";
    }

    private async void DeleteSelection()
    {
        if (canvas.SelectedCount == 0)
        {
            MessageBox.Show("Sélectionne d'abord des chunks (glisse la souris sur la carte).", "Team Launcher");
            return;
        }
        if (MessageBox.Show(
                $"Supprimer {canvas.SelectedCount:N0} chunk(s) du monde ?\n\n" +
                "IRREVERSIBLE. Fais une sauvegarde avant (bouton 💾).",
                "Team Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        btnWait(true);
        try
        {
            await Task.Run(() => canvas.DeleteSelected());
            UpdateStats($"Suppression terminée. Chunks restants : {canvas.TotalChunks:N0}");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
        finally { btnWait(false); }
    }

    private void BackupWorld()
    {
        string? worldPath = _selectedWorld;
        var si = SelectedInstance;
        if (worldPath == null || si == null) return;
        try
        {
            string name = Path.GetFileName(worldPath);
            string backups = Path.Combine(DataStore.InstancesRoot, si.Id, "backups");
            Directory.CreateDirectory(backups);
            string zip = Path.Combine(backups,
                $"monde-{name}-{DateTime.Now:yyyy-MM-dd_HH-mm}.zip");
            ZipFile.CreateFromDirectory(worldPath, zip, CompressionLevel.Optimal, false);
            UpdateStats("Sauvegarde : " + zip);
            MessageBox.Show("Monde sauvegardé :\n" + zip, "Team Launcher");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
    }

    private void btnWait(bool waiting)
    {
        Cursor = waiting ? Cursors.WaitCursor : Cursors.Default;
    }
}

internal static class InstExtensions
{
    public static string IdSafe(this InstanceInfo i) => i.Id;
}
