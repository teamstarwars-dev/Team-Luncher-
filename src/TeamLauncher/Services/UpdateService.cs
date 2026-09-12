using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace TeamLauncher;

public static class UpdateService
{
    private const string DefaultVersionUrl = "https://raw.githubusercontent.com/teamstarwars-dev/Team-Luncher-/master/version.json";

    public static string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(4) ?? "1.0.0.0";

    private static readonly string LogFile = Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath ?? ".") ?? ".",
        "update-debug.log");

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); } catch { }
    }

    public static async Task CheckOnStartupAsync()
    {
        try
        {
            Log($"=== Démarrage v{CurrentVersion} | ProcessPath={Environment.ProcessPath}");
            CleanupOldExe();

            var info = await CheckAsync();
            if (info == null)
            {
                Log("Pas de mise à jour disponible");
                return;
            }

            Log($"Mise à jour dispo: v{info.Value.Version} (local={CurrentVersion})");

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
                Log("Utilisateur a cliqué Oui");
                try
                {
                    await UpdateAsync(info.Value.Url, mainForm);
                }
                catch (Exception ex)
                {
                    Log($"ERREUR update: {ex}");
                    mainForm.BeginInvoke(() =>
                    {
                        MessageBox.Show(mainForm, "Erreur lors de la mise à jour :\n" + ex.Message,
                            "Team Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        mainForm.Text = "Team Launcher";
                    });
                }
            }
            else
            {
                Log("Utilisateur a cliqué Non");
            }
        }
        catch (Exception ex) { Log($"ERREUR CheckOnStartup: {ex}"); }
    }

    private static void CleanupOldExe()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exePath)) return;
            string dir = Path.GetDirectoryName(exePath) ?? "";
            string oldExe = exePath + ".old";
            string oldDll = Path.Combine(dir, "TeamLauncher.dll.old");
            string tempZip = Path.Combine(dir, "update-temp.zip");
            if (File.Exists(oldExe)) File.Delete(oldExe);
            if (File.Exists(oldDll)) File.Delete(oldDll);
            if (File.Exists(tempZip)) File.Delete(tempZip);
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

            Log($"Remote: version={latestVersion}, url={downloadUrl}");

            if (string.IsNullOrEmpty(latestVersion) || string.IsNullOrEmpty(downloadUrl))
                return null;

            if (Version.TryParse(latestVersion, out var latest) &&
                Version.TryParse(CurrentVersion, out var current))
            {
                Log($"Version compare: latest={latest} vs current={current} → latest>current={latest > current}");
            }

            if (Version.TryParse(latestVersion, out var latest2) &&
                Version.TryParse(CurrentVersion, out var current2) &&
                latest2 > current2)
            {
                return (latestVersion, downloadUrl, changelog);
            }
            return null;
        }
        catch (Exception ex) { Log($"ERREUR Check: {ex}"); return null; }
    }

    public static async Task UpdateAsync(string downloadUrl, Form owner)
    {
        string exePath = Environment.ProcessPath ?? "";
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            throw new Exception("Impossible de trouver l'exe en cours.");

        string dir = Path.GetDirectoryName(exePath) ?? "";
        string tempZip = Path.Combine(dir, "update-temp.zip");
        string oldExe = exePath + ".old";
        string oldDll = Path.Combine(dir, "TeamLauncher.dll.old");
        string dllPath = Path.Combine(dir, "TeamLauncher.dll");

        void SetProgress(string msg)
        {
            Log(msg);
            try { owner.BeginInvoke(() => owner.Text = $"Team Launcher — {msg}"); } catch { }
        }

        try
        {
            SetProgress("Téléchargement… 0%");

            using var resp = await Http.Shared.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? -1;
            Log($"Download: total={total} bytes");

            await using var fs = File.Create(tempZip);
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
            Log($"Download terminé: {read} bytes");

            SetProgress("Installation…");

            // Renommer les fichiers en cours d'exécution (autorisé sur Windows)
            if (File.Exists(oldExe)) File.Delete(oldExe);
            if (File.Exists(oldDll)) File.Delete(oldDll);
            File.Move(exePath, oldExe);
            if (File.Exists(dllPath)) File.Move(dllPath, oldDll);
            Log("Anciens fichiers renommés en .old");

            Log("Extraction du zip…");
            using (var zip = ZipFile.OpenRead(tempZip))
            {
                foreach (var entry in zip.Entries)
                {
                    string dest = Path.Combine(dir, entry.Name);
                    Log($"  Extraire {entry.Name} → {dest}");
                    entry.ExtractToFile(dest, overwrite: true);
                }
            }
            File.Delete(tempZip);
            Log("Extraction terminée");

            SetProgress("Relance…");
            Log($"Process.Start({exePath})");
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            Log("Nouveau process lancé");

            Log("Environment.Exit(0)");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log($"ERREUR update: {ex}");
            if (File.Exists(tempZip)) { try { File.Delete(tempZip); } catch { } }
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
