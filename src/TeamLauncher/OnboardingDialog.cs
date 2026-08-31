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

    public bool SkipImport { get; set; }

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
        dots.Text = step switch { 0 => "● ○ ○ ○", 1 => "○ ● ○ ○", 2 => "○ ○ ● ○", _ => "○ ○ ○ ●" };
        stepPanel.Controls.Clear();
        switch (step)
        {
            case 0: BuildWelcome(); break;
            case 1: BuildAccount(); break;
            case 2: BuildImport(); break;
            default: BuildShareCode(); break;
        }
    }

    // ---------------- étape 1 : bienvenue ----------------

    private void BuildWelcome()
    {
        stepPanel.Controls.Add(MkTitle(Lang.T("Bienvenue dans ton nouveau launcher !", "Welcome to your new launcher!")));
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
                if (SkipImport)
                {
                    DataStore.Settings.OnboardingDone = true;
                    DataStore.Save();
                    DialogResult = DialogResult.OK;
                    return;
                }
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

        Theme.ApplyInput(pseudoBox);
        pseudoBox.Font = new Font("Segoe UI", 11f);
        Theme.ApplyInput(pseudoBox);
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
            if (SkipImport)
            {
                DataStore.Settings.OnboardingDone = true;
                DataStore.Save();
                DialogResult = DialogResult.OK;
                return;
            }
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

        nextBtn.Text = "Importer et continuer";
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

        // Passer à l'étape code de partage
        step++;
        ShowStep();
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

    // ---------------- étape 4 : code de partage ----------------

    private void BuildShareCode()
    {
        nextBtn.Visible = true;

        var title = MkTitle(Lang.T("Rejoins ton équipe !", "Join your team!"));
        title.Location = new Point(0, 4);
        stepPanel.Controls.Add(title);

        var hint = MkText(
            Lang.T(
                "Un membre de ta team t'a envoyé un code de partage ?\n" +
                "Colle-le ci-dessous pour télécharger le modpack complet\n" +
                "(mods, shaders, configs, mondes).\n\n" +
                "Tu peux aussi passer et créer ton propre modpack plus tard.",
                "A team member sent you a share code?\n" +
                "Paste it below to download the full modpack\n" +
                "(mods, shaders, configs, worlds).\n\n" +
                "You can skip this and create your own modpack later."),
            9.5f);
        hint.Location = new Point(0, 38);
        stepPanel.Controls.Add(hint);

        var codeBox = new TextBox
        {
            Font = new Font("Consolas", 13f, FontStyle.Bold),
            Size = new Size(500, 36),
            Location = new Point(0, 180),
            BackColor = Theme.Card,
            ForeColor = Theme.Accent,
            BorderStyle = BorderStyle.None,
            Padding = new Padding(4),
            PlaceholderText = Lang.T("Colle le code ici…", "Paste the code here…")
        };
        codeBox.KeyPress += (_, e) =>
        {
            if (e.KeyChar == (char)13) { ImportFromCode(codeBox.Text.Trim()); e.Handled = true; }
        };
        stepPanel.Controls.Add(codeBox);

        var importBtn = new Button
        {
            Text = Lang.T("📥  Importer le modpack", "📥  Import modpack"),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Size = new Size(260, 42),
            Location = new Point(0, 228)
        };
        Theme.Apply(importBtn, primary: true);
        importBtn.Click += (_, _) => ImportFromCode(codeBox.Text.Trim());
        stepPanel.Controls.Add(importBtn);

        var skipLbl = new Label
        {
            Text = Lang.T(
                "Pas de code ? Pas de souci, tu pourras importer un pack depuis\n" +
                "Instances → clic droit → Importer un pack partagé.",
                "No code? No worries, you can import a pack later from\n" +
                "Instances → right click → Import shared pack."),
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.5f),
            AutoSize = true,
            Location = new Point(0, 280)
        };
        stepPanel.Controls.Add(skipLbl);

        nextBtn.Text = Lang.T("Terminer", "Finish");
        nextBtn.Click -= FinishHandler;
        nextBtn.Click += FinishHandler;
    }

    private Label? _codeStatus;

    private void ImportFromCode(string code)
    {
        if (code.Length == 0)
        {
            MessageBox.Show(
                Lang.T("Colle un code de partage dans le champ.", "Paste a share code in the field."),
                "Team Launcher");
            return;
        }

        // Afficher le statut
        if (_codeStatus == null)
        {
            _codeStatus = new Label
            {
                ForeColor = Theme.Accent,
                Font = new Font("Segoe UI", 9f),
                AutoSize = true,
                Location = new Point(0, 310)
            };
            stepPanel.Controls.Add(_codeStatus);
        }
        _codeStatus.Text = Lang.T("⏳ Import en cours…", "⏳ Importing…");

        nextBtn.Enabled = false;
        Task.Run(async () =>
        {
            try
            {
                var inst = await PackShareService.ImportAsync(code,
                    step => BeginInvoke(() => _codeStatus.Text = step));
                BeginInvoke(() =>
                {
                    _codeStatus.ForeColor = Color.FromArgb(80, 200, 120);
                    _codeStatus.Text = Lang.T(
                        $"✓ « {inst.Name} » importé avec succès !",
                        $"✓ \"{inst.Name}\" imported successfully!");
                    nextBtn.Enabled = true;
                });
            }
            catch (Exception ex)
            {
                BeginInvoke(() =>
                {
                    _codeStatus.ForeColor = Color.FromArgb(220, 80, 80);
                    _codeStatus.Text = Lang.T("✕ Échec : " + ex.Message, "✕ Failed: " + ex.Message);
                    nextBtn.Enabled = true;
                });
            }
        });
    }

    private void FinishHandler(object? s, EventArgs e)
    {
        nextBtn.Click -= FinishHandler;
        DataStore.Settings.OnboardingDone = true;
        DataStore.Save();
        DialogResult = DialogResult.OK;
    }

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
