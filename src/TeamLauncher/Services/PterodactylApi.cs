using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>
/// Client API Pterodactyl (Client API) pour gérer les serveurs Minecraft
/// hébergés sur le VPS. Communique via REST + WebSocket.
/// </summary>
public static class PterodactylApi
{
    // Utilise Http.Shared (client HTTP partagé)

    private static string PanelUrl => DataStore.Settings.VpsUrl.TrimEnd('/');
    private static string ApiKey => DataStore.Settings.VpsApiKey;

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(PanelUrl) && !string.IsNullOrWhiteSpace(ApiKey);

    // ======================== MODÈLES ========================

    public sealed record PtServer(
        string Id, string Name, string Node,
        string Status, int Cpu, long MemBytes, long DiskBytes,
        string? WebsocketToken, int[] Allocations);

    public sealed record PtServerState(
        bool IsRunning, bool IsInstalling,
        int CpuPercent, long MemUsedBytes, long DiskUsedBytes,
        int UptimeSeconds, int Players, int MaxPlayers);

    public sealed record PtFile(string Name, bool IsDirectory, long Size, string MimeType, DateTime Modified);

    public sealed record PtAllocation(string Ip, int Port);

    // ======================== API CALLS ========================

    private static async Task<JsonElement> GetAsync(string path)
    {
        EnsureConfigured();
        using var req = new HttpRequestMessage(HttpMethod.Get, PanelUrl + "/api/client" + path);
        req.Headers.Add("Authorization", "Bearer " + ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var resp = await Http.Shared.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement.Clone();
    }

    private static async Task<JsonElement> PostAsync(string path, string? jsonBody = null)
    {
        EnsureConfigured();
        using var req = new HttpRequestMessage(HttpMethod.Post, PanelUrl + "/api/client" + path);
        req.Headers.Add("Authorization", "Bearer " + ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (jsonBody != null)
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using var resp = await Http.Shared.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        string body = await resp.Content.ReadAsStringAsync();
        if (body.Length == 0 || body == "null") return default;
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task SendSignalAsync(string serverId, string signal)
    {
        await PostAsync($"/servers/{serverId}/power", JsonSerializer.Serialize(new { signal }));
    }

    private static void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new Exception(
                "Pterodactyl non configuré.\n\n" +
                "1. Installe Pterodactyl sur ton VPS\n" +
                "2. Crée une API key Client\n" +
                "3. Configure dans Paramètres → VPS Pterodactyl");
    }

    // ======================== SERVEURS ========================

    /// <summary>Liste tous les serveurs accessibles par la clé API.</summary>
    public static async Task<List<PtServer>> ListServersAsync()
    {
        var root = await GetAsync("/servers");
        var list = new List<PtServer>();
        if (root.TryGetProperty("data", out var data))
        {
            foreach (var s in data.EnumerateArray())
            {
                string sid = s.GetProperty("attributes").GetProperty("identifier").GetString() ?? "";
                var attr = s.GetProperty("attributes");
                string name = attr.GetProperty("name").GetString() ?? "";
                string node = attr.TryGetProperty("node", out var n) ? n.GetString() ?? "" : "";
                string status = attr.TryGetProperty("status", out var st) ? st.GetString() ?? "unknown" : "unknown";
                int cpu = attr.TryGetProperty("cpu", out var c) ? c.GetInt32() : 0;
                long mem = attr.TryGetProperty("memory", out var m) ? m.GetInt64() : 0;
                long disk = attr.TryGetProperty("disk", out var d) ? d.GetInt64() : 0;

                // WebSocket token
                string? wsToken = null;
                if (attr.TryGetProperty("websockets", out var ws) && ws.ValueKind == JsonValueKind.True)
                    wsToken = null; // token obtenu via endpoint dédié

                // Allocations
                int[] allocs = Array.Empty<int>();
                if (attr.TryGetProperty("allocations", out var al))
                {
                    var ports = new List<int>();
                    foreach (var a in al.EnumerateArray())
                        if (a.TryGetProperty("port", out var p)) ports.Add(p.GetInt32());
                    allocs = ports.ToArray();
                }

                list.Add(new PtServer(sid, name, node, status, cpu, mem, disk, wsToken, allocs));
            }
        }
        return list;
    }

    /// <summary>État en temps réel d'un serveur (CPU, RAM, joueurs).</summary>
    public static async Task<PtServerState> GetServerStateAsync(string serverId)
    {
        try
        {
            var root = await GetAsync($"/servers/{serverId}/resources");
            if (root.TryGetProperty("attributes", out var attr))
            {
                bool running = attr.TryGetProperty("running", out var r) && r.GetBoolean();
                bool installing = attr.TryGetProperty("installing", out var i) && i.ValueKind == JsonValueKind.True;
                int cpu = attr.TryGetProperty("cpu_absolute", out var c) ? (int)c.GetDouble() : 0;
                long mem = attr.TryGetProperty("memory_bytes", out var m) ? m.GetInt64() : 0;
                long disk = attr.TryGetProperty("disk_bytes", out var d) ? d.GetInt64() : 0;
                int uptime = attr.TryGetProperty("uptime", out var u) ? u.GetInt32() : 0;
                int players = attr.TryGetProperty("state", out var st) && st.TryGetProperty("players", out var p) ? p.GetInt32() : 0;
                int maxPlayers = attr.TryGetProperty("state", out var st2) && st2.TryGetProperty("max_players", out var mp) ? mp.GetInt32() : 0;
                return new PtServerState(running, installing, cpu, mem, disk, uptime, players, maxPlayers);
            }
        }
        catch { }
        return new PtServerState(false, false, 0, 0, 0, 0, 0, 0);
    }

    /// <summary>Récupère le token WebSocket pour la console.</summary>
    public static async Task<string?> GetWebsocketTokenAsync(string serverId)
    {
        try
        {
            var root = await GetAsync($"/servers/{serverId}/websocket");
            if (root.TryGetProperty("data", out var data))
                return data.GetProperty("token").GetString();
        }
        catch { }
        return null;
    }

    // ======================== COMMANDES ========================

    public static Task StartAsync(string serverId) => SendSignalAsync(serverId, "start");
    public static Task StopAsync(string serverId) => SendSignalAsync(serverId, "stop");
    public static Task RestartAsync(string serverId) => SendSignalAsync(serverId, "restart");
    public static Task KillAsync(string serverId) => SendSignalAsync(serverId, "kill");

    public static async Task SendCommandAsync(string serverId, string command)
    {
        await PostAsync($"/servers/{serverId}/command", JsonSerializer.Serialize(new { command }));
    }

    // ======================== FICHIERS ========================

    /// <summary>Liste les fichiers dans un répertoire du serveur.</summary>
    public static async Task<List<PtFile>> ListFilesAsync(string serverId, string directory = "/")
    {
        var root = await GetAsync($"/servers/{serverId}/files/list?directory={Uri.EscapeDataString(directory)}");
        var list = new List<PtFile>();
        if (root.TryGetProperty("data", out var data))
        {
            foreach (var f in data.EnumerateArray())
            {
                string name = f.GetProperty("attributes").GetProperty("name").GetString() ?? "";
                bool isDir = f.GetProperty("attributes").GetProperty("is_file").GetBoolean() == false;
                long size = f.GetProperty("attributes").GetProperty("size").GetInt64();
                string mime = f.GetProperty("attributes").GetProperty("mime_type").GetString() ?? "";
                DateTime mod = f.GetProperty("attributes").GetProperty("modified_at").GetDateTime();
                list.Add(new PtFile(name, isDir, size, mime, mod));
            }
        }
        return list;
    }

    /// <summary>Télécharge un fichier depuis le PC vers le serveur.</summary>
    public static async Task UploadFileAsync(string serverId, string remotePath, string localPath,
        Action<double>? progress = null)
    {
        EnsureConfigured();
        byte[] data = await File.ReadAllBytesAsync(localPath);
        string fileName = Path.GetFileName(localPath);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(data);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "files", fileName);
        content.Add(new StringContent(Path.GetDirectoryName(remotePath)?.Replace("\\", "/") ?? "/"), "root");

        using var req = new HttpRequestMessage(HttpMethod.Post, PanelUrl + $"/servers/{serverId}/files/upload")
        {
            Content = content
        };
        req.Headers.Add("Authorization", "Bearer " + ApiKey);

        using var resp = await Http.Shared.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Supprime un fichier/répertoire sur le serveur.</summary>
    public static async Task DeleteFileAsync(string serverId, string root, string[] files)
    {
        var body = JsonSerializer.Serialize(new { root, files });
        using var req = new HttpRequestMessage(HttpMethod.Post, PanelUrl + $"/servers/{serverId}/files/delete")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("Authorization", "Bearer " + ApiKey);
        using var resp = await Http.Shared.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }

    // ======================== ALLOCATIONS ========================

    /// <summary>Récupère les allocations (ports) d'un serveur.</summary>
    public static async Task<List<PtAllocation>> GetAllocationsAsync(string serverId)
    {
        var root = await GetAsync($"/servers/{serverId}/network/allocations");
        var list = new List<PtAllocation>();
        if (root.TryGetProperty("data", out var data))
        {
            foreach (var a in data.EnumerateArray())
            {
                string ip = a.GetProperty("attributes").GetProperty("ip").GetString() ?? "";
                int port = a.GetProperty("attributes").GetProperty("port").GetInt32();
                list.Add(new PtAllocation(ip, port));
            }
        }
        return list;
    }

    // ======================== CRÉATION DE SERVEUR (Application API) ========================

    /// <summary>
    /// Crée un serveur via l'Application API (nécessite une clé admin).
    /// Cette méthode est appelée par l'admin du panel pour provisionner de nouveaux serveurs.
    /// </summary>
    public static async Task<string> CreateServerAsync(
        string name, string eggId, int memoryMb, int diskMb,
        string nodeId, int allocationId, string? startCommand = null)
    {
        var body = JsonSerializer.Serialize(new
        {
            name,
            egg = int.Parse(eggId),
            docker_image = "ghcr.io/pterodactyl/yolks:java_17",
            startup = startCommand ?? "java -Xms128M -Xmx{SERVER_MEMORY}M -jar server.jar",
            environment = new { SERVER_JAR = "server.jar", JAVA_VERSION = "17" },
            limits = new { memory = memoryMb, disk = diskMb, cpu = 0, io = 500, threads = 0 },
            feature_limits = new { databases = 0, backups = 0, allocations = 1 },
            deploy = new { locations = new[] { allocationId }, ports = (string[]?)null, start_on_completion = false },
            start_on_complete = true
        });

        EnsureConfigured();
        using var req = new HttpRequestMessage(HttpMethod.Post, PanelUrl + "/api/application/servers")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("Authorization", "Bearer " + ApiKey);
        using var resp = await Http.Shared.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var root = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return root.RootElement.GetProperty("attributes").GetProperty("identifier").GetString() ?? "";
    }
}
