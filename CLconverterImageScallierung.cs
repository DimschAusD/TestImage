using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestImage
{
    public class CLconverterImageScallierung : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            //throw new NotImplementedException();
            if (value==null)
            {
                return 0;
            }

            if (value is Image img /*&& parameter is string paramStr && double.TryParse(paramStr, out double scale)*/)
            {
                //var scaledWidth = imgSource.Width;
                //var scaledHeight = imgSource.Height /** scale;*/   ;

                if (img.Source is BitmapSource bmp)
                {
                    double scaleX = img.ActualWidth / bmp.PixelWidth;
                    double scaleY = img.ActualHeight / bmp.PixelHeight;
                    return (scaleX, scaleY);
                }
                return (1.0, 1.0);

            }

            if (value is ScaleTransform st)
            {
                return (st.ScaleX, st.ScaleY);
            }
            else if (value is TransformGroup tg)
            {
                var scaleTransform = tg.Children.OfType<ScaleTransform>().FirstOrDefault();
                if (scaleTransform != null)
                {
                    return (scaleTransform.ScaleX, scaleTransform.ScaleY);
                }
            }

            return "12h x 14w";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
