namespace TeamLauncher;

/// <summary>
/// Options de lancement par instance : RAM allouée + arguments JVM personnalisés.
/// </summary>
public class LaunchOptionsDialog : Form
{
    private readonly NumericUpDown ram = new();
    private readonly TextBox jvmArgs = new();
    private readonly InstanceInfo inst;
    private readonly Label globalHint = new();

    public LaunchOptionsDialog(InstanceInfo instance)
    {
        inst = instance;
        Text = $"Options de lancement — {inst.Name}";
        Size = new Size(560, 340);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        BackColor = Theme.Panel;

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(18, 16, 18, 0)
        };

        layout.Controls.Add(new Label
        {
            Text = "Mémoire allouée (Go)", ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold), AutoSize = true
        });
        ram.Minimum = 1; ram.Maximum = 32; ram.Width = 100;
        ram.Value = Math.Clamp(inst.MaxRamGb > 0 ? inst.MaxRamGb : DataStore.Settings.MaxRamGb, 1, 32);
        ram.Font = new Font("Segoe UI", 10f);
        layout.Controls.Add(ram);
        globalHint.Text = $"(réglage global actuel : {DataStore.Settings.MaxRamGb} Go)";
        globalHint.ForeColor = Theme.TextDim;
        globalHint.AutoSize = true;
        layout.Controls.Add(globalHint);

        layout.Controls.Add(new Label
        {
            Text = "\nArguments JVM supplémentaires (optionnel)",
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold), AutoSize = true
        });
        jvmArgs.Width = 490; jvmArgs.Height = 90;
        jvmArgs.Multiline = true;
        jvmArgs.ScrollBars = ScrollBars.Vertical;
        jvmArgs.Font = new Font("Consolas", 9.5f);
        jvmArgs.BackColor = Theme.Card;
        jvmArgs.ForeColor = Theme.Text;
        jvmArgs.PlaceholderText = "ex : -XX:+UseZGC -Dsun.rmi.dgc.server.gcInterval=2147483646";
        jvmArgs.Text = inst.JvmArgs;
        layout.Controls.Add(jvmArgs);

        var save = new Button { Text = "💾 Enregistrer", Width = 200, Height = 42 };
        Theme.Apply(save, primary: true);
        save.Margin = new Padding(0, 14, 0, 0);
        save.Click += (_, _) =>
        {
            inst.MaxRamGb = (int)ram.Value;
            inst.JvmArgs = jvmArgs.Text.Trim();
            DataStore.Save();
            DialogResult = DialogResult.OK;
        };
        layout.Controls.Add(save);

        Controls.Add(layout);
    }
}
