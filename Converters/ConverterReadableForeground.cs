using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TestImage.Converters
{
    /// <summary>
    /// Wählt zur Hintergrundfarbe die lesbare Schriftfarbe.
    ///
    /// <b>Warum nicht der Mittelwert aus R, G, B:</b> Gerechnet wird die
    /// <i>wahrgenommene</i> Helligkeit nach ITU-R BT.709. Grün trägt dort mit 0,72 fast
    /// das Siebenfache von Blau bei (0,07). Ein sattes Blau ist für das Auge deutlich
    /// dunkler als ein sattes Grün, obwohl beide denselben Zahlenwert haben — ein
    /// einfacher Mittelwert läge bei genau diesen Farben falsch.
    ///
    /// Die Schwelle liegt bei 0,6 statt 0,5: Dunkle Schrift bleibt auf mittelhellem
    /// Grund besser lesbar als helle.
    ///
    /// <b>Grenze:</b> Der Konverter sieht nur den Pinsel, nicht das Ergebnis auf dem
    /// Schirm. Wird eine Fläche über <c>Opacity</c> aufgehellt — wie die Felder der
    /// groben Zeitleiste —, meldet er die Farbe des Pinsels und liegt daneben. Dafür
    /// bräuchte es einen Konverter mit zwei Eingängen.
    /// </summary>
    public sealed class ConverterReadableForeground : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not SolidColorBrush hintergrund)
            {
                return SystemColors.ControlTextBrush;   // neutraler Rückfall
            }

            Color c = hintergrund.Color;

            double helligkeit =
                (0.2126 * c.R +
                 0.7152 * c.G +
                 0.0722 * c.B) / 255.0;

            return helligkeit < 0.6
                ? SystemColors.ControlLightLightBrush   // sehr hell, aber nicht grell
                : SystemColors.ControlTextBrush;        // Standard-Textfarbe
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException("ConverterReadableForeground unterstützt kein ConvertBack.");
    }
}
