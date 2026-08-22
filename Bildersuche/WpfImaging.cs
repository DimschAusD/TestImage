using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMatching.Core;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Brücke zwischen WPF und der framework-unabhängigen Kernbibliothek
    /// (ImageMatching.Core): lädt/konvertiert Bilder in <see cref="GrayImage"/>
    /// bzw. <see cref="RgbImage"/>. Alles darunter (Sobel, Fingerabdruck, Index,
    /// CLIP) kennt kein WPF und bleibt wiederverwendbar.
    /// </summary>
    public static class WpfImaging
    {
        /// <summary>
        /// Liest nur den Dateikopf und liefert die Abmessungen, ohne Bildpunkte
        /// auszupacken. <c>(0, 0)</c>, wenn die Datei nicht lesbar ist.
        ///
        /// Der eigene <see cref="FileStream"/> ist Absicht: Ein Decoder auf einem URI
        /// hält die Datei offen, bis der Sammler ihn abräumt — und diese Anwendung
        /// verschiebt und löscht Bilder laufend.
        /// </summary>
        private static (int Breite, int Hoehe) LiesAbmessungen(string path)
        {
            try
            {
                using FileStream strom = File.OpenRead(path);
                BitmapDecoder decoder = BitmapDecoder.Create(
                    strom,
                    BitmapCreateOptions.DelayCreation,
                    BitmapCacheOption.None);

                BitmapFrame rahmen = decoder.Frames[0];
                return (rahmen.PixelWidth, rahmen.PixelHeight);
            }
            catch (Exception)
            {
                // Unlesbar oder unbekanntes Format: dann eben wie bisher voll auspacken,
                // die reguläre Ladestrecke meldet den Fehler gleich danach.
                return (0, 0);
            }
        }

        /// <summary>
        /// Setzt die Zielgrösse am Decoder, damit gar nicht erst voll ausgepackt wird.
        ///
        /// Ohne das packt der JPEG-Decoder bei einem 4000 x 3000 alle zwölf Millionen
        /// Bildpunkte aus, und erst danach wirft ein TransformedBitmap 98 % davon weg.
        /// Mit gesetzter Zielgrösse verkleinert schon der Decoder — bei JPEG in der
        /// Frequenzdarstellung, das ist ein Vielfaches billiger.
        ///
        /// Nur die längere Kante wird begrenzt, damit das Seitenverhältnis bleibt, und
        /// nur nach unten: Ein bereits kleines Bild darf der Decoder nicht aufblasen.
        /// </summary>
        private static void SetzeZielgroesse(BitmapImage bmp, string path, int maxSize)
        {
            (int breite, int hoehe) = LiesAbmessungen(path);

            if (breite <= 0 || hoehe <= 0 || Math.Max(breite, hoehe) <= maxSize)
            {
                return;
            }

            if (breite >= hoehe)
            {
                bmp.DecodePixelWidth = maxSize;
            }
            else
            {
                bmp.DecodePixelHeight = maxSize;
            }
        }

        /// <summary>Lädt eine Bilddatei und liefert sie als Graustufenbild.</summary>
        public static GrayImage LoadGray(string path, int maxSize = 256)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // Datei nicht gesperrt halten
            bmp.UriSource = new Uri(path);
            SetzeZielgroesse(bmp, path, maxSize);
            bmp.EndInit();
            bmp.Freeze();
            return ToGray(bmp, maxSize);
        }

        /// <summary>
        /// Wandelt ein WPF-Bild in ein Graustufenbild um und skaliert es dabei
        /// auf eine handliche Arbeitsgröße herunter (der Fingerabdruck ist
        /// skalierungsinvariant, kleinere Bilder beschleunigen das Indexieren).
        /// </summary>
        public static GrayImage ToGray(BitmapSource src, int maxSize = 256)
        {
            double scale = Math.Min(1.0, (double)maxSize / Math.Max(src.PixelWidth, src.PixelHeight));

            BitmapSource work = src;
            if (scale < 1.0)
                work = new TransformedBitmap(src, new ScaleTransform(scale, scale));

            var gray = new FormatConvertedBitmap(work, PixelFormats.Gray8, null, 0);
            int w = gray.PixelWidth, h = gray.PixelHeight;
            int stride = (w * 8 + 7) / 8;
            byte[] buf = new byte[h * stride];
            gray.CopyPixels(buf, stride, 0);

            var px = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = buf[y * stride + x] / 255f;

            return new GrayImage(w, h, px);
        }

        /// <summary>Lädt eine Bilddatei als Farbbild (RGB) für den CNN-Weg.</summary>
        public static RgbImage LoadRgb(string path, int maxSize = 512)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            SetzeZielgroesse(bmp, path, maxSize);
            bmp.EndInit();
            bmp.Freeze();
            return ToRgb(bmp, maxSize);
        }

        /// <summary>Wandelt ein WPF-Bild in ein RGB-Bild um (auf maxSize begrenzt).</summary>
        public static RgbImage ToRgb(BitmapSource src, int maxSize = 512)
        {
            double scale = Math.Min(1.0, (double)maxSize / Math.Max(src.PixelWidth, src.PixelHeight));

            BitmapSource work = src;
            if (scale < 1.0)
                work = new TransformedBitmap(src, new ScaleTransform(scale, scale));

            var bgra = new FormatConvertedBitmap(work, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            int stride = w * 4;
            byte[] buf = new byte[h * stride];
            bgra.CopyPixels(buf, stride, 0);

            var px = new byte[w * h * 3];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int s = y * stride + x * 4; // BGRA
                    int d = (y * w + x) * 3;    // RGB
                    px[d] = buf[s + 2];     // R
                    px[d + 1] = buf[s + 1]; // G
                    px[d + 2] = buf[s];     // B
                }
            }
            return new RgbImage(w, h, px);
        }
    }
}
