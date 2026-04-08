using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace TestImage
{
    internal class CLconverterStringZuImage : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // throw new NotImplementedException();

            BitmapImage bmp = new BitmapImage();
            try
            {
                using (var fs = new FileStream((string)value, FileMode.Open, FileAccess.Read, FileShare.Read))
                {

                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = fs;
                    bmp.EndInit();
                    bmp.Freeze();
                    //myImage.Source = bmp;

                    return bmp;

                }
            }
            catch (Exception)
            {

                //throw;
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
