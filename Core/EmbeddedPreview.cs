using System.Buffers.Binary;
namespace BimFamilias.Core;

// Reads the embedded PNG only. Never opens or saves a Revit document.
public static class EmbeddedPreview
{
    public static byte[]? Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var data = new byte[(int)Math.Min(stream.Length, 4 * 1024 * 1024)];
        stream.ReadExactly(data);
        ReadOnlySpan<byte> signature = [137,80,78,71,13,10,26,10];
        for (int i = 0; i < data.Length - 32; i++)
        {
            if (!data.AsSpan(i, 8).SequenceEqual(signature)) continue;
            var pos = i + 8;
            while (pos + 12 <= data.Length)
            {
                uint size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
                if (size > 4 * 1024 * 1024 || pos + 12L + size > data.Length) break;
                bool end = data[pos+4] == 73 && data[pos+5] == 69 && data[pos+6] == 78 && data[pos+7] == 68;
                pos += 12 + (int)size;
                if (end) return data.AsSpan(i, pos-i).ToArray();
            }
        }
        return null;
    }
}

