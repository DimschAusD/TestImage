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
    /// Ablauf in drei Stufen, damit kein n²-Byte-Vergleich nötig ist:
    /// 1. Nach Dateigrösse gruppieren — nur Grössen, die auf beiden Seiten vorkommen.
    /// 2. Für die verbliebenen Kandidaten SHA-256 berechnen (parallel).
    /// 3. Bei Hash-Gleichheit zusätzlich echten Byte-Vergleich zur Absicherung.
    /// </summary>
    internal static class ByteDublettenService
    {
        private static readonly string[] Bildendungen =
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

        /// <summary>Lesepuffer je Datei – zugleich die Schrittweite der Fortschrittsmeldung.</summary>
        private const int LeseBlock = 1024 * 1024;

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
            IProgress<(long Erledigt, long Gesamt, string Text)>? fortschritt,
            CancellationToken token,
            List<string>? nichtLesbarAusgabe = null)
        {
            var treffer = new List<ByteDublettenTreffer>();

            if (string.IsNullOrWhiteSpace(dublettenOrdner) || !Directory.Exists(dublettenOrdner))
                return treffer;

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

            // --- Stufe 1: nach Dateigrösse vorfiltern ---
            fortschritt?.Report((0, 0, "Dateigrössen werden verglichen …"));

            var basisNachGroesse = await Task.Run(
                () => GruppiereNachGroesse(basisDateien, token), token);

            var kandidaten = new List<(string Datei, long Groesse)>();
            foreach (var datei in vergleichsDateien)
            {
                token.ThrowIfCancellationRequested();

                long laenge;
                try { laenge = new FileInfo(datei).Length; }
                catch { continue; }

                // Länge 0 ist zugelassen: Zwei leere Dateien sind byte-identisch.
                // Für sie greift in Stufe 3 die zusätzliche Namensprüfung.
                if (laenge >= 0 && basisNachGroesse.ContainsKey(laenge))
                    kandidaten.Add((datei, laenge));
            }

            if (kandidaten.Count == 0)
            {
                fortschritt?.Report((0, 0, "Keine Byte-Duplikate gefunden."));
                return treffer;
            }

            // --- Stufe 2: Hashes berechnen ---
            // Nur die Referenzdateien hashen, deren Grösse überhaupt bei Kandidaten vorkommt.
            var relevanteGroessen = kandidaten.Select(k => k.Groesse).ToHashSet();
            var zuHashendeBasis = basisNachGroesse
                .Where(g => relevanteGroessen.Contains(g.Key))
                .SelectMany(g => g.Value.Select(datei => (Datei: datei, Groesse: g.Key)))
                .ToList();

            // --- Fortschritt in Bytes statt in Dateien ---
            //
            // Eine 2-GB-Datei braucht tausendmal so lange wie ein Vorschaubild, zählte
            // aber genauso viel wie dieses. Bei wenigen grossen Dateien stand der Balken
            // deshalb still und sprang am Ende ans Ziel. Gemessen wird jetzt die Menge
            // gelesener Bytes — laufend während des Lesens, nicht erst nach der Datei.
            long gesamt = zuHashendeBasis.Sum(b => Gewicht(b.Groesse))
                          + kandidaten.Sum(k => Gewicht(k.Groesse));
            long erledigt = 0;

            // Die Stückzahl bleibt daneben stehen: Bei tausenden kleinen Dateien sagt
            // „342 von 5000" mehr über den Stand als eine Megabyte-Angabe.
            int dateienGesamt = zuHashendeBasis.Count + kandidaten.Count;
            int dateienFertig = 0;

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

                long ziel = Interlocked.Read(ref gesamt);
                int stueck = Volatile.Read(ref dateienFertig);

                fortschritt?.Report((Math.Min(fertig, ziel), ziel,
                    $"Wird geprüft … {stueck} von {dateienGesamt} Dateien"
                    + $" – {GroesseText(fertig)} von {GroesseText(ziel)}"));
            };

            var basisHashes = new ConcurrentDictionary<string, List<string>>(StringComparer.Ordinal);

            // Dateien, die trotz Wiederholung nicht gelesen werden konnten – werden am
            // Ende gemeldet, damit sie nicht unbemerkt aus der Prüfung fallen.
            var nichtLesbar = new ConcurrentBag<string>();

            await Parallel.ForEachAsync(
                zuHashendeBasis,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token },
                async (eintrag, ct) =>
                {
                    var datei = eintrag.Datei;
                    var hash = await BerechneHashAsync(datei, Gewicht(eintrag.Groesse), nichtLesbar, melde, ct);

                    if (hash != null)
                    {
                        basisHashes.AddOrUpdate(
                            hash,
                            _ => new List<string> { datei },
                            (_, liste) => { lock (liste) { liste.Add(datei); } return liste; });
                    }

                    Interlocked.Increment(ref dateienFertig);
                });

            // --- Stufe 3: Kandidaten hashen und bei Treffer byteweise verifizieren ---
            var treffersammlung = new ConcurrentBag<ByteDublettenTreffer>();

            await Parallel.ForEachAsync(
                kandidaten,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token },
                async (kandidat, ct) =>
                {
                    var hash = await BerechneHashAsync(
                        kandidat.Datei, Gewicht(kandidat.Groesse), nichtLesbar, melde, ct);

                    if (hash != null && basisHashes.TryGetValue(hash, out var basisListe))
                    {
                        string[] schnappschuss;
                        lock (basisListe) { schnappschuss = basisListe.ToArray(); }

                        // Leere Dateien sind untereinander alle byte-identisch. Ohne
                        // zusätzliche Bedingung gäbe eine einzige leere Datei im
                        // Referenzbestand sämtliche leeren Dateien zum Löschen frei.
                        // Deshalb muss hier der Dateiname übereinstimmen.
                        if (kandidat.Groesse == 0)
                        {
                            string name = Path.GetFileName(kandidat.Datei);
                            schnappschuss = schnappschuss
                                .Where(b => string.Equals(Path.GetFileName(b), name, StringComparison.OrdinalIgnoreCase))
                                .ToArray();
                        }

                        foreach (var basisDatei in schnappschuss)
                        {
                            // Der Byte-Vergleich liest beide Dateien noch einmal komplett.
                            // Diese Arbeit steht vorher nicht fest — sie fällt nur bei
                            // Hash-Treffern an — und wird deshalb erst hier zum Gesamt
                            // addiert. Der Balken wird davon kurz langsamer, statt am
                            // Ende zu springen.
                            long vergleichsBudget = 2 * Gewicht(kandidat.Groesse);
                            Interlocked.Add(ref gesamt, vergleichsBudget);

                            if (await SindByteGleichAsync(
                                    basisDatei, kandidat.Datei, vergleichsBudget, nichtLesbar, melde, ct))
                            {
                                treffersammlung.Add(new ByteDublettenTreffer
                                {
                                    ReferenzDatei = basisDatei,
                                    DublettenDatei = kandidat.Datei,
                                    GroesseBytes = kandidat.Groesse
                                });
                                break; // eine Zuordnung genügt — die Dublette kann weg
                            }
                        }
                    }

                    Interlocked.Increment(ref dateienFertig);
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

        private static Dictionary<long, List<string>> GruppiereNachGroesse(
            IEnumerable<string> dateien, CancellationToken token)
        {
            var map = new Dictionary<long, List<string>>();

            foreach (var datei in dateien)
            {
                token.ThrowIfCancellationRequested();

                long laenge;
                try { laenge = new FileInfo(datei).Length; }
                catch { continue; }

                // Leere Dateien werden mitgenommen — sie kommen in Projektordnern
                // massenhaft vor (TemporaryGeneratedFile_*.cs und ähnliches) und
                // hielten den Ordner sonst dauerhaft „nicht leer".
                if (!map.TryGetValue(laenge, out var liste))
                {
                    liste = new List<string>();
                    map[laenge] = liste;
                }

                liste.Add(datei);
            }

            return map;
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
                        using var stream = new FileStream(
                            datei, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, LeseBlock, useAsync: true);

                        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                        var puffer = ArrayPool<byte>.Shared.Rent(LeseBlock);
                        try
                        {
                            int gelesen;
                            while ((gelesen = await LiesBlockAsync(stream, puffer, LeseBlock, token)) > 0)
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
        /// Echter Byte-Vergleich (Absicherung gegen Hash-Kollisionen), blockweise mit
        /// laufender Fortschrittsmeldung. Gelesen werden beide Dateien, deshalb zählt der
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
                        using var stromA = new FileStream(
                            a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, LeseBlock, useAsync: true);

                        using var stromB = new FileStream(
                            b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, LeseBlock, useAsync: true);

                        if (stromA.Length != stromB.Length)
                            return false;

                        var pufferA = ArrayPool<byte>.Shared.Rent(LeseBlock);
                        var pufferB = ArrayPool<byte>.Shared.Rent(LeseBlock);

                        try
                        {
                            while (true)
                            {
                                int gelesenA = await LiesBlockAsync(stromA, pufferA, LeseBlock, token);
                                int gelesenB = await LiesBlockAsync(stromB, pufferB, LeseBlock, token);

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
