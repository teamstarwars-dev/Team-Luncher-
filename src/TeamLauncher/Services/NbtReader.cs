using System.IO.Compression;
using System.Text;

namespace TeamLauncher;

/// <summary>
/// Parser NBT (Named Binary Tag) natif pour lire les données de chunks Minecraft.
/// Supporte les tags de base : TAG_Byte, TAG_Short, TAG_Int, TAG_Long, TAG_Float,
/// TAG_Double, TAG_Byte_Array, TAG_String, TAG_List, TAG_Compound, TAG_Int_Array, TAG_Long_Array.
/// </summary>
public sealed class NbtCompound
{
    private readonly Dictionary<string, object> _tags = new();

    public object? Get(string key) => _tags.TryGetValue(key, out var v) ? v : null;
    public T? Get<T>(string key) => _tags.TryGetValue(key, out var v) && v is T t ? t : default;
    public NbtCompound? GetCompound(string key) => Get<NbtCompound>(key);
    public NbtList? GetList(string key) => Get<NbtList>(key);
    public int GetInt(string key, int def = 0) => Get<int>(key);
    public long GetLong(string key, long def = 0) => Get<long>(key);
    public byte GetByte(string key, byte def = 0) => Get<byte>(key);
    public string GetString(string key, string def = "") => Get<string>(key) ?? def;
    public byte[]? GetByteArray(string key) => Get<byte[]>(key);
    public int[]? GetIntArray(string key) => Get<int[]>(key);
    public long[]? GetLongArray(string key) => Get<long[]>(key);
    public bool ContainsKey(string key) => _tags.ContainsKey(key);
    public IReadOnlyDictionary<string, object> Tags => _tags;

    internal void Set(string key, object value) => _tags[key] = value;

    public override string ToString() => $"NbtCompound({string.Join(", ", _tags.Keys)})";
}

public sealed class NbtList
{
    public byte ElementType { get; }
    public List<object> Items { get; } = new();
    public int Count => Items.Count;

    public NbtList(byte elementType) { ElementType = elementType; }

    public T? Get<T>(int index) => index < Items.Count && Items[index] is T t ? t : default;
    public NbtCompound? GetCompound(int index) => Get<NbtCompound>(index);
}

public static class NbtReader
{
    public static NbtCompound ReadGzipped(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var buf = new MemoryStream();
        gz.CopyTo(buf);
        return ReadUncompressed(buf.ToArray());
    }

    public static NbtCompound ReadUncompressed(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        return ReadCompound(br);
    }

    public static NbtCompound ReadFromCompressedStream(Stream stream)
    {
        using var gz = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        ms.Position = 0;
        using var br = new BinaryReader(ms);
        return ReadCompound(br);
    }

    private static NbtCompound ReadCompound(BinaryReader br)
    {
        var compound = new NbtCompound();
        byte tagType = br.ReadByte();
        if (tagType != 10) return compound; // TAG_Compound expected
        br.ReadInt16(); // name length (root name, usually empty)
        ReadTagPayload(br, compound, -1);
        return compound;
    }

    private static void ReadTagPayload(BinaryReader br, NbtCompound parent, int listElementType)
    {
        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            byte tagType;
            if (listElementType >= 0)
                tagType = (byte)listElementType;
            else
            {
                if (br.BaseStream.Position >= br.BaseStream.Length) return;
                tagType = br.ReadByte();
                if (tagType == 0) return; // TAG_End
            }

            string name = "";
            if (listElementType < 0)
            {
                short nameLen = br.ReadInt16();
                name = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
            }

            object value = ReadValue(br, tagType);
            if (listElementType < 0)
                parent.Set(name, value);

            if (listElementType >= 0) return; // list element: read one at a time
        }
    }

    private static object ReadValue(BinaryReader br, byte tagType) => tagType switch
    {
        1 => br.ReadByte(),                           // TAG_Byte
        2 => br.ReadInt16(),                          // TAG_Short
        3 => br.ReadInt32(),                          // TAG_Int
        4 => br.ReadInt64(),                          // TAG_Long
        5 => br.ReadSingle(),                         // TAG_Float
        6 => br.ReadDouble(),                         // TAG_Double
        7 => br.ReadBytes(br.ReadInt32()),            // TAG_Byte_Array
        8 => ReadString(br),                          // TAG_String
        9 => ReadList(br),                            // TAG_List
        10 => ReadCompoundTag(br),                    // TAG_Compound
        11 => ReadIntArray(br),                       // TAG_Int_Array
        12 => ReadLongArray(br),                      // TAG_Long_Array
        _ => throw new InvalidDataException($"Unknown NBT tag type: {tagType}")
    };

    private static string ReadString(BinaryReader br)
    {
        short len = br.ReadInt16();
        return Encoding.UTF8.GetString(br.ReadBytes(len));
    }

    private static NbtList ReadList(BinaryReader br)
    {
        byte elemType = br.ReadByte();
        int count = br.ReadInt32();
        var list = new NbtList(elemType);
        for (int i = 0; i < count; i++)
            list.Items.Add(ReadValue(br, elemType));
        return list;
    }

    private static NbtCompound ReadCompoundTag(BinaryReader br)
    {
        var compound = new NbtCompound();
        while (true)
        {
            byte tagType = br.ReadByte();
            if (tagType == 0) break; // TAG_End
            short nameLen = br.ReadInt16();
            string name = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
            compound.Set(name, ReadValue(br, tagType));
        }
        return compound;
    }

    private static int[] ReadIntArray(BinaryReader br)
    {
        int count = br.ReadInt32();
        var arr = new int[count];
        for (int i = 0; i < count; i++) arr[i] = br.ReadInt32();
        return arr;
    }

    private static long[] ReadLongArray(BinaryReader br)
    {
        int count = br.ReadInt32();
        var arr = new long[count];
        for (int i = 0; i < count; i++) arr[i] = br.ReadInt64();
        return arr;
    }
}
