using System.Diagnostics;
using System.Text;

namespace TeamLauncher;

public class InstancesPage : UserControl, IRefreshable
{
    private readonly FlowLayoutPanel cardsFlow = new();
    private TextBox filterBox = new();
    private Label emptyLabel = new();

    private static readonly Font CardNameFont = new("Segoe UI", 10f, FontStyle.Bold);
    private static readonly Font CardMetaFont = new("Segoe UI", 8f);
    private static readonly Font PlaceholderFont = new("Segoe UI", 26f, FontStyle.Bold);
    private static readonly Font PlayBtnFont = new("Segoe UI", 10f, FontStyle.Bold);

    public InstancesPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            AutoScroll = true,
            Padding = new Padding(32, 28, 32, 24)
        };

        var title = new Label
        {
            Text = Lang.T("Instances", "Instances"),
            ForeColor = Theme.Text,
            Font = Theme.Title,
            AutoSize = true,
            Location = new Point(0, 0)
        };

        var hint = new Label
        {
            Text = Lang.T("Crée, importe, modifie ou supprime tes instances. Double-clique pour jouer.", "Create, import, edit or delete your instances. Double-click to play."),
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            Location = new Point(0, 34)
        };

        var createBtn = new Button { Text = "+ Nouvelle instance", Width = 170, Height = 32 };
        Theme.Apply(createBtn, primary: true);
        createBtn.Location = new Point(0, 76);
        createBtn.Click += (_, _) =>
        {
            using var dlg = new CreateInstanceDialog();
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                RefreshData();
        };

        var moreBtn = new Button { Text = Lang.T("Plus d'actions  ▾", "More actions  ▾"), Width = 150, Height = 32, Margin = new Padding(8, 0, 0, 0) };
        Theme.Apply(moreBtn);
        moreBtn.Location = new Point(178, 76);
        moreBtn.Click += (_, _) => BuildActionsMenu(moreBtn).Show(moreBtn, new Point(0, moreBtn.Height));

        filterBox = new TextBox
        {
            Width = 260,
            Height = 30,
            Font = new Font("Segoe UI", 10f),
            PlaceholderText = Lang.T("Rechercher…", "Search…"),
            Location = new Point(0, 128)
        };
        Theme.ApplyInput(filterBox);
        filterBox.TextChanged += (_, _) => RefreshData();

        emptyLabel.ForeColor = Theme.TextDim;
        emptyLabel.Font = new Font("Segoe UI", 9.5f);
        emptyLabel.AutoSize = true;
        emptyLabel.Location = new Point(0, 176);
        emptyLabel.Visible = false;

        cardsFlow.AutoSize = true;
        cardsFlow.FlowDirection = FlowDirection.LeftToRight;
        cardsFlow.WrapContents = true;
        cardsFlow.Location = new Point(0, 176);

        root.Controls.Add(title);
        root.Controls.Add(hint);
        root.Controls.Add(createBtn);
        root.Controls.Add(moreBtn);
        root.Controls.Add(filterBox);
        root.Controls.Add(cardsFlow);
        root.Controls.Add(emptyLabel);

        Controls.Add(root);
    }

    private ContextMenuStrip BuildActionsMenu(Control parent)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            ShowImageMargin = false,
            Font = new Font("Segoe UI", 9.5f)
        };
        menu.Items.Add("Installer Essential", null, (_, _) =>
        {
            using var dlg = new EssentialDialog();
            dlg.ShowDialog(FindForm());
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Importer un dossier", null, ImportInstance);
        menu.Items.Add("Importer un .zip", null, ImportZip);
        menu.Items.Add("Exporter en .zip", null, ExportPack);
        menu.Items.Add("Depuis CurseForge (URL)", null, ImportFromCurseForgeUrl);
        menu.Items.Add("Depuis Modrinth (.mrpack)", null, ImportMrPack);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Partager mon pack", null, SharePack);
        menu.Items.Add("Importer un pack partagé", null, ImportSharedPack);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Mettre à jour les mods", null, UpdateMods);
        menu.Items.Add("Dupliquer une instance", null, (_, _) =>
        {
            using var pick = new InstancePickDialog("Dupliquer une instance", "Dupliquer");
            if (pick.ShowDialog(FindForm()) != DialogResult.OK || pick.Selected == null) return;
            try
            {
                PackService_Duplicate(pick.Selected);
                RefreshData();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
        });
        menu.Items.Add("Réparer une instance", null, RepairInstance);
        foreach (ToolStripItem item in menu.Items)
        {
            item.ForeColor = Theme.Text;
            if (item is ToolStripSeparator sep) sep.BackColor = Theme.Border;
        }
        menu.Opening += (_, _) => { foreach (ToolStripItem i in menu.Items) i.BackColor = Theme.Panel; };
        return menu;
    }

    private void RepairInstance(object? sender, EventArgs e)
    {
        using var pick = new InstancePickDialog("Réparer une instance", "Vérifier et réparer");
        if (pick.ShowDialog(FindForm()) != DialogResult.OK || pick.Selected == null) return;
        var inst = pick.Selected;
        var btn = (Button)sender!;
        btn.Enabled = false;
        Task.Run(async () =>
        {
            try
            {
                string version = inst.McVersion is "latest" or "?" or "" or null
                    ? await MojangApi.LatestReleaseAsync() : inst.McVersion;
                await GameInstaller.InstallAsync(version, inst.Loader, (_, _, _) => { },
                    CancellationToken.None, forceVerify: true);
                return "Instance vérifiée : tous les fichiers sont intacts\n(les fichiers corrompus ou manquants ont été retéléchargés).";
            }
            catch (Exception ex) { return "Erreur pendant la réparation : " + ex.Message; }
        }).ContinueWith(t =>
        {
            BeginInvoke(() =>
            {
                btn.Enabled = true;
                MessageBox.Show(t.Result, "Team Launcher");
            });
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ImportFromCurseForgeUrl(object? sender, EventArgs e)
    {
        using var input = new Form
        {
            Text = Lang.T("Installer un modpack CurseForge", "Install a CurseForge modpack"),
            Size = new Size(500, 200),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            BackColor = Theme.Panel
        };

        var lbl = new Label
        {
            Text = Lang.T("Colle le lien ou le code CurseForge :", "Paste the CurseForge link or code:"),
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(16, 14),
            AutoSize = true
        };

        var hint = new Label
        {
            Text = Lang.T("URL, ID projet, ou slug (ex: rlcraft, all-the-mods-9)", "URL, project ID, or slug (e.g. rlcraft, all-the-mods-9)"),
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8f),
            Location = new Point(16, 36),
            AutoSize = true
        };

        var urlBox = new TextBox
        {
            Location = new Point(16, 60),
            Width = 450,
            Font = new Font("Segoe UI", 10f),
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            PlaceholderText = "https://www.curseforge.com/minecraft/modpacks/..."
        };

        var dlBtn = new Button
        {
            Text = Lang.T("Installer", "Install"),
            Height = 36,
            Width = 120,
            Location = new Point(16, 100)
        };
        Theme.Apply(dlBtn, primary: true);

        var cancelBtn = new Button
        {
            Text = "Annuler",
            Height = 36,
            Width = 80,
            Location = new Point(146, 100)
        };
        Theme.Apply(cancelBtn);
        cancelBtn.Click += (_, _) => input.Close();

        input.Controls.AddRange(new Control[] { lbl, hint, urlBox, dlBtn, cancelBtn });
        input.AcceptButton = dlBtn;
        input.CancelButton = cancelBtn;

        dlBtn.Click += async (_, _) =>
        {
            string raw = urlBox.Text.Trim();
            if (raw.Length == 0) return;

            int projectId = await ParseCurseForgeIdAsync(raw);
            if (projectId <= 0)
            {
                MessageBox.Show(
                    "Introuvable. Colle une URL, un ID, ou un slug CurseForge valide.\n\n" +
                    "Exemples :\n" +
                    "• https://www.curseforge.com/minecraft/modpacks/rlcraft\n" +
                    "• 123456\n" +
                    "• rlcraft",
                    "Team Launcher");
                return;
            }

            dlBtn.Enabled = false;
            dlBtn.Text = Lang.T("Recherche…", "Searching…");
            urlBox.Enabled = false;

            try
            {
                // 1. Récupérer les fichiers du projet
                var files = await CurseForgeApi.GetFilesAsync(projectId);
                var packFile = files.FirstOrDefault(f => f.DownloadUrl != null && f.FileName.EndsWith(".zip"));

                if (packFile == null)
                {
                    MessageBox.Show(
                        "Aucun fichier modpack (.zip) trouvé pour ce projet CurseForge.",
                        "Team Launcher");
                    return;
                }

                // 2. Télécharger le zip
                dlBtn.Text = Lang.T("Téléchargement…", "Downloading…");
                string tmpZip = Path.Combine(Path.GetTempPath(), $"cf_{projectId}_{packFile.FileId}.zip");
                using (var http = new HttpClient())
                {
                    var data = await http.GetByteArrayAsync(packFile.DownloadUrl);
                    await File.WriteAllBytesAsync(tmpZip, data);
                }

                // 3. Importer via CfPackImporter
                dlBtn.Text = Lang.T("Installation…", "Installing…");
                var inst = await CfPackImporter.ImportAsync(tmpZip,
                    step => BeginInvoke(() => dlBtn.Text = step.Length > 30 ? step[..30] + "…" : step));

                try { File.Delete(tmpZip); } catch { }

                RefreshData();
                input.Close();
                MessageBox.Show(
                    $"Modpack « {inst.Name} » installé !\n{inst.Loader} • Minecraft {inst.McVersion}",
                    "Team Launcher");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message, "Team Launcher");
            }
            finally
            {
                dlBtn.Enabled = true;
                dlBtn.Text = Lang.T("Installer", "Install");
                urlBox.Enabled = true;
            }
        };

        input.ShowDialog(FindForm());
    }

    /// <summary>Extrait le numéro de projet CurseForge depuis une URL, un slug, ou un code brut.</summary>
    private static async Task<int> ParseCurseForgeIdAsync(string input)
    {
        // Numéro brut
        if (int.TryParse(input.Trim(), out int id)) return id;

        // URL CurseForge
        try
        {
            var uri = new Uri(input);
            var segments = uri.Segments.Select(s => s.TrimEnd('/')).ToList();

            foreach (var seg in segments)
                if (int.TryParse(seg, out int segId)) return segId;

            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            string? pid = query.Get("projectID");
            if (pid != null && int.TryParse(pid, out int qid)) return qid;

            // Slug dans l'URL : https://www.curseforge.com/minecraft/modpacks/rlcraft
            int modsIdx = segments.FindIndex(s => s.Equals("modpacks/", StringComparison.OrdinalIgnoreCase));
            if (modsIdx >= 0 && modsIdx + 1 < segments.Count)
            {
                string slug = segments[modsIdx + 1];
                if (slug.Length > 0)
                {
                    var hits = await CurseForgeApi.SearchAsync(slug, CurseForgeApi.ClassModpacks);
                    var match = hits.FirstOrDefault(h =>
                        h.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase) ||
                        h.Title.Replace(" ", "-").Equals(slug, StringComparison.OrdinalIgnoreCase));
                    if (match.ProjectId > 0) return match.ProjectId;
                    if (hits.Count > 0) return hits[0].ProjectId;
                }
            }
        }
        catch { }

        // Sinon : traiter comme un slug/nom → recherche
        var results = await CurseForgeApi.SearchAsync(input.Trim(), CurseForgeApi.ClassModpacks);
        if (results.Count > 0) return results[0].ProjectId;

        return -1;
    }

    private void ImportMrPack(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "Modpack Modrinth (.mrpack)|*.mrpack" };
        if (ofd.ShowDialog(FindForm()) != DialogResult.OK) return;

        string path = ofd.FileName;
        var btn = (Control)sender!;
        if (btn is Button b) b.Enabled = false;
        Notifier.Show("Modpack Modrinth", "Import en cours, ça peut prendre quelques minutes…");

        Task.Run(async () => await MrPackImporter.ImportAsync(path,
            step => BeginInvoke(() => filterBox.PlaceholderText = step)))
            .ContinueWith(t =>
            {
                if (btn is Button b2) b2.Enabled = true;
                filterBox.PlaceholderText = "Rechercher…";
                RefreshData();
                if (t.Exception != null)
                {
                    MessageBox.Show("Échec de l'import du modpack :\n" +
                        t.Exception.InnerException?.Message, "Team Launcher");
                }
                else
                {
                    Notifier.Show("Modpack importé",
                        $"« {t.Result.Name} » est prêt. Lance-le depuis l'accueil !");
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ShareInstance(InstanceInfo inst)
    {
        using var dlg = new Form
        {
            Text = $"Partager « {inst.Name} »",
            Size = new Size(420, 220),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            BackColor = Theme.Panel
        };

        var lbl = new Label
        {
            Text = Lang.T("Comment veux-tu partager cette instance ?", "How do you want to share this instance?"),
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 10f),
            Location = new Point(16, 16),
            AutoSize = true
        };

        var zipBtn = new Button
        {
            Text = "📦  Exporter en .zip (complet)",
            Size = new Size(370, 44),
            Location = new Point(16, 52),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f),
            ForeColor = Theme.Text,
            BackColor = Theme.Card,
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };
        zipBtn.FlatAppearance.BorderSize = 0;
        zipBtn.FlatAppearance.MouseOverBackColor = Theme.Hover;
        zipBtn.Click += async (_, _) =>
        {
            dlg.Close();
            using var save = new SaveFileDialog
            {
                FileName = inst.Name + ".zip",
                Filter = "Archive zip|*.zip"
            };
            if (save.ShowDialog(FindForm()) != DialogResult.OK) return;
            try
            {
                zipBtn.Text = "⏳ Création de l'archive…";
                zipBtn.Enabled = false;
                await PackShareService.ExportZipAsync(inst, save.FileName,
                    step => BeginInvoke(() => zipBtn.Text = step.Length > 40 ? step[..40] + "…" : step));
                MessageBox.Show(
                    $"Modpack complet exporté :\n{save.FileName}\n\n" +
                    "Ce zip contient : mods, shaders, configs, mondes, resource packs.",
                    "Team Launcher");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
            finally { zipBtn.Text = "📦  Exporter en .zip (complet)"; zipBtn.Enabled = true; }
        };

        var codeBtn = new Button
        {
            Text = "🔗  Générer un code de partage",
            Size = new Size(370, 44),
            Location = new Point(16, 106),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f),
            ForeColor = Theme.Text,
            BackColor = Theme.Card,
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };
        codeBtn.FlatAppearance.BorderSize = 0;
        codeBtn.FlatAppearance.MouseOverBackColor = Theme.Hover;
        codeBtn.Click += async (_, _) =>
        {
            codeBtn.Enabled = false;
            codeBtn.Text = "Analyse du modpack…";
            try
            {
                var (pack, recognizedMods, recognizedShaders) = await PackShareService.ExportAsync(inst,
                    step => BeginInvoke(() => codeBtn.Text = step.Length > 40 ? step[..40] + "…" : step));
                string json = PackShareService.Serialize(pack);
                string code = GenerateShareCode(pack);

                using var resultDlg = new Form
                {
                    Text = Lang.T("Code de partage", "Share code"),
                    Size = new Size(520, 360),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    MaximizeBox = false,
                    BackColor = Theme.Panel
                };

                int totalItems = pack.Mods.Count + pack.Shaders.Count;
                int totalRecognized = recognizedMods + recognizedShaders;
                var infoLbl = new Label
                {
                    Text = $"📦 {pack.Mods.Count} mods ({recognizedMods} reconnus)  •  " +
                           $"🌈 {pack.Shaders.Count} shaders ({recognizedShaders} reconnus)  •  " +
                           $"📁 {pack.Configs.Count} configs  •  " +
                           $"🌍 {pack.Worlds.Count} fichiers monde  •  " +
                           $"🎨 {pack.ResourcePacks.Count} resource packs",
                    ForeColor = Theme.Text,
                    Font = new Font("Segoe UI", 8.5f),
                    Location = new Point(16, 12),
                    AutoSize = true
                };

                var codeLbl = new Label
                {
                    Text = Lang.T("Code de partage :", "Share code:"),
                    ForeColor = Theme.TextDim,
                    Font = new Font("Segoe UI", 8.5f),
                    Location = new Point(16, 40),
                    AutoSize = true
                };

                var codeBox = new TextBox
                {
                    Text = code,
                    Location = new Point(16, 60),
                    Size = new Size(470, 30),
                    BackColor = Theme.Card,
                    ForeColor = Theme.Accent,
                    Font = new Font("Consolas", 14f, FontStyle.Bold),
                    ReadOnly = true,
                    TextAlign = HorizontalAlignment.Center
                };

                var hintLbl = new Label
                {
                    Text = "Tes amis collent le lien ou le code dans « Importer un pack partagé ».",
                    ForeColor = Theme.TextDim,
                    Font = new Font("Segoe UI", 8f),
                    Location = new Point(16, 96),
                    AutoSize = true
                };

                var copyCodeBtn = new Button
                {
                    Text = "📋  Copier le code",
                    Size = new Size(470, 38),
                    Location = new Point(16, 124)
                };
                Theme.Apply(copyCodeBtn, primary: true);
                copyCodeBtn.Click += (_, _) =>
                {
                    try { Clipboard.SetText(code); copyCodeBtn.Text = "✓  Code copié !"; }
                    catch { }
                };

                var jsonBtn = new Button
                {
                    Text = "🔗  Copier le lien de partage",
                    Size = new Size(470, 34),
                    Location = new Point(16, 170)
                };
                Theme.Apply(jsonBtn);
                string shareLink = $"teamlauncher://import/{code}";
                jsonBtn.Click += (_, _) =>
                {
                    try { Clipboard.SetText(shareLink); jsonBtn.Text = "✓  Lien copié !"; }
                    catch { }
                };

                var closeBtn = new Button
                {
                    Text = "Fermer",
                    Size = new Size(470, 32),
                    Location = new Point(16, 250)
                };
                Theme.Apply(closeBtn);
                closeBtn.Click += (_, _) => resultDlg.Close();

                resultDlg.Controls.AddRange(new Control[] { infoLbl, codeLbl, codeBox, hintLbl, copyCodeBtn, jsonBtn, closeBtn });
                dlg.Close();
                resultDlg.ShowDialog(FindForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message, "Team Launcher");
            }
        };

        dlg.Controls.AddRange(new Control[] { lbl, zipBtn, codeBtn });
        dlg.ShowDialog(FindForm());
    }

    /// <summary>Génère un code court type CurseForge (ex: "IGSTtI-m") à partir des mods du pack.</summary>
    private static string GenerateShareCode(PackShareService.SharedPack pack)
    {
        // Encoder les hashes SHA1 de tous les fichiers (mods + shaders)
        var sb = new StringBuilder();
        foreach (var item in pack.Mods.Concat(pack.Shaders).Where(m => m.Sha1.Length > 0).OrderBy(m => m.Sha1))
            sb.Append(item.Sha1[..8]);

        if (sb.Length == 0)
        {
            // Fallback : hash du nom + loader
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes($"{pack.Name}:{pack.Loader}:{pack.McVersion}");
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(nameBytes));
            sb.Append(hash[..12]);
        }

        // Convertir en base62 court avec tiret
        string raw = sb.ToString();
        string code = ToBase62(raw);

        // Formater : 4-4 (ex: "ABCD-EFGH")
        if (code.Length > 8)
            code = code[..4] + "-" + code[4..8];
        else if (code.Length > 4)
            code = code[..4] + "-" + code[4..];

        return code.ToUpperInvariant();
    }

    private static string ToBase62(string hex)
    {
        const string chars = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        var result = new StringBuilder();
        for (int i = 0; i < hex.Length && result.Length < 16; i += 2)
        {
            int val = Convert.ToInt32(hex.Substring(i, 2), 16);
            result.Append(chars[val % chars.Length]);
            if (val / chars.Length > 0 && result.Length < 16)
                result.Append(chars[val / chars.Length % chars.Length]);
        }
        return result.ToString();
    }

    private void ImportFromCurseForge(object? sender, EventArgs e)
    {
        var found = InstanceTools.DetectCurseForgeInstances();
        if (found.Count == 0)
        {
            MessageBox.Show(
                "Aucune instance CurseForge détectée sur ce PC.\n" +
                "(Recherche dans : C:\\Users\\[toi]\\curseforge\\minecraft\\Instances)",
                "Team Launcher");
            return;
        }

        using var dlg = new Form
        {
            Text = Lang.T("Importer depuis CurseForge", "Import from CurseForge"),
            Size = new Size(440, 380),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            BackColor = Theme.Panel
        };
        var checkList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None
        };
        foreach (var (_, name) in found) checkList.Items.Add(name, true);

        var importBtn = new Button { Text = Lang.T("Importer la sélection", "Import selection"), Height = 44, Dock = DockStyle.Bottom };
        Theme.Apply(importBtn, primary: true);
        var infoLabel = new Label
        {
            Text = $"{found.Count} instance(s) CurseForge détectée(s)",
            ForeColor = Theme.TextDim, Dock = DockStyle.Bottom, Height = 28,
            TextAlign = ContentAlignment.MiddleCenter
        };

        importBtn.Click += (_, _) =>
        {
            int imported = 0;
            importBtn.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (var item in checkList.CheckedItems.Cast<string>().ToList())
                {
                    var match = found.FirstOrDefault(f => f.Name == item);
                    if (match.Path != null)
                    {
                        InstanceTools.ImportDirectory(match.Path, match.Name);
                        imported++;
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
            finally { Cursor = Cursors.Default; }

            RefreshData();
            dlg.Close();
            MessageBox.Show($"{imported} instance(s) importée(s) dans Team Launcher !", "Team Launcher");
        };

        dlg.Controls.Add(checkList);
        dlg.Controls.Add(importBtn);
        dlg.Controls.Add(infoLabel);
        checkList.BringToFront();
        dlg.ShowDialog(FindForm());
    }

    private void PackService_Duplicate(InstanceInfo inst) => InstanceTools.Duplicate(inst);

    private void DeleteInstance(InstanceInfo inst)
    {
        var result = MessageBox.Show(
            $"Supprimer l'instance « {inst.Name} » ?\n\n" +
            "Tous les fichiers (mods, mondes, configurations) seront définitivement supprimés.",
            "Team Launcher — Supprimer une instance",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes) return;

        try
        {
            // Supprimer le dossier de l'instance
            string instDir = Path.Combine(DataStore.InstancesRoot, inst.Id);
            if (Directory.Exists(instDir))
                Directory.Delete(instDir, recursive: true);

            // Supprimer de la liste et sauvegarder
            DataStore.Settings.Instances.Remove(inst);
            DataStore.Save();

            // Télémétrie
            TelemetryService.ReportInstanceDeleted(inst);

            RefreshData();
            Notifier.Show("Instance supprimée", $"« {inst.Name} » a été supprimée.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Erreur lors de la suppression :\n" + ex.Message,
                "Team Launcher");
        }
    }

    private void SharePack(object? sender, EventArgs e)
    {
        using var pick = new InstancePickDialog("Partager quelle instance ?", "Partager");
        if (pick.ShowDialog(FindForm()) != DialogResult.OK || pick.Selected == null) return;
        var inst = pick.Selected;

        Notifier.Show("Partage de pack", "Analyse des mods en cours…");
        Task.Run(async () =>
        {
            var (pack, recognizedMods, recognizedShaders) = await PackShareService.ExportAsync(inst,
                step => BeginInvoke(() => filterBox.PlaceholderText = step));
            string json = PackShareService.Serialize(pack);
            BeginInvoke(() =>
            {
                try { Clipboard.SetText(json); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); return; }
                RefreshData();
                filterBox.PlaceholderText = "Rechercher…";
                MessageBox.Show(
                    $"« {inst.Name} » copié dans le presse-papiers !\n\n" +
                    $"📦 {recognizedMods}/{pack.Mods.Count} mods reconnus\n" +
                    $"🌈 {recognizedShaders}/{pack.Shaders.Count} shaders reconnus\n" +
                    "Colle le texte dans Discord : les autres font « Importer un pack partagé ».",
                    "Team Launcher");
            });
        }).ContinueWith(t =>
        {
            BeginInvoke(() =>
            {
                filterBox.PlaceholderText = "Rechercher…";
                if (t.Exception != null)
                    MessageBox.Show("Échec du partage :\n" + t.Exception.InnerException?.Message,
                        "Team Launcher");
            });
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ImportSharedPack(object? sender, EventArgs e)
    {
        string text;
        try { text = Clipboard.GetText().Trim(); } catch { return; }
        if (text.Length == 0)
        {
            MessageBox.Show(
                "Presse-papiers vide : copie d'abord un pack partagé par un membre.",
                "Team Launcher");
            return;
        }

        Notifier.Show("Pack partagé", "Import en cours, ça peut prendre quelques minutes…");
        Task.Run(async () => await PackShareService.ImportAsync(text,
            step => BeginInvoke(() => filterBox.PlaceholderText = step)))
            .ContinueWith(t =>
            {
                RefreshData();
                filterBox.PlaceholderText = "Rechercher…";
                if (t.Exception != null)
                {
                    MessageBox.Show("Échec de l'import du pack :\n" +
                        t.Exception.InnerException?.Message, "Team Launcher");
                }
                else
                {
                    Notifier.Show("Pack importé",
                        $"« {t.Result.Name} » est prêt. Lance-le depuis l'accueil !");
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Import a shared pack by short code (deep link support).</summary>
    public void ImportByCode(string code)
    {
        if (code.Length == 0) return;
        Notifier.Show("Pack partagé", "Import en cours…");
        Task.Run(async () => await PackShareService.ImportAsync(code,
            step => BeginInvoke(() => filterBox.PlaceholderText = step)))
            .ContinueWith(t =>
            {
                RefreshData();
                filterBox.PlaceholderText = "Rechercher…";
                if (t.Exception != null)
                    MessageBox.Show("Échec de l'import :\n" + t.Exception.InnerException?.Message, "Team Launcher");
                else
                    Notifier.Show("Pack importé", $"« {t.Result.Name} » est prêt !");
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ExportPack(object? sender, EventArgs e)
    {
        using var pick = new InstancePickDialog("Exporter un modpack", "Exporter");
        if (pick.ShowDialog(FindForm()) != DialogResult.OK || pick.Selected == null) return;

        using var save = new SaveFileDialog
        {
            FileName = pick.Selected.Name + ".zip",
            Filter = "Archive zip|*.zip"
        };
        if (save.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            PackService.Export(pick.Selected, save.FileName);
            MessageBox.Show("Modpack exporté :\n" + save.FileName, "Team Launcher");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
    }

    private void ImportZip(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "Modpack zip|*.zip" };
        if (ofd.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            PackService.Import(ofd.FileName);
            RefreshData();
            MessageBox.Show("Modpack importé dans tes instances !", "Team Launcher");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
    }

    private void UpdateMods(object? sender, EventArgs e)
    {
        using var pick = new InstancePickDialog("Mettre à jour les mods (Modrinth)", "Rechercher les mises à jour");
        if (pick.ShowDialog(FindForm()) != DialogResult.OK || pick.Selected == null) return;

        var btn = (Button)sender!;
        btn.Enabled = false;
        var inst = pick.Selected;
        Task.Run(async () =>
        {
            try { return await ModUpdaterService.UpdateModsAsync(inst); }
            catch (Exception ex) { return "Erreur : " + ex.Message; }
        }).ContinueWith(t =>
        {
            BeginInvoke(() =>
            {
                btn.Enabled = true;
                MessageBox.Show(t.Result, "Team Launcher");
            });
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ImportInstance(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "Choisis le dossier de l'instance à importer (.minecraft, modpack exporté...)" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        string name = Path.GetFileName(dlg.SelectedPath.TrimEnd('\\')) ?? "Import";
        var inst = new InstanceInfo
        {
            Name = name,
            Description = "Instance importée",
            McVersion = "?",
            Loader = "?"
        };

        try
        {
            var target = Path.Combine(DataStore.InstancesRoot, inst.Id);
            Directory.CreateDirectory(target);
            foreach (var src in Directory.GetFiles(dlg.SelectedPath, "*", SearchOption.AllDirectories))
            {
                var dst = Path.Combine(target, Path.GetRelativePath(dlg.SelectedPath, src));
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }
            DataStore.Settings.Instances.Add(inst);
            DataStore.Save();
            RefreshData();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur pendant l'import : {ex.Message}", "Team Launcher");
        }
    }

    public void RefreshData()
    {
        string filter = filterBox.Text.Trim();
        cardsFlow.SuspendLayout();
        cardsFlow.Controls.Clear();

        var instances = DataStore.Settings.Instances
            .Where(i => filter.Length == 0 || i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (instances.Count == 0)
        {
            cardsFlow.Visible = false;
            emptyLabel.Visible = true;
            emptyLabel.Text = filter.Length > 0
                ? "Aucune instance ne correspond à cette recherche."
                : "Pas encore d'instance. Clique sur « Nouvelle instance » pour commencer.";
        }
        else
        {
            cardsFlow.Visible = true;
            emptyLabel.Visible = false;
            foreach (var inst in instances)
                cardsFlow.Controls.Add(MakeCard(inst));
        }

        cardsFlow.ResumeLayout();
    }

    private Panel MakeCard(InstanceInfo inst)
    {
        const int cardW = 210, cardH = 220;

        var card = new Panel
        {
            Width = cardW,
            Height = cardH,
            BackColor = Theme.Card,
            Margin = new Padding(0, 0, 12, 12),
            Cursor = Cursors.Hand
        };
        Theme.Round(card, 6);

        // ---- Image / placeholder en haut ----
        var imgPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 100,
            BackColor = ControlPaint.Dark(Theme.Card, 0.02f),
            Cursor = Cursors.Hand
        };

        PictureBox? thumb = null;
        if (!string.IsNullOrWhiteSpace(inst.ImagePath) && File.Exists(inst.ImagePath))
        {
            try
            {
                using var fullImg = Image.FromFile(inst.ImagePath);
                var thumbBmp = new Bitmap(210, 110);
                using (var g = Graphics.FromImage(thumbBmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    g.DrawImage(fullImg, 0, 0, 210, 110);
                }
                thumb = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Image = thumbBmp,
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                imgPanel.Controls.Add(thumb);
            }
            catch { }
        }

        if (thumb == null)
        {
            var placeholder = new Label
            {
                Text = inst.Name.Length > 0 ? inst.Name[..1].ToUpper() : "?",
                ForeColor = Theme.AccentDim,
                Font = PlaceholderFont,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            imgPanel.Controls.Add(placeholder);
        }

        // ---- Crayon (modifier) en haut à droite ----
        var pencil = new Button
        {
            Text = "✎",
            Size = new Size(26, 26),
            Location = new Point(cardW - 32, 4),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f),
            ForeColor = Theme.TextDim,
            BackColor = Color.FromArgb(160, Theme.Card),
            Cursor = Cursors.Hand
        };
        pencil.FlatAppearance.BorderSize = 0;
        pencil.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, Theme.Card);
        pencil.Click += (_, _) =>
        {
            using var dlg = new InstanceEditDialog(inst);
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK) RefreshData();
        };
        imgPanel.Controls.Add(pencil);
        pencil.BringToFront();

        // ---- Bouton partager ----
        var shareBtn = new Button
        {
            Text = "🔗",
            Size = new Size(26, 26),
            Location = new Point(cardW - 60, 4),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Theme.TextDim,
            BackColor = Color.FromArgb(160, Theme.Card),
            Cursor = Cursors.Hand
        };
        shareBtn.FlatAppearance.BorderSize = 0;
        shareBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, Theme.Card);
        shareBtn.Click += (_, _) => ShareInstance(inst);
        imgPanel.Controls.Add(shareBtn);
        shareBtn.BringToFront();

        // ---- Bouton supprimer (X) ----
        var deleteBtn = new Button
        {
            Text = "✕",
            Size = new Size(26, 26),
            Location = new Point(cardW - 88, 4),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Theme.TextDim,
            BackColor = Color.FromArgb(160, Theme.Card),
            Cursor = Cursors.Hand
        };
        deleteBtn.FlatAppearance.BorderSize = 0;
        deleteBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 50, 50);
        deleteBtn.MouseEnter += (_, _) => deleteBtn.ForeColor = Color.White;
        deleteBtn.MouseLeave += (_, _) => deleteBtn.ForeColor = Theme.TextDim;
        deleteBtn.Click += (_, _) => DeleteInstance(inst);
        imgPanel.Controls.Add(deleteBtn);
        deleteBtn.BringToFront();

        // ---- Nom ----
        var nameLabel = new Label
        {
            Text = inst.Name,
            ForeColor = Theme.Text,
            Font = CardNameFont,
            Location = new Point(12, 106),
            AutoSize = true,
            MaximumSize = new Size(cardW - 24, 0),
            Cursor = Cursors.Hand
        };

        // ---- Métadonnées ----
        var metaLabel = new Label
        {
            Text = $"{inst.Loader} • Minecraft {inst.McVersion}",
            ForeColor = Theme.TextDim,
            Font = CardMetaFont,
            Location = new Point(12, 128),
            AutoSize = true,
            Cursor = Cursors.Hand
        };

        // ---- Compteurs ----
        string instDir = Path.Combine(DataStore.InstancesRoot, inst.Id);
        int modsCount = CountFiles(instDir, "mods", "*.jar");
        int mapsCount = CountFolders(instDir, "saves");

        var countsLabel = new Label
        {
            Text = $"{modsCount} mod(s)  •  {mapsCount} carte(s)  •  {inst.Launches} lancé(s)",
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 7.5f),
            Location = new Point(12, 146),
            AutoSize = true,
            Cursor = Cursors.Hand
        };

        // ---- Bouton Jouer ----
        var playBtn = new Button
        {
            Text = "▶  Jouer",
            Location = new Point(12, 174),
            Size = new Size(cardW - 24, 34)
        };
        Theme.Apply(playBtn, primary: true);
        playBtn.Click += (_, _) => GameLauncher.Play(inst);

        card.Controls.Add(imgPanel);
        card.Controls.Add(nameLabel);
        card.Controls.Add(metaLabel);
        card.Controls.Add(countsLabel);
        card.Controls.Add(playBtn);

        // Clic sur la carte → ouvrir la page détail
        void OpenDetail()
        {
            AppEvents.PendingDetailId = inst.Id;
            AppEvents.NavigateTo("detail");
        }

        card.Click += (_, _) => OpenDetail();
        imgPanel.Click += (_, _) => OpenDetail();
        if (thumb != null) thumb.Click += (_, _) => OpenDetail();
        nameLabel.Click += (_, _) => OpenDetail();
        metaLabel.Click += (_, _) => OpenDetail();
        countsLabel.Click += (_, _) => OpenDetail();

        return card;
    }

    private static int CountFiles(string instDir, string subDir, string pattern)
    {
        try
        {
            string path = Path.Combine(instDir, subDir);
            if (!Directory.Exists(path)) return 0;
            int count = 0;
            foreach (var _ in Directory.EnumerateFiles(path, pattern)) count++;
            return count;
        }
        catch { return 0; }
    }

    private static int CountFolders(string instDir, string subDir)
    {
        try
        {
            string path = Path.Combine(instDir, subDir);
            if (!Directory.Exists(path)) return 0;
            int count = 0;
            foreach (var _ in Directory.EnumerateDirectories(path)) count++;
            return count;
        }
        catch { return 0; }
    }
}

public class CreateInstanceDialog : Form
{
    private readonly TextBox nameBox = new() { Dock = DockStyle.Top, Font = new Font("Segoe UI", 11f) };
    private readonly TextBox descBox = new() { Dock = DockStyle.Top, Font = new Font("Segoe UI", 10f) };
    private readonly ComboBox loaderBox = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox versionBox = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private string imagePath = "";

    public CreateInstanceDialog()
    {
        Text = Lang.T("Créer une instance", "Create instance");
        Size = new Size(480, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Panel;

        Theme.ApplyInput(nameBox);
        Theme.ApplyInput(descBox);
        Theme.ApplyInput(loaderBox);
        Theme.ApplyInput(versionBox);
        loaderBox.Items.AddRange(new object[] { "Vanilla", "Forge", "Fabric", "NeoForge", "Quilt" });
        loaderBox.SelectedIndex = 0;

        var imgBtn = new Button { Text = Lang.T("Choisir une image...", "Choose an image..."), Height = 34, Dock = DockStyle.Top };
        Theme.Apply(imgBtn);
        imgBtn.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
            if (ofd.ShowDialog(this) == DialogResult.OK)
                imagePath = ofd.FileName;
        };

        var ok = new Button { Text = Lang.T("Créer l'instance", "Create instance"), Height = 44, Dock = DockStyle.Bottom };
        Theme.Apply(ok, primary: true);
        ok.Click += (_, _) => Create();

        var cancel = new Button { Text = "Annuler", Height = 36, Dock = DockStyle.Bottom };
        Theme.Apply(cancel);
        cancel.Click += (_, _) => Close();

        Controls.Add(ok);
        Controls.Add(cancel);

        var fieldsHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 14, 20, 8), AutoScroll = true };
        AddLabeled(fieldsHost, "Nom de l'instance", nameBox);
        AddLabeled(fieldsHost, "Description", descBox);
        AddLabeled(fieldsHost, "Loader", loaderBox);
        AddLabeled(fieldsHost, "Version Minecraft Java", versionBox);
        fieldsHost.Controls.Add(imgBtn);
        imgBtn.BringToFront();

        Controls.Add(fieldsHost);
        fieldsHost.BringToFront();

        _ = LoadVersionsAsync();
    }

    private static void AddLabeled(Panel host, string label, Control input)
    {
        host.Controls.Add(new Label
        {
            Text = label,
            ForeColor = Theme.TextDim,
            AutoSize = true,
            Dock = DockStyle.Top,
            Height = 24
        });
        host.Controls.Add(input);
        input.Dock = DockStyle.Top;
        input.BringToFront();
    }

    private async Task LoadVersionsAsync()
    {
        versionBox.Items.Add("chargement...");
        versionBox.SelectedIndex = 0;
        try
        {
            var versions = await MojangApi.GetReleasesAsync();
            versionBox.Items.Clear();
            foreach (var v in versions) versionBox.Items.Add(v);
            versionBox.SelectedIndex = Math.Min(1, versionBox.Items.Count - 1);
        }
        catch
        {
            versionBox.Items.Clear();
            versionBox.Items.Add("hors-ligne");
            versionBox.SelectedIndex = 0;
        }
    }

    private void Create()
    {
        var name = nameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("Donne un nom à ton instance.", "Team Launcher");
            return;
        }

        var inst = new InstanceInfo
        {
            Name = name,
            Description = descBox.Text.Trim(),
            Loader = loaderBox.SelectedItem?.ToString() ?? "Vanilla",
            McVersion = versionBox.SelectedItem?.ToString() ?? "?",
            ImagePath = imagePath
        };

        DataStore.Settings.Instances.Add(inst);
        DataStore.Save();
        DialogResult = DialogResult.OK;
        Close();
    }
}
