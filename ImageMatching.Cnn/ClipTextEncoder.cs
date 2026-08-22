using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ImageMatching.Cnn;

/// <summary>
/// CLIP-Text-Encoder: wandelt einen Text (Suchbegriff oder Tag) in ein
/// 512-dim Embedding im selben Raum wie die Bild-Embeddings des
/// <see cref="CnnDescriptor"/>. Damit lassen sich Bild und Sprache direkt
/// per Kosinus vergleichen (Zero-Shot-Tags, Freitextsuche).
/// </summary>
public sealed class ClipTextEncoder : IDisposable
{
    private readonly InferenceSession _session;
    private readonly ClipTokenizer _tokenizer;

    public ClipTextEncoder(string modelPath, string vocabJsonPath, string mergesTxtPath)
    {
        _session = new InferenceSession(modelPath);
        _tokenizer = new ClipTokenizer(vocabJsonPath, mergesTxtPath);
    }

    /// <summary>Berechnet das L2-normierte Text-Embedding.</summary>
    public float[] Embed(string text)
    {
        IReadOnlyList<long> ids = _tokenizer.Encode(text);
        int len = ids.Count;

        var idTensor = new DenseTensor<long>(new[] { 1, len });
        var maskTensor = new DenseTensor<long>(new[] { 1, len });
        for (int i = 0; i < len; i++)
        {
            idTensor[0, i] = ids[i];
            maskTensor[0, i] = 1;
        }

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("input_ids", idTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor)
        });

        DisposableNamedOnnxValue emb = results.FirstOrDefault(r => r.Name == "text_embeds") ?? results.First();
        float[] vec = emb.AsEnumerable<float>().ToArray();

        double norm = 0;
        foreach (float v in vec) norm += v * (double)v;
        norm = Math.Sqrt(norm);
        if (norm > 1e-9)
            for (int i = 0; i < vec.Length; i++) vec[i] = (float)(vec[i] / norm);
        return vec;
    }

    public void Dispose() => _session.Dispose();
}
