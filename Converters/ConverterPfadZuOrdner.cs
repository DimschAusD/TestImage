using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace TestImage.Converters
{
    /// <summary>
    /// Macht aus dem vollen Dateipfad den Ordner — wahlweise ganz oder gekürzt.
    ///
    /// Gegenstück zu <c>ConverterFullPathToNameConverter</c>, der den Dateinamen liefert.
    /// Beide arbeiten auf derselben Bindung (<c>SelectedBildchen.BName</c>), damit die
    /// Ansicht keine zweite Eigenschaft im ViewModel braucht, die nachgeführt werden muss.
    ///
    /// <b>Ohne Parameter:</b> der vollständige Ordnerpfad, für den Tooltip.
    ///
    /// <b>Mit <c>ConverterParameter="Kurz"</c>:</b> die letzten beiden Stufen mit
    /// vorangestelltem Auslassungszeichen, also etwa <c>…\Künzler_Bilder\Künzler1</c>.
    ///
    /// <b>Warum von vorn gekürzt wird und nicht hinten:</b> WPF kann mit
    /// <c>TextTrimming</c> nur am Ende kürzen — dort steht aber der Ordnername, also
    /// genau die Auskunft, um die es geht. Bei „wo bin ich" hilft <c>U:\Sicherung\Bil…</c>
    /// niemandem.
    /// </summary>
    public sealed class ConverterPfadZuOrdner : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string pfad || string.IsNullOrWhiteSpace(pfad))
            {
                return string.Empty;
            }

            string? ordner;
            try
            {
                ordner = Path.GetDirectoryName(pfad);
            }
            catch
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(ordner))
            {
                return string.Empty;
            }

            bool kurz = string.Equals(parameter as string, "Kurz", StringComparison.OrdinalIgnoreCase);
            if (!kurz)
            {
                return ordner;
            }

            var teile = ordner.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            // Zwei Stufen oder weniger passen ohnehin: „U:\Bilder" bleibt „U:\Bilder".
            if (teile.Length <= 2)
            {
                return ordner;
            }

            return "…" + Path.DirectorySeparatorChar
                       + teile[^2] + Path.DirectorySeparatorChar + teile[^1];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException("ConverterPfadZuOrdner unterstützt kein ConvertBack.");
    }
}
