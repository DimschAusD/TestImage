using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TestImageViewer.Converters
{
    /// <summary>true -> Rot (Gerät aktiv), false -> Grau (inaktiv).</summary>
    public class ConverterAktivZuFarbe : IValueConverter
    {
        private static readonly Brush Grau = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
        private static readonly Brush Rot = new SolidColorBrush(Color.FromRgb(0xE0, 0x39, 0x39));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Rot : Grau;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
