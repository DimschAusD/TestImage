using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Misst, ob ein Bild etwas taugt — nicht, ob die Datei heil ist.
    ///
    /// Die bisherigen Prüfungen der Ampel beantworten alle dieselbe Frage: lässt sich
    /// die Datei überhaupt lesen. Hier geht es um die andere: lohnt sich das Bild.
    ///
    /// <b>Alle vier Messungen sind praktisch umsonst.</b> Zwei kommen aus Dateigrösse
    /// und Abmessungen, also aus Angaben, die ohnehin schon gelesen wurden. Die beiden
    /// anderen rechnen auf Bildern, die zum Anzeigen sowieso dekodiert werden — das
    /// 100-Pixel-Vorschaubild und das Bild in Anzeigegrösse. Kein zusätzlicher
    /// Dateizugriff, kein zusätzliches Dekodieren.
    /// </summary>
    internal static class Bildbewertung
    {
        /// <summary>
        /// Dateigrösse geteilt durch Pixelzahl.
        ///
        /// Ein niedriger Wert heisst: für so viele Bildpunkte steht zu wenig Information
        /// in der Datei — also stark komprimiert und entsprechend matschig. Das trifft
        /// einen Wegwerfgrund, den die CLIP-Vektoren nicht sehen: Für die ist ein
        /// matschiges und ein sauberes Bild derselben Szene fast dasselbe.
        ///
        /// <c>0</c>, wenn sich nichts rechnen lässt.
        /// </summary>
        internal static double ByteJePixel(string pfad, int breite, int hoehe)
        {
            if (breite <= 0 || hoehe <= 0)
            {
                return 0;
            }

            try
            {
                long groesse = new FileInfo(pfad).Length;
                return groesse / (double)breite / hoehe;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Streuung der Helligkeit über das Vorschaubild, 0 … 1.
        ///
        /// Nahe null heisst: fast einfarbig. Das sind Platzhalter, gespeicherte
        /// Fehlerseiten oder versehentlich abgelegte Farbflächen — Dateien, die wie
        /// Bilder aussehen, aber keines sind.
        ///
        /// Gerechnet auf dem 100-Pixel-Vorschaubild, das für die Anzeige und die
        /// Farbsignatur ohnehin entsteht. Für diese Frage reicht die Auflösung: Eine
        /// leere Fläche bleibt auch verkleinert leer.
        /// </summary>
        internal static double Farbstreuung(BitmapSource? vorschau)
        {
            var werte = Graustufen(vorschau);
            if (werte is null || werte.Length == 0)
            {
                return 0;
            }

            double summe = 0;
            for (int i = 0; i < werte.Length; i++)
            {
                summe += werte[i];
            }

            double mittel = summe / werte.Length;

            double quadrate = 0;
            for (int i = 0; i < werte.Length; i++)
            {
                double d = werte[i] - mittel;
                quadrate += d * d;
            }

            return Math.Sqrt(quadrate / werte.Length) / 255.0;
        }

        /// <summary>
        /// Schärfe: mittlerer Unterschied benachbarter Bildpunkte, geteilt durch die
        /// Streuung des ganzen Bildes.
        ///
        /// <b>Warum das Verhältnis und nicht der Unterschied allein:</b> Ein flaues,
        /// kontrastarmes Bild hat überall kleine Unterschiede, ist aber nicht unscharf.
        /// Erst im Verhältnis zur Gesamtstreuung trennt sich „wenig Kontrast" von
        /// „keine Kanten".
        ///
        /// <b>Warum nicht Sobel</b>, obwohl es im Projekt liegt: Sobel bräuchte die
        /// Umwandlung von zwei Millionen Bildpunkten nach float und zwei Faltungen. Der
        /// waagerechte Nachbarunterschied kostet einen Durchlauf über Bytes und
        /// beantwortet dieselbe Frage.
        ///
        /// <b>Grenze:</b> Gemessen wird am Bild in <i>Anzeigegrösse</i>, nicht am
        /// Original. Eine Unschärfe, die feiner ist als die Verkleinerung, verschwindet
        /// dabei. Genau der gemeldete Fall — riesige Abmessungen, in Wahrheit ein
        /// hochskaliertes kleines Bild — überlebt die Verkleinerung aber, weil die
        /// Unschärfe dort über viele Bildpunkte geht.
        /// </summary>
        internal static double Kantendichte(BitmapSource? bild)
        {
            if (bild is null)
            {
                return 0;
            }

            var grau = GraustufenMitBreite(bild, out int breite, out int hoehe);
            if (grau is null || breite < 3 || hoehe < 3)
            {
                return 0;
            }

            double summe = 0;
            long n = 0;

            for (int y = 0; y < hoehe; y++)
            {
                int zeile = y * breite;
                for (int x = 1; x < breite; x++)
                {
                    summe += Math.Abs(grau[zeile + x] - grau[zeile + x - 1]);
                    n++;
                }
            }

            if (n == 0)
            {
                return 0;
            }

            double kante = summe / n;

            // Gesamtstreuung als Bezugsgrösse
            double s = 0;
            for (int i = 0; i < grau.Length; i++)
            {
                s += grau[i];
            }

            double mittel = s / grau.Length;

            double q = 0;
            for (int i = 0; i < grau.Length; i++)
            {
                double d = grau[i] - mittel;
                q += d * d;
            }

            double streuung = Math.Sqrt(q / grau.Length);
            return streuung < 1.0 ? 0 : kante / streuung;
        }

        private static byte[]? Graustufen(BitmapSource? quelle)
            => GraustufenMitBreite(quelle, out _, out _);

        private static byte[]? GraustufenMitBreite(BitmapSource? quelle, out int breite, out int hoehe)
        {
            breite = 0;
            hoehe = 0;

            if (quelle is null)
            {
                return null;
            }

            try
            {
                var grau = new FormatConvertedBitmap(quelle, PixelFormats.Gray8, null, 0);

                breite = grau.PixelWidth;
                hoehe = grau.PixelHeight;
                if (breite <= 0 || hoehe <= 0)
                {
                    return null;
                }

                // Gray8 ist zeilenweise auf 4 Byte ausgerichtet – ohne das Zusammenziehen
                // liefen die Auffüllbytes in die Rechnung ein.
                int schritt = (breite + 3) / 4 * 4;
                var roh = new byte[schritt * hoehe];
                grau.CopyPixels(roh, schritt, 0);

                if (schritt == breite)
                {
                    return roh;
                }

                var dicht = new byte[breite * hoehe];
                for (int y = 0; y < hoehe; y++)
                {
                    Array.Copy(roh, y * schritt, dicht, y * breite, breite);
                }

                return dicht;
            }
            catch
            {
                return null;
            }
        }
    }
}
