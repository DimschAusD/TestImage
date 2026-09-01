using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Sucht byte-identische Dateien zwischen dem Dubletten-Ordner (dort wird gelöscht)
    /// und beliebig vielen Referenzordnern (die bleiben unangetastet).
    ///
    /// Denkweise wie im Zwei-Fenster-Dateimanager: eine Seite ist der Bestand, die
    /// andere kommt weg. Der Dubletten-Ordner ist die Seite, die weg kann.
    ///
    /// Ablauf in zwei Stufen, damit kein n²-Byte-Vergleich nötig ist:
    /// 1. Nach Dateigrösse gruppieren — nur Grössen, die auf beiden Seiten vorkommen.
    /// 2. Für die verbliebenen Kandidaten SHA-256 berechnen (parallel). Gleiche Grösse
    ///    und gleicher Hash heissen gleicher Inhalt — ein Byte-Vergleich hinterher würde
    ///    beide Dateien ein zweites Mal lesen, nur um eine Kollision auszuschliessen,
    ///    die es praktisch nicht gibt.
    ///
    /// Ausnahme von Stufe 2: Gibt es zu einer Dateigrösse genau eine Referenzdatei,
    /// werden die beiden Dateien direkt byteweise verglichen, statt sie zu hashen.
    /// Gelesen wird dabei gleich viel, aber der Vergleich bricht an der ersten
    /// abweichenden Stelle ab.
    /// </summary>
    internal static class ByteDublettenService
    {
        private static readonly string[] Bildendungen =
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

        /// <summary>
        /// Lesepuffer je Datei – zugleich die Schrittweite der Fortschrittsmeldung.
        ///
        /// 4 MB statt 1 MB wegen der Netzlaufwerke: Über SMB kostet jeder Leseauftrag
        /// eine Umlaufzeit, und bei einer 1,5-GB-Videodatei sind das mit 1-MB-Blöcken
        /// 1500 Umläufe statt 375. Für kleine Dateien ändert sich nichts – der Puffer
        /// kommt aus dem ArrayPool und wird nur so weit gefüllt, wie die Datei reicht.
        /// </summary>
        private const int LeseBlock = 4 * 1024 * 1024;

        /// <summary>
        /// Untergrenze für den Lesepuffer. Kleine Dateien bekommen keinen 4-MB-Puffer:
        /// Der ArrayPool hält geliehene Puffer für spätere Läufe vor, und bei einem Leser
        /// je Kern (im Byte-Vergleich zwei Puffer je Leser) blieben sonst auf einem
        /// 16-Kern-Rechner über 100 MB belegt, nur um lauter 30-KB-Bilder zu lesen.
        /// </summary>
        private const int KleinsterBlock = 64 * 1024;

        /// <summary>
        /// Lesepuffer passend zur Datei: so gross wie nötig, höchstens <see cref="LeseBlock"/>.
        /// Eine 1,5-GB-Videodatei bekommt die vollen 4 MB, ein Vorschaubild 64 KB.
        /// </summary>
        private static int BlockGroesse(long dateiGroesse)
            => (int)Math.Clamp(dateiGroesse, KleinsterBlock, LeseBlock);

        /// <summary>
        /// Gleichzeitige Leser auf Netzlaufwerken. Mehr bremsen dort, statt zu nützen:
        /// Die Bandbreite der Freigabe teilt sich auf alle Ströme auf, und die
        /// Gegenstelle beginnt bei vielen parallelen Grossleseaufträgen zu drosseln.
        /// Lokal bleibt es bei einem Leser je Prozessorkern.
        /// </summary>
        private const int NetzParallel = 4;

        /// <summary>
        /// Mindestanteil, den eine Datei am Fortschritt hat. Öffnen, Suchen und Schliessen
        /// kosten auch bei einer 2-KB-Datei Zeit; ohne diesen Sockel bewegten tausend
        /// winzige Dateien den Balken kaum.
        /// </summary>
        private const long MindestGewicht = 64 * 1024;

        /// <summary>Kleinster Abstand zwischen zwei Fortschrittsmeldungen.</summary>
        private const int MeldeAbstandMs = 120;

        /// <summary>Anteil einer Datei am Gesamtfortschritt (Bytes, mindestens der Sockel).</summary>
        private static long Gewicht(long groesse) => Math.Max(groesse, MindestGewicht);

        /// <summary>
        /// Sucht im Dubletten-Ordner alle Dateien, die byte-identisch auch in einem
        /// der Referenzordner liegen.
        /// </summary>
        /// <param name="dublettenOrdner">Ordner, aus dem gelöscht werden darf.</param>
        /// <param name="referenzOrdner">Ordner, deren Dateien behalten werden.</param>
        /// <param name="mitUnterordnern">Unterordner ebenfalls durchsuchen.</param>
        /// <param name="alleDateitypen">False = nur Bilddateien, True = jede Datei.</param>
        /// <param name="nurGleicherName">
        /// True = eine Datei kommt nur für Referenzdateien <b>gleichen Namens</b> in Frage.
        /// Die Ordnernamen müssen dabei nicht übereinstimmen — verglichen wird allein der
        /// Dateiname. Das ist die Denkweise der Dateimanager-Dublettensuche und macht den
        /// Lauf drastisch schneller, weil pro Kandidat statt einer ganzen Grössengruppe
        /// meist nur noch ein einziges Gegenstück übrig bleibt.
        /// </param>
        /// <param name="tiefenpruefung">
        /// True (Vorgabe) = der Inhalt wird gelesen und geprüft.
        /// False = <b>allein der Dateiname entscheidet</b>, keine einzige Datei wird
        /// geöffnet. Das ist in Sekunden fertig, kann aber gleichnamige Dateien mit
        /// verschiedenem Inhalt zusammenbringen — die Grössen stehen deshalb in jedem
        /// Treffer nebeneinander.
        /// Nur zusammen mit <paramref name="nurGleicherName"/> sinnvoll; ohne Namensbezug
        /// wird die Tiefenprüfung erzwungen, sonst gälte jede Datei gleicher Grösse als
        /// Dublette.
        /// </param>
        /// <param name="fortschritt">
        /// Meldet (Erledigt, Gesamt, Statustext) — beide Zahlen in <b>Bytes</b>, nicht in
        /// Dateien. Solange der Umfang noch unbekannt ist (Dateien werden erfasst), wird
        /// Gesamt = 0 gemeldet.
        /// </param>
        /// <param name="nichtLesbarAusgabe">
        /// Wird mit den Dateien gefüllt, die wegen einer Sperre nicht geprüft werden
        /// konnten. Diese sind <b>nicht</b> als „kein Duplikat" zu verstehen — sie wurden
        /// gar nicht erst verglichen.
        /// </param>
        internal static async Task<List<ByteDublettenTreffer>> FindeByteDublettenAsync(
            string dublettenOrdner,
            IReadOnlyList<string> referenzOrdner,
            bool mitUnterordnern,
            bool alleDateitypen,
            bool nurGleicherName,
            bool tiefenpruefung,
            IProgress<(long Erledigt, long Gesamt, string Text)>? fortschritt,
            CancellationToken token,
            List<string>? nichtLesbarAusgabe = null)
        {
            var treffer = new List<ByteDublettenTreffer>();

            if (string.IsNullOrWhiteSpace(dublettenOrdner) || !Directory.Exists(dublettenOrdner))
                return treffer;

            // Sicherheitsnetz gegen eine Einstellung, die alles gleicher Grösse zur
            // Dublette erklären würde. Die Oberfläche lässt diese Kombination gar nicht
            // erst zu; falls sie doch ankommt, wird geprüft statt geraten.
            if (!nurGleicherName)
                tiefenpruefung = true;

            var suchTiefe = mitUnterordnern ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string was = alleDateitypen ? "Dateien" : "Bilder";

            fortschritt?.Report((0, 0, "Dateien werden erfasst …"));

            // Der Dubletten-Ordner ist die Löschseite und wird deshalb aus dem
            // Referenzbestand ausgeklammert — nicht umgekehrt.
            //
            // Wichtig für den häufigen Fall, dass der Dubletten-Ordner unterhalb eines
            // Referenzordners liegt (z. B. Referenz "C:\Lesen", Dubletten
            // "C:\Lesen\Alter_Desktop\..."). Würde man stattdessen alles unterhalb der
            // Referenzordner schützen, bliebe kein einziger Kandidat übrig. Und ohne
            // diese Ausklammerung fände sich jede Datei über den Referenzordner selbst
            // wieder und wäre ihr eigenes Duplikat.
            var dublettenWurzel = NormalisiereOrdner(dublettenOrdner);

            // --- Referenzdateien (werden behalten) ---
            var referenzDateien = new List<string>();

            foreach (var ordner in referenzOrdner)
            {
                token.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
                    continue;

                var gefunden = await Task.Run(
                    () => SammleDateien(ordner, suchTiefe, alleDateitypen), token);

                referenzDateien.AddRange(
                    gefunden.Where(d => !LiegtUnterhalb(d, dublettenWurzel)));
            }

            referenzDateien = referenzDateien
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (referenzDateien.Count == 0)
            {
                fortschritt?.Report((0, 0,
                    $"Ausserhalb des Dubletten-Ordners wurden in den Referenzordnern keine {was} gefunden."));
                return treffer;
            }

            // --- Löschkandidaten: alles im Dubletten-Ordner ---
            var kandidatenDateien = (await Task.Run(
                    () => SammleDateien(dublettenOrdner, suchTiefe, alleDateitypen), token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (kandidatenDateien.Count == 0)
            {
                fortschritt?.Report((0, 0, $"Im Dubletten-Ordner wurden keine {was} gefunden."));
                return treffer;
            }

            // Ab hier heissen die Referenzdateien intern „basis" (Bestand) und die
            // Kandidaten sind die Löschseite.
            var basisDateien = referenzDateien;
            var vergleichsDateien = kandidatenDateien;

            // --- Sonderweg: nur Namen vergleichen, ohne den Inhalt zu lesen ---
            //
            // Hier wird keine einzige Datei geöffnet. Der ganze Lauf besteht aus dem
            // Auflisten der Ordner und einem Nachschlagen je Kandidat und ist deshalb
            // auch über ein Netzlaufwerk in Sekunden fertig.
            //
            // Die Dateigrösse geht bewusst NICHT in die Bedingung ein: Wer den Inhalt
            // ausdrücklich nicht prüfen lässt, will den reinen Namensabgleich, wie ihn
            // ein Dateimanager anbietet. Eine heimlich mitlaufende Grössenbedingung würde
            // genau die Fälle verschweigen, um die es dabei geht — dieselbe Datei in
            // anderer Auflösung, anderer Qualitätsstufe, anderem Bearbeitungsstand.
            // Damit man das sieht, wandert die Grösse beider Seiten in den Treffer.
            if (!tiefenpruefung)
            {
                return NurNamenVergleichen(basisDateien, vergleichsDateien, fortschritt, token);
            }

            // --- Stufe 1: nach Dateigrösse vorfiltern ---
            fortschritt?.Report((0, 0,
                nurGleicherName
                    ? "Namen und Dateigrössen werden verglichen …"
                    : "Dateigrössen werden verglichen …"));

            var basisNachSchluessel = await Task.Run(
                () => GruppiereNachSchluessel(basisDateien, nurGleicherName, token), token);

            var kandidaten = new List<(string Datei, Vergleichsschluessel Schluessel)>();
            foreach (var datei in vergleichsDateien)
            {
                token.ThrowIfCancellationRequested();

                long laenge;
                try { laenge = new FileInfo(datei).Length; }
                catch { continue; }

                // Länge 0 ist zugelassen: Zwei leere Dateien sind byte-identisch.
                // Für sie greift in Stufe 2 die zusätzliche Namensprüfung.
                var schluessel = Schluessel(datei, laenge, nurGleicherName);

                if (laenge >= 0 && basisNachSchluessel.ContainsKey(schluessel))
                    kandidaten.Add((datei, schluessel));
            }

            if (kandidaten.Count == 0)
            {
                fortschritt?.Report((0, 0, "Keine Byte-Duplikate gefunden."));
                return treffer;
            }

            // --- Abkürzung bei eindeutiger Gruppe ---
            //
            // Gibt es zu einem Vergleichsschlüssel genau eine Referenzdatei, bringt der
            // Umweg über den Hash nichts ein. Beide Wege lesen Referenz und Kandidat je
            // einmal — der direkte Vergleich bricht aber an der ersten abweichenden Stelle
            // ab, während der Hash beide Dateien in jedem Fall bis zum Ende durchrechnet.
            //
            // Das ist der Normalfall bei Videos: grosse Dateien mit je eigener,
            // unverwechselbarer Grösse. Passen zwei davon nicht zusammen, sind statt
            // zweimal 1,5 GB nur ein paar Blöcke über die Leitung gegangen. Zählt der
            // Name mit, trifft die Abkürzung fast immer zu.
            //
            // Leere Dateien bleiben aussen vor: Für sie gilt weiter unten zusätzlich die
            // Namensprüfung, die es hier nicht gäbe.
            var direktKandidaten = new List<(string Datei, Vergleichsschluessel Schluessel)>();
            var hashKandidaten = new List<(string Datei, Vergleichsschluessel Schluessel)>();

            foreach (var kandidat in kandidaten)
            {
                if (kandidat.Schluessel.Groesse > 0 && basisNachSchluessel[kandidat.Schluessel].Count == 1)
                    direktKandidaten.Add(kandidat);
                else
                    hashKandidaten.Add(kandidat);
            }

            // Liegt eine der beiden Seiten im Netz, gilt die kleinere Leserzahl für den
            // ganzen Lauf — die Netzseite ist ohnehin die Bremse.
            int leserParallel =
                IstNetzpfad(dublettenOrdner) || referenzOrdner.Any(IstNetzpfad)
                    ? Math.Min(NetzParallel, Environment.ProcessorCount)
                    : Environment.ProcessorCount;

            // --- Stufe 2: Hashes berechnen ---
            // Nur die Referenzdateien hashen, deren Schlüssel überhaupt bei Kandidaten vorkommt.
            var relevanteSchluessel = hashKandidaten.Select(k => k.Schluessel).ToHashSet();
            var zuHashendeBasis = basisNachSchluessel
                .Where(g => relevanteSchluessel.Contains(g.Key))
                .SelectMany(g => g.Value.Select(datei => (Datei: datei, Groesse: g.Key.Groesse)))
                .ToList();

            // --- Fortschritt in Bytes statt in Dateien ---
            //
            // Eine 2-GB-Datei braucht tausendmal so lange wie ein Vorschaubild, zählte
            // aber genauso viel wie dieses. Bei wenigen grossen Dateien stand der Balken
            // deshalb still und sprang am Ende ans Ziel. Gemessen wird jetzt die Menge
            // gelesener Bytes — laufend während des Lesens, nicht erst nach der Datei.
            //
            // Ein direkt verglichener Kandidat zählt doppelt: Bei ihm werden Referenz und
            // Kandidat in einem Zug gelesen, dafür entfällt das Hashen der Referenz.
            long gesamt = zuHashendeBasis.Sum(b => Gewicht(b.Groesse))
                          + hashKandidaten.Sum(k => Gewicht(k.Schluessel.Groesse))
                          + direktKandidaten.Sum(k => 2 * Gewicht(k.Schluessel.Groesse));
            long erledigt = 0;

            // Die Stückzahl bleibt daneben stehen: Bei tausenden kleinen Dateien sagt
            // „342 von 5000" mehr über den Stand als eine Megabyte-Angabe.
            int dateienGesamt = zuHashendeBasis.Count + hashKandidaten.Count + direktKandidaten.Count;
            int dateienFertig = 0;

            // Wie viele Dateien gerade unter den Händen sind.
            //
            // Eine Datei zählt erst als fertig, wenn sie ganz durch ist. Bei zehn
            // Videodateien und vier gleichzeitigen Lesern stand deshalb minutenlang
            // „0 von 10 Dateien" da, während der Balken auf die Hälfte lief — richtig
            // gerechnet, aber es sah nach einem Hänger aus. Mit der Zahl der gerade
            // bearbeiteten Dateien daneben ist die Null erklärt, statt verdächtig zu sein.
            int dateienInArbeit = 0;

            var meldeUhr = Stopwatch.StartNew();
            long letzteMeldungMs = -MeldeAbstandMs;

            // Bucht gelesene Bytes und meldet den Stand — höchstens alle MeldeAbstandMs,
            // sonst überschütten die parallelen Leser die Oberfläche mit Meldungen.
            Action<long> melde = bytes =>
            {
                long fertig = Interlocked.Add(ref erledigt, bytes);
                long jetzt = meldeUhr.ElapsedMilliseconds;
                long letzte = Interlocked.Read(ref letzteMeldungMs);

                if (jetzt - letzte < MeldeAbstandMs)
                    return;

                // Verliert ein Strang das Rennen, meldet der andere gerade ohnehin.
                if (Interlocked.CompareExchange(ref letzteMeldungMs, jetzt, letzte) != letzte)
                    return;

                long ziel = gesamt;
                int stueck = Volatile.Read(ref dateienFertig);
                int laufend = Volatile.Read(ref dateienInArbeit);

                fortschritt?.Report((Math.Min(fertig, ziel), ziel,
                    $"Wird geprüft … {stueck} von {dateienGesamt} Dateien"
                    + (laufend > 0 ? $" ({laufend} in Arbeit)" : string.Empty)
                    + $" – {GroesseText(fertig)} von {GroesseText(ziel)}"));
            };

            var basisHashes = new ConcurrentDictionary<string, List<string>>(StringComparer.Ordinal);

            // Dateien, die trotz Wiederholung nicht gelesen werden konnten – werden am
            // Ende gemeldet, damit sie nicht unbemerkt aus der Prüfung fallen.
            var nichtLesbar = new ConcurrentBag<string>();

            var treffersammlung = new ConcurrentBag<ByteDublettenTreffer>();

            // --- Stufe 2a: eindeutige Grössengruppen direkt vergleichen ---
            await Parallel.ForEachAsync(
                direktKandidaten,
                new ParallelOptions { MaxDegreeOfParallelism = leserParallel, CancellationToken = token },
                async (kandidat, ct) =>
                {
                    string basisDatei = basisNachSchluessel[kandidat.Schluessel][0];
                    long groesse = kandidat.Schluessel.Groesse;

                    Interlocked.Increment(ref dateienInArbeit);
                    try
                    {
                        if (await SindByteGleichAsync(
                                basisDatei, kandidat.Datei, 2 * Gewicht(groesse),
                                nichtLesbar, melde, ct))
                        {
                            treffersammlung.Add(new ByteDublettenTreffer
                            {
                                ReferenzDatei = basisDatei,
                                DublettenDatei = kandidat.Datei,
                                GroesseBytes = groesse,
                                ReferenzGroesseBytes = groesse
                            });
                        }

                        // Bewusst hier und nicht im finally: Eine abgebrochene Datei ist
                        // nicht fertig geprüft und darf nicht als erledigt zählen.
                        Interlocked.Increment(ref dateienFertig);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref dateienInArbeit);
                    }
                });

            await Parallel.ForEachAsync(
                zuHashendeBasis,
                new ParallelOptions { MaxDegreeOfParallelism = leserParallel, CancellationToken = token },
                async (eintrag, ct) =>
                {
                    var datei = eintrag.Datei;

                    Interlocked.Increment(ref dateienInArbeit);
                    try
                    {
                        var hash = await BerechneHashAsync(
                            datei, Gewicht(eintrag.Groesse), nichtLesbar, melde, ct);

                        if (hash != null)
                        {
                            basisHashes.AddOrUpdate(
                                HashSchluessel(hash, datei, nurGleicherName),
                                _ => new List<string> { datei },
                                (_, liste) => { lock (liste) { liste.Add(datei); } return liste; });
                        }

                        Interlocked.Increment(ref dateienFertig);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref dateienInArbeit);
                    }
                });

            // --- Stufe 2b: Kandidaten hashen und Hash-Treffer zuordnen ---
            await Parallel.ForEachAsync(
                hashKandidaten,
                new ParallelOptions { MaxDegreeOfParallelism = leserParallel, CancellationToken = token },
                async (kandidat, ct) =>
                {
                    long groesse = kandidat.Schluessel.Groesse;

                    Interlocked.Increment(ref dateienInArbeit);
                    try
                    {
                        var hash = await BerechneHashAsync(
                            kandidat.Datei, Gewicht(groesse), nichtLesbar, melde, ct);

                        // Zählt der Name mit, steckt er auch im Hash-Schlüssel: Sonst fänden
                        // sich über den reinen Inhalts-Hash wieder Dateien beliebigen Namens.
                        if (hash != null
                            && basisHashes.TryGetValue(
                                HashSchluessel(hash, kandidat.Datei, nurGleicherName), out var basisListe))
                        {
                            string[] schnappschuss;
                            lock (basisListe) { schnappschuss = basisListe.ToArray(); }

                            // Leere Dateien sind untereinander alle byte-identisch. Ohne
                            // zusätzliche Bedingung gäbe eine einzige leere Datei im
                            // Referenzbestand sämtliche leeren Dateien zum Löschen frei.
                            // Deshalb muss hier der Dateiname übereinstimmen.
                            if (groesse == 0)
                            {
                                string name = Path.GetFileName(kandidat.Datei);
                                schnappschuss = schnappschuss
                                    .Where(b => string.Equals(Path.GetFileName(b), name, StringComparison.OrdinalIgnoreCase))
                                    .ToArray();
                            }

                            // Gleiche Grösse und gleicher SHA-256: Der Inhalt ist damit
                            // identisch, alle Einträge der Liste sind gleichwertig. Die
                            // erste genügt — eine Zuordnung reicht, damit die Dublette
                            // weg kann.
                            var basisDatei = schnappschuss.FirstOrDefault();
                            if (basisDatei != null)
                            {
                                treffersammlung.Add(new ByteDublettenTreffer
                                {
                                    ReferenzDatei = basisDatei,
                                    DublettenDatei = kandidat.Datei,
                                    GroesseBytes = groesse,
                                    ReferenzGroesseBytes = groesse
                                });
                            }
                        }

                        Interlocked.Increment(ref dateienFertig);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref dateienInArbeit);
                    }
                });

            treffer = treffersammlung
                .OrderBy(t => t.DublettenOrdner, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.DublettenDateiName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Gesperrte Dateien ausdrücklich benennen: Sie wurden NICHT geprüft und
            // könnten sehr wohl Duplikate sein — ein zweiter Lauf findet sie meist.
            var uebergangeneListe = nichtLesbar.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            nichtLesbarAusgabe?.AddRange(uebergangeneListe);

            int uebergangen = uebergangeneListe.Count;
            string zusatz = uebergangen == 0
                ? string.Empty
                : $" {uebergangen} Datei(en) waren gesperrt und konnten nicht geprüft werden – Suche später wiederholen.";

            long ziel = Interlocked.Read(ref gesamt);
            fortschritt?.Report((ziel, ziel,
                (treffer.Count == 0
                    ? "Keine Byte-Duplikate gefunden."
                    : $"{treffer.Count} Byte-Duplikate gefunden.") + zusatz));

            return treffer;
        }

        /// <summary>
        /// Auflistung für die Ordner-Übersicht (nach dem Drop). Nach Pfad sortiert,
        /// damit die Anzeige stabil bleibt.
        /// </summary>
        internal static List<string> ListeDateien(
            string ordner, bool mitUnterordnern, bool alleDateitypen, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
                return new List<string>();

            var tiefe = mitUnterordnern ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var dateien = SammleDateien(ordner, tiefe, alleDateitypen);

            token.ThrowIfCancellationRequested();

            dateien.Sort(StringComparer.OrdinalIgnoreCase);
            return dateien;
        }

        /// <summary>
        /// Sammelt die Dateien eines Ordners — standardmässig nur Bilder,
        /// mit <paramref name="alleDateitypen"/> jede Datei.
        ///
        /// Bewusst mit <see cref="EnumerationOptions"/> statt mit der SearchOption-Form:
        /// Jene stellt <c>IgnoreInaccessible</c> auf false und wirft beim ersten
        /// unzugänglichen Unterordner für den <b>gesamten</b> Durchlauf. Das
        /// darunterliegende <c>catch</c> lieferte dann eine leere Liste zurück — ein
        /// einziges gesperrtes Unterverzeichnis (System Volume Information, $Recycle.Bin,
        /// .vs, fremdes Benutzerprofil) liess also einen ganzen Referenzordner
        /// wirkungslos werden, ohne dass es jemand gemerkt hätte. Gemessen an
        /// C:\$Recycle.Bin: alte Form 0 Dateien mit Ausnahme, neue Form 3571 Dateien.
        ///
        /// <c>AttributesToSkip = 0</c> ist Absicht und hält den bisherigen Umfang:
        /// Der Vorgabewert überspränge versteckte und System-Dateien, die alte
        /// SearchOption-Form nahm sie mit.
        /// </summary>
        private static List<string> SammleDateien(string ordner, SearchOption tiefe, bool alleDateitypen)
        {
            try
            {
                var optionen = new EnumerationOptions
                {
                    RecurseSubdirectories = tiefe == SearchOption.AllDirectories,
                    IgnoreInaccessible = true,
                    AttributesToSkip = 0
                };

                var alle = Directory.EnumerateFiles(ordner, "*.*", optionen);

                if (!alleDateitypen)
                    alle = alle.Where(d => Bildendungen.Contains(Path.GetExtension(d).ToLowerInvariant()));

                return alle.ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Wonach zwei Dateien überhaupt zueinander passen dürfen: immer die Grösse,
        /// wahlweise zusätzlich der Dateiname. <see cref="Name"/> ist leer, solange der
        /// Name keine Rolle spielt — dann verhält sich der Schlüssel wie die frühere
        /// reine Grössengruppierung.
        ///
        /// Der Name wird kleingeschrieben abgelegt, weil Windows Dateinamen ohne
        /// Rücksicht auf Gross- und Kleinschreibung vergibt: „Urlaub.jpg" und
        /// „urlaub.jpg" sind dieselbe Datei und müssen im selben Fach landen.
        /// Der Ordnerpfad geht bewusst nicht ein — gesucht wird ja gerade dieselbe
        /// Datei an anderer Stelle.
        /// </summary>
        private readonly record struct Vergleichsschluessel(long Groesse, string Name);

        private static Vergleichsschluessel Schluessel(string datei, long groesse, bool mitName)
            => new(groesse, mitName ? Path.GetFileName(datei).ToLowerInvariant() : string.Empty);

        /// <summary>
        /// Schlüssel für die Hash-Zuordnung. Zählt der Name mit, muss er auch hier
        /// hinein: Der Inhalts-Hash allein brächte sonst wieder Dateien beliebigen
        /// Namens zusammen und höbe die Einschränkung still wieder auf. Das
        /// Trennzeichen ist ein Nullbyte — in Dateinamen kann es nicht vorkommen.
        /// </summary>
        private static string HashSchluessel(string hash, string datei, bool mitName)
            => mitName ? hash + "\0" + Path.GetFileName(datei).ToLowerInvariant() : hash;

        private static Dictionary<Vergleichsschluessel, List<string>> GruppiereNachSchluessel(
            IEnumerable<string> dateien, bool mitName, CancellationToken token)
        {
            var map = new Dictionary<Vergleichsschluessel, List<string>>();

            foreach (var datei in dateien)
            {
                token.ThrowIfCancellationRequested();

                long laenge;
                try { laenge = new FileInfo(datei).Length; }
                catch { continue; }

                // Leere Dateien werden mitgenommen — sie kommen in Projektordnern
                // massenhaft vor (TemporaryGeneratedFile_*.cs und ähnliches) und
                // hielten den Ordner sonst dauerhaft „nicht leer".
                var schluessel = Schluessel(datei, laenge, mitName);

                if (!map.TryGetValue(schluessel, out var liste))
                {
                    liste = new List<string>();
                    map[schluessel] = liste;
                }

                liste.Add(datei);
            }

            return map;
        }

        /// <summary>
        /// Reiner Namensabgleich, ohne eine einzige Datei zu öffnen. Jeder Kandidat
        /// bekommt die erste Referenzdatei gleichen Namens zugeordnet.
        ///
        /// „Erste" heisst hier: die zuerst aufgelistete. Bei mehreren gleichnamigen
        /// Referenzdateien ist die Wahl beliebig, und das ist in Ordnung — gelöscht wird
        /// ohnehin nur der Kandidat, und der Bestand bleibt vollständig, egal welches
        /// Gegenstück im Treffer steht.
        ///
        /// Der Fortschritt zählt hier Dateien statt Bytes; gelesen wird ja nichts. Die
        /// Einheit ist der aufrufenden Seite gleichgültig, sie rechnet daraus einen
        /// Anteil.
        /// </summary>
        private static List<ByteDublettenTreffer> NurNamenVergleichen(
            List<string> basisDateien,
            List<string> vergleichsDateien,
            IProgress<(long Erledigt, long Gesamt, string Text)>? fortschritt,
            CancellationToken token)
        {
            fortschritt?.Report((0, 0, "Dateinamen werden verglichen …"));

            var basisNachName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var datei in basisDateien)
            {
                token.ThrowIfCancellationRequested();

                var name = Path.GetFileName(datei);

                if (!basisNachName.ContainsKey(name))
                    basisNachName[name] = datei;
            }

            var treffer = new List<ByteDublettenTreffer>();
            long gesamt = Math.Max(1, vergleichsDateien.Count);

            var meldeUhr = Stopwatch.StartNew();
            long letzteMeldungMs = -MeldeAbstandMs;

            for (int i = 0; i < vergleichsDateien.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var kandidat = vergleichsDateien[i];

                if (basisNachName.TryGetValue(Path.GetFileName(kandidat), out var basisDatei))
                {
                    treffer.Add(new ByteDublettenTreffer
                    {
                        ReferenzDatei = basisDatei,
                        DublettenDatei = kandidat,
                        GroesseBytes = DateiGroesse(kandidat),
                        ReferenzGroesseBytes = DateiGroesse(basisDatei),
                        IstNurNamensTreffer = true
                    });
                }

                long jetzt = meldeUhr.ElapsedMilliseconds;
                if (jetzt - letzteMeldungMs >= MeldeAbstandMs)
                {
                    letzteMeldungMs = jetzt;
                    fortschritt?.Report((i + 1, gesamt,
                        $"Namen werden verglichen … {i + 1} von {vergleichsDateien.Count} Dateien"));
                }
            }

            var sortiert = treffer
                .OrderBy(t => t.DublettenOrdner, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.DublettenDateiName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int abweichend = sortiert.Count(t => t.HatAbweichendeGroesse);

            string zusatz = abweichend == 0
                ? string.Empty
                : $" Bei {abweichend} davon ist die Datei im Bestand unterschiedlich gross — "
                  + "gleicher Name heisst hier nicht gleicher Inhalt.";

            fortschritt?.Report((gesamt, gesamt,
                (sortiert.Count == 0
                    ? "Keine gleichnamigen Dateien gefunden."
                    : $"{sortiert.Count} gleichnamige Dateien gefunden (Inhalt nicht geprüft).") + zusatz));

            return sortiert;
        }

        /// <summary>Dateigrösse, 0 wenn sie nicht zu ermitteln ist.</summary>
        private static long DateiGroesse(string datei)
        {
            try { return new FileInfo(datei).Length; }
            catch { return 0; }
        }

        /// <summary>Wie oft ein gesperrter Zugriff wiederholt wird, bevor aufgegeben wird.</summary>
        private const int LeseVersuche = 3;

        /// <summary>Wartezeit zwischen zwei Leseversuchen.</summary>
        private const int LesePauseMs = 200;

        /// <summary>
        /// Buchführung für den Fortschritt einer einzelnen Datei: bucht laufend die
        /// gelesenen Bytes, nimmt bei einem Fehlversuch das Gebuchte zurück und gleicht
        /// am Ende auf das volle Budget aus. Dadurch stimmt die Summe auch dann, wenn
        /// eine Datei gesperrt ist oder ein Vergleich vorzeitig abbricht — sonst käme
        /// der Balken nie ganz an.
        /// </summary>
        private sealed class FortschrittsKonto
        {
            private readonly Action<long>? _melde;
            private readonly long _budget;
            private long _gebucht;

            internal FortschrittsKonto(Action<long>? melde, long budget)
            {
                _melde = melde;
                _budget = budget;
            }

            internal void Bucht(long bytes)
            {
                _gebucht += bytes;
                _melde?.Invoke(bytes);
            }

            /// <summary>Nimmt alles Gebuchte zurück — vor einem erneuten Leseversuch.</summary>
            internal void Zurueck()
            {
                if (_gebucht == 0)
                    return;

                _melde?.Invoke(-_gebucht);
                _gebucht = 0;
            }

            /// <summary>Bucht den Rest bis zum Budget — am Ende, wie es auch ausgegangen ist.</summary>
            internal void Abschluss()
            {
                long rest = _budget - _gebucht;
                if (rest > 0)
                    Bucht(rest);
            }
        }

        /// <summary>
        /// Öffnet eine Datei zum blockweisen Durchlesen.
        ///
        /// <c>SequentialScan</c> sagt dem Cache-Manager, dass die Datei von vorn bis
        /// hinten gelesen und danach nicht mehr gebraucht wird: Er liest vor und wirft
        /// Gelesenes gleich wieder weg, statt den Cache mit Gigabytes zu fluten, die
        /// niemand mehr anschaut.
        ///
        /// Die interne Pufferung des FileStreams ist abgeschaltet (<c>bufferSize: 1</c>).
        /// Sie brächte hier nichts – gelesen wird ohnehin in 4-MB-Blöcken in einen
        /// eigenen Puffer – und kostete je Block eine zusätzliche Kopie.
        ///
        /// <c>FileShare.ReadWrite</c> bleibt: Dateien, die ein anderes Programm gerade
        /// offen hat, sollen trotzdem geprüft werden können.
        /// </summary>
        private static FileStream OeffneZumLesen(string datei)
            => new FileStream(
                datei, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 1,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

        /// <summary>
        /// True, wenn der Pfad auf einer Netzfreigabe liegt – als UNC-Pfad
        /// (<c>\\rechner\freigabe</c>) oder über einen verbundenen Laufwerksbuchstaben.
        /// Im Zweifel false: Lieber mit voller Leserzahl arbeiten, als lokal ohne Grund
        /// zu bremsen.
        /// </summary>
        private static bool IstNetzpfad(string? pfad)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pfad))
                    return false;

                string voll = Path.GetFullPath(pfad);

                if (voll.StartsWith(@"\\", StringComparison.Ordinal))
                    return true;

                string? wurzel = Path.GetPathRoot(voll);
                if (string.IsNullOrEmpty(wurzel))
                    return false;

                return new DriveInfo(wurzel).DriveType == DriveType.Network;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Liest den Puffer möglichst voll. Ein einzelnes ReadAsync darf weniger liefern
        /// als angefordert; ohne dieses Nachfassen liefen die beiden Dateien im Vergleich
        /// gegeneinander versetzt und gälten fälschlich als ungleich.
        /// </summary>
        private static async Task<int> LiesBlockAsync(
            FileStream strom, byte[] puffer, int laenge, CancellationToken token)
        {
            int gefuellt = 0;

            while (gefuellt < laenge)
            {
                int gelesen = await strom.ReadAsync(puffer.AsMemory(gefuellt, laenge - gefuellt), token);

                if (gelesen == 0)
                    break;

                gefuellt += gelesen;
            }

            return gefuellt;
        }

        /// <summary>
        /// SHA-256 der Datei, blockweise gelesen und blockweise gemeldet — so bewegt auch
        /// eine einzelne sehr grosse Datei den Fortschrittsbalken.
        ///
        /// Bei gesperrter Datei (Virenscanner, Explorer-Vorschau, anderes Programm) wird
        /// mehrfach nachgefasst — solche Sperren sind meist kurzlebig. Gelingt es
        /// endgültig nicht, wird der Pfad in <paramref name="nichtLesbar"/> vermerkt
        /// statt still übergangen: sonst verschwindet die Datei kommentarlos aus der
        /// Trefferliste.
        /// </summary>
        private static async Task<string?> BerechneHashAsync(
            string datei, long budget, ConcurrentBag<string>? nichtLesbar,
            Action<long>? melde, CancellationToken token)
        {
            var konto = new FortschrittsKonto(melde, budget);

            try
            {
                for (int versuch = 1; versuch <= LeseVersuche; versuch++)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        using var stream = OeffneZumLesen(datei);

                        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                        int block = BlockGroesse(stream.Length);
                        var puffer = ArrayPool<byte>.Shared.Rent(block);
                        try
                        {
                            int gelesen;
                            while ((gelesen = await LiesBlockAsync(stream, puffer, block, token)) > 0)
                            {
                                hasher.AppendData(puffer, 0, gelesen);
                                konto.Bucht(gelesen);
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(puffer);
                        }

                        return Convert.ToHexString(hasher.GetHashAndReset());
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (IOException) when (versuch < LeseVersuche)
                    {
                        konto.Zurueck();
                        await Task.Delay(LesePauseMs, token);   // Sperre abwarten
                    }
                    catch (UnauthorizedAccessException) when (versuch < LeseVersuche)
                    {
                        konto.Zurueck();
                        await Task.Delay(LesePauseMs, token);
                    }
                    catch
                    {
                        konto.Zurueck();
                        break;
                    }
                }

                nichtLesbar?.Add(datei);
                return null;
            }
            finally
            {
                konto.Abschluss();
            }
        }

        /// <summary>
        /// Direkter Byte-Vergleich zweier Dateien — der Weg der Abkürzung bei eindeutiger
        /// Grössengruppe, blockweise mit laufender Fortschrittsmeldung und Abbruch an der
        /// ersten Abweichung. Gelesen werden beide Dateien, deshalb zählt der
        /// Vergleich doppelt so viele Bytes wie eine Datei gross ist. Wie beim Hashen wird
        /// bei gesperrter Datei nachgefasst und ein endgültiger Fehlschlag vermerkt.
        /// </summary>
        private static async Task<bool> SindByteGleichAsync(
            string a, string b, long budget, ConcurrentBag<string>? nichtLesbar,
            Action<long>? melde, CancellationToken token)
        {
            var konto = new FortschrittsKonto(melde, budget);

            try
            {
                for (int versuch = 1; versuch <= LeseVersuche; versuch++)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        using var stromA = OeffneZumLesen(a);
                        using var stromB = OeffneZumLesen(b);

                        if (stromA.Length != stromB.Length)
                            return false;

                        int block = BlockGroesse(stromA.Length);
                        var pufferA = ArrayPool<byte>.Shared.Rent(block);
                        var pufferB = ArrayPool<byte>.Shared.Rent(block);

                        try
                        {
                            while (true)
                            {
                                // Beide Seiten gleichzeitig anfordern statt nacheinander.
                                // Liegt eine Datei im Netz und die andere auf der lokalen
                                // Platte, addierten sich sonst die Wartezeiten, obwohl
                                // sich die beiden Geräte überhaupt nicht ins Gehege
                                // kommen. Task.WhenAll wird bewusst genutzt: Es wartet
                                // beide Aufträge ab, auch wenn der erste scheitert –
                                // sonst bliebe die Ausnahme des zweiten unbeobachtet.
                                var auftragA = LiesBlockAsync(stromA, pufferA, block, token);
                                var auftragB = LiesBlockAsync(stromB, pufferB, block, token);

                                await Task.WhenAll(auftragA, auftragB);

                                int gelesenA = auftragA.Result;
                                int gelesenB = auftragB.Result;

                                konto.Bucht(gelesenA + gelesenB);

                                if (gelesenA != gelesenB)
                                    return false;

                                if (gelesenA == 0)
                                    return true;   // beide gleichzeitig am Ende

                                if (!pufferA.AsSpan(0, gelesenA).SequenceEqual(pufferB.AsSpan(0, gelesenB)))
                                    return false;
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(pufferA);
                            ArrayPool<byte>.Shared.Return(pufferB);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (IOException) when (versuch < LeseVersuche)
                    {
                        konto.Zurueck();
                        await Task.Delay(LesePauseMs, token);
                    }
                    catch (UnauthorizedAccessException) when (versuch < LeseVersuche)
                    {
                        konto.Zurueck();
                        await Task.Delay(LesePauseMs, token);
                    }
                    catch
                    {
                        konto.Zurueck();
                        break;
                    }
                }

                nichtLesbar?.Add(b);
                return false;
            }
            finally
            {
                konto.Abschluss();
            }
        }

        /// <summary>Bytes lesbar als MB oder GB.</summary>
        private static string GroesseText(long bytes)
            => bytes >= 1024L * 1024 * 1024
                ? $"{bytes / 1024.0 / 1024.0 / 1024.0:0.00} GB"
                : $"{bytes / 1024.0 / 1024.0:0.0} MB";

        private static string NormalisiereOrdner(string ordner)
        {
            var voll = Path.GetFullPath(ordner);
            return voll.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        }

        /// <summary>True, wenn die Datei im angegebenen Ordner oder einem Unterordner liegt.</summary>
        private static bool LiegtUnterhalb(string datei, string ordnerMitTrenner)
        {
            try
            {
                return Path.GetFullPath(datei)
                    .StartsWith(ordnerMitTrenner, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// True, wenn im Ordner keine einzige Datei mehr liegt — auch nicht in
        /// Unterordnern. Reine Ordnergerüste ohne Inhalt gelten damit als leer, denn
        /// genau die bleiben nach einem Aufräumlauf zurück.
        /// </summary>
        internal static bool IstOrdnerLeer(string? ordner)
            => ZaehleVerbleibendeDateien(ordner) == 0;

        /// <summary>
        /// Anzahl der Dateien, die noch im Ordner liegen — alle Typen, alle Unterordner.
        /// −1, wenn der Ordner fehlt oder nicht lesbar ist.
        ///
        /// Wird gebraucht, um erklären zu können, warum ein Ordner nach dem Aufräumen
        /// nicht als leer gilt: Übrig bleibt alles ohne Gegenstück im Referenzbestand,
        /// bei „nur Bilder" zusätzlich sämtliche Nicht-Bilddateien.
        /// </summary>
        internal static int ZaehleVerbleibendeDateien(string? ordner)
            => ListeVerbleibendeDateien(ordner, int.MaxValue)?.Count ?? -1;

        /// <summary>
        /// Sammelt die verbliebenen Dateien, höchstens <paramref name="hoechstens"/> Stück.
        /// Null, wenn der Ordner fehlt.
        ///
        /// Bewusst Ordner für Ordner statt mit SearchOption.AllDirectories: Jener wirft
        /// bei einem einzigen unzugänglichen Unterordner (etwa .vs in Projektordnern)
        /// eine Ausnahme für den gesamten Durchlauf. Ein Ordner voller leerer Unterordner
        /// galt dadurch fälschlich als „nicht leer" — hier wird nur der betroffene
        /// Unterordner übersprungen.
        ///
        /// Versteckte und System-Dateien zählen mit: desktop.ini oder Thumbs.db sieht man
        /// im Explorer meist nicht, sie liegen aber sehr wohl im Ordner.
        /// </summary>
        internal static List<string>? ListeVerbleibendeDateien(string? ordner, int hoechstens)
        {
            if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
                return null;

            var gefunden = new List<string>();
            var offen = new Stack<string>();
            offen.Push(ordner);

            while (offen.Count > 0 && gefunden.Count < hoechstens)
            {
                string aktuell = offen.Pop();

                try
                {
                    foreach (var datei in Directory.EnumerateFiles(aktuell))
                    {
                        gefunden.Add(datei);
                        if (gefunden.Count >= hoechstens)
                            break;
                    }
                }
                catch
                {
                    // Dieser Ordner ist nicht lesbar – die übrigen trotzdem prüfen.
                }

                try
                {
                    foreach (var unter in Directory.EnumerateDirectories(aktuell))
                        offen.Push(unter);
                }
                catch
                {
                    // dito
                }
            }

            return gefunden;
        }

        /// <summary>
        /// Verschiebt einen Ordner samt leerer Unterstruktur in den Papierkorb.
        /// Prüft zur Sicherheit noch einmal selbst, ob er wirklich leer ist — zwischen
        /// Anzeige und Klick kann sich der Inhalt geändert haben.
        /// </summary>
        internal static bool OrdnerInDenPapierkorb(string ordner)
        {
            if (!IstOrdnerLeer(ordner))
                return false;

            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    ordner,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Warnt, wenn auf dem Laufwerk dieses Pfades vermutlich kein Papierkorb liegt.
        /// Liefert einen fertigen Absatz für die Rückfrage — oder <c>""</c>, wenn nichts
        /// dagegen spricht.
        ///
        /// Der Hintergrund: <c>RecycleOption.SendToRecycleBin</c> ist ein Wunsch, keine
        /// Zusage. Wo Windows keinen Papierkorb führt, löscht derselbe Aufruf sofort und
        /// endgültig — ohne Fehler und ohne Hinweis. Dann verspricht die Rückfrage etwas,
        /// das nicht eintritt, und das ist schlimmer, als gar nichts zu versprechen.
        ///
        /// Zwei Fälle sind sicher erkennbar:
        ///
        /// <b>Netzlaufwerke</b> haben grundsätzlich keinen Papierkorb.
        ///
        /// <b>Alles ausser NTFS.</b> Der Papierkorb setzt NTFS voraus. Grosse externe
        /// Platten sind oft exFAT formatiert, USB-Stöcke FAT32 — dort ist gelöscht
        /// gelöscht.
        ///
        /// NICHT erkannt wird die dritte Möglichkeit: Der Nutzer kann den Papierkorb je
        /// Laufwerk abschalten („Dateien sofort löschen"). Das stünde in der Registrierung
        /// unter BitBucket und wird hier nicht geprüft.
        /// </summary>
        internal static string PapierkorbWarnung(string pfad)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pfad))
                {
                    return string.Empty;
                }

                string voll = Path.GetFullPath(pfad);

                // UNC-Pfad (\\rechner\freigabe): DriveInfo kann damit nichts anfangen.
                if (voll.StartsWith(@"\\", StringComparison.Ordinal))
                {
                    return Warntext("Netzlaufwerke führen keinen Papierkorb.");
                }

                string? wurzel = Path.GetPathRoot(voll);
                if (string.IsNullOrEmpty(wurzel))
                {
                    return string.Empty;
                }

                var laufwerk = new DriveInfo(wurzel);
                if (!laufwerk.IsReady)
                {
                    return string.Empty;
                }

                if (laufwerk.DriveType == DriveType.Network)
                {
                    return Warntext("Netzlaufwerke führen keinen Papierkorb.");
                }

                if (!string.Equals(laufwerk.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                {
                    return Warntext($"Das Laufwerk ist mit {laufwerk.DriveFormat} formatiert, "
                                    + "und einen Papierkorb gibt es nur auf NTFS.");
                }

                return string.Empty;
            }
            catch (Exception)
            {
                // Unbekanntes oder nicht lesbares Laufwerk: lieber nicht warnen als falsch
                // warnen. Eine Warnung, die zu oft grundlos kommt, wird weggeklickt.
                return string.Empty;
            }
        }

        private static string Warntext(string grund) =>
            "\n\nACHTUNG — hier gilt das mit dem Papierkorb möglicherweise nicht:\n"
            + grund + "\n"
            + "Die Dateien wären dann sofort und endgültig gelöscht.";

        /// <summary>
        /// Verschiebt eine Datei in den Papierkorb (bewusst kein endgültiges Löschen).
        /// Bei kurzzeitiger Sperre wird wiederholt, statt sofort aufzugeben.
        /// </summary>
        internal static void InDenPapierkorb(string datei)
        {
            for (int versuch = 1; ; versuch++)
            {
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        datei,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    return;
                }
                catch (IOException) when (versuch < LeseVersuche)
                {
                    Thread.Sleep(LesePauseMs);
                }
                catch (UnauthorizedAccessException) when (versuch < LeseVersuche)
                {
                    Thread.Sleep(LesePauseMs);
                }
            }
        }
    }
}
