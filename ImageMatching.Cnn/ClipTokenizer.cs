using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ImageMatching.Cnn;

/// <summary>
/// CLIP-Tokenizer (Byte-Level-BPE) – zerlegt einen Text in die Token-IDs, die
/// der CLIP-Text-Encoder erwartet. Nachbau von OpenAIs simple_tokenizer auf
/// Basis der Dateien <c>vocab.json</c> und <c>merges.txt</c>. Damit bleibt die
/// Text-Seite Python-frei und komplett in der Bibliothek.
/// </summary>
public sealed class ClipTokenizer
{
    private readonly Dictionary<string, int> _encoder;
    private readonly Dictionary<(string, string), int> _bpeRanks;
    private readonly Dictionary<byte, char> _byteEncoder;
    private readonly long _sot;
    private readonly long _eot;

    private static readonly Regex Pattern = new(
        @"<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d|\p{L}+|\p{N}|[^\s\p{L}\p{N}]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ClipTokenizer(string vocabJsonPath, string mergesTxtPath)
    {
        _encoder = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(vocabJsonPath))
                   ?? throw new InvalidOperationException("vocab.json konnte nicht gelesen werden.");

        _bpeRanks = new Dictionary<(string, string), int>();
        int rank = 0;
        foreach (string line in File.ReadLines(mergesTxtPath))
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split(' ');
            if (parts.Length == 2) _bpeRanks[(parts[0], parts[1])] = rank++;
        }

        _byteEncoder = BytesToUnicode();
        _sot = _encoder["<|startoftext|>"];
        _eot = _encoder["<|endoftext|>"];
    }

    /// <summary>Wandelt Text in CLIP-Token-IDs (inkl. Start-/End-Token).</summary>
    public IReadOnlyList<long> Encode(string text)
    {
        var ids = new List<long> { _sot };
        text = WhitespaceClean(text).ToLowerInvariant();

        foreach (Match m in Pattern.Matches(text))
        {
            var sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(m.Value))
                sb.Append(_byteEncoder[b]);

            foreach (string piece in Bpe(sb.ToString()).Split(' '))
                if (_encoder.TryGetValue(piece, out int id))
                    ids.Add(id);
        }

        ids.Add(_eot);
        return ids;
    }

    private string Bpe(string token)
    {
        if (token.Length == 0) return token;

        var word = new List<string>(token.Length);
        foreach (char c in token) word.Add(c.ToString());
        word[^1] += "</w>";

        while (word.Count > 1)
        {
            (string, string)? best = null;
            int bestRank = int.MaxValue;
            for (int i = 0; i < word.Count - 1; i++)
            {
                var pair = (word[i], word[i + 1]);
                if (_bpeRanks.TryGetValue(pair, out int r) && r < bestRank)
                {
                    bestRank = r;
                    best = pair;
                }
            }
            if (best is null) break;

            (string first, string second) = best.Value;
            var merged = new List<string>(word.Count);
            int idx = 0;
            while (idx < word.Count)
            {
                int j = word.IndexOf(first, idx);
                if (j < 0)
                {
                    merged.AddRange(word.GetRange(idx, word.Count - idx));
                    break;
                }
                merged.AddRange(word.GetRange(idx, j - idx));
                idx = j;
                if (idx < word.Count - 1 && word[idx] == first && word[idx + 1] == second)
                {
                    merged.Add(first + second);
                    idx += 2;
                }
                else
                {
                    merged.Add(word[idx]);
                    idx += 1;
                }
            }
            word = merged;
        }

        return string.Join(' ', word);
    }

    private static string WhitespaceClean(string text)
        => Regex.Replace(System.Net.WebUtility.HtmlDecode(text), @"\s+", " ").Trim();

    /// <summary>GPT-2/CLIP-Abbildung Byte → sichtbares Unicode-Zeichen.</summary>
    private static Dictionary<byte, char> BytesToUnicode()
    {
        var bs = new List<int>();
        for (int i = '!'; i <= '~'; i++) bs.Add(i);
        for (int i = '¡'; i <= '¬'; i++) bs.Add(i);
        for (int i = '®'; i <= 'ÿ'; i++) bs.Add(i);

        var cs = new List<int>(bs);
        int n = 0;
        for (int b = 0; b < 256; b++)
            if (!bs.Contains(b)) { bs.Add(b); cs.Add(256 + n); n++; }

        var map = new Dictionary<byte, char>();
        for (int i = 0; i < bs.Count; i++) map[(byte)bs[i]] = (char)cs[i];
        return map;
    }
}
