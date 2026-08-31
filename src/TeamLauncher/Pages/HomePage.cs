using System.Drawing.Drawing2D;

namespace TeamLauncher;

/// <summary>
/// Accueil minimaliste : en-tête sobre + dernière instance jouée en grand + liste des autres.
/// Pas de hero coloré, pas de chart, pas de pavés de stats : juste l'essentiel.
/// </summary>
public class HomePage : UserControl, IRefreshable
{
    private readonly Label welcome = new();
    private readonly Panel primaryRow = new();
    private readonly FlowLayoutPanel listFlow = new();
    private readonly TextBox searchBox = new();
    private readonly Label emptyLabel = new();

    public HomePage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        // Conteneur principal : un seul panneau avec AutoScroll
        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            AutoScroll = true,
            Padding = new Padding(32, 28, 32, 24)
        };

        // ================= EN-TÊTE =================
        welcome.ForeColor = Theme.Text;
        welcome.Font = new Font("Segoe UI", 16f, FontStyle.Regular);
        welcome.AutoSize = true;
        welcome.Location = new Point(0, 0);

        var subtitle = new Label
        {
            Text = Lang.T("Reprends ta partie ou choisis une instance dans la liste.", "Pick up where you left off or choose an instance from the list."),
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            Location = new Point(0, 32)
        };

        // ================= INSTANCE PRINCIPALE =================
        primaryRow.Location = new Point(0, 70);
        primaryRow.Size = new Size(800, 96);
        primaryRow.BackColor = Theme.Card;
        Theme.Round(primaryRow, 6);

        primaryRow.Paint += (s, e) =>
        {
            // ligne d'accent à gauche pour signaler l'instance principale
            using var b = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(b, 0, 0, 2, primaryRow.Height);
        };

        // ================= LISTE =================
        var listHeader = new Label
        {
            Text = Lang.T("Toutes les instances", "All instances"),
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            Location = new Point(0, 188)
        };

        searchBox.Width = 280;
        searchBox.Height = 30;
        searchBox.Font = new Font("Segoe UI", 10f);
        searchBox.BorderStyle = BorderStyle.None;
        searchBox.BackColor = Theme.Card;
        searchBox.ForeColor = Theme.Text;
        searchBox.Padding = new Padding(4);
        searchBox.PlaceholderText = "Rechercher…";
        searchBox.Location = new Point(0, 210);
        searchBox.TextChanged += (_, _) => RefreshData();

        listFlow.Location = new Point(0, 252);
        listFlow.AutoSize = true;
        listFlow.WrapContents = false;
        listFlow.FlowDirection = FlowDirection.TopDown;

        emptyLabel.ForeColor = Theme.TextDim;
        emptyLabel.Font = new Font("Segoe UI", 9.5f);
        emptyLabel.AutoSize = true;
        emptyLabel.Location = new Point(0, 252);
        emptyLabel.Visible = false;

        root.Controls.Add(welcome);
        root.Controls.Add(subtitle);
        root.Controls.Add(primaryRow);
        root.Controls.Add(listHeader);
        root.Controls.Add(searchBox);
        root.Controls.Add(listFlow);
        root.Controls.Add(emptyLabel);

        Controls.Add(root);

        // Ajuste la largeur de la liste quand la fenêtre change
        Resize += (_, _) =>
        {
            int w = root.ClientSize.Width - root.Padding.Horizontal;
            primaryRow.Width = w;
            searchBox.Width = Math.Min(280, w);
            // la liste est gérée via SuspendLayout dans RefreshData
        };
    }

    public void RefreshData()
    {
        var s = DataStore.Settings;
        welcome.Text = $"Bonjour {s.PlayerName}";

        // ---- ligne principale : dernière instance jouée ----
        BuildPrimaryRow();

        // ---- liste de toutes les instances ----
        listFlow.SuspendLayout();
        listFlow.Controls.Clear();

        string query = searchBox.Text.Trim();
        var visible = string.IsNullOrEmpty(query)
            ? s.Instances
            : s.Instances.Where(i =>
                i.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.McVersion.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        // exclure l'instance déjà affichée en haut (si elle existe)
        var last = s.Instances
            .Where(i => i.LastPlayed > DateTime.MinValue)
            .OrderByDescending(i => i.LastPlayed)
            .FirstOrDefault();
        if (last != null)
            visible = visible.Where(i => i.Id != last.Id).ToList();

        if (s.Instances.Count == 0)
        {
            listFlow.Visible = false;
            emptyLabel.Visible = true;
            emptyLabel.Text = Lang.T("Aucune instance pour l'instant. Va dans « Instances » pour en créer une.", "No instances yet. Go to \"Instances\" to create one.");
        }
        else if (visible.Count == 0)
        {
            listFlow.Visible = false;
            emptyLabel.Visible = true;
            emptyLabel.Text = $"Aucune instance ne correspond à « {query} ».";
        }
        else
        {
            listFlow.Visible = true;
            emptyLabel.Visible = false;
            foreach (var inst in visible)
                listFlow.Controls.Add(MakeListRow(inst));
        }

        listFlow.ResumeLayout();
    }

    /// <summary>Construit la grande ligne d'instance principale (dernière jouée).</summary>
    private void BuildPrimaryRow()
    {
        primaryRow.Controls.Clear();
        var s = DataStore.Settings;
        var last = s.Instances
            .Where(i => i.LastPlayed > DateTime.MinValue)
            .OrderByDescending(i => i.LastPlayed)
            .FirstOrDefault() ?? s.Instances.FirstOrDefault();

        if (last == null)
        {
            // pas d'instance : on masque la ligne
            primaryRow.Visible = false;
            return;
        }

        primaryRow.Visible = true;

        var name = new Label
        {
            Text = last.Name,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 12f),
            AutoSize = true,
            Location = new Point(20, 18)
        };

        var meta = new Label
        {
            Text = $"{last.Loader} • Minecraft {last.McVersion}" +
                   (last.LastPlayed > DateTime.MinValue
                       ? $" • dernière partie le {last.LastPlayed:dd/MM/yyyy à HH:mm}"
                       : ""),
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 9f),
            AutoSize = true,
            Location = new Point(20, 44)
        };

        var play = new Button
        {
            Text = Lang.T("Jouer", "Play"),
            Size = new Size(96, 32),
            Font = new Font("Segoe UI", 9.5f)
        };
        Theme.Apply(play, primary: true);
        // positionné à droite par le Resize
        play.Click += (_, _) => GameLauncher.Play(last);

        primaryRow.Controls.Add(name);
        primaryRow.Controls.Add(meta);
        primaryRow.Controls.Add(play);

        primaryRow.Resize -= RepositionPlay;
        primaryRow.Resize += RepositionPlay;
        RepositionPlay(null, EventArgs.Empty);
    }

    private void RepositionPlay(object? s, EventArgs e)
    {
        var play = primaryRow.Controls.OfType<Button>().FirstOrDefault();
        if (play != null) play.Left = primaryRow.Width - play.Width - 18;
    }

    /// <summary>Ligne compacte pour la liste des instances secondaires.</summary>
    private Panel MakeListRow(InstanceInfo inst)
    {
        var row = new Panel
        {
            Height = 56,
            BackColor = Theme.Card,
            Margin = new Padding(0, 0, 0, 6)
        };
        Theme.Round(row, 5);

        var name = new Label
        {
            Text = inst.Name,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 10.5f),
            AutoSize = true,
            Location = new Point(18, 10)
        };

        var meta = new Label
        {
            Text = string.IsNullOrWhiteSpace(inst.Description)
                ? $"{inst.Loader} • Minecraft {inst.McVersion}"
                : $"{inst.Description}  •  {inst.Loader} • Minecraft {inst.McVersion}",
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.5f),
            AutoSize = true,
            Location = new Point(18, 32)
        };

        var play = new Button
        {
            Text = Lang.T("Jouer", "Play"),
            Size = new Size(80, 28),
            Font = new Font("Segoe UI", 9f)
        };
        Theme.Apply(play);
        play.Click += (_, _) => GameLauncher.Play(inst);

        var details = new Button
        {
            Text = Lang.T("Détails", "Details"),
            Size = new Size(80, 28),
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(6, 0, 0, 0)
        };
        Theme.Apply(details);
        details.Click += (_, _) =>
        {
            AppEvents.PendingDetailId = inst.Id;
            AppEvents.NavigateTo("detail");
        };

        row.Controls.Add(name);
        row.Controls.Add(meta);
        row.Controls.Add(play);
        row.Controls.Add(details);

        row.Resize += (_, _) =>
        {
            int x = row.Width - play.Width - 14;
            play.Left = x;
            details.Left = x - details.Width - 6;
            // ajuster la zone de texte pour ne pas chevaucher les boutons
            int maxTextRight = details.Left - 12;
            name.MaximumSize = new Size(Math.Max(0, maxTextRight - name.Left), 0);
            meta.MaximumSize = new Size(Math.Max(0, maxTextRight - meta.Left), 0);
        };

        return row;
    }
}
