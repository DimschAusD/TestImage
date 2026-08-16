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
    /// <summary>
    /// Stelle im Bild, an der das Zeichen sitzt.
    ///
    /// Ein Muster ist nicht nur eine Pixelverteilung, sondern auch ein Ort: Das
    /// DeviantArt-Zeichen liegt mittig, ein Patreon-Schriftzug dagegen oben rechts. Ohne
    /// diese Angabe würde die Prüfung an der falschen Stelle suchen und nichts finden.
    ///
    /// <b>Reihenfolge nicht ändern</b> — die Werte werden als Zahl gespeichert, und die
    /// Auswahlliste in der Oberfläche hängt am selben Index.
    /// </summary>
    public enum WasserzeichenBereich
    {
        Mitte = 0,
        ObenLinks = 1,
        ObenRechts = 2,
        UntenLinks = 3,
        UntenRechts = 4,

        /// <summary>
        /// Nur beim Lernen zulässig: alle Stellen durchprobieren und die mit dem
        /// deutlichsten Muster behalten. Am Ende steht in der Maske immer eine der
        /// konkreten Stellen — sonst müsste die Prüfung fünf Ausschnitte je Bild rechnen.
        ///
        /// Bewusst hinten angehängt: Die Zahlen stehen so in gespeicherten Mustern.
        /// </summary>
        Alle = 5
    }

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
        /// Stelle im Bild, an der dieses Muster gelernt wurde. Beim Prüfen wird derselbe
        /// Ausschnitt genommen — sonst sucht die Prüfung an der falschen Stelle.
        /// </summary>
        public WasserzeichenBereich Bereich { get; set; } = WasserzeichenBereich.Mitte;

        /// <summary>
        /// Eigene Erkennungsschwelle dieses Musters, beim Lernen eingemessen.
        /// 0 heisst „nicht gesetzt" — dann gilt der allgemeine Wert.
        ///
        /// Nötig, weil der erreichbare Wert davon abhängt, wie viel von der Ausschnitts-
        /// fläche das Zeichen füllt: Ein bildfüllendes Zeichen erreicht ein Vielfaches
        /// dessen, was ein Banner in der Ecke erreicht, das rund 7 % der Fläche belegt.
        /// Eine gemeinsame Schwelle für beide kann nur falsch sein.
        /// </summary>
        public float Schwelle { get; set; }

        /// <summary>Bezeichnung des Bereichs für die Anzeige.</summary>
        public string BereichName => Bereich switch
        {
            WasserzeichenBereich.ObenLinks => "oben links",
            WasserzeichenBereich.ObenRechts => "oben rechts",
            WasserzeichenBereich.UntenLinks => "unten links",
            WasserzeichenBereich.UntenRechts => "unten rechts",
            _ => "Mitte"
        };

        /// <summary>
        /// Anteil der Pixel, die über die Beispielbilder stabil geblieben sind.
        ///
        /// <b>Kein Gütemass.</b> Die Gewichte werden auf die mittlere Streuung normiert,
        /// also liegt ihr Mittelwert bauartbedingt nahe 0,5 — nachgemessen an drei sehr
        /// unterschiedlich guten Mustern, alle bei rund diesem Wert. Wer die Qualität
        /// eines Musters braucht, nimmt <see cref="MusterStaerke"/>.
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

        /// <summary>
        /// Wie deutlich das Muster ist: Streuung des gewichteten Musters.
        ///
        /// Damit lässt sich die richtige Stelle finden, wenn man sie nicht kennt. Eine
        /// leere oder gleichmässig dunkle Ecke mittelt sich zu einer flachen Fläche und
        /// bekommt hier fast null — auch wenn dort viele Bildpunkte „stabil" waren.
        /// Der <see cref="StabilerAnteil"/> allein taugt dafür also nicht.
        /// </summary>
        public double MusterStaerke
        {
            get
            {
                int n = _muster.Length;
                if (n == 0) return 0;

                double mittel = 0;
                for (int i = 0; i < n; i++) mittel += _muster[i] * _gewicht[i];
                mittel /= n;

                double quadrate = 0;
                for (int i = 0; i < n; i++)
                {
                    double d = _muster[i] * _gewicht[i] - mittel;
                    quadrate += d * d;
                }

                return Math.Sqrt(quadrate / n);
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
            WasserzeichenVorverarbeitung modus = WasserzeichenVorverarbeitung.Hochpass,
            WasserzeichenBereich bereich = WasserzeichenBereich.Mitte)
        {
            var liste = new List<string>(dateien);
            if (liste.Count < 5)
                return null;   // zu wenige Beispiele, das Muster bliebe verrauscht

            // Merkmalsfelder einmal berechnen und behalten. Bei 96×96 sind das rund
            // 37 KB je Bild – ein guter Tausch gegen weitere Dekodierdurchläufe, denn
            // darauf bauen auch das Einmessen der Schwelle und das Aufteilen in zwei
            // Muster auf.
            var felder = new List<float[]>(liste.Count);

            for (int i = 0; i < liste.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var feld = LadeMerkmalsfeld(liste[i], modus, bereich);
                if (feld is not null)
                    felder.Add(feld);

                fortschritt?.Report((i + 1, liste.Count));
            }

            return LerneAusFeldern(felder, modus, bereich);
        }

        /// <summary>Merkmalsfeld eines Bildes – für Aufrufer, die mehrfach damit rechnen wollen.</summary>
        internal static float[]? Merkmalsfeld(
            string pfad, WasserzeichenVorverarbeitung modus, WasserzeichenBereich bereich)
            => LadeMerkmalsfeld(pfad, modus, bereich);

        /// <summary>Prüft ein bereits berechnetes Merkmalsfeld – ohne erneutes Laden.</summary>
        internal float Pruefe(float[] feld) => Korrelation(feld);

        /// <summary>
        /// Der eigentliche Lernschritt auf fertigen Merkmalsfeldern. Getrennt vom Laden,
        /// damit dieselben Felder mehrfach verwendet werden können — etwa beim Aufteilen
        /// eines Ordners in zwei Muster.
        /// </summary>
        internal static WasserzeichenMaske? LerneAusFeldern(
            IReadOnlyList<float[]> felder,
            WasserzeichenVorverarbeitung modus,
            WasserzeichenBereich bereich)
        {
            var summe = new double[Kante * Kante];
            var summeQuadrat = new double[Kante * Kante];
            int gezaehlt = 0;

            foreach (var feld in felder)
            {
                for (int p = 0; p < summe.Length; p++)
                {
                    summe[p] += feld[p];
                    summeQuadrat[p] += (double)feld[p] * feld[p];
                }

                gezaehlt++;
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

            var maske = new WasserzeichenMaske(muster, BerechneGewichte(varianz), gezaehlt, modus)
            {
                Bereich = bereich
            };

            maske.Schwelle = MesseSchwelle(maske, felder);
            return maske;
        }

        /// <summary>
        /// Untergrenze der Schwelle. Unmarkierte Bilder lagen bei der Einmessung zwischen
        /// −0,071 und 0,050 — darunter fischt jede Schwelle im Rauschen, egal wie schwach
        /// das Zeichen ist.
        /// </summary>
        private const float SchwelleUntergrenze = 0.06f;

        /// <summary>Sicherheitsabstand nach unten: Unbekannte Bilder liegen oft etwas tiefer als die Beispiele.</summary>
        private const float SchwelleAbschlag = 0.75f;

        /// <summary>
        /// Misst die Schwelle dieses Musters an seinen eigenen Beispielen.
        ///
        /// Beim Lernen weiss man etwas, das später fehlt: Alle Beispiele tragen das
        /// Zeichen. Also verrät ihre Verteilung, welche Werte dieses Zeichen überhaupt
        /// erreicht — bildfüllend deutlich mehr als ein Banner in der Ecke.
        ///
        /// Genommen wird das zehnte Perzentil, damit ein einzelner Ausreisser die
        /// Schwelle nicht verdirbt.
        /// </summary>
        private static float MesseSchwelle(WasserzeichenMaske maske, IReadOnlyList<float[]> felder)
        {
            if (felder.Count == 0)
                return 0f;   // 0 heisst „nicht gesetzt" – dann gilt der allgemeine Wert

            var werte = new List<float>(felder.Count);
            foreach (var feld in felder)
                werte.Add(maske.Korrelation(feld));

            werte.Sort();

            int index = (int)(werte.Count * 0.10);
            if (index >= werte.Count) index = werte.Count - 1;

            float schwelle = werte[index] * SchwelleAbschlag;
            return Math.Clamp(schwelle, SchwelleUntergrenze, 0.9f);
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
            var feld = LadeMerkmalsfeld(bildPfad, Modus, Bereich);
            return feld is null ? 0f : Korrelation(feld);
        }

        /// <summary>Wie <see cref="Pruefe(string)"/>, aber für ein bereits geladenes Bild.</summary>
        public float Pruefe(BitmapSource bild)
        {
            var feld = MacheMerkmalsfeld(bild, Modus, Bereich);
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

        private static float[]? LadeMerkmalsfeld(
            string pfad, WasserzeichenVorverarbeitung modus, WasserzeichenBereich bereich)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // Datei nicht gesperrt halten
                bmp.UriSource = new Uri(pfad);
                bmp.EndInit();
                bmp.Freeze();

                return MacheMerkmalsfeld(bmp, modus, bereich);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Ausschnitt schneiden, auf Arbeitsgrösse bringen, Filter anwenden.</summary>
        private static float[]? MacheMerkmalsfeld(
            BitmapSource quelle, WasserzeichenVorverarbeitung modus, WasserzeichenBereich bereich)
        {
            try
            {
                int kurz = Math.Min(quelle.PixelWidth, quelle.PixelHeight);
                int s = (int)(kurz * AusschnittAnteil);
                if (s < 8)
                    return null;

                // Der Ausschnitt sitzt dort, wo das Muster gelernt wurde. Bei einem
                // hochkantigen Bild deckt ein Eckquadrat gut ein Drittel der Höhe ab –
                // genug, um einen Schriftzug in der Ecke sicher einzufangen.
                (int x, int y) = bereich switch
                {
                    WasserzeichenBereich.ObenLinks => (0, 0),
                    WasserzeichenBereich.ObenRechts => (quelle.PixelWidth - s, 0),
                    WasserzeichenBereich.UntenLinks => (0, quelle.PixelHeight - s),
                    WasserzeichenBereich.UntenRechts => (quelle.PixelWidth - s, quelle.PixelHeight - s),
                    _ => ((quelle.PixelWidth - s) / 2, (quelle.PixelHeight - s) / 2)
                };

                x = Math.Max(0, x);
                y = Math.Max(0, y);

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
        /// Es zeigt den Ausschnitt, in dem gelernt wurde (siehe <see cref="Bereich"/> und
        /// <see cref="AusschnittAnteil"/>) – bei einem Eckmuster also das Eckquadrat, nicht
        /// die Bildmitte. Es sieht daher nicht aus wie ein Bild, sondern wie ein Quadrat.
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

            /// <summary>Fehlt in Dateien vor der Bereichswahl – dann gilt die Mitte, wie bisher.</summary>
            public WasserzeichenBereich Bereich { get; set; } = WasserzeichenBereich.Mitte;

            /// <summary>Eigene Schwelle; 0 in alten Dateien – dann gilt der allgemeine Wert.</summary>
            public float Schwelle { get; set; }

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
            Bereich = Bereich,
            Schwelle = Schwelle,
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
                Name = daten.Name,
                Bereich = daten.Bereich,
                Schwelle = daten.Schwelle
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
