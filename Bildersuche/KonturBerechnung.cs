using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Kantenbild nach dem Sobel-Operator – übernommen aus dem Grundprojekt
    /// BildKonturBerechnen (dort „Kontur A"). Bewusst dieselbe Rechnung, damit das
    /// Ergebnis genauso aussieht wie dort: Graustufen nach Luminanz, optionaler
    /// 3x3-Weichzeichner, Faltung, und am Ende eine harte Schwelle – jedes Pixel ist
    /// entweder Kante (weiss) oder nicht (schwarz).
    ///
    /// Gegenüber der Vorlage kommt der <see cref="CancellationToken"/> dazu: Hier
    /// läuft die Rechnung beim Schieben des Reglers immer wieder neu an, und der
    /// vorherige Durchlauf muss dann aussteigen können. Abgebrochen wird per
    /// Rückgabewert <c>null</c>, nicht per Ausnahme — beim Ziehen des Reglers ist der
    /// Abbruch der Normalfall und keine Ausnahmesituation.
    ///
    /// Die zweite Abweichung ist der Vergleich mit der Schwelle: <c>&gt;</c> statt
    /// <c>&gt;=</c>. Begründung an der Stelle selbst.
    /// </summary>
    internal static class KonturBerechnung
    {
        /// <summary>Voreinstellung des Schwellwerts, wie im Grundprojekt.</summary>
        internal const double SchwelleStandard = 90.0;

        /// <summary>
        /// Berechnet das Kantenbild.
        /// </summary>
        /// <param name="quelle">Eingangsbild in beliebigem Format.</param>
        /// <param name="schwelle">Schwellwert für die Gradientenstärke (0…255).</param>
        /// <param name="weichzeichnen">Vorher einen 3x3-Mittelwertfilter anwenden.</param>
        /// <returns>Das Kantenbild, oder <c>null</c> bei Abbruch bzw. zu kleinem Bild.</returns>
        internal static BitmapSource? Sobel(
            BitmapSource quelle, int schwelle, bool weichzeichnen, CancellationToken token)
        {
            if (quelle is null)
                return null;

            // 1) In ein einheitliches BGRA32-Format bringen, damit die Rohbytes
            //    zuverlässig auslesbar sind.
            var bgra = new FormatConvertedBitmap(quelle, PixelFormats.Bgra32, null, 0);
            int breite = bgra.PixelWidth;
            int hoehe = bgra.PixelHeight;
            if (breite < 3 || hoehe < 3)
                return null;

            int schritt = breite * 4;
            var pixel = new byte[hoehe * schritt];
            bgra.CopyPixels(pixel, schritt, 0);

            // 2) Graustufen nach der üblichen Luminanz-Gewichtung.
            var grau = new double[breite * hoehe];
            for (int y = 0; y < hoehe; y++)
            {
                for (int x = 0; x < breite; x++)
                {
                    int i = y * schritt + x * 4;
                    byte b = pixel[i];
                    byte g = pixel[i + 1];
                    byte r = pixel[i + 2];
                    grau[y * breite + x] = 0.299 * r + 0.587 * g + 0.114 * b;
                }
            }

            if (token.IsCancellationRequested)
                return null;

            // 3) Optionaler 3x3-Mittelwert-Weichzeichner gegen Rauschen.
            if (weichzeichnen)
                grau = Weichzeichnen(grau, breite, hoehe);

            if (token.IsCancellationRequested)
                return null;

            // 4) Sobel-Faltung: Gradient in x- und y-Richtung, dann Betrag.
            int[] gxK = { -1, 0, 1, -2, 0, 2, -1, 0, 1 };
            int[] gyK = { -1, -2, -1, 0, 0, 0, 1, 2, 1 };
            var ausgabe = new byte[hoehe * schritt];

            for (int y = 1; y < hoehe - 1; y++)
            {
                // Zeilenweise prüfen: oft genug, um zügig auszusteigen, selten genug,
                // um die innere Schleife nicht auszubremsen.
                if (token.IsCancellationRequested)
                    return null;

                for (int x = 1; x < breite - 1; x++)
                {
                    double gx = 0, gy = 0;
                    int k = 0;
                    for (int ky = -1; ky <= 1; ky++)
                    {
                        for (int kx = -1; kx <= 1; kx++)
                        {
                            double v = grau[(y + ky) * breite + (x + kx)];
                            gx += v * gxK[k];
                            gy += v * gyK[k];
                            k++;
                        }
                    }

                    double betrag = Math.Sqrt(gx * gx + gy * gy);

                    // Grösser, nicht grösser-gleich.
                    //
                    // Die Vorlage prüfte >=. Damit lieferte Schwelle 0 ein durchgehend
                    // weisses Bild: Eine glatte Fläche hat den Betrag 0, und 0 >= 0 gilt.
                    // Der unterste Reglerwert war damit nicht die feinste Einstellung,
                    // sondern gar keine.
                    //
                    // Mit > heisst Schwelle 0 „alles zeigen, was überhaupt ein Gefälle
                    // hat" — glatte Flächen bleiben schwarz. Bei allen anderen Werten
                    // unterscheiden sich die beiden Prüfungen nur für Pixel, deren Betrag
                    // die Schwelle exakt trifft.
                    byte wert = betrag > schwelle ? (byte)255 : (byte)0;

                    int i = y * schritt + x * 4;
                    ausgabe[i] = wert;       // B
                    ausgabe[i + 1] = wert;   // G
                    ausgabe[i + 2] = wert;   // R
                    ausgabe[i + 3] = 255;    // A
                }
            }

            var ergebnis = new WriteableBitmap(
                breite, hoehe, bgra.DpiX, bgra.DpiY, PixelFormats.Bgra32, null);

            ergebnis.WritePixels(new Int32Rect(0, 0, breite, hoehe), ausgabe, schritt, 0);

            // Einfrieren, damit das Bild vom Hintergrund-Task an die Oberfläche darf.
            ergebnis.Freeze();
            return ergebnis;
        }

        /// <summary>Einfacher 3x3-Mittelwert-Weichzeichner (Randpixel gemittelt).</summary>
        private static double[] Weichzeichnen(double[] quelle, int breite, int hoehe)
        {
            var ziel = new double[quelle.Length];

            for (int y = 0; y < hoehe; y++)
            {
                for (int x = 0; x < breite; x++)
                {
                    double summe = 0;
                    int anzahl = 0;

                    for (int ky = -1; ky <= 1; ky++)
                    {
                        int yy = y + ky;
                        if (yy < 0 || yy >= hoehe) continue;

                        for (int kx = -1; kx <= 1; kx++)
                        {
                            int xx = x + kx;
                            if (xx < 0 || xx >= breite) continue;

                            summe += quelle[yy * breite + xx];
                            anzahl++;
                        }
                    }

                    ziel[y * breite + x] = summe / anzahl;
                }
            }

            return ziel;
        }
    }
}
