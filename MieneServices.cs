using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Features2D;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;


namespace TestImage
{
    internal static class MieneServices
    {
        #region Bild prüfen

        /// <summary>
        /// Determines whether the specified image file is corrupted or not a valid image format.
        /// </summary>
        /// <remarks>This method attempts to load the file as an image. If the file is not a
        /// valid image, is corrupted, or does not exist, the method returns true. Otherwise, it returns
        /// false.</remarks>
        /// <param name="dateiPfad">The full path to the image file to check. Cannot be null or an empty string.</param>
        /// <returns>true if the file is corrupted, not a valid image, or cannot be found; otherwise, false.</returns>
        internal static bool IsBildDateiBeschädigt(string dateiPfad)
        {
            //throw new NotImplementedException();
            try
            {
                if (!dateiPfad.EndsWith(".webp"))
                {
                    using (var img = System.Drawing.Image.FromFile(dateiPfad))
                    {
                        // Bild ist gültig
                    }
                }
                else
                {
                    // WebP-Bilder müssen mit einem speziellen Decoder geladen werden, da WPF sie nicht nativ unterstützt.
                    // Wir versuchen, das Bild mit BitmapImage zu laden, was eine Exception wirft, wenn es beschädigt ist.
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(dateiPfad);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze(); // Optional: macht das Bild thread-sicher

                    return false;
                }

                return false;
            }
            catch (OutOfMemoryException)
            {
                // Bild ist beschädigt oder kein gültiges Bildformat
                return true;
            }
            catch (FileNotFoundException)
            {
                // Datei nicht gefunden
                return true;
            }
            catch (Exception)
            {
                // Andere Fehler
                return true;
            }
        }


        // Definition der Magic Bytes für gängige Formate
        private static readonly Dictionary<string, byte[]> ImageHeaders
            = new()
            {
                { "JPG", new byte[] { 0xFF, 0xD8, 0xFF } },
                { "PNG", new byte[] { 0x89, 0x50, 0x4E, 0x47 } },
                { "GIF", new byte[] { 0x47, 0x49, 0x46, 0x38 } },
                { "BMP", new byte[] { 0x42, 0x4D } }
            };

        /// <summary>
        /// Determines whether the specified file begins with a recognized image file header signature. 
        /// <br>Prüft den Header, ob der zur Datei Erweiterung passt</br>
        /// </summary>
        /// <remarks>This method checks the first few bytes of the file against known image header
        /// signatures to identify common image formats. If the file does not exist or cannot be read, the method
        /// returns false.</remarks>
        /// <param name="filePath">The path to the file to check for a valid image header. Cannot be null or empty.</param>
        /// <returns>true if the file exists and its header matches a known image format signature; otherwise, false.</returns>
        internal static bool HasValidImageHeader(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            // Wir lesen nur die ersten 8 Bytes
            byte[] buffer = new byte[8];
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    //fs.Read(buffer, 0, buffer.Length);
                    fs.ReadExactly(buffer, 0, buffer.Length);
                }

                // Prüfen, ob der Puffer mit einer der Signaturen beginnt
                return ImageHeaders.Any(header =>
                    buffer.Take(header.Value.Length).SequenceEqual(header.Value));
            }
            catch
            {
                return false;
            }
        }


        internal static bool IsHeaderPassendZurErweiterung(string filePath)
        {


            if (!File.Exists(filePath))
            {
                return false;
            }

            var ext = Path.GetExtension(filePath).ToLowerInvariant().TrimStart('.'); // "webp", "png", ...

            // Für WebP brauchen wir mindestens 12 Bytes; für die meisten anderen reichen 8.
            int minBytes = ext == "webp" ? 12 : 8;
            byte[] buffer = new byte[minBytes];

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
#if NET8_0_OR_GREATER
                    fs.ReadExactly(buffer, 0, buffer.Length);
#else
            int read = fs.Read(buffer, 0, buffer.Length);
            if (read < buffer.Length) return false;
#endif
                }

                switch (ext)
                {
                    case "webp":
                        // Prüfen: "RIFF"...."WEBP" (Bytes 4–7 werden ignoriert, da Größe)
                        return buffer.Length >= 12
                            && buffer[0] == (byte)'R' && buffer[1] == (byte)'I'
                            && buffer[2] == (byte)'F' && buffer[3] == (byte)'F'
                            && buffer[8] == (byte)'W' && buffer[9] == (byte)'E'
                            && buffer[10] == (byte)'B' && buffer[11] == (byte)'P';

                    default:
                        // Bisherige Logik über Dictionary (Beispiel): 
                        // ImageHeaders["PNG"] = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
                        var key = ext.ToUpperInvariant();
                        if (!ImageHeaders.TryGetValue(key, out var magic) || magic == null)
                        {
                            return false;
                        }

                        int n = Math.Min(buffer.Length, magic.Length);
                        return buffer.Take(n).SequenceEqual(magic);
                }
            }
            catch
            {
                return false;
            }

            // vorher
            //if (!File.Exists(filePath)) return false;

            //var erweiterung = System.IO.Path.GetExtension(filePath).ToLower();

            //// Wir lesen nur die ersten 8 Bytes
            //byte[] buffer = new byte[8];
            //try
            //{
            //    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            //    {
            //        fs.ReadExactly(buffer, 0, buffer.Length);
            //    }

            //    var key = erweiterung.TrimStart('.').ToUpper();

            //    if (!ImageHeaders.TryGetValue(key, out var ef2) || ef2 == null)
            //    {
            //        return false;
            //    }

            //    return buffer.Take(ef2.Length).SequenceEqual(ef2);
            //}
            //catch
            //{
            //    return false;
            //}
        }

        internal static bool IsFrameImBildDrin(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

                // Für reine Validierung genügt in der Regel None; OnLoad lädt alles in den RAM.
                var decoder = BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.None,
                    BitmapCacheOption.None);

                // Gültige Bildformate haben mindestens 1 Frame
                return decoder.Frames != null && decoder.Frames.Count > 0;
            }
            catch (FileFormatException) // ungültiges/korruptes Bild oder Header passt nicht
            {
                return false;
            }
            catch (NotSupportedException) // kein passender WIC-Decoder vorhanden (z. B. fehlender WebP-Codec)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch
            {
                return false;
            }


            // vorher
            //try
            //{
            //    using (var stream = File.OpenRead(filePath))
            //    {
            //        // Ein BitmapDecoder erkennt, ob der Datei-Header zu einem Bildformat passt
            //        var decoder = BitmapDecoder.Create(
            //            stream,
            //            BitmapCreateOptions.None,
            //            BitmapCacheOption.OnLoad);

            //        // Ein gültiges Bild muss mindestens einen Frame (das eigentliche Bild) enthalten.
            //        // Wir prüfen also, ob die Anzahl der Frames größer als 0 ist.
            //        return decoder.Frames.Count > 0;
            //    }
            //}
            //catch (Exception)
            //{
            //    // Falls die Datei korrupt ist oder kein Bildformat hat, 
            //    // wirft der Decoder eine Exception.
            //    return false;
            //}
        }


        internal static bool IsBildDownloadCorrupted(string bildpfad)
        {
            // Create source            BitmapSource bitmap
            BitmapImage bitmap = new BitmapImage();

            try
            {
                // BitmapImage.UriSource must be in a BeginInit/EndInit block
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(bildpfad);

                // To save significant application memory, set the DecodePixelWidth or
                // DecodePixelHeight of the BitmapImage value of the image source to the desired
                // height or width of the rendered image. If you don't do this, the application will
                // cache the image as though it were rendered as its normal size rather than just
                // the size that is displayed.
                // Note: In order to preserve aspect ratio, set DecodePixelWidth
                // or DecodePixelHeight but not both.
                //myBitmapImage.DecodePixelWidth = 120;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze(); // Optional: Makes the image thread-safe
            }
            catch (Exception)
            {
                // Handle exceptions (e.g., file not found, invalid image format)
                return true; // Consider it corrupted if we can't load it
            }


            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int stride = width * 4;
            byte[] lastPixels = new byte[stride]; // Nur die letzte Zeile prüfen

            // Wir kopieren nur die letzte Zeile des Bildes
            Int32Rect lastRowRect = new Int32Rect(0, height - 1, width, 1);
            bitmap.CopyPixels(lastRowRect, lastPixels, stride, 0);

            int grayCount = 0;
            for (int x = 0; x < width; x++)
            {
                int i = x * 4;
                // Prüfung auf das typische "Download-Grau" (oft R=128, G=128, B=128)
                if (lastPixels[i] == 128 && lastPixels[i + 1] == 128 && lastPixels[i + 2] == 128)
                {
                    grayCount++;
                }
            }

            // Wenn mehr als 90% der letzten Zeile exakt grau sind, ist der Download fast sicher defekt
            return grayCount > (width * 0.9);
        }

        internal static bool IsBildNullDatei(string bName)
        {
            if (bName == null || string.IsNullOrWhiteSpace(bName))
            {
                return false;
            }

            try
            {
                FileInfo fi = new(bName);

                if (fi.Length == 0)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
            catch (Exception)
            {

                return false;
            }




        }



        #endregion

        internal static Task<ImageSource> KleinesBildchenLaden(string bildpfad)
        {
            // Create source            BitmapSource bitmap
            BitmapImage bitmap = new BitmapImage();

            try
            {
                // BitmapImage.UriSource must be in a BeginInit/EndInit block
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(bildpfad);

                bitmap.DecodePixelWidth = 120;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze(); // Optional: Makes the image thread-safe

                ImageSource bildchen = NormalizeDpi(bitmap);
                return Task.FromResult(bildchen);
            }
            catch (Exception)
            {
                // Handle exceptions (e.g., file not found, invalid image format)
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
                //return imageHiden;
                if (imageHiden is BitmapSource bs && bs.CanFreeze)
                {
                    bs.Freeze();
                }

                return Task.FromResult(imageHiden);
            }



        }


        internal static Task<ImageSource> GrossesBildchenLaden(string bildpfad)
        {
            // Create source            BitmapSource bitmap
            BitmapImage bitmap = new BitmapImage();

            try
            {
                // BitmapImage.UriSource must be in a BeginInit/EndInit block
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(bildpfad);

                //bitmap.DecodePixelWidth = 120;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze(); // Optional: Makes the image thread-safe

                ImageSource bildchen = NormalizeDpi(bitmap);
                return Task.FromResult(bildchen);
            }
            catch (Exception)
            {
                // Handle exceptions (e.g., file not found, invalid image format)
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
                //return imageHiden;
                if (imageHiden is BitmapSource bs && bs.CanFreeze)
                {
                    bs.Freeze();
                }

                return Task.FromResult(imageHiden);
            }


        }

        internal static BitmapSource CreateBitmap(string path, int? decodeWidth = null, int? decodeHeight = 0)
        {
            try
            {
                if (!path.EndsWith(".webp"))
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        if (decodeWidth.HasValue)
                        {
                            bitmap.DecodePixelWidth = decodeWidth.Value;
                        }
                        if (decodeHeight.HasValue)
                        {
                            bitmap.DecodePixelHeight = decodeHeight.Value;
                        }
                        bitmap.EndInit();
                        if (bitmap.CanFreeze)
                        {
                            bitmap.Freeze();
                        }

                        return NormalizeDpi(bitmap);
                    }
                }
                else
                {
                    // WebP-Bilder müssen mit einem speziellen Decoder geladen werden, da WPF sie nicht nativ unterstützt.


                    if (string.IsNullOrWhiteSpace(path))
                    {
                        throw new ArgumentException("Pfad ist leer.", nameof(path));
                    }

                    if (!File.Exists(path))
                    {
                        throw new FileNotFoundException("Datei nicht gefunden.", path);
                    }

                    if (decodeWidth is int w && w <= 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(decodeWidth), "decodeWidth muss > 0 sein.");
                    }

                    // BitmapImage verwenden, um DecodePixelWidth effizient an den Decoder zu übergeben
                    var bmp = new BitmapImage();
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad; // wichtig: vollständiges Laden jetzt
                        bmp.CreateOptions = BitmapCreateOptions.PreservePixelFormat;

                        // Vom Dateisystem entkoppeln: Stream angeben
                        bmp.StreamSource = fs;

                        // Effizientes Downscaling (wenn Decoder es unterstützt; WIC tut das i. d. R.)
                        if (decodeWidth.HasValue)
                        {
                            bmp.DecodePixelWidth = decodeWidth.Value;
                        }

                        if (decodeHeight.HasValue)
                        {
                            bmp.DecodePixelHeight = decodeHeight.Value;
                        }

                        bmp.EndInit();
                    }

                    // Jetzt ist alles im Speicher; Datei freigegeben
                    if (bmp.PixelWidth == 0 || bmp.PixelHeight == 0)
                    {
                        throw new InvalidDataException("Bild konnte nicht dekodiert werden (möglicherweise beschädigt oder Codec fehlt).");
                    }

                    bmp.Freeze(); // threadsicher
                    return NormalizeDpi(bmp);
                    // return bmp;

                }

            }
            catch
            {
                // return BitmapSource.Create(200, 200, 96, 96, PixelFormats.Pbgra32, null, new byte[] { 0 }, 1);
                var pixelFormat = PixelFormats.Pbgra32;
                int bytesPerPixel = (pixelFormat.BitsPerPixel + 7) / 8; // 4
                int stride = 200 * bytesPerPixel;
                // alle Pixel = transparent (0)
                var pixels = new byte[stride * 200];
                //return BitmapSource.Create(200, 200, 96, 96, pixelFormat, null, pixels, stride);
                var bmp = BitmapSource.Create(200, 200, 96, 96, pixelFormat, null, pixels, stride);
                if (bmp.CanFreeze)
                {
                    bmp.Freeze();   // <-- WICHTIG: macht das Objekt thread-sicher
                }

                return NormalizeDpi(bmp);
            }


        }

        /// <summary>
        /// Normalisiert ein BitmapSource auf 96 DPI, damit WPF-Darstellungsgröße konsistent ist.
        /// Gibt das Original zurück, wenn bereits 96 DPI.
        /// </summary>
        /// <param name="src"></param>
        /// <returns></returns>
        private static BitmapSource NormalizeDpi(BitmapSource src)
        {
            if (src == null)
            {
                return null!;
            }

            // Bereits 96 DPI -> nichts tun
            if (Math.Abs(src.DpiX - 96.0) < 0.01 && Math.Abs(src.DpiY - 96.0) < 0.01)
            {
                if (src.CanFreeze)
                {
                    src.Freeze();
                }

                return src;
            }

            int px = src.PixelWidth;
            int py = src.PixelHeight;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(src, new Rect(0, 0, px, py));
            }

            var rtb = new RenderTargetBitmap(px, py, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            if (rtb.CanFreeze)
            {
                rtb.Freeze();
            }

            return rtb;
        }




        // Asynchrone Funktion, um eine Datei zu kopieren und löschen der original datei mit abbruchunterstützung 
        public static async Task CopyAndDeleteFileAsync(string sourceFilePath, string destFilePath, CancellationToken cancellationToken)
        {
            try
            {
                // Datei kopieren


                //byte[] data = await File.ReadAllBytesAsync(sourcePath);
                //await File.WriteAllBytesAsync(destinationPath, data);

                // kleine Dateien 
                // mittlere Dateien
                // 81920 Grosse Dateien  = 80 * 1024



                const int bufferSize = 8 * 1024;

                using (var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var destStream = new FileStream(destFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await sourceStream.CopyToAsync(destStream, bufferSize, cancellationToken);
                }

                // Zeitstempel übernehmen (nachdem Streams zu sind)
                File.SetLastWriteTime(destFilePath, File.GetLastWriteTime(sourceFilePath));
                File.SetCreationTime(destFilePath, File.GetCreationTime(sourceFilePath));

                //// Originaldatei löschen
                //if (File.Exists(destFilePath))
                //{
                //    File.Delete(sourceFilePath);
                //}



                // ✅ ROBUSTES LÖSCHEN
                const int maxRetries = 5;
                for (int i = 0; i < maxRetries; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (File.Exists(sourceFilePath))
                        {
                            File.Delete(sourceFilePath);
                        }

                        return;
                    }
                    catch (IOException)
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                }




                //// Puffergroesse 8 KB ist oft optimal für kleine Dateien
                //int bufferSize2 = 8 * 1024;
                //using (FileStream input = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read)) 
                //using (FileStream output = new FileStream(destFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                //{
                //    byte[] buffer = new byte[bufferSize];
                //    int bytesRead;
                //    while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
                //    {
                //        output.Write(buffer, 0, bytesRead);
                //    }
                //}


                // Original Datei wird nicht immer gelöscht
                // Schreib mir mall eine Lösung




            }

            catch (OperationCanceledException)
            {
                if (File.Exists(destFilePath))
                {
                    File.Delete(destFilePath);
                }

                throw;
            }

            catch (Exception ex)
            {
                // Fehlerbehandlung
                Console.WriteLine($"Fehler beim Kopieren und Löschen der Datei: {ex.Message}");
            }
        }


        internal static async Task<bool> IsFileGleichAsync(string quelle, string ziel, CancellationToken token)
        {
            //throw new NotImplementedException();

            // 4096
            const int bufferSize = 1024 * 1024;
            //using var stream1 = new FileStream(quelle, FileMode.Open, FileAccess.Read);
            //using var stream2 = new FileStream(ziel, FileMode.Open, FileAccess.Read);
            //if (stream1.Length != stream2.Length)
            //{
            //    return false;
            //}
            ////Span<byte> buffer1 = new byte[bufferSize];
            //byte[] buffer1 = new byte[1024];
            //byte[] buffer2 = new byte[1024];
            ////int bytesRead;
            ////Span<byte> buffer1 = new byte[bufferSize];
            ////Span<byte> buffer2 = new byte[bufferSize];

            //while (true)
            //{
            //    var bytesRead1 = await stream1.ReadAsync(buffer1.AsMemory(0, buffer1.Length), token);
            //    var bytesRead2 = await stream2.ReadAsync(buffer2.AsMemory(0, buffer2.Length), token);
            //    if (bytesRead1 != bytesRead2)
            //    {
            //        return false;
            //    }
            //    if (bytesRead1 == 0)
            //    {
            //        return true;
            //    }
            //    if (!buffer1.SequenceEqual(buffer2))
            //    {
            //        return false;
            //    }
            //}


            if (!File.Exists(quelle) || !File.Exists(ziel))
            {
                throw new FileNotFoundException("Eine oder beide Dateien existieren nicht.");
            }

            using (var stream1 = new FileStream(quelle, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true))
            using (var stream2 = new FileStream(ziel, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true))
            {
                if (stream1.Length != stream2.Length)
                {
                    return false;
                }

                byte[] buffer1 = new byte[bufferSize];
                byte[] buffer2 = new byte[bufferSize];

                int bytesRead1, bytesRead2;
                do
                {
                    // buffer1, 0, buffer1.Length
                    bytesRead1 = await stream1.ReadAsync(buffer1.AsMemory(0, buffer1.Length), token);
                    bytesRead2 = await stream2.ReadAsync(buffer2.AsMemory(0, buffer2.Length), token);

                    if (bytesRead1 != bytesRead2 || !buffer1.AsSpan(0, bytesRead1).SequenceEqual(buffer2.AsSpan(0, bytesRead2)))
                    {
                        return false;
                    }
                } while (bytesRead1 > 0);

                return true;
            }
        }

        internal static async Task<bool> IsFileGleich2Async(FileStream stream1, string bName, CancellationToken token)
        {
            //throw new NotImplementedException();
            const int bufferSize = 1024 * 1024;

            using (var stream2 = new FileStream(bName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true))
            {
                if (stream1.Length != stream2.Length)
                {
                    return false;
                }

                byte[] buffer1 = new byte[bufferSize];
                byte[] buffer2 = new byte[bufferSize];

                int bytesRead1, bytesRead2;
                do
                {
                    // buffer1, 0, buffer1.Length
                    bytesRead1 = await stream1.ReadAsync(buffer1.AsMemory(0, buffer1.Length), token);
                    bytesRead2 = await stream2.ReadAsync(buffer2.AsMemory(0, buffer2.Length), token);

                    if (bytesRead1 != bytesRead2 || !buffer1.AsSpan(0, bytesRead1).SequenceEqual(buffer2.AsSpan(0, bytesRead2)))
                    {
                        return false;
                    }
                } while (bytesRead1 > 0);

                return true;
            }
        }

        internal static async Task<ulong> GetImageHash(string imagePath, CancellationToken token)
        {
            //throw new NotImplementedException();
            return await Task.Run(() =>
            {

                using (var img = new Bitmap(imagePath))
                {
                    // 1. Bild auf 8x8 verkleinern (Graustufen)
                    using (var smallImg = new Bitmap(8, 8))
                    using (var g = Graphics.FromImage(smallImg))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(img, 0, 0, 8, 8);

                        // 2. Graustufenwerte sammeln
                        byte[] grayValues = new byte[64];
                        int index = 0;
                        for (int y = 0; y < 8; y++)
                        {
                            for (int x = 0; x < 8; x++)
                            {
                                System.Drawing.Color pixel = smallImg.GetPixel(x, y);
                                byte gray = (byte)((pixel.R + pixel.G + pixel.B) / 3);
                                grayValues[index++] = gray;
                            }
                        }

                        // 3. Durchschnitt berechnen
                        double avg = 0;
                        foreach (var val in grayValues)
                        {
                            avg += val;
                        }

                        avg /= grayValues.Length;

                        // 4. Hash erstellen (1 = heller als Durchschnitt, 0 = dunkler)
                        ulong hash = 0;
                        for (int i = 0; i < grayValues.Length; i++)
                        {
                            if (grayValues[i] >= avg)
                            {
                                hash |= (1UL << i);
                            }
                        }

                        return hash;
                    }
                }
            }, token);
        }

        internal static async Task<int> HammingDistance(ulong hash1, ulong hash2, CancellationToken token)
        {
            //throw new NotImplementedException();
            return await Task.Run(() =>
            {
                ulong x = hash1 ^ hash2;
                int setBits = 0;
                while (x > 0)
                {
                    setBits += (int)(x & 1);
                    x >>= 1;
                }
                return setBits;
            }, token);

        }

        internal static async Task<double> CompareImagesORB(Image<Bgr, byte> img1, Image<Bgr, byte> img2)
        {
            // Image<Bgr, byte> img1

            // https://stackoverflow.com/questions/8028523/unable-to-load-cvextern-in-a-c-sharp-project
            // Downloads\emgu.cv.runtime.windows.4.12.0.5764.nupkg\runtimes\win-x64\native\
            // https://github.com/emgucv/emgucv/releases
            return await Task.Run(() =>
            {
                using var orb = new Emgu.CV.Features2D.ORB(500, 1.2f, 8);
                using var descriptors1 = new Mat();
                using var descriptors2 = new Mat();
                using var keypoints1 = new VectorOfKeyPoint();
                using var keypoints2 = new VectorOfKeyPoint();

                // Features extrahieren
                orb.DetectAndCompute(img1, null, keypoints1, descriptors1, false);
                orb.DetectAndCompute(img2, null, keypoints2, descriptors2, false);

                if (descriptors1.IsEmpty || descriptors2.IsEmpty)
                {
                    return 0;
                }

                // Matcher erstellen
                using var matcher = new BFMatcher(DistanceType.Hamming, crossCheck: true);
                using var matches = new VectorOfDMatch();
                matcher.Match(descriptors1, descriptors2, matches);

                if (matches.Size == 0)
                {
                    return 0;
                }

                // Gute Matches zählen (Schwelle: Distanz < 50)
                int goodMatches = 0;
                for (int i = 0; i < matches.Size; i++)
                {
                    var m = matches[i];
                    if (m.Distance < 50)
                    {
                        goodMatches++;
                    }
                }

                // Ähnlichkeit in Prozent
                double similarity = (double)goodMatches / matches.Size * 100;
                return similarity;
            });
        }

        internal static async Task<double> CompareBilderGleichheitORB(Mat image1, string bild2Path)
        {
            //throw new NotImplementedException();
            return await Task.Run(() =>
            {

                // Lade zwei Bilder
                //Mat image1 = CvInvoke.Imread(SelectedBildchen?.BName, ImreadModes.AnyColor);
                Mat image2 = CvInvoke.Imread(bild2Path, ImreadModes.AnyColor);

                if (image1.IsEmpty || image2.IsEmpty)
                {
                    Console.WriteLine("Fehler: Eines der Bilder konnte nicht geladen werden.");
                    return 0.0;
                }

                // Falls Größen unterschiedlich → skalieren
                if (image1.Size != image2.Size)
                {
                    CvInvoke.Resize(image2, image2, image1.Size);
                }

                //using var orb = new Emgu.CV.Features2D.ORB(500, 1.2f, 8);
                // ORB-Detector erstellen
                var orb = new Emgu.CV.Features2D.ORB(500, 1.2f, 8); // new ORBDetector(500); // 500 Keypoints


                // Keypoints und Deskriptoren extrahieren
                VectorOfKeyPoint kp1 = new VectorOfKeyPoint();
                VectorOfKeyPoint kp2 = new VectorOfKeyPoint();
                Mat desc1 = new Mat();
                Mat desc2 = new Mat();

                orb.DetectAndCompute(image1, null, kp1, desc1, false);
                orb.DetectAndCompute(image2, null, kp2, desc2, false);

                if (desc1.IsEmpty || desc2.IsEmpty)
                {
                    // Wenn keine Deskriptoren gefunden wurden, könnte das Bild zu einfarbig oder unscharf sein.
                    // In diesem Fall betrachten wir die Bilder als komplett unähnlich (0% Ähnlichkeit).
                    return 0.0;
                    // throw new Exception("Keine Features gefunden – Bilder zu einfarbig oder unscharf?");
                }


                // Matcher erstellen (BruteForce-Hamming für ORB)
                var bfMatcher = new BFMatcher(DistanceType.Hamming, crossCheck: true);

                // Matches finden
                var matches = new VectorOfDMatch();
                bfMatcher.Match(desc1, desc2, matches);

                if (matches.Size == 0)
                {
                    return 0.0;
                }

                // Durchschnittliche Distanz berechnen
                double totalDistance = 0;
                foreach (var m in matches.ToArray())
                {
                    totalDistance += m.Distance;
                }

                double avgDistance = totalDistance / matches.Size;

                // Ähnlichkeitswert berechnen (kleinere Distanz = höhere Ähnlichkeit)
                double similarity = 1.0 - (avgDistance / 256.0); // 256 = max. Hamming-Distanz
                return Math.Max(0, Math.Min(1, similarity));

            });
        }


        /// <summary>
        /// Parallel blockweises Hashen
        /// </summary>
        /// <param name="bName"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        internal static async Task<string> GetFileHashSHA256Async(string? bName, CancellationToken token)
        {
            //throw new NotImplementedException();
            // Läuft
            // Gegekontrolle certutil -hashfile "C:\Users\Bill-6e\Desktop\ZL4\Test 1\he17_同人CG集2025-09-10\At home by DavidMnr on DeviantArt[1].jpg" SHA256
            if (string.IsNullOrWhiteSpace(bName) || !File.Exists(bName))
            {
                throw new FileNotFoundException("Datei nicht gefunden", bName);
            }

            const int bufferSize = 4 * 1024 * 1024; // 4 MB Puffer
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var fs = new FileStream(bName, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);

            var buffer = new byte[bufferSize];
            int bytesRead;
            while ((bytesRead = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false)) > 0)
            {
                // Inkrementell verarbeiten
                sha.TransformBlock(buffer, 0, bytesRead, null, 0);
            }

            // Abschluss des Hash-Vorgangs
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            var finalHash = sha.Hash ?? Array.Empty<byte>();
            return BitConverter.ToString(finalHash).Replace("-", "").ToLowerInvariant();

            //// ende Fehler
            //const int blockSize = 4 * 1024 * 1024;
            //long fileLength = new FileInfo(bName).Length;
            //int blockCount = (int)Math.Ceiling((double)fileLength / blockSize);
            //var blockHashes = new byte[blockCount][];

            //var tasks = new Task[blockCount];
            //for (int i = 0; i < blockCount; i++)
            //{
            //    int idx = i;
            //    tasks[idx] = Task.Run(async () =>
            //    {
            //        var buffer = new byte[blockSize];
            //        using var fs = new FileStream(bName!, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            //        fs.Seek((long)idx * blockSize, SeekOrigin.Begin);
            //        int bytesRead = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);

            //        using var sha = System.Security.Cryptography.SHA256.Create();
            //        blockHashes[idx] = sha.ComputeHash(buffer, 0, bytesRead);
            //    }, token);
            //}

            //await Task.WhenAll(tasks).ConfigureAwait(false);

            //using var shaFinal = System.Security.Cryptography.SHA256.Create();
            //// Kombiniere Block-Hashes (oder besser: incremental transform, s.u.)
            //byte[] combined = blockHashes.SelectMany(b => b).ToArray();
            //byte[] finalHash = shaFinal.ComputeHash(combined);
            //return BitConverter.ToString(finalHash).Replace("-", "").ToLowerInvariant();

            //const int blockSize = 4 * 1024 * 1024; // 4 MB pro Block
            //byte[][] blockHashes;

            //long fileLength = new FileInfo(bName).Length;
            //int blockCount = (int)Math.Ceiling((double)fileLength / blockSize);
            //blockHashes = new byte[blockCount][];

            //// Parallel blockweises Hashen
            //Parallel.For(0, blockCount, async i =>
            //{
            //    byte[] buffer = new byte[blockSize];
            //    int bytesRead;
            //    using (FileStream fs = new FileStream(bName, FileMode.Open, FileAccess.Read, FileShare.Read))
            //    {
            //        fs.Seek((long)i * blockSize, SeekOrigin.Begin);
            //        //bytesRead = fs.Read(buffer, 0, buffer.Length);
            //        bytesRead = await fs.ReadAsync(buffer, token);
            //    }

            //    using (SHA256 sha = SHA256.Create())
            //    {
            //             //_ = sha.TransformBlock(buffer, 0, bytesRead,buffer, 0);
            //        blockHashes[i] = sha.ComputeHash(buffer, 0, bytesRead);
            //        //_ = hashAlgorithm.TransformBlock(byteBuffer, 0, read, byteBuffer, 0);

            //    }
            //});

            //// Kombiniere Block-Hashes zu einem finalen Hash
            //using (SHA256 shaFinal = SHA256.Create())
            //{
            //    byte[] combined = blockHashes.SelectMany(b => b).ToArray();
            //    byte[] finalHash = shaFinal.ComputeHash(combined);
            //    return BitConverter.ToString(finalHash).Replace("-", "").ToLowerInvariant();
            //}
        }

        internal static (int originalWidth, int originalHeight) ReadOriginalSize(string path)
        {
            // throw new NotImplementedException();

            try
            {
                using var stream = File.OpenRead(path);

                var decoder = BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.IgnoreColorProfile,
                    BitmapCacheOption.None);

                var frame = decoder.Frames[0];
                return (frame.PixelWidth, frame.PixelHeight);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex); // optional
                return (0, 0);

            }


        }

        internal static int GetMonitorDecodeWidth()
        {
            // throw new NotImplementedException();

            // Hauptfenster muss sichtbar sein
            var window = Application.Current.MainWindow;
            if (window == null)
            {
                return 1920; // Fallback
            }

            // WPF‑DPI korrekt ermitteln
            var dpi = VisualTreeHelper.GetDpi(window);

            // WPF‑Einheiten → echte Pixel
            double wpfWidth = SystemParameters.PrimaryScreenWidth;
            int pixelWidth = (int)(wpfWidth * dpi.DpiScaleX);

            return pixelWidth;

        }


        internal static int GetMonitorDecodeHeight()
        {
            // Hauptfenster muss sichtbar sein
            var window = Application.Current.MainWindow;
            if (window == null)
            {
                return 1080; // sinnvoller Fallback für Höhe
            }

            // WPF‑DPI korrekt ermitteln
            var dpi = VisualTreeHelper.GetDpi(window);

            // WPF‑Einheiten → echte Pixel
            double wpfHeight = SystemParameters.PrimaryScreenHeight;
            int pixelHeight = (int)(wpfHeight * dpi.DpiScaleY);

            return pixelHeight;
        }

        internal static (int monitorWidth, int monitorHeight) GetMonitorDecodeSize()
        {
            var window = Application.Current.MainWindow;
            if (window == null)
            {
                return (1920, 1080);
            }

            var dpi = VisualTreeHelper.GetDpi(window);

            int width = (int)(SystemParameters.PrimaryScreenWidth * dpi.DpiScaleX);
            int height = (int)(SystemParameters.PrimaryScreenHeight * dpi.DpiScaleY);

            return (width, height);

        }
    }
}





