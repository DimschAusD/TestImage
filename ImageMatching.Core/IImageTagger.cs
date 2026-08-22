namespace ImageMatching.Core;

/// <summary>
/// Vergibt beim Indexieren Schlagworte für ein Bild anhand seines Embeddings.
/// Implementiert im Modul ImageMatching.Cnn (ZeroShotTagger über CLIP-Text).
/// Setzt ein CLIP-Bild-Embedding voraus – funktioniert also nur zusammen mit
/// dem CLIP-Deskriptor, nicht mit dem Kontur-Verfahren.
/// </summary>
public interface IImageTagger
{
    /// <summary>Liefert je Kategorie das bestpassende Schlagwort (Kategorie → Wert).</summary>
    IReadOnlyDictionary<string, string> Tag(float[] imageEmbedding);
}

/// <summary>
/// Offene Bildbeschreibung: liefert die erkannten Begriffe aus einem freien
/// Vokabular (implementiert vom OpenVocabTagger im Modul ImageMatching.Cnn).
/// Setzt – wie das Tagging – ein CLIP-Bild-Embedding voraus.
/// </summary>
public interface IConceptTagger
{
    /// <summary>Die am besten passenden Begriffe des Bildes (bereits über der Schwelle, sortiert).</summary>
    IReadOnlyList<string> Describe(float[] imageEmbedding);
}
