using System.Text;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Client de l'API CurseForge (api.curseforge.com).
/// La clé API se configure dans les Paramètres (gratuite sur console.curseforge.com)
/// ou via la variable d'environnement CURSEFORGE_API_KEY.
/// </summary>
public static class CurseForgeApi
{
    // Utilise Http.Shared (client HTTP partagé)

    private const int MinecraftGameId = 432;

    // classes de projets CurseForge
    public const int ClassMods = 6;
    public const int ClassModpacks = 4471;
    public const int ClassResourcePacks = 12;
    public const int ClassShaders = 6552;

    public sealed record Hit(int ProjectId, string Slug, string Title, long Downloads,
        string Description, string Loaders, string IconUrl);

    public sealed record CfFile(long FileId, int ModId, string FileName, string DisplayName,
        string? DownloadUrl, List<string> GameVersions, List<string> Loaders, DateTime FileDate);

    private static string ApiKey =>
        DataStore.Settings.CurseForgeApiKey is { Length: > 0 } k ? k
            : Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY") ?? "";

    private static void EnsureKey()
    {
        if (ApiKey.Length == 0)
            throw new Exception(
                "Clé API CurseForge manquante.\n\n" +
                "1. Crée un compte développeur sur console.curseforge.com\n" +
                "2. Génère une clé API (gratuite, immédiate)\n" +
                "3. Colle-la dans Paramètres → Clé API CurseForge");
    }

    private static async Task<JsonElement> GetAsync(string path)
    {
        EnsureKey();
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.curseforge.com/v1" + path);
        req.Headers.Add("x-api-key", ApiKey);
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        using var resp = await Http.Shared.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement.Clone();
    }

    private static async Task<JsonElement> PostAsync(string path, string jsonBody)
    {
        EnsureKey();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.curseforge.com/v1" + path)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", ApiKey);
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        using var resp = await Http.Shared.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement.Clone();
    }

    private static readonly string[] KnownLoaders = { "forge", "fabric", "neoforge", "quilt" };

    /// <summary>Recherche de contenu CurseForge pour Minecraft.</summary>
    public static async Task<List<Hit>> SearchAsync(string query, int classId)
    {
        string url = $"mods/search?gameId={MinecraftGameId}&classId={classId}" +
                     $"&sortField=2&sortOrder=desc&pageSize=25" +
                     (query.Length > 0 ? "&searchFilter=" + Uri.EscapeDataString(query) : "");
        var root = await GetAsync(url);
        var list = new List<Hit>();
        foreach (var m in root.GetProperty("data").EnumerateArray())
        {
            var loaders = m.TryGetProperty("latestFilesIndexes", out var idx)
                ? string.Join(" ", idx.EnumerateArray()
                    .Select(i => i.TryGetProperty("modLoader", out var ml) ? LoaderName(ml.GetInt32()) : "")
                    .Where(l => l.Length > 0).Distinct())
                : "";
            list.Add(new Hit(
                m.GetProperty("id").GetInt32(),
                m.TryGetProperty("slug", out var sl) ? sl.GetString() ?? "" : "",
                m.GetProperty("name").GetString() ?? "",
                m.TryGetProperty("downloadCount", out var dc) ? (long)dc.GetDouble() : 0,
                m.TryGetProperty("summary", out var su) ? su.GetString() ?? "" : "",
                loaders,
                m.TryGetProperty("logo", out var lg) && lg.ValueKind == JsonValueKind.Object &&
                    lg.TryGetProperty("thumbnailUrl", out var tu) && tu.ValueKind == JsonValueKind.String
                    ? tu.GetString() ?? "" : ""));
        }
        return list;
    }

    private static string LoaderName(int modLoader) => modLoader switch
    {
        1 => "forge",
        4 => "fabric",
        5 => "quilt",
        6 => "neoforge",
        _ => ""
    };

    /// <summary>Liste des fichiers d'un projet (du plus récent au plus ancien).</summary>
    public static async Task<List<CfFile>> GetFilesAsync(int projectId)
    {
        var root = await GetAsync($"mods/{projectId}/files");
        var list = new List<CfFile>();
        foreach (var f in root.GetProperty("data").EnumerateArray())
            list.Add(ParseFile(f));
        return list.OrderByDescending(f => f.FileDate).ToList();
    }

    private static CfFile ParseFile(JsonElement f)
    {
        var gv = new List<string>();
        if (f.TryGetProperty("gameVersions", out var arr))
            foreach (var g in arr.EnumerateArray()) gv.Add(g.GetString() ?? "");
        var loaders = gv.Where(v => KnownLoaders.Contains(v)).ToList();
        // les versions de jeu sont les entrées non-loader et non-java
        gv.RemoveAll(v => KnownLoaders.Contains(v) || v.StartsWith("Java") || v == "Vanilla");
        return new CfFile(
            f.GetProperty("id").GetInt64(),
            f.GetProperty("modId").GetInt32(),
            f.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "",
            f.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "",
            f.TryGetProperty("downloadUrl", out var du) && du.ValueKind == JsonValueKind.String
                ? du.GetString() : null,
            gv, loaders,
            f.TryGetProperty("fileDate", out var fd) ? fd.GetDateTime() : DateTime.MinValue);
    }

    /// <summary>Récupère en lot les détails de fichiers (POST /v1/mods/files).</summary>
    public static async Task<List<CfFile>> GetFilesByIdsAsync(IEnumerable<long> fileIds)
    {
        var result = new List<CfFile>();
        var ids = fileIds.ToList();
        for (int i = 0; i < ids.Count; i += 64)
        {
            var batch = ids.Skip(i).Take(64).ToList();
            var root = await PostAsync("mods/files",
                JsonSerializer.Serialize(new { fileIds = batch }));
            foreach (var f in root.GetProperty("data").EnumerateArray())
                result.Add(ParseFile(f));
        }
        return result;
    }

    /// <summary>Récupère en lot les infos de projets (classe → dossier de destination).</summary>
    public static async Task<Dictionary<int, int>> GetProjectClassesAsync(IEnumerable<int> projectIds)
    {
        var map = new Dictionary<int, int>();
        var ids = projectIds.Distinct().ToList();
        for (int i = 0; i < ids.Count; i += 64)
        {
            var batch = ids.Skip(i).Take(64).ToList();
            var root = await PostAsync("mods", JsonSerializer.Serialize(new { modIds = batch }));
            foreach (var m in root.GetProperty("data").EnumerateArray())
            {
                int id = m.GetProperty("id").GetInt32();
                map[id] = m.TryGetProperty("classId", out var c) && c.ValueKind == JsonValueKind.Number
                    ? c.GetInt32() : ClassMods;
            }
        }
        return map;
    }

    /// <summary>Télécharge un fichier CurseForge vers destDir (URL directe ou CDN de secours).
    /// Depuis juillet 2026, le CDN CurseForge exige la clé API (header x-api-key).</summary>
    public static async Task<string> DownloadFileAsync(CfFile file, string destDir,
        Action<long, long>? progress = null, CancellationToken ct = default)
    {
        EnsureKey();
        Directory.CreateDirectory(destDir);
        string dest = Path.Combine(destDir, Sanitize(file.FileName));
        string url = file.DownloadUrl ??
                     $"https://www.curseforge.com/api/v1/mods/{file.ModId}/files/{file.FileId}/download";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("x-api-key", ApiKey);
        using var resp = await Http.Shared.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? -1;
        await using var fs = File.Create(dest);
        if (progress != null && total > 0)
        {
            var buffer = new byte[81920];
            long read = 0;
            int n;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                progress(read, total);
            }
        }
        else
        {
            await resp.Content.CopyToAsync(fs, ct);
        }
        return dest;
    }

    /// <summary>Empreintes SHA-1 des mods d'une instance → identification CurseForge.</summary>
    public static async Task<Dictionary<long, (string Name, long FileId)>> FingerprintAsync(
        IEnumerable<long> fingerprints)
    {
        var result = new Dictionary<long, (string, long)>();
        var list = fingerprints.Distinct().ToList();
        for (int i = 0; i < list.Count; i += 96)
        {
            var batch = list.Skip(i).Take(96).ToList();
            var root = await PostAsync("fingerprints",
                JsonSerializer.Serialize(new { fingerprints = batch }));
            if (!root.TryGetProperty("data", out var data)) continue;
            if (data.TryGetProperty("exactMatches", out var matches))
            {
                foreach (var m in matches.EnumerateArray())
                {
                    long fp = m.TryGetProperty("fileFingerprint", out var ff) ? ff.GetInt64()
                              : m.GetProperty("fileFingerprint").GetInt64();
                    var file = m.GetProperty("file");
                    result[fp] = (
                        file.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" :
                        file.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "",
                        file.GetProperty("id").GetInt64());
                }
            }
        }
        return result;
    }

    /// <summary>Empreinte MurmurHash2 (variante CurseForge) d'un fichier.</summary>
    public static long ComputeFingerprint(string filePath)
    {
        // implémentation standard de l'empreinte CurseForge :
        // le fichier est normalisé (\r\n et \t supprimés) puis hashé avec MurmurHash2 x64
        byte[] data;
        try
        {
            var raw = File.ReadAllBytes(filePath);
            var normalized = raw.Where(b => b != 9 && b != 10 && b != 13).ToArray();
            data = normalized;
        }
        catch { return 0; }
        return MurmurHash2(data, 0x1F123BB5u);
    }

    private static long MurmurHash2(byte[] data, uint seed)
    {
        uint len = (uint)data.Length;
        if (len == 0) return 0;
        uint m = 0x5bd1e995u;
        int r = 24;
        uint h = seed ^ len;
        int currentIndex = 0;
        while (len >= 4)
        {
            uint k = BitConverter.ToUInt32(data, currentIndex);
            k *= m; k ^= k >> r; k *= m;
            h *= m; h ^= k;
            currentIndex += 4;
            len -= 4;
        }
        switch (len)
        {
            case 3: h ^= (uint)data[currentIndex + 2] << 16; goto case 2;
            case 2: h ^= (uint)data[currentIndex + 1] << 8; goto case 1;
            case 1: h ^= data[currentIndex]; h *= m; break;
        }
        h ^= h >> 13; h *= m; h ^= h >> 15;
        return h;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
