using Velopack;

namespace TeamLauncher;

/// <summary>
/// Mise à jour automatique du launcher via Velopack.
/// Le flux (URL) se configure dans Paramètres ; tant qu'il est vide, rien ne se passe.
/// Publication d'une nouvelle version côté dev :
///   dotnet vpk pack -u TeamLauncher -v 1.0.1 -p dist-autonome -e TeamLauncher.exe
/// puis envoi des fichiers générés vers l'URL du flux (ex. GitHub Releases).
/// </summary>
public static class UpdateChecker
{
    public static async Task CheckOnStartupAsync()
    {
        string url = DataStore.Settings.UpdateUrl.Trim();
        if (url.Length == 0) return;
        try
        {
            var mgr = new UpdateManager(url);
            if (!mgr.IsInstalled) return; // lancé en mode dev / non packagé

            var info = await mgr.CheckForUpdatesAsync();
            if (info == null) return;

            Notifier.Show("Mise à jour",
                $"Version {info.TargetFullRelease.Version} disponible, téléchargement…");
            await mgr.DownloadUpdatesAsync(info);
            mgr.ApplyUpdatesAndRestart(info); // applique et relance le launcher
            Notifier.Show("Mise à jour",
                "Prête ! Elle sera appliquée au prochain lancement du launcher.");
        }
        catch
        {
            // jamais de blocage au démarrage à cause des mises à jour
        }
    }

    /// <summary>Vérification manuelle (bouton Paramètres) : renvoie un message à afficher,
    /// ou une chaîne vide si une mise à jour a été appliquée (le launcher redémarre).</summary>
    public static async Task<string> CheckNowAsync()
    {
        string url = DataStore.Settings.UpdateUrl.Trim();
        if (url.Length == 0)
            return "Aucune URL de mise à jour configurée.\n" +
                   "Colle-la dans le champ ci-dessus puis enregistre.";

        try
        {
            var mgr = new UpdateManager(url);
            if (!mgr.IsInstalled)
                return "Launcher lancé en mode développement (non packagé) :\n" +
                       "les mises à jour automatiques sont désactivées.";

            var info = await mgr.CheckForUpdatesAsync();
            if (info == null)
                return $"Tu es déjà à la dernière version ({mgr.CurrentVersion}).";

            await mgr.DownloadUpdatesAsync(info);
            mgr.ApplyUpdatesAndRestart(info); // applique et relance le launcher
            return "";
        }
        catch (Exception ex)
        {
            return "Impossible de vérifier les mises à jour :\n" + ex.Message;
        }
    }
}
