using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Farbsignatur eines Bildes: mehrere Mittelwerte über waagerechte Bänder, von oben
    /// nach unten als senkrechter Verlauf.
    ///
    /// Ein einzelner Mittelwert über das ganze Bild ergibt bei buntem Motiv fast immer
    /// ein müdes Graubraun, weil sich gegenüberliegende Farben auslöschen. Bandweise
    /// gemittelt bleibt die Aufteilung des Bildes erhalten – heller Himmel oben, Hautton
    /// in der Mitte, dunkler Rand unten – und das bei gleichem Aufwand: derselbe eine
    /// Durchlauf, nur mehrere Summen statt einer.
    ///
    /// Gedacht für das 100-px-Vorschaubild der Ladestrecke. Auf dem vollen Bild zu
    /// rechnen bringt kein besseres Ergebnis, kostet aber das Vielfache.
    /// </summary>
    internal static class Farbsignatur
    {
        /// <summary>Voreinstellung: genug Bänder, um die Aufteilung zu zeigen, ohne zu flimmern.</summary>
        internal const int BaenderStandard = 8;

        /// <summary>
        /// Baut den Verlauf. Der Pinsel ist eingefroren und darf damit aus einem
        /// Hintergrund-Task an die Oberfläche gereicht werden.
        /// </summary>
        /// <param name="vorschau">Kleines Vorschaubild, eingefroren.</param>
        /// <param name="baender">Anzahl der waagerechten Bänder.</param>
        /// <returns>Der Verlauf, oder <c>null</c> wenn das Bild unbrauchbar ist.</returns>
        internal static LinearGradientBrush? Erstelle(BitmapSource? vorschau, int baender = BaenderStandard)
        {
            if (vorschau is null)
                return null;

            try
            {
                var bgra = new FormatConvertedBitmap(vorschau, PixelFormats.Bgra32, null, 0);

                int breite = bgra.PixelWidth;
                int hoehe = bgra.PixelHeight;
                if (breite < 1 || hoehe < 1)
                    return null;

                // Nicht mehr Bänder als Zeilen – sonst blieben Bänder ohne Bildpunkte.
                if (baender > hoehe)
                    baender = hoehe;
                if (baender < 1)
                    return null;

                int schritt = breite * 4;
                var roh = new byte[hoehe * schritt];
                bgra.CopyPixels(roh, schritt, 0);

                var stopps = new GradientStopCollection(baender);
                Color letzte = Colors.Transparent;
                bool hatFarbe = false;

                for (int band = 0; band < baender; band++)
                {
                    int y0 = band * hoehe / baender;
                    int y1 = (band + 1) * hoehe / baender;   // ausschliesslich
                    if (y1 <= y0)
                        y1 = y0 + 1;

                    double sumR = 0, sumG = 0, sumB = 0, sumA = 0;

                    for (int y = y0; y < y1 && y < hoehe; y++)
                    {
                        int zeile = y * schritt;
                        for (int x = 0; x < breite; x++)
                        {
                            int i = zeile + x * 4;
                            byte a = roh[i + 3];

                            // Voll durchsichtige Punkte tragen keine Farbe. Ohne diese
                            // Prüfung zöge jedes PNG mit Freisteller nach Schwarz.
                            if (a == 0)
                                continue;

                            sumB += roh[i] * a;
                            sumG += roh[i + 1] * a;
                            sumR += roh[i + 2] * a;
                            sumA += a;
                        }
                    }

                    Color farbe;
                    if (sumA > 0)
                    {
                        farbe = Color.FromRgb(
                            (byte)Math.Clamp(sumR / sumA, 0, 255),
                            (byte)Math.Clamp(sumG / sumA, 0, 255),
                            (byte)Math.Clamp(sumB / sumA, 0, 255));

                        letzte = farbe;
                        hatFarbe = true;
                    }
                    else
                    {
                        // Durchgehend durchsichtiges Band: die vorige Farbe fortführen,
                        // damit im Verlauf kein schwarzer Riss entsteht.
                        farbe = letzte;
                    }

                    // Stopp in die Bandmitte: Dort gilt der Mittelwert wirklich, und
                    // zwischen den Mitten blendet WPF sauber über.
                    stopps.Add(new GradientStop(farbe, (band + 0.5) / baender));
                }

                if (!hatFarbe)
                    return null;   // komplett durchsichtiges Bild – lieber gar nichts zeigen

                var pinsel = new LinearGradientBrush(stopps)
                {
                    StartPoint = new Point(0.5, 0),
                    EndPoint = new Point(0.5, 1)
                };

                pinsel.Freeze();
                return pinsel;
            }
            catch
            {
                // Unlesbares oder ungewöhnliches Format – dann bleibt der Streifen leer.
                return null;
            }
        }
    }
}
