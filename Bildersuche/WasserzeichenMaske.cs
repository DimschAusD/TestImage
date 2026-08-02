using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestImage.Bildersuche
{
    /// <summary>Vorverarbeitung, mit der Muster und Prüfbild aufbereitet werden.</summary>
    public enum WasserzeichenVorverarbeitung
    {
        /// <summary>Lokalen Mittelwert abziehen (Box-Hochpass).</summary>
        Hochpass = 0,

        /// <summary>
        /// Sobel-Kantenstärke aus dem Grundprojekt.
        ///
        /// Naheliegend, weil das Wasserzeichen aus harten geometrischen Kanten besteht —
        /// gemessen aber schlechter: Anime-Illustrationen sind Linienzeichnungen und
        /// damit selbst kantenreich. Das hebt zwar das Wasserzeichen an, die unmarkierten
        /// Bilder aber ebenso, und die Mengen überlappen. Nur zum Vergleichen behalten,
        /// nicht als Standard.
        /// </summary>
        Kanten = 1
    }

    /// <summary>
    /// Erkennt aufgeprägte (sichtbare) Wasserzeichen, die immer an derselben Stelle
    /// sitzen — etwa das DeviantArt-Logo auf Vorschaubildern.
    ///
    /// Verfahren:
    /// 1. Aus jedem Bild einen quadratischen Mittelausschnitt schneiden, dessen Kante
    ///    ein fester Anteil der kürzeren Bildkante ist. Dadurch liegt das Wasserzeichen
    ///    unabhängig vom Seitenverhältnis immer an derselben Stelle im Ausschnitt.
    /// 2. Hochpass: den lokalen Mittelwert abziehen. Das entfernt den grossflächigen
    ///    Bildinhalt und lässt die feinen Kanten des Wasserzeichens stehen. Zugleich
    ///    macht es die Erkennung gegen Helligkeit und Kontrast unempfindlich — nötig,
    ///    weil das Wasserzeichen auf hellen Motiven kaum sichtbar ist.
    /// 3. Lernen: über viele Bilder mitteln. Der Bildinhalt ist verschieden und mittelt
    ///    sich weg, das immer gleiche Wasserzeichen bleibt stehen.
    /// 4. Prüfen: normierte Kreuzkorrelation (Pearson) gegen dieses Muster.
    /// </summary>
    public sealed class WasserzeichenMaske
    {
        /// <summary>Arbeitsauflösung des normierten Ausschnitts.</summary>
        public const int Kante = 96;

        /// <summary>Anteil der kürzeren Bildkante, der als Mittelausschnitt dient.</summary>
        private const double AusschnittAnteil = 0.45;

        /// <summary>Radius des Box-Filters für den lokalen Mittelwert.</summary>
        private const int HochpassRadius = 6;

        private readonly float[] _muster;   // Kante*Kante, gefiltert und mittelwertfrei

        public int Grundmenge { get; }

        /// <summary>Vorverarbeitung, mit der dieses Muster gelernt wurde. Beim Prüfen muss dieselbe laufen.</summary>
        public WasserzeichenVorverarbeitung Modus { get; }

        private WasserzeichenMaske(float[] muster, int grundmenge, WasserzeichenVorverarbeitung modus)
        {
            _muster = muster;
            Grundmenge = grundmenge;
            Modus = modus;
        }

        #region Lernen

        /// <summary>
        /// Lernt das Muster aus Bildern, die alle dasselbe Wasserzeichen tragen.
        /// Je mehr unterschiedliche Motive, desto sauberer wird die Maske.
        /// </summary>
        public static WasserzeichenMaske? Lerne(
            IEnumerable<string> dateien,
            IProgress<(int Erledigt, int Gesamt)>? fortschritt = null,
            CancellationToken token = default,
            WasserzeichenVorverarbeitung modus = WasserzeichenVorverarbeitung.Hochpass)
        {
            var liste = new List<string>(dateien);
            if (liste.Count < 5)
                return null;   // zu wenige Beispiele, das Muster bliebe verrauscht

            var summe = new double[Kante * Kante];
            int gezaehlt = 0;

            for (int i = 0; i < liste.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var feld = LadeMerkmalsfeld(liste[i], modus);
                if (feld is null)
                    continue;

                for (int p = 0; p < summe.Length; p++)
                    summe[p] += feld[p];

                gezaehlt++;
                fortschritt?.Report((i + 1, liste.Count));
            }

            if (gezaehlt < 5)
                return null;

            var muster = new float[summe.Length];
            for (int p = 0; p < muster.Length; p++)
                muster[p] = (float)(summe[p] / gezaehlt);

            ZentriereAufMittelwert(muster);
            return new WasserzeichenMaske(muster, gezaehlt, modus);
        }

        #endregion

        #region Prüfen

        /// <summary>
        /// Übereinstimmung eines Bildes mit dem gelernten Muster, −1 … +1.
        /// Werte nahe 0 bedeuten „kein Wasserzeichen", deutlich positive Werte
        /// sprechen dafür. Liefert 0, wenn das Bild nicht lesbar ist.
        /// </summary>
        public float Pruefe(string bildPfad)
        {
            var feld = LadeMerkmalsfeld(bildPfad, Modus);
            return feld is null ? 0f : Korrelation(_muster, feld);
        }

        /// <summary>Wie <see cref="Pruefe(string)"/>, aber für ein bereits geladenes Bild.</summary>
        public float Pruefe(BitmapSource bild)
        {
            var feld = MacheMerkmalsfeld(bild, Modus);
            return feld is null ? 0f : Korrelation(_muster, feld);
        }

        /// <summary>
        /// Pearson-Korrelation zweier gleich langer, mittelwertfreier Felder.
        /// Normiert, damit Helligkeit und Kontrast des Motivs keine Rolle spielen.
        /// </summary>
        private static float Korrelation(float[] a, float[] b)
        {
            if (a.Length != b.Length || a.Length == 0)
                return 0f;

            double ma = 0, mb = 0;
            for (int i = 0; i < a.Length; i++) { ma += a[i]; mb += b[i]; }
            ma /= a.Length; mb /= b.Length;

            double zaehler = 0, qa = 0, qb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double da = a[i] - ma, db = b[i] - mb;
                zaehler += da * db;
                qa += da * da;
                qb += db * db;
            }

            double nenner = Math.Sqrt(qa) * Math.Sqrt(qb);
            return nenner < 1e-9 ? 0f : (float)Math.Clamp(zaehler / nenner, -1.0, 1.0);
        }

        #endregion

        #region Bildaufbereitung

        private static float[]? LadeMerkmalsfeld(string pfad, WasserzeichenVorverarbeitung modus)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // Datei nicht gesperrt halten
                bmp.UriSource = new Uri(pfad);
                bmp.EndInit();
                bmp.Freeze();

                return MacheMerkmalsfeld(bmp, modus);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Mittelausschnitt schneiden, auf Arbeitsgrösse bringen, Filter anwenden.</summary>
        private static float[]? MacheMerkmalsfeld(BitmapSource quelle, WasserzeichenVorverarbeitung modus)
        {
            try
            {
                int kurz = Math.Min(quelle.PixelWidth, quelle.PixelHeight);
                int s = (int)(kurz * AusschnittAnteil);
                if (s < 8)
                    return null;

                int x = (quelle.PixelWidth - s) / 2;
                int y = (quelle.PixelHeight - s) / 2;

                var ausschnitt = new CroppedBitmap(quelle, new Int32Rect(x, y, s, s));

                double skala = (double)Kante / s;
                BitmapSource klein = skala < 1.0
                    ? new TransformedBitmap(ausschnitt, new ScaleTransform(skala, skala))
                    : ausschnitt;

                var grau = new FormatConvertedBitmap(klein, PixelFormats.Gray8, null, 0);

                int breite = grau.PixelWidth, hoehe = grau.PixelHeight;
                int schritt = (breite + 3) / 4 * 4;   // Gray8 ist auf 4 Byte ausgerichtet
                var roh = new byte[schritt * hoehe];
                grau.CopyPixels(roh, schritt, 0);

                // Auf exakt Kante × Kante bringen (Rundung der Skalierung ausgleichen).
                var feld = new float[Kante * Kante];
                for (int j = 0; j < Kante; j++)
                {
                    int sy = Math.Min(hoehe - 1, j * hoehe / Kante);
                    for (int i = 0; i < Kante; i++)
                    {
                        int sx = Math.Min(breite - 1, i * breite / Kante);
                        feld[j * Kante + i] = roh[sy * schritt + sx];
                    }
                }

                if (modus == WasserzeichenVorverarbeitung.Kanten)
                    Kantenstaerke(feld);
                else
                    Hochpass(feld);

                ZentriereAufMittelwert(feld);
                return feld;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Ersetzt das Feld durch die Sobel-Kantenstärke (Grundprojekt ImageMatching.Core).
        /// Das Wasserzeichen besteht aus harten geometrischen Kanten und tritt dadurch
        /// stärker hervor als bei reiner Hochpassfilterung.
        /// </summary>
        private static void Kantenstaerke(float[] feld)
        {
            // GrayImage erwartet Helligkeiten 0..1.
            var norm = new float[feld.Length];
            for (int i = 0; i < feld.Length; i++)
                norm[i] = feld[i] / 255f;

            var grau = new ImageMatching.Core.GrayImage(Kante, Kante, norm);
            var gradient = ImageMatching.Core.Sobel.Compute(grau);

            Array.Copy(gradient.Magnitude, feld, feld.Length);
        }

        /// <summary>
        /// Zieht den lokalen Mittelwert ab (Box-Filter über ein Integralbild, also
        /// O(n) statt O(n·r²)). Übrig bleibt die feine Struktur — dort sitzt das
        /// Wasserzeichen, während der Motivinhalt weitgehend verschwindet.
        /// </summary>
        private static void Hochpass(float[] feld)
        {
            int n = Kante;

            // Integralbild mit Randzeile/-spalte.
            var integral = new double[(n + 1) * (n + 1)];
            for (int j = 0; j < n; j++)
            {
                double zeile = 0;
                for (int i = 0; i < n; i++)
                {
                    zeile += feld[j * n + i];
                    integral[(j + 1) * (n + 1) + (i + 1)] = integral[j * (n + 1) + (i + 1)] + zeile;
                }
            }

            var ergebnis = new float[feld.Length];
            for (int j = 0; j < n; j++)
            {
                int y0 = Math.Max(0, j - HochpassRadius);
                int y1 = Math.Min(n - 1, j + HochpassRadius);

                for (int i = 0; i < n; i++)
                {
                    int x0 = Math.Max(0, i - HochpassRadius);
                    int x1 = Math.Min(n - 1, i + HochpassRadius);

                    double summe =
                        integral[(y1 + 1) * (n + 1) + (x1 + 1)]
                        - integral[y0 * (n + 1) + (x1 + 1)]
                        - integral[(y1 + 1) * (n + 1) + x0]
                        + integral[y0 * (n + 1) + x0];

                    int anzahl = (y1 - y0 + 1) * (x1 - x0 + 1);
                    ergebnis[j * n + i] = feld[j * n + i] - (float)(summe / anzahl);
                }
            }

            Array.Copy(ergebnis, feld, feld.Length);
        }

        private static void ZentriereAufMittelwert(float[] feld)
        {
            double m = 0;
            for (int i = 0; i < feld.Length; i++) m += feld[i];
            m /= feld.Length;

            for (int i = 0; i < feld.Length; i++) feld[i] -= (float)m;
        }

        #endregion

        #region Speichern und Laden

        private sealed class MaskenDatei
        {
            public int Kante { get; set; }
            public int Grundmenge { get; set; }
            public WasserzeichenVorverarbeitung Modus { get; set; }
            public float[] Muster { get; set; } = Array.Empty<float>();
        }

        public void Speichern(string pfad)
        {
            var daten = new MaskenDatei
            {
                Kante = Kante,
                Grundmenge = Grundmenge,
                Modus = Modus,
                Muster = _muster
            };

            using var fs = File.Create(pfad);
            JsonSerializer.Serialize(fs, daten);
        }

        public static WasserzeichenMaske? Laden(string pfad)
        {
            try
            {
                if (!File.Exists(pfad))
                    return null;

                using var fs = File.OpenRead(pfad);
                var daten = JsonSerializer.Deserialize<MaskenDatei>(fs);

                if (daten is null || daten.Kante != Kante || daten.Muster.Length != Kante * Kante)
                    return null;

                return new WasserzeichenMaske(daten.Muster, daten.Grundmenge, daten.Modus);
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
