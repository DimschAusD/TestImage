using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Ein Wort, das die Windows-OCR im ganzen Bild gefunden hat — in Bildpunkten des
    /// Originals. Der Kasten ist achsenparallel; die Engine kennt je Bild nur einen
    /// Textwinkel und dreht ihn nicht mit.
    /// </summary>
    internal sealed record OcrWort(string Text, double X, double Y, double Breite, double Hoehe);

    /// <summary>
    /// Das Ergebnis der Standardsuche: alle Wörter mit ihrer Lage, der Text am Stück
    /// und der Textwinkel, den die Engine für das Bild angenommen hat.
    /// </summary>
    internal sealed class OcrWortBefund
    {
        /// <summary>Die gefundenen Wörter, in der Reihenfolge der Engine.</summary>
        public required IReadOnlyList<OcrWort> Woerter { get; init; }

        /// <summary>Derselbe Text, den die Standardsuche in den Cache legt.</summary>
        public required string Text { get; init; }

        /// <summary>
        /// Der angenommene Textwinkel in Grad, <c>null</c>, wenn die Engine keinen
        /// erkannt hat. <b>Ein Wert für das ganze Bild</b> — daran endet, was sie auf
        /// einer Karte finden kann.
        /// </summary>
        public required double? TextWinkel { get; init; }

        /// <summary>Wie lange der Lauf gedauert hat.</summary>
        public required TimeSpan Dauer { get; init; }
    }

    /// <summary>
    /// Macht aus den Wörtern der Standardsuche dieselben Anzeigefunde, die auch das
    /// Plugin liefert — damit beide Ebenen in derselben Ansicht, mit derselben
    /// Hervorhebung und demselben Streifen unter dem Bild liegen können.
    ///
    /// <b>Auch hier ein Streifen.</b> Bei einem Wort ist er nur ausgeschnitten und
    /// vergrössert, nicht geradegerückt — aber genau das ist der Vergleich: Man sieht
    /// nebeneinander, mit welcher Auflösung die Standardsuche gearbeitet hat und was
    /// das Plugin derselben Stelle vorgelegt hätte.
    /// </summary>
    internal static class OcrWortAnsicht
    {
        /// <summary>Höhe, auf die der Ausschnitt gebracht wird — wie beim Plugin.</summary>
        private const double ZielHoehe = 48;

        private const double MaxFaktor = 6.0;

        /// <summary>Luft ringsum, als Anteil der Worthöhe. Ohne sie klebt die Schrift am Rand.</summary>
        private const double Luft = 0.35;

        internal static List<OcrZeilenFund> Baue(OcrWortBefund befund, BitmapSource bild)
        {
            var funde = new List<OcrZeilenFund>(befund.Woerter.Count);
            int nummer = 0;

            foreach (OcrWort wort in befund.Woerter)
            {
                double luft = wort.Hoehe * Luft;

                Int32Rect kasten = Begrenzt(
                    wort.X - luft,
                    wort.Y - luft,
                    wort.Breite + 2 * luft,
                    wort.Hoehe + 2 * luft,
                    bild.PixelWidth,
                    bild.PixelHeight);

                if (kasten.Width <= 0 || kasten.Height <= 0)
                {
                    continue;
                }

                funde.Add(new OcrZeilenFund
                {
                    Nummer = ++nummer,
                    Herkunft = "Windows-OCR",
                    Text = wort.Text,
                    Ecken = Ecken(wort),
                    Streifen = Streifen(bild, kasten),

                    // Der Winkel des ganzen Bildes, nicht des Wortes: Etwas anderes
                    // gibt die Engine nicht her.
                    Winkel = befund.TextWinkel ?? 0,
                    Zeichen = wort.Text.Length,
                    Laenge = wort.Breite,
                    MittlereHoehe = wort.Hoehe
                });
            }

            return funde;
        }

        /// <summary>Die vier Ecken des Kastens, im Uhrzeigersinn ab oben links.</summary>
        private static PointCollection Ecken(OcrWort wort)
        {
            var ecken = new PointCollection(4)
            {
                new Point(wort.X, wort.Y),
                new Point(wort.X + wort.Breite, wort.Y),
                new Point(wort.X + wort.Breite, wort.Y + wort.Hoehe),
                new Point(wort.X, wort.Y + wort.Hoehe)
            };

            ecken.Freeze();
            return ecken;
        }

        /// <summary>
        /// Schneidet den Kasten aus und vergrössert ihn auf eine lesbare Höhe.
        ///
        /// <see cref="CroppedBitmap"/> und <see cref="TransformedBitmap"/> rechnen
        /// nichts aus, sie merken sich nur Quelle und Auftrag — der Ausschnitt kostet
        /// also fast nichts, bis er gezeichnet wird.
        /// </summary>
        private static BitmapSource Streifen(BitmapSource bild, Int32Rect kasten)
        {
            var ausschnitt = new CroppedBitmap(bild, kasten);
            ausschnitt.Freeze();

            double faktor = Math.Clamp(ZielHoehe / Math.Max(1, kasten.Height), 1.0, MaxFaktor);

            if (faktor <= 1.0)
            {
                return ausschnitt;
            }

            var wandlung = new ScaleTransform(faktor, faktor);
            wandlung.Freeze();

            var gross = new TransformedBitmap(ausschnitt, wandlung);
            gross.Freeze();

            return gross;
        }

        /// <summary>
        /// Auf ganze Bildpunkte innerhalb des Bildes beschneiden. Ohne das wirft
        /// <see cref="CroppedBitmap"/>, sobald ein Wort am Rand liegt und die Luft
        /// darüber hinausreicht.
        /// </summary>
        private static Int32Rect Begrenzt(double x, double y, double breite, double hoehe, int maxBreite, int maxHoehe)
        {
            int links = (int)Math.Floor(Math.Clamp(x, 0, maxBreite));
            int oben = (int)Math.Floor(Math.Clamp(y, 0, maxHoehe));
            int rechts = (int)Math.Ceiling(Math.Clamp(x + breite, 0, maxBreite));
            int unten = (int)Math.Ceiling(Math.Clamp(y + hoehe, 0, maxHoehe));

            return new Int32Rect(links, oben, Math.Max(0, rechts - links), Math.Max(0, unten - oben));
        }
    }
}
