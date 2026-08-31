namespace TeamLauncher;

/// <summary>
/// Dialogue simple avec un champ de texte pour renommer / saisir une valeur.
/// </summary>
public sealed class InputDialog : Form
{
    public string Value { get; private set; } = "";

    public InputDialog(string title, string label, string defaultValue = "")
    {
        Text = title;
        Size = new Size(360, 160);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var lbl = new Label
        {
            Text = label,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 10f),
            Location = new Point(16, 16),
            AutoSize = true
        };

        var box = new TextBox
        {
            Text = defaultValue,
            Location = new Point(16, 48),
            Width = 310,
            Font = new Font("Segoe UI", 10f),
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            Padding = new Padding(4)
        };
        box.SelectAll();
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                Value = box.Text;
                DialogResult = DialogResult.OK;
                Close();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        var okBtn = new Button
        {
            Text = Lang.T("OK", "OK"),
            Width = 100,
            Height = 32,
            Location = new Point(130, 88),
            FlatStyle = FlatStyle.Flat
        };
        Theme.Apply(okBtn, primary: true);
        okBtn.Click += (_, _) =>
        {
            Value = box.Text;
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancelBtn = new Button
        {
            Text = Lang.T("Annuler", "Cancel"),
            Width = 100,
            Height = 32,
            Location = new Point(240, 88),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Card
        };
        cancelBtn.FlatAppearance.BorderSize = 0;
        cancelBtn.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.Add(lbl);
        Controls.Add(box);
        Controls.Add(okBtn);
        Controls.Add(cancelBtn);

        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        box.Focus();
    }
}
