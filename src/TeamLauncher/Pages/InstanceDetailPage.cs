using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
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
    private readonly FlowLayoutPanel screenshotsPanel = new();
    private readonly Panel logsPanel = new();
    private readonly RichTextBox logBox = new();
    private readonly TextBox logSearchBox = new();
    private bool logAutoScroll = true;
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
        AddTab("📝 Configs");
        AddTab("📷 Screenshots");
        AddTab("📝 Journaux");

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

        // ---- panneau screenshots (grille de vignettes) ----
        screenshotsPanel.Dock = DockStyle.Fill;
        screenshotsPanel.BackColor = Theme.Bg;
        screenshotsPanel.AutoScroll = true;
        screenshotsPanel.Padding = new Padding(16);
        screenshotsPanel.Visible = false;

        // ---- panneau journaux (logs) ----
        logsPanel.Dock = DockStyle.Fill;
        logsPanel.BackColor = Theme.Bg;
        logsPanel.Visible = false;

        var logTopBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 4),
            BackColor = Theme.Bg
        };

        logSearchBox.PlaceholderText = "🔍  Search log...";
        logSearchBox.Size = new Size(220, 30);
        logSearchBox.Font = new Font("Segoe UI", 9.5f);
        logSearchBox.BackColor = Theme.Panel;
        logSearchBox.ForeColor = Theme.Text;
        logSearchBox.BorderStyle = BorderStyle.FixedSingle;
        logSearchBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { SearchLog(); e.SuppressKeyPress = true; } };
        logTopBar.Controls.Add(logSearchBox);

        var logSearchBtn = new Button { Text = "🔍", Size = new Size(36, 30), Font = new Font("Segoe UI", 10f) };
        Theme.Apply(logSearchBtn);
        logSearchBtn.Click += (_, _) => SearchLog();
        logTopBar.Controls.Add(logSearchBtn);

        var logHighlightBtn = new Button { Text = "🧹", Size = new Size(36, 30), Font = new Font("Segoe UI", 10f) };
        Theme.Apply(logHighlightBtn);
        logHighlightBtn.Click += (_, _) => { logSearchBox.Clear(); LoadLogs(); };
        logTopBar.Controls.Add(logHighlightBtn);

        var logAutoScrollBtn = new Button { Text = "⬇", Size = new Size(36, 30), Font = new Font("Segoe UI", 10f) };
        Theme.Apply(logAutoScrollBtn);
        logAutoScrollBtn.Click += (_, _) =>
        {
            logAutoScroll = !logAutoScroll;
            logAutoScrollBtn.ForeColor = logAutoScroll ? Theme.Accent : Theme.TextDim;
            if (logAutoScroll) logBox.SelectionStart = logBox.TextLength;
        };
        logTopBar.Controls.Add(logAutoScrollBtn);

        logBox.Dock = DockStyle.Fill;
        logBox.BackColor = Color.FromArgb(30, 30, 30);
        logBox.ForeColor = Color.FromArgb(200, 200, 200);
        logBox.Font = new Font("Consolas", 9f);
        logBox.BorderStyle = BorderStyle.None;
        logBox.ReadOnly = true;
        logBox.WordWrap = false;
        logBox.DetectUrls = false;
        logBox.ShortcutsEnabled = false;

        logsPanel.Controls.Add(logBox);
        logsPanel.Controls.Add(logTopBar);

        actionsRow.Dock = DockStyle.Bottom;
        actionsRow.Height = 46;
        actionsRow.WrapContents = false;
        actionsRow.Padding = new Padding(16, 6, 0, 4);

        Controls.Add(screenshotsPanel);
        Controls.Add(logsPanel);
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
        bool isScreenshots = name == "📷 Screenshots";
        bool isLogs = name == "📝 Journaux";
        descBox.Visible = isDesc;
        itemsList.Visible = !isDesc && !isScreenshots && !isLogs;
        screenshotsPanel.Visible = isScreenshots;
        logsPanel.Visible = isLogs;

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
            case "📝 Configs": LoadFiles("config"); break;
            case "📷 Screenshots": LoadScreenshots(); break;
            case "📝 Journaux": LoadLogs(); break;
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
        screenshotsPanel.SuspendLayout();
        screenshotsPanel.Controls.Clear();

        // Bouton "Ouvrir le dossier"
        var openFolderBtn = new Button
        {
            Text = "📂  Ouvrir le dossier",
            AutoSize = true,
            Height = 34,
            Padding = new Padding(10, 0, 10, 0),
            Font = new Font("Segoe UI", 10f),
            Dock = DockStyle.Top,
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text,
            BackColor = Theme.Bg,
            Cursor = Cursors.Hand
        };
        openFolderBtn.FlatAppearance.BorderSize = 0;
        openFolderBtn.Click += (_, _) => OpenFolder("screenshots");
        screenshotsPanel.Controls.Add(openFolderBtn);

        string dir = Path.Combine(DataStore.InstancesRoot, inst.Id, "screenshots");
        if (!Directory.Exists(dir))
        {
            var lbl = new Label
            {
                Text = "Aucun screenshot.\nEn jeu : appuie sur F2.",
                ForeColor = Theme.TextDim,
                Font = new Font("Segoe UI", 10f),
                AutoSize = true,
                Padding = new Padding(0, 20, 0, 0)
            };
            screenshotsPanel.Controls.Add(lbl);
            screenshotsPanel.ResumeLayout();
            return;
        }

        var files = Directory.GetFiles(dir, "*.png")
            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
            .ToList();

        if (files.Count == 0)
        {
            var lbl = new Label
            {
                Text = "Aucun screenshot pour l'instant (F2 en jeu).",
                ForeColor = Theme.TextDim,
                Font = new Font("Segoe UI", 10f),
                AutoSize = true,
                Padding = new Padding(0, 20, 0, 0)
            };
            screenshotsPanel.Controls.Add(lbl);
            screenshotsPanel.ResumeLayout();
            return;
        }

        int thumbW = 250, thumbH = 150, gap = 12;

        foreach (var f in files)
        {
            var fi = new FileInfo(f);
            var card = new Panel
            {
                Size = new Size(thumbW, thumbH + 4),
                Margin = new Padding(gap / 2),
                BackColor = Theme.Card,
                Cursor = Cursors.Hand,
                Tag = f
            };
            Theme.Round(card, 6);

            var pic = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Theme.Card,
                Tag = f
            };
            Theme.Round(pic, 6);

            // Chargement asynchrone du thumbnail
            string path = f;
            _ = LoadThumbAsync(pic, path);

            pic.Click += (_, _) => OpenScreenshot(path);
            card.Click += (_, _) => OpenScreenshot(path);
            pic.MouseEnter += (_, _) => { pic.BackColor = ControlPaint.Light(Theme.Card, 0.15f); card.BackColor = pic.BackColor; };
            pic.MouseLeave += (_, _) => { pic.BackColor = Theme.Card; card.BackColor = Theme.Card; };

            card.Controls.Add(pic);
            screenshotsPanel.Controls.Add(card);
        }

        screenshotsPanel.ResumeLayout();
    }

    private static async Task LoadThumbAsync(PictureBox pic, string path)
    {
        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var img = Image.FromStream(fs);
            // Redimensionner en mémoire pour le thumbnail
            var thumb = new Bitmap(250, 150);
            using (var g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.DrawImage(img, 0, 0, 250, 150);
            }
            img.Dispose();
            if (pic.IsDisposed) { thumb.Dispose(); return; }
            pic.Image = thumb;
        }
        catch { }
    }

    private static void OpenScreenshot(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    // ---------------- journaux (logs) ----------------

    private void LoadLogs()
    {
        logBox.SuspendRendering();
        logBox.Clear();

        // Cherche game-log.txt puis logs/latest.log
        string instDir = Path.Combine(DataStore.InstancesRoot, inst.Id);
        string? logFile = null;

        string gameLog = Path.Combine(instDir, "game-log.txt");
        if (File.Exists(gameLog)) logFile = gameLog;

        string latestLog = Path.Combine(instDir, "logs", "latest.log");
        if (logFile == null && File.Exists(latestLog)) logFile = latestLog;

        if (logFile == null)
        {
            AppendText("Aucun journal.\nLance Minecraft pour générer des logs.", Color.FromArgb(150, 150, 150));
            logBox.ResumeRendering();
            return;
        }

        try
        {
            var lines = File.ReadAllLines(logFile);
            foreach (var line in lines)
                AppendLogLine(line);
        }
        catch (Exception ex)
        {
            AppendText($"Erreur de lecture : {ex.Message}", Color.Red);
        }

        if (logAutoScroll)
            logBox.SelectionStart = logBox.TextLength;

        logBox.ResumeRendering();
    }

    private void AppendLogLine(string line)
    {
        Color color;
        if (line.Contains("/ERROR") || line.Contains("[ERROR]"))
            color = Color.FromArgb(255, 80, 80);
        else if (line.Contains("/WARN") || line.Contains("[WARN]"))
            color = Color.FromArgb(255, 200, 60);
        else if (line.Contains("/DEBUG") || line.Contains("[DEBUG]"))
            color = Color.FromArgb(120, 180, 255);
        else if (line.Contains("/INFO") || line.Contains("[INFO]"))
            color = Color.FromArgb(200, 200, 200);
        else
            color = Color.FromArgb(160, 160, 160);

        AppendText(line + "\n", color);
    }

    private void SearchLog()
    {
        string query = logSearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) { LoadLogs(); return; }

        logBox.SuspendRendering();
        logBox.Clear();

        string instDir = Path.Combine(DataStore.InstancesRoot, inst.Id);
        string? logFile = null;

        string gameLog = Path.Combine(instDir, "game-log.txt");
        if (File.Exists(gameLog)) logFile = gameLog;

        string latestLog = Path.Combine(instDir, "logs", "latest.log");
        if (logFile == null && File.Exists(latestLog)) logFile = latestLog;

        if (logFile == null)
        {
            AppendText("Aucun journal.", Color.FromArgb(150, 150, 150));
            logBox.ResumeRendering();
            return;
        }

        try
        {
            int count = 0;
            foreach (var line in File.ReadLines(logFile))
            {
                if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    AppendLogLine(line);
                    count++;
                }
            }
            AppendText($"\n── {count} résultat(s) pour « {query} » ──\n", Color.FromArgb(100, 200, 255));
        }
        catch (Exception ex)
        {
            AppendText($"Erreur : {ex.Message}", Color.Red);
        }

        logBox.SelectionStart = logBox.TextLength;
        logBox.ResumeRendering();
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
        itemsList.Columns.Add("Monde", 240);
        itemsList.Columns.Add("Dernière partie", 130);
        itemsList.Columns.Add("État régions", 130);
        itemsList.Columns.Add("Origine", 110);
        itemsList.Columns.Add("Taille", 70);

        string savesDir = Path.Combine(DataStore.InstancesRoot, inst.Id, "saves");
        if (!Directory.Exists(savesDir))
        {
            itemsList.Items.Add(new ListViewItem(new[] { "Aucun monde.", "", "", "", "" }));
            return;
        }

        // Récupère le snapshot CurseForge pour annoter la provenance
        string cfRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "curseforge", "minecraft", "Instances", inst.Name, "minecraft", "saves");
        var cfWorlds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(cfRoot))
            foreach (var w in Directory.GetDirectories(cfRoot))
                if (File.Exists(Path.Combine(w, "level.dat")))
                    cfWorlds.Add(Path.GetFileName(w));

        foreach (var w in Directory.GetDirectories(savesDir))
        {
            var (name, lastPlayed) = WorldTools.ReadLevelDat(w);
            long size = Directory.GetFiles(w, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
            string worldName = Path.GetFileName(w);
            bool inCf = cfWorlds.Contains(worldName);

            itemsList.Items.Add(new ListViewItem(new[]
            {
                name ?? worldName,
                lastPlayed?.ToString("dd/MM/yyyy HH:mm") ?? "?",
                WorldTools.CountEmptyRegions(w) > 0 ? "régions à nettoyer" : "ok",
                inCf ? "CurseForge + Launcher" : "Launcher seul",
                size / 1024.0 / 1024.0 > 1 ? $"{size / 1024.0 / 1024.0:0.#} Mo" : $"{size / 1024.0:0.#} Ko"
            })
            {
                Tag = w,
                ForeColor = inCf ? Theme.Text : Theme.TextDim
            });
        }
        if (itemsList.Items.Count == 0)
            itemsList.Items.Add(new ListViewItem(new[] { "Aucun monde sauvegardé.", "", "", "", "" }));
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

            var worldEditBtn = MkActionBtn("🔧 Installer WorldEdit");
            worldEditBtn.ForeColor = Theme.Accent;
            worldEditBtn.Click += async (_, _) => await InstallWorldEditAsync();
            actionsRow.Controls.Add(worldEditBtn);

            var openModsBtn = MkActionBtn("📂 Ouvrir le dossier mods");
            openModsBtn.Click += (_, _) => OpenFolder("mods");
            actionsRow.Controls.Add(openModsBtn);
        }
        else if (currentTab == "🌍 Mondes")
        {
            var importCfBtn = MkActionBtn("⇆ Importer depuis CurseForge");
            importCfBtn.ForeColor = Theme.Accent;
            importCfBtn.Click += (_, _) =>
            {
                using var dlg = new WorldImportDialog(inst);
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK) RefreshData();
            };
            actionsRow.Controls.Add(importCfBtn);

            var openSavesBtn = MkActionBtn("📂 Ouvrir le dossier saves");
            openSavesBtn.Click += (_, _) => OpenFolder("saves");
            actionsRow.Controls.Add(openSavesBtn);
        }
        else if (currentTab == "🎨 Shaders")
        {
            var openShadersBtn = MkActionBtn("📂 Ouvrir le dossier shaders");
            openShadersBtn.Click += (_, _) => OpenFolder("shaderpacks");
            actionsRow.Controls.Add(openShadersBtn);
        }
        else if (currentTab == "🖼️ Resource Packs")
        {
            var openRpBtn = MkActionBtn("📂 Ouvrir le dossier resource packs");
            openRpBtn.Click += (_, _) => OpenFolder("resourcepacks");
            actionsRow.Controls.Add(openRpBtn);
        }
        else if (currentTab == "📝 Configs")
        {
            var openCfgBtn = MkActionBtn("📂 Ouvrir le dossier config");
            openCfgBtn.Click += (_, _) => OpenFolder("config");
            actionsRow.Controls.Add(openCfgBtn);
        }

        // Bouton "Ouvrir le dossier instance" toujours présent
        var openAllBtn = MkActionBtn("📂 Dossier instance");
        openAllBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Path.Combine(DataStore.InstancesRoot, inst.Id)) { UseShellExecute = true }); } catch { }
        };
        actionsRow.Controls.Add(openAllBtn);
    }

    private void OpenFolder(string subFolder)
    {
        string dir = Path.Combine(DataStore.InstancesRoot, inst.Id, subFolder);
        Directory.CreateDirectory(dir);
        try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); } catch { }
    }

    private static Button MkActionBtn(string text)
    {
        var b = new Button { Text = text, Height = 32, AutoSize = true, Padding = new Padding(10, 0, 10, 0) };
        Theme.Apply(b);
        b.Font = new Font("Segoe UI", 8.5f);
        return b;
    }

    private async Task InstallWorldEditAsync()
    {
        string modsDir = Path.Combine(DataStore.InstancesRoot, inst.Id, "mods");
        Directory.CreateDirectory(modsDir);

        // Vérifier si WorldEdit est déjà installé
        var existing = Directory.GetFiles(modsDir, "worldedit*.jar");
        if (existing.Length > 0)
        {
            MessageBox.Show(
                Lang.T("WorldEdit est déjà installé :\n" + Path.GetFileName(existing[0]),
                    "WorldEdit is already installed:\n" + Path.GetFileName(existing[0])),
                "Team Launcher");
            return;
        }

        string mcVersion = inst.McVersion;
        string loader = inst.Loader;

        // Déterminer l'URL de téléchargement selon le loader
        string downloadUrl = loader switch
        {
            "Fabric" => $"https://mediafilez.forgecdn.net/files/5781/538/worldedit-mod-7.3.16-{mcVersion}.jar",
            "Forge" => $"https://mediafilez.forgecdn.net/files/5781/538/worldedit-mod-7.3.16-{mcVersion}.jar",
            "NeoForge" => $"https://mediafilez.forgecdn.net/files/5781/538/worldedit-mod-7.3.16-{mcVersion}.jar",
            _ => $"https://mediafilez.forgecdn.net/files/5781/538/worldedit-mod-7.3.16-{mcVersion}.jar"
        };

        // Utiliser Modrinth API pour trouver la bonne version
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

            // Chercher sur Modrinth
            string projectUrl = $"https://api.modrinth.com/v2/project/worldedit/version?game_versions=%5B%22{mcVersion}%22%5D&loaders=%5B%22{loader.ToLower()}%22%5D";
            var response = await http.GetStringAsync(projectUrl);
            var versions = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(response);

            if (versions.GetArrayLength() > 0)
            {
                var first = versions[0];
                var files = first.GetProperty("files");
                if (files.GetArrayLength() > 0)
                {
                    downloadUrl = files[0].GetProperty("url").GetString()!;
                }
            }
        }
        catch
        {
            // Fallback sur CurseForge
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            var bytes = await http.GetByteArrayAsync(downloadUrl);
            string fileName = $"worldedit-{mcVersion}-{loader}.jar";
            await File.WriteAllBytesAsync(Path.Combine(modsDir, fileName), bytes);

            MessageBox.Show(
                Lang.T(
                    $"WorldEdit installé !\n\n" +
                    $"Fichier : {fileName}\n" +
                    $"Lance Minecraft avec cette instance pour utiliser les commandes WorldEdit :\n" +
                    $"  //wand — obtenir la baguette magique\n" +
                    $"  //pos1, //pos2 — définir une sélection\n" +
                    $"  //set stone — remplir la sélection\n" +
                    $"  //copy, //paste — copier/coller\n" +
                    $"  //undo — annuler\n\n" +
                    $"Documentation : worldedit.enginehub.org",
                    $"WorldEdit installed!\n\n" +
                    $"File: {fileName}\n" +
                    $"Launch Minecraft with this instance to use WorldEdit commands:\n" +
                    $"  //wand — get the magic wand\n" +
                    $"  //pos1, //pos2 — set a selection\n" +
                    $"  //set stone — fill the selection\n" +
                    $"  //copy, //paste — copy/paste\n" +
                    $"  //undo — undo\n\n" +
                    $"Documentation: worldedit.enginehub.org"),
                "Team Launcher");
            RefreshData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Lang.T("Erreur lors de l'installation de WorldEdit :\n" + ex.Message,
                    "Error installing WorldEdit:\n" + ex.Message),
                "Team Launcher");
        }
    }

    private void OpenSelectedItem(object? sender, EventArgs e)
    {
        if (itemsList.SelectedItems.Count == 0) return;
        string? path = itemsList.SelectedItems[0].Tag as string;
        if (path == null) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }

    // ---------------- helpers RichTextBox ----------------

    private void AppendText(string text, Color color)
    {
        logBox.SelectionStart = logBox.TextLength;
        logBox.SelectionLength = 0;
        logBox.SelectionColor = color;
        logBox.AppendText(text);
    }
}

static class RichTextBoxExtensions
{
    public static void SuspendRendering(this RichTextBox rtb)
        => SendMessage(rtb.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

    public static void ResumeRendering(this RichTextBox rtb)
    {
        SendMessage(rtb.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
        rtb.Invalidate(true);
        rtb.Update();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);
    private const int WM_SETREDRAW = 0x000B;
}
