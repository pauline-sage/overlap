using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Overlap.Api.Auth;

// The browser submits fixed-size RGB pixels, never an executable or uploaded image container.
// Construct a fresh PNG with no metadata and no server-side image decoder dependency.
public static class AvatarImage
{
    public const int Size = 128;
    public static byte[] Encode(byte[] pixels) => EncodeSquare(pixels, Size);
    public static byte[] EncodeSquare(byte[] pixels, int size)
    {
        if (size is not (128 or 512) || pixels.Length != size * size * 3) throw new ArgumentException("Invalid avatar dimensions.");
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), size);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), size);
        header[8] = 8; header[9] = 2;
        Chunk(png, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zip = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            for (var row = 0; row < size; row++)
            {
                zip.WriteByte(0);
                zip.Write(pixels, row * size * 3, size * 3);
            }
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }
    private static void Chunk(Stream stream, string type, byte[] content)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, content.Length);
        stream.Write(length);
        var bytes = Encoding.ASCII.GetBytes(type).Concat(content).ToArray();
        stream.Write(bytes);
        uint crc = 0xffffffff;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
        stream.Write(length);
    }
}
