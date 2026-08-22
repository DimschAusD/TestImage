using System.Numerics;

namespace ImageMatching.Core;

/// <summary>
/// Perceptual Hash (dHash) zum schnellen Finden gleicher/fast gleicher Bilder.
///
/// Das Bild wird auf ein winziges 9×8-Raster heruntergerechnet; pro Zeile wird
/// jeder Pixel mit seinem rechten Nachbarn verglichen (heller? → 1-Bit). Das
/// ergibt einen 64-Bit-Fingerabdruck, der gegen Skalierung, Kompression und
/// leichte Farbänderungen robust ist. Die Ähnlichkeit zweier Bilder ist die
/// Hamming-Distanz (Anzahl unterschiedlicher Bits): 0 = identisch, klein = fast gleich.
/// </summary>
public static class PerceptualHash
{
    private const int W = 9; // eine Spalte mehr für den Nachbarvergleich
    private const int H = 8;

    /// <summary>Berechnet den 64-Bit-dHash eines Graustufenbildes.</summary>
    public static ulong Compute(GrayImage img)
    {
        float[] small = Downsample(img, W, H);

        ulong hash = 0;
        int bit = 0;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W - 1; x++)
            {
                if (small[y * W + x] > small[y * W + x + 1])
                    hash |= 1UL << bit;
                bit++;
            }
        }
        return hash; // 8 Zeilen × 8 Vergleiche = 64 Bit
    }

    /// <summary>Anzahl unterschiedlicher Bits (0 = identisch, größer = unähnlicher).</summary>
    public static int HammingDistance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    /// <summary>Verkleinert das Bild per Flächenmittelung auf nw×nh Pixel.</summary>
    private static float[] Downsample(GrayImage img, int nw, int nh)
    {
        var dst = new float[nw * nh];
        for (int ny = 0; ny < nh; ny++)
        {
            for (int nx = 0; nx < nw; nx++)
            {
                int x0 = (int)((long)nx * img.Width / nw);
                int x1 = (int)((long)(nx + 1) * img.Width / nw);
                int y0 = (int)((long)ny * img.Height / nh);
                int y1 = (int)((long)(ny + 1) * img.Height / nh);
                if (x1 <= x0) x1 = x0 + 1;
                if (y1 <= y0) y1 = y0 + 1;

                double sum = 0;
                int count = 0;
                for (int y = y0; y < y1 && y < img.Height; y++)
                    for (int x = x0; x < x1 && x < img.Width; x++)
                    {
                        sum += img.Pixels[y * img.Width + x];
                        count++;
                    }
                dst[ny * nw + nx] = count > 0 ? (float)(sum / count) : 0f;
            }
        }
        return dst;
    }
}
