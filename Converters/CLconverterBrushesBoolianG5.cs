using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;

namespace TestImage.Converters
{
    internal class CLconverterBrushesBoolianG5 : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // throw new NotImplementedException();
            if (value is null)
            {
                return Brushes.Gainsboro;
            }

            if (value is bool boolValue)
            {
                return boolValue ? Brushes.Tomato : Brushes.GreenYellow;
            }

            return Brushes.Yellow;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
