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
        /// aussucht, und damit das brauchbare Gütemass — anders als
        /// <see cref="StabilProzent"/>. Eine leere oder gleichmässig dunkle Ecke mittelt
        /// sich zu einer flachen Fläche und landet nahe null, auch wenn dort viele
        /// Bildpunkte „stabil" waren.
        /// </summary>
        public double Staerke { get; set; }

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
        public string Beschreibung => $"{BereichName} · {Grundmenge} Bilder · Deutlichkeit {StaerkeText} · ab {SchwelleProzent} %";

        public string Tooltip =>
            $"Gelernt im Bereich: {BereichName}.\n"
            + $"Aus {Grundmenge} Bildern gelernt.\n"
            + $"Eigene Erkennungsschwelle: ab {SchwelleProzent} %.\n"
            + "\n"
            + $"Deutlichkeit {StaerkeText}: der Kontrast des Musters, gemessen in\n"
            + "Graustufen der Skala 0 bis 255. Nach dieser Zahl sucht „alle Bereiche“\n"
            + "die beste Stelle aus. Eine leere oder gleichmässig dunkle Ecke mittelt\n"
            + "sich zu einer flachen Fläche und landet nahe null. Der Wert ist zum\n"
            + "Vergleichen da, nicht als Note mit fester Grenze.\n"
            + "\n"
            + $"{StabilProzent} % der Bildpunkte blieben über alle Beispiele hinweg\n"
            + "gleich. Diese Zahl taugt nicht als Note: Sie wird auf die mittlere\n"
            + "Streuung normiert und liegt deshalb fast immer nahe 50 %.";
    }
}
