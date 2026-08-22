namespace ImageMatching.Core;

/// <summary>
/// Form-Fingerabdruck eines Bildes über ein Histogramm der Kanten-
/// orientierungen (verwandt mit HOG, aber global statt pro Zelle).
///
/// Eigenschaften:
///  • translationsinvariant – wo im Bild die Kanten liegen, ist egal;
///  • skalierungsinvariant  – durch L2-Normierung unabhängig von Bildgröße/Kontrast;
///  • rotationstolerant     – beim Vergleich wird das Histogramm zyklisch
///                            verschoben und die beste Übereinstimmung genommen.
///
/// Damit eignet es sich für „gleiches Objekt, andere Lage, gleich oder abgewandelt“.
/// </summary>
public static class ShapeDescriptor
{
    /// <summary>Anzahl der Orientierungs-Bins über 0..180° (hier 5° pro Bin).</summary>
    public const int Bins = 36;

    /// <summary>Berechnet den L2-normierten Fingerabdruck eines Graustufenbildes.</summary>
    public static float[] Build(GrayImage img)
    {
        GradientField g = Sobel.Compute(img);
        var hist = new float[Bins];

        // Schwelle relativ zur stärksten Kante: schwaches Rauschen ausblenden.
        float max = 0f;
        for (int i = 0; i < g.Magnitude.Length; i++)
            if (g.Magnitude[i] > max) max = g.Magnitude[i];
        float threshold = max * 0.10f;

        float binSize = MathF.PI / Bins;
        for (int i = 0; i < g.Magnitude.Length; i++)
        {
            float m = g.Magnitude[i];
            if (m <= threshold) continue;

            // Weiche Zuordnung auf die zwei benachbarten Bins (stabiler gegen Drehung).
            float pos = g.Orientation[i] / binSize;      // 0..Bins
            int b0 = (int)MathF.Floor(pos) % Bins;
            int b1 = (b0 + 1) % Bins;
            float frac = pos - MathF.Floor(pos);
            hist[b0] += m * (1f - frac);
            hist[b1] += m * frac;
        }

        // L2-Normierung -> unabhängig von Bildgröße und Kontrast.
        double norm = 0;
        for (int b = 0; b < Bins; b++) norm += hist[b] * (double)hist[b];
        norm = Math.Sqrt(norm);
        if (norm > 1e-9)
            for (int b = 0; b < Bins; b++) hist[b] = (float)(hist[b] / norm);

        return hist;
    }

    /// <summary>
    /// Ähnlichkeit zweier Fingerabdrücke im Bereich 0..1, rotationsinvariant:
    /// Kosinus-Ähnlichkeit über die beste zyklische Verschiebung. Da beide
    /// Histogramme L2-normiert sind, ist das Skalarprodukt bereits der Kosinus.
    /// </summary>
    public static float Similarity(float[] a, float[] b)
    {
        if (a.Length != Bins || b.Length != Bins) return 0f;

        float best = 0f;
        for (int shift = 0; shift < Bins; shift++)
        {
            float dot = 0f;
            for (int i = 0; i < Bins; i++)
                dot += a[i] * b[(i + shift) % Bins];
            if (dot > best) best = dot;
        }
        return best;
    }
}
