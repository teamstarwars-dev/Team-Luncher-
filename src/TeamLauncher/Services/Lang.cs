using System.Diagnostics;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Multilingue : T("texte français", "english text") renvoie le texte selon
/// DataStore.Settings.Language ("fr" par défaut, "en" disponible).
/// Les pages appellent Lang.Apply(this) pour se traduire à l'affichage.
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
        ["Rejoindre"] = "Join",
        ["Déconnecté"] = "Disconnected",
        ["Déconnexion"] = "Sign out",
        ["Copier l'identifiant"] = "Copy UUID",
        ["UUID copié !"] = "UUID copied!",
        ["Identifiant copié !"] = "Username copied!",
        ["Nom"] = "Name",
        ["Adresse"] = "Address",
        ["Emoji"] = "Emoji",

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
        ["ACTUALITÉS"] = "NEWS",

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
        ["✘ Hors ligne"] = "✘ Offline",
        ["Rejoindre avec quelle instance ?"] = "Join with which instance?",
        ["Erreur en ajoutant le serveur :\n"] = "Error adding server:\n",
        ["Propriétaire (optionnel)"] = "Owner (optional)",

        // ---- réglages ----
        ["Activer la Rich Presence Discord"] = "Enable Discord Rich Presence",
        ["🧹 Libérer de l'espace (cache)"] = "🧹 Free up space (cache)",

        // ---- page d'accueil ----
        ["Reprends ta partie ou choisis une instance dans la liste."] = "Pick up where you left off or choose an instance from the list.",

        // ---- skins ----
        ["Explore les skins 3D de ta bibliothèque ou importe-en depuis Minecraft."] = "Browse your 3D skin library or import from Minecraft.",
        ["Tu n'as pas encore de skin changeant. Ouvre Minecraft, joue une partie pour en créer un !"] = "No custom skin yet. Launch Minecraft and play a game to create one!",
        ["Tu n'as pas de skin actif. Ouvre Minecraft pour en porter un !"] = "No skin yet. Launch Minecraft to get one!",

        // ---- explorateur ----
        ["Recherche un mod, modpack…"] = "Search for a mod, modpack…",

        // ---- explorateur fichiers ----
        ["Pas de contenu"] = "No content",
        ["Aucune instance sélectionnée"] = "No instance selected",
        ["Aucun monde trouvé"] = "No worlds found",

        // ---- bedrock ----
        ["Ouvre Minecraft Bedrock Edition, crée ou rejoins un monde en multijoueur. Le jeu apparaîtra ici dès qu'il sera détecté."] = "Open Minecraft Bedrock Edition, create or join a multiplayer world. The game will appear here once detected.",

        // ---- map editor ----
        ["🗺 Ouvrir MCA Selector"] = "🗺 Open MCA Selector",
        ["🌍 Ouvrir Amulet (3D)"] = "🌍 Open Amulet (3D)",
        ["📐 Ouvrir Blockbench (3D)"] = "📐 Open Blockbench (3D)",
        ["🏙 Générer une ville réelle (Arnis)"] = "🏙 Generate a real city (Arnis)",
        ["🗑 Supprimer la sélection"] = "🗑 Delete selection",
        ["✖ Tout désélectionner"] = "✖ Deselect all",
        ["💾 Sauvegarder le monde"] = "💾 Save world",
        ["🔧 Installer WorldEdit"] = "🔧 Install WorldEdit",
        ["Sélectionne une instance, puis un monde. Glisse la souris pour sélectionner des chunks,\npuis supprime-les (terres abandonnées, chunks corrompus, reset de zones...)."] = "Select an instance, then a world. Drag to select chunks,\nthen delete them (abandoned terrain, corrupted chunks, zone resets...).",
        ["Monde :"] = "World:",
        ["Instance :"] = "Instance:",
        ["Sélectionne d'abord un monde."] = "Select a world first.",
        ["Téléchargement de MCA Selector..."] = "Downloading MCA Selector...",
        ["MCA Selector téléchargé !"] = "MCA Selector downloaded!",
        ["Ouverture de MCA Selector..."] = "Opening MCA Selector...",
        ["Téléchargement d'Amulet (3D Editor)..."] = "Downloading Amulet (3D Editor)...",
        ["Extraction d'Amulet..."] = "Extracting Amulet...",
        ["Amulet téléchargé !"] = "Amulet downloaded!",
        ["Ouverture d'Amulet (3D Editor)..."] = "Opening Amulet (3D Editor)...",
        ["Java introuvable. Installe Java pour utiliser MCA Selector."] = "Java not found. Install Java to use MCA Selector.",
        ["Suppression terminée."] = "Deletion complete.",

        // ---- launch options ----
        ["Mémoire allouée (Go)"] = "Allocated memory (GB)",
        ["Arguments JVM (séparés par des espaces)"] = "JVM arguments (space-separated)",

        // ---- instance card ----
        ["{0} lancement(s)"] = "{0} launch(es)",

        // ---- instance edit ----
        ["Aucune instance sélectionnée."] = "No instance selected.",
        ["Aucune instance disponible."] = "No instance available.",
        ["Aucune instance trouvée. Crée-en une d'abord !"] = "No instance found. Create one first!",

        // ---- account choice ----
        ["Connecte-toi pour jouer avec ce compte."] = "Sign in to play with this account.",

        // ---- essential dialog ----
        ["Essential n'est pas installé dans cette instance."] = "Essential is not installed in this instance.",
        ["Clique sur « Installer Essential » pour l'ajouter automatiquement, ou installe-le manuellement depuis CurseForge."] = "Click \"Install Essential\" to add it automatically, or install it manually from CurseForge.",
        ["Essential est installé. Tu peux ouvrir le friends list avec le bouton ci-dessous ou via le menu du jeu."] = "Essential is installed. You can open the friends list with the button below or via the in-game menu.",
        ["Aucune instance disponible. Crée ou importe une instance d'abord."] = "No instance available. Create or import an instance first.",

        // ---- onboarding ----
        ["BENVENUE DANS TEAM LAUNCHER"] = "WELCOME TO TEAM LAUNCHER",
        ["Le launcher qui met le multijoueur en avant."] = "The launcher that puts multiplayer first.",
        ["Connecte un compte Microsoft pour jouer en ligne, ou crée un compte hors-ligne rapide."] = "Connect a Microsoft account to play online, or create a quick offline account.",
        ["Detecte les instances existantes depuis ton dossier .minecraft pour les ajouter au launcher."] = "Detect existing instances from your .minecraft folder to add them to the launcher.",
        ["Tout est en place ! Tu peux fermer et commencer à jouer."] = "Everything is ready! You can close this and start playing.",
        ["Bievenue dans Team Launcher !"] = "Welcome to Team Launcher!",
        ["Commençons par connecter un compte. Tu pourras en ajouter d'autres plus tard dans Réglages > Comptes."] = "Let's start by connecting an account. You can add more later in Settings > Accounts.",
        ["Tu peux détecter automatiquement les instances Minecraft déjà installées sur ton PC, ou en importer depuis un fichier Modrinth/CurseForge."] = "You can automatically detect Minecraft instances already installed on your PC, or import from a Modrinth/CurseForge file.",
        ["Tu peux also détecter les instances Minecraft déjà installées. Tu pourras les ajouter plus tard dans Instances > Explorer."] = "You can also detect already installed Minecraft instances. You can add them later in Instances > Explorer.",
        ["Installation terminée !"] = "Installation complete!",
        ["Tout est en place !"] = "All set!",
        ["Ferme cette fenêtre et commence à jouer."] = "Close this window and start playing.",

        // ---- server players ----
        ["Ce serveur n'est pas accessible."] = "This server is unreachable.",
        ["Requête de liste des joueurs échouée. Vérifie l'adresse et réessaie."] = "Player list query failed. Check the address and try again.",

        // ---- health / diagnostic ----
        ["Pas de profil Java pour cette version."] = "No Java profile for this version.",

        // ---- game installer ----
        ["Recherche de la version"] = "Searching for version",
        ["Lecture du cache"] = "Reading cache",
        ["Téléchargement du jeu"] = "Downloading game",
        ["Installation en cours"] = "Installing",

        // ---- app tasks ----
        ["Démarrage…"] = "Starting…",

        // ---- crash summary ----
        ["RESCAPÉE"] = "CRASH SUMMARY",
        ["Aucun crash récent détecté."] = "No recent crashes detected.",

        // ---- changelog ----
        ["NOUVEAU"] = "NEW",
        ["Instance Explorer : installe des modpacks depuis Modrinth ou CurseForge directement dans Team Launcher."] = "Instance Explorer: install modpacks from Modrinth or CurseForge directly in Team Launcher.",
        ["Se connecte automatiquement à ton compte Microsoft."] = "Automatically signs in to your Microsoft account.",
        ["Affiche un résumé détaillé des crashes (F8) avec une analyse automatique."] = "Displays a detailed crash summary (F8) with automatic analysis.",
        ["Améliore la stabilité du launcher et corrige plusieurs bugs."] = "Improved launcher stability and bug fixes.",

        // ---- home page ----
        ["Toutes les instances"] = "All instances",
        ["Aucune instance pour l'instant. Va dans « Instances » pour en créer une."] = "No instances yet. Go to \"Instances\" to create one.",
        ["Détails"] = "Details",

        // ---- instances page ----
        ["Instances"] = "Instances",
        ["Crée, importe, modifie ou supprime tes instances. Double-clique pour jouer."] = "Create, import, edit or delete your instances. Double-click to play.",
        ["Plus d'actions  ▾"] = "More actions  ▾",
        ["Rechercher…"] = "Search…",
        ["Installer un modpack CurseForge"] = "Install a CurseForge modpack",
        ["Colle le lien ou le code CurseForge :"] = "Paste the CurseForge link or code:",
        ["URL, ID projet, ou slug (ex: rlcraft, all-the-mods-9)"] = "URL, project ID, or slug (e.g. rlcraft, all-the-mods-9)",
        ["Installer"] = "Install",
        ["Recherche…"] = "Searching…",
        ["Téléchargement…"] = "Downloading…",
        ["Installation…"] = "Installing…",
        ["Comment veux-tu partager cette instance ?"] = "How do you want to share this instance?",
        ["Code de partage"] = "Share code",
        ["Code de partage :"] = "Share code:",
        ["Tes amis collent le lien ou le code dans « Importer un pack partagé »."] = "Your friends paste the link or code in \"Import a shared pack\".",
        ["Importer depuis CurseForge"] = "Import from CurseForge",
        ["Importer la sélection"] = "Import selection",
        ["Créer une instance"] = "Create instance",
        ["Choisir une image..."] = "Choose an image...",
        ["Créer l'instance"] = "Create instance",

        // ---- edit page ----
        ["Édition"] = "Edit",
        ["Modifie la carte d'une instance : nom, image, description."] = "Edit an instance: name, image, description.",
        ["Enregistrer les modifications"] = "Save changes",
        ["Restaurer"] = "Restore",
        ["Supprimer"] = "Delete",

        // ---- explorer page ----
        ["Explorateur"] = "Explorer",
        ["Fichiers et dossiers de tes instances."] = "Files and folders of your instances.",
        ["Ouvrir dans Windows"] = "Open in Windows",

        // ---- explore page ----
        ["Exploration"] = "Browse",
        ["Mods, modpacks et shaders de Modrinth ET CurseForge — Forge, Fabric, NeoForge, Quilt..."] = "Mods, modpacks and shaders from Modrinth AND CurseForge — Forge, Fabric, NeoForge, Quilt...",
        ["Rechercher"] = "Search",
        ["Téléchargement..."] = "Downloading...",

        // ---- bedrock page ----
        ["CHANGER DE MINECRAFT"] = "SWITCH MINECRAFT",
        ["Ton launcher gère Minecraft JAVA. Bascule ici sur Minecraft BEDROCK\n" + "(édition Microsoft Store, cross-play mobile / console / PC)."] = "Your launcher manages Minecraft JAVA. Switch to Minecraft BEDROCK here\n" + "(Microsoft Store edition, cross-play mobile / console / PC).",

        // ---- skins page ----
        ["Bibliothèque locale + aperçu 3D rotatif. Clic = sélectionner, double-clic = appliquer."] = "Local library + 3D rotating preview. Click = select, double-click to apply.",
        ["Aucun skin. Importe un fichier .png."] = "No skin. Import a .png file.",
        ["Application..."] = "Applying...",

        // ---- account page ----
        ["Choisis ton mode de connexion. Tu peux aussi le changer au lancement du launcher."] = "Choose your login method. You can also change it at launcher startup.",
        ["Compte Microsoft officiel (recommandé)"] = "Official Microsoft account (recommended)",
        ["Se connecter maintenant avec Microsoft"] = "Sign in with Microsoft now",
        ["Jouer hors-ligne (pseudo local)"] = "Play offline (local username)",
        ["Pseudo"] = "Username",

        // ---- settings page ----
        ["Enregistrer les paramètres"] = "Save settings",
        ["Couleurs par défaut"] = "Default colors",
        ["Retirer l'image"] = "Remove image",
        ["Vérifier maintenant"] = "Check now",
        ["Vérification..."] = "Checking...",
        ["Paramètres enregistrés. Redémarrer pour appliquer ?"] = "Settings saved. Restart to apply?",
        ["Couleurs réinitialisées. Redémarrer pour appliquer ?"] = "Colors reset. Restart to apply?",
        ["Image enregistrée ! Redémarrer pour l'appliquer ?"] = "Image saved! Restart to apply?",
        ["Image retirée. Redémarrer pour tout appliquer ?"] = "Image removed. Restart to apply?",
        ["Compteur FPS/RAM en jeu (optionnel) — installe un petit mod dans chaque instance compatible"] = "In-game FPS/RAM counter (optional) — installs a small mod in each compatible instance",
        ["Activer la Rich Presence Discord"] = "Enable Discord Rich Presence",
        ["ID d'application Discord (discord.com/developers/applications)"] = "Discord Application ID (discord.com/developers/applications)",
        ["URL du flux de mises à jour (Velopack, optionnel)"] = "Update feed URL (Velopack, optional)",
        ["Version installée : "] = "Installed version: ",
        ["Impossible de lire le journal."] = "Cannot read the log.",
        ["Chemin de Java (vide = détection automatique)"] = "Java path (empty = auto-detect)",
        ["URL des actualités (fichier JSON : title, date, tag, text)"] = "News URL (JSON file: title, date, tag, text)",
        ["Clé API CurseForge (console.curseforge.com — gratuite)"] = "CurseForge API key (console.curseforge.com — free)",

        // ---- servers page ----
        ["Relancer automatiquement si le serveur s'arrête anormalement (crash)"] = "Auto-restart if server stops abnormally (crash)",
        ["Redémarrage quotidien à (HH:mm, vide = aucun) :"] = "Daily restart at (HH:mm, empty = none):",
        ["Serveur arrêté requis."] = "Server stop required.",
        ["Envoyer"] = "Send",
        ["Impossible de charger la liste des versions."] = "Cannot load version list.",

        // ---- map editor ----
        ["Édition de carte"] = "Map editor",
        ["Sélectionne une instance, puis un monde. Glisse la souris pour sélectionner des chunks,\npuis supprime-les (terres abandonnées, chunks corrompus, reset de zones...)."] = "Select an instance, then a world. Drag to select chunks,\nthen delete them (abandoned terrain, corrupted chunks, zone resets...).",

        // ---- misc ----
        ["Serveur hébergé par Team Launcher"] = "Server hosted by Team Launcher",
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

    /// <summary>
    /// Redémarre le launcher proprement (ferme la fenêtre actuelle, relance l'exe).
    /// </summary>
    public static void RestartApp()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return;

            // Sauvegarder avant de quitter
            DataStore.Save();

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true
            });

            Environment.Exit(0);
        }
        catch { }
    }
}
