using System.Drawing.Drawing2D;
using System.IO.Compression;

namespace TeamLauncher;

/// <summary>
/// Éditeur de mondes façon MCA Selector :
/// rendu des chunks des régions .mca sur une grille, sélection au lasso,
/// suppression physique des chunks sélectionnés (réécriture propre des .mca).
/// </summary>
public class MapEditorPage : UserControl, IRefreshable
{
    private readonly ComboBox instanceBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly FlowLayoutPanel worldsPanel = new();
    private readonly EditorCanvas canvas = new();
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
            Text = "Sélectionne une instance, puis un monde. Glisse la souris pour sélectionner des chunks,\n" +
                   "puis supprime-les (terres abandonnées, chunks corrompus, reset de zones...).",
            ForeColor = Theme.TextDim, AutoSize = true
        });

        // ---- Instance selector ----
        var row1 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
        var instLabel = new Label { Text = "Instance :", ForeColor = Theme.TextDim, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        instanceBox.Width = 280; instanceBox.Font = new Font("Segoe UI", 10f);
        row1.Controls.Add(instLabel);
        row1.Controls.Add(instanceBox);
        root.Controls.Add(row1);

        // ---- World selector (cards) ----
        root.Controls.Add(new Label
        {
            Text = "Monde :", ForeColor = Theme.TextDim,
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
        tools.Controls.Add(MkBtn("⟳ Actualiser", false, (_, _) => { LoadWorldList(); LoadWorldChunks(); }));

        statsLabel.ForeColor = Theme.Text;
        statsLabel.Font = new Font("Consolas", 9.5f);
        statsLabel.AutoSize = true;
        statsLabel.Margin = new Padding(4, 2, 0, 2);

        // canevas
        canvas.Dock = DockStyle.Fill; canvas.Height = 430; canvas.Width = 920;
        canvas.BackColor = Color.FromArgb(18, 20, 24);
        canvas.OnStatsChanged += () => UpdateStats();

        root.Controls.Add(tools);
        root.Controls.Add(statsLabel);
        root.Controls.Add(canvas);
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
            UpdateStats("Aucune instance sélectionnée.");
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

    private void SelectWorld(string worldPath, Panel card)
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

        LoadWorldChunks();
    }

    private void LoadWorldChunks()
    {
        string? worldPath = _selectedWorld;
        if (worldPath == null)
        {
            canvas.LoadRegions(null);
            UpdateStats("Sélectionne un monde.");
            return;
        }
        try
        {
            canvas.LoadRegions(worldPath);
            string name = Path.GetFileName(worldPath);
            UpdateStats($"« {name} » chargé — {canvas.TotalChunks:N0} chunks trouvés. Glisse pour sélectionner.");
        }
        catch (Exception ex) { UpdateStats("Erreur : " + ex.Message); }
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
