using System.IO;
using System.Windows.Media.Imaging;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Lädt eine Bilddatei als eingefrorene Bitmap in Originalgrösse.
    ///
    /// <b>Eine Stelle für alle.</b> Die Koordinaten aller Funde — der Wortkästen der
    /// Windows-OCR wie der Zeilenrahmen des Plugins — beziehen sich auf genau diese
    /// Bitmap. Lädt eine zweite Stelle die Datei mit anderer Grösse oder anderer
    /// EXIF-Drehung, sitzen deren Rahmen neben dem Text. Deshalb gibt es den Lader
    /// einmal und nicht in jedem Dienst.
    /// </summary>
    internal static class BildLader
    {
        /// <summary>
        /// Lädt und friert ein. Eingefroren, weil die Bitmap auf einem
        /// Hintergrundfaden entsteht und im Anzeige-Faden gezeichnet wird — ein nicht
        /// eingefrorenes Freezable gehört dem Faden, der es erzeugt hat.
        /// </summary>
        internal static BitmapSource Lade(string pfad)
        {
            var bild = new BitmapImage();

            using (var strom = new FileStream(pfad, FileMode.Open, FileAccess.Read))
            {
                bild.BeginInit();
                bild.CacheOption = BitmapCacheOption.OnLoad;   // Datei nicht gesperrt halten
                bild.StreamSource = strom;
                bild.EndInit();
            }

            bild.Freeze();
            return bild;
        }
    }
}
