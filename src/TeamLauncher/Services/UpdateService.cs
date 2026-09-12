using System.Diagnostics;
using System.Text.Json;

namespace TeamLauncher;

public static class UpdateService
{
    private const string DefaultVersionUrl = "https://raw.githubusercontent.com/teamstarwars-dev/Team-Luncher-/master/version.json";

    public static string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public static async Task CheckOnStartupAsync()
    {
        try
        {
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

        string tempExe = exePath + ".update";
        string batPath = exePath + ".update.bat";

        // Créer une ProgressBar dans le titre du formulaire
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
                {
                    int pct = (int)(read * 100 / total);
                    SetProgress($"Téléchargement… {pct}%");
                }
            }
            fs.Close();

            SetProgress("Installation…");

            // Script batch : attendre que l'exe se ferme, remplacer, relancer
            string batContent = $@"
@echo off
timeout /t 2 /nobreak >nul
del ""{exePath}"" 2>nul
move /y ""{tempExe}"" ""{exePath}"" >nul 2>&1
start """" ""{exePath}""
del ""%~f0""
";
            File.WriteAllText(batPath, batContent);

            // Lancer le batch puis quitter
            Process.Start(new ProcessStartInfo
            {
                FileName = batPath,
                UseShellExecute = true,
                CreateNoWindow = true
            });

            Environment.Exit(0);
        }
        catch
        {
            if (File.Exists(tempExe)) { try { File.Delete(tempExe); } catch { } }
            if (File.Exists(batPath)) { try { File.Delete(batPath); } catch { } }
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
