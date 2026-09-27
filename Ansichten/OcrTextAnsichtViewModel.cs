using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using TestImage.Bildersuche;

namespace TestImage.Ansichten
{
    /// <summary>
    /// Die Rahmenansicht: ein Bild, darüber die Funde beider Suchen als Rahmen, daneben
    /// je Ebene die Liste des Gelesenen.
    ///
    /// <b>Zwei Ebenen, und der Vergleich ist der Zweck.</b>
    /// <list type="bullet">
    /// <item>
    /// <b>Windows-OCR</b> — die Standardsuche der Anwendung: das ganze Bild in einem
    /// Stück, ein Textwinkel für alles. Ihre Kästen sind achsenparallel. Das ist die
    /// Ebene, die es auch ohne eingelegtes Plugin gibt.
    /// </item>
    /// <item>
    /// <b>Plugin</b> — Zeilensuche in beliebiger Neigung, jede Zeile geradegerückt und
    /// einzeln gelesen. Ihre Rahmen sind gedreht.
    /// </item>
    /// </list>
    ///
    /// Erst nebeneinander sieht man, was zählt: Welche Beschriftung hat die
    /// Standardsuche übersehen? Welche hat das Plugin doppelt gefunden (überlappende
    /// Rahmen)? Und was hat es für Schrift gehalten, ohne dass etwas darin stand?
    ///
    /// <b>Eigenes ViewModel, nicht das grosse.</b> Die Ansicht lebt nur, solange das
    /// Fenster offen ist, und arbeitet für genau ein Bild. Sie hält dafür eine Bitmap
    /// samt aller Streifen im Speicher; das gehört nicht ins ViewModel der Anwendung,
    /// das über die ganze Sitzung stehen bleibt.
    ///
    /// <b>Nichts wird gespeichert.</b> Rahmen und Streifen passen nicht in
    /// <c>.bildocr.json</c> (dort steht nur Text), und für eine Ansicht, die man auf
    /// Klick öffnet, ist der frische Lauf der ehrlichere Weg: Er zeigt, was die beiden
    /// heute finden, nicht was sie einmal gefunden haben.
    /// </summary>
    internal sealed partial class OcrTextAnsichtViewModel : ObservableObject
    {
        internal OcrTextAnsichtViewModel(string bildPfad)
        {
            BildPfad = bildPfad;
            Dateiname = Path.GetFileName(bildPfad);
            PluginVermerk = PluginOcrDienst.IstVerfuegbar
                ? PluginOcrDienst.Beschreibung
                : "Kein Plugin eingelegt — nur die Standardsuche, siehe lib\\LIESMICH.md";
        }

        /// <summary>Das Bild, um das es geht.</summary>
        internal string BildPfad { get; }

        /// <summary>Dateiname für die Kopfzeile — der ganze Pfad wäre dort zu lang.</summary>
        public string Dateiname { get; }

        /// <summary>Name und Fassung des Plugins, oder der Hinweis, dass keines daliegt.</summary>
        public string PluginVermerk { get; }

        /// <summary>
        /// Das Bild, auf das sich alle Koordinaten beziehen — geladen mit
        /// <see cref="BildLader"/>, wie beide Suchen es tun.
        ///
        /// Ein zweites Laden der Datei an anderer Stelle könnte anders skalieren oder
        /// EXIF anders drehen; die Rahmen sässen dann neben dem Text.
        /// </summary>
        [ObservableProperty]
        public partial BitmapSource? Bild { get; set; }

        /// <summary>Die Wortkästen der Standardsuche.</summary>
        public ObservableCollection<OcrZeilenFund> WindowsFunde { get; } = new();

        /// <summary>Die Zeilenrahmen des Plugins — auch die ohne gelesenen Text.</summary>
        public ObservableCollection<OcrZeilenFund> PluginFunde { get; } = new();

        /// <summary>Was in der Liste über der Windows-Ebene steht.</summary>
        [ObservableProperty]
        public partial string WindowsKopf { get; set; } = "Windows-OCR, ganzes Bild";

        /// <summary>Was in der Liste über der Plugin-Ebene steht.</summary>
        [ObservableProperty]
        public partial string PluginKopf { get; set; } = "Plugin, schräge Zeilen";

        /// <summary>
        /// Ebenen einzeln ausblendbar: Übereinander sind zwölf gedrehte Rahmen und
        /// dreissig Wortkästen auf einem Kartenausschnitt nicht mehr auseinanderzuhalten.
        /// Erst das Wegschalten der einen zeigt, was die andere allein gefunden hat.
        /// </summary>
        [ObservableProperty]
        public partial bool ZeigeWindows { get; set; } = true;

        /// <summary>Siehe <see cref="ZeigeWindows"/>.</summary>
        [ObservableProperty]
        public partial bool ZeigePlugin { get; set; } = true;

        /// <summary>
        /// Die Zeile oder das Wort, auf dem die Maus steht — aus einer der Listen oder
        /// vom Rahmen im Bild. <c>null</c>, sobald die Maus beides verlässt. Der
        /// Streifen unter dem Bild gehört dazu.
        /// </summary>
        [ObservableProperty]
        public partial OcrZeilenFund? ZeileUnterMaus { get; set; }

        /// <summary>Strichstärke der Rahmen, passend zur Bildgrösse gerechnet.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StrichstaerkeStark))]
        public partial double Strichstaerke { get; set; } = 2;

        /// <summary>
        /// Strichstärke des hervorgehobenen Rahmens. Als Eigenschaft und nicht als
        /// Umwandler in der Ansicht: Ein Faktor, den WPF nur multiplizieren soll, ist
        /// in XAML ein eigener Umwandler samt Registrierung — hier ist es eine Zeile.
        /// </summary>
        public double StrichstaerkeStark => Strichstaerke * 2.5;

        /// <summary>Was gerade läuft, oder was am Ende herauskam.</summary>
        [ObservableProperty]
        public partial string Status { get; set; } = "Noch nicht gesucht.";

        /// <summary>
        /// Fortschritt des Plugins, 0 … 1 über alle gewählten Durchgänge. Die
        /// Standardsuche davor braucht Zehntelsekunden und bekommt keinen Balken.
        /// </summary>
        [ObservableProperty]
        public partial double PluginFortschritt { get; set; }

        /// <summary>Schriftart, Stufe, Anteil und Restzeit zum Balken.</summary>
        [ObservableProperty]
        public partial string PluginFortschrittText { get; set; } = string.Empty;

        /// <summary>
        /// Blendet den Balken ein, solange das Plugin sucht. Zurückgesetzt wird er im
        /// <c>finally</c> des Befehls und nicht über die letzte Meldung — die kommt
        /// über <c>Progress</c> verzögert an, auch noch nach dem <c>await</c>.
        /// </summary>
        [ObservableProperty]
        public partial bool PluginSucht { get; set; }

        /// <summary>
        /// Zählt die Plugin-Läufe. Eine verspätete Meldung des vorigen Laufs trüge
        /// sonst ihren Stand in den Balken des neuen.
        /// </summary>
        private int _pluginLauf;

        /// <summary>
        /// Sperrt Suche und Umschalter, solange gesucht wird. Der Modus darf nicht
        /// mitten im Lauf wechseln — sonst gehörten die Rahmen zur einen und die Zahlen
        /// zur anderen Einstellung.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteSuchenCommand))]
        [NotifyPropertyChangedFor(nameof(KannBedienen))]
        [NotifyPropertyChangedFor(nameof(KannModusWaehlen))]
        public partial bool IsEineAufgabeLäuft { get; set; }

        /// <summary>Umkehrung von <see cref="IsEineAufgabeLäuft"/> für die Bedienelemente.</summary>
        public bool KannBedienen => !IsEineAufgabeLäuft;

        /// <summary>
        /// Der Umschalter der Schriftart gilt nur für das Plugin — ohne Plugin bleibt er
        /// gesperrt, sonst versprächen bediente Knöpfe eine Wirkung, die es nicht gibt.
        /// </summary>
        public bool KannModusWaehlen => KannBedienen && PluginOcrDienst.IstVerfuegbar;

        /// <summary>
        /// Beide Schriftarten hintereinander — die Vorgabe, weil das Lesen es genauso
        /// macht. Die Ansicht soll zeigen, was der Ordnerlauf findet, nicht die Hälfte
        /// davon.
        ///
        /// Drei einzelne Kennzeichen statt einer Aufzählung: Eine Aufzählung an
        /// Auswahlknöpfe zu binden braucht in XAML für jeden Wert einen Umwandler. Die
        /// drei teilen eine Gruppe, es kann also immer nur eines gesetzt sein.
        /// </summary>
        [ObservableProperty]
        public partial bool ModusBeide { get; set; } = true;

        /// <summary>Nur helle Schrift auf dunklem Grund (Karte, Luftbild).</summary>
        [ObservableProperty]
        public partial bool ModusHell { get; set; }

        /// <summary>Nur dunkle Schrift auf hellem Grund (Dokument, Bildschirmfoto).</summary>
        [ObservableProperty]
        public partial bool ModusDunkel { get; set; }

        // Umgelegt heisst: neu suchen, vorher gilt nichts mehr. Nur beim Setzen, nicht
        // beim Abwählen — sonst liefe beim Wechsel zwischen zwei Knöpfen zweimal eine
        // Suche an, denn WPF wählt zuerst den alten ab und dann den neuen aus.
        partial void OnModusBeideChanged(bool value) => SucheErneut(value);

        partial void OnModusHellChanged(bool value) => SucheErneut(value);

        partial void OnModusDunkelChanged(bool value) => SucheErneut(value);

        private void SucheErneut(bool gesetzt)
        {
            if (gesetzt && CanExecuteCommandSuchen())
            {
                CommandExecuteSuchenCommand.Execute(null);
            }
        }

        /// <summary>
        /// Welche Durchgänge der gewählte Modus verlangt: <c>false</c> steht für helle
        /// Schrift auf dunklem Grund, <c>true</c> für dunkle auf hellem.
        ///
        /// Helle zuerst — bei ihr liegt der belegte Gewinn, und bricht der Lauf zwischen
        /// den beiden ab, ist der wichtigere Teil schon zu sehen.
        /// </summary>
        private bool[] GewaehlteSchriftarten()
        {
            if (ModusHell)
            {
                return [false];
            }

            return ModusDunkel ? [true] : [false, true];
        }

        /// <summary>Name des Modus für Kopfzeile und Zustandstext.</summary>
        private string ModusName
        {
            get
            {
                if (ModusHell)
                {
                    return "helle Schrift";
                }

                return ModusDunkel ? "dunkle Schrift" : "hell + dunkel";
            }
        }

        private bool CanExecuteCommandSuchen() => !IsEineAufgabeLäuft && OcrDienst.IstVerfuegbar;

        /// <summary>
        /// Sucht beide Ebenen. Läuft beim Öffnen des Fensters von selbst an und erneut,
        /// wenn der Modus wechselt.
        ///
        /// <b>Zuerst die Standardsuche</b>, und ihre Kästen stehen sofort da: Sie
        /// braucht Zehntelsekunden, das Plugin über eine Sekunde. Wer zusieht, hat damit
        /// die Vergleichsebene vor Augen, während die andere noch entsteht.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteCommandSuchen), IncludeCancelCommand = true)]
        private async Task CommandExecuteSuchen(CancellationToken token)
        {
            IsEineAufgabeLäuft = true;

            // Erst leeren: Was noch dasteht, gehört zum alten Lauf. Ein Rahmen, der
            // jetzt nicht mehr gefunden würde, darf nicht stehen bleiben, während
            // schon neu gesucht wird.
            ZeileUnterMaus = null;
            WindowsFunde.Clear();
            PluginFunde.Clear();
            WindowsKopf = "Windows-OCR, ganzes Bild";
            PluginKopf = "Plugin, schräge Zeilen";
            Status = "Lädt das Bild …";

            try
            {
                BitmapSource bild = await Task.Run(() => BildLader.Lade(BildPfad), token).ConfigureAwait(true);
                token.ThrowIfCancellationRequested();

                Bild = bild;

                // Gezeichnet wird in Bildkoordinaten und danach eingepasst — ein fester
                // Strich wäre bei einem kleinen Bild ein Klotz, bei einem grossen unsichtbar.
                Strichstaerke = Math.Max(1.5, bild.PixelWidth / 700.0);

                Status = "Standardsuche über das ganze Bild …";
                string windowsText = await SucheWindowsEbeneAsync(bild, token).ConfigureAwait(true);

                token.ThrowIfCancellationRequested();

                if (!PluginOcrDienst.IstVerfuegbar)
                {
                    PluginKopf = "Plugin, schräge Zeilen — kein Plugin eingelegt";
                    Status = "Nur die Standardsuche. Zum Vergleichen fehlt das Plugin.";
                    return;
                }

                Status = $"Plugin sucht schräge Zeilen ({ModusName}) …";

                await SuchePluginEbeneAsync(windowsText, token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                Status = "Abgebrochen.";
            }
            finally
            {
                PluginSucht = false;
                IsEineAufgabeLäuft = false;
            }
        }

        /// <summary>
        /// Setzt Balken und Text. Die Restzeit ist linear hochgerechnet und steht erst
        /// ab 2 % da — davor ist sie nur Rauschen. Bei beiden Schriftarten ist sie
        /// anfangs zu hoch: Der dunkle Durchgang geht auf Karten meist leer aus und
        /// ist dann schneller als der helle.
        /// </summary>
        private void MeldePluginFortschritt(DateTime begonnen, double anteil, string stufe)
        {
            PluginFortschritt = anteil;

            if (anteil < 0.02)
            {
                PluginFortschrittText = $"{stufe}: {anteil:P0}";
                return;
            }

            double restSekunden = (DateTime.UtcNow - begonnen).TotalSeconds * (1 - anteil) / anteil;
            PluginFortschrittText = $"{stufe}: {anteil:P0} – noch ca. {Math.Ceiling(restSekunden):F0} s";
        }

        /// <summary>
        /// Füllt die Windows-Ebene und gibt deren Text zurück — er ist das Maß dafür,
        /// was am Plugin-Fund später neu ist.
        /// </summary>
        private async Task<string> SucheWindowsEbeneAsync(BitmapSource bild, CancellationToken token)
        {
            OcrWortBefund? befund = await OcrDienst.SucheWoerterAsync(BildPfad).ConfigureAwait(true);

            if (befund is null)
            {
                WindowsKopf = "Windows-OCR — nicht gelesen";
                return string.Empty;
            }

            token.ThrowIfCancellationRequested();

            // Ausschneiden und Vergrössern je Wort: auf einem Hintergrundfaden, weil ein
            // Bildschirmfoto leicht hundert Wörter hat.
            List<OcrZeilenFund> funde =
                await Task.Run(() => OcrWortAnsicht.Baue(befund, bild), token).ConfigureAwait(true);

            foreach (OcrZeilenFund fund in funde)
            {
                WindowsFunde.Add(fund);
            }

            string winkel = befund.TextWinkel is { } w
                ? $"Textwinkel {w:F1}°"
                : "kein Textwinkel erkannt";

            WindowsKopf = $"Windows-OCR, ganzes Bild — {funde.Count} Wörter, {winkel}, "
                        + $"{befund.Dauer.TotalMilliseconds:F0} ms";

            return befund.Text;
        }

        /// <summary>
        /// Füllt die Plugin-Ebene und zählt, wie viel davon in der Standardsuche
        /// <b>nicht</b> vorkam.
        ///
        /// Das Maß ist dasselbe wie beim Anhängen an den Cache: Ein Zeilentext gilt als
        /// neu, wenn er im Text des ganzen Bildes nicht enthalten ist. Grob, aber
        /// ehrlich — und beide Stellen zählen gleich.
        /// </summary>
        private async Task SuchePluginEbeneAsync(string windowsText, CancellationToken token)
        {
            int mitText = 0;
            int neu = 0;
            TimeSpan dauer = TimeSpan.Zero;

            // Im Modus „hell + dunkel" ab Plugin 0.0.5 ein einziger Lauf: Das Plugin sucht
            // beide Farben, wirft Doppelte weg und verbindet Zeilen über einen Farbwechsel
            // hinweg. Die beiden Einzelmodi bleiben, was sie sind — wer sie wählt, will
            // genau eine Art sehen.
            bool beideAufEinmal = !ModusHell && !ModusDunkel && PluginOcrDienst.KannBeideSchriftfarben;
            bool[] schriftarten = beideAufEinmal ? [false] : GewaehlteSchriftarten();

            int lauf = ++_pluginLauf;
            int durchgang = 0;

            // Eine Startzeit für alle Durchgänge: Die Restzeit gilt für den ganzen Lauf.
            DateTime begonnen = DateTime.UtcNow;

            PluginFortschritt = 0;
            PluginFortschrittText = string.Empty;
            PluginSucht = true;

            foreach (bool dunklerTextAufHell in schriftarten)
            {
                // Festgehalten, weil verspätete Meldungen erst ankommen können, wenn die
                // Schleife schon beim nächsten Durchgang ist.
                int vorher = durchgang++;
                string art = dunklerTextAufHell ? "dunkle Schrift" : "helle Schrift";

                // Im Oberflächenfaden angelegt: Progress stellt die Meldungen hierher zurück.
                var fortschritt = new Progress<(int Erledigt, int Gesamt, string Stufe)>(p =>
                {
                    if (lauf != _pluginLauf)
                    {
                        return;
                    }

                    double anteil = (vorher + (double)p.Erledigt / Math.Max(1, p.Gesamt)) / schriftarten.Length;
                    MeldePluginFortschritt(begonnen, anteil, $"{art} · {p.Stufe}");
                });

                // Die Nummern laufen über beide Durchgänge weiter: In der Liste steht
                // eine Nummer je Rahmen im Bild, und zwei Einsen wären nicht zuzuordnen.
                OcrZeilenBefund? befund = await PluginOcrDienst
                    .SucheBefundAsync(BildPfad, dunklerTextAufHell, token, PluginFunde.Count, fortschritt,
                        beideSchriftfarben: beideAufEinmal)
                    .ConfigureAwait(true);

                token.ThrowIfCancellationRequested();

                if (befund is null)
                {
                    PluginKopf = $"Plugin, schräge Zeilen ({ModusName}) — nicht gelesen";
                    Status = "Das Plugin konnte das Bild nicht lesen.";
                    return;
                }

                dauer += befund.Dauer;

                foreach (OcrZeilenFund zeile in befund.Zeilen)
                {
                    PluginFunde.Add(zeile);

                    if (!zeile.ZaehltAlsText)
                    {
                        continue;
                    }

                    mitText++;

                    if (!windowsText.Contains(zeile.Text, StringComparison.OrdinalIgnoreCase))
                    {
                        neu++;
                    }
                }
            }

            PluginKopf = PluginFunde.Count == 0
                ? $"Plugin, schräge Zeilen ({ModusName}) — keine gefunden, {dauer.TotalMilliseconds:F0} ms"
                : $"Plugin, schräge Zeilen ({ModusName}) — {PluginFunde.Count} Zeilen, "
                  + $"{mitText} mit Text, {neu} davon neu, {dauer.TotalMilliseconds:F0} ms";

            Status = neu > 0
                ? $"{neu} Zeilen hat nur das Plugin gefunden."
                : "Das Plugin hat nichts gefunden, was die Standardsuche nicht schon hatte.";
        }

        /// <summary>
        /// Hebt einen Fund hervor, oder mit <c>null</c> keinen mehr. Gerufen vom
        /// Überfahren der Listen <b>und</b> der Rahmen im Bild — beides zeigt auf
        /// dasselbe.
        /// </summary>
        [RelayCommand]
        private void CommandExecuteZeileHervorheben(OcrZeilenFund? zeile)
        {
            if (ReferenceEquals(ZeileUnterMaus, zeile))
            {
                return;
            }

            // Die alte zuerst löschen: Zwischen Verlassen und Betreten liegt bei
            // benachbarten Zeilen kein Zwischenschritt, sonst blieben zwei hervorgehoben.
            if (ZeileUnterMaus is { } alt)
            {
                alt.IstHervorgehoben = false;
            }

            ZeileUnterMaus = zeile;

            if (zeile is not null)
            {
                zeile.IstHervorgehoben = true;
            }
        }
    }
}
