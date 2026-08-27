namespace TeamLauncher;

/// <summary>Édition rapide d'une instance directement depuis sa carte (bouton crayon).</summary>
public class InstanceEditDialog : Form
{
    private readonly InstanceInfo _inst;
    private readonly TextBox nameBox = new();
    private readonly TextBox descBox = new();
    private readonly ComboBox loaderBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox versionBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown ramBox = new() { Minimum = 0, Maximum = 32, Value = 0 };
    private string imagePath;

    public InstanceEditDialog(InstanceInfo inst)
    {
        _inst = inst;
        Text = "Modifier « " + inst.Name + " »";
        Size = new Size(480, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Panel;

        nameBox.Text = inst.Name;
        nameBox.Font = new Font("Segoe UI", 11f);
        descBox.Text = inst.Description;
        imagePath = inst.ImagePath;

        loaderBox.Items.AddRange(new object[] { "Vanilla", "Forge", "Fabric", "NeoForge", "Quilt" });
        loaderBox.SelectedItem = loaderBox.Items.Contains(inst.Loader) ? inst.Loader : "Vanilla";

        var imgBtn = new Button { Text = "Choisir une image...", Height = 34, Dock = DockStyle.Top };
        Theme.Apply(imgBtn);
        imgBtn.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
            if (ofd.ShowDialog(this) == DialogResult.OK) imagePath = ofd.FileName;
        };

        var saveBtn = new Button { Text = "Enregistrer", Height = 44, Dock = DockStyle.Bottom };
        Theme.Apply(saveBtn, primary: true);
        saveBtn.Click += (_, _) => Save();

        var cancelBtn = new Button { Text = "Annuler", Height = 36, Dock = DockStyle.Bottom };
        Theme.Apply(cancelBtn);
        cancelBtn.Click += (_, _) => Close();

        Controls.Add(saveBtn);
        Controls.Add(cancelBtn);

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 6), AutoScroll = true };

        AddField(host, "Nom", nameBox);
        AddField(host, "Description", descBox);
        AddField(host, "Loader", loaderBox);
        AddField(host, "Version Minecraft Java", versionBox);

        var ramLabel = FieldLabel("Mémoire en Go (0 = réglage global)");
        host.Controls.Add(ramLabel); host.Controls.Add(ramBox);
        ramBox.Dock = DockStyle.Top; ramBox.BringToFront();

        host.Controls.Add(imgBtn);
        imgBtn.BringToFront();

        ramLabel.BringToFront();
        loaderBox.BringToFront();
        versionBox.BringToFront();
        descBox.BringToFront();
        nameBox.BringToFront();

        Controls.Add(host);
        host.BringToFront();

        _ = LoadVersionsAsync();
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text, ForeColor = Theme.TextDim,
        AutoSize = true, Dock = DockStyle.Top, Height = 22
    };

    private void AddField(Panel host, string label, Control input)
    {
        host.Controls.Add(FieldLabel(label));
        host.Controls.Add(input);
        input.Dock = DockStyle.Top;
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
            versionBox.SelectedItem = versions.Contains(_inst.McVersion) ? _inst.McVersion : null;
            if (versionBox.SelectedIndex < 0) versionBox.SelectedIndex = Math.Min(1, versionBox.Items.Count - 1);
        }
        catch
        {
            versionBox.Items.Clear();
            versionBox.Items.Add("hors-ligne");
            versionBox.SelectedIndex = 0;
        }
    }

    private void Save()
    {
        var name = nameBox.Text.Trim();
        if (name.Length == 0) { MessageBox.Show("Le nom ne peut pas être vide.", "Team Launcher"); return; }

        _inst.Name = name;
        _inst.Description = descBox.Text.Trim();
        _inst.ImagePath = imagePath;
        _inst.Loader = loaderBox.SelectedItem?.ToString() ?? _inst.Loader;
        _inst.McVersion = versionBox.SelectedItem?.ToString() ?? _inst.McVersion;
        _inst.MaxRamGb = (int)ramBox.Value;
        DataStore.Save();
        DialogResult = DialogResult.OK;
        Close();
    }
}
