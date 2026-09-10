using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TestImage.Ansichten;

namespace TestImage
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml — nur noch Host: schaltet zwischen
    /// NormalAnsicht und VollbildAnsicht (per IsImageMaximiert) und behält die
    /// fensterweiten Belange (dunkle Titelleiste, globale Tastatur-Navigation).
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private void SetTitleBarDark(bool dark)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int value = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }

        public MainWindow()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                if (DataContext is AufgabeViewModel vm)
                    vm.PropertyChanged += OnVmPropertyChanged;
            };
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not AufgabeViewModel vm)
                return;

            if (e.PropertyName == nameof(AufgabeViewModel.IsImageMaximiert))
            {
                SetTitleBarDark(vm.IsImageMaximiert);

                // Das Eigenschaften-Feld gibt es nur im Vollbild. Verlässt man die Ansicht
                // mit offenem Feld, muss die dafür geholte Breite mit ihm verschwinden —
                // sonst stünde ein zu breites Fenster ohne sichtbaren Anlass da.
                //
                // Ohne Animation: Beim Ansichtswechsel wird ohnehin alles ausgetauscht, da
                // wäre eine aufziehende Spalte nur ein zweiter Vorgang im selben Moment.
                PasseFensterbreiteAnBildinfoAn(vm.IsImageMaximiert && vm.IsBildinfoSichtbar, TimeSpan.Zero);
            }
            else if (e.PropertyName == nameof(AufgabeViewModel.IsBildinfoSichtbar))
            {
                PasseFensterbreiteAnBildinfoAn(vm.IsImageMaximiert && vm.IsBildinfoSichtbar, BildinfoFahrdauer);
            }
        }

        #region Fensterbreite fürs Eigenschaften-Feld

        /// <summary>Untergrenze, die das Zuklappen nicht unterschreiten darf.</summary>
        private const double FensterMindestbreite = 400;

        /// <summary>
        /// Dauer, in der das Feld herein- und hinausfährt.
        ///
        /// Etwas grosszügiger als die frühere Fensterfahrt: Hier bewegt sich nur noch eine
        /// fertige Textur, das darf man auch sehen.
        /// </summary>
        private static readonly TimeSpan BildinfoFahrdauer = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// Um wie viel das Fenster fürs Feld gewachsen ist; 0 = gar nicht. Beim Zuklappen
        /// wird genau dieser Betrag zurückgegeben und nie pauschal die Spaltenbreite —
        /// sonst schrumpfte auch ein Fenster, das nie gewachsen ist.
        /// </summary>
        private double _bildinfoZuwachs;

        /// <summary>Linke Kante vor dem Wachsen — nur belegt, wenn dafür geschoben wurde.</summary>
        private double _bildinfoLinksVorher;

        /// <summary>
        /// Linke Kante, die wir zuletzt selbst gesetzt haben. Schiebt der Nutzer das Fenster
        /// danach von Hand, bleibt es beim Zuklappen dort stehen, wo er es hingestellt hat.
        /// </summary>
        private double _bildinfoLinksGesetzt;

        /// <summary>
        /// Holt die Breite fürs Eigenschaften-Feld vom Bildschirm statt vom Bild.
        ///
        /// Ohne das nähme die Spalte dem Bild 300 Punkte weg — ausgerechnet in der Ansicht,
        /// in der das Bild so gross wie möglich sein soll. Also wächst das Fenster um die
        /// Spaltenbreite und gibt sie beim Zuklappen wieder her.
        ///
        /// Drei Fälle bleiben beim alten Verhalten (das Bild rückt zur Seite), weil Wachsen
        /// dort nicht möglich ist: maximiertes Fenster, Fenster schon so breit wie die
        /// Arbeitsfläche, und der Rest, wenn nur ein Teil der Breite frei ist.
        ///
        /// Die Breite ändert sich in einem Schritt, nicht animiert. Eine animierte
        /// Fensterbreite läuft nicht im selben Takt wie das Layout dahinter — die Breite
        /// kommt über SetWindowPos herein, die Spalte im Rendertakt, und was zwischen den
        /// beiden nicht zusammenpasst, sieht man am Bild als Zappeln und an der
        /// Titelleiste als Nachziehen. Ein einziger Sprung hat diesen Zwischenzustand
        /// nicht; die Fahrt macht danach das Feld, das keine Fenstergrösse anfasst.
        /// </summary>
        private void PasseFensterbreiteAnBildinfoAn(bool sichtbar, TimeSpan dauer)
        {
            // Wann die Fensterbreite an der Reihe ist, entscheidet die Ansicht: Sie muss den
            // Schritt in denselben Layout-Durchlauf legen wie das Erscheinen der Spalte.
            VIEW_Vollbild.SetzeBildinfoSpalte(
                sichtbar,
                dauer,
                sichtbar ? VergrössereFensterFürBildinfo : VerkleinereFensterNachBildinfo);
        }

        private void VergrössereFensterFürBildinfo()
        {
            if (_bildinfoZuwachs > 0 || WindowState != WindowState.Normal)
                return;

            var fläche = ErmittleArbeitsfläche();
            double zuwachs = Math.Min(VollbildAnsicht.BildinfoSpaltenbreite, Math.Max(0, fläche.Width - Width));
            if (zuwachs <= 0)
                return;

            _bildinfoZuwachs = zuwachs;
            _bildinfoLinksVorher = Left;
            Width += zuwachs;

            // Nach rechts ist am Bildschirmrand Schluss: Was dort nicht mehr hinpasst,
            // holt sich das Fenster nach links, sonst läge die neue Spalte ausserhalb.
            if (Left + Width > fläche.Right)
                Left = Math.Max(fläche.Left, fläche.Right - Width);

            _bildinfoLinksGesetzt = Left;
        }

        private void VerkleinereFensterNachBildinfo()
        {
            if (_bildinfoZuwachs <= 0)
                return;

            Width = Math.Max(FensterMindestbreite, Width - _bildinfoZuwachs);

            if (Math.Abs(Left - _bildinfoLinksGesetzt) < 1)
                Left = _bildinfoLinksVorher;

            _bildinfoZuwachs = 0;
        }

        /// <summary>
        /// Arbeitsfläche des Bildschirms, auf dem das Fenster gerade liegt.
        ///
        /// <see cref="SystemParameters.WorkArea"/> meint immer den Hauptbildschirm; auf dem
        /// zweiten Schirm käme das Fenster damit an einer Kante an, die es dort nicht gibt.
        /// Fällt darauf nur zurück, wenn der Monitor nicht zu ermitteln ist.
        /// </summary>
        private Rect ErmittleArbeitsfläche()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var quelle = PresentationSource.FromVisual(this) as HwndSource;
            if (hwnd == IntPtr.Zero || quelle?.CompositionTarget is null)
                return SystemParameters.WorkArea;

            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
                return SystemParameters.WorkArea;

            // Der Monitor rechnet in Gerätepunkten, Left und Width in WPF-Einheiten.
            var vonGerät = quelle.CompositionTarget.TransformFromDevice;
            var obenLinks = vonGerät.Transform(new Point(info.rcWork.Left, info.rcWork.Top));
            var untenRechts = vonGerät.Transform(new Point(info.rcWork.Right, info.rcWork.Bottom));
            return new Rect(obenLinks, untenRechts);
        }

        private const int MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        #endregion

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var vm = DataContext as AufgabeViewModel;

            // Esc schliesst die Tastenübersicht – in beiden Ansichten, denn geöffnet
            // werden kann sie auch über den Knopf in der Normalansicht.
            if (e.Key == Key.Escape && vm?.IsVollbildHilfeOffen == true)
            {
                vm.IsVollbildHilfeOffen = false;
                e.Handled = true;
                return;
            }

            // Pfeiltasten: Bild navigieren (vor ListBox-Scroll abfangen)
            if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (vm?.CommandExecuteBildLinksCommand.CanExecute(null) == true)
                    vm.CommandExecuteBildLinksCommand.Execute(null);
                else if (vm?.IsImageMaximiert == true && DarfWackeln(vm))
                    VIEW_Vollbild.ShakeImage(nachRechts: false);
                e.Handled = true;
            }
            else if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (vm?.CommandExecuteBildNachRechtsCommand.CanExecute(null) == true)
                    vm.CommandExecuteBildNachRechtsCommand.Execute(null);
                else if (vm?.IsImageMaximiert == true && DarfWackeln(vm))
                    VIEW_Vollbild.ShakeImage(nachRechts: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (vm?.CommandExecuteBildInsKeinFavVerzeichnisVerschiebenCommand.CanExecute(null) == true)
                {
                    vm.CommandExecuteBildInsKeinFavVerzeichnisVerschiebenCommand.Execute(null);
                    ZeigeKantenschein(vm, VollbildAnsicht.Bildablage.KeinFav);
                }
                else
                    WackleNachUnten(vm);   // geht gerade nicht – kurz wackeln statt stumm bleiben
                e.Handled = true;
            }

            // Shift+↓ → Bild in den Ordner „Besonders".
            // Nicht in Eingabefeldern: dort markiert Shift+↓ Text.
            else if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.Shift && !IstTextEingabeAktiv())
            {
                if (vm?.CommandExecuteBildInsBesondersVerschiebenCommand.CanExecute(null) == true)
                {
                    vm.CommandExecuteBildInsBesondersVerschiebenCommand.Execute(null);
                    ZeigeKantenschein(vm, VollbildAnsicht.Bildablage.Besonders);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Up && Keyboard.Modifiers == ModifierKeys.None)
            {
                // Aktuelles Bild zurück → sonst letzte Verschiebung rückgängig
                if (vm?.CommandExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand.CanExecute(null) == true)
                {
                    vm.CommandExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand.Execute(null);
                    ZeigeKantenschein(vm, VollbildAnsicht.Bildablage.Zurückgeholt);
                }
                else if (vm?.CommandExecuteVerschiebenZurückCommand.CanExecute(null) == true)
                {
                    vm.CommandExecuteVerschiebenZurückCommand.Execute(null);
                    ZeigeKantenschein(vm, VollbildAnsicht.Bildablage.Zurückgeholt);
                }
                e.Handled = true;
            }

            // Ctrl+Z → Verschieben rückgängig (statt Undo)
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (vm?.CommandExecuteVerschiebenZurückCommand.CanExecute(null) == true)
                    vm.CommandExecuteVerschiebenZurückCommand.Execute(null);
                e.Handled = true;
            }

            // K → Bild in den KI-Fehler-Ordner, in BEIDEN Ansichten.
            //
            // Steht hier oben und nicht unten bei den Bildmodus-Tasten, weil es auch in
            // der Normalansicht gelten soll. Der Wächter macht es möglich: Ohne ihn
            // schluckte ein blosses K jede Eingabe im Filterfeld.
            //
            // Nebenwirkung, bewusst in Kauf genommen: In den Bilderlisten funktioniert
            // das Anspringen per Anfangsbuchstabe für K nicht mehr.
            else if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.None && !IstTextEingabeAktiv())
            {
                if (vm?.CommandExecuteBildInsKIFehlerVerschiebenCommand.CanExecute(null) == true)
                {
                    vm.CommandExecuteBildInsKIFehlerVerschiebenCommand.Execute(null);
                    ZeigeKantenschein(vm, VollbildAnsicht.Bildablage.KIFehler);
                }
                e.Handled = true;
            }

            // Umschalt+F → erweiterte Suche ein-/ausblenden, dasselbe wie
            // BTN_IndexSuchleiste. Strg+F bleibt bewusst frei: Das steht überall für die
            // einfache Suche im Sichtbaren, hier geht es über den Index.
            //
            // Nicht in Eingabefeldern: dort ist Shift+F schlicht ein grosses F.
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Shift && !IstTextEingabeAktiv())
            {
                if (vm?.CommandExecuteSuchleisteToggleCommand.CanExecute(null) == true)
                    vm.CommandExecuteSuchleisteToggleCommand.Execute(null);
                e.Handled = true;
            }

            // F1 → Tastenübersicht, in beiden Ansichten. Dasselbe wie BTN_TastenHilfe
            // in der Normalansicht, nur eben über die Taste. F1 darf auch im Suchfeld
            // greifen: Es steht für kein Schriftzeichen und stört das Tippen nicht.
            else if (e.Key == Key.F1 && vm is not null)
            {
                vm.CommandExecuteVollbildHilfeToggleCommand.Execute(null);
                e.Handled = true;
            }

            // „?" nur im Bildmodus. Dort gibt es keine Eingabefelder; in der
            // Normalansicht würde die Taste sonst das Tippen von „?" verschlucken.
            // Eigene Modifier-Prüfung, weil „?" auf deutscher Tastatur Shift+ß ist.
            else if (vm?.IsImageMaximiert == true
                     && e.Key == Key.OemQuestion
                     && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
            {
                vm.CommandExecuteVollbildHilfeToggleCommand.Execute(null);
                e.Handled = true;
            }

            // Einzelbuchstaben nur im Bildmodus: Dort gibt es keine Eingabefelder.
            // In der Normalansicht würden sie das Tippen in Suchfeld und Filter stören.
            else if (vm?.IsImageMaximiert == true && Keyboard.Modifiers == ModifierKeys.None)
            {
                BehandleVollbildTaste(vm, e);
            }
        }

        /// <summary>
        /// Lässt das gerade sichtbare Bild kurz nach unten wackeln. Das waagerechte
        /// Wackeln am Listenende gibt es nur im Bildmodus, weil dort auch nur dessen
        /// Bild zu sehen ist – nach unten wird aber in beiden Ansichten verschoben,
        /// also muss die Rückmeldung auch in beiden ankommen.
        /// </summary>
        /// <summary>
        /// Darf die Ansicht jetzt wackeln?
        ///
        /// Das Wackeln heisst „hier ist Schluss" — Anfang oder Ende der Liste. Es lief
        /// aber auch dann, wenn der Weg nur für einen Augenblick versperrt ist, weil das
        /// nächste Bild noch lädt: Die Pfeile hängen an <c>PrüfungLäuft</c> und melden
        /// solange <c>CanExecute == false</c>, ganz gleich, wo in der Liste man steht.
        /// Damit stand dieselbe Bewegung für zwei ganz verschiedene Sachverhalte, und der
        /// häufigere von beiden war der falsche: Mitten in der Liste zu wackeln sieht aus,
        /// als wäre die Liste dort zu Ende. Auf einer langsamen Platte war das der
        /// Normalfall.
        ///
        /// Solange gewartet wird, bleibt die Ansicht deshalb ruhig; was los ist, sagt der
        /// Ring in der Bildmitte (CTL_WarteIndikator). Am wirklichen Listenende wackelt es
        /// unverändert.
        /// </summary>
        private static bool DarfWackeln(AufgabeViewModel? vm) => vm is not null && !vm.WartenLäuft;

        /// <summary>
        /// Lässt die Vollbildansicht kurz an der Kante aufleuchten, in die das Bild
        /// gegangen ist — die Gegenmeldung zum Wackeln: Dort ist etwas <i>nicht</i>
        /// gegangen, hier ist etwas gegangen.
        ///
        /// Nur im Bildmodus. Die Normalansicht sagt dasselbe schon auf ihre Art: Dort
        /// trägt der Hintergrund über CLconverterColorBoolianMasterHintergrund die Farbe
        /// des Zustands, und man sieht die Kachel aus der Liste wandern.
        /// </summary>
        private void ZeigeKantenschein(AufgabeViewModel? vm, VollbildAnsicht.Bildablage ablage)
        {
            if (vm?.IsImageMaximiert == true)
                VIEW_Vollbild.ZeigeKantenschein(ablage);
        }

        private void WackleNachUnten(AufgabeViewModel? vm)
        {
            if (!DarfWackeln(vm))
                return;

            if (vm?.IsImageMaximiert == true)
                VIEW_Vollbild.ShakeImageSenkrecht(nachUnten: true);
            else
                VIEW_Normal.ShakeImageSenkrecht(nachUnten: true);
        }

        /// <summary>
        /// True, wenn der Tastaturfokus in einem Eingabefeld liegt. Dort haben
        /// Tastenkombinationen wie Shift+Pfeil ihre eigene Bedeutung (Text markieren)
        /// und dürfen nicht abgefangen werden.
        /// </summary>
        private static bool IstTextEingabeAktiv()
            => Keyboard.FocusedElement is System.Windows.Controls.TextBox
                or System.Windows.Controls.PasswordBox
                or System.Windows.Controls.ComboBox;

        /// <summary>
        /// Tastenkürzel für den Bildmodus. Sie bilden die Knöpfe nach, die in der
        /// Normalansicht sichtbar sind — im Vollbild soll nichts die Ansicht verdecken.
        /// </summary>
        private void BehandleVollbildTaste(AufgabeViewModel vm, KeyEventArgs e)
        {
            switch (e.Key)
            {
                // K steht nicht mehr hier, sondern weiter oben in Window_PreviewKeyDown:
                // Es gilt inzwischen in beiden Ansichten und wird deshalb vor dieser
                // Methode abgefangen. Ein Fall hier wäre toter Code.

                // S → Bildgrösse/Stretch umschalten
                case Key.S:
                    if (vm.CommandExecuteBildStretchAnpassenCommand.CanExecute(null))
                        vm.CommandExecuteBildStretchAnpassenCommand.Execute(null);
                    e.Handled = true;
                    break;

                // I → Eigenschaften des Bildes ein-/ausblenden
                case Key.I:
                    vm.CommandExecuteBildinfoToggleCommand.Execute(null);
                    e.Handled = true;
                    break;

                // E → Datei im Explorer zeigen
                case Key.E:
                    if (vm.CommandExecuteDateiImExplorerÖffnenCommand.CanExecute(null))
                        vm.CommandExecuteDateiImExplorerÖffnenCommand.Execute(null);
                    e.Handled = true;
                    break;

                // R → Ordner neu einlesen
                case Key.R:
                    if (vm.CommandExecuteAlleBilderNeuEinlesenCommand.CanExecute(null))
                        vm.CommandExecuteAlleBilderNeuEinlesenCommand.Execute(null);
                    e.Handled = true;
                    break;

                // Esc → erst die Hilfe schliessen, sonst den Bildmodus verlassen
                case Key.Escape:
                    if (vm.IsVollbildHilfeOffen)
                        vm.IsVollbildHilfeOffen = false;
                    else if (vm.CommandExecuteImageMaximierenToggleCommand.CanExecute(null))
                        vm.CommandExecuteImageMaximierenToggleCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }
    }
}
