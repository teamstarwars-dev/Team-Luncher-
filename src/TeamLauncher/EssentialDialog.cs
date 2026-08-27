namespace TeamLauncher;

public class EssentialDialog : Form
{
    private readonly ComboBox picker = new();
    private readonly Button installBtn;
    private readonly Label status = new();

    public EssentialDialog()
    {
        Text = "Installer Essential";
        Size = new Size(440, 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Panel;

        var title = new Label
        {
            Text = "Essential ajoute le mode Social (amis, invitations),\nles cosmétiques et son menu latéral dans Minecraft.",
            ForeColor = Theme.TextDim,
            Dock = DockStyle.Top,
            Height = 52,
            TextAlign = ContentAlignment.MiddleCenter
        };

        picker.DropDownStyle = ComboBoxStyle.DropDownList;
        picker.Font = new Font("Segoe UI", 11f);

        foreach (var i in DataStore.Settings.Instances)
            picker.Items.Add(i.Name);
        if (picker.Items.Count > 0) picker.SelectedIndex = 0;

        status.ForeColor = Theme.Text;
        status.Dock = DockStyle.Bottom;
        status.Height = 32;
        status.TextAlign = ContentAlignment.MiddleCenter;

        installBtn = new Button { Text = "⚡ Installer dans l'instance", Height = 44, Dock = DockStyle.Bottom };
        Theme.Apply(installBtn, primary: true);
        installBtn.Click += async (_, _) => await InstallAsync();

        Controls.Add(picker);
        Controls.Add(installBtn);
        Controls.Add(status);
        picker.BringToFront();

        if (picker.Items.Count == 0)
        {
            picker.Enabled = false;
            installBtn.Enabled = false;
            status.Text = "Crée d'abord une instance.";
        }
    }

    private async Task InstallAsync()
    {
        var inst = DataStore.Settings.Instances.ElementAtOrDefault(picker.SelectedIndex);
        if (inst == null) return;

        installBtn.Enabled = false;
        status.Text = "Téléchargement d'Essential...";
        try
        {
            await Task.Run(() => EssentialService.InstallAsync(inst));
            status.Text = "✅ Essential installé dans « " + inst.Name + " » !";
            MessageBox.Show(
                $"Essential a été installé dans « {inst.Name} ».\n\n" +
                "Son menu latéral (Social, Settings, cosmétiques...) apparaîtra\n" +
                "dans le jeu au prochain lancement de cette instance.",
                "Team Launcher");
        }
        catch (Exception ex)
        {
            status.Text = "❌ Échec de l'installation.";
            MessageBox.Show(ex.Message, "Team Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            installBtn.Enabled = true;
        }
    }
}
