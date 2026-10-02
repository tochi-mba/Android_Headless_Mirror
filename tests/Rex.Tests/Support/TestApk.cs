using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Rex.Tests.Support;

/// <summary>
/// The smallest APK the app can read: a zip whose AndroidManifest.xml is Android's binary XML with
/// a manifest element carrying its package and version name. Nothing else of an APK is needed.
/// </summary>
public static class TestApk
{
    /// <summary>Where the string pool starts in a manifest built here, after the 8-byte file header.</summary>
    public const int PoolAt = 8;

    /// <summary>Writes an APK for this package and version at the path.</summary>
    public static void Write(string path, string package, string version)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var output = zip.CreateEntry("AndroidManifest.xml").Open();
        output.Write(Manifest(package, version));
    }

    /// <summary>Where the manifest element starts: just after the string pool.</summary>
    public static int ElementAt(byte[] xml) => PoolAt + (int)BinaryPrimitives.ReadUInt32LittleEndian(xml.AsSpan(PoolAt + 4, 4));

    /// <summary>
    /// The binary XML, its strings in UTF-8 or UTF-16 as Android writes either, with the long
    /// length forms a string past 127 bytes (UTF-8) or 32767 characters (UTF-16) needs.
    /// </summary>
    public static byte[] Manifest(string package, string version, bool utf16 = false, string element = "manifest")
    {
        string[] strings = [element, "package", "versionName", package, version];
        var stringData = new List<byte>();
        var offsets = new List<int>();
        foreach (var value in strings)
        {
            offsets.Add(stringData.Count);
            if (utf16)
            {
                if (value.Length > 0x7fff)
                {
                    Add16(stringData, 0x8000 | (value.Length >> 16));
                    Add16(stringData, value.Length & 0xffff);
                }
                else
                {
                    Add16(stringData, value.Length);
                }

                stringData.AddRange(Encoding.Unicode.GetBytes(value));
                Add16(stringData, 0);
            }
            else
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                AddLength8(stringData, value.Length);
                AddLength8(stringData, bytes.Length);
                stringData.AddRange(bytes);
                stringData.Add(0);
            }
        }

        while (stringData.Count % 4 != 0) stringData.Add(0);
        var poolSize = 28 + offsets.Count * 4 + stringData.Count;
        var pool = new byte[poolSize];
        Put16(pool, 0, 0x0001); Put16(pool, 2, 28); Put32(pool, 4, poolSize);
        Put32(pool, 8, strings.Length); Put32(pool, 16, utf16 ? 0 : 0x100); Put32(pool, 20, 28 + offsets.Count * 4);
        for (var i = 0; i < offsets.Count; i++) Put32(pool, 28 + i * 4, offsets[i]);
        stringData.CopyTo(pool, 28 + offsets.Count * 4);

        var node = new byte[36 + 40];
        Put16(node, 0, 0x0102); Put16(node, 2, 16); Put32(node, 4, node.Length);
        Put32(node, 16, -1); Put32(node, 20, 0); Put16(node, 24, 20); Put16(node, 26, 20); Put16(node, 28, 2);
        Attribute(node, 36, 1, 3);
        Attribute(node, 56, 2, 4);

        var xml = new byte[PoolAt + pool.Length + node.Length];
        Put16(xml, 0, 0x0003); Put16(xml, 2, 8); Put32(xml, 4, xml.Length);
        pool.CopyTo(xml, PoolAt);
        node.CopyTo(xml, PoolAt + pool.Length);
        return xml;
    }

    public static void Put16(byte[] bytes, int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at, 2), (ushort)value);
    public static void Put32(byte[] bytes, int at, int value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at, 4), unchecked((uint)value));

    private static void Attribute(byte[] bytes, int at, int name, int value)
    {
        Put32(bytes, at, -1); Put32(bytes, at + 4, name); Put32(bytes, at + 8, value);
        Put16(bytes, at + 12, 8); bytes[at + 15] = 3; Put32(bytes, at + 16, value);
    }

    private static void AddLength8(List<byte> data, int length)
    {
        if (length > 0x7f)
        {
            data.Add((byte)(0x80 | (length >> 8)));
        }

        data.Add((byte)(length & 0xff));
    }

    private static void Add16(List<byte> data, int value)
    {
        data.Add((byte)(value & 0xff));
        data.Add((byte)(value >> 8));
    }
}
