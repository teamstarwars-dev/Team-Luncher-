using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TeamLauncher;

/// <summary>Statut d'un serveur multijoueur via le protocole SLP Minecraft (1.7+).</summary>
public static class ServerPing
{
    public sealed record Status(string Motd, int Online, int Max, string Version)
    {
        /// <summary>Pseudos des joueurs connectés (si le serveur les expose).</summary>
        public string[] Players { get; init; } = Array.Empty<string>();
    }

    public static async Task<Status?> QueryAsync(string address)
    {
        string host = address;
        int port = 25565;
        int colon = address.LastIndexOf(':');
        if (colon > 0 && int.TryParse(address[(colon + 1)..], out var p)) { host = address[..colon]; port = p; }

        using var tcp = new TcpClient();
        var connect = tcp.ConnectAsync(host, port);
        if (await Task.WhenAny(connect, Task.Delay(3500)) != connect) return null;

        await using var stream = tcp.GetStream();
        stream.ReadTimeout = stream.WriteTimeout = 3500;

        // handshake : id 0x00, protocole 47, hôte, port, état 1
        byte[] hostBytes = Encoding.UTF8.GetBytes(host);
        var hs = new MemoryStream();
        WriteVarInt(hs, 0x00); WriteVarInt(hs, 47);
        WriteVarInt(hs, hostBytes.Length); hs.Write(hostBytes);
        hs.Write(new byte[] { (byte)(port >> 8), (byte)port });
        WriteVarInt(hs, 1);
        Send(stream, hs.ToArray());
        Send(stream, new byte[] { 0x01, 0x00 }); // requête de statut

        int len = ReadVarInt(stream);
        ReadVarInt(stream); // id paquet
        int strLen = ReadVarInt(stream);
        var buf = new byte[strLen];
        await stream.ReadExactlyAsync(buf);
        using var doc = JsonDocument.Parse(buf);

        var root = doc.RootElement;
        int online = 0, max = 0;
        var names = new List<string>();
        if (root.TryGetProperty("players", out var players))
        {
            online = players.TryGetProperty("online", out var o) ? o.GetInt32() : 0;
            max = players.TryGetProperty("max", out var mx) ? mx.GetInt32() : 0;
            if (players.TryGetProperty("sample", out var sample) && sample.ValueKind == JsonValueKind.Array)
                foreach (var e in sample.EnumerateArray())
                    if (e.TryGetProperty("name", out var nm))
                        names.Add(nm.GetString() ?? "");
        }
        string version = root.TryGetProperty("version", out var ver)
            ? ver.GetProperty("name").GetString() ?? "" : "";
        string motd = "";
        if (root.TryGetProperty("description", out var desc))
            motd = desc.ValueKind == JsonValueKind.String
                ? desc.GetString() ?? ""
                : desc.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return new Status(motd.Replace('\n', ' '), online, max, version)
        {
            Players = names.ToArray()
        };
    }

    private static void Send(NetworkStream s, byte[] packet)
    {
        var len = new MemoryStream();
        WriteVarInt(len, packet.Length);
        s.Write(len.ToArray());
        s.Write(packet);
        s.Flush();
    }

    private static void WriteVarInt(MemoryStream s, int value)
    {
        while (true)
        {
            if ((value & ~0x7F) == 0) { s.WriteByte((byte)value); return; }
            s.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
    }

    private static int ReadVarInt(NetworkStream s)
    {
        int value = 0, shift = 0;
        while (true)
        {
            int b = s.ReadByte();
            if (b < 0) throw new IOException("Connexion fermée.");
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
            if (shift > 28) throw new IOException("Varint trop long.");
        }
    }
}

/// <summary>Purge du cache : installeurs, zips de Java, dossiers inutiles.</summary>
public static class CleanupService
{
    public static (int Files, double Mb) Run()
    {
        int count = 0; double mb = 0;
        string runtime = GameInstaller.RuntimeRoot;

        foreach (var pattern in new[] { "forge-installer-*.jar", "neoforge-installer-*.jar", "adoptium-jre-*.zip" })
            foreach (var f in Directory.GetFiles(runtime, pattern))
            { mb += new FileInfo(f).Length; File.Delete(f); count++; }

        // anciens logs (garder 30 jours)
        foreach (var dir in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "TeamLauncher")
                 })
            foreach (var f in Directory.GetFiles(dir, "*.zip"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-30))
                { mb += new FileInfo(f).Length; File.Delete(f); count++; }

        return (count, mb / 1024.0 / 1024.0);
    }
}
