using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace TestImage.Converters
{
    internal class CLconverterStringZuMB : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            //throw new NotImplementedException();
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
            {
                return "0.0";
            }
            else
            {
                if (value is string vollpfad)
                {
                    try
                    {
                        var fileInfo = new System.IO.FileInfo(vollpfad);
                        //double megabytes = fileInfo.Length / (1024.0 * 1024.0);
                        //return megabytes.ToString("F2", CultureInfo.InvariantCulture) + " MB";

                        double val = fileInfo.Length;
                        string[] exts = new string[] { "B", "Kb", "Mb", "Gb", "Tb", "Pb", "Xb", "Yb", "Zb" };
                        int index = 0;
                        while (val >= 1024)
                        {
                            val /= 1024;
                            index++;
                            if (index >= exts.Length - 1)
                            {
                                break;
                            }
                        }

                        string zg = $" {val:0.00} {exts[index]} ";


                        return zg;


                        //return megabytes.ToString("F2", CultureInfo.InvariantCulture) + " MB";
                    }
                    catch (Exception)
                    {
                            return "0.0";
                    }
                   


                }
                else
                {
                    return "12.12 mb";
                }
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
