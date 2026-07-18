using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace TestImage.Converters
{
    internal class ConverterFullPathToNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            //throw new NotImplementedException();

            if (value is not string fullPath || string.IsNullOrWhiteSpace(fullPath))
            {
                return value;
            }

            // Entfernt evtl. abschließende \ oder /
            fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return Path.GetFileName(fullPath);

        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
