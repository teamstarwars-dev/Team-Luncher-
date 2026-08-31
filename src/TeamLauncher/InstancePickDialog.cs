namespace TeamLauncher;

/// <summary>Petit dialogue pour choisir une instance dans une liste.</summary>
public class InstancePickDialog : Form
{
    public InstanceInfo? Selected { get; private set; }
    private readonly ComboBox picker = new();

    /// <summary>ID de la dernière instance sélectionnée (persisté entre les dialogs).</summary>
    private static string? _lastSelectedId;

    public InstancePickDialog(string title, string actionLabel)
    {
        Text = title;
        Size = new Size(420, 170);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Panel;

        Theme.ApplyInput(picker);
        picker.DropDownStyle = ComboBoxStyle.DropDownList;
        picker.Font = new Font("Segoe UI", 11f);
        foreach (var i in DataStore.Settings.Instances) picker.Items.Add(i.Name);

        if (picker.Items.Count > 0)
        {
            // Reprendre la dernière instance sélectionnée, sinon la dernière jouée, sinon la première
            int defaultIndex = 0;
            if (!string.IsNullOrEmpty(_lastSelectedId))
            {
                int idx = DataStore.Settings.Instances.FindIndex(i => i.Id == _lastSelectedId);
                if (idx >= 0) defaultIndex = idx;
            }
            else
            {
                var lastPlayed = DataStore.Settings.Instances
                    .OrderByDescending(i => i.LastPlayed)
                    .FirstOrDefault();
                if (lastPlayed != null)
                    defaultIndex = DataStore.Settings.Instances.IndexOf(lastPlayed);
            }
            picker.SelectedIndex = defaultIndex;
        }

        var go = new Button { Text = actionLabel, Height = 42, Dock = DockStyle.Bottom };
        Theme.Apply(go, primary: true);
        go.Click += (_, _) =>
        {
            Selected = DataStore.Settings.Instances.ElementAtOrDefault(picker.SelectedIndex);
            if (Selected != null) _lastSelectedId = Selected.Id;
            DialogResult = DialogResult.OK;
        };

        Controls.Add(picker);
        Controls.Add(go);

        if (picker.Items.Count == 0)
            MessageBox.Show(Lang.T("Aucune instance disponible.", "No instance available."), "Team Launcher");
    }
}
