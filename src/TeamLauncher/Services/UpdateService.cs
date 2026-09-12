using System.Diagnostics;
using System.Text.Json;

namespace TeamLauncher;

public static class UpdateService
{
    private const string DefaultVersionUrl = "https://raw.githubusercontent.com/teamstarwars-dev/Team-Luncher-/master/version.json";

    public static string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(4) ?? "1.0.0.0";

    public static async Task CheckOnStartupAsync()
    {
        try
        {
            // Supprimer l'ancien exe (.old) au démarrage s'il traîne
            CleanupOldExe();

            var info = await CheckAsync();
            if (info == null) return;

            var mainForm = Application.OpenForms.OfType<Form>().FirstOrDefault();
            if (mainForm == null) return;

            var result = mainForm.Invoke(() => MessageBox.Show(mainForm,
                $"Une mise à jour est disponible : v{info.Value.Version}\n\n" +
                $"Tu es en v{CurrentVersion}\n\n" +
                $"Changelog :\n{info.Value.Changelog}\n\n" +
                $"Mettre à jour maintenant ?",
                "Team Launcher — Mise à jour",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information));

            if (result == DialogResult.Yes)
            {
                try
                {
                    await UpdateAsync(info.Value.Url, mainForm);
                }
                catch (Exception ex)
                {
                    mainForm.BeginInvoke(() =>
                    {
                        MessageBox.Show(mainForm, "Erreur lors de la mise à jour :\n" + ex.Message,
                            "Team Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        mainForm.Text = "Team Launcher";
                    });
                }
            }
        }
        catch { }
    }

    private static void CleanupOldExe()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exePath)) return;
            string old = exePath + ".old";
            if (File.Exists(old)) File.Delete(old);
        }
        catch { }
    }

    public static async Task<(string Version, string Url, string Changelog)?> CheckAsync()
    {
        try
        {
            string json = await Http.Shared.GetStringAsync(DefaultVersionUrl);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string latestVersion = root.GetProperty("version").GetString() ?? "";
            string downloadUrl = root.GetProperty("url").GetString() ?? "";
            string changelog = root.TryGetProperty("changelog", out var cl) ? cl.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(latestVersion) || string.IsNullOrEmpty(downloadUrl))
                return null;

            if (Version.TryParse(latestVersion, out var latest) &&
                Version.TryParse(CurrentVersion, out var current) &&
                latest > current)
            {
                return (latestVersion, downloadUrl, changelog);
            }
            return null;
        }
        catch { return null; }
    }

    public static async Task UpdateAsync(string downloadUrl, Form owner)
    {
        string exePath = Environment.ProcessPath ?? "";
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            throw new Exception("Impossible de trouver l'exe en cours.");

        string dir = Path.GetDirectoryName(exePath) ?? "";
        string tempNew = Path.Combine(dir, "TeamLauncher.new.exe");
        string oldExe = exePath + ".old";

        void SetProgress(string msg)
        {
            try { owner.BeginInvoke(() => owner.Text = $"Team Launcher — {msg}"); } catch { }
        }

        try
        {
            SetProgress("Téléchargement… 0%");

            using var resp = await Http.Shared.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? -1;
            await using var fs = File.Create(tempNew);
            await using var src = await resp.Content.ReadAsStreamAsync();

            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, n));
                read += n;
                if (total > 0)
                {
                    int pct = (int)(read * 100 / total);
                    SetProgress($"Téléchargement… {pct}%");
                }
            }
            fs.Close();

            SetProgress("Installation…");

            // Étape 1 : Renommer l'ancien exe (autorisé même en cours d'exécution sur Windows)
            if (File.Exists(oldExe)) File.Delete(oldExe);
            File.Move(exePath, oldExe);

            // Étape 2 : Renommer le nouveau exe à la place
            File.Move(tempNew, exePath);

            // Étape 3 : Relancer le nouveau
            SetProgress("Relance…");
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });

            // Étape 4 : Quitter
            Environment.Exit(0);
        }
        catch
        {
            // Rollback
            if (File.Exists(tempNew)) { try { File.Delete(tempNew); } catch { } }
            if (File.Exists(oldExe) && !File.Exists(exePath))
            {
                try { File.Move(oldExe, exePath); } catch { }
            }
            throw;
        }
    }

    public static async Task PromptUpdateAsync(Form owner)
    {
        var info = await CheckAsync();
        if (info == null)
        {
            MessageBox.Show(owner, "Tu es à jour !", "Team Launcher");
            return;
        }

        var result = MessageBox.Show(owner,
            $"Nouvelle version disponible : v{info.Value.Version}\n\n" +
            $"Tu es en v{CurrentVersion}\n\n" +
            $"Changelog :\n{info.Value.Changelog}\n\n" +
            $"Mettre à jour maintenant ?",
            "Team Launcher — Mise à jour",
            MessageBoxButtons.YesNo, MessageBoxIcon.Information);

        if (result == DialogResult.Yes)
        {
            try
            {
                await UpdateAsync(info.Value.Url, owner);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Erreur :\n" + ex.Message, "Team Launcher");
                owner.Text = "Team Launcher";
            }
        }
    }
}
