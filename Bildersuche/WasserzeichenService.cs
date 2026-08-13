using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TestImage.Bildersuche
{
    /// <summary>Befund zu einem einzelnen Bild.</summary>
    public sealed class WasserzeichenBefund
    {
        public string Pfad { get; set; } = string.Empty;

        /// <summary>Übereinstimmung mit der gelernten Maske, −1 … +1.</summary>
        public float Aehnlichkeit { get; set; }

        /// <summary>True, wenn die Übereinstimmung über der Schwelle liegt.</summary>
        public bool HatSichtbares { get; set; }

        /// <summary>Name des Musters, das am besten passte. Leer, wenn keines passte.</summary>
        public string MaskenName { get; set; } = string.Empty;

        /// <summary>Gefundene Metadaten-Markierungen (Autor, Copyright, XMP, C2PA …).</summary>
        public List<string> MetadatenHinweise { get; set; } = new();

        public bool HatMetadaten => MetadatenHinweise.Count > 0;

        public bool HatIrgendetwas => HatSichtbares || HatMetadaten;

        /// <summary>Kurzbegründung für den Tooltip am Badge.</summary>
        public string Begruendung()
        {
            var teile = new List<string>();

            if (HatSichtbares)
            {
                string muster = string.IsNullOrWhiteSpace(MaskenName) ? "Wasserzeichen" : MaskenName;
                teile.Add($"Sichtbares Wasserzeichen erkannt – Muster „{muster}“ ({Aehnlichkeit * 100f:F0} % Übereinstimmung)");
            }

            teile.AddRange(MetadatenHinweise);

            return teile.Count == 0 ? "Keine Markierung gefunden" : string.Join("\n", teile);
        }
    }

    /// <summary>
    /// Prüft Bilder auf Wasserzeichen — sichtbar aufgeprägte über die gelernte
    /// <see cref="WasserzeichenMaske"/>, unsichtbare über <see cref="MetadatenPruefer"/>.
    /// Die Befunde liegen als Seitendatei neben dem CLIP-Index, damit das Fremdprojekt
    /// ImageMatching.Core unverändert bleibt.
    /// </summary>
    internal static class WasserzeichenService
    {
        /// <summary>Befunddatei je Bildordner.</summary>
        internal const string CacheDateiName = ".bildwasserzeichen.json";

        /// <summary>
        /// Sammlung der gelernten Muster, gilt anwendungsweit (nicht je Ordner).
        /// Mehrzahl, weil ein Anbieter durchaus mehrere Zeichentypen verwendet —
        /// DeviantArt etwa mindestens drei.
        /// </summary>
        internal const string MaskenDateiName = "wasserzeichen.masken.json";

        /// <summary>Einzelmaske der ersten Fassung. Wird beim ersten Laden übernommen.</summary>
        internal const string MaskenDateiNameAlt = "wasserzeichen.maske.json";

        /// <summary>
        /// Ab dieser Korrelation gilt ein Wasserzeichen als erkannt.
        ///
        /// Eingemessen an 32 DeviantArt-Bildern gegen 120 unmarkierte: Treffer lagen
        /// bei 0,154 … 0,279, unmarkierte Bilder bei −0,071 … 0,050. Der Wert liegt
        /// mittig in dieser Lücke und trennte beide Mengen fehlerfrei.
        /// </summary>
        internal const float Schwelle = 0.10f;

        private static List<WasserzeichenMaske>? _masken;

        internal static string MaskenPfad =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MaskenDateiName);

        private static string MaskenPfadAlt =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MaskenDateiNameAlt);

        /// <summary>Alle gelernten Muster. Ohne mindestens eines greift nur die Metadatenprüfung.</summary>
        internal static IReadOnlyList<WasserzeichenMaske> Masken => HoleMasken();

        /// <summary>Mindestens ein Muster vorhanden?</summary>
        internal static bool MaskeVorhanden => HoleMasken().Count > 0;

        /// <summary>Summe der Bilder, aus denen die Muster gelernt wurden.</summary>
        internal static int MaskenGrundmenge => HoleMasken().Sum(m => m.Grundmenge);

        private static List<WasserzeichenMaske> HoleMasken()
        {
            if (_masken is null)
            {
                _masken = LadeMasken();

                // Einzelmaske der ersten Fassung übernehmen, damit ein bereits
                // gelerntes Muster beim Umstieg nicht verlorengeht.
                if (_masken.Count == 0 && File.Exists(MaskenPfadAlt))
                {
                    var alt = WasserzeichenMaske.Laden(MaskenPfadAlt);
                    if (alt is not null)
                    {
                        alt.Name = string.IsNullOrWhiteSpace(alt.Name) ? "Muster 1" : alt.Name;
                        _masken.Add(alt);
                        SpeichereMasken(_masken);
                    }
                }
            }

            return _masken;
        }

        /// <summary>Erzwingt das Neuladen, nachdem sich die Muster geändert haben.</summary>
        internal static void MaskeVergessen() => _masken = null;

        #region Muster lernen und verwalten

        /// <summary>
        /// Lernt ein Muster aus einem Ordner, in dem <b>alle</b> Bilder denselben
        /// Zeichentyp tragen, und legt es unter <paramref name="name"/> in der Sammlung
        /// ab. Ein gleichnamiges Muster wird ersetzt — nochmal lernen heisst auffrischen.
        /// </summary>
        /// <returns>Anzahl der verwendeten Bilder, 0 bei Misserfolg.</returns>
        internal static async Task<int> LerneMaskeAsync(
            string ordner,
            string name,
            IProgress<(int Erledigt, int Gesamt)>? fortschritt,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
                return 0;

            var dateien = SammleBilder(ordner);
            if (dateien.Count < 5)
                return 0;

            var maske = await Task.Run(
                () => WasserzeichenMaske.Lerne(dateien, fortschritt, token), token).ConfigureAwait(false);

            if (maske is null)
                return 0;

            maske.Name = string.IsNullOrWhiteSpace(name) ? "Muster" : name.Trim();

            var alle = HoleMasken();
            alle.RemoveAll(m => string.Equals(m.Name, maske.Name, StringComparison.OrdinalIgnoreCase));
            alle.Add(maske);

            SpeichereMasken(alle);
            return maske.Grundmenge;
        }

        /// <summary>Entfernt ein Muster aus der Sammlung.</summary>
        internal static bool EntferneMaske(string name)
        {
            var alle = HoleMasken();
            if (alle.RemoveAll(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) == 0)
                return false;

            SpeichereMasken(alle);
            return true;
        }

        private static List<WasserzeichenMaske> LadeMasken()
        {
            var liste = new List<WasserzeichenMaske>();

            try
            {
                if (!File.Exists(MaskenPfad))
                    return liste;

                using var fs = File.OpenRead(MaskenPfad);
                var daten = JsonSerializer.Deserialize<List<WasserzeichenMaske.MaskenDatei>>(fs);

                if (daten is null)
                    return liste;

                foreach (var d in daten)
                {
                    var maske = WasserzeichenMaske.AusDatensatz(d);
                    if (maske is not null)
                        liste.Add(maske);
                }
            }
            catch
            {
                // beschädigte Datei → wie „noch nichts gelernt" behandeln
            }

            return liste;
        }

        private static void SpeichereMasken(List<WasserzeichenMaske> masken)
        {
            try
            {
                using var fs = File.Create(MaskenPfad);
                JsonSerializer.Serialize(fs, masken.Select(m => m.AlsDatensatz()).ToList());
            }
            catch
            {
                // schreibgeschützter Programmordner – dann bleibt es bei dieser Sitzung
            }
        }

        #endregion

        #region Ordner prüfen

        /// <summary>
        /// Prüft alle Bilder eines Ordners und schreibt die Befunde in die Seitendatei.
        /// Wird beim Indexieren mitgerufen.
        /// </summary>
        internal static async Task<Dictionary<string, WasserzeichenBefund>> PruefeOrdnerAsync(
            string ordner,
            IProgress<(int Erledigt, int Gesamt)>? fortschritt,
            CancellationToken token)
        {
            var ergebnis = new Dictionary<string, WasserzeichenBefund>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
                return ergebnis;

            var dateien = SammleBilder(ordner);
            if (dateien.Count == 0)
                return ergebnis;

            var masken = HoleMasken();

            // Wie beim Indexieren mehrere Bilder gleichzeitig: Jedes Bild wird für sich
            // geladen und gerechnet, es gibt keine gemeinsamen Zwischenstände. Der Aufwand
            // liegt im Dekodieren und in der Korrelation, beides skaliert über die Kerne.
            int grad = Math.Max(1, Environment.ProcessorCount);
            var gesammelt = new System.Collections.Concurrent.ConcurrentDictionary<string, WasserzeichenBefund>(
                StringComparer.OrdinalIgnoreCase);

            int erledigt = 0;

            await Task.Run(() =>
            {
                Parallel.ForEach(
                    dateien,
                    new ParallelOptions { MaxDegreeOfParallelism = grad },
                    datei =>
                    {
                        // Abbruch ohne Ausnahme, damit die bereits geprüften Bilder
                        // erhalten bleiben – wie beim Indexieren.
                        if (token.IsCancellationRequested)
                            return;

                        var befund = PruefeDatei(datei, masken);
                        gesammelt[befund.Pfad] = befund;

                        fortschritt?.Report((Interlocked.Increment(ref erledigt), dateien.Count));
                    });
            }, token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();

            foreach (var paar in gesammelt)
                ergebnis[paar.Key] = paar.Value;

            Speichere(ordner, ergebnis);
            return ergebnis;
        }

        /// <summary>
        /// Einzelnes Bild prüfen (sichtbares Wasserzeichen + Metadaten). Es gewinnt das
        /// Muster mit der höchsten Übereinstimmung — die Zeichentypen schliessen sich
        /// gegenseitig aus, ein Bild trägt nur einen davon.
        /// </summary>
        internal static WasserzeichenBefund PruefeDatei(string pfad, IReadOnlyList<WasserzeichenMaske> masken)
        {
            var befund = new WasserzeichenBefund { Pfad = pfad };

            float beste = 0f;
            string besterName = string.Empty;

            // Die Datei einmal dekodieren und alle Muster gegen dasselbe Bild prüfen.
            //
            // Vorher rief jede Maske Pruefe(pfad) auf, und das lud die Datei jedes Mal
            // neu — bei drei Mustern also dreimal dekodieren je Bild. Das ist beim
            // Umbau auf mehrere Muster hineingerutscht.
            var bild = LadeBild(pfad);

            if (bild is not null)
            {
                foreach (var maske in masken)
                {
                    float wert = maske.Pruefe(bild);
                    if (wert > beste)
                    {
                        beste = wert;
                        besterName = maske.Name;
                    }
                }
            }

            befund.Aehnlichkeit = beste;
            befund.HatSichtbares = beste >= Schwelle;
            befund.MaskenName = befund.HatSichtbares ? besterName : string.Empty;

            befund.MetadatenHinweise = MetadatenPruefer.Pruefe(pfad).ToList();
            return befund;
        }

        /// <summary>
        /// Lädt das Bild einmal, eingefroren, damit es über Fadengrenzen hinweg benutzt
        /// werden darf. <c>null</c>, wenn die Datei nicht lesbar ist.
        /// </summary>
        private static System.Windows.Media.Imaging.BitmapSource? LadeBild(string pfad)
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(pfad);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Befunde speichern und laden

        private sealed class BefundDatei
        {
            public int Version { get; set; } = 1;
            public List<WasserzeichenBefund> Befunde { get; set; } = new();
        }

        private static void Speichere(string ordner, Dictionary<string, WasserzeichenBefund> befunde)
        {
            try
            {
                var datei = new BefundDatei { Befunde = befunde.Values.ToList() };
                using var fs = File.Create(Path.Combine(ordner, CacheDateiName));
                JsonSerializer.Serialize(fs, datei);
            }
            catch
            {
                // Schreibgeschützter Ordner o. ä. – der Befund geht dann nur verloren.
            }
        }

        /// <summary>Gespeicherte Befunde eines Ordners, leer wenn noch nicht geprüft.</summary>
        internal static Dictionary<string, WasserzeichenBefund> Lade(string ordner)
        {
            var map = new Dictionary<string, WasserzeichenBefund>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string pfad = Path.Combine(ordner, CacheDateiName);
                if (!File.Exists(pfad))
                    return map;

                using var fs = File.OpenRead(pfad);
                var datei = JsonSerializer.Deserialize<BefundDatei>(fs);

                if (datei?.Befunde is null)
                    return map;

                foreach (var b in datei.Befunde)
                    map[b.Pfad] = b;
            }
            catch
            {
                // beschädigte Datei → wie „nicht geprüft" behandeln
            }

            return map;
        }

        #endregion

        private static readonly string[] Bildendungen =
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

        private static List<string> SammleBilder(string ordner)
        {
            try
            {
                return Directory
                    .EnumerateFiles(ordner, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(d => Bildendungen.Contains(Path.GetExtension(d).ToLowerInvariant()))
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }
    }
}
