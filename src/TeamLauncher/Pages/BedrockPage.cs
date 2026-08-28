using System.Diagnostics;

namespace TeamLauncher;

/// <summary>
/// Page Bedrock : bloc de statut d'installation, actions empilées façon menu,
/// et carte "à savoir" sur les différences Java / Bedrock.
/// </summary>
public class BedrockPage : UserControl, IRefreshable
{
    private readonly Label statusLabel = new();
    private readonly Button playBtn = new();

    private static string PackageDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe");

    private static string OptionsDir => Path.Combine(PackageDir,
        "LocalState", "games", "com.mojang", "minecraftpe");
    private static string OptionsFile => Path.Combine(OptionsDir, "options.txt");
    private static string BackupFile => Path.Combine(OptionsDir, "options.txt.backup-original");

    public BedrockPage()
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
            Text = Lang.T("CHANGER DE MINECRAFT", "SWITCH MINECRAFT"), ForeColor = Theme.Text,
            Font = Theme.Title,
            AutoSize = true
        });
        root.Controls.Add(new Label
        {
            Text = Lang.T("Ton launcher gère Minecraft JAVA. Bascule ici sur Minecraft BEDROCK\n" +
                   "(édition Microsoft Store, cross-play mobile / console / PC).", "Your launcher manages Minecraft JAVA. Switch to Minecraft BEDROCK here\n" +
                   "(Microsoft Store edition, cross-play mobile / console / PC)."),
            ForeColor = Theme.TextDim, AutoSize = true
        });

        // ---- bloc de statut ----
        var statusBlock = new Panel { Width = 920, Height = 70, BackColor = Theme.Card, Margin = new Padding(0, 16, 0, 0) };
        Theme.Blockify(statusBlock);
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.Padding = new Padding(18, 0, 0, 0);
        statusLabel.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        statusBlock.Controls.Add(statusLabel);
        root.Controls.Add(statusBlock);

        // ---- actions empilées façon menu MC ----
        playBtn.Text = "▶  LANCER MINECRAFT BEDROCK";
        playBtn.Width = 440;
        playBtn.Height = 56;
        StyleMenuBtn(playBtn, primary: true);
        playBtn.Margin = new Padding(0, 22, 0, 0);
        playBtn.Click += (_, _) => LaunchBedrock();

        var optBtn = new Button { Text = "🚀 OPTIMISER LES PERFORMANCES", Width = 440, Height = 52 };
        StyleMenuBtn(optBtn, false);
        optBtn.Margin = new Padding(0, 10, 0, 0);
        optBtn.Click += (_, _) => OptimizeGraphics();

        var restoreBtn = new Button { Text = "↩ RESTAURER LES RÉGLAGES D'ORIGINE", Width = 440, Height = 46 };
        StyleMenuBtn(restoreBtn, false);
        restoreBtn.Margin = new Padding(0, 10, 0, 0);
        restoreBtn.Click += (_, _) => RestoreGraphics();

        var storeBtn = new Button { Text = "🛒 INSTALLER DEPUIS LE MICROSOFT STORE", Width = 440, Height = 46 };
        StyleMenuBtn(storeBtn, false);
        storeBtn.Margin = new Padding(0, 10, 0, 0);
        storeBtn.Click += (_, _) =>
        {
            try { ProcessStart("ms-windows-store://pdp/?ProductId=9NBLGGH2JHXJ"); }
            catch { }
        };

        root.Controls.Add(playBtn);
        root.Controls.Add(optBtn);
        root.Controls.Add(restoreBtn);
        root.Controls.Add(storeBtn);

        // ---- carte à savoir ----
        var infoCard = new Panel { Width = 920, Height = 150, BackColor = Theme.Card, Margin = new Padding(0, 24, 0, 0) };
        Theme.Blockify(infoCard);
        var infoText = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 12, 8, 8),
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 9f),
            Text = "DIFFÉRENCES JAVA vs BEDROCK :\n" +
                   "• Java : mods, Forge/Fabric/NeoForge, serveurs communautaires (ton usage actuel)\n" +
                   "• Bedrock : cross-play avec mobile/console/PC, Marketplace officielle, plus fluide sur petites machines\n" +
                   "• Les mondes, skins et mods ne sont PAS partageables entre les deux éditions",
            TextAlign = ContentAlignment.TopLeft
        };
        infoCard.Controls.Add(infoText);
        root.Controls.Add(infoCard);

        Controls.Add(root);
        RefreshData();
    }

    private static void StyleMenuBtn(Button b, bool primary)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 2;
        b.FlatAppearance.BorderColor = ControlPaint.Dark(primary ? Theme.Accent : Theme.Panel, 0.15f);
        b.FlatAppearance.MouseOverBackColor = primary ? Theme.AccentHover : Theme.Hover;
        b.BackColor = primary ? Theme.Accent : Theme.Panel;
        b.ForeColor = primary ? Color.FromArgb(20, 24, 16) : Theme.Text;
        b.Cursor = Cursors.Hand;
        b.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        b.TextAlign = ContentAlignment.MiddleCenter;
    }

    private static void ProcessStart(string target)
    {
        try { System.Diagnostics.Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { }
    }

    private void LaunchBedrock()
    {
        if (!Directory.Exists(PackageDir))
        {
            MessageBox.Show(
                "Minecraft Bedrock n'est pas installé sur ce PC.\n" +
                "Installe-le d'abord via le bouton Microsoft Store ci-dessous.",
                "Team Launcher");
            return;
        }
        ProcessStart("minecraft:");
    }

    // ---------------- optimisation graphique ----------------

    private static void OptimizeGraphics()
    {
        try
        {
            Directory.CreateDirectory(OptionsDir);
            if (File.Exists(OptionsFile) && !File.Exists(BackupFile))
                File.Copy(OptionsFile, BackupFile);

            var lines = File.Exists(OptionsFile)
                ? File.ReadAllLines(OptionsFile).ToList()
                : new List<string>();

            Set(lines, "gfx_viewdistance", "6");
            Set(lines, "gfx_fancygraphics", "0");
            Set(lines, "gfx_particleviewdistance", "4");
            Set(lines, "gfx_viewbobbing", "0");
            Set(lines, "gfx_smoothbrightness", "1");

            File.WriteAllLines(OptionsFile, lines);
            MessageBox.Show(
                "Optimisations appliquées :\n" +
                "• Distance d'affichage : 6 chunks\n• Graphismes : rapides\n" +
                "• Particules : réduites\n• Balancement de caméra : désactivé\n\n" +
                "Réglages d'origine sauvegardés (bouton Restaurer).", "Team Launcher");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
    }

    private static void RestoreGraphics()
    {
        try
        {
            if (!File.Exists(BackupFile))
            {
                MessageBox.Show("Aucune sauvegarde d'origine trouvée.", "Team Launcher");
                return;
            }
            File.Copy(BackupFile, OptionsFile, overwrite: true);
            MessageBox.Show("Réglages d'origine restaurés.", "Team Launcher");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
    }

    private static void Set(List<string> lines, string key, string value)
    {
        int idx = lines.FindIndex(l => l.StartsWith(key + ":", StringComparison.Ordinal));
        if (idx >= 0) lines[idx] = $"{key}:{value}";
        else lines.Add($"{key}:{value}");
    }

    public void RefreshData()
    {
        bool installed = Directory.Exists(PackageDir);
        statusLabel.Text = installed
            ? "✅  Minecraft Bedrock est installé sur ce PC"
            : "❌  Minecraft Bedrock n'est pas installé";
        statusLabel.ForeColor = installed ? Theme.Accent : Color.OrangeRed;
        playBtn.Enabled = installed;
    }
}


