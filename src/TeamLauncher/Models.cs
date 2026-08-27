namespace TeamLauncher;

public class InstanceInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string Loader { get; set; } = "Vanilla";
    public string McVersion { get; set; } = "latest";
    public int Launches { get; set; }
    public long PlaySeconds { get; set; }
    public int MaxRamGb { get; set; } // 0 = utiliser le réglage global
    public string JvmArgs { get; set; } = ""; // arguments JVM supplémentaires (optionnel)
    public string Notes { get; set; } = "";
    public DateTime LastPlayed { get; set; } = DateTime.MinValue;
}

public class HostedServer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string McVersion { get; set; } = "";
    public string Loader { get; set; } = "Vanilla"; // Vanilla ou Fabric
    public int Port { get; set; } = 25565;
    public string Motd { get; set; } = "Serveur hébergé par Team Launcher";
    public int MaxRamGb { get; set; } = 2;
    public int JavaMajor { get; set; } = 8;
    public bool AutoRestart { get; set; } = true;   // relancer si le serveur s'arrête anormalement
    public string RestartAt { get; set; } = "";     // redémarrage quotidien "HH:mm" (vide = désactivé)
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string PublicAddress { get; set; } = ""; // adresse publique playit.gg détectée/mémorisée
    public bool RpProfile { get; set; } = false;    // profil « ville RP » : whitelist, command blocks…
    public bool WhitelistEnabled { get; set; } = false;
    public List<string> Whitelist { get; set; } = new();
    public string WelcomeMessage { get; set; } = ""; // envoyé quand un joueur rejoint ({joueur} = pseudo)
}

/// <summary>Ville RP d'un membre de la team : serveur favori avec identité.</summary>
public class TeamCity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Address { get; set; } = "";
    public string Description { get; set; } = "";
}

public class AppSettings
{
    public string PlayerName { get; set; } = "Joueur";
    public string AccountMode { get; set; } = ""; // "microsoft" ou "offline"
    public string JavaPath { get; set; } = "";
    public int MaxRamGb { get; set; } = 4;
    public string AzureClientId { get; set; } = "";
    public string InstancesDir { get; set; } = "";
    public string BgColor { get; set; } = "";
    public string CardColor { get; set; } = "";
    public string AccentColor { get; set; } = "";
    public string BackgroundImagePath { get; set; } = ""; // image de fond du launcher (vide = aucune)
    public bool FpsCounterEnabled { get; set; } = false;
    public bool DiscordEnabled { get; set; } = false;
    public string DiscordAppId { get; set; } = "";
    public string UpdateUrl { get; set; } = ""; // flux Velopack (optionnel) pour l'auto-mise à jour
    public string NewsUrl { get; set; } = "";   // flux JSON des actualités affichées dans le launcher
    public string Language { get; set; } = "fr"; // "fr" ou "en"
    public string CurseForgeApiKey { get; set; } = ""; // clé API CurseForge (console.curseforge.com)
    public bool OnboardingDone { get; set; } = false;  // assistant de premier lancement déjà passé
    public List<InstanceInfo> Instances { get; set; } = new();
    public List<string> Servers { get; set; } = new();
    public List<TeamCity> Cities { get; set; } = new();
    public List<HostedServer> HostedServers { get; set; } = new();
    public bool AutoShortcut { get; set; } = false;
}

public interface IRefreshable
{
    void RefreshData();
}

public static class AppEvents
{
    /// <summary>Demande de navigation : "home", "instances", "edit", ...</summary>
    public static event Action<string>? NavigationRequested;
    public static void NavigateTo(string key) => NavigationRequested?.Invoke(key);

    /// <summary>Le compte actif a changé (connexion Microsoft, pseudo hors-ligne...).</summary>
    public static event Action? AccountChanged;
    public static void NotifyAccountChanged() => AccountChanged?.Invoke();

    /// <summary>Instance à présélectionner dans la page Édition.</summary>
    public static string? PendingEditId { get; set; }

    /// <summary>Instance à afficher dans la page Détails.</summary>
    public static string? PendingDetailId { get; set; }

    /// <summary>Onglet à ouvrir dans la page Détails (ex: "🧩 Mods", "🌍 Mondes").</summary>
    public static string? PendingDetailTab { get; set; }
}
