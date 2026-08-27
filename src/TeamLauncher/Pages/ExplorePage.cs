using System.Diagnostics;

namespace TeamLauncher;

/// <summary>
/// Exploration façon boutique : cartes riches avec icône, description,
/// badges de loaders et bouton d'installation directe vers une instance.
/// Sources : Modrinth et CurseForge.
/// </summary>
public class ExplorePage : UserControl, IRefreshable
{
    private sealed record SearchHit(string Source, string Key, string Title, long Downloads,
        string Description, string Loaders, string IconUrl);

    private readonly TextBox searchBox = new();
    private readonly ComboBox typeBox = new();
    private readonly ComboBox sourceBox = new();
    private readonly FlowLayoutPanel resultsFlow = new();

    public ExplorePage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(24, 16, 24, 16)
        };

        root.Controls.Add(new Label
        {
            Text = "Exploration", ForeColor = Theme.Text,
            Font = Theme.Title,
            AutoSize = true
        });
        root.Controls.Add(new Label
        {
            Text = "Mods, modpacks et shaders de Modrinth ET CurseForge — Forge, Fabric, NeoForge, Quilt...",
            ForeColor = Theme.TextDim, AutoSize = true
        });

        var bar = new Panel { Height = 44, Width = 920, Margin = new Padding(0, 10, 0, 0) };
        searchBox.SetBounds(0, 4, 360, 30);
        searchBox.Font = new Font("Segoe UI", 11f);
        sourceBox.DropDownStyle = ComboBoxStyle.DropDownList;
        sourceBox.SetBounds(370, 4, 130, 30);
        sourceBox.Items.AddRange(new object[] { "Modrinth", "CurseForge" });
        sourceBox.SelectedIndex = 0;
        typeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        typeBox.SetBounds(510, 4, 140, 30);
        typeBox.Items.AddRange(new object[] { "Modpacks", "Mods", "Shaders" });
        typeBox.SelectedIndex = 0;
        var go = new Button { Text = "Rechercher", Size = new Size(130, 32), Location = new Point(660, 2) };
        Theme.Apply(go, primary: true);
        go.Click += async (_, _) => await SearchAsync();
        bar.Controls.AddRange(new Control[] { searchBox, sourceBox, typeBox, go });

        resultsFlow.WrapContents = true;
        resultsFlow.AutoSize = false;
        resultsFlow.Height = 500;
        resultsFlow.Width = 920;
        resultsFlow.BackColor = Theme.Bg;

        root.Controls.Add(bar);
        root.Controls.Add(resultsFlow);
        Controls.Add(root);

        Resize += (_, _) =>
        {
            bar.Width = Math.Max(600, Width - 48);
            resultsFlow.Width = Math.Max(600, Width - 48);
        };

        _ = SearchAsync();
    }

    public void RefreshData() { }

    private async Task SearchAsync()
    {
        bool isCf = sourceBox.SelectedItem?.ToString() == "CurseForge";
        string category = typeBox.SelectedItem?.ToString() ?? "Modpacks";
        ShowMessage(isCf ? "Recherche sur CurseForge..." : "Recherche sur Modrinth...");
        try
        {
            List<SearchHit> hits;
            if (isCf)
            {
                int classId = category switch
                {
                    "Mods" => CurseForgeApi.ClassMods,
                    "Shaders" => CurseForgeApi.ClassShaders,
                    _ => CurseForgeApi.ClassModpacks
                };
                hits = (await CurseForgeApi.SearchAsync(searchBox.Text.Trim(), classId))
                    .Select(h => new SearchHit("curseforge", h.ProjectId.ToString(), h.Title,
                        h.Downloads, h.Description, h.Loaders, h.IconUrl))
                    .ToList();
            }
            else
            {
                var typeMap = new Dictionary<string, string>
                {
                    ["Modpacks"] = "modpack",
                    ["Mods"] = "mod",
                    ["Shaders"] = "shader"
                };
                hits = (await ModrinthApi.SearchAsync(searchBox.Text.Trim(), typeMap[category]))
                    .Select(h => new SearchHit("modrinth", h.Slug, h.Title, h.Downloads,
                        h.Description, h.Loaders, h.IconUrl))
                    .ToList();
            }
            resultsFlow.SuspendLayout();
            resultsFlow.Controls.Clear();
            foreach (var h in hits)
                resultsFlow.Controls.Add(MakeCard(h));
            if (hits.Count == 0)
                ShowMessage("Aucun résultat.");
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message.Contains("Clé API")
                ? ex.Message
                : "Impossible de joindre la source : " + ex.Message);
        }
    }

    private void ShowMessage(string text)
    {
        resultsFlow.Controls.Clear();
        var label = new Label
        {
            Text = text, ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 11f), AutoSize = true,
            Margin = new Padding(4, 16, 0, 0)
        };
        resultsFlow.Controls.Add(label);
    }

    // ---------------- carte résultat ----------------

    private Panel MakeCard(SearchHit h)
    {
        var card = new Panel
        {
            Size = new Size(440, 150),
            BackColor = Theme.Card,
            Margin = new Padding(0, 0, 14, 14),
            Tag = h
        };
        Theme.Blockify(card);

        // icône du projet
        var icon = new PictureBox
        {
            Size = new Size(56, 56),
            Location = new Point(12, 12),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = ControlPaint.Dark(Theme.Card, 0.05f)
        };
        LoadIconAsync(icon, h.IconUrl);

        var title = new Label
        {
            Text = h.Title,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            AutoEllipsis = true,
            Location = new Point(80, 10),
            Size = new Size(240, 24)
        };
        title.Click += (_, _) => OpenProjectPage(h);

        var sourceTag = new Label
        {
            Text = h.Source == "curseforge" ? "CF" : "MR",
            ForeColor = Theme.Bg,
            BackColor = h.Source == "curseforge" ? Color.FromArgb(245, 85, 40) : Color.FromArgb(30, 180, 120),
            Font = new Font("Consolas", 8f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(400, 12),
            Padding = new Padding(3, 1, 3, 1)
        };

        var downloads = new Label
        {
            Text = $"⬇ {h.Downloads:N0}",
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(80, 36),
            Size = new Size(120, 18)
        };

        var desc = new Label
        {
            Text = string.IsNullOrWhiteSpace(h.Description) ? "(pas de description)" : h.Description,
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(12, 76),
            Size = new Size(414, 32),
            AutoEllipsis = false
        };

        var loaders = new Label
        {
            Text = string.IsNullOrEmpty(h.Loaders) ? "" : h.Loaders.ToUpperInvariant(),
            ForeColor = Theme.Accent,
            Font = new Font("Consolas", 8.5f, FontStyle.Bold),
            Location = new Point(12, 114),
            Size = new Size(220, 18)
        };

        var installBtn = new Button
        {
            Text = "⤓ Installer",
            Size = new Size(120, 34),
            Location = new Point(200, 108)
        };
        Theme.Apply(installBtn, primary: true);
        installBtn.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        installBtn.Click += async (_, _) =>
        {
            installBtn.Enabled = false;
            installBtn.Text = "…";
            try
            {
                await InstallHitAsync(h, installBtn);
                installBtn.Text = "✔ Installé";
            }
            catch (Exception ex)
            {
                installBtn.Text = "⤓ Installer";
                MessageBox.Show(ex.Message, "Team Launcher",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        var openBtn = new Button
        {
            Text = "↗",
            Size = new Size(38, 34),
            Location = new Point(328, 108)
        };
        Theme.Apply(openBtn);
        openBtn.Font = new Font("Segoe UI", 10f);
        openBtn.Click += (_, _) => OpenProjectPage(h);

        card.Controls.AddRange(new Control[]
        {
            title, downloads, desc, loaders, installBtn, openBtn, icon, sourceTag
        });
        return card;
    }

    private static void OpenProjectPage(SearchHit h)
    {
        try
        {
            string url = h.Source == "curseforge"
                ? $"https://www.curseforge.com/minecraft/search?search={Uri.EscapeDataString(h.Title)}"
                : $"https://modrinth.com/project/{h.Key}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private static async void LoadIconAsync(PictureBox box, string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var bytes = await http.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            var img = Image.FromStream(ms);
            box.BeginInvoke(() =>
            {
                if (!box.IsDisposed) box.Image = img;
            });
        }
        catch { }
    }

    // ---------------- installation ----------------

    private async Task InstallHitAsync(SearchHit h, Button btn)
    {
        using var pick = new InstancePickDialog($"Installer « {h.Title} » dans quelle instance ?", "Installer");
        if (pick.ShowDialog(FindForm()) != DialogResult.OK || pick.Selected == null)
            throw new Exception("Installation annulée.");
        var inst = pick.Selected;

        string category = typeBox.SelectedItem?.ToString() ?? "Mods";
        btn.Enabled = false;
        btn.Text = "Téléchargement...";

        if (h.Source == "modrinth")
            await InstallModrinthAsync(h.Key, inst, category);
        else
            await InstallCurseForgeAsync(int.Parse(h.Key), inst, category);

        MessageBox.Show($"« {h.Title} » a été installé dans « {inst.Name} » !",
            "Team Launcher");
    }

    private static async Task InstallModrinthAsync(string slug, InstanceInfo inst, string category)
    {
        if (category == "Shaders")
        {
            await ModrinthApi.DownloadProjectFileAsync(slug,
                Path.Combine(DataStore.InstancesRoot, inst.Id, "shaderpacks"));
        }
        else if (category == "Modpacks")
        {
            string tempFile = Path.Combine(Path.GetTempPath(),
                "teamlauncher-" + slug + ".mrpack");
            await ModrinthApi.DownloadProjectFileAsync(slug, Path.GetDirectoryName(tempFile)!);
            PackService.Import(tempFile);
        }
        else
        {
            string loader = ResolveLoader(inst);
            string mcVersion = await ResolveMcVersion(inst);
            await ModrinthApi.DownloadProjectFileAsync(slug,
                Path.Combine(DataStore.InstancesRoot, inst.Id, "mods"),
                loader, mcVersion);
        }
    }

    private static async Task InstallCurseForgeAsync(int projectId, InstanceInfo inst, string category)
    {
        var files = await CurseForgeApi.GetFilesAsync(projectId);
        if (files.Count == 0)
            throw new Exception("Aucun fichier trouvé sur CurseForge pour ce projet.");

        string mcVersion = await ResolveMcVersion(inst);

        if (category == "Modpacks")
        {
            // télécharge le .zip du modpack puis l'importe comme instance complète
            string tempFile = Path.Combine(Path.GetTempPath(),
                $"teamlauncher-cf-{projectId}.zip");
            var packFile = files.First(); // le plus récent
            await CurseForgeApi.DownloadFileAsync(packFile, Path.GetDirectoryName(tempFile)!);
            string downloaded = Path.Combine(Path.GetDirectoryName(tempFile)!,
                SafeFileName(packFile.FileName));
            await CfPackImporter.ImportAsync(downloaded, _ => { });
            try { File.Delete(downloaded); } catch { }
            return;
        }

        // mods / shaders : filtre par version du jeu (+ loader pour les mods)
        string loader = category == "Mods" ? ResolveLoader(inst) : "";
        var compatible = files.Where(f =>
                f.GameVersions.Contains(mcVersion, StringComparer.OrdinalIgnoreCase) &&
                (loader.Length == 0 || f.Loaders.Count == 0 ||
                 f.Loaders.Any(l => l.Equals(loader, StringComparison.OrdinalIgnoreCase))))
            .ToList();
        if (compatible.Count == 0)
            throw new Exception(
                $"Aucun fichier CurseForge compatible {inst.Loader} {mcVersion}.\n" +
                "Vérifie la version de l'instance ou choisis un autre mod.");

        string destDir = Path.Combine(DataStore.InstancesRoot, inst.Id,
            category == "Shaders" ? "shaderpacks" : "mods");
        await CurseForgeApi.DownloadFileAsync(compatible[0], destDir);
    }

    internal static string ResolveLoader(InstanceInfo inst) =>
        inst.Loader.ToLowerInvariant() switch
        {
            "forge" or "fabric" or "neoforge" => inst.Loader.ToLowerInvariant(),
            "quilt" => "fabric",
            _ => throw new Exception(
                "Définis d'abord le loader de l'instance (page Édition) : Forge ou Fabric.")
        };

    internal static Task<string> ResolveMcVersion(InstanceInfo inst) =>
        Task.FromResult(inst.McVersion is "latest" or "?" or "" or null
            ? MojangApi.GetReleasesAsync().GetAwaiter().GetResult()[0]
            : inst.McVersion);

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
