namespace TeamLauncher;

/// <summary>
/// Page 📰 Actualités : les dernières infos du launcher (mises à jour, événements,
/// maintenance…), alimentées par le flux JSON configuré dans Paramètres.
/// Cartes à hauteur mesurée précisément, badge de tag coloré, date lisible.
/// </summary>
public class NewsPage : UserControl, IRefreshable
{
    private const int PerPage = 1;

    private readonly FlowLayoutPanel list = new();
    private readonly Label status = new();
    private readonly Button refreshBtn = new();
    private readonly FlowLayoutPanel pager = new();
    private readonly Button prevBtn = new();
    private readonly Button nextBtn = new();
    private readonly Label pageLbl = new();
    private List<NewsService.NewsItem> allItems = new();
    private int page;

    public NewsPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true,
            Padding = new Padding(24, 16, 24, 16)
        };

        root.Controls.Add(new Label
        {
            Text = "ACTUALITÉS", ForeColor = Theme.Text,
            Font = Theme.Title,
            AutoSize = true
        });

        var headRow = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false,
            Margin = new Padding(0, 2, 0, 0)
        };
        headRow.Controls.Add(new Label
        {
            Text = Lang.T(
                "Les dernières nouvelles du launcher : mises à jour, événements, annonces.",
                "The latest launcher news: updates, events, announcements."),
            ForeColor = Theme.TextDim, AutoSize = true,
            Margin = new Padding(0, 8, 16, 0)
        });
        refreshBtn.Text = Lang.T("⟳ Actualiser", "⟳ Refresh");
        refreshBtn.Width = 130;
        refreshBtn.Height = 34;
        Theme.Apply(refreshBtn);
        refreshBtn.Margin = new Padding(0, 2, 0, 0);
        refreshBtn.Click += (_, _) => RefreshData();
        headRow.Controls.Add(refreshBtn);
        root.Controls.Add(headRow);

        status.ForeColor = Theme.TextDim;
        status.AutoSize = true;
        status.Margin = new Padding(0, 10, 0, 0);
        root.Controls.Add(status);

        list.FlowDirection = FlowDirection.TopDown;
        list.WrapContents = false;
        list.AutoScroll = false;
        list.Margin = new Padding(0, 8, 0, 0);
        root.Controls.Add(list);

        // ---- pagination : plus besoin de faire défiler à la souris ----
        pager.FlowDirection = FlowDirection.LeftToRight;
        pager.WrapContents = false;
        pager.AutoSize = true;
        pager.Margin = new Padding(0, 10, 0, 0);

        prevBtn.Text = "◀  " + Lang.T("Précédent", "Previous");
        prevBtn.Width = 140;
        prevBtn.Height = 36;
        Theme.Apply(prevBtn);
        prevBtn.Margin = new Padding(0, 0, 8, 0);
        prevBtn.Click += (_, _) => { if (page > 0) { page--; ShowPage(); } };

        nextBtn.Text = Lang.T("Suivant", "Next") + "  ▶";
        nextBtn.Width = 130;
        nextBtn.Height = 36;
        Theme.Apply(nextBtn);
        nextBtn.Margin = new Padding(8, 0, 14, 0);
        nextBtn.Click += (_, _) => { if (page < MaxPage()) { page++; ShowPage(); } };

        pageLbl.ForeColor = Theme.TextDim;
        pageLbl.AutoSize = true;
        pageLbl.Margin = new Padding(0, 9, 0, 0);

        pager.Controls.Add(prevBtn);
        pager.Controls.Add(nextBtn);
        pager.Controls.Add(pageLbl);
        root.Controls.Add(pager);

        Controls.Add(root);
        Resize += (_, _) => { list.Width = Math.Max(600, Width - 48); };
    }

    private int MaxPage() => Math.Max(0, (allItems.Count - 1) / PerPage);

    private void ShowPage()
    {
        int max = MaxPage();
        if (page > max) page = max;
        if (page < 0) page = 0;

        list.SuspendLayout();
        list.Controls.Clear();

        foreach (var n in allItems.Skip(page * PerPage).Take(PerPage))
            list.Controls.Add(MakeNewsCard(n));

        list.ResumeLayout();

        prevBtn.Enabled = page > 0;
        nextBtn.Enabled = page < max;
        bool manyPages = allItems.Count > PerPage;
        pager.Visible = manyPages;
        if (manyPages)
            pageLbl.Text = string.Format(Lang.T("Page {0} / {1}", "Page {0} / {1}"), page + 1, max + 1);
    }

    public void RefreshData()
    {
        if (DataStore.Settings.NewsUrl.Trim().Length == 0)
        {
            status.Text = Lang.T(
                "Aucun flux configuré.\n" +
                "(Paramètres → URL des actualités — pour toi : un simple fichier JSON en ligne)",
                "No feed configured.\n" +
                "(Settings → News URL — a simple JSON file hosted online works)");
            list.Controls.Clear();
            return;
        }

        status.Text = Lang.T("Chargement des actualités…", "Loading news…");
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var items = await NewsService.GetAsync();

        if (IsDisposed) return;
        BeginInvoke(() =>
        {
            allItems = items;
            page = 0;

            if (items.Count == 0)
                status.Text = Lang.T(
                    "Pas d'actualités pour le moment.",
                    "No news for now.");
            else if (items.Count == 1)
                status.Text = Lang.T("1 actualité.", "1 news item.");
            else
                status.Text = string.Format(
                    Lang.T("{0} actualités, de la plus récente à la plus ancienne.",
                           "{0} news items, newest first."), items.Count);

            ShowPage();
        });
    }

    /// <summary>Couleur du badge selon le tag (MAJ, NOUVEAU, FIX, EVENT…).</summary>
    private static Color TagColor(string tag)
    {
        string t = tag.Trim().ToUpperInvariant();
        if (t is "MAJ" or "UPDATE" or "VERSION") return Color.FromArgb(111, 191, 63);   // vert
        if (t is "NOUVEAU" or "NEW" or "FEATURE") return Color.FromArgb(74, 163, 191); // bleu
        if (t is "FIX" or "CORRECTION" or "BUG") return Color.FromArgb(224, 160, 60);  // orange
        if (t is "EVENT" or "ÉVÉNEMENT" or "EVENEMENT") return Color.FromArgb(170, 125, 220); // violet
        if (t is "IMPORTANT" or "URGENT") return Color.FromArgb(242, 85, 90);          // rouge
        return Theme.TextDim;
    }

    private static Panel MakeNewsCard(NewsService.NewsItem n)
    {
        var card = new Panel
        {
            Width = 900,
            BackColor = Theme.Card,
            Margin = new Padding(0, 6, 14, 6),
            Padding = new Padding(16, 12, 16, 14)
        };

        // ---- ligne de titre : badge tag + titre + date ----
        bool hasTag = !string.IsNullOrWhiteSpace(n.Tag);
        int y = 12;
        int x = 16;

        if (hasTag)
        {
            var badge = new Label
            {
                Text = n.Tag.ToUpperInvariant(),
                BackColor = TagColor(n.Tag),
                ForeColor = Color.FromArgb(18, 20, 14),
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Location = new Point(x, y + 3),
                AutoSize = true,
                Padding = new Padding(6, 3, 6, 3)
            };
            card.Controls.Add(badge);
            x += badge.Width + 10;
        }

        var title = new Label
        {
            Text = n.Title,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Location = new Point(x, y), AutoSize = true
        };
        card.Controls.Add(title);

        var date = new Label
        {
            Text = FormatDate(n.Date),
            ForeColor = Theme.TextDim,
            Font = new Font("Consolas", 8.5f),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            AutoSize = true
        };
        card.Controls.Add(date);

        // ---- texte : hauteur mesurée précisément ----
        var textFont = new Font("Segoe UI", 9f);
        int innerWidth = 900 - 32;
        var textSize = TextRenderer.MeasureText(n.Text, textFont,
            new Size(innerWidth, int.MaxValue), TextFormatFlags.WordBreak);

        var text = new Label
        {
            Text = n.Text,
            ForeColor = Theme.Text,
            Font = textFont,
            Location = new Point(16, y + 26),
            Size = new Size(innerWidth, textSize.Height)
        };
        card.Controls.Add(text);

        // ---- positionnement de la date à droite, puis hauteur totale ----
        card.Height = y + 26 + textSize.Height + 10;
        date.Location = new Point(900 - date.Width - 16, y + 4);

        Theme.Blockify(card);
        return card;
    }

    private static string FormatDate(string raw)
    {
        if (DateTime.TryParse(raw, out var d)) return d.ToString("dd/MM/yyyy");
        return raw;
    }
}
