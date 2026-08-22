using ImageMatching.Core;

namespace ImageMatching.Cnn;

/// <summary>
/// Offene („open-vocabulary") Bildbeschreibung: statt fester Kategorien wird
/// das Bild-Embedding gegen eine große Wortliste verglichen und die am besten
/// passenden Begriffe zurückgegeben. So erscheinen auch Inhalte wie „flower",
/// die keine feste Tag-Kategorie sind – im Gegensatz zum <see cref="ZeroShotTagger"/>.
/// </summary>
public sealed class OpenVocabTagger : IConceptTagger
{
    private readonly (string Word, float[] Emb)[] _concepts;

    /// <summary>Mindest-Ähnlichkeit (Kosinus), damit ein Begriff angezeigt wird.</summary>
    public float MinRelevance { get; set; }

    /// <summary>Wie viele Begriffe die Interface-Variante <see cref="Describe(float[])"/> höchstens liefert.</summary>
    public int TopN { get; set; } = 8;

    public OpenVocabTagger(ClipTextEncoder text, IEnumerable<string> concepts, float minRelevance = 0.2f)
    {
        MinRelevance = minRelevance;
        _concepts = concepts
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(w => (w, text.Embed($"a photo of {w}"))) // Prompt-Vorlage hilft CLIP
            .ToArray();
    }

    /// <summary>Interface-Variante: nur die Begriffe (bis <see cref="TopN"/>).</summary>
    public IReadOnlyList<string> Describe(float[] imageEmbedding)
        => DescribeScored(imageEmbedding, TopN).Select(x => x.Word).ToList();

    /// <summary>Liefert die am besten passenden Begriffe mit Score (absteigend), über der Schwelle.</summary>
    public IReadOnlyList<(string Word, float Score)> DescribeScored(float[] imageEmbedding, int topN = 8)
    {
        return _concepts
            .Select(c => (c.Word, Score: Dot(imageEmbedding, c.Emb)))
            .Where(x => x.Score >= MinRelevance)
            .OrderByDescending(x => x.Score)
            .Take(topN)
            .ToList();
    }

    private static float Dot(float[] a, float[] b)
    {
        float s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }
}
