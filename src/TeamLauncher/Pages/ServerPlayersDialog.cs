namespace TeamLauncher;

/// <summary>
/// Gestion des joueurs d'un serveur hébergé sans passer par la console :
/// whitelist (case + liste de pseudos), OP en un clic depuis les connectés,
/// MOTD et message de bienvenue.
/// </summary>
public class ServerPlayersDialog : Form
{
    private readonly HostedServer _s;
    private readonly bool _running;
    private readonly CheckBox wlChk = new();
    private readonly TextBox addBox = new();
    private readonly ListBox wlList = new();
    private readonly ListBox onlineList = new();
    private readonly TextBox motdBox = new();
    private readonly TextBox welcomeBox = new();

    public ServerPlayersDialog(HostedServer s)
    {
        _s = s;
        _running = ServerHost.IsRunning(s);

        Text = $"Joueurs — {s.Name}";
        Size = new Size(720, 700);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        BackColor = Theme.Bg;

        Label MkTitle(string t) => new()
        {
            Text = t, ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            AutoSize = true, Margin = new Padding(0, 14, 0, 4)
        };
        Label MkHint(string t) => new()
        {
            Text = t, ForeColor = Theme.TextDim, AutoSize = true,
            Font = new Font("Segoe UI", 8.5f)
        };

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true,
            Padding = new Padding(16, 8, 16, 8)
        };

        // ---- whitelist ----
        root.Controls.Add(MkTitle(Lang.T("🔒 Whitelist", "🔒 Whitelist")));
        wlChk.Text = Lang.T(
            "Whitelist activée — seuls les joueurs ci-dessous peuvent rejoindre",
            "Whitelist enabled — only the players below can join");
        wlChk.Checked = s.RpProfile || s.WhitelistEnabled;
        wlChk.ForeColor = Theme.Text; wlChk.AutoSize = true;
        if (s.RpProfile) wlChk.Enabled = false; // forcée par le profil RP
        root.Controls.Add(wlChk);

        var addRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        addBox.Width = 260;
        addBox.Font = new Font("Segoe UI", 10f);
        addBox.BorderStyle = BorderStyle.FixedSingle;
        addBox.BackColor = Theme.Card;
        addBox.ForeColor = Theme.Text;
        addBox.PlaceholderText = Lang.T("Pseudo Minecraft", "Minecraft username");
        var addBtn = new Button { Text = Lang.T("＋ Autoriser", "＋ Allow"), Width = 130, Height = 30 };
        Theme.Apply(addBtn, primary: true);
        addBtn.Click += (_, _) =>
        {
            string name = addBox.Text.Trim();
            if (name.Length == 0 || !name.All(c => char.IsLetterOrDigit(c) || c == '_')) return;
            if (!_s.Whitelist.Contains(name)) _s.Whitelist.Add(name);
            DataStore.Save();
            if (wlChk.Checked && _running) ServerHost.SendCommand(_s.Id, $"whitelist add {name}");
            RefreshWlList();
            addBox.Text = "";
        };
        addRow.Controls.Add(addBox);
        addRow.Controls.Add(addBtn);
        root.Controls.Add(addRow);

        wlList.Size = new Size(400, 120);
        wlList.BorderStyle = BorderStyle.FixedSingle;
        wlList.BackColor = Theme.Card;
        wlList.ForeColor = Theme.Text;
        wlList.Font = new Font("Consolas", 10f);
        root.Controls.Add(wlList);

        var wlBtns = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        var removeBtn = new Button { Text = Lang.T("✖ Retirer le sélectionné", "✖ Remove selected"), Width = 220, Height = 32 };
        Theme.Apply(removeBtn);
        removeBtn.Click += (_, _) =>
        {
            if (wlList.SelectedItem is not string name) return;
            _s.Whitelist.Remove(name);
            DataStore.Save();
            if (_running) ServerHost.SendCommand(_s.Id, $"whitelist remove {name}");
            RefreshWlList();
        };
        wlBtns.Controls.Add(removeBtn);
        root.Controls.Add(wlBtns);
        root.Controls.Add(MkHint(Lang.T(
            "La liste est appliquée immédiatement si le serveur tourne, sinon au prochain démarrage.",
            "Changes apply immediately while the server runs, otherwise on next start.")));

        // ---- connectés ----
        root.Controls.Add(MkTitle(Lang.T("👥 Joueurs connectés", "👥 Connected players")));
        onlineList.Size = new Size(400, 110);
        onlineList.BorderStyle = BorderStyle.FixedSingle;
        onlineList.BackColor = Color.FromArgb(12, 14, 10);
        onlineList.ForeColor = Theme.Text;
        onlineList.Font = new Font("Consolas", 10f);
        root.Controls.Add(onlineList);

        var onlineBtns = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        var refreshBtn = new Button { Text = "⟳ Actualiser", Width = 140, Height = 32 };
        Theme.Apply(refreshBtn);
        refreshBtn.Click += (_, _) => _ = RefreshOnlineAsync();
        var opBtn = new Button { Text = Lang.T("★ Promouvoir OP", "★ Promote to OP"), Width = 190, Height = 32 };
        Theme.Apply(opBtn, primary: true);
        opBtn.Click += (_, _) =>
        {
            if (onlineList.SelectedItem is not string name) return;
            if (!_running)
            {
                MessageBox.Show(Lang.T(
                    "Le serveur doit être démarré pour promouvoir un joueur.",
                    "Start the server before promoting a player."), "Team Launcher");
                return;
            }
            ServerHost.SendCommand(_s.Id, $"op {name}");
            Notifier.Show(_s.Name, string.Format(Lang.T("{0} est maintenant opérateur !", "{0} is now an operator!"), name));
        };
        onlineBtns.Controls.Add(refreshBtn);
        onlineBtns.Controls.Add(opBtn);
        root.Controls.Add(onlineBtns);
        root.Controls.Add(MkHint(Lang.T(
            "Un OP peut utiliser les commandes du serveur (/gamemode, /tp…). À réserver aux modos de ta ville.",
            "OPs can use server commands (/gamemode, /tp…). Reserve this for your city moderators.")));

        // ---- messages ----
        root.Controls.Add(MkTitle(Lang.T("💬 Messages", "💬 Messages")));
        root.Controls.Add(new Label
        {
            Text = "MOTD (message affiché dans la liste des serveurs) :",
            ForeColor = Theme.TextDim, AutoSize = true
        });
        motdBox.Width = 480;
        motdBox.Font = new Font("Segoe UI", 10f);
        motdBox.BorderStyle = BorderStyle.FixedSingle;
        motdBox.BackColor = Theme.Card;
        motdBox.ForeColor = Theme.Text;
        motdBox.Text = s.Motd;
        root.Controls.Add(motdBox);
        root.Controls.Add(new Label
        {
            Text = Lang.T(
                "Message de bienvenue ({joueur} = pseudo du joueur qui arrive) :",
                "Welcome message ({joueur} = joining player's name):"),
            ForeColor = Theme.TextDim, AutoSize = true
        });
        welcomeBox.Width = 480;
        welcomeBox.Font = new Font("Segoe UI", 10f);
        welcomeBox.BorderStyle = BorderStyle.FixedSingle;
        welcomeBox.BackColor = Theme.Card;
        welcomeBox.ForeColor = Theme.Text;
        welcomeBox.PlaceholderText = Lang.T(
            "Ex : Bienvenue {joueur} sur notre ville RP !",
            "E.g.: Welcome {joueur} to our RP city!");
        welcomeBox.Text = s.WelcomeMessage;
        root.Controls.Add(welcomeBox);

        // ---- sauvegarde ----
        var save = new Button
        {
            Text = Lang.T("💾 Enregistrer", "💾 Save"),
            Dock = DockStyle.Bottom, Height = 44
        };
        Theme.Apply(save, primary: true);
        save.Click += (_, _) =>
        {
            _s.WhitelistEnabled = wlChk.Checked && !_s.RpProfile;
            if (motdBox.Text.Trim().Length > 0) _s.Motd = motdBox.Text.Trim();
            _s.WelcomeMessage = welcomeBox.Text.Trim();
            DataStore.Save();
            ServerHost.ApplyProperties(_s);
            _ = Task.Run(() => ServerHost.SyncWhitelistAsync(_s));
            if (_running)
                ServerHost.SendCommand(_s.Id, wlChk.Checked ? "whitelist on" : "whitelist off");
            Notifier.Show(_s.Name, Lang.T(
                "Joueurs et messages enregistrés.", "Players and messages saved."));
            DialogResult = DialogResult.OK;
        };

        Controls.Add(root);
        Controls.Add(save);

        RefreshWlList();
        _ = RefreshOnlineAsync();
    }

    private void RefreshWlList()
    {
        wlList.BeginUpdate();
        wlList.Items.Clear();
        foreach (var name in _s.Whitelist) wlList.Items.Add(name);
        wlList.EndUpdate();
    }

    private async Task RefreshOnlineAsync()
    {
        onlineList.Items.Clear();
        onlineList.Items.Add("…");
        ServerPing.Status? st = null;
        try { st = await ServerPing.QueryAsync("127.0.0.1:" + _s.Port); } catch { }
        if (IsDisposed) return;
        onlineList.Items.Clear();
        if (st == null)
        {
            onlineList.Items.Add(Lang.T("(serveur arrêté ou injoignable)", "(server stopped or unreachable)"));
            return;
        }
        if (st.Players.Length == 0)
            onlineList.Items.Add(st.Online > 0
                ? Lang.T($"({st.Online} joueur(s), liste non exposée par le serveur)", $"({st.Online} player(s), list not exposed by the server)")
                : Lang.T("(aucun joueur connecté)", "(no players connected)"));
        else
            foreach (var p in st.Players) onlineList.Items.Add(p);
    }
}
