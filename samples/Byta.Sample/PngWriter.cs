// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Byta.Sample;

/// <summary>
/// 一个不依赖任何第三方库的最小 PNG 编码器（8bit RGBA，无滤波）。
/// 只是为了让示例能直接吐出可看的图片，不属于 Byta 库本体。
/// </summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary><paramref name="rgba"/> 是自上而下的紧密排列像素。</summary>
    public static void Write(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length < width * height * 4)
            throw new ArgumentException("像素数据不足", nameof(rgba));

        using var file = File.Create(path);
        file.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // RGBA
        ihdr[10] = 0;  // deflate
        ihdr[11] = 0;  // adaptive filtering
        ihdr[12] = 0;  // no interlace
        WriteChunk(file, "IHDR", ihdr);

        // 每行前加一个 filter 字节 0（None）
        int stride = width * 4;
        byte[] raw = new byte[(stride + 1) * height];
        for (int y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = 0;
            rgba.Slice(y * stride, stride).CopyTo(raw.AsSpan(y * (stride + 1) + 1, stride));
        }

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
                zlib.Write(raw);

            WriteChunk(file, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        }

        WriteChunk(file, "IEND", ReadOnlySpan<byte>.Empty);
    }

    /// <summary>GL 的 <c>glReadPixels</c> 是左下角原点，PNG 是左上角原点。</summary>
    public static void FlipVertically(int width, int height, Span<byte> rgba)
    {
        int stride = width * 4;
        byte[] temp = new byte[stride];
        for (int y = 0; y < height / 2; y++)
        {
            var top = rgba.Slice(y * stride, stride);
            var bottom = rgba.Slice((height - 1 - y) * stride, stride);
            top.CopyTo(temp);
            bottom.CopyTo(top);
            temp.AsSpan(0, stride).CopyTo(bottom);
        }
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)data.Length);
        stream.Write(buffer);

        Span<byte> typeBytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(type, typeBytes);
        stream.Write(typeBytes);
        stream.Write(data);

        uint crc = Crc32(typeBytes, data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        stream.Write(buffer);
    }

    private static uint Crc32(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte value in a)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        foreach (byte value in b)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
}
