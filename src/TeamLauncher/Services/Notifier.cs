using System.Runtime.InteropServices;

namespace TeamLauncher;

/// <summary>
/// Petites notifications non intrusives en bas à droite de l'écran
/// (serveur démarré, téléchargement terminé, mise à jour…).
/// </summary>
public static class Notifier
{
    private static readonly List<Form> Open = new();
    private static SynchronizationContext? _ui;

    /// <summary>À appeler une fois sur le thread UI (chargement du formulaire principal).</summary>
    public static void Init() => _ui = SynchronizationContext.Current;

    public static void Show(string title, string message)
    {
        // Appels depuis un thread d'arrière-plan (crash serveur, téléchargement…) :
        // on bascule sur le thread UI.
        if (_ui != null && SynchronizationContext.Current != _ui)
        {
            _ui.Post(_ => Show(title, message), null);
            return;
        }
        if (!SystemInformation.UserInteractive) return;
        try
        {
            var form = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                TopMost = true,
                Size = new Size(320, 84),
                BackColor = Theme.Panel
            };
            form.Paint += (_, e) =>
            {
                using var pen = new Pen(Theme.Accent, 2);
                e.Graphics.DrawRectangle(pen, 0, 0, form.Width - 1, form.Height - 1);
            };

            var titleLbl = new Label
            {
                Text = title,
                ForeColor = Theme.Accent,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(14, 10),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            var msgLbl = new Label
            {
                Text = message,
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 9f),
                Location = new Point(14, 32),
                MaximumSize = new Size(292, 0),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            form.Controls.Add(titleLbl);
            form.Controls.Add(msgLbl);

            // position : empilé au-dessus des notifications déjà ouvertes
            var work = Screen.PrimaryScreen.WorkingArea;
            int index = 0;
            lock (Open)
            {
                index = Open.Count;
                Open.Add(form);
            }
            form.Location = new Point(work.Right - form.Width - 16,
                work.Bottom - form.Height - 16 - index * (form.Height + 8));

            var timer = new System.Windows.Forms.Timer { Interval = 4500 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try { form.Close(); } catch { }
            };
            form.FormClosed += (_, _) =>
            {
                timer.Dispose();
                lock (Open) Open.Remove(form);
            };
            timer.Start();
            form.Show();
        }
        catch { }
    }
}
