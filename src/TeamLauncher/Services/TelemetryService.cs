using System.Text;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Télémétrie distante via Discord webhook.
/// Envoie les rapports de crash, stats de lancement et diagnostics.
/// Activable/désactivable dans les paramètres.
/// </summary>
public static class TelemetryService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Envoie un rapport de crash Minecraft dans Discord.</summary>
    public static void ReportCrash(InstanceInfo inst, int exitCode, string? gameLogTail = null)
    {
        if (!IsEnabled) return;
        var webhook = DataStore.Settings.DiscordTelemetryWebhook;
        if (string.IsNullOrWhiteSpace(webhook)) return;

        var sb = new StringBuilder();
        sb.AppendLine($"**Crash Minecraft** — {inst.Name}");
        sb.AppendLine($"```");
        sb.AppendLine($"Instance  : {inst.Name} ({inst.Id})");
        sb.AppendLine($"Loader    : {inst.Loader}");
        sb.AppendLine($"Version   : Minecraft {inst.McVersion}");
        sb.AppendLine($"Exit code : {exitCode}");
        sb.AppendLine($"Date      : {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"Lancements: {inst.Launches}");
        sb.AppendLine($"```");

        if (!string.IsNullOrEmpty(gameLogTail))
        {
            string tail = gameLogTail.Length > 1800
                ? "..." + gameLogTail[^1800..]
                : gameLogTail;
            sb.AppendLine("**Dernières lignes du log :**");
            sb.AppendLine($"```");
            sb.AppendLine(tail);
            sb.AppendLine($"```");
        }

        SendEmbed(webhook, "Crash Minecraft", sb.ToString(), 0xE74C3C);
    }

    /// <summary>Envoie un rapport de crash du launcher lui-même.</summary>
    public static void ReportLauncherCrash(Exception ex, string context = "")
    {
        if (!IsEnabled) return;
        var webhook = DataStore.Settings.DiscordTelemetryWebhook;
        if (string.IsNullOrWhiteSpace(webhook)) return;

        var sb = new StringBuilder();
        sb.AppendLine($"**Crash du Launcher**");
        sb.AppendLine($"```");
        sb.AppendLine($"Version   : {UpdateService.CurrentVersion}");
        sb.AppendLine($"OS        : {Environment.OSVersion}");
        sb.AppendLine($"64-bit    : {Environment.Is64BitOperatingSystem}");
        sb.AppendLine($"Date      : {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        if (context.Length > 0) sb.AppendLine($"Contexte  : {context}");
        sb.AppendLine($"```");
        sb.AppendLine($"**Exception :**");
        sb.AppendLine($"```");
        sb.AppendLine(ex.ToString().Length > 1800
            ? ex.ToString()[..1800] + "..."
            : ex.ToString());
        sb.AppendLine($"```");

        SendEmbed(webhook, "Crash du Launcher", sb.ToString(), 0xE67E22);
    }

    /// <summary>Envoie les stats de lancement d'une instance.</summary>
    public static void ReportLaunch(InstanceInfo inst)
    {
        if (!IsEnabled) return;
        var webhook = DataStore.Settings.DiscordTelemetryWebhook;
        if (string.IsNullOrWhiteSpace(webhook)) return;

        var sb = new StringBuilder();
        sb.AppendLine($"**Lancement** — {inst.Name}");
        sb.AppendLine($"```");
        sb.AppendLine($"Loader    : {inst.Loader}");
        sb.AppendLine($"Version   : Minecraft {inst.McVersion}");
        sb.AppendLine($"RAM       : {(inst.MaxRamGb > 0 ? inst.MaxRamGb + " Go" : "globale")}");
        sb.AppendLine($"Lancements: {inst.Launches}");
        sb.AppendLine($"Date      : {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"```");

        SendEmbed(webhook, "Lancement", sb.ToString(), 0x3498DB);
    }

    /// <summary>Envoie un diagnostic complet au démarrage (silencieux, 1 fois par session).</summary>
    public static async Task ReportStartupAsync()
    {
        if (!IsEnabled) return;
        var webhook = DataStore.Settings.DiscordTelemetryWebhook;
        if (string.IsNullOrWhiteSpace(webhook)) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"**Démarrage du Launcher**");
            sb.AppendLine($"```");
            sb.AppendLine($"Version     : {UpdateService.CurrentVersion}");
            sb.AppendLine($"OS          : {Environment.OSVersion}");
            sb.AppendLine($"64-bit      : {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"Instances   : {DataStore.Settings.Instances.Count}");
            sb.AppendLine($"Compte      : {DataStore.Settings.AccountMode}");
            sb.AppendLine($"Java custom : {(string.IsNullOrEmpty(DataStore.Settings.JavaPath) ? "non" : "oui")}");
            sb.AppendLine($"Date        : {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine($"```");

            // Diagnostic Java rapide
            bool hasJava8 = await Task.Run(() => GameLauncher.FindJava(8) != null);
            bool hasJava17 = await Task.Run(() => GameLauncher.FindJava(17) != null);
            bool hasJava21 = await Task.Run(() => GameLauncher.FindJava(21) != null);
            sb.AppendLine($"**Java** : 8={(hasJava8 ? "✔" : "✘")} 17={(hasJava17 ? "✔" : "✘")} 21={(hasJava21 ? "✔" : "✘")}");

            await SendEmbedAsync(webhook, "Démarrage", sb.ToString(), 0x2ECC71);
        }
        catch { /* non bloquant */ }
    }

    /// <summary>Envoie un diagnostic quand une instance est supprimée.</summary>
    public static void ReportInstanceDeleted(InstanceInfo inst)
    {
        if (!IsEnabled) return;
        var webhook = DataStore.Settings.DiscordTelemetryWebhook;
        if (string.IsNullOrWhiteSpace(webhook)) return;

        var sb = new StringBuilder();
        sb.AppendLine($"**Instance supprimée** — {inst.Name}");
        sb.AppendLine($"```");
        sb.AppendLine($"ID        : {inst.Id}");
        sb.AppendLine($"Loader    : {inst.Loader}");
        sb.AppendLine($"Version   : Minecraft {inst.McVersion}");
        sb.AppendLine($"Lancements: {inst.Launches}");
        sb.AppendLine($"Date      : {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"```");

        SendEmbed(webhook, "Instance supprimée", sb.ToString(), 0x9B59B6);
    }

    // ---- helpers ----

    private static bool IsEnabled => DataStore.Settings.TelemetryEnabled;

    private static void SendEmbed(string webhookUrl, string title, string description, int color)
    {
        _ = Task.Run(async () => await SendEmbedAsync(webhookUrl, title, description, color));
    }

    private static async Task SendEmbedAsync(string webhookUrl, string title, string description, int color)
    {
        try
        {
            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = $"Team Launcher — {title}",
                        description = description,
                        color = color,
                        footer = new { text = $"Team Launcher v{UpdateService.CurrentVersion}" },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(webhookUrl, content);
            // Silencieux en cas d'échec (webhook incorrect, Discord down...)
        }
        catch { }
    }
}
