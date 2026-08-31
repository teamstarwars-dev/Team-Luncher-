using System.IO.Compression;

namespace TeamLauncher;

/// <summary>
/// Écriture de blocs dans les chunks Minecraft.
/// Modifie le NBT en place et réécrit les fichiers .mca.
/// </summary>
public static class ChunkWriter
{
    public static void SetBlock(NbtCompound chunk, int lx, int y, int lz, string blockName)
    {
        var sections = chunk.GetList("sections");
        if (sections == null) return;

        int sectionIndex = (y + 64) / 16;
        int localY = y & 0xF;

        if (sectionIndex < 0 || sectionIndex >= sections.Count) return;
        var section = sections.GetCompound(sectionIndex);
        if (section == null) return;

        var blockStates = section.GetCompound("block_states");
        if (blockStates != null)
        {
            SetBlockNewFormat(blockStates, lx, localY, lz, blockName);
            return;
        }

        var blocksArr = section.GetByteArray("Blocks");
        var dataArr = section.GetByteArray("Data");
        var palette = section.GetList("Palette");
        if (blocksArr != null)
        {
            SetBlockOldFormat(blocksArr, dataArr, palette, lx, localY, lz, blockName);
        }
    }

    private static void SetBlockNewFormat(NbtCompound blockStates, int lx, int y, int lz, string blockName)
    {
        var palette = blockStates.GetList("palette");
        var data = blockStates.GetLongArray("data");
        if (palette == null || palette.Count == 0) return;

        // Trouver ou ajouter le bloc dans la palette
        int targetIndex = -1;
        for (int i = 0; i < palette.Count; i++)
        {
            var tag = palette.GetCompound(i);
            if (tag != null && tag.GetString("Name") == blockName)
            {
                targetIndex = i;
                break;
            }
        }

        // Ajouter le nouveau bloc à la palette
        if (targetIndex < 0)
        {
            var newTag = new NbtCompound();
            newTag.Set("Name", blockName);
            palette.Items.Add(newTag);
            targetIndex = palette.Count - 1;
        }

        int blockIndex = (y << 8) | (lz << 4) | lx;

        if (palette.Count == 1)
            return; // tout est le même bloc, pas besoin de data

        int bitsPerEntry = Math.Max(4, (int)Math.Ceiling(Math.Log2(palette.Count)));
        int valuesPerLong = 64 / bitsPerEntry;
        long mask = (1L << bitsPerEntry) - 1;

        if (data == null || data.Length == 0)
        {
            data = new long[4096 / valuesPerLong + 1];
            blockStates.Set("data", data);
        }

        int longIndex = blockIndex / valuesPerLong;
        int bitOffset = (blockIndex % valuesPerLong) * bitsPerEntry;

        if (longIndex < data.Length)
        {
            data[longIndex] &= ~(mask << bitOffset);
            data[longIndex] |= ((long)targetIndex & mask) << bitOffset;
        }
    }

    private static void SetBlockOldFormat(byte[] blocks, byte[]? data, NbtList? palette,
        int lx, int y, int lz, string blockName)
    {
        int blockIndex = (y << 8) | (lz << 4) | lx;
        if (blockIndex >= blocks.Length) return;

        if (palette == null) return;

        // Trouver l'index dans la palette
        int targetIndex = -1;
        for (int i = 0; i < palette.Count; i++)
        {
            var tag = palette.GetCompound(i);
            if (tag != null && tag.GetString("Name") == blockName)
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex < 0)
        {
            var newTag = new NbtCompound();
            newTag.Set("Name", blockName);
            palette.Items.Add(newTag);
            targetIndex = palette.Count - 1;
        }

        blocks[blockIndex] = (byte)targetIndex;
        if (data != null && blockIndex / 2 < data.Length)
        {
            if ((blockIndex & 1) == 0)
                data[blockIndex / 2] = (byte)((data[blockIndex / 2] & 0xF0) | (targetIndex & 0x0F));
            else
                data[blockIndex / 2] = (byte)((data[blockIndex / 2] & 0x0F) | ((targetIndex & 0x0F) << 4));
        }
    }

    public static void WriteChunk(string regionFile, int cx, int cz, NbtCompound chunk)
    {
        byte[] regionData = File.ReadAllBytes(regionFile);

        var m = System.Text.RegularExpressions.Regex.Match(
            Path.GetFileName(regionFile), @"r\.(-?\d+)\.(-?\d+)\.mca$");
        if (!m.Success) return;
        int rx = int.Parse(m.Groups[1].Value);
        int rz = int.Parse(m.Groups[2].Value);

        int lx = cx - rx * 32;
        int lz = cz - rz * 32;
        if (lx < 0 || lx > 31 || lz < 0 || lz > 31) return;

        int i = lz * 32 + lx;
        int oldOffset = (regionData[i * 4] << 16) | (regionData[i * 4 + 1] << 8) | regionData[i * 4 + 2];

        // Sérialiser le chunk en NBT
        byte[] uncompressed = NbtWriter.WriteCompound(chunk);

        // Compresser en zlib
        byte[] compressed = ZlibCompress(uncompressed);

        // Écrire la longueur
        byte[] lenBytes = new byte[4 + compressed.Length];
        lenBytes[0] = (byte)(compressed.Length >> 24);
        lenBytes[1] = (byte)(compressed.Length >> 16);
        lenBytes[2] = (byte)(compressed.Length >> 8);
        lenBytes[3] = (byte)compressed.Length;
        Array.Copy(compressed, 0, lenBytes, 4, compressed.Length);

        // Trouver un nouvel offset à la fin du fichier
        int newOffset = (int)(regionData.Length / 4096) + 1;
        int newSectorCount = (int)Math.Ceiling((double)lenBytes.Length / 4096);

        // Mettre à jour l'entrée dans la table
        regionData[i * 4] = (byte)(newOffset >> 16);
        regionData[i * 4 + 1] = (byte)(newOffset >> 8);
        regionData[i * 4 + 2] = (byte)newOffset;
        regionData[i * 4 + 3] = (byte)newSectorCount;

        // Écrire le fichier complet
        using var fs = File.Create(regionFile);
        fs.Write(regionData);
        fs.Write(lenBytes);
        // Padding
        int padding = 4096 - (int)(lenBytes.Length % 4096);
        if (padding < 4096) fs.Write(new byte[padding]);
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); ms.WriteByte(0x9C); // zlib header
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(data);
        return ms.ToArray();
    }
}
