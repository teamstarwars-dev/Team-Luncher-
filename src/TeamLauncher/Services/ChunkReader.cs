using System.IO.Compression;

namespace TeamLauncher;

/// <summary>
/// Lit les données de chunks Minecraft (.mca) et extrait les blocs, le heightmap, etc.
/// Supporte les formats 1.18+ (palette étendue avec block_states) et 1.12 (ancien format).
/// </summary>
public static class ChunkReader
{
    /// <summary>
    /// Lit un chunk aux coordonnées (cx, cz) à partir du fichier région .mca.
    /// Retourne le NBT brut du chunk ou null si absent/corrompu.
    /// </summary>
    public static NbtCompound? ReadChunk(string regionFile, int cx, int cz)
    {
        byte[] data = File.ReadAllBytes(regionFile);
        if (data.Length < 8192) return null;

        // Extraire les coordonnées régionales du nom du fichier
        var m = System.Text.RegularExpressions.Regex.Match(
            Path.GetFileName(regionFile), @"r\.(-?\d+)\.(-?\d+)\.mca$");
        if (!m.Success) return null;
        int rx = int.Parse(m.Groups[1].Value);
        int rz = int.Parse(m.Groups[2].Value);

        // Coordonnées locales dans la région (0-31)
        int lx = cx - rx * 32;
        int lz = cz - rz * 32;
        if (lx < 0 || lx > 31 || lz < 0 || lz > 31) return null;

        int i = lz * 32 + lx;
        int offset = (data[i * 4] << 16) | (data[i * 4 + 1] << 8) | data[i * 4 + 2];
        if (offset == 0) return null;

        int srcPos = offset * 4096;
        if (srcPos + 4 > data.Length) return null;
        int len = (data[srcPos] << 24) | (data[srcPos + 1] << 16) |
                  (data[srcPos + 2] << 8) | data[srcPos + 3];
        if (len <= 0 || srcPos + 4 + len > data.Length) return null;

        // Le chunk est compressé en zlib ( deflate avec header zlib )
        byte[] compressed = new byte[len];
        Array.Copy(data, srcPos + 4, compressed, 0, len);

        try
        {
            byte[] decompressed = ZlibDecompress(compressed);
            return NbtReader.ReadUncompressed(decompressed);
        }
        catch { return null; }
    }

    /// <summary>
    /// Extrait le tableau de blocs (palette + data) d'un chunk NBT.
    /// Retourne un dictionnaire (x,y,z) -> nom du bloc.
    /// </summary>
    public static Dictionary<(int X, int Y, int Z), string> GetBlocks(NbtCompound chunk)
    {
        var blocks = new Dictionary<(int, int, int), string>();
        var sections = chunk.GetList("sections");
        if (sections == null) return blocks;

        // Lire la palette globale (1.18+ stocke les blocs dans "block_states")
        for (int si = 0; si < sections.Count; si++)
        {
            var section = sections.GetCompound(si);
            if (section == null) continue;

            int sy = section.GetByte("Y");

            // Nouveau format (1.18+): block_states → palette + data
            var blockStates = section.GetCompound("block_states");
            if (blockStates != null)
            {
                ReadBlockStates(blocks, blockStates, sy);
                continue;
            }

            // Ancien format (1.12-1.17): Blocks + Data + Palette
            var blocksArr = section.GetByteArray("Blocks");
            var dataArr = section.GetByteArray("Data");
            var palette = section.GetList("Palette");
            if (blocksArr != null)
            {
                ReadOldFormat(blocks, blocksArr, dataArr, palette, sy);
            }
        }

        return blocks;
    }

    private static void ReadBlockStates(Dictionary<(int, int, int), string> blocks,
        NbtCompound blockStates, int sectionY)
    {
        var palette = blockStates.GetList("palette");
        var data = blockStates.GetLongArray("data");

        if (palette == null || palette.Count == 0) return;

        // Si un seul bloc dans la palette, toute la section est ce bloc
        if (palette.Count == 1)
        {
            string name = GetBlockName(palette.GetCompound(0));
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                    for (int z = 0; z < 16; z++)
                        blocks[(x, sectionY * 16 + y, z)] = name;
            return;
        }

        if (data == null || data.Length == 0) return;

        int bitsPerEntry = Math.Max(4, (int)Math.Ceiling(Math.Log2(palette.Count)));
        long mask = (1L << bitsPerEntry) - 1;
        int valuesPerLong = 64 / bitsPerEntry;
        int totalEntries = 4096;

        for (int i = 0; i < totalEntries; i++)
        {
            int longIndex = i / valuesPerLong;
            int bitOffset = (i % valuesPerLong) * bitsPerEntry;
            if (longIndex >= data.Length) break;

            int paletteIndex = (int)((data[longIndex] >> bitOffset) & mask);
            if (paletteIndex < 0 || paletteIndex >= palette.Count) continue;

            string name = GetBlockName(palette.GetCompound(paletteIndex));
            int x = i & 0xF;
            int y = (i >> 8) & 0xF;
            int z = (i >> 4) & 0xF;
            blocks[(x, sectionY * 16 + y, z)] = name;
        }
    }

    private static void ReadOldFormat(Dictionary<(int, int, int), string> blocks,
        byte[] blockArr, byte[]? dataArr, NbtList? palette, int sectionY)
    {
        var names = new List<string>();
        if (palette != null)
        {
            for (int i = 0; i < palette.Count; i++)
            {
                var tag = palette.GetCompound(i);
                names.Add(tag?.GetString("Name") ?? "minecraft:air");
            }
        }

        for (int i = 0; i < 4096 && i < blockArr.Length; i++)
        {
            int blockId = blockArr[i] & 0xFF;
            byte dataVal = 0;
            if (dataArr != null && i / 2 < dataArr.Length)
                dataVal = (byte)((i & 1) == 0
                    ? (dataArr[i / 2] & 0x0F)
                    : ((dataArr[i / 2] >> 4) & 0x0F));

            string name = blockId < names.Count ? names[blockId] : $"minecraft:unknown_{blockId}";
            int x = i & 0xF;
            int y = (i >> 8) & 0xF;
            int z = (i >> 4) & 0xF;
            blocks[(x, sectionY * 16 + y, z)] = name;
        }
    }

    private static string GetBlockName(NbtCompound? tag)
    {
        return tag?.GetString("Name") ?? "minecraft:air";
    }

    /// <summary>
    /// Récupère la palette de blocs unique d'un chunk (pour coloration).
    /// </summary>
    public static HashSet<string> GetUniqueBlocks(NbtCompound chunk)
    {
        var blocks = GetBlocks(chunk);
        return new HashSet<string>(blocks.Values);
    }

    /// <summary>
    /// Récupère le Y max (plus haut bloc non-air) pour chaque colonne (x,z).
    /// </summary>
    public static int[,] GetHeightmap(NbtCompound chunk)
    {
        var hm = new int[16, 16];
        var blocks = GetBlocks(chunk);
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                int maxY = -1;
                for (int y = 319; y >= -64; y--)
                {
                    if (blocks.TryGetValue((x, y, z), out var name) && name != "minecraft:air")
                    {
                        maxY = y;
                        break;
                    }
                }
                hm[x, z] = maxY;
            }
        }
        return hm;
    }

    private static byte[] ZlibDecompress(byte[] data)
    {
        // Zlib = header (2 bytes) + deflate stream
        using var ms = new MemoryStream(data, 2, data.Length - 2);
        using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }
}
