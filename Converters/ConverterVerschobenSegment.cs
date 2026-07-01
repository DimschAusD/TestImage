using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TestImage.Converters
{
    internal class ConverterVerschobenSegment : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {

            if (values.Length < 2)
            {
                return Brushes.LightGray;
            }

            int verschoben = System.Convert.ToInt32(values[0]);
            int gesamt = System.Convert.ToInt32(values[1]);

            if (gesamt == 0)
            {
                return Brushes.LightGray;
            }

            double quote = verschoben / (double)gesamt;

            int segment = int.Parse(parameter!.ToString()!);

            double segmentStart = (segment - 1) / 4.0;
            double segmentMid = (segment - 0.5) / 4.0;
            double segmentEnd = segment / 4.0;

            if (quote <= segmentStart)
                return Brushes.LightGray;

            if (quote >= segmentEnd)
                return new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));

            if (quote >= segmentMid)
                return new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00));

            return Brushes.Gold;

        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
