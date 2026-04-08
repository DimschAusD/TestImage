using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace TestImage
{
    [ValueConversion(typeof(bool), typeof(Brush))]
    internal class CLconverterBrushesBoolian : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            //throw new NotImplementedException();
            if (value is null)
            {
                return Brushes.Fuchsia;
            }

            if (value is bool boolValue)
            {
                return boolValue ? Brushes.GhostWhite : Brushes.Transparent;
            }

            return Brushes.Yellow;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
