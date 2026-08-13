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
        /// Anteil der Bildpunkte, die über alle Beispiele gleich blieben. Grobes Mass
        /// dafür, wie klar das Muster ist: Beim DeviantArt-Zeichen bleibt das Logo
        /// stabil, der Künstlername darunter wechselt und drückt den Wert.
        /// </summary>
        public int StabilProzent { get; set; }

        /// <summary>
        /// Das gelernte Muster als Bild. Zeigt den Mittelausschnitt, gewichtet mit der
        /// Stabilität – der feste Teil des Zeichens steht klar da, der wechselnde
        /// verblasst. <c>null</c>, wenn sich kein brauchbares Bild ergab.
        /// </summary>
        public ImageSource? Vorschau { get; set; }

        /// <summary>Knapp gehalten – die Liste kann lang werden, das Wort „stabil" steht im Tooltip.</summary>
        public string Beschreibung => $"{Grundmenge} Bilder · {StabilProzent} %";

        public string Tooltip =>
            $"Aus {Grundmenge} Bildern gelernt.\n" +
            $"{StabilProzent} % der Bildpunkte blieben über alle Beispiele hinweg gleich.\n" +
            "Niedrige Werte heissen: Die Beispiele hatten wenig gemeinsam – dann besser\n" +
            "einen Ordner nehmen, in dem wirklich nur ein Zeichentyp vorkommt.";
    }
}
