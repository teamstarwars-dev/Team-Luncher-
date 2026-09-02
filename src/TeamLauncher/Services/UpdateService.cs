using System.Diagnostics;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Mise à jour automatique du launcher.
/// Vérifie un fichier version.json sur GitHub/serveur, télécharge et relance.
/// Mode automatique : au démarrage, si une update existe → téléchargement + relance sans interaction.
/// </summary>
public static class UpdateService
{
    // URL par défaut (fallback si pas défini dans les settings)
    private const string DefaultVersionUrl = "https://raw.githubusercontent.com/teamstarwars-dev/Team-Luncher-/main/version.json";
    // Utilise Http.Shared (client HTTP partagé)

    /// <summary>Version actuelle de l'exe.</summary>
    public static string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>
    /// Vérifie au démarrage. Si une mise à jour est disponible,
    /// la propose automatiquement à l'utilisateur (comme Numek Launcher).
    /// </summary>
    public static async Task CheckOnStartupAsync()
    {
        try
        {
            var info = await CheckAsync();
            if (info == null) return;

            // Proposer automatiquement la mise à jour
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
                    await UpdateAsync(info.Value.Url, msg =>
                    {
                        mainForm.BeginInvoke(() => mainForm.Text = $"Team Launcher — {msg}");
                    });
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

    /// <summary>Vérifie si une mise à jour existe. Retourne les infos ou null.</summary>
    public static async Task<(string Version, string Url, string Changelog)?> CheckAsync()
    {
        try
        {
            string versionUrl = !string.IsNullOrEmpty(DataStore.Settings.UpdateUrl)
                ? DataStore.Settings.UpdateUrl
                : DefaultVersionUrl;

            string json = await Http.Shared.GetStringAsync(versionUrl);
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

    /// <summary>Télécharge la mise à jour, remplace l'exe, relance.</summary>
    public static async Task UpdateAsync(string downloadUrl, Action<string>? progress = null)
    {
        string exePath = Environment.ProcessPath ?? "";
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            throw new Exception("Impossible de trouver l'exe en cours.");

        string tempExe = exePath + ".update";
        string backupExe = exePath + ".old";

        try
        {
            progress?.Invoke("Téléchargement de la mise à jour…");

            using var resp = await Http.Shared.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? -1;
            await using var fs = File.Create(tempExe);
            await using var src = await resp.Content.ReadAsStreamAsync();

            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, n));
                read += n;
                if (total > 0)
                    progress?.Invoke($"Téléchargement : {read * 100 / total}%");
            }
            fs.Close();

            progress?.Invoke("Installation…");

            // Backup l'ancien exe
            if (File.Exists(backupExe)) File.Delete(backupExe);
            File.Move(exePath, backupExe);

            // Remplacer
            File.Move(tempExe, exePath);

            // Relancer
            progress?.Invoke("Relance…");
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });

            // Quitter
            Environment.Exit(0);
        }
        catch
        {
            // Rollback si échec
            if (File.Exists(tempExe)) { try { File.Delete(tempExe); } catch { } }
            if (File.Exists(backupExe) && !File.Exists(exePath))
            {
                try { File.Move(backupExe, exePath); } catch { }
            }
            throw;
        }
    }

    /// <summary>Propose la mise à jour à l'utilisateur (depuis les paramètres).</summary>
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
                await UpdateAsync(info.Value.Url, msg =>
                {
                    owner.BeginInvoke(() => owner.Text = $"Team Launcher — {msg}");
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Erreur :\n" + ex.Message, "Team Launcher");
                owner.Text = "Team Launcher";
            }
        }
    }
}
