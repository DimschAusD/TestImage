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
                    hinweise.Add("Autor: " + meta.Author!.First());

                if (HatText(() => meta.Copyright))
                    hinweise.Add("Copyright: " + Kuerze(meta.Copyright!));

                if (HatText(() => meta.ApplicationName))
                    hinweise.Add("Software: " + Kuerze(meta.ApplicationName!));

                if (HatText(() => meta.Title))
                    hinweise.Add("Titel: " + Kuerze(meta.Title!));

                if (HatText(() => meta.Comment))
                    hinweise.Add("Kommentar: " + Kuerze(meta.Comment!));

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

        private static string Kuerze(string wert, int max = 60)
        {
            wert = wert.Trim();
            return wert.Length <= max ? wert : wert[..max] + "…";
        }
    }
}
