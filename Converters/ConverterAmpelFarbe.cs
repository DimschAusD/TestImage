using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TestImage.Converters
{
    /// <summary>
    /// Eine Farbregel für alle Felder der Prüf-Ampel.
    ///
    /// <list type="bullet">
    /// <item><b>Grau</b> – kein Urteil: nicht geprüft, nicht messbar, oder es fehlt noch
    ///       der Vergleichsmassstab des Ordners.</item>
    /// <item><b>Grün</b> – in Ordnung.</item>
    /// <item><b>Rot</b> – auffällig.</item>
    /// </list>
    ///
    /// <b>Warum ein Konverter statt dreier:</b> Vorher gab es
    /// <c>CLconverterBrushesBoolianG1</c>, <c>…G2</c> und <c>…G5</c>. G5 war eine
    /// wortgleiche Kopie von G1, G2 unterschied sich allein darin, dass es die Bedeutung
    /// von <c>true</c> umdreht. Drei Klassen für eine Regel heisst: Wer die Farbe ändern
    /// will, muss drei Stellen finden — und wer eine übersieht, bekommt eine Ampel mit
    /// zwei verschiedenen Grüntönen.
    ///
    /// <b>Die Umkehrung als Parameter:</b> Bei den meisten Eigenschaften heisst
    /// <c>true</c> „auffällig" (<c>IsBildDateiBeschädigt</c>). Bei zweien heisst es das
    /// Gegenteil (<c>IsHeaderPassendZurErweiterung</c>, <c>IsFrameImBildDrin</c>) — sie
    /// sind positiv formuliert. Statt diese Eigenschaften umzubenennen und damit an
    /// mehreren Stellen im ViewModel einzugreifen, kippt dort
    /// <c>ConverterParameter="Gut"</c> die Zuordnung.
    /// </summary>
    public sealed class ConverterAmpelFarbe : IValueConverter
    {
        /// <summary>Kein Urteil.</summary>
        private static readonly Brush Grau = Frier(Colors.Gainsboro);

        /// <summary>In Ordnung.</summary>
        private static readonly Brush Gruen = Frier(Colors.GreenYellow);

        /// <summary>Auffällig.</summary>
        private static readonly Brush Rot = Frier(Colors.Tomato);

        private static Brush Frier(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Alles, was kein bool ist — auch null —, ist kein Urteil.
            //
            // Die alten Konverter lieferten hier Gelb. Das war eine vierte Farbe ohne
            // Bedeutung: Sie kam nur zustande, wenn etwas anderes als bool? gebunden war,
            // also bei einem Fehler in der Bindung. Grau ist die ehrlichere Antwort.
            if (value is not bool wert)
            {
                return Grau;
            }

            bool positivFormuliert =
                string.Equals(parameter as string, "Gut", StringComparison.OrdinalIgnoreCase);

            bool auffaellig = positivFormuliert ? !wert : wert;

            return auffaellig ? Rot : Gruen;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException("ConverterAmpelFarbe unterstützt kein ConvertBack.");
    }
}
