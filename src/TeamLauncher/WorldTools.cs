using System.IO.Compression;
using System.Text;

namespace TeamLauncher;

/// <summary>Outils de lecture des mondes : level.dat (NBT gzip) et régions MCA.</summary>
public static class WorldTools
{
    /// <summary>Lit level.dat (gzip + NBT) pour extraire nom et date, sans dépendance externe.</summary>
    public static (string? Name, DateTime? LastPlayed) ReadLevelDat(string worldDir)
    {
        try
        {
            string path = Path.Combine(worldDir, "level.dat");
            using var fs = File.OpenRead(path);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            gz.CopyTo(ms);
            var bytes = ms.ToArray();

            string? name = FindStringAfterTag(bytes, "LevelName");
            DateTime? played = null;
            long javaMs = FindLongAfterTag(bytes, "LastPlayed");
            if (javaMs > 0)
                played = DateTimeOffset.FromUnixTimeMilliseconds(javaMs).LocalDateTime;
            return (name, played);
        }
        catch { return (null, null); }
    }

    private static string? FindStringAfterTag(byte[] data, string tagName)
    {
        int idx = IndexOf(data, Encoding.ASCII.GetBytes(tagName));
        if (idx < 0) return null;
        int pos = idx + tagName.Length;
        if (pos + 2 > data.Length) return null;
        int len = (data[pos] << 8) | data[pos + 1];
        if (len <= 0 || pos + 2 + len > data.Length) return null;
        return Encoding.UTF8.GetString(data, pos + 2, len);
    }

    private static long FindLongAfterTag(byte[] data, string tagName)
    {
        int idx = IndexOf(data, Encoding.ASCII.GetBytes(tagName));
        if (idx < 0 || idx + tagName.Length + 8 > data.Length) return 0;
        long v = 0;
        for (int i = 0; i < 8; i++)
            v = (v << 8) | data[idx + tagName.Length + i];
        return v;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length && ok; j++)
                if (haystack[i + j] != needle[j]) ok = false;
            if (ok) return i;
        }
        return -1;
    }

    /// <summary>Compte les fichiers de région MCA vides ou corrompus (≤ 8 Ko).</summary>
    public static int CountEmptyRegions(string worldDir)
    {
        string region = Path.Combine(worldDir, "region");
        if (!Directory.Exists(region)) return 0;
        return Directory.GetFiles(region, "*.mca").Count(f => new FileInfo(f).Length <= 8192);
    }

    /// <summary>Supprime les fichiers de région vides/corrompus.</summary>
    public static int DeleteEmptyRegions(string worldDir)
    {
        string region = Path.Combine(worldDir, "region");
        if (!Directory.Exists(region)) return 0;
        int removed = 0;
        foreach (var f in Directory.GetFiles(region, "*.mca"))
        {
            if (new FileInfo(f).Length <= 8192)
            {
                File.Delete(f);
                removed++;
            }
        }
        return removed;
    }
}
