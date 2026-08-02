using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        /// <summary>
        /// Sucht im Dubletten-Ordner alle Dateien, die byte-identisch auch in einem
        /// der Referenzordner liegen.
        /// </summary>
        /// <param name="dublettenOrdner">Ordner, aus dem gelöscht werden darf.</param>
        /// <param name="referenzOrdner">Ordner, deren Dateien behalten werden.</param>
        /// <param name="mitUnterordnern">Unterordner ebenfalls durchsuchen.</param>
        /// <param name="alleDateitypen">False = nur Bilddateien, True = jede Datei.</param>
        /// <param name="fortschritt">Meldet (Erledigt, Gesamt, Statustext).</param>
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
            IProgress<(int Erledigt, int Gesamt, string Text)>? fortschritt,
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

                if (laenge > 0 && basisNachGroesse.ContainsKey(laenge))
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
                .SelectMany(g => g.Value)
                .ToList();

            int gesamt = zuHashendeBasis.Count + kandidaten.Count;
            int erledigt = 0;

            var basisHashes = new ConcurrentDictionary<string, List<string>>(StringComparer.Ordinal);

            // Dateien, die trotz Wiederholung nicht gelesen werden konnten – werden am
            // Ende gemeldet, damit sie nicht unbemerkt aus der Prüfung fallen.
            var nichtLesbar = new ConcurrentBag<string>();

            await Parallel.ForEachAsync(
                zuHashendeBasis,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token },
                async (datei, ct) =>
                {
                    var hash = await BerechneHashAsync(datei, nichtLesbar, ct);
                    if (hash != null)
                    {
                        basisHashes.AddOrUpdate(
                            hash,
                            _ => new List<string> { datei },
                            (_, liste) => { lock (liste) { liste.Add(datei); } return liste; });
                    }

                    int fertig = Interlocked.Increment(ref erledigt);
                    if (fertig % 25 == 0)
                        fortschritt?.Report((fertig, gesamt, $"Prüfsummen: {fertig} / {gesamt}"));
                });

            // --- Stufe 3: Kandidaten hashen und bei Treffer byteweise verifizieren ---
            var treffersammlung = new ConcurrentBag<ByteDublettenTreffer>();

            await Parallel.ForEachAsync(
                kandidaten,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token },
                async (kandidat, ct) =>
                {
                    var hash = await BerechneHashAsync(kandidat.Datei, nichtLesbar, ct);

                    if (hash != null && basisHashes.TryGetValue(hash, out var basisListe))
                    {
                        string[] schnappschuss;
                        lock (basisListe) { schnappschuss = basisListe.ToArray(); }

                        foreach (var basisDatei in schnappschuss)
                        {
                            if (await SindByteGleichAsync(basisDatei, kandidat.Datei, nichtLesbar, ct))
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

                    int fertig = Interlocked.Increment(ref erledigt);
                    if (fertig % 25 == 0)
                        fortschritt?.Report((fertig, gesamt, $"Prüfsummen: {fertig} / {gesamt}"));
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

            fortschritt?.Report((gesamt, gesamt,
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
        /// </summary>
        private static List<string> SammleDateien(string ordner, SearchOption tiefe, bool alleDateitypen)
        {
            try
            {
                var alle = Directory.EnumerateFiles(ordner, "*.*", tiefe);

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

                if (laenge == 0)
                    continue;

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
        /// SHA-256 der Datei. Bei gesperrter Datei (Virenscanner, Explorer-Vorschau,
        /// anderes Programm) wird mehrfach nachgefasst — solche Sperren sind meist
        /// kurzlebig. Gelingt es endgültig nicht, wird der Pfad in
        /// <paramref name="nichtLesbar"/> vermerkt statt still übergangen: sonst
        /// verschwindet die Datei kommentarlos aus der Trefferliste.
        /// </summary>
        private static async Task<string?> BerechneHashAsync(
            string datei, ConcurrentBag<string>? nichtLesbar, CancellationToken token)
        {
            for (int versuch = 1; versuch <= LeseVersuche; versuch++)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    using var stream = new FileStream(
                        datei, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024, useAsync: true);

                    var hash = await SHA256.HashDataAsync(stream, token);
                    return Convert.ToHexString(hash);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (IOException) when (versuch < LeseVersuche)
                {
                    await Task.Delay(LesePauseMs, token);   // Sperre abwarten
                }
                catch (UnauthorizedAccessException) when (versuch < LeseVersuche)
                {
                    await Task.Delay(LesePauseMs, token);
                }
                catch
                {
                    break;
                }
            }

            nichtLesbar?.Add(datei);
            return null;
        }

        /// <summary>
        /// Echter Byte-Vergleich (Absicherung gegen Hash-Kollisionen). Wie beim Hashen
        /// wird bei gesperrter Datei nachgefasst und ein endgültiger Fehlschlag vermerkt.
        /// </summary>
        private static async Task<bool> SindByteGleichAsync(
            string a, string b, ConcurrentBag<string>? nichtLesbar, CancellationToken token)
        {
            for (int versuch = 1; versuch <= LeseVersuche; versuch++)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    using var stream = new FileStream(
                        a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024, useAsync: true);

                    return await MieneServices.IsFileGleich2Async(stream, b, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (IOException) when (versuch < LeseVersuche)
                {
                    await Task.Delay(LesePauseMs, token);
                }
                catch (UnauthorizedAccessException) when (versuch < LeseVersuche)
                {
                    await Task.Delay(LesePauseMs, token);
                }
                catch
                {
                    break;
                }
            }

            nichtLesbar?.Add(b);
            return false;
        }

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
        {
            if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
                return false;

            try
            {
                return !Directory.EnumerateFiles(ordner, "*", SearchOption.AllDirectories).Any();
            }
            catch
            {
                return false;   // nicht lesbar → im Zweifel nichts anbieten
            }
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
