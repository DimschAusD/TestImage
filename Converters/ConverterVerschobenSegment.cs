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

            double grenze = segment / 4.0;

            return quote >= grenze
                ? Brushes.Gold
                : Brushes.LightGray;

        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
