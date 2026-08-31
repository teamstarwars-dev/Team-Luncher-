using System.Text;

namespace TeamLauncher;

/// <summary>
/// Écriture NBT ( Named Binary Tag ) — sérialise les données en bytes.
/// </summary>
public static class NbtWriter
{
    public static byte[] WriteCompound(NbtCompound compound)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((byte)10); // TAG_Compound
        bw.Write((short)0); // root name
        WriteCompoundPayload(bw, compound);
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
                bw.Write((byte)1); WriteName(bw, name); bw.Write(v); break;
            case short v:
                bw.Write((byte)2); WriteName(bw, name); bw.Write(v); break;
            case int v:
                bw.Write((byte)3); WriteName(bw, name); bw.Write(v); break;
            case long v:
                bw.Write((byte)4); WriteName(bw, name); bw.Write(v); break;
            case float v:
                bw.Write((byte)5); WriteName(bw, name); bw.Write(v); break;
            case double v:
                bw.Write((byte)6); WriteName(bw, name); bw.Write(v); break;
            case byte[] v:
                bw.Write((byte)7); WriteName(bw, name); bw.Write(v.Length); bw.Write(v); break;
            case string v:
                bw.Write((byte)8); WriteName(bw, name);
                var bytes = Encoding.UTF8.GetBytes(v);
                bw.Write((short)bytes.Length); bw.Write(bytes); break;
            case NbtList v:
                bw.Write((byte)9); WriteName(bw, name);
                bw.Write(v.ElementType);
                bw.Write(v.Items.Count);
                foreach (var item in v.Items)
                    WriteValueInline(bw, v.ElementType, item);
                break;
            case NbtCompound v:
                bw.Write((byte)10); WriteName(bw, name);
                WriteCompoundPayload(bw, v); break;
            case int[] v:
                bw.Write((byte)11); WriteName(bw, name);
                bw.Write(v.Length); foreach (var i in v) bw.Write(i); break;
            case long[] v:
                bw.Write((byte)12); WriteName(bw, name);
                bw.Write(v.Length); foreach (var l in v) bw.Write(l); break;
        }
    }

    private static void WriteValueInline(BinaryWriter bw, byte tagType, object value)
    {
        switch (tagType)
        {
            case 1: bw.Write((byte)value); break;
            case 2: bw.Write((short)value); break;
            case 3: bw.Write((int)value); break;
            case 4: bw.Write((long)value); break;
            case 5: bw.Write((float)value); break;
            case 6: bw.Write((double)value); break;
            case 7: var ba = (byte[])value; bw.Write(ba.Length); bw.Write(ba); break;
            case 8: var s = (string)value; var sb = Encoding.UTF8.GetBytes(s);
                bw.Write((short)sb.Length); bw.Write(sb); break;
            case 10: WriteCompoundPayload(bw, (NbtCompound)value); break;
            case 11: var ia = (int[])value; bw.Write(ia.Length); foreach (var i in ia) bw.Write(i); break;
            case 12: var la = (long[])value; bw.Write(la.Length); foreach (var l in la) bw.Write(l); break;
        }
    }

    private static void WriteName(BinaryWriter bw, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        bw.Write((short)bytes.Length);
        bw.Write(bytes);
    }
}
