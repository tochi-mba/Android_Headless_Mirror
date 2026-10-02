using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Rex.Core;

public sealed record ApkIdentity(string Package, string? VersionName);

/// <summary>Reads the small amount of binary AndroidManifest.xml needed to describe an APK.</summary>
public static class ApkManifest
{
    private const ushort StringPool = 0x0001;
    private const ushort StartElement = 0x0102;
    private const uint Utf8 = 0x00000100;
    private const byte StringValue = 0x03;

    /// <summary>The package and version from an APK, or null for any malformed or unreadable file.</summary>
    public static ApkIdentity? Read(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.GetEntry("AndroidManifest.xml");
            if (entry is null || entry.Length is <= 0 or > 16 * 1024 * 1024)
            {
                return null;
            }

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return Read(memory.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    internal static ApkIdentity? Read(ReadOnlySpan<byte> xml)
    {
        try
        {
            if (xml.Length < 8 || U16(xml, 0) != 0x0003 || U32(xml, 4) > xml.Length)
            {
                return null;
            }

            string[]? strings = null;
            for (var at = (int)U16(xml, 2); at + 8 <= xml.Length;)
            {
                var type = U16(xml, at);
                var header = U16(xml, at + 2);
                var size = checked((int)U32(xml, at + 4));
                if (header < 8 || size < header || at + size > xml.Length)
                {
                    return null;
                }

                if (type == StringPool)
                {
                    strings = ReadStrings(xml.Slice(at, size));
                    if (strings is null)
                    {
                        return null;
                    }
                }
                else if (type == StartElement && strings is not null && Element(xml.Slice(at, size), strings) is { } app)
                {
                    return app;
                }

                at += size;
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException or DecoderFallbackException)
        {
            return null;
        }

        return null;
    }

    private static string[]? ReadStrings(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length < 28)
        {
            return null;
        }

        var count = checked((int)U32(chunk, 8));
        var flags = U32(chunk, 16);
        var start = checked((int)U32(chunk, 20));
        if (count < 0 || count > 100_000 || 28 + count * 4 > chunk.Length || start > chunk.Length)
        {
            return null;
        }

        var answer = new string[count];
        for (var i = 0; i < count; i++)
        {
            var offset = checked(start + (int)U32(chunk, 28 + i * 4));
            answer[i] = (flags & Utf8) != 0 ? ReadUtf8(chunk, offset) : ReadUtf16(chunk, offset);
        }

        return answer;
    }

    private static string ReadUtf8(ReadOnlySpan<byte> bytes, int at)
    {
        _ = Length8(bytes, ref at);
        var byteLength = Length8(bytes, ref at);
        if (byteLength < 0 || at + byteLength >= bytes.Length || bytes[at + byteLength] != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        return new UTF8Encoding(false, true).GetString(bytes.Slice(at, byteLength));
    }

    private static string ReadUtf16(ReadOnlySpan<byte> bytes, int at)
    {
        var length = Length16(bytes, ref at);
        var byteLength = checked(length * 2);
        if (length < 0 || at + byteLength + 2 > bytes.Length || U16(bytes, at + byteLength) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        return Encoding.Unicode.GetString(bytes.Slice(at, byteLength));
    }

    private static int Length8(ReadOnlySpan<byte> bytes, ref int at)
    {
        var first = bytes[at++];
        return (first & 0x80) == 0 ? first : ((first & 0x7f) << 8) | bytes[at++];
    }

    private static int Length16(ReadOnlySpan<byte> bytes, ref int at)
    {
        var first = U16(bytes, at);
        at += 2;
        if ((first & 0x8000) == 0)
        {
            return first;
        }

        var second = U16(bytes, at);
        at += 2;
        return ((first & 0x7fff) << 16) | second;
    }

    private static ApkIdentity? Element(ReadOnlySpan<byte> chunk, IReadOnlyList<string> strings)
    {
        if (chunk.Length < 36 || Name(strings, U32(chunk, 20)) != "manifest")
        {
            return null;
        }

        var attributeStart = U16(chunk, 24);
        var attributeSize = U16(chunk, 26);
        var attributeCount = U16(chunk, 28);
        var first = checked(16 + attributeStart);
        if (attributeSize < 20 || first + attributeSize * attributeCount > chunk.Length)
        {
            return null;
        }

        string? package = null;
        string? version = null;
        for (var i = 0; i < attributeCount; i++)
        {
            var at = first + i * attributeSize;
            var name = Name(strings, U32(chunk, at + 4));
            var raw = U32(chunk, at + 8);
            var type = chunk[at + 15];
            var data = U32(chunk, at + 16);
            var value = raw != uint.MaxValue ? Name(strings, raw) : type == StringValue ? Name(strings, data) : null;
            if (name == "package")
            {
                package = value;
            }
            else if (name == "versionName")
            {
                version = value;
            }
        }

        return PackageName.IsValid(package) ? new ApkIdentity(package!, version) : null;
    }

    private static string? Name(IReadOnlyList<string> strings, uint index) => index < strings.Count ? strings[(int)index] : null;
    private static ushort U16(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(at, 2));
    private static uint U32(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(at, 4));
}
