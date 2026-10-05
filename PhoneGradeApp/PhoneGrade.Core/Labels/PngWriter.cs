using System.IO.Compression;

namespace PhoneGrade.Core;

/// <summary>
/// Writes a black and white PNG, and nothing else.
///
/// The barcode on a label is the one picture the app has to draw itself. QuestPDF
/// bundles its own copy of SkiaSharp and does not expose it, and pulling SkiaSharp
/// into this project would put two copies in one executable and change the text
/// metrics the label is laid out with, which is the sort of thing that shows up as
/// a blank label rather than as a build error.
///
/// So the bytes are written here. A one bit greyscale PNG is a signature, a header,
/// one compressed block of scanlines and an end marker, and the compressor is
/// "stored": no compression at all, which for a barcode is a few hundred bytes and
/// costs nothing to be simple about.
/// </summary>
public static class PngWriter
{
    /// <summary>
    /// A barcode as an eight bit greyscale PNG, one pixel per dot.
    ///
    /// Eight bit rather than one bit: one bit is a smaller file, and every decoder
    /// this has to satisfy has to be asked whether it wants it. QuestPDF's image
    /// handling dropped the one bit form without saying so, which left the label
    /// with its words on it and no barcode, which is the one fault here that would
    /// not have shown up as an error anywhere.
    /// </summary>
    /// <param name="bars">True where there is ink.</param>
    /// <param name="width">How many pixels wide, in total.</param>
    /// <param name="height">How many pixels high.</param>
    public static byte[] Barcode(IReadOnlyList<bool> bars, int width, int height)
    {
        var pixels = new bool[width * height];
        int x = 0;
        foreach (bool isBar in bars)
        {
            // A bar is three dots and a space is one, which is the ratio Code39 is
            // read at. Only the bar is ink; the space is left white and is what
            // makes it a barcode rather than a black rectangle.
            int span = isBar ? 3 : 1;
            for (int i = 0; i < span && x < width; i++, x++)
            {
                if (!isBar) continue;
                for (int y = 0; y < height; y++) pixels[(y * width) + x] = true;
            }
        }

        return Greyscale(pixels, width, height, oneBit: false);
    }

    /// <summary>A one bit or eight bit greyscale PNG of the given pixels.</summary>
    private static byte[] Greyscale(bool[] pixels, int width, int height, bool oneBit)
    {
        int stride = oneBit ? (width + 7) / 8 : width;
        var scanlines = new byte[(stride + 1) * height];

        for (int y = 0; y < height; y++)
        {
            int at = y * (stride + 1);
            scanlines[at] = 0; // the filter byte: none

            for (int x = 0; x < width; x++)
            {
                // A set bit is ink in a one bit image, so the bits are written
                // inverted against the pixel values.
                bool ink = pixels[(y * width) + x];
                if (oneBit)
                {
                    if (ink) scanlines[at + 1 + (x / 8)] |= (byte)(0x80 >> (x % 8));
                }
                else if (!ink)
                {
                    scanlines[at + 1 + x] = 0xFF;
                }
            }
        }

        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        WriteInt(header, 0, width);
        WriteInt(header, 4, height);
        header[8] = oneBit ? (byte)1 : (byte)8;  // bits per sample
        header[9] = 0;                            // greyscale, no colour
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", Zlib(scanlines));
        WriteChunk(png, "IEND", []);

        return png.ToArray();
    }

    /// <summary>
    /// A zlib stream of stored deflate blocks.
    ///
    /// Stored rather than squeezed on purpose: a barcode is mostly runs of identical
    /// bytes, but a compressor here would be a hundred lines to save a few hundred
    /// bytes on a file that is written once and read by a printer.
    /// </summary>
    private static byte[] Zlib(byte[] data)
    {
        using var stream = new MemoryStream();
        stream.WriteByte(0x78); // deflate, 32k window
        stream.WriteByte(0x01); // no dictionary, fastest

        int offset = 0;
        do
        {
            int take = Math.Min(65535, data.Length - offset);
            bool last = offset + take >= data.Length;

            stream.WriteByte((byte)(last ? 1 : 0));
            stream.WriteByte((byte)take);
            stream.WriteByte((byte)(take >> 8));
            stream.WriteByte((byte)~take);
            stream.WriteByte((byte)((~take) >> 8));
            stream.Write(data, offset, take);

            offset += take;
        }
        while (offset < data.Length);

        uint adler = Adler32(data);
        stream.WriteByte((byte)(adler >> 24));
        stream.WriteByte((byte)(adler >> 16));
        stream.WriteByte((byte)(adler >> 8));
        stream.WriteByte((byte)adler);

        return stream.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1;
        uint b = 0;
        foreach (byte value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }

    private static void WriteChunk(Stream into, string kind, byte[] data)
    {
        var length = new byte[4];
        WriteInt(length, 0, data.Length);
        into.Write(length);

        var body = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) body[i] = (byte)kind[i];
        data.CopyTo(body, 4);
        into.Write(body);

        var crc = new byte[4];
        WriteInt(crc, 0, unchecked((int)Crc32(body)));
        into.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static void WriteInt(byte[] into, int at, int value)
    {
        into[at] = (byte)(value >> 24);
        into[at + 1] = (byte)(value >> 16);
        into[at + 2] = (byte)(value >> 8);
        into[at + 3] = (byte)value;
    }
}
