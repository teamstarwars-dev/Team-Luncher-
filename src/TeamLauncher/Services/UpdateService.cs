using System.Diagnostics;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Mise à jour automatique du launcher.
/// Vérifie un fichier version.json sur GitHub/serveur, télécharge et relance.
/// </summary>
public static class UpdateService
{
    // URL du version.json sur GitHub — À MODIFIER avec ton repo
    // Format : https://raw.githubusercontent.com/TON_USER/TON_REPO/main/version.json
    private const string VersionUrl = "https://raw.githubusercontent.com/teamstarwars-dev/Team-Luncher-/main/version.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Version actuelle de l'exe.</summary>
    public static string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>Vérifie au démarrage. Silencieux si pas de mise à jour.</summary>
    public static async Task CheckOnStartupAsync()
    {
        try
        {
            var info = await CheckAsync();
            if (info == null) return;

            // Notifier l'utilisateur
            Notifier.Show(
                $"Mise à jour disponible : v{info.Value.Version}",
                $"Tu es en v{CurrentVersion}. Relance pour mettre à jour.");
        }
        catch { }
    }

    /// <summary>Vérifie si une mise à jour existe. Retourne les infos ou null.</summary>
    public static async Task<(string Version, string Url, string Changelog)?> CheckAsync()
    {
        try
        {
            string json = await Http.GetStringAsync(VersionUrl);
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

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using var resp = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
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

    /// <summary>Propose la mise à jour à l'utilisateur.</summary>
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
