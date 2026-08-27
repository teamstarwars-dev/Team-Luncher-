using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace TeamLauncher;

/// <summary>
/// Page détaillée d'une instance style CurseForge :
/// bannière avec infos complètes, onglets Description / Mods / Mondes / Shaders / RP / Screenshots.
/// </summary>
public class InstanceDetailPage : UserControl, IRefreshable
{
    private InstanceInfo inst = new();
    private readonly Label nameLabel = new();
    private readonly Label descLabel = new();
    private readonly Label metaLabel = new();

    private readonly FlowLayoutPanel tabRow = new();
    private readonly Dictionary<string, Button> tabButtons = new();
    private readonly ListView itemsList = new();
    private readonly RichTextBox descBox = new();
    private readonly FlowLayoutPanel actionsRow = new();
    private string currentTab = "Description";

    public InstanceDetailPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        // ---- bannière CurseForge-style ----
        var head = new Panel { Dock = DockStyle.Top, Height = 140, BackColor = Theme.Card };

        // Image de l'instance (gauche)
        var imgBox = new PictureBox
        {
            Size = new Size(100, 100),
            Location = new Point(20, 20),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = ControlPaint.Dark(Theme.Card, 0.05f)
        };
        Theme.Round(imgBox, 6);
        head.Controls.Add(imgBox);

        // Textes (centre)
        var texts = new Panel { Location = new Point(130, 12), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

        nameLabel.ForeColor = Theme.Text;
        nameLabel.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
        nameLabel.Location = new Point(0, 0);
        nameLabel.AutoSize = true;
        nameLabel.MaximumSize = new Size(600, 0);

        descLabel.ForeColor = Theme.TextDim;
        descLabel.Font = new Font("Segoe UI", 10f);
        descLabel.Location = new Point(0, 30);
        descLabel.AutoSize = true;
        descLabel.MaximumSize = new Size(600, 0);

        metaLabel.ForeColor = Theme.TextDim;
        metaLabel.Font = new Font("Segoe UI", 9f);
        metaLabel.Location = new Point(0, 56);
        metaLabel.AutoSize = true;

        var notesLabel = new Label
        {
            ForeColor = Theme.AccentDim,
            Font = new Font("Segoe UI", 9f, FontStyle.Italic),
            Location = new Point(0, 76),
            AutoSize = true,
            MaximumSize = new Size(600, 0)
        };

        texts.Controls.Add(nameLabel);
        texts.Controls.Add(descLabel);
        texts.Controls.Add(metaLabel);
        texts.Controls.Add(notesLabel);
        texts.Resize += (_, _) =>
        {
            int w = texts.Width;
            nameLabel.MaximumSize = new Size(w, 0);
            descLabel.MaximumSize = new Size(w, 0);
            notesLabel.MaximumSize = new Size(w, 0);
        };
        head.Controls.Add(texts);

        // Boutons d'action (droite)
        var playBtn = new Button { Text = "▶  Jouer", Size = new Size(160, 40), Location = new Point(0, 8) };
        Theme.Apply(playBtn, primary: true);
        playBtn.Click += (_, _) => GameLauncher.Play(inst);

        var editBtn = new Button { Text = "✎ Modifier", Size = new Size(160, 34), Location = new Point(0, 54) };
        Theme.Apply(editBtn);
        editBtn.Click += (_, _) =>
        {
            using var dlg = new InstanceEditDialog(inst);
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK) RefreshData();
        };

        var folderBtn = new Button { Text = "📂 Dossier", Size = new Size(160, 34), Location = new Point(0, 94) };
        Theme.Apply(folderBtn);
        folderBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Path.Combine(DataStore.InstancesRoot, inst.Id)) { UseShellExecute = true }); }
            catch { }
        };

        head.Resize += (_, _) =>
        {
            int rx = head.Width - 180;
            playBtn.Left = rx;
            editBtn.Left = rx;
            folderBtn.Left = rx;
            int maxText = rx - 150;
            if (maxText > 50)
            {
                nameLabel.MaximumSize = new Size(maxText, 0);
                descLabel.MaximumSize = new Size(maxText, 0);
                notesLabel.MaximumSize = new Size(maxText, 0);
            }
        };
        head.Controls.Add(playBtn);
        head.Controls.Add(editBtn);
        head.Controls.Add(folderBtn);

        // ---- barre d'onglets (style CurseForge) ----
        tabRow.Dock = DockStyle.Top;
        tabRow.Height = 44;
        tabRow.WrapContents = false;
        tabRow.Padding = new Padding(16, 0, 0, 0);
        tabRow.BackColor = Theme.Panel;
        AddTab("Description");
        AddTab("🧩 Mods");
        AddTab("🌍 Mondes");
        AddTab("🎨 Shaders");
        AddTab("🖼️ Resource Packs");
        AddTab("📷 Screenshots");

        // ---- zone de contenu ----
        descBox.Dock = DockStyle.Fill;
        descBox.BackColor = Theme.Bg;
        descBox.ForeColor = Theme.Text;
        descBox.Font = new Font("Segoe UI", 10f);
        descBox.BorderStyle = BorderStyle.None;
        descBox.ReadOnly = true;
        descBox.Visible = false;

        itemsList.View = View.Details;
        itemsList.FullRowSelect = true;
        itemsList.Dock = DockStyle.Fill;
        itemsList.BackColor = Theme.Bg;
        itemsList.ForeColor = Theme.Text;
        itemsList.BorderStyle = BorderStyle.None;
        itemsList.Font = new Font("Segoe UI", 9.5f);
        itemsList.DoubleClick += OpenSelectedItem;

        actionsRow.Dock = DockStyle.Bottom;
        actionsRow.Height = 46;
        actionsRow.WrapContents = false;
        actionsRow.Padding = new Padding(16, 6, 0, 4);

        Controls.Add(itemsList);
        Controls.Add(descBox);
        Controls.Add(actionsRow);
        Controls.Add(tabRow);
        Controls.Add(head);

        string initialTab = AppEvents.PendingDetailTab ?? "Description";
        if (!tabButtons.ContainsKey(initialTab)) initialTab = "Description";
        SetTab(initialTab);
    }

    private void AddTab(string name)
    {
        var b = new Button
        {
            Text = name,
            AutoSize = true,
            Height = 40,
            Padding = new Padding(12, 0, 12, 0),
            Font = new Font("Segoe UI", 9.5f),
            Tag = name
        };
        StyleTab(b, false);
        b.Click += (_, _) => SetTab(name);
        tabButtons[name] = b;
        tabRow.Controls.Add(b);
    }

    private void SetTab(string name)
    {
        currentTab = name;
        foreach (var kv in tabButtons)
            StyleTab(kv.Value, kv.Key == name);

        bool isDesc = name == "Description";
        descBox.Visible = isDesc;
        itemsList.Visible = !isDesc;

        RefreshData();
    }

    private void StyleTab(Button b, bool active)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = active ? Theme.Bg : Color.Transparent;
        b.ForeColor = active ? Theme.Accent : Theme.TextDim;
        b.Cursor = Cursors.Hand;
        if (active)
        {
            b.FlatAppearance.BorderColor = Theme.Accent;
            b.Height = 40;
        }
    }

    public void RefreshData()
    {
        var pending = AppEvents.PendingDetailId;
        AppEvents.PendingDetailId = null;

        var pendingTab = AppEvents.PendingDetailTab;
        AppEvents.PendingDetailTab = null;

        inst = DataStore.Settings.Instances.FirstOrDefault(i => i.Id == pending)
               ?? DataStore.Settings.Instances.FirstOrDefault()
               ?? new InstanceInfo();

        // Bannière
        nameLabel.Text = inst.Name;
        descLabel.Text = string.IsNullOrWhiteSpace(inst.Description)
            ? "" : inst.Description;

        string launches = $"{inst.Launches} lancement(s)";
        string playTime = inst.PlaySeconds > 0 ? $" • {FormatTime(inst.PlaySeconds)} jouées" : "";
        string lastPlayed = inst.LastPlayed > DateTime.MinValue
            ? $" • Vu le {inst.LastPlayed:dd/MM/yy}" : "";
        metaLabel.Text = $"{inst.Loader}  •  Minecraft {inst.McVersion}  •  {launches}{playTime}{lastPlayed}";

        var notesLabel = descLabel.Parent?.Controls.OfType<Label>()
            .FirstOrDefault(l => l.Font.Style == FontStyle.Italic);
        if (notesLabel != null)
            notesLabel.Text = string.IsNullOrWhiteSpace(inst.Notes) ? "" : "📝 " + inst.Notes;

        // Image
        if (descLabel.Parent?.Parent is Panel headPanel)
        {
            var imgBox = headPanel.Controls.OfType<PictureBox>().FirstOrDefault();
            if (imgBox != null)
            {
                imgBox.Image?.Dispose();
                imgBox.Image = null;
                if (!string.IsNullOrWhiteSpace(inst.ImagePath) && File.Exists(inst.ImagePath))
                {
                    try { imgBox.Image = Image.FromFile(inst.ImagePath); } catch { }
                }
            }
        }

        // Onglet
        if (pendingTab != null && tabButtons.ContainsKey(pendingTab))
            SetTab(pendingTab);
        else
            LoadTabContent();

        BuildActionButtons();
    }

    private void LoadTabContent()
    {
        switch (currentTab)
        {
            case "🌍 Mondes": LoadWorlds(); break;
            case "🎨 Shaders": LoadFiles("shaderpacks"); break;
            case "🖼️ Resource Packs": LoadFiles("resourcepacks"); break;
            case "📷 Screenshots": LoadScreenshots(); break;
            case "🧩 Mods": LoadMods(); break;
            default: LoadDescription(); break;
        }
    }

    private void LoadDescription()
    {
        descBox.Text = string.IsNullOrWhiteSpace(inst.Description)
            ? "Pas de description pour cette instance."
            : inst.Description;

        if (!string.IsNullOrWhiteSpace(inst.Notes))
            descBox.Text += "\n\n📝 Notes :\n" + inst.Notes;

        string instDir = Path.Combine(DataStore.InstancesRoot, inst.Id);
        int mods = CountFiles(instDir, "mods", "*.jar");
        int maps = CountFolders(instDir, "saves");
        int shaders = CountFiles(instDir, "shaderpacks", "*.zip");
        int rp = CountFiles(instDir, "resourcepacks", "*.zip");

        descBox.Text += $"\n\n---\n\n" +
            $"📦 Contenu de l'instance :\n" +
            $"   {mods} mod(s)\n" +
            $"   {maps} monde(s)\n" +
            $"   {shaders} shader(s)\n" +
            $"   {rp} resource pack(s)\n\n" +
            $"⚙️ Configuration :\n" +
            $"   Loader : {inst.Loader}\n" +
            $"   Version : Minecraft {inst.McVersion}\n" +
            $"   RAM max : {(inst.MaxRamGb > 0 ? $"{inst.MaxRamGb} Go" : "globale")}\n" +
            $"   Lancements : {inst.Launches}\n" +
            $"   Temps de jeu : {FormatTime(inst.PlaySeconds)}";
    }

    private static int CountFiles(string instDir, string subDir, string pattern)
    {
        try { string p = Path.Combine(instDir, subDir); return Directory.Exists(p) ? Directory.GetFiles(p, pattern).Length : 0; }
        catch { return 0; }
    }
    private static int CountFolders(string instDir, string subDir)
    {
        try { string p = Path.Combine(instDir, subDir); return Directory.Exists(p) ? Directory.GetDirectories(p).Length : 0; }
        catch { return 0; }
    }

    private static string FormatTime(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h{ts.Minutes:D2}" : $"{ts.Minutes}min";
    }

    // ---------------- chargement des onglets ----------------

    private void LoadScreenshots()
    {
        itemsList.Columns.Clear();
        itemsList.Items.Clear();
        itemsList.Columns.Add("Screenshot", 500);
        itemsList.Columns.Add("Date", 150);

        string dir = Path.Combine(DataStore.InstancesRoot, inst.Id, "screenshots");
        if (!Directory.Exists(dir))
        {
            itemsList.Items.Add(new ListViewItem(new[] { "Aucun screenshot. En jeu : appuie sur F2.", "" }));
            return;
        }
        foreach (var f in Directory.GetFiles(dir, "*.png").OrderByDescending(f => f))
        {
            var fi = new FileInfo(f);
            itemsList.Items.Add(new ListViewItem(new[] { fi.Name, fi.LastWriteTime.ToString("dd/MM/yyyy HH:mm") }) { Tag = f });
        }
        if (itemsList.Items.Count == 0)
            itemsList.Items.Add(new ListViewItem(new[] { "Aucun screenshot pour l'instant (F2 en jeu).", "" }));
    }

    private void LoadMods()
    {
        itemsList.Columns.Clear();
        itemsList.Items.Clear();
        itemsList.Columns.Add("Mod", 420);
        itemsList.Columns.Add("État", 90);
        itemsList.Columns.Add("Taille", 110);

        string dir = Path.Combine(DataStore.InstancesRoot, inst.Id, "mods");
        if (!Directory.Exists(dir))
        {
            itemsList.Items.Add(new ListViewItem(new[] { "Aucun dossier mods.", "", "" }));
            return;
        }
        foreach (var f in Directory.GetFiles(dir, "*.jar*").OrderBy(f => f))
        {
            var fi = new FileInfo(f);
            bool disabled = f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            string baseName = disabled ? fi.Name[..^".disabled".Length] : fi.Name;
            itemsList.Items.Add(new ListViewItem(new[]
            {
                baseName,
                disabled ? "⏸ désactivé" : "✔ actif",
                $"{fi.Length / 1024.0 / 1024.0:0.#} Mo"
            }) { Tag = f, ForeColor = disabled ? Theme.TextDim : Theme.Text });
        }
        if (itemsList.Items.Count == 0)
            itemsList.Items.Add(new ListViewItem(new[] { "Aucun mod installé.", "", "" }));
    }

    private void LoadFiles(string subFolder)
    {
        itemsList.Columns.Clear();
        itemsList.Items.Clear();
        itemsList.Columns.Add("Fichier", 420);
        itemsList.Columns.Add("Taille", 110);
        itemsList.Columns.Add("Modifié", 140);

        string dir = Path.Combine(DataStore.InstancesRoot, inst.Id, subFolder);
        if (!Directory.Exists(dir))
        {
            itemsList.Items.Add(new ListViewItem(new[] { $"Aucun dossier {subFolder}.", "", "" }));
            return;
        }
        foreach (var f in Directory.GetFiles(dir).OrderBy(f => f))
        {
            var fi = new FileInfo(f);
            if (fi.Length == 0 && fi.Extension != ".txt") continue;
            itemsList.Items.Add(new ListViewItem(new[]
            {
                fi.Name,
                $"{fi.Length / 1024.0:0.#} Ko",
                fi.LastWriteTime.ToString("dd/MM/yyyy HH:mm")
            }) { Tag = f });
        }
        if (itemsList.Items.Count == 0)
            itemsList.Items.Add(new ListViewItem(new[] { "Dossier vide.", "", "" }));
    }

    private void LoadWorlds()
    {
        itemsList.Columns.Clear();
        itemsList.Items.Clear();
        itemsList.Columns.Add("Monde", 300);
        itemsList.Columns.Add("Dernière partie", 150);
        itemsList.Columns.Add("État régions", 160);
        itemsList.Columns.Add("Taille", 90);

        string savesDir = Path.Combine(DataStore.InstancesRoot, inst.Id, "saves");
        if (!Directory.Exists(savesDir))
        {
            itemsList.Items.Add(new ListViewItem(new[] { "Aucun monde.", "", "", "" }));
            return;
        }
        foreach (var w in Directory.GetDirectories(savesDir))
        {
            var (name, lastPlayed) = WorldTools.ReadLevelDat(w);
            long size = Directory.GetFiles(w, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
            itemsList.Items.Add(new ListViewItem(new[]
            {
                name ?? Path.GetFileName(w),
                lastPlayed?.ToString("dd/MM/yyyy HH:mm") ?? "?",
                WorldTools.CountEmptyRegions(w) > 0 ? "régions à nettoyer" : "ok",
                size / 1024.0 / 1024.0 > 1 ? $"{size / 1024.0 / 1024.0:0.#} Mo" : $"{size / 1024.0:0.#} Ko"
            }) { Tag = w });
        }
        if (itemsList.Items.Count == 0)
            itemsList.Items.Add(new ListViewItem(new[] { "Aucun monde sauvegardé.", "", "", "" }));
    }

    // ---------------- actions contextuelles ----------------

    private void BuildActionButtons()
    {
        actionsRow.Controls.Clear();

        if (currentTab == "🧩 Mods")
        {
            var addModBtn = MkActionBtn("+ Ajouter un mod");
            addModBtn.Click += (_, _) =>
            {
                using var ofd = new OpenFileDialog { Filter = "Mod Minecraft|*.jar" };
                if (ofd.ShowDialog(FindForm()) != DialogResult.OK) return;
                string dest = Path.Combine(DataStore.InstancesRoot, inst.Id, "mods", Path.GetFileName(ofd.FileName));
                File.Copy(ofd.FileName, dest, overwrite: true);
                RefreshData();
            };
            actionsRow.Controls.Add(addModBtn);

            var openModsBtn = MkActionBtn("📂 Ouvrir le dossier mods");
            openModsBtn.Click += (_, _) =>
            {
                string dir = Path.Combine(DataStore.InstancesRoot, inst.Id, "mods");
                Directory.CreateDirectory(dir);
                try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); } catch { }
            };
            actionsRow.Controls.Add(openModsBtn);
        }
        else if (currentTab == "🌍 Mondes")
        {
            var openSavesBtn = MkActionBtn("📂 Ouvrir le dossier saves");
            openSavesBtn.Click += (_, _) =>
            {
                string dir = Path.Combine(DataStore.InstancesRoot, inst.Id, "saves");
                Directory.CreateDirectory(dir);
                try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); } catch { }
            };
            actionsRow.Controls.Add(openSavesBtn);
        }
        else if (currentTab == "📷 Screenshots")
        {
            var openBtn = MkActionBtn("📂 Ouvrir le dossier screenshots");
            openBtn.Click += (_, _) =>
            {
                string dir = Path.Combine(DataStore.InstancesRoot, inst.Id, "screenshots");
                Directory.CreateDirectory(dir);
                try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); } catch { }
            };
            actionsRow.Controls.Add(openBtn);
        }
    }

    private static Button MkActionBtn(string text)
    {
        var b = new Button { Text = text, Height = 32, AutoSize = true, Padding = new Padding(10, 0, 10, 0) };
        Theme.Apply(b);
        b.Font = new Font("Segoe UI", 8.5f);
        return b;
    }

    private void OpenSelectedItem(object? sender, EventArgs e)
    {
        if (itemsList.SelectedItems.Count == 0) return;
        string? path = itemsList.SelectedItems[0].Tag as string;
        if (path == null) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }
}
