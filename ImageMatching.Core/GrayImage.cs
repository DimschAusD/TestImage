namespace ImageMatching.Core;

/// <summary>
/// Framework-unabhängiges Graustufenbild: nur Breite, Höhe und Helligkeits-
/// werte im Bereich 0..1 (zeilenweise gespeichert). Die Kernbibliothek kennt
/// weder WPF noch System.Drawing – das Laden/Dekodieren der Bilder übernimmt
/// die jeweilige Anwendung und übergibt hier fertige Pixel.
/// </summary>
public sealed class GrayImage
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Helligkeitswerte 0..1, Länge = Width*Height, Zeile für Zeile.</summary>
    public float[] Pixels { get; }

    public GrayImage(int width, int height, float[] pixels)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Breite und Höhe müssen positiv sein.");
        if (pixels.Length != width * height)
            throw new ArgumentException("Pixelanzahl passt nicht zu Breite×Höhe.");

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public float this[int x, int y] => Pixels[y * Width + x];
}
