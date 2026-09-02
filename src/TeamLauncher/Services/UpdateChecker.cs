namespace TeamLauncher;

/// <summary>
/// Mise à jour du launcher via GitHub Releases + version.json.
/// Remplace l'ancien système Velopack.
/// </summary>
public static class UpdateChecker
{
    public static async Task CheckOnStartupAsync()
    {
        await UpdateService.CheckOnStartupAsync();
    }

    /// <summary>Vérification manuelle (bouton Paramètres) : renvoie un message à afficher.</summary>
    public static async Task<string> CheckNowAsync()
    {
        try
        {
            var info = await UpdateService.CheckAsync();
            if (info == null)
                return "Tu es déjà à la dernière version (v" + UpdateService.CurrentVersion + ").";

            return $"Nouvelle version disponible : v{info.Value.Version}\n\n" +
                   $"Tu es en v{UpdateService.CurrentVersion}\n\n" +
                   $"Changelog :\n{info.Value.Changelog}";
        }
        catch (Exception ex)
        {
            return "Impossible de vérifier les mises à jour :\n" + ex.Message;
        }
    }
}
