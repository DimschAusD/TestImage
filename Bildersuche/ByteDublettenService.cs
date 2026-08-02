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
    /// Sucht byte-identische Bilddateien zwischen einem Basisordner (wird behalten)
    /// und beliebig vielen Vergleichsordnern (dort dürfen die Duplikate weg).
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
        /// Sucht Byte-Duplikate der Basisordner-Dateien in den Vergleichsordnern.
        /// </summary>
        /// <param name="basisOrdner">Ordner, dessen Dateien behalten werden.</param>
        /// <param name="vergleichsOrdner">Ordner, in denen Duplikate gesucht werden.</param>
        /// <param name="mitUnterordnern">Unterordner ebenfalls durchsuchen.</param>
        /// <param name="fortschritt">Meldet (Erledigt, Gesamt, Statustext).</param>
        internal static async Task<List<ByteDublettenTreffer>> FindeByteDublettenAsync(
            string basisOrdner,
            IReadOnlyList<string> vergleichsOrdner,
            bool mitUnterordnern,
            IProgress<(int Erledigt, int Gesamt, string Text)>? fortschritt,
            CancellationToken token)
        {
            var treffer = new List<ByteDublettenTreffer>();

            if (string.IsNullOrWhiteSpace(basisOrdner) || !Directory.Exists(basisOrdner))
                return treffer;

            var suchTiefe = mitUnterordnern ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            fortschritt?.Report((0, 0, "Dateien werden erfasst …"));

            // --- Basisdateien (werden behalten) ---
            var basisDateien = await Task.Run(
                () => SammleBilder(basisOrdner, suchTiefe), token);

            if (basisDateien.Count == 0)
            {
                fortschritt?.Report((0, 0, "Im Basisordner wurden keine Bilder gefunden."));
                return treffer;
            }

            // Basisordner normalisiert — Dateien darunter dürfen nie Löschkandidat sein.
            var basisWurzel = NormalisiereOrdner(basisOrdner);

            // --- Vergleichsdateien (Löschkandidaten) ---
            var vergleichsDateien = new List<string>();
            foreach (var ordner in vergleichsOrdner)
            {
                token.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
                    continue;

                var gefunden = await Task.Run(() => SammleBilder(ordner, suchTiefe), token);

                // Alles, was im Basisordner (oder darunter) liegt, wird geschützt.
                vergleichsDateien.AddRange(
                    gefunden.Where(d => !LiegtUnterhalb(d, basisWurzel)));
            }

            vergleichsDateien = vergleichsDateien
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (vergleichsDateien.Count == 0)
            {
                fortschritt?.Report((0, 0, "In den Vergleichsordnern wurden keine Bilder gefunden."));
                return treffer;
            }

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
            // Nur die Basisdateien hashen, deren Grösse überhaupt bei Kandidaten vorkommt.
            var relevanteGroessen = kandidaten.Select(k => k.Groesse).ToHashSet();
            var zuHashendeBasis = basisNachGroesse
                .Where(g => relevanteGroessen.Contains(g.Key))
                .SelectMany(g => g.Value)
                .ToList();

            int gesamt = zuHashendeBasis.Count + kandidaten.Count;
            int erledigt = 0;

            var basisHashes = new ConcurrentDictionary<string, List<string>>(StringComparer.Ordinal);

            await Parallel.ForEachAsync(
                zuHashendeBasis,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token },
                async (datei, ct) =>
                {
                    var hash = await BerechneHashAsync(datei, ct);
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
            var gefundene = new ConcurrentBag<ByteDublettenTreffer>();

            await Parallel.ForEachAsync(
                kandidaten,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token },
                async (kandidat, ct) =>
                {
                    var hash = await BerechneHashAsync(kandidat.Datei, ct);

                    if (hash != null && basisHashes.TryGetValue(hash, out var basisListe))
                    {
                        string[] schnappschuss;
                        lock (basisListe) { schnappschuss = basisListe.ToArray(); }

                        foreach (var basisDatei in schnappschuss)
                        {
                            if (await SindByteGleichAsync(basisDatei, kandidat.Datei, ct))
                            {
                                gefundene.Add(new ByteDublettenTreffer
                                {
                                    BasisDatei = basisDatei,
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

            treffer = gefundene
                .OrderBy(t => t.DublettenOrdner, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.DublettenDateiName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            fortschritt?.Report((gesamt, gesamt,
                treffer.Count == 0
                    ? "Keine Byte-Duplikate gefunden."
                    : $"{treffer.Count} Byte-Duplikate gefunden."));

            return treffer;
        }

        /// <summary>Sammelt alle unterstützten Bilddateien eines Ordners.</summary>
        private static List<string> SammleBilder(string ordner, SearchOption tiefe)
        {
            try
            {
                return Directory
                    .EnumerateFiles(ordner, "*.*", tiefe)
                    .Where(d => Bildendungen.Contains(Path.GetExtension(d).ToLowerInvariant()))
                    .ToList();
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

        private static async Task<string?> BerechneHashAsync(string datei, CancellationToken token)
        {
            try
            {
                using var stream = new FileStream(
                    datei, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);

                var hash = await SHA256.HashDataAsync(stream, token);
                return Convert.ToHexString(hash);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Echter Byte-Vergleich (Absicherung gegen Hash-Kollisionen).</summary>
        private static async Task<bool> SindByteGleichAsync(string a, string b, CancellationToken token)
        {
            try
            {
                using var stream = new FileStream(
                    a, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);

                return await MieneServices.IsFileGleich2Async(stream, b, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
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
        /// Verschiebt eine Datei in den Papierkorb (bewusst kein endgültiges Löschen).
        /// </summary>
        internal static void InDenPapierkorb(string datei)
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                datei,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
    }
}
