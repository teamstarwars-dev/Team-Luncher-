namespace TeamLauncher;

/// <summary>
/// Multilingue : T("texte français", "english text") renvoie le texte selon
/// DataStore.Settings.Language ("fr" par défaut, "en" disponible).
/// Les pages appellent Lang.Apply(this) pour se traduire à l'affichage.
/// La couverture des textes est progressive : les nouveaux écrans ajoutent
/// simplement leurs chaînes via Lang.T(...).
/// </summary>
public static class Lang
{
    public static string Current =>
        DataStore.Settings.Language == "en" ? "en" : "fr";

    public static bool IsEn => Current == "en";

    /// <summary>Retourne le texte dans la langue active.</summary>
    public static string T(string fr, string en) => IsEn ? en : fr;

    /// <summary>
    /// Traduit automatiquement les Labels/Boutons d'un conteneur grâce aux
    /// correspondances ci-dessous (appliqué récursivement).
    /// </summary>
    private static readonly Dictionary<string, string> FrToEn = new()
    {
        // ---- navigation ----
        ["🏠  Accueil"] = "🏠  Home",
        ["📰  Actualités"] = "📰  News",
        ["📦  Tes instances"] = "📦  Your instances",
        ["🗂️  Explorateur"] = "🗂️  Explorer",
        ["👕  Skins"] = "👕  Skins",
        ["🔍  Exploration"] = "🔍  Browse",
        ["🌐  Serveurs"] = "🌐  Servers",
        ["🪨  Bedrock"] = "🪨  Bedrock",
        ["🗺️  Édition de carte"] = "🗺️  Map editor",
        ["👤  Compte"] = "👤  Account",
        ["⚙️  Paramètres"] = "⚙️  Settings",

        // ---- boutons courants ----
        ["▶  Jouer"] = "▶  Play",
        ["✎ Modifier cette instance"] = "✎ Edit this instance",
        ["⚙ Options de lancement (RAM, JVM)"] = "⚙ Launch options (RAM, JVM)",
        ["📂 Ouvrir le dossier"] = "📂 Open folder",
        ["📄 Voir les logs du jeu"] = "📄 View game logs",
        ["＋ Ajouter"] = "＋ Add",
        ["🗑 Retirer"] = "🗑 Remove",
        ["⟳ Actualiser"] = "⟳ Refresh",
        ["Enregistrer"] = "Save",
        ["Appliquer"] = "Apply",
        ["Appliquer aux instances"] = "Apply to instances",
        ["Envoyer"] = "Send",
        ["Annuler"] = "Cancel",
        ["Fermer"] = "Close",

        // ---- titres de sections ----
        ["Paramètres"] = "Settings",
        ["Compte"] = "Account",
        ["SERVEURS FAVORIS"] = "FAVORITE SERVERS",
        ["MES SERVEURS"] = "MY SERVERS",
        ["🖥  MES SERVEURS"] = "🖥  MY SERVERS",
        ["⭐  SERVEURS FAVORIS"] = "⭐  FAVORITE SERVERS",
        ["SKINS"] = "SKINS",
        ["TES INSTANCES"] = "YOUR INSTANCES",
        ["TEMPS DE JEU PAR INSTANCE"] = "PLAYTIME PER INSTANCE",
        ["MISES À JOUR AUTOMATIQUES"] = "AUTOMATIC UPDATES",
        ["ACTUALITÉS & LANGUE"] = "NEWS & LANGUAGE",

        // ---- page serveurs ----
        ["＋ CRÉER LE SERVEUR"] = "＋ CREATE SERVER",
        ["▶  Démarrer"] = "▶  Start",
        ["🖥  Console"] = "🖥  Console",
        ["🗺  Map & Mods"] = "🗺  Map & Mods",
        ["⚙  Réglages"] = "⚙  Settings",
        ["⏹ Arrêter le serveur"] = "⏹ Stop server",
        ["▶ Démarrer le serveur"] = "▶ Start server",
        ["✓ Adresse copiée !"] = "✓ Address copied!",
        ["🗺 Importer une map"] = "🗺 Import a map",
        ["🧩 Gérer les mods"] = "🧩 Manage mods",
        ["🌍 Ouvrir sur Internet"] = "🌍 Open to Internet",
        ["⚙ server.properties…"] = "⚙ server.properties…",
        ["🖼 Changer l'icône"] = "🖼 Change icon",
        ["💾 Enregistrer"] = "💾 Save",
        ["➕ Ajouter des .jar"] = "➕ Add .jar files",
        ["🗑 Supprimer cochés"] = "🗑 Delete checked",
        ["↩ Restaurer cette sauvegarde"] = "↩ Restore this backup",
        ["🗂 Sauvegardes du monde…"] = "🗂 World backups…",
        ["💾 Sauvegarder maintenant"] = "💾 Back up now",

        // ---- réglages ----
        ["Activer la Rich Presence Discord"] = "Enable Discord Rich Presence",
        ["🧹 Libérer de l'espace (cache)"] = "🧹 Free up space (cache)",
    };

    public static void Apply(Control root)
    {
        try
        {
            if (!IsEn) return;
            if (root is Label lbl && FrToEn.TryGetValue(lbl.Text, out var lt)) { lbl.Text = lt; }
            else if (root is Button btn && FrToEn.TryGetValue(btn.Text, out var bt))
            {
                btn.Text = bt;
            }

            foreach (Control child in root.Controls)
                Apply(child);
        }
        catch { }
    }
}
