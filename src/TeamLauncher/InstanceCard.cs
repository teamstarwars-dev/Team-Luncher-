namespace TeamLauncher;

/// <summary>
/// Carte visuelle d'une instance : version compacte, utilisée notamment sur
/// la page Explorateur. Style minimaliste, pas d'image, juste nom + meta + actions.
/// </summary>
public static class InstanceCard
{
    public static Panel Build(InstanceInfo inst)
    {
        var card = new Panel
        {
            Size = new Size(260, 92),
            BackColor = Theme.Card,
            Margin = new Padding(0, 0, 8, 8)
        };
        Theme.Blockify(card);

        var name = new Label
        {
            Text = inst.Name,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 10.5f),
            AutoEllipsis = true,
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(14, 10, 8, 0)
        };

        var desc = new Label
        {
            Text = string.IsNullOrWhiteSpace(inst.Description) ? $"{inst.Loader} • {inst.McVersion}" : inst.Description,
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.5f),
            AutoEllipsis = true,
            Dock = DockStyle.Top,
            Height = 22,
            Padding = new Padding(14, 2, 8, 0)
        };

        var meta = new Label
        {
            Text = $"{inst.Loader} • Minecraft {inst.McVersion} • {string.Format(Lang.T("{0} lancement(s)", "{0} launch(es)"), inst.Launches)}",
            ForeColor = Theme.Accent,
            Font = new Font("Segoe UI", 8f),
            AutoEllipsis = true,
            Dock = DockStyle.Top,
            Height = 22,
            Padding = new Padding(14, 2, 8, 0)
        };

        var play = new Button { Text = "Jouer", Dock = DockStyle.Bottom, Height = 32 };
        Theme.Apply(play, primary: true);
        play.Click += (_, _) => GameLauncher.Play(inst);

        var infoHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
        infoHost.Controls.Add(meta);
        infoHost.Controls.Add(desc);
        infoHost.Controls.Add(name);
        name.BringToFront();

        card.Controls.Add(play);
        card.Controls.Add(infoHost);

        card.DoubleClick += (_, _) => GameLauncher.Play(inst);

        return card;
    }

    public static void TryLoadImage(PictureBox box, string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                    box.Image = Image.FromStream(fs);
        }
        catch { /* image invalide : on garde le fond coloré */ }
    }
}
