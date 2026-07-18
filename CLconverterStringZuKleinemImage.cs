using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestImage
{
    internal class CLconverterStringZuKleinemImage : IValueConverter
    {
        private static readonly ConcurrentDictionary<string, BitmapImage> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        public static void InvalidateCache(string pfad)
        {
            if (!string.IsNullOrEmpty(pfad))
            {
                _cache.TryRemove(pfad, out _);
            }
        }

        /// <summary>
        /// Gemeinsamer 120px-Thumbnail-Cache – auch von der Schnell-Liste genutzt.
        /// </summary>
        internal static bool TryHoleAusCache(string pfad, out BitmapImage bmp)
            => _cache.TryGetValue(pfad, out bmp!);

        /// <summary>
        /// Legt ein extern (z. B. von der Schnell-Liste) dekodiertes Thumbnail in den Cache.
        /// </summary>
        internal static void LegeInCache(string pfad, BitmapImage bmp)
        {
            if (!string.IsNullOrEmpty(pfad) && bmp != null)
            {
                _cache[pfad] = bmp;
            }
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string pfad || string.IsNullOrEmpty(pfad))
            {
                return MachePlatzhalter();
            }

            if (_cache.TryGetValue(pfad, out var cached))
            {
                return cached;
            }

            BitmapImage bmp = new BitmapImage();
            try
            {
                bmp.BeginInit();
                bmp.UriSource = new Uri(pfad, UriKind.Relative);
                bmp.DecodePixelWidth = 120;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                _cache[pfad] = bmp;
                return bmp;
            }
            catch
            {
                return MachePlatzhalter();
            }
        }

        /// <summary>
        /// Erzeugt ein Platzhalter-Bild für den Fall, dass kein Pfad vorliegt oder das
        /// Bild nicht geladen werden kann: ein 200×200 transparentes Feld mit gelbem
        /// Rahmen. Wird nicht in den Cache gelegt (nur echte Thumbnails werden gecacht).
        /// </summary>
        private static RenderTargetBitmap MachePlatzhalter()
        {
            int width = 200, height = 200;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(0, 0, width, height));
                var pen = new System.Windows.Media.Pen { Thickness = 3, Brush = System.Windows.Media.Brushes.Yellow };
                dc.DrawRectangle(null, pen, new Rect(1.5, 1.5, width - 3, height - 3));
            }
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            return rtb;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
