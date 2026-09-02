using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Applique un skin en mode HORS-LIGNE grâce au mod CustomSkinLoader :
/// le mod lit le fichier config\CustomSkinLoader\LocalSkin\<pseudo>.png de l'instance.
/// (Avec une connexion Microsoft officielle, le skin du vrai compte s'applique tout seul.)
/// </summary>
public static class SkinService
{
    // Utilise Http.Shared (client HTTP partagé)

    public static async Task<string> ApplyAsync(InstanceInfo inst, string skinPath)
    {
        string instDir = Path.Combine(DataStore.InstancesRoot, inst.Id);
        string modsDir = Path.Combine(instDir, "mods");
        Directory.CreateDirectory(modsDir);

        // 1. S'assure que CustomSkinLoader est installé dans l'instance
        var csl = Directory.GetFiles(modsDir)
            .FirstOrDefault(f => Path.GetFileName(f)
                .Contains("customskinloader", StringComparison.OrdinalIgnoreCase));

        if (csl == null)
        {
            csl = await InstallCustomSkinLoaderAsync(inst, modsDir);
        }

        // 2. Copie le skin sous le pseudo du joueur (source "LocalSkin" de CustomSkinLoader)
        string localSkinDir = Path.Combine(instDir, "config", "CustomSkinLoader", "LocalSkin");
        Directory.CreateDirectory(localSkinDir);
        File.Copy(skinPath,
            Path.Combine(localSkinDir, DataStore.Settings.PlayerName + ".png"),
            overwrite: true);

        return Path.GetFileName(csl!);
    }

    private static async Task<string> InstallCustomSkinLoaderAsync(InstanceInfo inst, string modsDir)
    {
        string mcVersion = inst.McVersion is "latest" or "?" or "" or null
            ? await MojangApi.LatestReleaseAsync() : inst.McVersion;

        // Dernière version Forge compatible avec la version Minecraft de l'instance
        string url =
            $"https://api.modrinth.com/v2/project/customskinloader/version" +
            $"?loaders={Uri.EscapeDataString("[\"forge\"]")}" +
            $"&game_versions={Uri.EscapeDataString($"[\"{mcVersion}\"]")}";
        using var doc = JsonDocument.Parse(await Http.Shared.GetStringAsync(url));
        if (doc.RootElement.GetArrayLength() == 0)
            throw new Exception(
                $"CustomSkinLoader introuvable pour Forge {mcVersion}.");

        JsonElement chosen = doc.RootElement[0];
        JsonElement file = chosen.GetProperty("files").EnumerateArray().FirstOrDefault(f =>
            f.TryGetProperty("primary", out var p) && p.GetBoolean());
        if (file.ValueKind == JsonValueKind.Undefined)
            file = chosen.GetProperty("files")[0];

        string fileName = file.GetProperty("filename").GetString()!;
        string dest = Path.Combine(modsDir, fileName);
        using (var resp = await Http.Shared.GetAsync(file.GetProperty("url").GetString()!,
                   HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(dest);
            await resp.Content.CopyToAsync(fs);
        }
        return dest;
    }
}
