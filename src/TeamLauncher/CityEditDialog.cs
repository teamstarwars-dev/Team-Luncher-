namespace TeamLauncher;

/// <summary>Ajout ou modification d'une ville RP de la team.</summary>
public class CityEditDialog : Form
{
    public TeamCity City { get; private set; } = new();

    private readonly TextBox nameBox = new() { Width = 400, Font = new Font("Segoe UI", 10f), BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text, PlaceholderText = "Ex : Valmont", Padding = new Padding(4) };
    private readonly TextBox ownerBox = new() { Width = 400, Font = new Font("Segoe UI", 10f), BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text, PlaceholderText = "Ton pseudo", Padding = new Padding(4) };
    private readonly TextBox addressBox = new() { Width = 400, Font = new Font("Consolas", 10f), BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text, PlaceholderText = "ville.rp-team.fr", Padding = new Padding(4) };
    private readonly TextBox descBox = new() { Width = 400, Font = new Font("Segoe UI", 10f), BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text, PlaceholderText = "Ville portuaire médiévale…", Padding = new Padding(4) };

    public CityEditDialog(TeamCity? existing = null)
    {
        Text = existing == null ? "Ajouter une ville de la team" : "Modifier la ville";
        Size = new Size(450, 340);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Panel;

        Label MkLabel(string t) => new()
        {
            Text = t, ForeColor = Theme.TextDim, AutoSize = true,
            Margin = new Padding(0, 10, 0, 2)
        };

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(16, 12, 16, 12)
        };
        root.Controls.Add(MkLabel(Lang.T("Nom de la ville", "City name")));
        root.Controls.Add(nameBox);
        root.Controls.Add(MkLabel(Lang.T("Propriétaire (optionnel)", "Owner (optional)")));
        root.Controls.Add(ownerBox);
        root.Controls.Add(MkLabel(Lang.T("Adresse du serveur", "Server address")));
        root.Controls.Add(addressBox);
        root.Controls.Add(MkLabel(Lang.T("Description (optionnel)", "Description (optional)")));
        root.Controls.Add(descBox);

        var ok = new Button { Text = Lang.T("Enregistrer", "Save"), Dock = DockStyle.Bottom, Height = 42 };
        Theme.Apply(ok, primary: true);
        ok.Click += (_, _) =>
        {
            if (nameBox.Text.Trim().Length == 0 || addressBox.Text.Trim().Length == 0)
            {
                MessageBox.Show(
                    Lang.T("Le nom et l'adresse sont obligatoires.", "Name and address are required."),
                    "Team Launcher");
                return;
            }
            City = existing ?? new TeamCity();
            City.Name = nameBox.Text.Trim();
            City.Owner = ownerBox.Text.Trim();
            City.Address = addressBox.Text.Trim();
            City.Description = descBox.Text.Trim();
            DialogResult = DialogResult.OK;
        };

        Controls.Add(root);
        Controls.Add(ok);
        AcceptButton = ok;

        if (existing != null)
        {
            nameBox.Text = existing.Name;
            ownerBox.Text = existing.Owner;
            addressBox.Text = existing.Address;
            descBox.Text = existing.Description;
        }
    }
}
