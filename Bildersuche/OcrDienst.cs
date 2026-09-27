using System;
using System.Collections.Generic;
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
        /// Länge, auf die die längere Kante kleiner Bilder gebracht wird, bevor erkannt
        /// wird.
        ///
        /// Gemessen an einem Kartenausschnitt mit Strassennamen, 1026 × 607: In
        /// Originalgrösse fand die Engine 2 Wörter, verdoppelt 5 — und die verdoppelten
        /// waren sauber geschrieben, während in Originalgrösse „Helena-weg“ herauskam.
        /// Der Lauf kostete dafür 94 statt 254 ms je Bild.
        ///
        /// Bilder, die schon grösser sind, bleiben unangetastet: Dort ist die Schrift
        /// ohnehin gross genug, und die vierfache Fläche wäre nur Rechenzeit.
        /// </summary>
        private const uint ZielKante = 2000;


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
                Entpackt entpackt = await LadeBitmapAsync(pfad).ConfigureAwait(false);
                using (entpackt.Bitmap)
                {
                    OcrResult ergebnis = await Engine.RecognizeAsync(entpackt.Bitmap);
                    return ergebnis.Text ?? string.Empty;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Wie <see cref="LiesTextAsync"/>, liefert aber zusätzlich <b>wo</b> jedes Wort
        /// steht — und damit die Ebene, gegen die sich alles andere vergleichen lässt.
        ///
        /// Das ist die Standardsuche der Anwendung, nur mit offengelegten Koordinaten:
        /// Dieselbe Engine, dasselbe ganze Bild, dieselbe Vergrösserung kleiner Bilder.
        /// <c>null</c> unter denselben Bedingungen wie dort.
        ///
        /// <b>Die Kästen sind Bildpunkte des Originals</b>, nicht der Bitmap, die die
        /// Engine gesehen hat: Sie werden durch den Vergrösserungsfaktor zurückgerechnet.
        /// Sonst läge jeder Kasten bei einem kleinen Bild um den Faktor 2 daneben.
        /// </summary>
        internal static async Task<OcrWortBefund?> SucheWoerterAsync(string pfad)
        {
            if (Engine is null || string.IsNullOrWhiteSpace(pfad) || !File.Exists(pfad))
            {
                return null;
            }

            try
            {
                var uhr = System.Diagnostics.Stopwatch.StartNew();

                Entpackt entpackt = await LadeBitmapAsync(pfad).ConfigureAwait(false);

                using (entpackt.Bitmap)
                {
                    OcrResult ergebnis = await Engine.RecognizeAsync(entpackt.Bitmap);

                    double faktor = entpackt.Faktor <= 0 ? 1 : entpackt.Faktor;
                    var woerter = new List<OcrWort>();

                    foreach (OcrLine zeile in ergebnis.Lines)
                    {
                        foreach (OcrWord wort in zeile.Words)
                        {
                            woerter.Add(new OcrWort(
                                wort.Text ?? string.Empty,
                                wort.BoundingRect.X / faktor,
                                wort.BoundingRect.Y / faktor,
                                wort.BoundingRect.Width / faktor,
                                wort.BoundingRect.Height / faktor));
                        }
                    }

                    return new OcrWortBefund
                    {
                        Woerter = woerter,
                        Text = ergebnis.Text ?? string.Empty,

                        // Der eine Textwinkel, den die Engine je Bild kennt. Genau hier
                        // liegt ihre Grenze: Was quer dazu läuft, findet sie nicht.
                        TextWinkel = ergebnis.TextAngle,
                        Dauer = uhr.Elapsed
                    };
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
        ///
        /// <b>Kleine Bilder werden vergrössert</b>, siehe <see cref="ZielKante"/>.
        /// </summary>
        private static async Task<Entpackt> LadeBitmapAsync(string pfad)
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
            uint laengste = Math.Max(breite, hoehe);

            var wandlung = new BitmapTransform();
            double faktor = 1;

            if (laengste > grenze)
            {
                faktor = (double)grenze / laengste;
                wandlung.ScaledWidth = (uint)Math.Max(1, breite * faktor);
                wandlung.ScaledHeight = (uint)Math.Max(1, hoehe * faktor);
                wandlung.InterpolationMode = BitmapInterpolationMode.Fant;
            }
            else if (laengste > 0 && laengste < ZielKante)
            {
                // Höchstens verdoppeln: Darüber hinaus wurde es im Versuch wieder
                // schlechter — bei Faktor 3 und mehr fand die Engine gar keinen
                // Textwinkel mehr und lieferte fast nichts.
                faktor = Math.Min(2.0, (double)ZielKante / laengste);

                wandlung.ScaledWidth = (uint)Math.Round(breite * faktor);
                wandlung.ScaledHeight = (uint)Math.Round(hoehe * faktor);
                wandlung.InterpolationMode = BitmapInterpolationMode.Fant;
            }

            // Nach der Rundung nachgerechnet: Der Faktor muss der sein, den die Bitmap
            // wirklich hat, sonst wandern die zurückgerechneten Wortkästen um bis zu
            // einen halben Bildpunkt je hundert.
            if (wandlung.ScaledWidth > 0 && breite > 0)
            {
                faktor = wandlung.ScaledWidth / (double)breite;
            }

            SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                wandlung,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);

            return new Entpackt(bitmap, faktor);
        }

        /// <summary>
        /// Die entpackte Bitmap und der Faktor, mit dem sie gegenüber der Datei
        /// skaliert wurde. Der Faktor gilt für beide Kanten gleich — auch bei
        /// gedrehtem EXIF, wo sich Breite und Höhe tauschen.
        /// </summary>
        private readonly record struct Entpackt(SoftwareBitmap Bitmap, double Faktor);
    }
}
