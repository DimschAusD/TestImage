using System;
using System.Globalization;
using System.Windows.Data;

namespace TestImage.Converters
{
    /// <summary>
    /// Wandelt einen Dateipfad in das Windows-Dateisymbol um, damit in Listen sofort
    /// erkennbar ist, ob eine Zeile eine PDF, ein JPG oder ein Archiv meint.
    ///
    /// Die eigentliche Arbeit macht <see cref="FileIconProvider"/> samt Cache je
    /// Endung — der Converter läuft im UI-Thread und darf deshalb nichts Teures tun.
    /// </summary>
    public sealed class CLconverterStringZuDateiIcon : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => FileIconProvider.HoleIcon(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
