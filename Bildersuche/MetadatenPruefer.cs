using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Sucht nach Urheber- und Herkunftsmarkierungen, die man dem Bild nicht ansieht:
    /// EXIF-Felder (Autor, Copyright, Software), XMP-Blöcke und C2PA/Content-Credentials.
    ///
    /// Abgrenzung: Das sind Markierungen in den <b>Dateimetadaten</b>. Wasserzeichen, die
    /// in die Pixel selbst eingerechnet sind (SynthID, Digimarc, Stable-Diffusion-Watermark),
    /// lassen sich damit nicht finden — dafür müsste man das jeweilige Verfahren kennen.
    /// </summary>
    internal static class MetadatenPruefer
    {
        /// <summary>Gefundene Hinweise, leer wenn die Datei metadatenfrei ist.</summary>
        internal static IReadOnlyList<string> Pruefe(string pfad)
        {
            var hinweise = new List<string>();

            LiesBitmapMetadaten(pfad, hinweise);
            SucheC2paSegment(pfad, hinweise);

            return hinweise;
        }

        /// <summary>EXIF- und XMP-Felder über die WPF-Bilddekoder auslesen.</summary>
        private static void LiesBitmapMetadaten(string pfad, List<string> hinweise)
        {
            try
            {
                using var fs = File.OpenRead(pfad);
                var frames = BitmapFrame.Create(
                    fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);

                if (frames.Metadata is not BitmapMetadata meta)
                    return;

                if (HatText(() => meta.Author?.FirstOrDefault()))
                    hinweise.Add("Autor: " + Kuerze(meta.Author!.First()));

                if (HatText(() => meta.Copyright))
                    hinweise.Add("Copyright: " + Kuerze(meta.Copyright!));

                if (HatText(() => meta.ApplicationName))
                    hinweise.Add("Software: " + Kuerze(meta.ApplicationName!));

                if (HatText(() => meta.Title))
                    hinweise.Add("Titel: " + Kuerze(meta.Title!));

                if (HatText(() => meta.Comment))
                    hinweise.Add(BeschreibeKommentar(meta.Comment!));

                // XMP wird u. a. von Lightroom/Photoshop und für Rechteangaben genutzt.
                if (Vorhanden(meta, "/xmp"))
                    hinweise.Add("XMP-Block vorhanden");

                // IPTC: klassische Agentur-/Rechtefelder.
                if (Vorhanden(meta, "/app13/irb/8bimiptc/iptc"))
                    hinweise.Add("IPTC-Block vorhanden");
            }
            catch
            {
                // Unlesbare oder untypische Datei – kein Hinweis, kein Fehler.
            }
        }

        /// <summary>
        /// C2PA („Content Credentials") liegt als JUMBF-Container im APP11-Segment.
        /// Das erkennen die WPF-Dekoder nicht, deshalb der direkte Blick in die Segmente.
        /// </summary>
        private static void SucheC2paSegment(string pfad, List<string> hinweise)
        {
            try
            {
                using var fs = File.OpenRead(pfad);

                // Nur der Kopfbereich ist interessant; C2PA steht vor den Bilddaten.
                int laenge = (int)Math.Min(fs.Length, 512 * 1024);
                var puffer = new byte[laenge];
                if (fs.Read(puffer, 0, laenge) <= 0)
                    return;

                if (EnthaeltZeichenfolge(puffer, "jumb") && EnthaeltZeichenfolge(puffer, "c2pa"))
                    hinweise.Add("C2PA-Herkunftsnachweis vorhanden");
            }
            catch
            {
                // ignorieren
            }
        }

        private static bool EnthaeltZeichenfolge(byte[] daten, string text)
        {
            var muster = System.Text.Encoding.ASCII.GetBytes(text);

            for (int i = 0; i <= daten.Length - muster.Length; i++)
            {
                bool passt = true;
                for (int j = 0; j < muster.Length; j++)
                {
                    if (daten[i + j] != muster[j]) { passt = false; break; }
                }

                if (passt)
                    return true;
            }

            return false;
        }

        private static bool HatText(Func<string?> lese)
        {
            try { return !string.IsNullOrWhiteSpace(lese()); }
            catch { return false; }
        }

        private static bool Vorhanden(BitmapMetadata meta, string abfrage)
        {
            try { return meta.GetQuery(abfrage) != null; }
            catch { return false; }
        }

        /// <summary>
        /// Beschreibt das Kommentarfeld, statt es abzuschreiben.
        ///
        /// Bildgeneratoren legen dort ihre kompletten Parameter ab – als JSON (SwarmUI,
        /// ComfyUI) oder als Fliesstext (Automatic1111). Angeschnitten gelesen stand in
        /// der Karte dann Quelltext zwischen lauter Klartext-Angaben wie „Autor: …".
        /// Gemeldet wird deshalb, <b>was</b> dort liegt und von welchem Programm.
        /// </summary>
        private static string BeschreibeKommentar(string wert)
        {
            string erzeuger = ErkenneErzeuger(wert);

            if (erzeuger.Length == 0)
                return "Kommentar: " + Kuerze(wert);

            string modell = LiesModell(wert);

            return modell.Length == 0
                ? $"KI-Generierungsdaten im Kommentar ({erzeuger})"
                : $"KI-Generierungsdaten im Kommentar ({erzeuger}) – Modell: {Kuerze(modell, 40)}";
        }

        /// <summary>
        /// Erkennt das erzeugende Programm an seinen Kennfeldern. Leer, wenn der
        /// Kommentar ein gewöhnlicher Kommentar ist.
        /// </summary>
        private static string ErkenneErzeuger(string wert)
        {
            if (wert.Contains("sui_image_params", StringComparison.OrdinalIgnoreCase))
                return "SwarmUI";

            if (wert.Contains("\"class_type\"", StringComparison.OrdinalIgnoreCase)
                || wert.Contains("\"workflow\"", StringComparison.OrdinalIgnoreCase))
                return "ComfyUI";

            if (wert.Contains("Negative prompt:", StringComparison.OrdinalIgnoreCase)
                || (wert.Contains("Steps:", StringComparison.OrdinalIgnoreCase)
                    && wert.Contains("Sampler:", StringComparison.OrdinalIgnoreCase)))
                return "Automatic1111";

            // Unbekanntes Werkzeug, aber eindeutig Generierungsdaten: JSON mit Prompt.
            if (wert.TrimStart().StartsWith('{')
                && wert.Contains("\"prompt\"", StringComparison.OrdinalIgnoreCase))
                return "Bildgenerator";

            return string.Empty;
        }

        /// <summary>
        /// Holt den Modellnamen aus den JSON-Parametern – die einzige Angabe daraus, die
        /// beim Sichten wirklich etwas beiträgt. Fehlt sie oder ist der Kommentar kein
        /// JSON, bleibt es beim blossen Programmnamen.
        /// </summary>
        private static string LiesModell(string wert)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(wert);

                var wurzel = doc.RootElement;
                if (wurzel.ValueKind != System.Text.Json.JsonValueKind.Object)
                    return string.Empty;

                if (wurzel.TryGetProperty("sui_image_params", out var parameter)
                    && parameter.ValueKind == System.Text.Json.JsonValueKind.Object)
                    wurzel = parameter;

                if (wurzel.TryGetProperty("model", out var modell)
                    && modell.ValueKind == System.Text.Json.JsonValueKind.String)
                    return modell.GetString() ?? string.Empty;
            }
            catch
            {
                // Kein oder beschädigtes JSON – dann eben ohne Modellnamen.
            }

            return string.Empty;
        }

        /// <summary>
        /// Kürzt und legt den Wert auf eine Zeile. Ohne das Zusammenziehen sprengte ein
        /// mehrzeiliger Eintrag die Liste, in der sonst je Zeile eine Markierung steht.
        /// </summary>
        private static string Kuerze(string wert, int max = 60)
        {
            wert = System.Text.RegularExpressions.Regex.Replace(wert, @"\s+", " ").Trim();
            return wert.Length <= max ? wert : wert[..max] + "…";
        }
    }
}
