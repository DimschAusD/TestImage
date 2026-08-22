namespace ImageMatching.Core;

/// <summary>
/// Framework-unabhängiges Farbbild (RGB, 8 Bit je Kanal, interleaved).
/// Wird für den CNN-Weg gebraucht, weil Netze wie CLIP Farbe erwarten –
/// im Gegensatz zum Graustufen-Kontur-Weg (<see cref="GrayImage"/>).
/// </summary>
public sealed class RgbImage
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>RGB-Werte 0..255, Länge = Width*Height*3, Reihenfolge R,G,B.</summary>
    public byte[] Pixels { get; }

    public RgbImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Breite und Höhe müssen positiv sein.");
        if (pixels.Length != width * height * 3)
            throw new ArgumentException("Pixelanzahl passt nicht zu Width*Height*3.");

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Wandelt in ein Graustufenbild um (Luminanz-Gewichtung, 0..1).</summary>
    public GrayImage ToGray()
    {
        var px = new float[Width * Height];
        for (int i = 0, p = 0; i < px.Length; i++, p += 3)
            px[i] = (0.299f * Pixels[p] + 0.587f * Pixels[p + 1] + 0.114f * Pixels[p + 2]) / 255f;
        return new GrayImage(Width, Height, px);
    }

    /// <summary>
    /// Bilineare Skalierung auf nw×nh Pixel; liefert erneut ein RGB-Bytearray
    /// (Länge nw*nh*3). Wird für die feste Eingabegröße der Netze gebraucht.
    /// </summary>
    public byte[] ResampleBilinear(int nw, int nh)
    {
        var dst = new byte[nw * nh * 3];
        for (int ny = 0; ny < nh; ny++)
        {
            float sy = nh == 1 ? 0 : (float)ny * (Height - 1) / (nh - 1);
            int y0 = (int)sy;
            int y1 = Math.Min(y0 + 1, Height - 1);
            float fy = sy - y0;

            for (int nx = 0; nx < nw; nx++)
            {
                float sx = nw == 1 ? 0 : (float)nx * (Width - 1) / (nw - 1);
                int x0 = (int)sx;
                int x1 = Math.Min(x0 + 1, Width - 1);
                float fx = sx - x0;

                for (int c = 0; c < 3; c++)
                {
                    float p00 = Pixels[(y0 * Width + x0) * 3 + c];
                    float p01 = Pixels[(y0 * Width + x1) * 3 + c];
                    float p10 = Pixels[(y1 * Width + x0) * 3 + c];
                    float p11 = Pixels[(y1 * Width + x1) * 3 + c];
                    float top = p00 + (p01 - p00) * fx;
                    float bot = p10 + (p11 - p10) * fx;
                    dst[(ny * nw + nx) * 3 + c] = (byte)(top + (bot - top) * fy + 0.5f);
                }
            }
        }
        return dst;
    }
}
