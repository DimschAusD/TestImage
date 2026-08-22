using ImageMatching.Core;

namespace ImageMatching.Cnn;

/// <summary>Eine Tag-Kategorie mit ihren möglichen Werten (Anzeigename → CLIP-Prompt).</summary>
public sealed class TagCategory
{
    public string Name { get; init; } = "";

    /// <summary>Anzeige-Label → englischer CLIP-Prompt (z. B. "innen" → "a photo taken indoors").</summary>
    public Dictionary<string, string> Labels { get; init; } = new();
}

/// <summary>
/// Gewähltes Tag einer Kategorie. <see cref="Confidence"/> = Softmax-Sicherheit
/// *innerhalb* der Kategorie (welche Option passt am besten). <see cref="Relevance"/>
/// = absolute Ähnlichkeit (Kosinus) zum gewählten Label, also *ob* das Tag
/// überhaupt zutrifft – daran hängt die Konfidenz-Schwelle.
/// </summary>
public readonly record struct TagResult(string Category, string Label, float Confidence, float Relevance);

/// <summary>
/// Zero-Shot-Verschlagwortung: vergleicht ein Bild-Embedding mit den
/// vorberechneten Text-Embeddings vorgegebener Tag-Werte und wählt je Kategorie
/// den bestpassenden. Ohne Training – allein über CLIPs gemeinsamen Bild-Text-Raum.
///
/// Über <c>minRelevance</c> lässt sich eine Schwelle setzen: passt die beste
/// Option einer Kategorie absolut zu schlecht (niedriger Kosinus), wird die
/// Kategorie gar nicht vergeben. So variiert die Tag-Anzahl sinnvoll.
/// </summary>
public sealed class ZeroShotTagger : IImageTagger
{
    private readonly List<(string Category, string Label, float[] Emb)> _labels = new();
    private readonly List<string> _categories = new();

    /// <summary>Mindest-Ähnlichkeit (Kosinus 0..1), damit ein Tag vergeben wird. Zur Laufzeit änderbar.</summary>
    public float MinRelevance { get; set; }

    /// <param name="text">Text-Encoder zum einmaligen Vorberechnen der Label-Vektoren.</param>
    /// <param name="categories">Die Kategorien mit ihren möglichen Werten.</param>
    /// <param name="minRelevance">Mindest-Ähnlichkeit (Kosinus 0..1), damit ein Tag vergeben wird. 0 = alles vergeben.</param>
    public ZeroShotTagger(ClipTextEncoder text, IEnumerable<TagCategory> categories, float minRelevance = 0f)
    {
        MinRelevance = minRelevance;
        foreach (TagCategory cat in categories)
        {
            _categories.Add(cat.Name);
            foreach (KeyValuePair<string, string> kv in cat.Labels)
                _labels.Add((cat.Name, kv.Key, text.Embed(kv.Value)));
        }
    }

    /// <summary>Interface-Variante: je Kategorie das bestpassende Tag (Kategorie → Wert), gefiltert nach Schwelle.</summary>
    public IReadOnlyDictionary<string, string> Tag(float[] imageEmbedding)
        => TagDetailed(imageEmbedding).ToDictionary(t => t.Category, t => t.Label);

    /// <summary>
    /// Liefert je Kategorie das bestpassende Tag mit Konfidenz und Relevanz.
    /// Kategorien unterhalb der Relevanz-Schwelle werden weggelassen.
    /// </summary>
    public IReadOnlyList<TagResult> TagDetailed(float[] imageEmbedding)
    {
        var results = new List<TagResult>(_categories.Count);
        foreach (string cat in _categories)
        {
            var labels = _labels.Where(l => l.Category == cat).ToList();
            var sims = labels.Select(l => Dot(imageEmbedding, l.Emb)).ToArray();

            int best = 0;
            for (int i = 1; i < sims.Length; i++)
                if (sims[i] > sims[best]) best = i;

            float relevance = sims[best]; // absolute Ähnlichkeit zum besten Label
            if (relevance < MinRelevance) continue; // Tag trifft nicht sicher genug zu

            float confidence = Softmax(sims, scale: 100f)[best];
            results.Add(new TagResult(cat, labels[best].Label, confidence, relevance));
        }
        return results;
    }

    private static float Dot(float[] a, float[] b)
    {
        float s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    private static float[] Softmax(float[] x, float scale)
    {
        float max = x.Max();
        var e = new float[x.Length];
        float sum = 0;
        for (int i = 0; i < x.Length; i++) { e[i] = MathF.Exp(scale * (x[i] - max)); sum += e[i]; }
        for (int i = 0; i < x.Length; i++) e[i] /= sum;
        return e;
    }
}
