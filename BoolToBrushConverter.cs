using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace TestImage
{
    internal class BoolToBrushConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            //throw new NotImplementedException();
            //if (value is bool boolValue)
            //{
            //    return boolValue ? System.Windows.Media.Brushes.LightBlue : System.Windows.Media.Brushes.Transparent;
            //}
            return (bool)value ? Brushes.Red : Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
