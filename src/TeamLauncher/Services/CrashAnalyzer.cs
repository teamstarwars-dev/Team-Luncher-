using System.Text.RegularExpressions;

namespace TeamLauncher;

/// <summary>
/// Lit les logs d'un plantage de Minecraft et traduit l'erreur technique
/// en explication claire avec la marche à suivre.
/// </summary>
public static class CrashAnalyzer
{
    private static readonly (Regex Pattern, string Advice)[] Rules =
    {
        (new Regex("OutOfMemoryError", RegexOptions.IgnoreCase),
            "Minecraft a manqué de mémoire.\n→ Augmente la RAM allouée (⚙ Options de lancement de l'instance, ou Paramètres)."),
        (new Regex("UnsupportedClassVersionError", RegexOptions.IgnoreCase),
            "La version de Java ne correspond pas à cette version de Minecraft.\n→ Laisse le launcher télécharger le bon Java, ou installe-le depuis adoptium.net."),
        (new Regex("NoClassDefFoundError|ClassNotFoundException", RegexOptions.IgnoreCase),
            "Un mod est manquant, corrompu ou incompatible.\n→ Retire le dernier mod ajouté, ou répare l'instance."),
        (new Regex("Invalid session|Failed to verify authentication", RegexOptions.IgnoreCase),
            "Ta session de jeu a expiré.\n→ Relance simplement : le launcher se reconnecte automatiquement à Microsoft."),
        (new Regex("Pixel format not accelerated|OpenGL", RegexOptions.IgnoreCase),
            "Problème de pilote graphique.\n→ Mets à jour les pilotes de ta carte graphique (NVIDIA / AMD / Intel)."),
        (new Regex("Could not reserve enough space for object heap", RegexOptions.IgnoreCase),
            "Pas assez de RAM libre pour la quantité demandée.\n→ Réduis la RAM allouée dans ⚙ Options de lancement."),
        (new Regex("Access is denied|AccessDeniedException", RegexOptions.IgnoreCase),
            "Un fichier du jeu est bloqué (antivirus ?).\n→ Ajoute une exclusion pour le dossier Team Launcher dans ton antivirus."),
        (new Regex("UnknownHostException|Connection timed out|Connection refused", RegexOptions.IgnoreCase),
            "Problème de connexion Internet pendant le chargement.\n→ Vérifie ta connexion et relance."),
        (new Regex("DuplicateModsFoundException|ModResolutionException", RegexOptions.IgnoreCase),
            "Conflit entre mods (doublon ou incompatibilité).\n→ Retire les doublons du dossier mods de l'instance."),
    };

    /// <summary>Retourne l'explication trouvée, ou null si rien de connu n'a été détecté.</summary>
    public static string? Analyze(string logText)
    {
        foreach (var (pattern, advice) in Rules)
        {
            if (pattern.IsMatch(logText)) return advice;
        }
        return null;
    }

    /// <summary>Analyse les journaux d'une instance après un plantage.</summary>
    public static string? AnalyzeInstance(string gameDir)
    {
        try
        {
            // 1. rapport de crash officiel (le plus parlant)
            string crashes = Path.Combine(gameDir, "crash-reports");
            if (Directory.Exists(crashes))
            {
                var newest = Directory.GetFiles(crashes, "*.txt")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();
                if (newest != null && DateTime.UtcNow - newest.LastWriteTime.ToUniversalTime() < TimeSpan.FromMinutes(5))
                    return Analyze(File.ReadAllText(newest.FullName)) ??
                           "Minecraft a planté (rapport : crash-reports).";
            }

            // 2. fin du journal du jeu
            string gameLog = Path.Combine(gameDir, "game-log.txt");
            if (File.Exists(gameLog))
            {
                var lines = File.ReadLines(gameLog);
                int total = lines.Count();
                string tail = string.Join("\n",
                    lines.Skip(Math.Max(0, total - 300)));
                return Analyze(tail);
            }
        }
        catch { }
        return null;
    }
}
