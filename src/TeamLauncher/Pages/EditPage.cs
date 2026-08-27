namespace TeamLauncher;

public class EditPage : UserControl, IRefreshable
{
    private readonly ComboBox picker = new();
    private readonly TextBox nameBox = new();
    private readonly TextBox descBox = new();
    private readonly TextBox imageBox = new() { ReadOnly = true };
    private readonly ComboBox loaderBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox versionBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListBox backupList = new();
    private readonly NumericUpDown ramBox = new() { Minimum = 0, Maximum = 32, Value = 0, Width = 90 };
    private readonly TextBox notesBox = new() { Width = 420, Height = 60, Multiline = true };
    private string imagePath = "";

    public EditPage()
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

        var title = new Label
        {
            Text = "Édition",
            ForeColor = Theme.Text,
            Font = Theme.Title,
            AutoSize = true
        };
        var hint = new Label
        {
            Text = "Modifie la carte d'une instance : nom, image, description.",
            ForeColor = Theme.TextDim,
            AutoSize = true
        };

        picker.DropDownStyle = ComboBoxStyle.DropDownList;
        picker.Width = 320;
        picker.SelectedIndexChanged += (_, _) => LoadSelected();

        nameBox.Width = 420;
        nameBox.Font = new Font("Segoe UI", 11f);
        descBox.Width = 420;
        descBox.Height = 70;
        descBox.Multiline = true;
        imageBox.Width = 420;

        var imgBtn = new Button { Text = "Choisir une image...", Width = 200, Height = 36 };
        Theme.Apply(imgBtn);
        imgBtn.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
            if (ofd.ShowDialog(FindForm()) == DialogResult.OK)
            {
                imagePath = ofd.FileName;
                imageBox.Text = imagePath;
            }
        };

        var save = new Button { Text = "Enregistrer les modifications", Width = 260, Height = 42 };
        Theme.Apply(save, primary: true);
        save.Margin = new Padding(0, 16, 0, 0);
        save.Click += (_, _) => Save();

        root.Controls.Add(title);
        root.Controls.Add(hint);
        root.Controls.Add(EditLabel("Instance à modifier"));
        root.Controls.Add(picker);
        root.Controls.Add(EditLabel("Nom"));
        root.Controls.Add(nameBox);
        root.Controls.Add(EditLabel("Description"));
        root.Controls.Add(descBox);
        root.Controls.Add(EditLabel("Loader"));
        root.Controls.Add(loaderBox);
        root.Controls.Add(EditLabel("Version Minecraft Java (le jeu se lance EXACTEMENT sur cette version)"));
        root.Controls.Add(versionBox);
        root.Controls.Add(EditLabel("Mémoire allouée à CETTE instance en Go (0 = réglage global des Paramètres)"));
        root.Controls.Add(ramBox);
        root.Controls.Add(EditLabel("Notes personnelles (affichées au survol de la carte)"));
        root.Controls.Add(notesBox);
        root.Controls.Add(EditLabel("Image de la carte"));
        root.Controls.Add(imageBox);
        root.Controls.Add(imgBtn);
        root.Controls.Add(save);

        // ---- Sauvegardes des mondes ----
        root.Controls.Add(EditLabel("Sauvegardes automatiques des mondes (créées à la fermeture du jeu)"));
        backupList.Width = 420;
        backupList.Height = 90;

        var backupRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        var restoreBtn = new Button { Text = "Restaurer", Width = 130, Height = 34 };
        Theme.Apply(restoreBtn);
        restoreBtn.Click += (_, _) =>
        {
            var inst = Selected;
            if (inst == null || backupList.SelectedItem == null) return;
            if (MessageBox.Show("Remplacer les mondes actuels par cette sauvegarde ?",
                    "Team Launcher", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try
            {
                BackupService.Restore(inst.Id, ((BackupItem)backupList.SelectedItem!).Path);
                MessageBox.Show("Mondes restaurés.", "Team Launcher");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
        };
        var deleteBtn = new Button { Text = "Supprimer", Width = 110, Height = 34, Margin = new Padding(8, 0, 0, 0) };
        Theme.Apply(deleteBtn);
        deleteBtn.Click += (_, _) =>
        {
            if (backupList.SelectedItem == null) return;
            try
            {
                BackupService.Delete(((BackupItem)backupList.SelectedItem!).Path);
                RefreshBackups();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Team Launcher"); }
        };
        backupRow.Controls.Add(restoreBtn);
        backupRow.Controls.Add(deleteBtn);

        root.Controls.Add(backupList);
        root.Controls.Add(backupRow);

        loaderBox.Items.AddRange(new object[] { "Vanilla", "Forge", "Fabric", "NeoForge", "Quilt" });

        Controls.Add(root);

        _ = LoadVersionsAsync(); // toutes les versions release, dont 1.12.2
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            var versions = await MojangApi.GetReleasesAsync();
            versionBox.Items.Clear();
            foreach (var v in versions) versionBox.Items.Add(v);
            LoadSelected();
        }
        catch { }
    }

    private static Label EditLabel(string text) => new()
    {
        Text = text,
        ForeColor = Theme.TextDim,
        AutoSize = true,
        Margin = new Padding(0, 12, 0, 2)
    };

    private InstanceInfo? Selected =>
        picker.SelectedItem is InstanceWrapper w ? DataStore.Settings.Instances.FirstOrDefault(i => i.Id == w.Id) : null;

    public void RefreshData()
    {
        picker.Items.Clear();
        foreach (var i in DataStore.Settings.Instances)
            picker.Items.Add(new InstanceWrapper(i));

        // présélection demandée via clic droit sur une carte
        var pending = AppEvents.PendingEditId;
        AppEvents.PendingEditId = null;
        int idx = pending == null ? -1 :
            picker.Items.Cast<InstanceWrapper>().ToList().FindIndex(w => w.Id == pending);

        if (picker.Items.Count > 0)
            picker.SelectedIndex = idx >= 0 ? idx : (picker.SelectedIndex < 0 ? 0 : picker.SelectedIndex);
        else
            nameBox.Text = descBox.Text = imageBox.Text = "";
        LoadSelected();
    }

    private void LoadSelected()
    {
        var inst = Selected;
        if (inst == null) return;
        nameBox.Text = inst.Name;
        descBox.Text = inst.Description;
        imagePath = inst.ImagePath;
        imageBox.Text = imagePath.Length == 0 ? "(aucune image)" : imagePath;
        loaderBox.SelectedItem = loaderBox.Items.Contains(inst.Loader) ? inst.Loader : "Vanilla";
        versionBox.SelectedItem = versionBox.Items.Contains(inst.McVersion) ? inst.McVersion : null;
        ramBox.Value = Math.Clamp(inst.MaxRamGb, 0, 32);
        notesBox.Text = inst.Notes;
        RefreshBackups();
    }

    private void RefreshBackups()
    {
        var inst = Selected;
        backupList.Items.Clear();
        if (inst == null) return;
        foreach (var (file, date) in BackupService.List(inst.Id))
            backupList.Items.Add(new BackupItem(file, date));
    }

    private sealed record BackupItem(string Path, DateTime Date)
    {
        public override string ToString() => Date.ToString("dd/MM/yyyy HH:mm");
    }

    private void Save()
    {
        var inst = Selected;
        if (inst == null) return;
        inst.Name = nameBox.Text.Trim().Length > 0 ? nameBox.Text.Trim() : inst.Name;
        inst.Description = descBox.Text.Trim();
        inst.ImagePath = imagePath;
        inst.Loader = loaderBox.SelectedItem?.ToString() ?? inst.Loader;
        inst.McVersion = versionBox.SelectedItem?.ToString() ?? inst.McVersion;
        inst.MaxRamGb = (int)ramBox.Value;
        inst.Notes = notesBox.Text.Trim();
        DataStore.Save();
        RefreshData();
        MessageBox.Show(
            "Carte mise à jour.\n\n" +
            $"Cette instance lancera désormais Minecraft {inst.Loader} {inst.McVersion}.",
            "Team Launcher");
    }

    private sealed class InstanceWrapper(InstanceInfo instance)
    {
        public string Id { get; } = instance.Id;
        public override string ToString() => instance.Name;
    }
}

