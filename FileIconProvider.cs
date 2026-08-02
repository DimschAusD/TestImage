using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestImage
{
    /// <summary>
    /// Liefert das Windows-Dateisymbol zu einem Pfad, eingefroren und damit
    /// thread-übergreifend nutzbar (Hintergrund → UI).
    ///
    /// Übernommen aus dem Projekt ArbeitDocfetcherNachbau2, hier ergänzt um einen
    /// Zwischenspeicher je Dateiendung: Alle PDFs teilen sich dasselbe Symbol, ebenso
    /// alle JPGs. Ohne diesen Cache würde für jede der womöglich tausenden Dateien
    /// einzeln auf die Platte zugegriffen — auf einer HDD der Bremsklotz schlechthin.
    /// </summary>
    internal static class FileIconProvider
    {
        /// <summary>Symbol je Endung (".pdf" → Symbol). Endungslose Dateien unter "".</summary>
        private static readonly ConcurrentDictionary<string, ImageSource?> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Symbol zur Datei. Der erste Aufruf je Endung liest von der Platte, alle
        /// weiteren kommen aus dem Zwischenspeicher. Null, wenn nichts zu holen ist.
        /// </summary>
        internal static ImageSource? HoleIcon(string? dateiPfad)
        {
            if (string.IsNullOrWhiteSpace(dateiPfad))
                return null;

            string endung;
            try { endung = Path.GetExtension(dateiPfad) ?? string.Empty; }
            catch { return null; }

            // Ausführbare Dateien tragen ihr eigenes Symbol – die dürfen nicht über
            // die Endung zusammengefasst werden.
            bool eigenesSymbol =
                endung.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                endung.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
                endung.Equals(".lnk", StringComparison.OrdinalIgnoreCase);

            if (eigenesSymbol)
                return LadeIcon(dateiPfad);

            return _cache.GetOrAdd(endung, _ => LadeIcon(dateiPfad));
        }

        /// <summary>Leert den Zwischenspeicher (z. B. nach einem Themenwechsel).</summary>
        internal static void CacheLeeren() => _cache.Clear();

        private static ImageSource? LadeIcon(string dateiPfad)
        {
            if (!File.Exists(dateiPfad))
                return null;

            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(dateiPfad);
                if (icon is null)
                    return null;

                var bild = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(16, 16));

                bild.Freeze();   // thread-sicher weitergeben
                return bild;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return null;
            }
        }
    }
}
