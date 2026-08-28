using System.Diagnostics;

namespace TeamLauncher;

/// <summary>
/// Explorateur de fichiers avec style moderne : items en liste avec icônes,
/// pas de slots type inventaire Minecraft.
/// </summary>
public class ExplorerPage : UserControl, IRefreshable
{
    private readonly FlowLayoutPanel itemsFlow = new();
    private readonly TextBox addressBox = new();
    private readonly Stack<string> history = new();
    private string current = "";
    private bool programmaticNav;

    public ExplorerPage()
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
            Text = Lang.T("Explorateur", "Explorer"),
            ForeColor = Theme.Text,
            Font = Theme.Title,
            AutoSize = true,
            Location = new Point(0, 0)
        };

        var hint = new Label
        {
            Text = Lang.T("Fichiers et dossiers de tes instances.", "Files and folders of your instances."),
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            Location = new Point(0, 28)
        };

        // ---- barre de navigation ----
        var navBar = new Panel { Height = 40, Location = new Point(0, 60), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

        var backBtn = new Button
        {
            Text = "◀",
            Size = new Size(36, 30),
            Location = new Point(0, 5),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Theme.TextDim,
            BackColor = Theme.Card,
            Cursor = Cursors.Hand
        };
        backBtn.FlatAppearance.BorderSize = 0;
        backBtn.FlatAppearance.MouseOverBackColor = Theme.Hover;
        backBtn.Click += (_, _) => GoBack();

        var upBtn = new Button
        {
            Text = "▲",
            Size = new Size(36, 30),
            Location = new Point(40, 5),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Theme.TextDim,
            BackColor = Theme.Card,
            Cursor = Cursors.Hand
        };
        upBtn.FlatAppearance.BorderSize = 0;
        upBtn.FlatAppearance.MouseOverBackColor = Theme.Hover;
        upBtn.Click += (_, _) => GoUp();

        addressBox.Location = new Point(84, 5);
        addressBox.Height = 30;
        addressBox.Font = new Font("Consolas", 9.5f);
        addressBox.BorderStyle = BorderStyle.FixedSingle;
        addressBox.BackColor = Theme.Card;
        addressBox.ForeColor = Theme.Text;
        addressBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        addressBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && Directory.Exists(addressBox.Text.Trim()))
                Navigate(addressBox.Text.Trim());
        };

        var openWinBtn = new Button
        {
            Text = Lang.T("Ouvrir dans Windows", "Open in Windows"),
            Size = new Size(140, 30),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Theme.TextDim,
            BackColor = Theme.Card,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        openWinBtn.FlatAppearance.BorderSize = 0;
        openWinBtn.FlatAppearance.MouseOverBackColor = Theme.Hover;
        openWinBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(current) { UseShellExecute = true }); } catch { }
        };

        navBar.Controls.AddRange(new Control[] { backBtn, upBtn, addressBox, openWinBtn });

        // ---- liste des items ----
        itemsFlow.AutoScroll = true;
        itemsFlow.WrapContents = false;
        itemsFlow.FlowDirection = FlowDirection.TopDown;
        itemsFlow.Dock = DockStyle.Fill;
        itemsFlow.BackColor = Theme.Bg;
        itemsFlow.Padding = new Padding(0, 8, 0, 0);

        root.Controls.Add(title);
        root.Controls.Add(hint);
        root.Controls.Add(navBar);
        root.Controls.Add(itemsFlow);
        Controls.Add(root);

        Resize += (_, _) =>
        {
            navBar.Width = Math.Max(500, Width - 48);
            openWinBtn.Left = navBar.Width - openWinBtn.Width;
            addressBox.Width = openWinBtn.Left - addressBox.Left - 8;
        };
    }

    public void RefreshData()
    {
        if (string.IsNullOrEmpty(current) || !Directory.Exists(current))
            Navigate(DataStore.InstancesRoot);
        else
            ShowFolder(current);
    }

    private void Navigate(string path)
    {
        if (!Directory.Exists(path)) return;
        if (!programmaticNav && current != path && current.Length > 0)
            history.Push(current);
        current = path;
        programmaticNav = false;
        ShowFolder(path);
    }

    private void GoBack()
    {
        if (history.Count == 0) return;
        string prev = history.Pop();
        programmaticNav = true;
        Navigate(prev);
    }

    private void GoUp()
    {
        var parent = Directory.GetParent(current);
        if (parent != null) Navigate(parent.FullName);
    }

    private void ShowFolder(string path)
    {
        addressBox.Text = path;
        itemsFlow.SuspendLayout();
        itemsFlow.Controls.Clear();

        try
        {
            foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
                itemsFlow.Controls.Add(MakeItem(dir, isFolder: true));

            foreach (var file in Directory.GetFiles(path).OrderBy(f => f))
                itemsFlow.Controls.Add(MakeItem(file, isFolder: false));
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }

        itemsFlow.ResumeLayout();
    }

    private Panel MakeItem(string path, bool isFolder)
    {
        string name = Path.GetFileName(path);
        string ext = Path.GetExtension(name).ToLowerInvariant();

        var item = new Panel
        {
            Height = 40,
            Width = 800,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
            Tag = path,
            Margin = new Padding(0, 1, 0, 1)
        };

        // Icône
        var icon = new Label
        {
            Text = isFolder ? "📁" : FileIcon(ext),
            Font = new Font("Segoe UI Emoji", 12f),
            Location = new Point(12, 6),
            Size = new Size(28, 28),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };

        // Nom
        var nameLabel = new Label
        {
            Text = name,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(48, 10),
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            BackColor = Color.Transparent
        };

        // Info (taille ou type)
        var info = new Label
        {
            Text = isFolder ? "" : FileInfoText(path),
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8f),
            AutoSize = true,
            Location = new Point(560, 12),
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        // Hover background
        var hoverBg = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Visible = false
        };
        hoverBg.BringToFront();

        item.Controls.Add(hoverBg);
        item.Controls.Add(icon);
        item.Controls.Add(nameLabel);
        item.Controls.Add(info);

        // Hover effect
        Action<bool> setHover = hover =>
        {
            hoverBg.BackColor = hover ? Theme.Hover : Color.Transparent;
            item.BackColor = hover ? Theme.Hover : Color.Transparent;
        };
        item.MouseEnter += (_, _) => setHover(true);
        item.MouseLeave += (_, _) => setHover(false);
        icon.MouseEnter += (_, _) => setHover(true);
        icon.MouseLeave += (_, _) => setHover(false);
        nameLabel.MouseEnter += (_, _) => setHover(true);
        nameLabel.MouseLeave += (_, _) => setHover(false);
        info.MouseEnter += (_, _) => setHover(true);
        info.MouseLeave += (_, _) => setHover(false);

        // Click
        EventHandler activate = (_, _) =>
        {
            if (isFolder) Navigate(path);
            else
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch { MessageBox.Show("Impossible d'ouvrir ce fichier.", "Team Launcher"); }
            }
        };
        item.DoubleClick += activate;
        icon.DoubleClick += activate;
        nameLabel.DoubleClick += activate;

        // Séparateur subtil en bas
        var sep = new Panel
        {
            Height = 1,
            Dock = DockStyle.Bottom,
            BackColor = Theme.Border
        };
        item.Controls.Add(sep);
        sep.BringToFront();

        return item;
    }

    private static string FileIcon(string ext) => ext switch
    {
        ".jar" => "☕",
        ".zip" or ".mrpack" => "📦",
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" => "🖼️",
        ".txt" or ".log" => "📄",
        ".json" => "📋",
        ".cfg" or ".properties" or ".toml" or ".yml" or ".yaml" => "⚙️",
        ".dat" or ".dat_old" or ".level" => "💾",
        ".mcmod" or ".inf" => "🧩",
        _ => "📄"
    };

    private static string FileInfoText(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            long bytes = fi.Length;
            if (bytes < 1024) return $"{bytes} o";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F0} Ko";
            return $"{bytes / (1024.0 * 1024.0):F1} Mo";
        }
        catch { return ""; }
    }
}
