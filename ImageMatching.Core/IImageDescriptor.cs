namespace ImageMatching.Core;

/// <summary>
/// Austauschbarer Bild-Fingerabdruck. So kann der <see cref="ImageIndex"/> mit
/// verschiedenen Verfahren arbeiten, ohne dass sich Index oder Abfrage ändern:
///  • <see cref="ShapeDescriptorStrategy"/> – Kontur-Histogramm (Weg A);
///  • CnnDescriptor (Modul ImageMatching.Cnn) – CLIP-Embedding (Weg B).
/// Eingang ist immer ein <see cref="RgbImage"/>; ein reines Kontur-Verfahren
/// wandelt intern selbst in Graustufen um.
/// </summary>
public interface IImageDescriptor
{
    /// <summary>Berechnet den Fingerabdruck (Merkmalsvektor) eines Farbbildes.</summary>
    float[] Describe(RgbImage image);

    /// <summary>Ähnlichkeit zweier Fingerabdrücke im Bereich 0..1.</summary>
    float Similarity(float[] a, float[] b);
}

/// <summary>Kontur-Fingerabdruck (Weg A) als austauschbare Strategie.</summary>
public sealed class ShapeDescriptorStrategy : IImageDescriptor
{
    public float[] Describe(RgbImage image) => ShapeDescriptor.Build(image.ToGray());
    public float Similarity(float[] a, float[] b) => ShapeDescriptor.Similarity(a, b);
}
