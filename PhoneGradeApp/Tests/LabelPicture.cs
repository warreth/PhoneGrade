using SkiaSharp;

namespace PhoneGrade.Tests;

/// <summary>
/// A rendered PDF, measured like a photograph.
/// <para>
/// The label PDF once came out as a barcode and nothing else. The file existed,
/// it parsed, it had every word written into it, and it printed a blank label
/// with bars on it: the words were clipped out of the page by a height constraint
/// on the band above them. None of that is visible from the bytes. It is only
/// visible from the pixels, which is why this is here and why the label tests ask
/// how much ink landed where.
/// </para>
/// </summary>
public sealed class LabelPicture : IDisposable
{
    private readonly SKBitmap _bitmap;

    private LabelPicture(SKBitmap bitmap) => _bitmap = bitmap;

    public int Width => _bitmap.Width;

    public int Height => _bitmap.Height;

    /// <summary>Draws a PDF at a resolution a scanner would recognise.</summary>
    public static LabelPicture Of(byte[] pdf, int dpi = 300)
    {
        // A PDFium backed raster, in a throwaway project that has no such thing as
        // a licence to get wrong.
        using SKBitmap source = PDFtoImage.Conversion.ToImage(pdf, options: new(Dpi: dpi));
        var copy = new SKBitmap(source.Info);
        source.CopyTo(copy);
        return new LabelPicture(copy);
    }

    /// <summary>
    /// How much of the page between two fractions of its height carries ink.
    ///
    /// Sampled on a grid rather than counted whole: a label is small and a 300 dpi
    /// render of one is a couple of million pixels, most of them white.
    /// </summary>
    public long InkBetween(double fromTop, double toTop)
    {
        int first = (int)(_bitmap.Height * fromTop);
        int last = Math.Min(_bitmap.Height, (int)(_bitmap.Height * toTop));
        long ink = 0;

        for (int y = first; y < last; y += 2)
            for (int x = 0; x < _bitmap.Width; x += 2)
                if (_bitmap.GetPixel(x, y).Red < 128) ink++;

        return ink;
    }

    /// <summary>The whole page's worth of ink.</summary>
    public long Ink() => InkBetween(0d, 1d);

    /// <summary>Whether two renders of the same label are pixel for pixel the same.</summary>
    public bool SameAs(LabelPicture other)
    {
        if (Width != other.Width || Height != other.Height) return false;

        for (int y = 0; y < Height; y += 2)
            for (int x = 0; x < Width; x += 2)
                if (_bitmap.GetPixel(x, y).Red != other._bitmap.GetPixel(x, y).Red) return false;

        return true;
    }

    /// <summary>The page rendered as a PNG, for a human to look at.</summary>
    public byte[] Png()
    {
        using SKData data = _bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void Dispose() => _bitmap.Dispose();
}
