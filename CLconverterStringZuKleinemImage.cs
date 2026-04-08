using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestImage
{
    internal class CLconverterStringZuKleinemImage : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {

            BitmapImage bmp = new BitmapImage();
            try
            {
                bmp.BeginInit();
                bmp.UriSource = new Uri((string)value, UriKind.Relative);
                bmp.DecodePixelWidth = 120;
                //RenderTransform = new ScaleTransform(1.5, 2.0), // X=150%, Y=200%
                //RenderTransformOrigin = new Point(0.5, 0.5)
                
                // Datei komplett in den Speicher laden
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze(); // Thread-sicher machen und Ressourcen freigeben
            }
            catch (Exception)
            {

                // Erstelle ein leeres DrawingVisual
                //var borderColor = System.Drawing.Color.FromArgb(255, 0, 0, 0); // Schwarz
                //var borderColor = System.Windows.Media.Brushes.Black.Color; // Schwarz
                int width = 200;
                int height = 200;
                var borderWidth = 2;
                DrawingVisual drawingVisual = new DrawingVisual();
                using (DrawingContext drawingContext = drawingVisual.RenderOpen())
                {
                    // Hintergrund (optional)
                    drawingContext.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(0, 0, width, height));

                    // Rahmen zeichnen
                    System.Windows.Media.Pen borderPen = new System.Windows.Media.Pen() { Thickness = 3, Brush = System.Windows.Media.Brushes.Yellow };
                    drawingContext.DrawRectangle(null, borderPen, new Rect(borderWidth / 2.0, borderWidth / 2.0, width - borderWidth, height - borderWidth));
                }

                // RenderTargetBitmap erstellen
                RenderTargetBitmap renderBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                renderBitmap.Render(drawingVisual);

                // Bild für die ObservableCollection
                ImageSource imageHiden = renderBitmap;
                return imageHiden;
            }
           

           return bmp; // Image-Steuerelement setzen
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
