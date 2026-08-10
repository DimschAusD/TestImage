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
    /// 4. Stabilitätsgewichte: Ein Wasserzeichen besteht oft aus einem festen und einem
    ///    wechselnden Teil — beim DeviantArt-Zeichen ist das Logo immer gleich, der
    ///    Künstlername darunter nicht. Der wechselnde Teil mittelt sich zwar weg, seine
    ///    Pixel bleiben aber im Vergleich und verwässern das Ergebnis. Darum wird je
    ///    Pixel auch die Streuung über die Beispielbilder gemessen: stabile Pixel zählen
    ///    voll, stark schwankende kaum. So bleibt das Logo als Erkennungsmerkmal übrig.
    /// 5. Prüfen: gewichtete normierte Kreuzkorrelation (Pearson) gegen dieses Muster.
    /// </summary>
    public sealed class WasserzeichenMaske
    {
        /// <summary>Arbeitsauflösung des normierten Ausschnitts.</summary>
        public const int Kante = 96;

        /// <summary>Anteil der kürzeren Bildkante, der als Mittelausschnitt dient.</summary>
        private const double AusschnittAnteil = 0.45;

        /// <summary>Radius des Box-Filters für den lokalen Mittelwert.</summary>
        private const int HochpassRadius = 6;

        private readonly float[] _muster;    // Kante*Kante, gefiltert und mittelwertfrei
        private readonly float[] _gewicht;   // Kante*Kante, 0 … 1 — wie stabil das Pixel über die Beispiele war

        public int Grundmenge { get; }

        /// <summary>
        /// Sprechender Name, damit mehrere Muster nebeneinander unterscheidbar bleiben
        /// (etwa „DeviantArt-Logo" gegen einen zweiten Zeichentyp).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Vorverarbeitung, mit der dieses Muster gelernt wurde. Beim Prüfen muss dieselbe laufen.</summary>
        public WasserzeichenVorverarbeitung Modus { get; }

        /// <summary>
        /// Anteil der Pixel, die über die Beispielbilder stabil geblieben sind.
        /// Niedrige Werte heissen: Die Beispiele hatten wenig gemeinsam — das Muster
        /// taugt dann wenig.
        /// </summary>
        public double StabilerAnteil
        {
            get
            {
                double summe = 0;
                for (int i = 0; i < _gewicht.Length; i++) summe += _gewicht[i];
                return summe / _gewicht.Length;
            }
        }

        private WasserzeichenMaske(float[] muster, float[] gewicht, int grundmenge, WasserzeichenVorverarbeitung modus)
        {
            _muster = muster;
            _gewicht = gewicht;
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
            var summeQuadrat = new double[Kante * Kante];
            int gezaehlt = 0;

            for (int i = 0; i < liste.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var feld = LadeMerkmalsfeld(liste[i], modus);
                if (feld is null)
                    continue;

                for (int p = 0; p < summe.Length; p++)
                {
                    summe[p] += feld[p];
                    summeQuadrat[p] += (double)feld[p] * feld[p];
                }

                gezaehlt++;
                fortschritt?.Report((i + 1, liste.Count));
            }

            if (gezaehlt < 5)
                return null;

            var muster = new float[summe.Length];
            var varianz = new double[summe.Length];

            for (int p = 0; p < muster.Length; p++)
            {
                double m = summe[p] / gezaehlt;
                muster[p] = (float)m;

                // Verschiebungssatz; negative Rundungsreste abfangen.
                varianz[p] = Math.Max(0.0, summeQuadrat[p] / gezaehlt - m * m);
            }

            ZentriereAufMittelwert(muster);
            return new WasserzeichenMaske(muster, BerechneGewichte(varianz), gezaehlt, modus);
        }

        /// <summary>
        /// Übersetzt die Pixelstreuung in Gewichte 0 … 1. Bezugsgrösse ist der Median
        /// der Streuungen, nicht ein fester Betrag — dadurch bleiben die Gewichte
        /// unabhängig von Motivkontrast und Bildmaterial vergleichbar.
        ///
        /// Ein Pixel mit der typischen Streuung bekommt 0,5; deutlich ruhigere Pixel
        /// gehen gegen 1, deutlich unruhigere gegen 0. Der weiche Verlauf ist Absicht:
        /// eine harte Schwelle würde bei knapp danebenliegenden Pixeln kippen.
        /// </summary>
        private static float[] BerechneGewichte(double[] varianz)
        {
            var sortiert = (double[])varianz.Clone();
            Array.Sort(sortiert);

            double typisch = sortiert[sortiert.Length / 2];
            if (typisch < 1e-6)
                typisch = 1e-6;   // alle Pixel gleich ruhig – dann zählen alle gleich

            var gewicht = new float[varianz.Length];
            for (int p = 0; p < gewicht.Length; p++)
                gewicht[p] = (float)(typisch / (typisch + varianz[p]));

            return gewicht;
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
            return feld is null ? 0f : Korrelation(feld);
        }

        /// <summary>Wie <see cref="Pruefe(string)"/>, aber für ein bereits geladenes Bild.</summary>
        public float Pruefe(BitmapSource bild)
        {
            var feld = MacheMerkmalsfeld(bild, Modus);
            return feld is null ? 0f : Korrelation(feld);
        }

        /// <summary>
        /// Gewichtete Pearson-Korrelation zwischen Muster und Prüffeld. Normiert, damit
        /// Helligkeit und Kontrast des Motivs keine Rolle spielen; die Gewichte blenden
        /// die Bildstellen aus, die schon beim Lernen von Beispiel zu Beispiel wechselten.
        /// </summary>
        private float Korrelation(float[] b)
        {
            float[] a = _muster;
            float[] w = _gewicht;

            if (a.Length != b.Length || a.Length == 0)
                return 0f;

            double summeGewicht = 0, ma = 0, mb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                summeGewicht += w[i];
                ma += w[i] * a[i];
                mb += w[i] * b[i];
            }

            if (summeGewicht < 1e-9)
                return 0f;

            ma /= summeGewicht;
            mb /= summeGewicht;

            double zaehler = 0, qa = 0, qb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double da = a[i] - ma, db = b[i] - mb;
                zaehler += w[i] * da * db;
                qa += w[i] * da * da;
                qb += w[i] * db * db;
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

        #region Vorschau

        /// <summary>
        /// Wie viele Standardabweichungen die Graustufen abdecken. Grösser heisst
        /// flauer, kleiner lässt mehr Bildpunkte an den Enden abschneiden.
        /// </summary>
        private const double VorschauSigma = 2.5;

        /// <summary>
        /// Graustufenbild des gelernten Musters, zum Ansehen in der Oberfläche.
        ///
        /// Zwei Entscheidungen stecken darin:
        ///
        /// 1. <b>Mit den Gewichten multipliziert.</b> Gezeigt wird damit das, was beim
        ///    Vergleich tatsächlich zählt: Der feste Teil des Zeichens steht klar da,
        ///    der über die Beispiele wechselnde Teil – beim DeviantArt-Zeichen der
        ///    Künstlername – verblasst ins Graue. So sieht man nicht nur, dass wenig
        ///    stabil war, sondern auch wo.
        ///
        /// 2. <b>Über die Standardabweichung normiert, nicht über Min und Max.</b> Nach
        ///    Hochpass und Mittelung ist der Ausschlag klein und enthält Ausreisser;
        ///    eine Min/Max-Streckung würde von einzelnen Punkten bestimmt und liesse
        ///    den Rest grau. Null wird Mittelgrau, das Zeichen tritt hell und dunkel
        ///    daraus hervor.
        ///
        /// Es zeigt den Mittelausschnitt (siehe <see cref="AusschnittAnteil"/>), sieht
        /// also nicht aus wie ein Bild, sondern wie ein Quadrat um die Bildmitte.
        /// </summary>
        public BitmapSource? ErzeugeVorschau()
        {
            try
            {
                int n = _muster.Length;

                var feld = new double[n];
                for (int i = 0; i < n; i++)
                    feld[i] = _muster[i] * _gewicht[i];

                double mittel = 0;
                for (int i = 0; i < n; i++) mittel += feld[i];
                mittel /= n;

                double quadrate = 0;
                for (int i = 0; i < n; i++)
                {
                    double d = feld[i] - mittel;
                    quadrate += d * d;
                }

                double streuung = Math.Sqrt(quadrate / n);
                if (streuung < 1e-9)
                    return null;   // völlig flaches Muster – da gibt es nichts zu zeigen

                double spanne = VorschauSigma * streuung;

                var punkte = new byte[n];
                for (int i = 0; i < n; i++)
                {
                    double v = (feld[i] - mittel) / spanne;      // typisch −1 … +1
                    punkte[i] = (byte)Math.Clamp((v + 1.0) * 127.5, 0, 255);
                }

                var bild = BitmapSource.Create(
                    Kante, Kante, 96, 96, PixelFormats.Gray8, null, punkte, Kante);

                bild.Freeze();
                return bild;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Speichern und Laden

        /// <summary>Serialisierbare Form einer Maske. Auch als Listeneintrag verwendet.</summary>
        internal sealed class MaskenDatei
        {
            public int Kante { get; set; }
            public int Grundmenge { get; set; }
            public string Name { get; set; } = string.Empty;
            public WasserzeichenVorverarbeitung Modus { get; set; }
            public float[] Muster { get; set; } = Array.Empty<float>();

            /// <summary>Fehlt in Dateien der ersten Fassung – dann zählen alle Pixel gleich.</summary>
            public float[]? Gewicht { get; set; }
        }

        internal MaskenDatei AlsDatensatz() => new()
        {
            Kante = Kante,
            Grundmenge = Grundmenge,
            Name = Name,
            Modus = Modus,
            Muster = _muster,
            Gewicht = _gewicht
        };

        internal static WasserzeichenMaske? AusDatensatz(MaskenDatei? daten)
        {
            if (daten is null || daten.Kante != Kante || daten.Muster.Length != Kante * Kante)
                return null;

            // Ältere Dateien kennen keine Gewichte: dann verhält sich die Maske exakt
            // wie vorher, statt wegen fehlender Daten unbrauchbar zu werden.
            float[] gewicht;
            if (daten.Gewicht is { Length: Kante * Kante })
            {
                gewicht = daten.Gewicht;
            }
            else
            {
                gewicht = new float[Kante * Kante];
                Array.Fill(gewicht, 1f);
            }

            return new WasserzeichenMaske(daten.Muster, gewicht, daten.Grundmenge, daten.Modus)
            {
                Name = daten.Name
            };
        }

        public void Speichern(string pfad)
        {
            using var fs = File.Create(pfad);
            JsonSerializer.Serialize(fs, AlsDatensatz());
        }

        public static WasserzeichenMaske? Laden(string pfad)
        {
            try
            {
                if (!File.Exists(pfad))
                    return null;

                using var fs = File.OpenRead(pfad);
                return AusDatensatz(JsonSerializer.Deserialize<MaskenDatei>(fs));
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
