using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

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
            if (e.PropertyName == nameof(AufgabeViewModel.IsImageMaximiert) && sender is AufgabeViewModel vm)
            {
                SetTitleBarDark(vm.IsImageMaximiert);
            }
        }

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
                else if (vm?.IsImageMaximiert == true)
                    VIEW_Vollbild.ShakeImage(nachRechts: false);
                e.Handled = true;
            }
            else if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (vm?.CommandExecuteBildNachRechtsCommand.CanExecute(null) == true)
                    vm.CommandExecuteBildNachRechtsCommand.Execute(null);
                else if (vm?.IsImageMaximiert == true)
                    VIEW_Vollbild.ShakeImage(nachRechts: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (vm?.CommandExecuteBildInsKeinFavVerzeichnisVerschiebenCommand.CanExecute(null) == true)
                    vm.CommandExecuteBildInsKeinFavVerzeichnisVerschiebenCommand.Execute(null);
                else
                    WackleNachUnten(vm);   // geht gerade nicht – kurz wackeln statt stumm bleiben
                e.Handled = true;
            }

            // Shift+↓ → Bild in den Ordner „Besonders".
            // Nicht in Eingabefeldern: dort markiert Shift+↓ Text.
            else if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.Shift && !IstTextEingabeAktiv())
            {
                if (vm?.CommandExecuteBildInsBesondersVerschiebenCommand.CanExecute(null) == true)
                    vm.CommandExecuteBildInsBesondersVerschiebenCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && Keyboard.Modifiers == ModifierKeys.None)
            {
                // Aktuelles Bild zurück → sonst letzte Verschiebung rückgängig
                if (vm?.CommandExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand.CanExecute(null) == true)
                    vm.CommandExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand.Execute(null);
                else if (vm?.CommandExecuteVerschiebenZurückCommand.CanExecute(null) == true)
                    vm.CommandExecuteVerschiebenZurückCommand.Execute(null);
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
                    vm.CommandExecuteBildInsKIFehlerVerschiebenCommand.Execute(null);
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
        private void WackleNachUnten(AufgabeViewModel? vm)
        {
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
