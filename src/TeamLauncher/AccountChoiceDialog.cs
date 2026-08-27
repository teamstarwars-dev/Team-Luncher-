using System.Drawing.Drawing2D;

namespace TeamLauncher;

public class AccountChoiceDialog : Form
{
    private readonly TextBox pseudoBox = new();
    private readonly Button msBtn = new();
    private readonly Button offBtn = new();

    public AccountChoiceDialog()
    {
        Text = "Team Launcher";
        Size = new Size(560, 420);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        TopMost = true;

        // coins arrondis + barre de titre déplaçable
        var path = new GraphicsPath();
        int r = 18;
        path.AddArc(0, 0, r, r, 180, 90);
        path.AddArc(Width - r, 0, r, r, 270, 90);
        path.AddArc(Width - r, Height - r, r, r, 0, 90);
        path.AddArc(0, Height - r, r, r, 90, 90);
        Region = new Region(path);

        bool dragging = false; Point start = default;
        MouseDown += (_, e) => { dragging = true; start = e.Location; };
        MouseMove += (_, e) => { if (dragging) { Location = new Point(Left + e.X - start.X, Top + e.Y - start.Y); } };
        MouseUp += (_, _) => dragging = false;

        // ---- contenu ----
        var logo = new Label
        {
            Text = "TEAM LAUNCHER",
            ForeColor = Theme.Accent,
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            AutoSize = true
        };
        logo.Location = new Point((Width - TextRenderer.MeasureText(logo.Text, logo.Font).Width) / 2, 48);

        var subtitle = new Label
        {
            Text = "Connecte-toi pour jouer",
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 11f),
            AutoSize = true
        };
        subtitle.Location = new Point((Width - TextRenderer.MeasureText(subtitle.Text, subtitle.Font).Width) / 2, 92);

        // bouton Microsoft officiel
        msBtn.Text = "Se connecter avec Microsoft";
        msBtn.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        msBtn.Size = new Size(340, 52);
        msBtn.Location = new Point((Width - 340) / 2, 150);
        Theme.Apply(msBtn, primary: true);
        msBtn.Click += async (_, _) =>
        {
            DataStore.Settings.AccountMode = "microsoft";
            DataStore.Save();

            msBtn.Enabled = false;
            msBtn.Text = "Connexion en cours...";
            var session = await MsAuth.LoginAsync(this);
            if (session == null) { Close(); Application.Exit(); return; }
            DataStore.Settings.PlayerName = session.Name;
            DataStore.Save();
            AppEvents.NotifyAccountChanged();
            DialogResult = DialogResult.OK;
        };

        // séparateur
        var sep = new Label
        {
            Text = "───────────   ou   ───────────",
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 9f),
            AutoSize = true
        };
        sep.Location = new Point((Width - TextRenderer.MeasureText(sep.Text, sep.Font).Width) / 2, 228);

        // mode hors-ligne
        pseudoBox.Font = new Font("Segoe UI", 11f);
        pseudoBox.Size = new Size(340, 30);
        pseudoBox.Location = new Point((Width - 340) / 2, 264);
        pseudoBox.PlaceholderText = "Ton pseudo pour le mode hors-ligne";

        offBtn.Text = "Continuer hors-ligne";
        offBtn.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        offBtn.Size = new Size(340, 44);
        offBtn.Location = new Point((Width - 340) / 2, 304);
        Theme.Apply(offBtn);
        offBtn.Click += (_, _) =>
        {
            var name = pseudoBox.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show("Entre un pseudo pour le mode hors-ligne.", "Team Launcher");
                return;
            }
            DataStore.Settings.AccountMode = "offline";
            DataStore.Settings.PlayerName = name;
            DataStore.Save();
            DialogResult = DialogResult.OK;
        };

        // croix de fermeture
        var closeBtn = new Label
        {
            Text = "✕",
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 11f),
            AutoSize = true,
            Cursor = Cursors.Hand
        };
        closeBtn.Location = new Point(Width - 36, 14);
        closeBtn.Click += (_, _) => Application.Exit();

        Controls.Add(logo);
        Controls.Add(subtitle);
        Controls.Add(msBtn);
        Controls.Add(sep);
        Controls.Add(pseudoBox);
        Controls.Add(offBtn);
        Controls.Add(closeBtn);

        AcceptButton = msBtn;
    }
}
