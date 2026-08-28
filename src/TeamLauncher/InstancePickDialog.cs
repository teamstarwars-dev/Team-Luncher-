namespace TeamLauncher;

/// <summary>Petit dialogue pour choisir une instance dans une liste.</summary>
public class InstancePickDialog : Form
{
    public InstanceInfo? Selected { get; private set; }
    private readonly ComboBox picker = new();

    public InstancePickDialog(string title, string actionLabel)
    {
        Text = title;
        Size = new Size(420, 170);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Panel;

        picker.DropDownStyle = ComboBoxStyle.DropDownList;
        picker.Font = new Font("Segoe UI", 11f);
        foreach (var i in DataStore.Settings.Instances) picker.Items.Add(i.Name);
        if (picker.Items.Count > 0) picker.SelectedIndex = 0;

        var go = new Button { Text = actionLabel, Height = 42, Dock = DockStyle.Bottom };
        Theme.Apply(go, primary: true);
        go.Click += (_, _) =>
        {
            Selected = DataStore.Settings.Instances.ElementAtOrDefault(picker.SelectedIndex);
            DialogResult = DialogResult.OK;
        };

        Controls.Add(picker);
        Controls.Add(go);

        if (picker.Items.Count == 0)
            MessageBox.Show(Lang.T("Aucune instance disponible.", "No instance available."), "Team Launcher");
    }
}
