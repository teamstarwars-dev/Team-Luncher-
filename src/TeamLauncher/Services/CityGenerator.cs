using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TeamLauncher;

/// <summary>
/// Générateur de villes réelles à partir de données OpenStreetMap.
/// Remplace Arnis : récupère les bâtiments, routes et structures via l'API Overpass,
/// puis les place directement dans le monde Minecraft en écrivant les fichiers .mca.
/// </summary>
public static class CityGenerator
{
    // Utilise Http.Shared (client HTTP partagé)

    private static readonly Dictionary<string, byte> BlockColors = new()
    {
        ["building"] = 0,          // stone
        ["building:material:brick"] = 1, // bricks
        ["building:material:wood"] = 2,  // oak_planks
        ["building:material:glass"] = 3, // glass
        ["highway"] = 4,           // gravel
        ["highway:primary"] = 5,   // stone_bricks
        ["highway:residential"] = 6, // cobblestone
        ["water"] = 7,             // water
        ["natural:water"] = 7,
        ["leisure:park"] = 8,      // grass_block
        ["landuse:grass"] = 8,
        ["landuse:residential"] = 9, // sand
        ["railway"] = 10,          // iron_block
    };

    private static readonly string[] BlockNames = [
        "minecraft:stone",           // 0
        "minecraft:bricks",          // 1
        "minecraft:oak_planks",      // 2
        "minecraft:glass",           // 3
        "minecraft:gravel",          // 4
        "minecraft:stone_bricks",    // 5
        "minecraft:cobblestone",     // 6
        "minecraft:water",           // 7
        "minecraft:grass_block",     // 8
        "minecraft:sand",            // 9
        "minecraft:iron_block",      // 10
    ];

    /// <summary>
    /// Récupère les données OSM pour une bounding box et retourne les entités.
    /// </summary>
    public static async Task<OsmData> FetchOsmDataAsync(string bbox, IProgress<string>? progress = null)
    {
        // bbox = "minLon,minLat,maxLon,maxLat"
        string[] parts = bbox.Split(',');
        if (parts.Length != 4)
            throw new ArgumentException("Format bbox : minLon,minLat,maxLon,maxLat");

        string minLon = parts[0].Trim();
        string minLat = parts[1].Trim();
        string maxLon = parts[2].Trim();
        string maxLat = parts[3].Trim();

        progress?.Report("Récupération des données OpenStreetMap...");

        // Overpass API query: get buildings, highways, water, parks
        string query = $@"
[out:json][timeout:120];
(
  way[""building""]({minLat},{minLon},{maxLat},{maxLon});
  way[""highway""]({minLat},{minLon},{maxLat},{maxLon});
  way[""waterway""]({minLat},{minLon},{maxLat},{maxLon});
  way[""natural""=""water""]({minLat},{minLon},{maxLat},{maxLon});
  way[""leisure""=""park""]({minLat},{minLon},{maxLat},{maxLon});
  way[""landuse""]({minLat},{minLon},{maxLat},{maxLon});
  way[""railway""]({minLat},{minLon},{maxLat},{maxLon});
  relation[""building""]({minLat},{minLon},{maxLat},{maxLon});
);
out body;
>;
out skel qt;";

        var content = new StringContent(query, Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await Http.Shared.PostAsync("https://overpass-api.de/api/interpreter", content);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync();

        progress?.Report("Analyse des données...");

        return ParseOverpassJson(json, minLon, minLat, maxLon, maxLat);
    }

    /// <summary>
    /// Parse la réponse JSON d'Overpass en données structurées.
    /// </summary>
    private static OsmData ParseOverpassJson(string json, string minLon, string minLat,
        string maxLon, string maxLat)
    {
        var data = new OsmData();
        var doc = JsonDocument.Parse(json);
        var elements = doc.RootElement.GetProperty("elements");

        var nodes = new Dictionary<long, (double Lon, double Lat)>();

        // First pass: collect all nodes
        foreach (var el in elements.EnumerateArray())
        {
            string type = el.GetProperty("type").GetString() ?? "";
            if (type == "node")
            {
                long id = el.GetProperty("id").GetInt64();
                double lon = el.GetProperty("lon").GetDouble();
                double lat = el.GetProperty("lat").GetDouble();
                nodes[id] = (lon, lat);
            }
        }

        double minLonD = double.Parse(minLon.Replace(',', '.'));
        double minLatD = double.Parse(minLat.Replace(',', '.'));
        double maxLonD = double.Parse(maxLon.Replace(',', '.'));
        double maxLatD = double.Parse(maxLat.Replace(',', '.'));
        double centerLon = (minLonD + maxLonD) / 2;
        double centerLat = (minLatD + maxLatD) / 2;

        // Convert lat/lon to Minecraft coordinates
        // Approximation: 1 degree latitude ≈ 111,320 meters ≈ 111,320 blocks
        // At latitude 48°, 1 degree longitude ≈ 74,640 meters
        double lonScale = 111320.0 * Math.Cos(centerLat * Math.PI / 180);
        double latScale = 111320.0;

        data.LonToX = lon => (int)((lon - centerLon) * lonScale);
        data.LatToZ = lat => (int)((lat - centerLat) * latScale);

        // Second pass: collect ways
        foreach (var el in elements.EnumerateArray())
        {
            string type = el.GetProperty("type").GetString() ?? "";
            if (type != "way") continue;

            long id = el.GetProperty("id").GetInt64();
            var tags = el.TryGetProperty("tags", out var t) ? t : default;
            var nodeIds = new List<long>();
            if (el.TryGetProperty("nodes", out var nArr))
                foreach (var n in nArr.EnumerateArray())
                    nodeIds.Add(n.GetInt64());

            var points = new List<(int X, int Z)>();
            foreach (var nid in nodeIds)
            {
                if (nodes.TryGetValue(nid, out var pos))
                    points.Add((data.LonToX(pos.Lon), data.LatToZ(pos.Lat)));
            }

            if (points.Count < 2) continue;

            var entity = new OsmEntity { Id = id, Points = points };

            // Classify entity
            if (tags.ValueKind != JsonValueKind.Undefined)
            {
                if (tags.TryGetProperty("building", out _))
                {
                    entity.Type = "building";
                    entity.Height = 4; // default
                    if (tags.TryGetProperty("building:levels", out var lvl))
                    {
                        if (int.TryParse(lvl.GetString(), out int l) && l > 0)
                            entity.Height = l * 4;
                    }
                }
                else if (tags.TryGetProperty("highway", out var hw))
                {
                    entity.Type = "highway";
                    string hwType = hw.GetString() ?? "";
                    entity.Width = hwType is "primary" or "secondary" or "tertiary" ? 6 : 3;
                }
                else if (tags.TryGetProperty("waterway", out _) || tags.TryGetProperty("natural", out _))
                    entity.Type = "water";
                else if (tags.TryGetProperty("leisure", out _) || tags.TryGetProperty("landuse", out _))
                    entity.Type = "park";
                else if (tags.TryGetProperty("railway", out _))
                    entity.Type = "railway";
            }

            data.Entities.Add(entity);
        }

        return ParseOverpassJson(json, minLon, minLat, maxLon, maxLat);
    }

    /// <summary>
    /// Génère la ville dans le monde Minecraft spécifié.
    /// Écrit les blocs directement dans les fichiers .mca existants.
    /// </summary>
    public static async Task<int> GenerateInWorldAsync(string worldDir, OsmData data,
        int baseY = 64, IProgress<string>? progress = null)
    {
        string regionDir = Path.Combine(worldDir, "region");
        if (!Directory.Exists(regionDir))
            Directory.CreateDirectory(regionDir);

        // Calculate bounding box in chunk coordinates
        int minCX = int.MaxValue, minCZ = int.MaxValue;
        int maxCX = int.MinValue, maxCZ = int.MinValue;

        foreach (var entity in data.Entities)
        {
            foreach (var (x, z) in entity.Points)
            {
                int cx = x >> 4;
                int cz = z >> 4;
                minCX = Math.Min(minCX, cx);
                minCZ = Math.Min(minCZ, cz);
                maxCX = Math.Max(maxCX, cx);
                maxCZ = Math.Max(maxCZ, cz);
            }
        }

        if (minCX > maxCX) return 0;

        int placed = 0;
        int totalChunks = (maxCX - minCX + 1) * (maxCZ - minCZ + 1);
        int chunkIdx = 0;

        for (int cx = minCX; cx <= maxCX; cx++)
        {
            for (int cz = minCZ; cz <= maxCZ; cz++)
            {
                chunkIdx++;
                if (chunkIdx % 10 == 0)
                    progress?.Report($"Placement: {chunkIdx}/{totalChunks} chunks...");

                await Task.Run(() =>
                {
                    placed += PlaceBlocksInChunk(regionDir, cx, cz, data, baseY);
                });
            }
        }

        return placed;
    }

    private static int PlaceBlocksInChunk(string regionDir, int cx, int cz,
        OsmData data, int baseY)
    {
        int rx = cx >> 5;
        int rz = cz >> 5;
        string regionFile = Path.Combine(regionDir, $"r.{rx}.{rz}.mca");

        // Read existing chunk if it exists
        var existingBlocks = new Dictionary<(int X, int Y, int Z), string>();
        NbtCompound? chunkNbt = null;

        if (File.Exists(regionFile))
        {
            chunkNbt = ChunkReader.ReadChunk(regionFile, cx, cz);
            if (chunkNbt != null)
                existingBlocks = ChunkReader.GetBlocks(chunkNbt);
        }

        // Determine blocks to place for this chunk
        int localX = cx * 16;
        int localZ = cz * 16;
        var newBlocks = new Dictionary<(int, int, int), string>();

        foreach (var entity in data.Entities)
        {
            switch (entity.Type)
            {
                case "building":
                    FillPolygon(entity.Points, entity.Height, baseY, data,
                        localX, localZ, newBlocks, "minecraft:stone", "minecraft:glass");
                    break;

                case "highway":
                    FillLine(entity.Points, entity.Width, baseY - 1, data,
                        localX, localZ, newBlocks, "minecraft:cobblestone");
                    break;

                case "water":
                    FillPolygon(entity.Points, 2, baseY - 2, data,
                        localX, localZ, newBlocks, "minecraft:water");
                    break;

                case "park":
                    FillPolygon(entity.Points, 1, baseY - 1, data,
                        localX, localZ, newBlocks, "minecraft:grass_block");
                    break;

                case "railway":
                    FillLine(entity.Points, 1, baseY, data,
                        localX, localZ, newBlocks, "minecraft:iron_block");
                    break;
            }
        }

        if (newBlocks.Count == 0) return 0;

        // Merge with existing
        foreach (var kvp in newBlocks)
            existingBlocks[kvp.Key] = kvp.Value;

        // Write back the chunk
        try
        {
            WriteChunk(regionFile, cx, cz, existingBlocks, baseY);
            return newBlocks.Count;
        }
        catch { return 0; }
    }

    private static void FillPolygon(List<(int X, int Z)> points, int height, int baseY,
        OsmData data, int localX, int localZ,
        Dictionary<(int, int, int), string> blocks,
        string fillBlock, string? edgeBlock = null)
    {
        if (points.Count < 3) return;

        // Find bounding box of the polygon
        int minX = points.Min(p => p.X) - localX;
        int maxX = points.Max(p => p.X) - localX;
        int minZ = points.Min(p => p.Z) - localZ;
        int maxZ = points.Max(p => p.Z) - localZ;

        // Clamp to chunk
        minX = Math.Max(0, minX);
        maxX = Math.Min(15, maxX);
        minZ = Math.Max(0, minZ);
        maxZ = Math.Min(15, maxZ);

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                // Point-in-polygon test using ray casting
                if (PointInPolygon(x + localX, z + localZ, points))
                {
                    for (int y = baseY; y < baseY + height; y++)
                    {
                        string block = (y == baseY + height - 1 && edgeBlock != null)
                            ? edgeBlock : fillBlock;
                        blocks[(x, y, z)] = block;
                    }
                }
            }
        }
    }

    private static void FillLine(List<(int X, int Z)> points, int width, int y,
        OsmData data, int localX, int localZ,
        Dictionary<(int, int, int), string> blocks, string block)
    {
        for (int i = 0; i < points.Count - 1; i++)
        {
            var (x0, z0) = points[i];
            var (x1, z1) = points[i + 1];

            // Bresenham-like line with width
            int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(z1 - z0));
            if (steps == 0) continue;

            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                int px = (int)(x0 + (x1 - x0) * t) - localX;
                int pz = (int)(z0 + (z1 - z0) * t) - localZ;

                for (int dx = -width / 2; dx <= width / 2; dx++)
                {
                    for (int dz = -width / 2; dz <= width / 2; dz++)
                    {
                        int bx = px + dx;
                        int bz = pz + dz;
                        if (bx >= 0 && bx <= 15 && bz >= 0 && bz <= 15)
                            blocks[(bx, y, bz)] = block;
                    }
                }
            }
        }
    }

    private static bool PointInPolygon(int px, int pz, List<(int X, int Z)> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (xi, zi) = polygon[i];
            var (xj, zj) = polygon[j];
            if ((zi > pz) != (zj > pz) &&
                px < (xj - xi) * (pz - zi) / (zj - zi + 0.001) + xi)
                inside = !inside;
        }
        return inside;
    }

    private static void WriteChunk(string regionFile, int cx, int cz,
        Dictionary<(int X, int Y, int Z), string> blocks, int baseY)
    {
        // Build sections
        var sections = new NbtList(10);

        for (int sy = -4; sy <= 19; sy++) // Y sections from -64 to 319
        {
            var sectionBlocks = new List<(string Name, int X, int Y, int Z)>();
            foreach (var ((x, y, z), name) in blocks)
            {
                if ((y >> 4) == sy)
                    sectionBlocks.Add((name, x & 0xF, y & 0xF, z & 0xF));
            }

            if (sectionBlocks.Count == 0) continue;

            var section = new NbtCompound();
            section.Set("Y", (byte)(sy & 0xFF));

            // Build palette
            var paletteList = new NbtList(10);
            var nameToIdx = new Dictionary<string, int>();
            foreach (var (name, _, _, _) in sectionBlocks)
            {
                if (!nameToIdx.ContainsKey(name))
                {
                    nameToIdx[name] = nameToIdx.Count;
                    var tag = new NbtCompound();
                    tag.Set("Name", name);
                    paletteList.Items.Add(tag);
                }
            }

            var blockStates = new NbtCompound();
            blockStates.Set("palette", paletteList);

            if (nameToIdx.Count > 1)
            {
                int bits = Math.Max(4, (int)Math.Ceiling(Math.Log2(nameToIdx.Count)));
                int valuesPerLong = 64 / bits;
                int totalLongs = (4096 + valuesPerLong - 1) / valuesPerLong;
                var data = new long[totalLongs];
                long mask = (1L << bits) - 1;

                foreach (var (name, x, y, z) in sectionBlocks)
                {
                    int idx = (y << 8) | (z << 4) | x;
                    int longIdx = idx / valuesPerLong;
                    int bitOff = (idx % valuesPerLong) * bits;
                    data[longIdx] |= ((long)nameToIdx[name] << bitOff) & (mask << bitOff);
                }

                blockStates.Set("data", data);
            }

            section.Set("block_states", blockStates);
            sections.Items.Add(section);
        }

        // Build chunk NBT
        var chunk = new NbtCompound();
        chunk.Set("sections", sections);

        // Write as .mca region file
        WriteRegionFile(regionFile, cx, cz, chunk);
    }

    private static void WriteRegionFile(string regionFile, int cx, int cz, NbtCompound chunk)
    {
        // Serialize chunk NBT to bytes
        byte[] chunkData = SerializeCompound(chunk);

        // Compress with zlib
        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            // Zlib header
            ms.WriteByte(0x78);
            ms.WriteByte(0x01);
            using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                deflate.Write(chunkData);
            compressed = ms.ToArray();
        }

        // Calculate sector layout
        int totalLen = 4 + compressed.Length; // 4 bytes for length + compressed data
        int sectors = (totalLen + 4095) / 4096;

        int rx = cx >> 5;
        int rz = cz >> 5;
        int lx = cx & 31;
        int lz = cz & 31;

        byte[] regionData;
        if (File.Exists(regionFile))
        {
            regionData = File.ReadAllBytes(regionFile);
        }
        else
        {
            regionData = new byte[8192]; // Header only
        }

        // Find a free sector offset
        int maxSector = 2; // Sectors 0-1 are header
        for (int i = 0; i < 1024; i++)
        {
            int off = (regionData[i * 4] << 16) | (regionData[i * 4 + 1] << 8) | regionData[i * 4 + 2];
            int cnt = regionData[i * 4 + 3];
            if (off + cnt > maxSector)
                maxSector = off + cnt;
        }

        int newOffset = maxSector;
        int idx = lz * 32 + lx;
        regionData[idx * 4] = (byte)(newOffset >> 16);
        regionData[idx * 4 + 1] = (byte)(newOffset >> 8);
        regionData[idx * 4 + 2] = (byte)newOffset;
        regionData[idx * 4 + 3] = (byte)sectors;

        // Ensure regionData is large enough
        int requiredSize = (newOffset + sectors) * 4096;
        if (regionData.Length < requiredSize)
        {
            var newData = new byte[requiredSize];
            Array.Copy(regionData, newData, regionData.Length);
            regionData = newData;
        }

        // Write chunk data
        int pos = newOffset * 4096;
        regionData[pos] = (byte)(totalLen >> 24);
        regionData[pos + 1] = (byte)(totalLen >> 16);
        regionData[pos + 2] = (byte)(totalLen >> 8);
        regionData[pos + 3] = (byte)totalLen;
        Array.Copy(compressed, 0, regionData, pos + 4, compressed.Length);

        File.WriteAllBytes(regionFile, regionData);
    }

    private static byte[] SerializeCompound(NbtCompound compound)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write((byte)10); // TAG_Compound
        bw.Write((short)0); // root name
        WriteCompoundPayload(bw, compound);
        bw.Write((byte)0); // TAG_End
        return ms.ToArray();
    }

    private static void WriteCompoundPayload(BinaryWriter bw, NbtCompound compound)
    {
        foreach (var kvp in compound.Tags)
        {
            WriteTag(bw, kvp.Key, kvp.Value);
        }
        bw.Write((byte)0); // TAG_End
    }

    private static void WriteTag(BinaryWriter bw, string name, object value)
    {
        switch (value)
        {
            case byte v:
                bw.Write((byte)1);
                WriteName(bw, name);
                bw.Write(v);
                break;
            case short v:
                bw.Write((byte)2);
                WriteName(bw, name);
                bw.Write(v);
                break;
            case int v:
                bw.Write((byte)3);
                WriteName(bw, name);
                bw.Write(v);
                break;
            case long v:
                bw.Write((byte)4);
                WriteName(bw, name);
                bw.Write(v);
                break;
            case float v:
                bw.Write((byte)5);
                WriteName(bw, name);
                bw.Write(v);
                break;
            case double v:
                bw.Write((byte)6);
                WriteName(bw, name);
                bw.Write(v);
                break;
            case byte[] v:
                bw.Write((byte)7);
                WriteName(bw, name);
                bw.Write(v.Length);
                bw.Write(v);
                break;
            case string v:
                bw.Write((byte)8);
                WriteName(bw, name);
                var bytes = Encoding.UTF8.GetBytes(v);
                bw.Write((short)bytes.Length);
                bw.Write(bytes);
                break;
            case NbtList v:
                bw.Write((byte)9);
                WriteName(bw, name);
                bw.Write(v.ElementType);
                bw.Write(v.Count);
                foreach (var item in v.Items)
                {
                    switch (item)
                    {
                        case NbtCompound c: WriteCompoundPayload(bw, c); break;
                        case byte b: bw.Write(b); break;
                        case short s: bw.Write(s); break;
                        case int i: bw.Write(i); break;
                        case long l: bw.Write(l); break;
                        case float f: bw.Write(f); break;
                        case double d: bw.Write(d); break;
                    }
                }
                break;
            case NbtCompound v:
                bw.Write((byte)10);
                WriteName(bw, name);
                WriteCompoundPayload(bw, v);
                break;
            case int[] v:
                bw.Write((byte)11);
                WriteName(bw, name);
                bw.Write(v.Length);
                foreach (var i in v) bw.Write(i);
                break;
            case long[] v:
                bw.Write((byte)12);
                WriteName(bw, name);
                bw.Write(v.Length);
                foreach (var l in v) bw.Write(l);
                break;
        }
    }

    private static void WriteName(BinaryWriter bw, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        bw.Write((short)bytes.Length);
        bw.Write(bytes);
    }
}

public sealed class OsmData
{
    public List<OsmEntity> Entities { get; } = new();
    public Func<double, int> LonToX { get; set; } = lon => 0;
    public Func<double, int> LatToZ { get; set; } = lat => 0;
}

public sealed class OsmEntity
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public int Height { get; set; } = 1;
    public int Width { get; set; } = 1;
    public List<(int X, int Z)> Points { get; set; } = new();
}
