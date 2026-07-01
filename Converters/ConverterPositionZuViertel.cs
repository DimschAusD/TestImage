using System;
using System.Globalization;
using System.Windows.Data;

namespace TestImage.Converters
{
    internal class ConverterPositionZuViertel : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2)
                return "?";

            int position = System.Convert.ToInt32(values[0]) + 1;
            int count = System.Convert.ToInt32(values[1]);

            if (count <= 0)
                return "?";

            double quote = position / (double)count;

            if (quote <= 0.25) return "1";
            if (quote <= 0.50) return "2";
            if (quote <= 0.75) return "3";
            return "4";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
