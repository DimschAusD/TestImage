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
    internal class CLconverterColorBoolianMasterHintergrund : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            ////throw new NotImplementedException();
            ///
            //var termin = value as TerminModel;

            if (value == null)
            {
                return Brushes.Transparent;
            }

            if (value is bool termin)
            {
                //return Brushes.Transparent;
                if (termin)
                {
                    return Brushes.HotPink;
                }
                else
                {
                    return Brushes.DimGray;
                }


            }
            else
            {
                return Brushes.LightCyan;


            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
