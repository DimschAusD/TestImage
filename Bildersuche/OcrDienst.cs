using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Liest Text aus Bildern — mit der Texterkennung, die in Windows steckt
    /// (<c>Windows.Media.Ocr</c>).
    ///
    /// Kein NuGet-Paket, keine native Bibliothek, nichts im Ausgabeordner: Das Ziel-
    /// framework net10.0-windows10.0.18362.0 erzeugt die WinRT-Projektionen mit, und
    /// die Erkennung selbst liegt beim Nutzer im System. Das ist auch der Grund, warum
    /// diese Lösung für ein offenes Repo taugt — es wird nichts mitgeliefert.
    ///
    /// <b>Grenzen, die man kennen sollte:</b> Ausgelegt ist die Engine auf gedruckte
    /// und Bildschirmschrift. Halbdurchsichtige Aufdrucke, verschnörkelte Schriften,
    /// Text über gemustertem Untergrund und Handschrift liefern oft nichts oder Unsinn.
    /// </summary>
    internal static class OcrDienst
    {
        /// <summary>
        /// Die Engine für die Anzeigesprachen des Nutzers. <c>null</c>, wenn für keine
        /// davon ein Erkennungspaket installiert ist.
        ///
        /// Einmal erzeugt und behalten: Das Anlegen kostet spürbar, und der Zustand
        /// ändert sich während eines Programmlaufs nicht.
        /// </summary>
        private static readonly OcrEngine? Engine = ErzeugeEngine();

        private static OcrEngine? ErzeugeEngine()
        {
            try
            {
                return OcrEngine.TryCreateFromUserProfileLanguages();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>True, wenn auf diesem Rechner überhaupt erkannt werden kann.</summary>
        internal static bool IstVerfuegbar => Engine is not null;

        /// <summary>
        /// Sprache, in der erkannt wird — für die Anzeige. Leer, wenn nichts geht.
        /// </summary>
        internal static string Sprache => Engine?.RecognizerLanguage?.DisplayName ?? string.Empty;

        /// <summary>
        /// Liest den Text eines Bildes. <c>null</c>, wenn keine Erkennung möglich war —
        /// fehlendes Sprachpaket, unlesbare Datei, unbekanntes Format. Ein leerer String
        /// heisst dagegen: erkannt, aber es stand kein Text darin.
        /// </summary>
        internal static async Task<string?> LiesTextAsync(string pfad)
        {
            if (Engine is null || string.IsNullOrWhiteSpace(pfad) || !File.Exists(pfad))
            {
                return null;
            }

            try
            {
                SoftwareBitmap bitmap = await LadeBitmapAsync(pfad).ConfigureAwait(false);
                using (bitmap)
                {
                    OcrResult ergebnis = await Engine.RecognizeAsync(bitmap);
                    return ergebnis.Text ?? string.Empty;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Lädt die Datei als <see cref="SoftwareBitmap"/> in der Grösse, die die Engine
        /// verarbeitet.
        ///
        /// Der Umweg über einen Speicherstrom statt über StorageFile ist Absicht: Er
        /// braucht keine Paketidentität und hält die Datei nicht länger offen als nötig.
        ///
        /// <see cref="OcrEngine.MaxImageDimension"/> ist eine harte Grenze — darüber
        /// wirft RecognizeAsync. Verkleinert wird schon beim Auspacken, nicht danach:
        /// So packt der Decoder gar nicht erst die volle Grösse aus.
        /// </summary>
        private static async Task<SoftwareBitmap> LadeBitmapAsync(string pfad)
        {
            byte[] roh = await File.ReadAllBytesAsync(pfad).ConfigureAwait(false);

            using var strom = new InMemoryRandomAccessStream();
            var schreiber = new DataWriter(strom);
            schreiber.WriteBytes(roh);
            await schreiber.StoreAsync();
            await schreiber.FlushAsync();
            schreiber.DetachStream();
            strom.Seek(0);

            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(strom);

            uint breite = decoder.PixelWidth;
            uint hoehe = decoder.PixelHeight;
            uint grenze = OcrEngine.MaxImageDimension;

            var wandlung = new BitmapTransform();
            if (breite > grenze || hoehe > grenze)
            {
                double faktor = (double)grenze / Math.Max(breite, hoehe);
                wandlung.ScaledWidth = (uint)Math.Max(1, breite * faktor);
                wandlung.ScaledHeight = (uint)Math.Max(1, hoehe * faktor);
                wandlung.InterpolationMode = BitmapInterpolationMode.Fant;
            }

            return await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                wandlung,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);
        }
    }
}
