using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TestImage.Converters
{
    internal class ConverterPositionZuDreiecke : IMultiValueConverter
    {
        private static readonly Brush BrushVoll = new SolidColorBrush(Color.FromRgb(0x00, 0x4E, 0x8C));
        private static readonly Brush BrushHalb = new SolidColorBrush(Color.FromArgb(115, 0x00, 0x4E, 0x8C));
        private static readonly Brush BrushLeer = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0));

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] == DependencyProperty.UnsetValue || values[1] == DependencyProperty.UnsetValue)
                return BrushLeer;

            int position = System.Convert.ToInt32(values[0]) + 1;
            int count = System.Convert.ToInt32(values[1]);
            int dreieckIndex = System.Convert.ToInt32(parameter);

            if (count <= 0)
                return BrushLeer;

            double quote = position / (double)count;

            // 8 Stufen: 0=leer, 1=D1 halb, 2=D1 voll, 3=D2 halb, ... 8=D4 voll
            int stufe = (int)Math.Ceiling(quote * 8.0);
            if (stufe > 8) stufe = 8;

            int dreieckStufe = stufe - dreieckIndex * 2;

            if (dreieckStufe >= 2)
                return BrushVoll;
            if (dreieckStufe == 1)
                return BrushHalb;
            return BrushLeer;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
