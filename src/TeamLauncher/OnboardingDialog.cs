using System.Drawing.Drawing2D;

namespace TeamLauncher;

/// <summary>
/// Assistant de premier lancement : bienvenue → connexion compte →
/// détection/import des instances existantes (.minecraft officiel, CurseForge).
/// </summary>
public class OnboardingDialog : Form
{
    private readonly Panel stepPanel = new();
    private readonly Label dots = new();
    private int step;

    private readonly TextBox pseudoBox = new();
    private readonly CheckedListBox foundList = new();
    private readonly Label importStatusLabel = new();
    private Button nextBtn = new();

    // dossiers candidats détectés sur le PC (instances Minecraft existantes)
    private readonly List<string> candidates = new();

    public OnboardingDialog()
    {
        Text = "Bienvenue — Team Launcher";
        Size = new Size(640, 480);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        TopMost = true;

        var path = new GraphicsPath();
        int r = 18;
        path.AddArc(0, 0, r, r, 180, 90);
        path.AddArc(Width - r, 0, r, r, 270, 90);
        path.AddArc(Width - r, Height - r, r, r, 0, 90);
        path.AddArc(0, Height - r, r, r, 90, 90);
        Region = new Region(path);

        bool dragging = false; Point start = default;
        MouseDown += (_, e) => { dragging = true; start = e.Location; };
        MouseMove += (_, e) => { if (dragging) Location = new Point(Left + e.X - start.X, Top + e.Y - start.Y); };
        MouseUp += (_, _) => dragging = false;

        var logo = new Label
        {
            Text = "TEAM LAUNCHER",
            ForeColor = Theme.Accent,
            Font = new Font("Segoe UI", 20f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(36, 26)
        };

        dots.ForeColor = Theme.TextDim;
        dots.Font = new Font("Segoe UI", 11f);
        dots.AutoSize = true;
        dots.Location = new Point(Width - 110, 34);
        dots.Text = "● ○ ○";

        stepPanel.Location = new Point(36, 84);
        stepPanel.Size = new Size(Width - 72, Height - 160);

        nextBtn.Size = new Size(200, 46);
        nextBtn.Location = new Point(Width / 2 - 100, Height - 66);
        Theme.Apply(nextBtn, primary: true);

        Controls.Add(logo);
        Controls.Add(dots);
        Controls.Add(stepPanel);
        Controls.Add(nextBtn);

        ShowStep();
    }

    private void ShowStep()
    {
        dots.Text = step switch { 0 => "● ○ ○", 1 => "○ ● ○", _ => "○ ○ ●" };
        stepPanel.Controls.Clear();
        switch (step)
        {
            case 0: BuildWelcome(); break;
            case 1: BuildAccount(); break;
            default: BuildImport(); break;
        }
    }

    // ---------------- étape 1 : bienvenue ----------------

    private void BuildWelcome()
    {
        stepPanel.Controls.Add(MkTitle("Bienvenue dans ton nouveau launcher !"));
        stepPanel.Controls.Add(MkText(
            "Léger, rapide et sans pub : tes instances, tes mods, tes serveurs.\n\n" +
            "En 2 minutes :\n" +
            "   1. Connecte ton compte Microsoft (ou joue hors-ligne)\n" +
            "   2. Importe automatiquement tes instances Minecraft existantes\n" +
            "      (.minecraft officiel, instances CurseForge détectées)\n" +
            "   3. Installe des mods depuis Modrinth et CurseForge en un clic\n\n" +
            "Tout est prêt ? C'est parti !", 10));
        nextBtn.Text = "Commencer";
        nextBtn.Click -= NextHandler;
        nextBtn.Click += NextHandler;
    }

    private void NextHandler(object? s, EventArgs e) { step++; ShowStep(); }

    // ---------------- étape 2 : compte ----------------

    private void BuildAccount()
    {
        var title = MkTitle("Connecte-toi pour jouer");
        title.Location = new Point(0, 8);
        stepPanel.Controls.Add(title);

        var msBtn = new Button
        {
            Text = "Se connecter avec Microsoft",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Size = new Size(380, 52),
            Location = new Point((stepPanel.Width - 380) / 2, 60)
        };
        Theme.Apply(msBtn, primary: true);
        msBtn.Click += async (_, _) =>
        {
            DataStore.Settings.AccountMode = "microsoft";
            DataStore.Save();
            msBtn.Enabled = false;
            msBtn.Text = "Connexion en cours...";
            var session = await MsAuth.LoginAsync(this);
            if (session != null)
            {
                DataStore.Settings.PlayerName = session.Name;
                DataStore.Save();
                AppEvents.NotifyAccountChanged();
                step++; ShowStep();
            }
            else
            {
                msBtn.Enabled = true;
                msBtn.Text = "Réessayer avec Microsoft";
            }
        };

        var sep = new Label
        {
            Text = "───────────   ou   ───────────",
            ForeColor = Theme.TextDim, AutoSize = true,
            Location = new Point((stepPanel.Width - 220) / 2, 128)
        };

        pseudoBox.Font = new Font("Segoe UI", 11f);
        pseudoBox.Size = new Size(380, 30);
        pseudoBox.Location = new Point((stepPanel.Width - 380) / 2, 168);
        pseudoBox.PlaceholderText = "Ton pseudo pour le mode hors-ligne";

        var offBtn = new Button
        {
            Text = "Continuer hors-ligne",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Size = new Size(380, 44),
            Location = new Point((stepPanel.Width - 380) / 2, 208)
        };
        Theme.Apply(offBtn);
        offBtn.Click += (_, _) =>
        {
            var name = pseudoBox.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show("Entre un pseudo pour le mode hors-ligne.", "Team Launcher");
                return;
            }
            DataStore.Settings.AccountMode = "offline";
            DataStore.Settings.PlayerName = name;
            DataStore.Save();
            AppEvents.NotifyAccountChanged();
            step++; ShowStep();
        };

        stepPanel.Controls.Add(msBtn);
        stepPanel.Controls.Add(sep);
        stepPanel.Controls.Add(pseudoBox);
        stepPanel.Controls.Add(offBtn);

        nextBtn.Text = "";
        nextBtn.Visible = false;
    }

    // ---------------- étape 3 : import des instances ----------------

    private void BuildImport()
    {
        nextBtn.Visible = true;

        var title = MkTitle("Tes instances Minecraft existantes");
        title.Location = new Point(0, 4);
        stepPanel.Controls.Add(title);

        var hint = MkText(
            "On a cherché sur ce PC les installations Minecraft déjà présentes.\n" +
            "Coche celles à importer (copie complète : mondes, mods, configs).", 9);
        hint.Location = new Point(0, 38);
        stepPanel.Controls.Add(hint);

        foreach (var dir in CandidateRoots())
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                // .minecraft officiel = une seule instance ; CurseForge/Prism = sous-dossiers
                bool looksLikeGame = Directory.Exists(Path.Combine(dir, "mods")) ||
                                     Directory.Exists(Path.Combine(dir, "saves")) ||
                                     File.Exists(Path.Combine(dir, "options.txt"));
                if (looksLikeGame && IsRealInstance(dir)) candidates.Add(dir);
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if (IsRealInstance(sub)) candidates.Add(sub);
                }
            }
            catch { }
        }

        foundList.CheckOnClick = true;
        foundList.Font = new Font("Segoe UI", 10f);
        foundList.BackColor = Theme.Card;
        foundList.ForeColor = Theme.Text;
        foundList.BorderStyle = BorderStyle.None;
        foundList.SetBounds(0, 96, stepPanel.Width, 180);
        foreach (var c in candidates.Distinct())
            foundList.Items.Add(c, true);
        if (candidates.Count == 0)
            foundList.Items.Add("(aucune installation trouvée — tu peux créer une instance plus tard)");

        importStatusLabel.ForeColor = Theme.TextDim;
        importStatusLabel.AutoSize = true;
        importStatusLabel.Location = new Point(0, 286);
        stepPanel.Controls.Add(foundList);
        stepPanel.Controls.Add(importStatusLabel);

        nextBtn.Text = "Importer et terminer";
        nextBtn.Click -= ImportHandlerAsync;
        nextBtn.Click += ImportHandlerAsync;
    }

    private async void ImportHandlerAsync(object? sender, EventArgs args)
    {
        nextBtn.Click -= ImportHandlerAsync;
        var chosen = foundList.CheckedItems.Cast<string>()
            .Where(candidates.Contains).Distinct().ToList();

        if (chosen.Count > 0)
        {
            nextBtn.Enabled = false;
            int i = 0;
            foreach (var src in chosen)
            {
                i++;
                importStatusLabel.Text = $"Import de « {Path.GetFileName(src)} » ({i}/{chosen.Count})...";
                await Task.Run(() => CopyInstance(src));
            }
        }

        DataStore.Settings.OnboardingDone = true;
        DataStore.Save();
        DialogResult = DialogResult.OK;
    }

    /// <summary>Une instance plausible contient au moins un marqueur de jeu.</summary>
    private static bool IsRealInstance(string dir)
    {
        try
        {
            return Directory.Exists(Path.Combine(dir, "mods"))
                   || Directory.Exists(Path.Combine(dir, "config"))
                   || Directory.Exists(Path.Combine(dir, "saves"))
                   || Directory.Exists(Path.Combine(dir, "resourcepacks"))
                   || File.Exists(Path.Combine(dir, "options.txt"));
        }
        catch { return false; }
    }

    private static IEnumerable<string> CandidateRoots()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(roaming, ".minecraft"); // launcher officiel
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "curseforge", "minecraft", "Instances");         // app CurseForge
        yield return Path.Combine(roaming, "PrismLauncher", "instances");
    }

    private static string GuessLoader(string dir)
    {
        try
        {
            string mods = Path.Combine(dir, "mods");
            if (!Directory.Exists(mods)) return "Vanilla";
            var jars = Directory.GetFiles(mods, "*.jar").Select(Path.GetFileName).OfType<string>().ToList();
            if (jars.Any(j => j.Contains("forge", StringComparison.OrdinalIgnoreCase))) return "Forge";
            if (jars.Any(j => j.Contains("fabric", StringComparison.OrdinalIgnoreCase))) return "Fabric";
            if (jars.Any(j => j.Contains("neoforge", StringComparison.OrdinalIgnoreCase))) return "NeoForge";
        }
        catch { }
        return "Vanilla";
    }

    private void CopyInstance(string src)
    {
        var inst = new InstanceInfo
        {
            Name = Path.GetFileName(src) is { Length: > 0 } n ? n : "Instance importée",
            Description = "Importée par l'assistant de premier lancement.",
            Loader = GuessLoader(src)
        };
        string dest = Path.Combine(DataStore.InstancesRoot, inst.Id);
        CopyDirectory(src, dest);
        DataStore.Settings.Instances.Add(inst);
        DataStore.Save();
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(src))
        {
            try { File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: false); }
            catch { }
        }
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    // ---------------- helpers ----------------

    private static Label MkTitle(string text) => new()
    {
        Text = text,
        ForeColor = Theme.Text,
        Font = new Font("Segoe UI", 14f, FontStyle.Bold),
        AutoSize = true,
        Location = new Point(0, 8)
    };

    private static Label MkText(string text, float size) => new()
    {
        Text = text,
        ForeColor = Theme.TextDim,
        Font = new Font("Segoe UI", size),
        AutoSize = true,
        Location = new Point(0, 50)
    };
}
