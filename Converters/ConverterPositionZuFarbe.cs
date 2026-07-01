using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TestImage.Converters
{
    internal class ConverterPositionZuFarbe : IMultiValueConverter
    {
        private static readonly Brush BrushGrau = Brushes.LightGray;
        private static readonly Brush BrushGelb = Brushes.Gold;
        private static readonly Brush BrushOrange = new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00));
        private static readonly Brush BrushBlau = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));
        private static readonly Brush BrushDunkelBlau = new SolidColorBrush(Color.FromRgb(0x00, 0x4E, 0x8C));

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2)
                return BrushGrau;

            int position = System.Convert.ToInt32(values[0]) + 1;
            int count = System.Convert.ToInt32(values[1]);

            if (count <= 0)
                return BrushGrau;

            double quote = position / (double)count;

            if (quote <= 0.25) return BrushGelb;
            if (quote <= 0.50) return BrushOrange;
            if (quote <= 0.75) return BrushBlau;
            return BrushDunkelBlau;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
