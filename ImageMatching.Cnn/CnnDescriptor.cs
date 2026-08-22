using ImageMatching.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ImageMatching.Cnn;

/// <summary>
/// CNN-basierter Bild-Fingerabdruck über ein ONNX-Modell (z. B. CLIP-Bild-
/// encoder, MIT-lizenziert). Liefert ein L2-normiertes Embedding; die
/// Ähnlichkeit zweier Embeddings ist die Kosinus-Ähnlichkeit.
///
/// GERÜST MIT PLATZHALTER: Ist keine Modelldatei vorhanden, läuft die Pipeline
/// trotzdem – dann wird ein deterministisches Ersatz-Embedding (grobes Farb-
/// histogramm) erzeugt. So ist alles Ende-zu-Ende testbar; sobald die echte
/// <c>.onnx</c>-Datei eingelegt wird, schaltet die Klasse automatisch auf echte
/// Inferenz um (siehe <see cref="IsPlaceholder"/>).
/// </summary>
public sealed class CnnDescriptor : IImageDescriptor, IDisposable
{
    private readonly InferenceSession? _session;
    private readonly string _inputName;
    private readonly int _size;

    // Standard-Vorverarbeitung für CLIP (ViT-B/32): Normierung je Kanal.
    private static readonly float[] Mean = { 0.48145466f, 0.4578275f, 0.40821073f };
    private static readonly float[] Std = { 0.26862954f, 0.26130258f, 0.27577711f };

    /// <summary>True, solange kein Modell geladen ist (Ersatz-Embedding aktiv).</summary>
    public bool IsPlaceholder => _session is null;

    /// <param name="modelPath">Pfad zur ONNX-Datei. Fehlt sie, läuft der Platzhalter.</param>
    /// <param name="inputSize">Eingabekantenlänge des Netzes (CLIP: 224).</param>
    public CnnDescriptor(string? modelPath = null, int inputSize = 224)
    {
        _size = inputSize;
        if (!string.IsNullOrEmpty(modelPath) && File.Exists(modelPath))
        {
            _session = new InferenceSession(modelPath);
            _inputName = _session.InputMetadata.Keys.First();
        }
        else
        {
            _session = null;
            _inputName = "";
        }
    }

    /// <summary>Berechnet das Embedding eines Farbbildes.</summary>
    public float[] Describe(RgbImage image)
        => _session is null ? Placeholder(image) : Infer(image);

    private float[] Infer(RgbImage image)
    {
        DenseTensor<float> input = Preprocess(image);
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
            _session!.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, input) });

        // Manche CLIP-Exporte liefern mehrere Ausgaben (z. B. Feature-Map +
        // gepooltes Embedding). Wir nehmen die mit dem niedrigsten Rang – das
        // ist der gepoolte Vektor [1, N].
        float[]? embedding = null;
        int bestRank = int.MaxValue;
        foreach (DisposableNamedOnnxValue r in results)
        {
            if (r.Value is DenseTensor<float> t && t.Dimensions.Length < bestRank)
            {
                bestRank = t.Dimensions.Length;
                embedding = t.ToArray();
            }
        }
        embedding ??= results.First().AsEnumerable<float>().ToArray();
        return Normalize(embedding);
    }

    /// <summary>
    /// Skaliert auf die Netzgröße und legt das NCHW-Tensor mit Normierung an.
    ///
    /// Geschrieben wird direkt in den Puffer, nicht über <c>tensor[0, c, y, x]</c>. Der
    /// mehrdimensionale Indexer rechnet bei jedem der 150 528 Zugriffe die Schrittweiten
    /// neu aus; gemessen kostete das 10,5 ms je Bild gegen 0,36 ms hier — Faktor 29.
    ///
    /// NCHW heisst: erst alle Rot-Werte, dann alle Grün-, dann alle Blau-Werte, jede
    /// Ebene zeilenweise. Der Wert für Kanal c an Position p steht also bei
    /// <c>c * Fläche + p</c>. Gegen den Indexer geprüft, alle 150 528 Werte bitgleich.
    ///
    /// Die Rechnung je Wert ist absichtlich Zeichen für Zeichen dieselbe geblieben —
    /// mit Kehrwerten zu multiplizieren wäre schneller, ergäbe aber andere letzte Bits.
    /// </summary>
    private DenseTensor<float> Preprocess(RgbImage image)
    {
        byte[] px = image.ResampleBilinear(_size, _size);
        var tensor = new DenseTensor<float>(new[] { 1, 3, _size, _size });

        Span<float> ziel = tensor.Buffer.Span;
        int flaeche = _size * _size;

        // In lokale Werte holen: spart je Bildpunkt sechs Zugriffe auf die statischen
        // Felder samt Bereichsprüfung. An der Rechnung ändert das nichts.
        float mR = Mean[0], mG = Mean[1], mB = Mean[2];
        float sR = Std[0], sG = Std[1], sB = Std[2];

        for (int p = 0; p < flaeche; p++)
        {
            int i = p * 3;
            ziel[p] = (px[i] / 255f - mR) / sR;                    // R
            ziel[flaeche + p] = (px[i + 1] / 255f - mG) / sG;      // G
            ziel[flaeche * 2 + p] = (px[i + 2] / 255f - mB) / sB;  // B
        }

        return tensor;
    }

    /// <summary>
    /// Ersatz-Embedding ohne Modell: grobes 4×4×4-Farbhistogramm (64 Dim),
    /// L2-normiert. Reicht nicht für echte Wiedererkennung, macht die Pipeline
    /// aber sofort lauffähig und testbar.
    /// </summary>
    private static float[] Placeholder(RgbImage image)
    {
        var hist = new float[64];
        byte[] p = image.Pixels;
        for (int i = 0; i < p.Length; i += 3)
        {
            int r = p[i] >> 6, g = p[i + 1] >> 6, b = p[i + 2] >> 6; // je 0..3
            hist[(r * 4 + g) * 4 + b]++;
        }
        return Normalize(hist);
    }

    /// <summary>Kosinus-Ähnlichkeit 0..1 zweier Embeddings (erwartet L2-normiert).</summary>
    public float Similarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;
        float dot = 0f;
        for (int i = 0; i < a.Length; i++) dot += a[i] * b[i];
        return dot < 0f ? 0f : dot;
    }

    private static float[] Normalize(float[] v)
    {
        double norm = 0;
        for (int i = 0; i < v.Length; i++) norm += v[i] * (double)v[i];
        norm = Math.Sqrt(norm);
        if (norm > 1e-9)
            for (int i = 0; i < v.Length; i++) v[i] = (float)(v[i] / norm);
        return v;
    }

    public void Dispose() => _session?.Dispose();
}
