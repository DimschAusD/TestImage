namespace ImageMatching.Core;

/// <summary>Ergebnis der Sobel-Faltung: Gradientenstärke und -richtung je Pixel.</summary>
public readonly struct GradientField
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Betrag des Gradienten je Pixel.</summary>
    public float[] Magnitude { get; }

    /// <summary>Richtung des Gradienten je Pixel in Radiant, 0..π (vorzeichenlos).</summary>
    public float[] Orientation { get; }

    public GradientField(int width, int height, float[] magnitude, float[] orientation)
    {
        Width = width;
        Height = height;
        Magnitude = magnitude;
        Orientation = orientation;
    }
}

/// <summary>
/// Selbst implementierte Sobel-Kantenerkennung. Liefert pro Pixel Stärke und
/// Richtung des Helligkeitsgradienten – die Grundlage für den Form-Fingerabdruck.
/// </summary>
public static class Sobel
{
    public static GradientField Compute(GrayImage img)
    {
        int w = img.Width, h = img.Height;
        float[] p = img.Pixels;
        var mag = new float[w * h];
        var ori = new float[w * h];

        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                float tl = p[(y - 1) * w + x - 1], tc = p[(y - 1) * w + x], tr = p[(y - 1) * w + x + 1];
                float ml = p[y * w + x - 1],                                mr = p[y * w + x + 1];
                float bl = p[(y + 1) * w + x - 1], bc = p[(y + 1) * w + x], br = p[(y + 1) * w + x + 1];

                float gx = (tr + 2 * mr + br) - (tl + 2 * ml + bl);
                float gy = (bl + 2 * bc + br) - (tl + 2 * tc + tr);

                int i = y * w + x;
                mag[i] = MathF.Sqrt(gx * gx + gy * gy);

                float a = MathF.Atan2(gy, gx);   // -π..π
                if (a < 0) a += MathF.PI;         // 0..π (Kantenrichtung ist vorzeichenlos)
                ori[i] = a;
            }
        }

        return new GradientField(w, h, mag, ori);
    }
}
