using System.Text.Json;

namespace TeamLauncher;

public static class MojangApi
{
    private static readonly HttpClient Http = new();
    private static List<string>? _releasesCache;

    public static async Task<List<string>> GetVersionsAsync()
    {
        var json = await Http.GetStringAsync(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("versions")
            .EnumerateArray()
            .Select(v => v.GetProperty("id").GetString() ?? "")
            .ToList();
    }

    /// <summary>Toutes les versions « release » officielles de Minecraft Java (de la plus récente à la plus ancienne).</summary>
    public static async Task<List<string>> GetReleasesAsync()
    {
        if (_releasesCache != null) return _releasesCache;
        var json = await Http.GetStringAsync(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
        using var doc = JsonDocument.Parse(json);
        _releasesCache = doc.RootElement.GetProperty("versions")
            .EnumerateArray()
            .Where(v => v.TryGetProperty("type", out var t) && t.GetString() == "release")
            .Select(v => v.GetProperty("id").GetString() ?? "")
            .ToList();
        return _releasesCache;
    }

    public static async Task<string> LatestReleaseAsync() => (await GetReleasesAsync())[0];

    /// <summary>UUID (avec tirets) d'un pseudo Minecraft, ou null si introuvable/hors ligne.</summary>
    public static async Task<string?> GetUuidAsync(string playerName)
    {
        try
        {
            var json = await Http.GetStringAsync(
                $"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(playerName)}");
            using var doc = JsonDocument.Parse(json);
            string id = doc.RootElement.GetProperty("id").GetString() ?? "";
            if (id.Length == 32) // sans tirets → avec tirets
                id = $"{id[0..8]}-{id[8..12]}-{id[12..16]}-{id[16..20]}-{id[20..]}";
            return id.Length == 36 ? id : null;
        }
        catch { return null; }
    }
}

public static class ModrinthApi
{
    private static readonly HttpClient Http = new();

    public sealed record Hit(string Title, string Slug, string Type, long Downloads,
        string Description, string Loaders, string IconUrl);

    private static readonly string[] KnownLoaders = { "forge", "fabric", "quilt", "neoforge", "rift" };

    /// <summary>Télécharge le dernier fichier compatible d'un projet Modrinth dans destDir.</summary>
    public static async Task<string> DownloadProjectFileAsync(string slug, string destDir,
        string? loader = null, string? mcVersion = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(loader))
            parts.Add($"loaders={Uri.EscapeDataString($"[\"{loader}\"]")}");
        if (!string.IsNullOrEmpty(mcVersion))
            parts.Add($"game_versions={Uri.EscapeDataString($"[\"{mcVersion}\"]")}");
        string url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(slug)}/version";
        if (parts.Count > 0) url += "?" + string.Join("&", parts);

        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        if (doc.RootElement.GetArrayLength() == 0)
            throw new Exception("Aucune version compatible trouvée sur Modrinth.");

        JsonElement file = default;
        foreach (var f in doc.RootElement[0].GetProperty("files").EnumerateArray())
        {
            if (!f.TryGetProperty("primary", out var p) || !p.GetBoolean()) continue;
            file = f.Clone();
            break;
        }
        if (file.ValueKind == JsonValueKind.Undefined)
            file = doc.RootElement[0].GetProperty("files")[0].Clone();

        Directory.CreateDirectory(destDir);
        string dest = Path.Combine(destDir,
            file.GetProperty("filename").GetString() ?? Guid.NewGuid().ToString("N"));
        using (var resp = await Http.GetAsync(file.GetProperty("url").GetString()!,
                   HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(dest);
            await resp.Content.CopyToAsync(fs);
        }
        return dest;
    }

    public static async Task<List<Hit>> SearchAsync(string query, string projectType)
    {
        var url = $"https://api.modrinth.com/v2/search?limit=25&query={Uri.EscapeDataString(query)}" +
                  $"&facets=%5B%5B%22project_type%3A{projectType}%22%5D%5D";
        var json = await Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("hits")
            .EnumerateArray()
            .Select(h =>
            {
                var loaders = h.TryGetProperty("categories", out var cats)
                    ? string.Join(" ", cats.EnumerateArray()
                        .Select(c => c.GetString() ?? "")
                        .Where(c => KnownLoaders.Contains(c)))
                    : "";
                return new Hit(
                    h.GetProperty("title").GetString() ?? "",
                    h.GetProperty("slug").GetString() ?? "",
                    h.GetProperty("project_type").GetString() ?? "",
                    h.TryGetProperty("downloads", out var d) ? d.GetInt64() : 0,
                    h.TryGetProperty("description", out var de) ? de.GetString() ?? "" : "",
                    loaders,
                    h.TryGetProperty("icon_url", out var ic) && ic.ValueKind == JsonValueKind.String
                        ? ic.GetString() ?? "" : "");
            })
            .ToList();
    }
}
