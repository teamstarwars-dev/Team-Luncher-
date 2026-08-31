using System.IO.Compression;

namespace TeamLauncher;

/// <summary>
/// Dialog : détecte les mondes présents dans CurseForge (même nom de pack que
/// l'instance passée en paramètre) et propose de les importer dans l'instance
/// du launcher.
/// </summary>
public class WorldImportDialog : Form
{
    private readonly InstanceInfo inst;
    private readonly ListView list = new();
    private readonly Button importBtn = new();
    private readonly Button closeBtn = new();
    private readonly Label headerLabel = new();
    private List<WorldSyncService.WorldSnapshot> candidates = new();

    public WorldImportDialog(InstanceInfo inst)
    {
        this.inst = inst;
        Text = $"Importer des mondes — {inst.Name}";
        Size = new Size(720, 460);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Panel;

        headerLabel.Text =
            "Mondes trouvés dans CurseForge pour ce modpack.\n" +
            "Coche ceux que tu veux importer dans ton instance Team Launcher.\n" +
            "(Un backup automatique du monde existant est créé s'il y a un conflit.)";
        headerLabel.ForeColor = Theme.TextDim;
        headerLabel.Font = new Font("Segoe UI", 9f);
        headerLabel.Location = new Point(20, 14);
        headerLabel.Size = new Size(660, 60);

        list.View = View.Details;
        list.CheckBoxes = true;
        list.FullRowSelect = true;
        list.GridLines = false;
        list.BackColor = Theme.Card;
        list.ForeColor = Theme.Text;
        list.BorderStyle = BorderStyle.None;
        list.Font = new Font("Segoe UI", 9f);
        list.Location = new Point(20, 84);
        list.Size = new Size(660, 290);
        list.Columns.Add("Importer", 60);
        list.Columns.Add("Monde", 200);
        list.Columns.Add("Dernière partie", 140);
        list.Columns.Add("Taille", 90);
        list.Columns.Add("Emplacement CurseForge", 200);

        importBtn.Text = "Importer la sélection";
        importBtn.Size = new Size(180, 34);
        Theme.Apply(importBtn, primary: true);
        importBtn.Location = new Point(500, 388);
        importBtn.Click += (_, _) => DoImport();

        closeBtn.Text = "Fermer";
        closeBtn.Size = new Size(90, 34);
        Theme.Apply(closeBtn);
        closeBtn.Location = new Point(20, 388);
        closeBtn.Click += (_, _) => Close();

        Controls.Add(headerLabel);
        Controls.Add(list);
        Controls.Add(importBtn);
        Controls.Add(closeBtn);

        Load += (_, _) => Populate();
    }

    private void Populate()
    {
        list.Items.Clear();
        string cfRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "curseforge", "minecraft", "Instances", inst.Name);
        if (!Directory.Exists(cfRoot))
        {
            list.Items.Add(new ListViewItem(new[]
            {
                "",
                $"Aucun dossier CurseForge trouvé pour « {inst.Name} ».",
                "", "", cfRoot
            }) { ForeColor = Theme.TextDim });
            importBtn.Enabled = false;
            return;
        }

        var res = WorldSyncService.CompareAll()
            .FirstOrDefault(r => string.Equals(r.CurseForgeInstanceName, inst.Name,
                StringComparison.OrdinalIgnoreCase));
        if (res == null || res.NewerWorlds.Count == 0)
        {
            list.Items.Add(new ListViewItem(new[]
            {
                "",
                "Aucun monde CurseForge plus récent à importer.",
                "", "", ""
            }) { ForeColor = Theme.TextDim });
            importBtn.Enabled = false;
            return;
        }

        candidates = res.NewerWorlds;
        foreach (var w in candidates)
        {
            var item = new ListViewItem(new[]
            {
                "✔",
                w.DisplayName,
                w.LevelLastPlayed?.ToString("dd/MM/yyyy HH:mm") ?? w.LastModified.ToString("dd/MM/yyyy HH:mm"),
                w.SizeBytes / 1024.0 / 1024.0 > 1 ? $"{w.SizeBytes / 1024.0 / 1024.0:0.#} Mo" : $"{w.SizeBytes / 1024.0:0.#} Ko",
                w.FullPath
            })
            {
                Tag = w,
                Checked = true,
                ForeColor = Theme.Text
            };
            list.Items.Add(item);
        }
    }

    private void DoImport()
    {
        var selected = list.Items.Cast<ListViewItem>()
            .Where(i => i.Checked && i.Tag is WorldSyncService.WorldSnapshot)
            .Select(i => (WorldSyncService.WorldSnapshot)i.Tag!)
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show("Coche au moins un monde à importer.", "Team Launcher");
            return;
        }

        int ok = 0, errors = 0;
        foreach (var w in selected)
        {
            try
            {
                WorldSyncService.ImportWorld(w, inst);
                ok++;
            }
            catch (Exception ex)
            {
                errors++;
                MessageBox.Show($"Échec d'import de « {w.DisplayName} » :\n{ex.Message}",
                    "Team Launcher");
            }
        }

        MessageBox.Show(
            $"{ok} monde(s) importé(s)" + (errors > 0 ? $", {errors} erreur(s)." : "."),
            "Team Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);

        DialogResult = DialogResult.OK;
        Close();
    }
}
