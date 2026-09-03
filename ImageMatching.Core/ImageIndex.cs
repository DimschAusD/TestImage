using System.Text.Json;

namespace ImageMatching.Core;

/// <summary>Ein gespeicherter Fingerabdruck samt Datei-Metadaten (für den Cache).</summary>
public sealed class IndexEntry
{
    public string Path { get; set; } = "";
    public long FileSize { get; set; }
    public long LastWriteTicks { get; set; }
    public float[] Descriptor { get; set; } = Array.Empty<float>();

    /// <summary>Perceptual Hash (dHash) für schnelle Dubletten-Erkennung.</summary>
    public ulong Hash { get; set; }

    /// <summary>Automatisch vergebene Schlagworte (Kategorie → Wert), sofern getaggt.</summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>Offen erkannte Begriffe (open vocabulary), sofern beschrieben.</summary>
    public List<string> Concepts { get; set; } = new();
}

/// <summary>Ein Suchtreffer: Pfad und Ähnlichkeit zum Query-Bild.</summary>
public sealed class SearchResult
{
    public string Path { get; init; } = "";

    /// <summary>Ähnlichkeit 0..1.</summary>
    public float Similarity { get; init; }

    /// <summary>Ähnlichkeit in Prozent (0..100).</summary>
    public float Percent => Similarity * 100f;

    /// <summary>Schlagworte des Treffers (Kategorie → Wert), sofern vorhanden.</summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();

    /// <summary>Offen erkannte Begriffe des Treffers.</summary>
    public IReadOnlyList<string> Concepts { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Verwaltet die Fingerabdrücke vieler Bilder, speichert sie in einer Cache-
/// Datei und liefert zu einem Query-Bild die ähnlichsten Treffer.
///
/// Das verwendete Verfahren (Kontur oder CLIP) steckt hinter dem austauschbaren
/// <see cref="IImageDescriptor"/>; Index und Abfrage bleiben davon unberührt.
/// Das Laden der Bilddaten wird als Delegat übergeben (<c>Func&lt;string,RgbImage&gt;</c>),
/// damit die Bibliothek unabhängig von einem konkreten Bild-Framework bleibt.
/// </summary>
public sealed class ImageIndex
{
    private readonly Dictionary<string, IndexEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IImageDescriptor _descriptor;
    private readonly IImageTagger? _tagger;
    private readonly IConceptTagger? _conceptTagger;

    /// <summary>Cache-Format-Version. Erhöhen, wenn sich das Format/die Pipeline ändert – alte Caches werden dann verworfen.</summary>
    private const int CacheVersion = 2;

    private static readonly string[] Extensions =
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    /// <param name="descriptor">Das Fingerabdruck-Verfahren (Kontur oder CLIP).</param>
    /// <param name="tagger">Optional: vergibt beim Indexieren Schlagworte (nur mit CLIP sinnvoll).</param>
    /// <param name="conceptTagger">Optional: offene Bildbeschreibung (nur mit CLIP sinnvoll).</param>
    public ImageIndex(IImageDescriptor descriptor, IImageTagger? tagger = null, IConceptTagger? conceptTagger = null)
    {
        _descriptor = descriptor;
        _tagger = tagger;
        _conceptTagger = conceptTagger;
    }

    public int Count => _entries.Count;
    public IReadOnlyCollection<IndexEntry> Entries => _entries.Values;

    /// <summary>
    /// Wie viele Karteileichen der letzte <see cref="IndexFolder"/>-Lauf ausgeräumt hat
    /// — Einträge zu Dateien, die es nicht mehr gibt. Nur zur Rückmeldung.
    /// </summary>
    public int EntfernteEintraege { get; private set; }

    /// <summary>
    /// Wie viele Einträge der letzte <see cref="Load"/> verworfen hat, weil sie zu einem
    /// anderen Ordner gehören — Kennzeichen einer mitkopierten Cache-Datei. Nur zur
    /// Rückmeldung; gefiltert wird ausschliesslich mit <c>nurAusDiesemOrdner</c>.
    /// </summary>
    public int FremdeEintraege { get; private set; }

    /// <summary>Schützt <see cref="_entries"/>, wenn mehrere Bilder gleichzeitig laufen.</summary>
    private readonly object _eintragSperre = new();

    /// <summary>
    /// Indexiert alle Bilder eines Ordners. Bereits bekannte und unveränderte
    /// Bilder (gleiche Größe und Änderungszeit) werden übersprungen, sodass ein
    /// erneuter Lauf nur neue/geänderte Dateien berechnet.
    /// </summary>
    /// <param name="maxParallel">
    /// Wie viele Bilder gleichzeitig verarbeitet werden. Vorgabe 1 – also genau das
    /// bisherige Verhalten, damit bestehende Aufrufer unberührt bleiben.
    ///
    /// Höhere Werte lohnen sich deutlich: Der weitaus grösste Posten ist die
    /// Beschreibung durch das neuronale Netz, und die skaliert über die Kerne. Auf
    /// einem 4-Kerner gemessen: 1,96-facher Durchsatz bei 8 gleichzeitig.
    ///
    /// Bedingung ist ein <see cref="IImageDescriptor"/>, dessen
    /// <c>Describe</c> nebenläufig aufgerufen werden darf. Für den CLIP-Beschreiber
    /// trifft das zu (ONNX Runtime erlaubt gleichzeitige Läufe auf einer Sitzung).
    /// Wer sich dessen bei seiner Umsetzung nicht sicher ist, lässt die Vorgabe stehen.
    /// </param>
    public void IndexFolder(
        string folder,
        Func<string, RgbImage> loader,
        bool recursive = true,
        IProgress<(int done, int total, string file)>? progress = null,
        CancellationToken cancel = default,
        int maxParallel = 1)
    {
        SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(folder, "*.*", option)
            .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        EntfernteEintraege = EntferneVerschwundene();

        int done = 0;

        void Verarbeite(string file)
        {
            if (cancel.IsCancellationRequested)
                return; // Abbruch: bereits Indexiertes bleibt erhalten (später fortsetzbar)

            int fertig = Interlocked.Increment(ref done);
            progress?.Report((fertig, files.Count, file));

            try
            {
                var info = new FileInfo(file);

                lock (_eintragSperre)
                {
                    if (_entries.TryGetValue(file, out IndexEntry? existing)
                        && existing.FileSize == info.Length
                        && existing.LastWriteTicks == info.LastWriteTimeUtc.Ticks)
                    {
                        return; // unverändert -> Embedding aus dem Cache behalten
                    }
                }

                // Laden und Rechnen bewusst ausserhalb der Sperre: Das ist der
                // langsame Teil, und genau der soll gleichzeitig laufen.
                RgbImage img = loader(file);
                var eintrag = new IndexEntry
                {
                    Path = file,
                    FileSize = info.Length,
                    LastWriteTicks = info.LastWriteTimeUtc.Ticks,
                    Descriptor = _descriptor.Describe(img),
                    Hash = PerceptualHash.Compute(img.ToGray()) // Dubletten immer über Kontur/Grau
                };

                lock (_eintragSperre)
                {
                    _entries[file] = eintrag;
                }
            }
            catch
            {
                // Beschädigte oder nicht lesbare Dateien überspringen.
            }
        }

        if (maxParallel <= 1)
        {
            foreach (string file in files)
            {
                if (cancel.IsCancellationRequested)
                    break;

                Verarbeite(file);
            }
        }
        else
        {
            // Der Abbruch wird bewusst nicht an ParallelOptions gegeben: Sonst würde
            // eine Ausnahme fliegen. Stattdessen steigt jeder Durchlauf für sich aus,
            // und das bereits Indexierte bleibt erhalten – wie im Einzelbetrieb.
            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = maxParallel },
                Verarbeite);
        }

        // Tags mit aktuellem Tagger/Schwelle auffrischen – auch für übersprungene,
        // unveränderte Bilder. Nutzt das bereits gespeicherte Embedding, daher
        // ohne erneutes Laden/Einbetten (nur Skalarprodukte, sehr schnell).
        RetagAll();
    }

    /// <summary>
    /// Räumt Einträge aus, deren Datei es nicht mehr gibt — gelöscht, umbenannt oder in
    /// einen anderen Ordner verschoben. Ohne das wüchse der Cache immer weiter und
    /// <see cref="Count"/> nennte eine Zahl, die es so nicht mehr gibt.
    ///
    /// Zwei Vorsichtsmassnahmen:
    /// <list type="bullet">
    /// <item>Geprüft wird gegen die Datei selbst, nicht gegen die eingesammelte
    /// Dateiliste. Ein abgebrochener Lauf räumt deshalb nichts Falsches aus, und
    /// Einträge aus Unterordnern bleiben auch bei einem nicht-rekursiven Lauf stehen.</item>
    /// <item>Liegt der Ordner eines Eintrags gar nicht (mehr) vor — abgezogenes
    /// Wechsellaufwerk, getrennte Netzfreigabe —, bleibt der Eintrag unangetastet.
    /// Sonst löschte ein Lauf ohne verbundenes Laufwerk den halben Index weg.</item>
    /// </list>
    /// </summary>
    /// <returns>Anzahl der entfernten Einträge.</returns>
    private int EntferneVerschwundene()
    {
        var verschwunden = new List<string>();

        lock (_eintragSperre)
        {
            foreach (string pfad in _entries.Keys)
            {
                string? ordner = System.IO.Path.GetDirectoryName(pfad);
                if (string.IsNullOrEmpty(ordner) || !Directory.Exists(ordner))
                    continue;

                if (!File.Exists(pfad))
                    verschwunden.Add(pfad);
            }

            foreach (string pfad in verschwunden)
                _entries.Remove(pfad);
        }

        return verschwunden.Count;
    }

    /// <summary>
    /// Berechnet die Tags aller Einträge aus ihren gespeicherten Embeddings neu.
    /// Wirkt sofort mit der aktuellen Schwelle des Taggers – ohne Bildzugriff.
    /// Ohne Tagger (z. B. Kontur-Verfahren) passiert nichts.
    /// </summary>
    public void RetagAll()
    {
        if (_tagger is null && _conceptTagger is null) return;
        foreach (IndexEntry e in _entries.Values)
        {
            if (_tagger is not null)
                e.Tags = new Dictionary<string, string>(_tagger.Tag(e.Descriptor));
            if (_conceptTagger is not null)
                e.Concepts = _conceptTagger.Describe(e.Descriptor).ToList();
        }
    }

    /// <summary>Alle vorkommenden erkannten Begriffe (sortiert) – für die Filter-Auswahl.</summary>
    public IReadOnlyList<string> ConceptOptions()
        => _entries.Values
            .SelectMany(e => e.Concepts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Alle Bilder, deren erkannte Begriffe <paramref name="concept"/> enthalten.</summary>
    public IReadOnlyList<IndexEntry> FilterByConcept(string concept)
        => _entries.Values
            .Where(e => e.Concepts.Contains(concept, StringComparer.OrdinalIgnoreCase))
            .ToList();

    /// <summary>Liefert die ähnlichsten Bilder zu einem Query-Bild, absteigend sortiert.</summary>
    public IReadOnlyList<SearchResult> Query(RgbImage query, int topN = 30, float minSimilarity = 0f)
    {
        float[] qd = _descriptor.Describe(query);
        return _entries.Values
            .Select(e => new SearchResult
            {
                Path = e.Path,
                Similarity = _descriptor.Similarity(qd, e.Descriptor),
                Tags = e.Tags,
                Concepts = e.Concepts
            })
            .Where(r => r.Similarity >= minSimilarity)
            .OrderByDescending(r => r.Similarity)
            .Take(topN)
            .ToList();
    }

    /// <summary>
    /// Gruppiert fast identische Bilder (Duplikate/Serien) anhand ihres
    /// Perceptual Hash. Zwei Bilder gehören zur selben Gruppe, wenn ihre
    /// Hamming-Distanz &lt;= <paramref name="maxDistance"/> ist (0 = exakt gleich,
    /// bis ~5 = praktisch gleich). Nur Gruppen mit mehr als einem Bild werden
    /// zurückgegeben, die größten zuerst.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<IndexEntry>> FindDuplicateGroups(int maxDistance = 5)
    {
        var list = _entries.Values.ToList();
        int n = list.Count;

        // Union-Find: alle ähnlich genug liegenden Bilder verschmelzen.
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }

        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (PerceptualHash.HammingDistance(list[i].Hash, list[j].Hash) <= maxDistance)
                    parent[Find(i)] = Find(j);

        var groups = new Dictionary<int, List<IndexEntry>>();
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            if (!groups.TryGetValue(root, out List<IndexEntry>? g))
                groups[root] = g = new List<IndexEntry>();
            g.Add(list[i]);
        }

        return groups.Values
            .Where(g => g.Count > 1)
            .OrderByDescending(g => g.Count)
            .ToList();
    }

    /// <summary>
    /// Liefert die ähnlichsten Bilder zu einem fertigen Merkmalsvektor
    /// (z. B. einem CLIP-Text-Embedding für die Freitextsuche). Vergleich über
    /// dieselbe Ähnlichkeitsfunktion des aktiven Deskriptors.
    /// </summary>
    public IReadOnlyList<SearchResult> QueryByVector(float[] queryVector, int topN = 40, float minSimilarity = 0f)
    {
        return _entries.Values
            .Select(e => new SearchResult
            {
                Path = e.Path,
                Similarity = _descriptor.Similarity(queryVector, e.Descriptor),
                Tags = e.Tags,
                Concepts = e.Concepts
            })
            .Where(r => r.Similarity >= minSimilarity)
            .OrderByDescending(r => r.Similarity)
            .Take(topN)
            .ToList();
    }

    /// <summary>
    /// Liefert alle vorkommenden Tag-Werte je Kategorie (Kategorie → sortierte
    /// Werteliste) – zum Befüllen der Filter-Auswahl.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> TagOptions()
    {
        var acc = new Dictionary<string, SortedSet<string>>();
        foreach (IndexEntry e in _entries.Values)
            foreach (KeyValuePair<string, string> kv in e.Tags)
            {
                if (!acc.TryGetValue(kv.Key, out SortedSet<string>? set))
                    acc[kv.Key] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(kv.Value);
            }

        return acc.ToDictionary(
            k => k.Key,
            k => (IReadOnlyList<string>)k.Value.ToList());
    }

    /// <summary>Alle Bilder, deren Tag in <paramref name="category"/> gleich <paramref name="value"/> ist.</summary>
    public IReadOnlyList<IndexEntry> FilterByTag(string category, string value)
        => _entries.Values
            .Where(e => e.Tags.TryGetValue(category, out string? v)
                        && string.Equals(v, value, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public void Save(string path)
    {
        using FileStream fs = File.Create(path);
        JsonSerializer.Serialize(fs, new CacheFile { Version = CacheVersion, Entries = _entries.Values.ToList() });
    }

    /// <param name="nurAusDiesemOrdner">
    /// Verwirft beim Laden alle Einträge, deren Datei nicht unterhalb des Ordners liegt,
    /// in dem die Cache-Datei selbst steht.
    ///
    /// Nötig, weil im Cache absolute Pfade stehen: Wird ein Bilderordner samt Cache-Datei
    /// an eine andere Stelle kopiert, beschreiben deren Einträge weiterhin die Dateien am
    /// Herkunftsort. Ein Indexlauf am neuen Ort legt daneben Einträge mit den neuen Pfaden
    /// an — jedes Bild steht dann zweimal im Index und erscheint in jeder Suche doppelt.
    /// Ausgeräumt wird das nicht: <see cref="EntferneVerschwundene"/> lässt Einträge stehen,
    /// deren Datei am Herkunftsort noch liegt, und rührt einen fehlenden Ordner absichtlich
    /// nicht an.
    ///
    /// Vorgabe <c>false</c>, also unverändertes Verhalten: Wer die Cache-Datei bewusst
    /// ausserhalb der Bilder ablegt, verlöre sonst mit einem Schlag den ganzen Index.
    /// </param>
    public void Load(string path, bool nurAusDiesemOrdner = false)
    {
        _entries.Clear();
        FremdeEintraege = 0;
        if (!File.Exists(path)) return;

        string? wurzel = null;
        if (nurAusDiesemOrdner)
        {
            try
            {
                wurzel = Path.GetDirectoryName(Path.GetFullPath(path));
            }
            catch
            {
                wurzel = null;   // unbrauchbarer Pfad -> lieber gar nicht filtern
            }
        }

        try
        {
            using FileStream fs = File.OpenRead(path);
            CacheFile? cache = JsonSerializer.Deserialize<CacheFile>(fs);
            if (cache is null || cache.Version != CacheVersion)
                return; // veraltetes Format/Version -> Index neu aufbauen

            foreach (IndexEntry e in cache.Entries)
            {
                if (wurzel is not null && !LiegtUnterhalb(e.Path, wurzel))
                {
                    FremdeEintraege++;
                    continue;
                }

                _entries[e.Path] = e;
            }
        }
        catch
        {
            // beschädigter oder alter Cache -> einfach neu aufbauen
        }
    }

    /// <summary>
    /// Liegt die Datei im Ordner <paramref name="wurzel"/> oder darunter? Verglichen wird
    /// über die vollständigen Pfade, damit auch <c>..</c> und Kurzformen greifen.
    /// </summary>
    private static bool LiegtUnterhalb(string datei, string wurzel)
    {
        try
        {
            string voll = Path.GetFullPath(datei);
            string praefix = wurzel.TrimEnd(Path.DirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;

            return voll.StartsWith(praefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private sealed class CacheFile
    {
        public int Version { get; set; }
        public List<IndexEntry> Entries { get; set; } = new();
    }
}
