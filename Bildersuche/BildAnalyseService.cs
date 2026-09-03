using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageMatching.Cnn;
using ImageMatching.Core;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Kapselt die CLIP-Analyse eines einzelnen Bildes: lädt das Vision-Modell
    /// und den Text-Encoder faul (einmalig, im Hintergrund) und liefert zu einem
    /// Bild die am besten passenden Begriffe („open vocabulary").
    /// </summary>
    public sealed class BildAnalyseService : IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly GermanQueryTranslator _uebersetzer = new();

        /// <summary>
        /// Wörter der letzten Suchanfrage, für die es keine Übersetzung gab.
        ///
        /// Sie gehen unübersetzt in den englischen Text-Encoder und tragen dort nichts
        /// bei. Die Oberfläche nennt sie in der Statuszeile — sonst sieht ein leeres
        /// Ergebnis aus wie „das Bild gibt es nicht", obwohl in Wahrheit das Wort fehlte.
        /// </summary>
        public IReadOnlyList<string> LetzteNichtUebersetzt { get; private set; } = Array.Empty<string>();
        private CnnDescriptor? _cnn;
        private ClipTextEncoder? _text;
        private OpenVocabTagger? _tagger;
        private ZeroShotTagger? _zeroShot;
        private bool _versucht;

        /// <summary>True, sobald Vision-Modell + Text-Encoder + Tagger bereitstehen.</summary>
        public bool Bereit => _cnn is { IsPlaceholder: false } && _tagger is not null;

        /// <summary>
        /// Lädt die Modelle einmalig. Gibt false zurück, wenn keine echten
        /// Modelldateien gefunden wurden (dann bleibt der Dienst ohne Funktion).
        /// </summary>
        public async Task<bool> StelleSicherGeladenAsync()
        {
            if (Bereit) return true;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Bereit) return true;
                if (_versucht && _tagger is null) return false; // schon einmal erfolglos versucht
                _versucht = true;

                await Task.Run(() =>
                {
                    string? vision = FindeModell("clip-vit-b32-vision.onnx");
                    (string model, string vocab, string merges)? text = FindeTextDateien();
                    if (vision is null || text is null) return;

                    _cnn = new CnnDescriptor(vision);
                    if (_cnn.IsPlaceholder) return; // Modell nicht ladbar

                    _text = new ClipTextEncoder(text.Value.model, text.Value.vocab, text.Value.merges);
                    _zeroShot = new ZeroShotTagger(_text, BuildCategories(), minRelevance: 0.20f);
                    _tagger = new OpenVocabTagger(_text, Concepts, minRelevance: 0.20f);
                }).ConfigureAwait(false);

                return Bereit;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Erkennt die passendsten Begriffe zu einer Bilddatei. Liefert eine leere
        /// Liste, wenn die Modelle fehlen oder nichts über der Schwelle liegt.
        /// </summary>
        public async Task<IReadOnlyList<(string Word, float Score)>> ErkenneAsync(
            string bildPfad, float minRelevance, int topN = 12)
        {
            if (!await StelleSicherGeladenAsync().ConfigureAwait(false)) return Array.Empty<(string, float)>();
            if (string.IsNullOrEmpty(bildPfad) || !File.Exists(bildPfad)) return Array.Empty<(string, float)>();

            float threshold = minRelevance;
            return await Task.Run(() =>
            {
                var rgb = WpfImaging.LoadRgb(bildPfad);
                float[] emb = _cnn!.Describe(rgb);
                _tagger!.MinRelevance = threshold;
                return _tagger.DescribeScored(emb, topN);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Berechnet eine Heatmap (Ähnlichkeits-Grid) für einen Begriff auf einem Bild.
        /// Das Bild wird in gridSize×gridSize Kacheln aufgeteilt, jede einzeln durch CLIP
        /// geschickt und mit dem Text-Embedding des Begriffs verglichen.
        /// Rückgabe: float[row,col] mit Werten 0..1.
        /// </summary>
        public async Task<float[,]?> HeatmapAsync(string bildPfad, string begriffEnglisch, int gridSize = 4)
        {
            if (!await StelleSicherGeladenAsync().ConfigureAwait(false)) return null;
            if (_text is null) return null;
            if (string.IsNullOrEmpty(bildPfad) || !File.Exists(bildPfad)) return null;

            return await Task.Run(() =>
            {
                var rgb = WpfImaging.LoadRgb(bildPfad);
                float[] textVec = _text.Embed($"a photo of a {begriffEnglisch}");
                var scores = new float[gridSize, gridSize];

                int cropW = rgb.Width / gridSize;
                int cropH = rgb.Height / gridSize;
                if (cropW < 4 || cropH < 4) return null;

                for (int row = 0; row < gridSize; row++)
                {
                    for (int col = 0; col < gridSize; col++)
                    {
                        int x0 = col * cropW;
                        int y0 = row * cropH;
                        var cropPixels = new byte[cropW * cropH * 3];
                        for (int y = 0; y < cropH; y++)
                        {
                            int srcOff = ((y0 + y) * rgb.Width + x0) * 3;
                            int dstOff = y * cropW * 3;
                            Array.Copy(rgb.Pixels, srcOff, cropPixels, dstOff, cropW * 3);
                        }
                        var crop = new RgbImage(cropW, cropH, cropPixels);
                        float[] emb = _cnn!.Describe(crop);
                        scores[row, col] = _cnn.Similarity(emb, textVec);
                    }
                }
                return scores;
            }).ConfigureAwait(false);
        }

        /// <summary>Name der Cache-Datei je Ordner (CLIP-Embeddings).</summary>
        public const string CacheDateiName = ".bildindex.clip.json";

        /// <summary>
        /// Freitextsuche im Index eines Ordners: deutsche Anfrage → Englisch →
        /// CLIP-Text-Embedding → Vergleich mit den Bild-Embeddings des Ordners.
        /// Liefert die ähnlichsten Pfade mit Score (leer, wenn nicht indexiert).
        /// </summary>
        public async Task<IReadOnlyList<(string Path, float Score)>> SucheAsync(
            string ordner, string frageDeutsch, int topN = 40, float minSim = 0f)
        {
            if (!await StelleSicherGeladenAsync().ConfigureAwait(false))
                return Array.Empty<(string, float)>();
            if (_text is null || string.IsNullOrWhiteSpace(frageDeutsch) ||
                string.IsNullOrEmpty(ordner) || !Directory.Exists(ordner))
                return Array.Empty<(string, float)>();

            return await Task.Run<IReadOnlyList<(string Path, float Score)>>(() =>
            {
                string englisch = _uebersetzer.Translate(frageDeutsch, out var unbekannt);
                LetzteNichtUebersetzt = unbekannt;

                int wordCount = englisch.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                string clipQuery = wordCount == 1 ? $"a photo of a {englisch}"
                                 : wordCount <= 3 ? $"a photo of {englisch}"
                                 : englisch;
                float[] vec = _text.Embed(clipQuery);

                var index = new ImageIndex(_cnn!);
                index.Load(Path.Combine(ordner, CacheDateiName), nurAusDiesemOrdner: true);
                if (index.Count == 0) return Array.Empty<(string, float)>();

                return index.QueryByVector(vec, topN, minSim)
                    .Select(r => (r.Path, r.Similarity))
                    .ToList();
            }).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<string>> SucheNachFilterAsync(string ordner, string kategorie, string wert)
        {
            if (!await StelleSicherGeladenAsync().ConfigureAwait(false)) return Array.Empty<string>();
            if (string.IsNullOrEmpty(ordner) || !Directory.Exists(ordner))
                return Array.Empty<string>();

            return await Task.Run(() =>
            {
                var index = new ImageIndex(_cnn!);
                index.Load(Path.Combine(ordner, CacheDateiName), nurAusDiesemOrdner: true);
                if (index.Count == 0) return Array.Empty<string>();

                var treffer = string.Equals(kategorie, "Erkannt", StringComparison.OrdinalIgnoreCase)
                    ? index.FilterByConcept(wert)
                    : index.FilterByTag(kategorie, wert);

                return treffer.Select(e => e.Path).ToArray();
            }).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<string>> SucheNachKonzeptAsync(string ordner, string konzeptEnglisch)
        {
            if (!await StelleSicherGeladenAsync().ConfigureAwait(false)) return Array.Empty<string>();
            if (string.IsNullOrEmpty(ordner) || !Directory.Exists(ordner))
                return Array.Empty<string>();

            return await Task.Run(() =>
            {
                var index = new ImageIndex(_cnn!);
                index.Load(Path.Combine(ordner, CacheDateiName), nurAusDiesemOrdner: true);
                if (index.Count == 0) return Array.Empty<string>();

                return index.FilterByConcept(konzeptEnglisch)
                    .Select(e => e.Path)
                    .ToArray();
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Anker-Schwelle der erweiterten Seriensuche: ein Kettenglied muss zusätzlich
        /// zum direkten Vorgänger (≥ minSim) auch dem Startbild noch ≥ dieser Schwelle
        /// ähneln. Verhindert, dass die Kette wegdriftet und Fremdbilder einsammelt.
        /// </summary>
        private const float AnkerSchwelle = 0.70f;

        /// <summary>
        /// Erweiterte Seriensuche (Kettensuche): startet beim ausgewählten Bild
        /// und folgt transitiv allen Nachbarn mit ≥ minSim. Bild A→B→C→D bilden
        /// eine Kette, auch wenn A und D nichts gemeinsam haben – solange jedes Glied
        /// dem Startbild noch mindestens <see cref="AnkerSchwelle"/> ähnelt.
        /// </summary>
        public async Task<IReadOnlyList<(string Path, float Score)>> SucheNachErweiterterSerieAsync(
            string ordner, string bildPfad, float minSim = 0.85f,
            IProgress<(int Prozent, int RestSekunden)>? fortschritt = null,
            CancellationToken abbruch = default)
        {
            if (!await StelleSicherGeladenAsync().ConfigureAwait(false))
                return Array.Empty<(string, float)>();
            if (string.IsNullOrEmpty(ordner) || !Directory.Exists(ordner))
                return Array.Empty<(string, float)>();

            return await Task.Run<IReadOnlyList<(string Path, float Score)>>(() =>
            {
                var index = new ImageIndex(_cnn!);
                index.Load(Path.Combine(ordner, CacheDateiName), nurAusDiesemOrdner: true);
                if (index.Count == 0) return Array.Empty<(string, float)>();

                abbruch.ThrowIfCancellationRequested();

                var lookup = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in index.Entries)
                    lookup[e.Path] = e;

                if (!lookup.TryGetValue(bildPfad, out var start) || start.Descriptor.Length == 0)
                    return Array.Empty<(string, float)>();

                var besucht = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { bildPfad };
                var queue = new Queue<IndexEntry>();
                queue.Enqueue(start);
                var ergebnis = new List<(string Path, float Score)> { (bildPfad, 1f) };

                int gesamt = lookup.Count;
                long vergleiche = 0;          // gemachte Ähnlichkeits-Vergleiche
                long letzteMeldung = 0;
                var uhr = System.Diagnostics.Stopwatch.StartNew();

                while (queue.Count > 0)
                {
                    abbruch.ThrowIfCancellationRequested();
                    var aktuell = queue.Dequeue();
                    foreach (var kandidat in lookup.Values)
                    {
                        if (besucht.Contains(kandidat.Path))
                            continue;
                        float sim = _cnn!.Similarity(aktuell.Descriptor, kandidat.Descriptor);
                        vergleiche++;
                        if (sim >= minSim)
                        {
                            // Anker: muss auch dem Startbild noch ähneln, sonst driftet
                            // die Kette weg und sammelt Fremdbilder ein.
                            float simZumStart = _cnn.Similarity(start.Descriptor, kandidat.Descriptor);
                            if (simZumStart >= AnkerSchwelle)
                            {
                                besucht.Add(kandidat.Path);
                                ergebnis.Add((kandidat.Path, sim));
                                queue.Enqueue(kandidat);
                            }
                        }

                        // ~alle 2000 Vergleiche Fortschritt + Restzeit hochrechnen.
                        // Schätzung: jedes gefundene Bild wird noch gegen alle geprüft,
                        // also geschätzte Gesamt-Vergleiche = gefundene × alle Bilder.
                        if (fortschritt != null && vergleiche - letzteMeldung >= 2000)
                        {
                            letzteMeldung = vergleiche;
                            long geschaetzt = (long)besucht.Count * gesamt;
                            if (geschaetzt < vergleiche) geschaetzt = vergleiche;

                            int prozent = (int)(100.0 * vergleiche / geschaetzt);
                            if (prozent > 99) prozent = 99;   // 100 erst wenn fertig

                            double proSek = vergleiche / Math.Max(uhr.Elapsed.TotalSeconds, 0.001);
                            int restSek = (int)Math.Ceiling((geschaetzt - vergleiche) / Math.Max(proSek, 1));

                            fortschritt.Report((prozent, restSek));
                        }
                    }
                }

                fortschritt?.Report((100, 0));
                return ergebnis;
            }, abbruch).ConfigureAwait(false);
        }

        /// <summary>
        /// Seriensuche: holt das gespeicherte CLIP-Embedding des Bildes aus dem
        /// Index und sucht alle visuell ähnlichen Bilder im selben Ordner.
        /// </summary>
        /// <param name="kalibrierKomponenten">
        /// −1 = aus (roher CLIP-Kosinus wie bisher). Ab 0 werden die Embeddings vor dem
        /// Vergleich zentriert und so viele Hauptkomponenten herausprojiziert
        /// (siehe <see cref="EmbeddingKalibrierung"/>). Die Ähnlichkeitsskala ist dann
        /// eine andere — <paramref name="minSim"/> muss dafür passend gewählt sein.
        /// </param>
        public async Task<IReadOnlyList<(string Path, float Score)>> SucheNachSerieAsync(
            string ordner, string bildPfad, int topN = 80, float minSim = 0.85f,
            CancellationToken abbruch = default, int kalibrierKomponenten = -1)
        {
            if (!await StelleSicherGeladenAsync().ConfigureAwait(false))
                return Array.Empty<(string, float)>();
            if (string.IsNullOrEmpty(ordner) || !Directory.Exists(ordner))
                return Array.Empty<(string, float)>();

            return await Task.Run<IReadOnlyList<(string Path, float Score)>>(() =>
            {
                var index = new ImageIndex(_cnn!);
                index.Load(Path.Combine(ordner, CacheDateiName), nurAusDiesemOrdner: true);
                if (index.Count == 0) return Array.Empty<(string, float)>();

                abbruch.ThrowIfCancellationRequested();

                var entry = index.Entries
                    .FirstOrDefault(e => string.Equals(e.Path, bildPfad, StringComparison.OrdinalIgnoreCase));
                if (entry is null || entry.Descriptor.Length == 0)
                    return Array.Empty<(string, float)>();

                var treffer = new List<(string Path, float Score)>();

                // Kalibrierter Weg: Mittelvektor und stärkste Hauptkomponenten aus dem
                // gesamten Ordner-Index gewinnen, dann darauf vergleichen.
                EmbeddingKalibrierung? kalibrierung = null;
                if (kalibrierKomponenten >= 0)
                {
                    var gueltige = index.Entries
                        .Where(e => e.Descriptor.Length > 0)
                        .Select(e => e.Descriptor)
                        .ToList();

                    kalibrierung = EmbeddingKalibrierung.Erstelle(gueltige, kalibrierKomponenten, abbruch);
                }

                if (kalibrierung is not null)
                {
                    var frageVektor = kalibrierung.Anwenden(entry.Descriptor);

                    foreach (var k in index.Entries)
                    {
                        abbruch.ThrowIfCancellationRequested();
                        if (k.Descriptor.Length == 0) continue;

                        float sim = EmbeddingKalibrierung.Aehnlichkeit(
                            frageVektor, kalibrierung.Anwenden(k.Descriptor));

                        if (sim >= minSim)
                            treffer.Add((k.Path, sim));
                    }
                }
                else
                {
                    foreach (var k in index.Entries)
                    {
                        abbruch.ThrowIfCancellationRequested();
                        if (k.Descriptor.Length == 0) continue;
                        float sim = _cnn!.Similarity(entry.Descriptor, k.Descriptor);
                        if (sim >= minSim)
                            treffer.Add((k.Path, sim));
                    }
                }

                treffer.Sort((a, b) => b.Score.CompareTo(a.Score));
                if (treffer.Count > topN)
                    treffer.RemoveRange(topN, treffer.Count - topN);

                return treffer;
            }, abbruch).ConfigureAwait(false);
        }

        /// <summary>
        /// Wie <see cref="SucheNachSerieAsync"/>, aber über mehrere Ordner.
        ///
        /// Das Anfragebild muss im Index **seines eigenen** Ordners stehen — von dort
        /// kommt die Beschreibung. Verglichen wird sie danach gegen die Indexe aller
        /// angegebenen Ordner.
        ///
        /// <b>Ohne Kalibrierung.</b> Sie gewinnt Mittelvektor und Hauptkomponenten aus
        /// einem Ordner-Index; über mehrere Ordner hinweg wäre nicht definiert, welcher
        /// Bestand die Bezugsgrösse liefert — ein gemeinsamer Bezug über alle Ordner
        /// wäre etwas anderes als der ordnerweise, und die eingemessenen Schwellen
        /// gälten nicht mehr. Deshalb hier bewusst der schlichte Kosinus-Vergleich.
        /// </summary>
        /// <param name="ordner">Zu durchsuchende Ordner. Fehlende werden übersprungen.</param>
        /// <param name="fortschritt">Meldet (fertige Ordner, Gesamtzahl).</param>
        public async Task<IReadOnlyList<(string Path, float Score)>> SucheNachSerieInOrdnernAsync(
            IReadOnlyList<string> ordner, string bildPfad, int topN = 200, float minSim = 0.5f,
            IProgress<(int Fertig, int Gesamt)>? fortschritt = null,
            CancellationToken abbruch = default)
        {
            if (!await StelleSicherGeladenAsync().ConfigureAwait(false))
                return Array.Empty<(string, float)>();
            if (ordner is null || ordner.Count == 0 || string.IsNullOrEmpty(bildPfad))
                return Array.Empty<(string, float)>();

            string? heimatOrdner = Path.GetDirectoryName(bildPfad);
            if (string.IsNullOrEmpty(heimatOrdner))
                return Array.Empty<(string, float)>();

            return await Task.Run<IReadOnlyList<(string Path, float Score)>>(() =>
            {
                // 1) Beschreibung des Anfragebildes aus seinem eigenen Ordner holen.
                var heimat = new ImageIndex(_cnn!);
                heimat.Load(Path.Combine(heimatOrdner, CacheDateiName), nurAusDiesemOrdner: true);

                var frage = heimat.Entries.FirstOrDefault(
                    e => string.Equals(e.Path, bildPfad, StringComparison.OrdinalIgnoreCase));

                if (frage is null || frage.Descriptor.Length == 0)
                    return Array.Empty<(string, float)>();

                // 2) Gegen jeden Ordner vergleichen.
                var treffer = new List<(string Path, float Score)>();
                int fertig = 0;

                foreach (string o in ordner)
                {
                    abbruch.ThrowIfCancellationRequested();

                    string cache = Path.Combine(o, CacheDateiName);
                    if (Directory.Exists(o) && File.Exists(cache))
                    {
                        var index = new ImageIndex(_cnn!);
                        index.Load(cache, nurAusDiesemOrdner: true);

                        foreach (var k in index.Entries)
                        {
                            if (k.Descriptor.Length == 0) continue;

                            float sim = _cnn!.Similarity(frage.Descriptor, k.Descriptor);
                            if (sim >= minSim)
                                treffer.Add((k.Path, sim));
                        }
                    }

                    fortschritt?.Report((++fertig, ordner.Count));
                }

                // Derselbe Pfad kann in zwei Indexen stehen – etwa wenn ein Ordner und
                // sein Unterordner beide indexiert sind. Der bessere Wert gewinnt.
                var beste = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                foreach (var (pfad, wert) in treffer)
                {
                    if (!beste.TryGetValue(pfad, out float alt) || wert > alt)
                        beste[pfad] = wert;
                }

                var ergebnis = beste.Select(p => (Path: p.Key, Score: p.Value)).ToList();
                ergebnis.Sort((a, b) => b.Score.CompareTo(a.Score));

                if (ergebnis.Count > topN)
                    ergebnis.RemoveRange(topN, ergebnis.Count - topN);

                return ergebnis;
            }, abbruch).ConfigureAwait(false);
        }

        /// <summary>
        /// Schwelle, ab der zwei Bilder als Dublette (praktisch identisch) gelten.
        /// Echte Duplikate/Neuspeicherungen liegen bei ≈ 0,98–1,00; bloße Varianten
        /// deutlich darunter (≈ 0,85–0,90) und fallen so bewusst nicht mit hinein.
        /// </summary>
        private const float DublettenSchwelle = 0.98f;

        /// <summary>
        /// Findet Dubletten-Gruppen im Ordner: Bilder, deren CLIP-Embeddings sich
        /// mit ≥ <see cref="DublettenSchwelle"/> ähneln, werden per Union-Find zu
        /// Gruppen zusammengefasst (A≈B, B≈C ⇒ eine Gruppe). Nur Gruppen ab zwei
        /// Bildern kommen zurück, größte zuerst; je Bild die Ähnlichkeit zum ersten
        /// Bild der Gruppe (Repräsentant), dieses selbst mit 1,0.
        /// Meldet Fortschritt + geschätzte Restzeit und ist abbrechbar.
        /// </summary>
        public async Task<IReadOnlyList<IReadOnlyList<(string Path, float Score)>>> FindeDublettenAsync(
            string ordner,
            IProgress<(int Prozent, int RestSekunden)>? fortschritt = null,
            CancellationToken abbruch = default)
        {
            if (!await StelleSicherGeladenAsync().ConfigureAwait(false))
                return Array.Empty<IReadOnlyList<(string, float)>>();
            if (string.IsNullOrEmpty(ordner) || !Directory.Exists(ordner))
                return Array.Empty<IReadOnlyList<(string, float)>>();

            return await Task.Run<IReadOnlyList<IReadOnlyList<(string Path, float Score)>>>(() =>
            {
                var index = new ImageIndex(_cnn!);
                index.Load(Path.Combine(ordner, CacheDateiName), nurAusDiesemOrdner: true);
                if (index.Count == 0) return Array.Empty<IReadOnlyList<(string, float)>>();

                abbruch.ThrowIfCancellationRequested();

                // Nur Einträge mit gültigem Descriptor.
                var eintraege = index.Entries.Where(e => e.Descriptor.Length > 0).ToList();
                int n = eintraege.Count;
                if (n < 2) return Array.Empty<IReadOnlyList<(string, float)>>();

                // Union-Find über den „quasi-identisch"-Graphen.
                var parent = new int[n];
                for (int i = 0; i < n; i++) parent[i] = i;
                int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra != rb) parent[ra] = rb; }

                long gesamtVergleiche = (long)n * (n - 1) / 2;
                long vergleiche = 0, letzteMeldung = 0;
                var uhr = System.Diagnostics.Stopwatch.StartNew();

                for (int i = 0; i < n; i++)
                {
                    abbruch.ThrowIfCancellationRequested();
                    for (int j = i + 1; j < n; j++)
                    {
                        float sim = _cnn!.Similarity(eintraege[i].Descriptor, eintraege[j].Descriptor);
                        vergleiche++;
                        if (sim >= DublettenSchwelle) Union(i, j);

                        // ~alle 5000 Vergleiche Fortschritt + Restzeit hochrechnen.
                        if (fortschritt != null && vergleiche - letzteMeldung >= 5000)
                        {
                            letzteMeldung = vergleiche;
                            int prozent = (int)(100.0 * vergleiche / gesamtVergleiche);
                            if (prozent > 99) prozent = 99;   // 100 erst wenn fertig
                            double proSek = vergleiche / Math.Max(uhr.Elapsed.TotalSeconds, 0.001);
                            int restSek = (int)Math.Ceiling((gesamtVergleiche - vergleiche) / Math.Max(proSek, 1));
                            fortschritt.Report((prozent, restSek));
                        }
                    }
                }

                // Gruppen einsammeln (Wurzel → Mitglieder-Indizes).
                var gruppenMap = new Dictionary<int, List<int>>();
                for (int i = 0; i < n; i++)
                {
                    int r = Find(i);
                    if (!gruppenMap.TryGetValue(r, out var mitglieder)) { mitglieder = new(); gruppenMap[r] = mitglieder; }
                    mitglieder.Add(i);
                }

                var gruppen = new List<IReadOnlyList<(string Path, float Score)>>();
                foreach (var mitglieder in gruppenMap.Values)
                {
                    if (mitglieder.Count < 2) continue;   // Singletons sind keine Dubletten
                    var repr = eintraege[mitglieder[0]];
                    var eintraegeGruppe = mitglieder
                        .Select(idx => (Path: eintraege[idx].Path,
                                        Score: idx == mitglieder[0]
                                            ? 1f
                                            : _cnn!.Similarity(repr.Descriptor, eintraege[idx].Descriptor)))
                        .OrderByDescending(t => t.Score)
                        .ToList();
                    gruppen.Add(eintraegeGruppe);
                }

                // Größte Gruppen zuerst.
                gruppen.Sort((a, b) => b.Count.CompareTo(a.Count));
                fortschritt?.Report((100, 0));
                return gruppen;
            }, abbruch).ConfigureAwait(false);
        }

        /// <summary>
        /// Indexiert nur die Bilder direkt im angegebenen Ordner (nicht rekursiv),
        /// berechnet die CLIP-Embeddings und speichert die JSON-Cache-Datei im
        /// selben Ordner. Meldet den Fortschritt (fertig/gesamt/Datei).
        /// Rückgabe: Anzahl der Bilder im Index (0, falls Modelle fehlen).
        /// </summary>
        public async Task<int> IndexiereOrdnerAsync(
            string ordner,
            IProgress<(int done, int total, string file)>? progress = null,
            CancellationToken cancel = default)
        {
            if (!await StelleSicherGeladenAsync().ConfigureAwait(false)) return 0;
            if (string.IsNullOrEmpty(ordner) || !Directory.Exists(ordner)) return 0;

            return await Task.Run(() =>
            {
                // Messung: Wie viel Zeit geht ins Laden von der Platte, wie viel ins
                // Rechnen? Der Ladevorgang läuft ohnehin über diese Rückruffunktion,
                // deshalb genügt es, sie zu umschliessen – ImageIndex im Grundprojekt
                // bleibt dafür unangetastet.
                var gesamtUhr = System.Diagnostics.Stopwatch.StartNew();
                long ladeTicks = 0;
                int verarbeitet = 0;

                // Mehrere Bilder gleichzeitig. Der grösste Posten ist die Beschreibung
                // durch das neuronale Netz, und die skaliert über die Kerne – auf einem
                // 4-Kerner gemessen fast der doppelte Durchsatz, auf einer Maschine mit
                // 16 Kernen entsprechend mehr.
                //
                // Die Voreinstellungen von ONNX Runtime bleiben dabei unangetastet: Die
                // Messung hat gezeigt, dass ein Begrenzen der Threads je Einzelinferenz
                // das Ergebnis durchweg verschlechtert.
                int grad = Math.Max(1, Environment.ProcessorCount);

                var index = new ImageIndex(_cnn!, tagger: _zeroShot, conceptTagger: _tagger);
                string cache = Path.Combine(ordner, CacheDateiName);
                index.Load(cache, nurAusDiesemOrdner: true);

                // Direkt nach dem Laden ablesen: Ein Indexlauf ändert diesen Wert nicht mehr.
                LetzteFremdeEintraege = index.FremdeEintraege;

                index.IndexFolder(ordner, p =>
                {
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    try
                    {
                        return WpfImaging.LoadRgb(p);
                    }
                    finally
                    {
                        // Interlocked statt Stopwatch: Der Lader läuft jetzt auf mehreren
                        // Fäden, eine gemeinsame Stoppuhr wäre dabei nicht verlässlich.
                        System.Threading.Interlocked.Add(
                            ref ladeTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
                        System.Threading.Interlocked.Increment(ref verarbeitet);
                    }
                }, recursive: false, progress: progress, cancel: cancel, maxParallel: grad);

                index.Save(cache);

                gesamtUhr.Stop();
                LetzteIndexDauer = gesamtUhr.Elapsed;
                LetzteLadeDauer = TimeSpan.FromSeconds(
                    (double)ladeTicks / System.Diagnostics.Stopwatch.Frequency);
                LetzteVerarbeiteteBilder = verarbeitet;
                LetzteParallelitaet = grad;
                LetzteAufgeraeumteEintraege = index.EntfernteEintraege;

                return index.Count;
            }, cancel).ConfigureAwait(false);
        }

        #region Messwerte des letzten Indexlaufs

        /// <summary>
        /// Gesamtdauer des letzten Indexlaufs. Nur zur Beurteilung, wo die Zeit hingeht —
        /// die Werte steuern nichts.
        /// </summary>
        public TimeSpan LetzteIndexDauer { get; private set; }

        /// <summary>
        /// Im Laden und Dekodieren der Bilddateien verbracht — <b>aufsummiert über alle
        /// Fäden</b>. Läuft mehr als ein Bild gleichzeitig, kann dieser Wert grösser sein
        /// als <see cref="LetzteIndexDauer"/>; er ist dann keine Wanduhrzeit mehr.
        /// </summary>
        public TimeSpan LetzteLadeDauer { get; private set; }

        /// <summary>Wie viele Bilder beim letzten Lauf gleichzeitig verarbeitet wurden.</summary>
        public int LetzteParallelitaet { get; private set; }

        /// <summary>
        /// Beim letzten Lauf ausgeräumte Karteileichen — Einträge zu Dateien, die
        /// gelöscht oder weggeschoben wurden. Steuert nichts, wird nur gemeldet.
        /// </summary>
        public int LetzteAufgeraeumteEintraege { get; private set; }

        /// <summary>
        /// Beim letzten Lauf verworfene Fremdeinträge — Einträge, die zu einem anderen
        /// Ordner gehören. So etwas steckt in einer mitkopierten Indexdatei; ohne das
        /// Aussortieren stünde jedes Bild zweimal im Index (alter und neuer Pfad) und
        /// erschiene in jeder Suche doppelt.
        /// </summary>
        public int LetzteFremdeEintraege { get; private set; }

        /// <summary>
        /// Tatsächlich neu verarbeitete Bilder. Unveränderte Dateien überspringt der
        /// Index; ohne diese Zahl wären die Zeiten nicht einzuordnen.
        /// </summary>
        public int LetzteVerarbeiteteBilder { get; private set; }

        #endregion

        /// <summary>
        /// Liest den Index eines Ordners und liefert die Filter-Optionen:
        /// Kategorie → sortierte Werteliste. Concepts kommen als Pseudo-Kategorie "Erkannt".
        /// </summary>
        public Dictionary<string, IReadOnlyList<string>> LadeFilterOptionen(string ordner)
        {
            string cache = Path.Combine(ordner, CacheDateiName);
            if (!File.Exists(cache)) return new();

            var index = new ImageIndex(_cnn ?? new CnnDescriptor());
            index.Load(cache, nurAusDiesemOrdner: true);
            var opts = new Dictionary<string, IReadOnlyList<string>>(index.TagOptions());
            var concepts = index.ConceptOptions();
            if (concepts.Count > 0) opts["Erkannt"] = concepts;
            return opts;
        }

        private static IEnumerable<TagCategory> BuildCategories() => new[]
        {
            new TagCategory { Name = "Ort", Labels = new()
                { ["innen"] = "a photo taken indoors", ["außen"] = "a photo taken outdoors" } },
            new TagCategory { Name = "Haarfarbe", Labels = new()
                { ["schwarz"] = "a person with black hair", ["blond"] = "a person with blonde hair",
                  ["braun"] = "a person with brown hair", ["rot"] = "a person with red hair" } },
            new TagCategory { Name = "Personen", Labels = new()
                { ["keine"] = "a photo with no people", ["eine"] = "a photo of a single person", ["mehrere"] = "a photo of a group of people" } },
            new TagCategory { Name = "Ausschnitt", Labels = new()
                { ["Nahaufnahme"] = "a close-up portrait of a face", ["Ganzkörper"] = "a full body shot of a person" } },
        };

        /// <summary>Sucht eine Modelldatei in „models" bzw. „BildKonturBerechnen/models" aufwärts.</summary>
        private static string? FindeModell(string dateiName)
        {
            string? env = Environment.GetEnvironmentVariable("CLIP_ONNX");
            if (!string.IsNullOrEmpty(env) && File.Exists(env) &&
                string.Equals(Path.GetFileName(env), dateiName, StringComparison.OrdinalIgnoreCase))
                return env;

            foreach (string root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var dir = new DirectoryInfo(root);
                for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
                {
                    string a = Path.Combine(dir.FullName, "models", dateiName);
                    if (File.Exists(a)) return a;
                    string b = Path.Combine(dir.FullName, "BildKonturBerechnen", "models", dateiName);
                    if (File.Exists(b)) return b;
                }
            }
            return null;
        }

        private static (string model, string vocab, string merges)? FindeTextDateien()
        {
            string? m = FindeModell("clip-text.onnx");
            string? v = FindeModell("clip-vocab.json");
            string? g = FindeModell("clip-merges.txt");
            return (m is not null && v is not null && g is not null) ? (m, v, g) : null;
        }

        public void Dispose()
        {
            _cnn?.Dispose();
            _text?.Dispose();
        }

        /// <summary>Wortliste für die offene Bildbeschreibung (aus BildKonturBerechnen übernommen).</summary>
        private static readonly string[] Concepts =
        {
            "flower", "tree", "forest", "mountain", "beach", "ocean", "lake", "river", "sky",
            "clouds", "sunset", "sunrise", "night", "snow", "rain", "water", "fire", "grass",
            "garden", "field", "waterfall", "island", "desert", "cave", "rainbow", "star", "moon",
            "city", "street", "building", "house", "room", "bedroom", "kitchen", "castle", "temple",
            "church", "school", "library", "office", "bridge", "train", "spaceship", "battlefield",
            "person", "girl", "boy", "man", "woman", "child", "baby", "group", "crowd", "couple",
            "face", "portrait", "soldier", "knight", "warrior", "king", "queen", "angel", "demon",
            "robot", "ninja", "samurai", "witch", "monster",
            "animal", "dog", "cat", "bird", "horse", "fish", "dragon", "wolf", "fox", "rabbit",
            "tiger", "lion", "bear", "butterfly", "snake", "owl",
            "car", "boat", "ship", "airplane", "sword", "gun", "knife", "shield", "book", "food",
            "cake", "coffee", "guitar", "piano", "camera", "umbrella", "phone", "clock", "mirror",
            "candle", "flag", "key", "treasure", "magic", "throne",
            "dress", "shirt", "uniform", "armor", "bikini", "swimsuit", "kimono", "hat", "glasses",
            "mask", "crown", "cape", "gloves", "boots",
            "red", "blue", "green", "yellow", "black", "white", "brown", "blonde", "gray", "pink",
            "purple", "orange",
            "close-up", "full body", "silhouette", "reflection", "landscape", "colorful",
            "fighting", "running", "flying", "sitting", "standing", "smiling", "sleeping",
            "swimming", "dancing",
            "fog", "mist", "storm", "lightning", "volcano", "jungle", "swamp", "meadow", "cliff",
            "valley", "hill", "canyon", "glacier", "cherry blossom", "autumn leaves", "starry sky",
            "moonlight", "aurora", "space", "planet", "galaxy", "underwater", "coral reef", "sand",
            "rock", "path", "road", "flower field",
            "bathroom", "living room", "classroom", "hallway", "balcony", "rooftop", "staircase",
            "tower", "lighthouse", "windmill", "ruins", "shrine", "cathedral", "market", "shop",
            "cafe", "restaurant", "bar", "hospital", "station", "airport", "harbor", "factory",
            "laboratory", "dungeon", "arena", "stage", "playground", "tent", "campfire", "hot spring",
            "pool", "aquarium", "museum",
            "teenager", "elderly", "twins", "family", "student", "teacher", "nurse", "doctor", "chef",
            "police officer", "firefighter", "pilot", "sailor", "maid", "butler", "priest", "nun",
            "monk", "wizard", "mage", "hunter", "assassin", "thief", "pirate", "cowboy", "vampire",
            "ghost", "zombie", "skeleton", "mermaid", "fairy", "elf", "orc", "giant", "alien",
            "cyborg", "mecha", "superhero", "villain", "goddess", "prince", "princess",
            "eyes", "tears", "blush", "wings", "horns", "tail", "cat ears", "fangs", "halo", "tattoo",
            "scar", "muscles", "long hair", "short hair", "ponytail", "twintails", "braid", "beard",
            "freckles",
            "jacket", "coat", "sweater", "hoodie", "skirt", "shorts", "pants", "jeans", "stockings",
            "scarf", "tie", "belt", "backpack", "necklace", "earrings", "ring", "sunglasses",
            "headphones", "veil", "apron", "robe", "suit", "wedding dress", "school uniform",
            "military uniform", "cloak", "high heels", "helmet", "tiara", "cap",
            "laptop", "computer", "television", "microphone", "pen", "paintbrush", "notebook",
            "newspaper", "map", "compass", "telescope", "hourglass", "lantern", "torch", "chandelier",
            "fireplace", "ladder", "rope", "chain", "cage", "barrel", "box", "basket", "vase",
            "bottle", "cup", "glass", "plate", "bowl", "teapot", "chair", "table", "desk", "bed",
            "sofa", "shelf", "painting", "poster", "banner", "sign", "statue", "fountain", "bench",
            "window", "door", "wall", "fence", "stairs", "balloon", "kite", "teddy bear", "doll",
            "bicycle", "motorcycle", "bus", "truck", "tank", "helicopter", "rocket", "submarine",
            "sailboat", "carriage", "tram", "ufo",
            "bow", "arrow", "spear", "axe", "dagger", "hammer", "staff", "wand", "cannon", "rifle",
            "pistol", "katana", "scythe", "whip", "magic circle", "portal", "crystal", "orb", "scroll",
            "potion", "treasure chest", "gold", "coins", "gem",
            "dolphin", "whale", "shark", "octopus", "jellyfish", "crab", "turtle", "frog", "lizard",
            "dinosaur", "elephant", "giraffe", "zebra", "monkey", "panda", "penguin", "eagle", "crow",
            "dove", "parrot", "peacock", "swan", "duck", "chicken", "cow", "pig", "sheep", "deer",
            "squirrel", "bat", "spider", "bee", "dragonfly", "unicorn", "phoenix",
            "fruit", "apple", "strawberry", "grapes", "watermelon", "cherry", "lemon", "vegetable",
            "tomato", "pumpkin", "mushroom", "corn", "bread", "sandwich", "burger", "pizza", "pasta",
            "noodles", "ramen", "sushi", "rice", "soup", "salad", "cookie", "donut", "ice cream",
            "chocolate", "candy", "pancake", "egg", "tea", "milk", "juice", "wine", "beer", "cocktail",
            "rose", "tulip", "sunflower", "lotus", "lily", "daisy", "cactus", "bamboo", "palm tree",
            "pine tree", "maple leaf", "lavender", "ivy",
            "cute", "cool", "elegant", "scary", "dark", "bright", "pastel", "neon", "glowing",
            "sparkles", "magical", "futuristic", "cyberpunk", "steampunk", "medieval", "ancient",
            "modern", "vintage", "gothic", "fantasy", "sci-fi", "horror", "dreamy", "cozy",
            "wide shot", "aerial view", "from behind", "from below", "side profile", "shadow",
            "blurry background", "bokeh", "symmetry", "black and white", "sepia", "panorama", "macro",
            "group photo", "selfie",
            "happy", "sad", "angry", "crying", "laughing", "surprised", "scared", "shy", "serious",
            "confused", "sleepy", "excited", "embarrassed", "determined", "calm",
        };
    }
}
