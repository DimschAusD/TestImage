using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace TestImage.Converters
{
    internal class CLconverterCountBildchenFuerLinks : IValueConverter
    {
      
// Zählt wie viele Bildchen in AufgabenView das Flag BildFürLinks == true haben.
// AufgabenView ist eine ListCollectionView mit einer ObservableCollection<MeinBildchen>.
// Soll eine int zurückgeben.

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // throw new NotImplementedException();

            if (value is IEnumerable<MeinBildchen> liste)
            {
                return liste.Count(x => x.BildFürLinks);
            }
            return 0;


            //var items = AufgabenView.Cast<MeinBildchen>();
            //return items.Count(x => x.BildFürLinks);




            //}
            //return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
