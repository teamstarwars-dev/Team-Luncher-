using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Installation d'Essential (mod Social/cosmétiques) en un clic dans une instance.
/// Essential s'installe comme un mod Fabric ; son menu latéral (Social, Settings...)
/// apparaît ensuite à l'intérieur de Minecraft.
/// </summary>
public static class EssentialService
{
    // Utilise Http.Shared (client HTTP partagé)

    private const string ProjectId = "essential"; // slug Modrinth officiel

    public static async Task InstallAsync(InstanceInfo inst)
    {
        // Résout la version Minecraft effective de l'instance
        string mcVersion = inst.McVersion is "latest" or "?" or "" or null
            ? await LatestReleaseAsync() : inst.McVersion;

        // Dernière version d'Essential compatible Fabric pour cette version de MC
        string url =
            $"https://api.modrinth.com/v2/project/{ProjectId}/version" +
            $"?loaders={Uri.EscapeDataString("[\"fabric\"]")}" +
            $"&game_versions={Uri.EscapeDataString($"[\"{mcVersion}\"]")}";
        using var doc = JsonDocument.Parse(await Http.Shared.GetStringAsync(url));
        if (doc.RootElement.GetArrayLength() == 0)
            throw new Exception(
                $"Aucune version d'Essential trouvée pour Fabric {mcVersion}.\n" +
                "Vérifie que cette version de Minecraft est supportée.");

        // Fichier principal de la première version compatible
        var files = doc.RootElement[0].GetProperty("files").EnumerateArray();
        JsonElement chosen = default;
        foreach (var f in files)
        {
            if (!f.TryGetProperty("primary", out var p) || !p.GetBoolean()) continue;
            chosen = f;
            break;
        }
        if (chosen.ValueKind == JsonValueKind.Undefined)
            chosen = doc.RootElement[0].GetProperty("files")[0];

        string fileName = chosen.GetProperty("filename").GetString()!;
        string downloadUrl = chosen.GetProperty("url").GetString()!;

        // Dossier mods de l'instance
        string modsDir = Path.Combine(DataStore.InstancesRoot, inst.Id, "mods");
        Directory.CreateDirectory(modsDir);
        string dest = Path.Combine(modsDir, fileName);

        using (var resp = await Http.Shared.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(dest);
            await resp.Content.CopyToAsync(fs);
        }

        // Essential tourne sur Fabric : on met à jour la carte si besoin
        if (inst.Loader is "Vanilla" or "?" or "")
        {
            inst.Loader = "Fabric";
            DataStore.Save();
        }
    }

    private static Task<string> LatestReleaseAsync() => MojangApi.LatestReleaseAsync();
}
