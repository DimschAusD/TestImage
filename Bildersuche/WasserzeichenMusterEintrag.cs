using System.Windows.Media;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Anzeigezeile für ein gelerntes Wasserzeichen-Muster. Eigener Typ statt einer
    /// Klasse im ViewModel, damit XAML ihn für Entwurfsdaten benennen kann.
    /// </summary>
    public sealed class WasserzeichenMusterEintrag
    {
        /// <summary>
        /// Nicht „Name": XAML behandelt ein Attribut dieses Namens als Elementnamen,
        /// womit Entwurfsdaten nicht mehr setzbar wären.
        /// </summary>
        public string MusterName { get; set; } = string.Empty;

        /// <summary>Anzahl der Bilder, aus denen gelernt wurde.</summary>
        public int Grundmenge { get; set; }

        /// <summary>
        /// Anteil der Bildpunkte, die über alle Beispiele gleich blieben.
        ///
        /// <b>Nicht als Güte lesen.</b> Die Gewichte werden auf die mittlere Streuung
        /// normiert, also liegt der Mittelwert bauartbedingt nahe 50 % — nachgemessen an
        /// drei sehr unterschiedlich guten Mustern, alle bei rund diesem Wert. Der Wert
        /// steht in der Zeile, weil er zum Muster gehört, nicht weil er es bewertet.
        /// </summary>
        public int StabilProzent { get; set; }

        /// <summary>Eigene Erkennungsschwelle dieses Musters, in Prozent.</summary>
        public int SchwelleProzent { get; set; }

        /// <summary>
        /// Deutlichkeit des Musters: die Streuung des gewichteten Musters, gemessen in
        /// Graustufen der Skala 0 … 255.
        ///
        /// Das ist die Zahl, mit der die Automatik „alle Bereiche" die beste Stelle
        /// aussucht: Eine leere oder gleichmässig dunkle Ecke mittelt sich zu einer
        /// flachen Fläche und landet nahe null, auch wenn dort viele Bildpunkte „stabil"
        /// waren.
        ///
        /// <b>Auch das ist keine Note.</b> Zum Vergleich <i>zwischen Stellen desselben
        /// Ordners</i> taugt sie, über Ordner hinweg nicht: Nachgemessen erreichte ein
        /// Ordner ohne gemeinsames Zeichen 3,69 und lag damit über einem anderen mit 3,37 —
        /// erkannt hat keiner von beiden etwas. Dafür steht <see cref="Trennschaerfe"/> da.
        /// </summary>
        public double Staerke { get; set; }

        /// <summary>
        /// Übereinstimmung, die dieses Muster bei einem Bild erreicht, das beim Lernen
        /// <b>nicht</b> dabei war. <c>null</c> bei Mustern, die vor dieser Messung
        /// gelernt wurden.
        ///
        /// Die einzige Zahl in dieser Zeile, die etwas über die Brauchbarkeit sagt.
        /// </summary>
        public float? Trennschaerfe { get; set; }

        /// <summary>True, wenn das Muster nachweislich mehr erkennt als seine Lernbilder.</summary>
        public bool IstBelegt { get; set; }

        /// <summary>
        /// Hochgerechnete Bilderzahl, ab der das Muster belegt wäre. <c>null</c> heisst
        /// entweder „schon belegt" oder „kein Zeichen, das sich herausmitteln liesse".
        /// </summary>
        public int? BilderFuerBeleg { get; set; }

        /// <summary>
        /// „–", solange nicht gemessen wurde.
        ///
        /// Eine Nachkommastelle: Auf ganze Prozent gerundet stand bei einem Muster mit
        /// 0,149 die Zahl „15 %" — daneben das Urteil „zu schwach", und die Grenze liegt
        /// bei 15 %. Das las sich wie ein Widerspruch.
        /// </summary>
        public string TrennschaerfeText => Trennschaerfe is null
            ? "–"
            : $"{Trennschaerfe.Value * 100f:0.0} %";

        /// <summary>Eine Nachkommastelle reicht — die Zahl dient dem Vergleich, nicht der Messung.</summary>
        public string StaerkeText => Staerke.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);

        /// <summary>
        /// Das gelernte Muster als Bild. Zeigt den Ausschnitt, in dem gelernt wurde
        /// (siehe <see cref="BereichName"/>), gewichtet mit der Stabilität – der feste
        /// Teil des Zeichens steht klar da, der wechselnde verblasst. <c>null</c>, wenn
        /// sich kein brauchbares Bild ergab.
        /// </summary>
        public ImageSource? Vorschau { get; set; }

        /// <summary>Stelle im Bild, an der dieses Muster gelernt wurde — „Mitte", „oben rechts" …</summary>
        public string BereichName { get; set; } = "Mitte";

        /// <summary>Knapp gehalten – die Liste kann lang werden, Erklärungen stehen im Tooltip.</summary>

        /// <summary>
        /// In der Zeile steht die Deutlichkeit, nicht mehr der Stabil-Anteil: Der lag
        /// bauartbedingt bei jedem Muster nahe 50 % und war damit keine Auskunft. Er
        /// steht weiterhin im Tooltip.
        /// </summary>
        public string Beschreibung =>
            $"{BereichName} · {Grundmenge} Bilder · erkennt fremde Bilder mit {TrennschaerfeText}"
            + $" · ab {SchwelleProzent} %"
            + (IstBelegt
                ? string.Empty
                : Trennschaerfe is null
                    ? " · nie nachgemessen – wird übersprungen"
                    : BilderFuerBeleg is { } n
                        ? $" · prüft mit, findet aber nur einen Teil – sicher ab rund {n} Bildern"
                        : " · prüft mit, findet aber nur einen Teil");

        public string Tooltip =>
            $"Gelernt im Bereich: {BereichName}.\n"
            + $"Aus {Grundmenge} Bildern gelernt.\n"
            + $"Eigene Erkennungsschwelle: ab {SchwelleProzent} %.\n"
            + "\n"
            // Das Bild darüber ist der Mittelwert. Dass darin ein Zeichen klar zu sehen
            // ist, sagt nichts darüber, ob es in einem einzelnen Bild zu finden ist:
            // Über 39 Bilder sinkt das Motivrauschen um das Sechsfache (gemessene
            // Streuung 18,3 → 4,9), im Einzelbild steht es voll da. Ohne diesen Satz
            // liest man das scharfe Musterbild als Gütezeichen — und wundert sich, dass
            // trotzdem nichts gefunden wird.
            + $"Das Bild oben ist der Mittelwert über alle {Grundmenge} Beispiele. Darin\n"
            + "tritt ein Zeichen viel deutlicher hervor als in einem einzelnen Bild:\n"
            + "Das Motivrauschen mittelt sich weg, das Zeichen bleibt. Wie gut ein\n"
            + "einzelnes Bild getroffen wird, sagt allein die Trennschärfe.\n"
            + "\n"
            + $"Trennschärfe {TrennschaerfeText}: so gut trifft dieses Muster ein Bild,\n"
            + "das beim Lernen nicht dabei war. Nur diese Zahl sagt etwas über die\n"
            + "Brauchbarkeit. Gemessen erreichen Ordner ohne gemeinsames Zeichen bis\n"
            + "6 %, ein echtes Zeichen 23 bis 37 %. Unter 15 % gilt das Muster als\n"
            + "nicht belegt. Es prüft trotzdem mit, findet aber nur einen Teil:\n"
            + "Bei 7,4 % Trennschärfe waren es gemessen 17 % der Bilder bei 2 %\n"
            + "Fehlalarm. Mehr Bilder desselben Zeichens heben beides.\n"
            + "\n"
            + $"Deutlichkeit {StaerkeText}: der Kontrast des Musters, gemessen in\n"
            + "Graustufen der Skala 0 bis 255. Nach dieser Zahl sucht „alle Bereiche“\n"
            + "die beste Stelle aus. Eine leere oder gleichmässig dunkle Ecke mittelt\n"
            + "sich zu einer flachen Fläche und landet nahe null. Innerhalb eines\n"
            + "Ordners vergleichbar, über Ordner hinweg nicht — ein Ordner ohne\n"
            + "gemeinsames Zeichen erreicht ohne Weiteres denselben Wert.\n"
            + "\n"
            + $"{StabilProzent} % der Bildpunkte blieben über alle Beispiele hinweg\n"
            + "gleich. Diese Zahl taugt nicht als Note: Sie wird auf die mittlere\n"
            + "Streuung normiert und liegt deshalb fast immer nahe 50 %.";
    }
}
