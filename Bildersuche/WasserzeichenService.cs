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

        /// <summary>Gefundene Metadaten-Markierungen (Autor, Copyright, XMP, C2PA …).</summary>
        public List<string> MetadatenHinweise { get; set; } = new();

        public bool HatMetadaten => MetadatenHinweise.Count > 0;

        public bool HatIrgendetwas => HatSichtbares || HatMetadaten;

        /// <summary>Kurzbegründung für den Tooltip am Badge.</summary>
        public string Begruendung()
        {
            var teile = new List<string>();

            if (HatSichtbares)
                teile.Add($"Sichtbares Wasserzeichen erkannt ({Aehnlichkeit * 100f:F0} % Übereinstimmung)");

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

        /// <summary>Gelernte Maske, gilt anwendungsweit (nicht je Ordner).</summary>
        internal const string MaskenDateiName = "wasserzeichen.maske.json";

        /// <summary>
        /// Ab dieser Korrelation gilt ein Wasserzeichen als erkannt.
        ///
        /// Eingemessen an 32 DeviantArt-Bildern gegen 120 unmarkierte: Treffer lagen
        /// bei 0,154 … 0,279, unmarkierte Bilder bei −0,071 … 0,050. Der Wert liegt
        /// mittig in dieser Lücke und trennte beide Mengen fehlerfrei.
        /// </summary>
        internal const float Schwelle = 0.10f;

        private static WasserzeichenMaske? _maske;
        private static bool _maskeGeladen;

        internal static string MaskenPfad =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MaskenDateiName);

        /// <summary>Gelernte Maske vorhanden? Ohne sie ist nur die Metadatenprüfung möglich.</summary>
        internal static bool MaskeVorhanden => HoleMaske() is not null;

        /// <summary>Anzahl der Bilder, aus denen die aktive Maske gelernt wurde.</summary>
        internal static int MaskenGrundmenge => HoleMaske()?.Grundmenge ?? 0;

        private static WasserzeichenMaske? HoleMaske()
        {
            if (!_maskeGeladen)
            {
                _maske = WasserzeichenMaske.Laden(MaskenPfad);
                _maskeGeladen = true;
            }

            return _maske;
        }

        /// <summary>Erzwingt das Neuladen, nachdem eine Maske gelernt wurde.</summary>
        internal static void MaskeVergessen()
        {
            _maske = null;
            _maskeGeladen = false;
        }

        #region Maske lernen

        /// <summary>
        /// Lernt die Maske aus einem Ordner, in dem <b>alle</b> Bilder dasselbe
        /// Wasserzeichen tragen, und legt sie neben der Anwendung ab.
        /// </summary>
        /// <returns>Anzahl der verwendeten Bilder, 0 bei Misserfolg.</returns>
        internal static async Task<int> LerneMaskeAsync(
            string ordner,
            IProgress<(int Erledigt, int Gesamt)>? fortschritt,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
                return 0;

            var dateien = SammleBilder(ordner);
            if (dateien.Count < 5)
                return 0;

            return await Task.Run(() =>
            {
                var maske = WasserzeichenMaske.Lerne(dateien, fortschritt, token);
                if (maske is null)
                    return 0;

                maske.Speichern(MaskenPfad);
                MaskeVergessen();
                return maske.Grundmenge;
            }, token).ConfigureAwait(false);
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

            var maske = HoleMaske();

            await Task.Run(() =>
            {
                for (int i = 0; i < dateien.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var befund = PruefeDatei(dateien[i], maske);
                    ergebnis[befund.Pfad] = befund;

                    fortschritt?.Report((i + 1, dateien.Count));
                }
            }, token).ConfigureAwait(false);

            Speichere(ordner, ergebnis);
            return ergebnis;
        }

        /// <summary>Einzelnes Bild prüfen (sichtbares Wasserzeichen + Metadaten).</summary>
        internal static WasserzeichenBefund PruefeDatei(string pfad, WasserzeichenMaske? maske)
        {
            var befund = new WasserzeichenBefund { Pfad = pfad };

            if (maske is not null)
            {
                befund.Aehnlichkeit = maske.Pruefe(pfad);
                befund.HatSichtbares = befund.Aehnlichkeit >= Schwelle;
            }

            befund.MetadatenHinweise = MetadatenPruefer.Pruefe(pfad).ToList();
            return befund;
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
