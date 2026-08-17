using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using TestImage.Bildersuche;

namespace TestImage
{
    /// <summary>
    /// Die zweite Sorte Prüfung in der Ampel: nicht „ist die Datei heil", sondern
    /// „taugt das Bild etwas".
    ///
    /// <b>Verglichen wird innerhalb des Ordners, nicht gegen feste Zahlen.</b>
    ///
    /// Der erste Entwurf hatte Konstanten — 0,45 Byte je Pixel und so weiter. Die hätten
    /// eingemessen werden müssen, und zwar an einem Bestand von 200 GB. Zwei Dinge
    /// sprechen dagegen:
    ///
    /// <list type="number">
    /// <item>Wer die Anwendung mit fünf Bildern benutzt, hat nichts einzumessen. Ein
    ///       Entwurf, der ohne grossen Bestand nicht funktioniert, ist der falsche.</item>
    /// <item>Eine eingemessene Zahl gilt für den Bestand, an dem sie entstand. Ein
    ///       Künstler mit flächigen Farben komprimiert ganz anders als einer, der
    ///       malerisch arbeitet — beim nächsten Ordner läge sie wieder daneben.</item>
    /// </list>
    ///
    /// Auffällig ist ein Bild deshalb nicht bei Unterschreiten einer Zahl, sondern wenn
    /// es <b>deutlich unter den anderen im selben Ordner</b> liegt. Das ist dieselbe
    /// Lehre wie bei der FS-Sortierung: global gemessen 0,53 (Münzwurf), im eigenen
    /// Ordner 0,76.
    ///
    /// Die Vergleichswerte fallen beim Blättern ohnehin an; sie werden nur behalten. Ab
    /// <see cref="MindestVergleichsbilder"/> Bildern urteilt die Ampel, vorher zeigt sie
    /// nur die Zahl. Kosten: kein zusätzlicher Dateizugriff, kein Vorlauf.
    /// </summary>
    public partial class AufgabeViewModel
    {
        #region Massstäbe

        /// <summary>Ab so vielen gemessenen Bildern im Ordner ist der Mittelwert belastbar genug für ein Urteil.</summary>
        private const int MindestVergleichsbilder = 10;

        /// <summary>
        /// Anteil am Ordner-Median, unter dem ein Wert als auffällig gilt.
        ///
        /// Auch das sind gewählte Zahlen — aber <b>Verhältnisse</b>, keine absoluten
        /// Werte. Sie hängen nicht am Material: „halb so scharf wie der Rest des Ordners"
        /// bedeutet bei Linienzeichnungen dasselbe wie bei gemalten Bildern.
        /// </summary>
        private const double AnteilKompression = 0.40;
        private const double AnteilAbmessung = 0.50;
        private const double AnteilStreuung = 0.30;

        /// <summary>
        /// Nachgeprüft am Ordner <c>cyberdelta271</c> (29 normale Bilder gegen 11, die
        /// der Nutzer wegen Unschärfe in <c>Fehler_Bilder</c> aussortiert hatte):
        ///
        /// <code>
        /// normal         Median 0,0422   niedrigster 0,0279
        /// Fehler_Bilder  Median 0,0093   höchster    0,0112
        /// </code>
        ///
        /// Die Wertebereiche überlappen nicht; dazwischen liegt ein Faktor von 2,5. Die
        /// Grenze bei 55 % des Medians (0,0232) fällt mitten in diese Lücke — 11 von 11
        /// erkannt, 0 von 29 Fehlalarmen. Jeder Anteil zwischen 30 % und 65 % täte es
        /// genauso; erst ab 70 % kommt der erste Fehlalarm.
        /// </summary>
        private const double AnteilKanten = 0.55;

        /// <summary>
        /// Absolute Untergrenze für „fast einfarbig". Anders als die übrigen drei ist
        /// diese Aussage nicht vom Ordner abhängig: Eine praktisch gleichmässige Fläche
        /// ist ein Platzhalter, egal was daneben liegt.
        /// </summary>
        private const double StreuungAbsolutLeer = 0.02;

        /// <summary>
        /// Absolute Untergrenze für Byte je Pixel — die zweite Notbremse.
        ///
        /// <b>Wozu, wenn doch mit dem Ordner verglichen wird:</b> Der Vergleich hat einen
        /// blinden Fleck. Sind <i>alle</i> Bilder eines Ordners gleich schlecht, liegt
        /// der Median tief und keines fällt auf. Genau das zeigte der Ordner
        /// <c>cyberdelta271\Fehler_Bilder</c> — dort blieb die Ampel grün, obwohl jedes
        /// einzelne Bild Ausschuss ist.
        ///
        /// <b>Warum 0,1 und nicht die gemessene Grenze:</b> Die aussortierten Bilder dort
        /// lagen bei 0,04 bis 0,05 Byte je Pixel, die behaltenen bei 0,69 bis 0,95 — ein
        /// Faktor von rund achtzehn. Dazwischen ist viel Luft, und 0,1 liegt mit Abstand
        /// über dem Ausschuss und weit unter allem, was gehalten wurde.
        ///
        /// So tief ist es keine Geschmacksfrage mehr: 0,1 Byte je Pixel heisst, dass für
        /// zehn Bildpunkte ein einziges Byte übrig ist. Was dabei herauskommt, ist matsch,
        /// egal was der Ordner sonst enthält.
        /// </summary>
        private const double BytePixelAbsolutTief = 0.10;

        /// <summary>Ab dieser Pixelzahl wird Kantenarmut als Mangel gewertet. Ein kleines Bild darf weich sein.</summary>
        private const long GrossAbPixel = 2_000_000;

        #endregion

        #region Gemerkte Messwerte des offenen Ordners

        private sealed class Bildwerte
        {
            public double BytePixel;
            public double KleinsteKante;
            public double Streuung;
            public double Kanten;
            public long Pixel;
        }

        /// <summary>
        /// Messwerte je Datei des gerade betrachteten Ordners.
        ///
        /// Nach Pfad abgelegt und nicht als blosse Liste: Wer vor- und zurückblättert,
        /// misst dasselbe Bild mehrfach — als Liste geführt bekäme es dann auch mehrfach
        /// Gewicht und verschöbe den Mittelwert.
        /// </summary>
        private readonly Dictionary<string, Bildwerte> _ordnerwerte =
            new(StringComparer.OrdinalIgnoreCase);

        private string? _bewertungsOrdner;

        /// <summary>
        /// Holt den Eintrag zur Datei und wirft die Sammlung weg, sobald der Ordner
        /// wechselt — Werte aus einem anderen Künstlerordner wären als Massstab wertlos.
        /// </summary>
        private Bildwerte HoleWerte(string pfad)
        {
            string ordner = Path.GetDirectoryName(pfad) ?? string.Empty;

            if (!string.Equals(ordner, _bewertungsOrdner, StringComparison.OrdinalIgnoreCase))
            {
                _ordnerwerte.Clear();
                _bewertungsOrdner = ordner;
            }

            if (!_ordnerwerte.TryGetValue(pfad, out var w))
            {
                w = new Bildwerte();
                _ordnerwerte[pfad] = w;
            }

            return w;
        }

        /// <summary>Median der bisher gemessenen Werte, 0 wenn zu wenige vorliegen.</summary>
        private double Median(Func<Bildwerte, double> auswahl, out int anzahl)
        {
            var werte = _ordnerwerte.Values
                .Select(auswahl)
                .Where(x => x > 0)
                .OrderBy(x => x)
                .ToList();

            anzahl = werte.Count;
            return anzahl == 0 ? 0 : werte[anzahl / 2];
        }

        #endregion

        #region Ergebnisse

        /// <summary>True = auffällig. <c>null</c> = kein Urteil (nicht gemessen oder zu wenig Vergleich).</summary>
        [ObservableProperty]
        public partial bool? IsBildStarkKomprimiert { get; set; }

        [ObservableProperty]
        public partial bool? IsBildZuKlein { get; set; }

        [ObservableProperty]
        public partial bool? IsBildFastEinfarbig { get; set; }

        [ObservableProperty]
        public partial bool? IsBildKantenarm { get; set; }

        /// <summary>ToolTip-Texte: gemessener Wert, Ordner-Median und Anzahl der Vergleichsbilder.</summary>
        [ObservableProperty]
        public partial string KompressionText { get; set; } = "Byte je Pixel – noch nicht gemessen";

        [ObservableProperty]
        public partial string AbmessungText { get; set; } = "Abmessungen – noch nicht gemessen";

        [ObservableProperty]
        public partial string EinfarbigText { get; set; } = "Farbstreuung – noch nicht gemessen";

        [ObservableProperty]
        public partial string KantenText { get; set; } = "Kantendichte – noch nicht gemessen";

        /// <summary>
        /// ToolTip des Kopf/Endung-Feldes. Nennt bei einer Abweichung, welche Endung
        /// richtig wäre — die blosse Farbe sagte nur, dass etwas nicht stimmt.
        /// </summary>
        [ObservableProperty]
        public partial string HeaderText { get; set; } = "Dateikopf passt zur Endung – noch nicht geprüft";

        #endregion

        /// <summary>
        /// Setzt den Erklärtext zum Kopf/Endung-Feld aus dem Prüfergebnis.
        /// </summary>
        private void SetzeHeaderText(string? pfad, bool passt, string? erkanntesFormat)
        {
            if (string.IsNullOrEmpty(pfad))
            {
                HeaderText = "Dateikopf passt zur Endung – keine Datei";
                return;
            }

            string endung = Path.GetExtension(pfad).TrimStart('.').ToLowerInvariant();
            string erkannt = (erkanntesFormat ?? string.Empty).ToLowerInvariant();

            if (passt)
            {
                HeaderText = $"Dateikopf und Endung stimmen überein (.{endung}).";
                return;
            }

            if (erkannt.Length == 0 || erkannt == "unknown")
            {
                HeaderText =
                    $"Der Dateikopf passt nicht zur Endung .{endung}.\n"
                    + "Welches Format es wirklich ist, liess sich nicht erkennen – "
                    + "möglicherweise ist der Anfang der Datei beschädigt.";
                return;
            }

            HeaderText =
                $"Die Datei heisst .{endung}, ist aber ein {erkannt.ToUpperInvariant()}.\n"
                + $"Richtig wäre die Endung .{erkannt}.";
        }

        /// <summary>Dateigrösse lesbar – für die Meldung der Notbremse.</summary>
        private static string Groesse(string pfad)
        {
            try
            {
                long b = new FileInfo(pfad).Length;
                return b >= 1024 * 1024
                    ? $"{b / 1024.0 / 1024.0:F1} MB"
                    : $"{b / 1024.0:F0} KB";
            }
            catch
            {
                return "unbekannter Grösse";
            }
        }

        private void LeereBildbewertung()
        {
            IsBildStarkKomprimiert = null;
            IsBildZuKlein = null;
            IsBildFastEinfarbig = null;
            IsBildKantenarm = null;
        }

        /// <summary>
        /// Bewertet, was aus Dateiangaben und Vorschaubild ablesbar ist: Kompression,
        /// Abmessungen, Einfarbigkeit.
        ///
        /// <b>Gerechnet wird im Hintergrund.</b> Die Dateigrösse zu erfragen ist ein
        /// Plattenzugriff, und die Streuung läuft über alle Bildpunkte der Vorschau.
        /// Beides gehört nicht in den Oberflächenfaden, der währenddessen das Bild
        /// zeichnen soll. Die Bilder sind eingefroren, dürfen also über Fadengrenzen
        /// hinweg gelesen werden. Nach dem <c>await</c> geht es auf dem Oberflächenfaden
        /// weiter — die Eigenschaften werden dort gesetzt, wo sie hingehören.
        /// </summary>
        private async Task BewerteAusVorschauAsync(string? pfad, BitmapSource? vorschau)
        {
            if (string.IsNullOrEmpty(pfad))
            {
                LeereBildbewertung();
                return;
            }

            int breite = OriginalImageWidth;
            int hoehe = OriginalImageHeight;

            var (bytePixel, streuungGemessen) = await Task.Run(() =>
                (Bildbewertung.ByteJePixel(pfad, breite, hoehe),
                 Bildbewertung.Farbstreuung(vorschau)));

            var werte = HoleWerte(pfad);
            werte.Pixel = (long)breite * hoehe;

            // --- Byte je Pixel
            werte.BytePixel = bytePixel;
            double medianBjp = Median(w => w.BytePixel, out int nBjp);

            (IsBildStarkKomprimiert, KompressionText) = Beurteile(
                "Byte je Pixel", werte.BytePixel, medianBjp, nBjp, AnteilKompression, "F2",
                auffaellig: "Deutlich weniger Daten je Pixel als der Rest des Ordners – stark komprimiert.",
                inOrdnung: "Datenmenge liegt im Rahmen des Ordners.");

            // Notbremse ohne Ordnervergleich: Liegt der ganze Ordner im Keller, fällt
            // beim Vergleich nichts auf — siehe BytePixelAbsolutTief. Diese Grenze
            // greift auch dann, und auch schon vor dem zehnten Bild.
            if (werte.BytePixel > 0 && werte.BytePixel < BytePixelAbsolutTief)
            {
                IsBildStarkKomprimiert = true;
                KompressionText =
                    $"{werte.BytePixel:F2} Byte je Pixel – unter der absoluten Grenze von "
                    + $"{BytePixelAbsolutTief:F2}.\n"
                    + $"{breite} × {hoehe} Pixel in {Groesse(pfad)} gequetscht. "
                    + "So tief ist es unabhängig vom Ordner Ausschuss.";
            }

            // --- Abmessungen
            werte.KleinsteKante = breite > 0 && hoehe > 0 ? Math.Min(breite, hoehe) : 0;
            double medianKante = Median(w => w.KleinsteKante, out int nKante);

            (IsBildZuKlein, AbmessungText) = Beurteile(
                "kürzeste Kante", werte.KleinsteKante, medianKante, nKante, AnteilAbmessung, "F0",
                auffaellig: "Deutlich kleiner als der Rest des Ordners – eher ein Vorschaubild.",
                inOrdnung: "Grösse liegt im Rahmen des Ordners.");

            // Der Name zuerst, dann die Zahlen: Wer über ein 20-Punkte-Kästchen mit
            // einem „G" fährt, will als Erstes wissen, wofür das G steht.
            if (breite > 0 && hoehe > 0)
            {
                AbmessungText = $"Grösse: {breite} × {hoehe} Pixel\n" + AbmessungText;
            }
            else
            {
                AbmessungText = "Grösse\n" + AbmessungText;
            }

            // --- Einfarbigkeit
            werte.Streuung = streuungGemessen;
            double medianStreuung = Median(w => w.Streuung, out int nStreuung);

            (IsBildFastEinfarbig, EinfarbigText) = Beurteile(
                "Helligkeitsstreuung", werte.Streuung, medianStreuung, nStreuung, AnteilStreuung, "F3",
                auffaellig: "Deutlich flacher als der Rest des Ordners.",
                inOrdnung: "Genug Struktur im Bild.");

            // Absolute Notbremse: Eine praktisch gleichmässige Fläche ist ein Platzhalter,
            // unabhängig davon, was im Ordner daneben liegt.
            if (werte.Streuung > 0 && werte.Streuung < StreuungAbsolutLeer)
            {
                IsBildFastEinfarbig = true;
                EinfarbigText =
                    $"Helligkeitsstreuung {werte.Streuung:F3} – praktisch einfarbig.\n"
                    + "Platzhalter oder gespeicherte Fehlerseite?";
            }
        }

        /// <summary>
        /// Misst die Kantendichte am Bild in Anzeigegrösse.
        ///
        /// <b>Heisst bewusst nicht „Schärfe".</b> Gerechnet wird der Unterschied
        /// benachbarter Bildpunkte im Verhältnis zur Gesamtstreuung — also wie viele
        /// harte Übergänge das Bild für seine Grösse hat. Ein hochskaliertes Bild hat
        /// wenige davon, ein weich gemaltes aber genauso: Airbrush, Farbverläufe, Nebel.
        /// Die Zahl kann beides nicht unterscheiden, und ein Feld namens „unscharf"
        /// würde mehr behaupten, als sie hergibt.
        ///
        /// Der Ordnervergleich fängt den Stil ab: Malt jemand durchweg weich, liegt der
        /// Median seines Ordners tief und niemand wird angemeckert.
        /// </summary>
        private async Task BewerteKantendichteAsync(BitmapSource? grossesBild)
        {
            string? pfad = SelectedBildchen?.BName;
            if (string.IsNullOrEmpty(pfad))
            {
                IsBildKantenarm = null;
                return;
            }

            // Die teuerste der vier Messungen: Umwandlung nach Graustufen und zwei
            // Durchläufe über das Bild in Anzeigegrösse — bei 1920 × 1080 sind das zwei
            // Millionen Bildpunkte. Deshalb im Hintergrund; das Bild ist eingefroren.
            double gemessen = await Task.Run(() => Bildbewertung.Kantendichte(grossesBild));

            var werte = HoleWerte(pfad);
            werte.Kanten = gemessen;

            double median = Median(w => w.Kanten, out int anzahl);

            (bool? urteil, string text) = Beurteile(
                "Kantendichte", werte.Kanten, median, anzahl, AnteilKanten, "F3",
                auffaellig: "Deutlich weniger Kanten als der Rest des Ordners – hochskaliert oder weich gezeichnet.",
                inOrdnung: "Kantendichte liegt im Rahmen des Ordners.");

            // Nur bei grossen Bildern als Mangel werten. Der gemeldete Fall ist gerade
            // der: riesige Abmessungen und trotzdem keine Kanten, weil hochskaliert.
            bool gross = werte.Pixel >= GrossAbPixel;

            IsBildKantenarm = gross ? urteil : null;
            KantenText = gross
                ? text
                : text + $"\nBild unter {GrossAbPixel / 1_000_000.0:F0} Megapixel – Kantenarmut wird hier nicht bemängelt.";
        }

        /// <summary>
        /// Vergleicht einen Messwert mit dem Median des Ordners und schreibt den Text
        /// dazu.
        ///
        /// Der Text nennt immer <b>beide</b> Zahlen und die Anzahl der Vergleichsbilder.
        /// Ohne den Massstab wäre der Messwert allein nicht einzuordnen — genau der
        /// Grund, warum feste Schwellen hier nicht taugen.
        /// </summary>
        private static (bool? Urteil, string Text) Beurteile(
            string name, double wert, double median, int anzahl, double anteil, string format,
            string auffaellig, string inOrdnung)
        {
            if (wert <= 0)
            {
                return (null, $"{name} – nicht bestimmbar");
            }

            if (anzahl < MindestVergleichsbilder)
            {
                return (null,
                    $"{name} {wert.ToString(format)}\n"
                    + $"Noch kein Urteil: erst {anzahl} von {MindestVergleichsbilder} Vergleichsbildern "
                    + "in diesem Ordner gemessen.");
            }

            double grenze = median * anteil;
            bool schlecht = wert < grenze;

            string text =
                $"{name} {wert.ToString(format)}\n"
                + $"Ordner-Median {median.ToString(format)} aus {anzahl} Bildern, "
                + $"Grenze {grenze.ToString(format)} ({anteil:P0})\n"
                + (schlecht ? auffaellig : inOrdnung);

            return (schlecht, text);
        }
    }
}
