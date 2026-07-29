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

            // Ctrl+A → Bild nach links (statt SelectAll)
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (vm?.CommandExecuteBildLinksCommand.CanExecute(null) == true)
                    vm.CommandExecuteBildLinksCommand.Execute(null);
                e.Handled = true;
            }

            // Ctrl+Z → Verschieben rückgängig (statt Undo)
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (vm?.CommandExecuteVerschiebenZurückCommand.CanExecute(null) == true)
                    vm.CommandExecuteVerschiebenZurückCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
