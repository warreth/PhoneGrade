
using System.IO.Compression;

namespace Tests;

/// <summary>Minimal PNG reader: decodes a truecolor 8-bit RGB(A) PNG and computes
/// average luminance. Enough for visual theme tests — no external dependency.</summary>
public static class PngLuminance
{
    public static double Average(byte[] png)
    {
        // Parse PNG chunks
        int pos = 8; // signature
        int width = 0, height = 0;
        byte bitDepth = 0, colorType = 0;
        var idat = new List<byte>();

        while (pos < png.Length)
        {
            int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
            string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            byte[] data = png[(pos + 8)..(pos + 8 + len)];

            if (type == "IHDR")
            {
                width = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
                height = (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7];
                bitDepth = data[8];
                colorType = data[9];
            }
            else if (type == "IDAT") idat.AddRange(data);
            else if (type == "IEND") break;

            pos += 12 + len;
        }

        if (bitDepth != 8 || (colorType != 2 && colorType != 6))
            throw new NotSupportedException($"unexpected PNG: depth={bitDepth} color={colorType}");

        int channels = colorType == 6 ? 4 : 3;

        // Inflate IDAT
        byte[] raw;
        using (var z = new ZLibStream(new MemoryStream(idat.ToArray()), CompressionMode.Decompress))
        using (var outMs = new MemoryStream())
        {
            z.CopyTo(outMs);
            raw = outMs.ToArray();
        }

        // Defilter: we only need filter type 0/1/2 rows' first pixel sample, but be correct: full defilter.
        int stride = width * channels + 1;
        if (raw.Length < stride * height) throw new InvalidOperationException("truncated PNG");

        var px = new byte[channels];
        double total = 0;
        long n = 0;
        var prevRow = new byte[width * channels];

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * stride;
            byte filter = raw[rowStart];
            var curRow = new byte[width * channels];
            for (int x = 0; x < width * channels; x++)
            {
                byte val = raw[rowStart + 1 + x];
                byte left = x >= channels ? curRow[x - channels] : (byte)0;
                byte up = prevRow[x];
                byte upLeft = x >= channels ? prevRow[x - channels] : (byte)0;
                curRow[x] = filter switch
                {
                    0 => val,
                    1 => (byte)(val + left),
                    2 => (byte)(val + up),
                    3 => (byte)(val + ((left + up) / 2)),
                    4 => (byte)(val + Paeth(left, up, upLeft)),
                    _ => throw new NotSupportedException($"filter {filter}"),
                };
            }
            // sample every 8th pixel of this row
            for (int x = 0; x < width; x += 8)
            {
                byte r = curRow[x * channels];
                byte g = curRow[x * channels + (channels > 1 ? 1 : 0)];
                byte b = curRow[x * channels + (channels > 2 ? 2 : 0)];
                total += 0.2126 * r + 0.7152 * g + 0.0722 * b;
                n++;
            }
            prevRow = curRow;
        }
        return total / n;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
