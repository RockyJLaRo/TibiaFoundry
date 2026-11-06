using System.Buffers.Binary;

namespace TibiaFoundry.Core.Imaging;

/// <summary>
/// Writes 32-bit BMP images in the exact layout of the client's sprite sheets: a 14-byte file
/// header, a BITMAPV4HEADER with BI_BITFIELDS (masks R=00FF0000 G=0000FF00 B=000000FF A=FF000000),
/// bottom-up rows, no color table. A sheet decoded with <see cref="BmpDecoder"/> and encoded again
/// is byte-identical to the original (verified in the tests).
/// </summary>
public static class BmpEncoder
{
    private const int FileHeaderSize = 14;
    private const int V4HeaderSize = 108;
    public const int PixelOffset = FileHeaderSize + V4HeaderSize;

    public static byte[] Encode(RgbaImage image)
    {
        int stride = image.Width * 4;
        var data = new byte[PixelOffset + stride * image.Height];
        var span = data.AsSpan();
        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], PixelOffset);
        BinaryPrimitives.WriteInt32LittleEndian(span[14..], V4HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span[18..], image.Width);
        BinaryPrimitives.WriteInt32LittleEndian(span[22..], image.Height);
        BinaryPrimitives.WriteUInt16LittleEndian(span[26..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[28..], 32);
        BinaryPrimitives.WriteInt32LittleEndian(span[30..], 3);
        // Image size, resolution and color counts stay 0, as in the client's files.
        BinaryPrimitives.WriteUInt32LittleEndian(span[54..], 0x00FF0000);
        BinaryPrimitives.WriteUInt32LittleEndian(span[58..], 0x0000FF00);
        BinaryPrimitives.WriteUInt32LittleEndian(span[62..], 0x000000FF);
        BinaryPrimitives.WriteUInt32LittleEndian(span[66..], 0xFF000000);
        // Color space type, end points and gamma stay 0.
        for (int y = 0; y < image.Height; y++)
        {
            int dst = PixelOffset + (image.Height - 1 - y) * stride;
            var row = image.Pixels.AsSpan(y * image.Width, image.Width);
            for (int x = 0; x < row.Length; x++) BinaryPrimitives.WriteUInt32LittleEndian(span[(dst + x * 4)..], row[x]);
        }
        return data;
    }
}
