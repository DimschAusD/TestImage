using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestImage.Bildersuche;

namespace TestImage
{
    /// <summary>Eine Zeile im Eigenschaften-Feld: links die Bezeichnung, rechts der Wert.</summary>
    public sealed class BildinfoZeile
    {
        public string Feld { get; init; } = string.Empty;

        public string Wert { get; init; } = string.Empty;
    }

    /// <summary>
    /// Eigenschaften des angezeigten Bildes, einblendbar in der Vollbildansicht.
    ///
    /// Bewusst seitlich statt über dem Bild: Ein Feld, das über den Pixeln liegt, verdeckt
    /// ausgerechnet den Teil, den man gerade beurteilt. In einer eigenen Spalte rückt das
    /// Bild zur Seite und bleibt vollständig sichtbar.
    /// </summary>
    public partial class AufgabeViewModel
    {
        #region Zustand

        /// <summary>Feld eingeblendet. Aus = keine Datei wird dafür angefasst.</summary>
        [ObservableProperty]
        public partial bool IsBildinfoSichtbar { get; set; }

        /// <summary>Dateiname als Überschrift des Feldes.</summary>
        [ObservableProperty]
        public partial string BildinfoDatei { get; set; } = string.Empty;

        /// <summary>Ordner darunter, in kleiner Schrift — der Name allein sagt nicht, woher.</summary>
        [ObservableProperty]
        public partial string BildinfoOrdner { get; set; } = string.Empty;

        /// <summary>Die Angaben selbst, in der Reihenfolge, in der sie hereinkommen.</summary>
        public ObservableCollection<BildinfoZeile> BildinfoZeilen { get; } = new();

        [RelayCommand]
        private void CommandExecuteBildinfoToggle() => IsBildinfoSichtbar = !IsBildinfoSichtbar;

        // Erst beim Einblenden lesen: Solange das Feld zu ist, soll das Blättern keine
        // einzige Datei zusätzlich anfassen.
        partial void OnIsBildinfoSichtbarChanged(bool value)
        {
            if (value)
                AktualisiereBildinfo();
            else
                _bildinfoEntpreller?.Stop();
        }

        #endregion

        #region Füllen

        /// <summary>
        /// Wartet nach einem Bildwechsel kurz ab, bevor Dateigrösse, Datum und Metadaten
        /// gelesen werden.
        ///
        /// Beim Blättern mit gehaltener Pfeiltaste kommen die Wechsel im Wiederholtakt der
        /// Tastatur. Ohne diese Pause liefe für jedes durchflogene Bild ein eigener
        /// Datei- und Metadatenzugriff — und zwar genau in dem Moment, in dem die Ansicht
        /// mit dem Dekodieren des nächsten Bildes beschäftigt ist.
        /// </summary>
        private System.Windows.Threading.DispatcherTimer? _bildinfoEntpreller;

        /// <summary>
        /// Zählt die Anfragen mit. Eine Antwort, die zu einem inzwischen weitergeblätterten
        /// Bild gehört, wird verworfen statt angezeigt.
        /// </summary>
        private int _bildinfoLauf;

        /// <summary>
        /// Übernimmt die kostenlosen Angaben sofort und stösst das Lesen der übrigen an.
        /// Aufgerufen bei jedem Bildwechsel; tut nichts, solange das Feld zu ist.
        /// </summary>
        internal void AktualisiereBildinfo()
        {
            if (!IsBildinfoSichtbar)
                return;

            string? pfad = SelectedBildchen?.BName;

            if (string.IsNullOrWhiteSpace(pfad))
            {
                BildinfoDatei = string.Empty;
                BildinfoOrdner = string.Empty;
                BildinfoZeilen.Clear();
                return;
            }

            BildinfoDatei = Path.GetFileName(pfad);
            BildinfoOrdner = Path.GetDirectoryName(pfad) ?? string.Empty;

            // Maße bewusst NICHT aus DisplayImage: Das grosse Bild wird mit einer
            // Dekodierbreite passend zur Anzeigefläche geladen (CreateBitmap mit
            // decodeWidth/decodeHeight). Seine PixelWidth ist damit die Grösse, in der
            // dekodiert wurde, nicht die des Bildes in der Datei — und das kleine
            // Vorschaubild der ersten Ladestufe hätte ohnehin nur 100 Punkte.
            // Alle Angaben kommen deshalb aus dem Dateikopf.
            BildinfoZeilen.Clear();

            PlaneBildinfoNachladen();
        }

        private void PlaneBildinfoNachladen()
        {
            _bildinfoEntpreller ??= ErzeugeBildinfoEntpreller();

            _bildinfoEntpreller.Stop();
            _bildinfoEntpreller.Start();
        }

        private System.Windows.Threading.DispatcherTimer ErzeugeBildinfoEntpreller()
        {
            var uhr = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };

            uhr.Tick += async (_, _) =>
            {
                uhr.Stop();
                await LiesBildinfoNachAsync();
            };

            return uhr;
        }

        /// <summary>
        /// Holt Dateigrösse, Datum und die Metadaten-Hinweise im Hintergrund nach.
        ///
        /// Der Metadatenleser öffnet die Datei ein zweites Mal und durchsucht bei Bedarf
        /// den Kopfbereich — auf einer drehenden Platte oder über das Netz dauert das
        /// spürbar, und im Anzeige-Faden bliebe darüber das Bild stehen.
        /// </summary>
        private async Task LiesBildinfoNachAsync()
        {
            string? pfad = SelectedBildchen?.BName;

            if (string.IsNullOrWhiteSpace(pfad))
                return;

            int lauf = ++_bildinfoLauf;

            var zeilen = await Task.Run(() => SammleBildinfo(pfad));

            // Weitergeblättert, während gelesen wurde: Diese Angaben gehören zu einem
            // Bild, das gar nicht mehr dasteht.
            if (lauf != _bildinfoLauf || !IsBildinfoSichtbar)
                return;

            if (!string.Equals(SelectedBildchen?.BName, pfad, StringComparison.OrdinalIgnoreCase))
                return;

            foreach (var zeile in zeilen)
                BildinfoZeilen.Add(zeile);

            ErgaenzeErkanntenText(pfad);
        }

        /// <summary>
        /// Hängt den bereits erkannten Text an — nur, was der Ordnerlauf schon gelesen hat.
        ///
        /// Bewusst ohne eigene Erkennung: Ein Bild zu lesen dauert Sekunden, und beim
        /// Blättern liefe das für jedes Bild an. Was noch nicht gelesen ist, bleibt hier
        /// leer; die Texterkennung selbst wird wie bisher über ihre eigene Karte angestossen.
        /// </summary>
        private void ErgaenzeErkanntenText(string pfad)
        {
            // Lädt den Cache des Ordners, falls die Texterkennung in dieser Sitzung noch
            // nicht geöffnet war. Steigt bei gleichem Ordner sofort wieder aus.
            StelleOcrCacheBereit();

            string? text = _ocrCache.Hole(pfad);

            if (string.IsNullOrWhiteSpace(text))
                return;

            BildinfoZeilen.Add(new BildinfoZeile
            {
                Feld = "Erkannter Text",

                // Gekappt, weil eine volle Textseite das Feld zum Textdokument machen
                // würde. Die ganze Fassung steht in der Texterkennungs-Karte.
                Wert = text.Length <= 800 ? text : text[..800] + " …"
            });
        }

        /// <summary>
        /// Alles, was einen Dateizugriff kostet. Läuft im Hintergrund und fasst die
        /// Ausbeute des <see cref="MetadatenPruefer"/> mit ein — bei diesem Material sind
        /// dessen Funde (Software, KI-Generierungsdaten, Herkunftsnachweis) die
        /// interessantere Angabe als ein Aufnahmedatum, das Illustrationen nie tragen.
        /// </summary>
        private static System.Collections.Generic.List<BildinfoZeile> SammleBildinfo(string pfad)
        {
            var zeilen = new System.Collections.Generic.List<BildinfoZeile>();

            LiesBildmasse(pfad, zeilen);

            try
            {
                var datei = new FileInfo(pfad);

                if (datei.Exists)
                {
                    zeilen.Add(new BildinfoZeile { Feld = "Grösse", Wert = DateigroesseText(datei.Length) });
                    zeilen.Add(new BildinfoZeile { Feld = "Geändert", Wert = datei.LastWriteTime.ToString("dd.MM.yyyy HH:mm") });

                    string endung = datei.Extension.TrimStart('.').ToUpperInvariant();
                    if (endung.Length > 0)
                        zeilen.Add(new BildinfoZeile { Feld = "Format", Wert = endung });
                }
            }
            catch
            {
                // Gesperrt oder weggezogen – dann eben ohne Dateiangaben.
            }

            try
            {
                foreach (var hinweis in MetadatenPruefer.Pruefe(pfad))
                {
                    // Der Prüfer liefert fertige Sätze der Form „Autor: X". Wo ein
                    // Doppelpunkt steht, wird daraus Feld und Wert, sonst steht der Satz
                    // für sich — „XMP-Block vorhanden" hat keinen Wert daneben.
                    int trenner = hinweis.IndexOf(':');

                    zeilen.Add(trenner > 0
                        ? new BildinfoZeile
                        {
                            Feld = hinweis[..trenner],
                            Wert = hinweis[(trenner + 1)..].Trim()
                        }
                        : new BildinfoZeile { Feld = string.Empty, Wert = hinweis });
                }
            }
            catch
            {
                // Metadaten sind Beiwerk – ihr Fehlen darf das Feld nicht leer lassen.
            }

            return zeilen;
        }

        /// <summary>
        /// Maße, Auflösung und Farbtiefe aus dem Dateikopf.
        ///
        /// Gelesen wird nur der Kopf: <c>BitmapCacheOption.None</c> hält keine Pixel, und
        /// die Werte stehen im Dekoder bereit, sobald er das Bild kennt.
        /// <c>PreservePixelFormat</c> ist für die Farbtiefe nötig — ohne das meldet WPF
        /// sein eigenes Anzeigeformat statt dem der Datei.
        /// </summary>
        private static void LiesBildmasse(string pfad, System.Collections.Generic.List<BildinfoZeile> zeilen)
        {
            try
            {
                using var strom = File.OpenRead(pfad);

                var rahmen = System.Windows.Media.Imaging.BitmapFrame.Create(
                    strom,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.None);

                zeilen.Add(new BildinfoZeile
                {
                    Feld = "Maße",
                    Wert = $"{rahmen.PixelWidth} × {rahmen.PixelHeight} Punkte"
                });

                // Quadratische Auflösung ist der Normalfall und braucht nur eine Zahl;
                // abweichende X/Y kommen bei gescanntem Material vor und sind dann eine
                // Angabe, die man sehen will.
                int dpiX = (int)Math.Round(rahmen.DpiX);
                int dpiY = (int)Math.Round(rahmen.DpiY);

                if (dpiX > 0)
                {
                    zeilen.Add(new BildinfoZeile
                    {
                        Feld = "Auflösung",
                        Wert = dpiX == dpiY ? $"{dpiX} DPI" : $"{dpiX} × {dpiY} DPI"
                    });
                }

                int bits = rahmen.Format.BitsPerPixel;

                if (bits > 0)
                    zeilen.Add(new BildinfoZeile { Feld = "Farbtiefe", Wert = $"{bits} Bit" });

                if (rahmen.Metadata is System.Windows.Media.Imaging.BitmapMetadata meta)
                    LiesAufnahmedaten(meta, zeilen);
            }
            catch
            {
                // Kein lesbares Bildformat – dann bleiben die Dateiangaben darunter allein.
            }
        }

        /// <summary>
        /// Die Aufnahmedaten einer Kamera. Bei heruntergeladenen Illustrationen fehlen sie
        /// durchweg — dann entsteht auch keine Zeile; bei fotografiertem Material sind sie
        /// die Angaben, für die man das Feld überhaupt aufklappt.
        ///
        /// Kamera und Datum haben in WPF eigene Eigenschaften; für Blende, Belichtung, ISO
        /// und Brennweite gibt es keine, die holt <c>GetQuery</c> aus dem EXIF-Verzeichnis.
        /// </summary>
        private static void LiesAufnahmedaten(
            System.Windows.Media.Imaging.BitmapMetadata meta,
            System.Collections.Generic.List<BildinfoZeile> zeilen)
        {
            string kamera = string.Join(" ", new[] { Feldwert(() => meta.CameraManufacturer), Feldwert(() => meta.CameraModel) }
                .Where(t => t.Length > 0)).Trim();

            if (kamera.Length > 0)
                zeilen.Add(new BildinfoZeile { Feld = "Kamera", Wert = kamera });

            string aufnahme = Feldwert(() => meta.DateTaken);
            if (aufnahme.Length > 0)
                zeilen.Add(new BildinfoZeile { Feld = "Aufgenommen", Wert = aufnahme });

            double? belichtung = Bruchwert(Abfrage(meta, "/app1/ifd/exif/{ushort=33434}"));
            if (belichtung is > 0)
            {
                // Unter einer Sekunde ist der Kehrwert die übliche Schreibweise: „1/125 s"
                // liest sich als Verschlusszeit, „0,008 s" nicht.
                zeilen.Add(new BildinfoZeile
                {
                    Feld = "Belichtung",
                    Wert = belichtung < 1
                        ? $"1/{Math.Round(1 / belichtung.Value)} s"
                        : $"{belichtung.Value:0.#} s"
                });
            }

            double? blende = Bruchwert(Abfrage(meta, "/app1/ifd/exif/{ushort=33437}"));
            if (blende is > 0)
                zeilen.Add(new BildinfoZeile { Feld = "Blende", Wert = $"f/{blende.Value:0.#}" });

            if (Abfrage(meta, "/app1/ifd/exif/{ushort=34855}") is ushort iso && iso > 0)
                zeilen.Add(new BildinfoZeile { Feld = "ISO", Wert = iso.ToString() });

            double? brennweite = Bruchwert(Abfrage(meta, "/app1/ifd/exif/{ushort=37386}"));
            if (brennweite is > 0)
                zeilen.Add(new BildinfoZeile { Feld = "Brennweite", Wert = $"{brennweite.Value:0.#} mm" });
        }

        private static object? Abfrage(System.Windows.Media.Imaging.BitmapMetadata meta, string pfad)
        {
            try { return meta.GetQuery(pfad); }
            catch { return null; }
        }

        private static string Feldwert(Func<string?> lese)
        {
            try { return lese() ?? string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>
        /// EXIF speichert diese Werte als Bruch: Zähler und Nenner stecken als zwei
        /// 32-Bit-Zahlen in einem 64-Bit-Wert. Ohne das Auseinandernehmen stünde dort eine
        /// achtstellige Zahl statt „f/2,8".
        /// </summary>
        private static double? Bruchwert(object? wert)
        {
            if (wert is ulong r)
            {
                uint zaehler = (uint)(r & 0xFFFFFFFF);
                uint nenner = (uint)(r >> 32);

                return nenner == 0 ? null : (double)zaehler / nenner;
            }

            // Vorzeichenbehaftete Variante, die manche Hersteller verwenden.
            if (wert is long sr)
            {
                int zaehler = (int)(sr & 0xFFFFFFFF);
                int nenner = (int)(sr >> 32);

                return nenner == 0 ? null : (double)zaehler / nenner;
            }

            return null;
        }

        private static string DateigroesseText(long bytes)
            => bytes >= 1024L * 1024
                ? $"{bytes / 1024.0 / 1024.0:0.0} MB"
                : $"{bytes / 1024.0:0} KB";

        #endregion
    }
}
