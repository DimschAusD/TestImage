using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace TestImage.Bildersuche
{
    public partial class IndexSuchPanel : UserControl
    {
        public IndexSuchPanel()
        {
            InitializeComponent();
        }

        // --- Griffleiste: Panel im Popup mit der Maus verschieben ---
        // Das Panel ist das Child eines Popups; verschoben wird über dessen
        // Horizontal-/VerticalOffset. Als Bezug dient die Maus-Bildschirmposition,
        // damit das Ziehen stabil bleibt, während sich das Panel mitbewegt.
        private bool _zieht;
        private Point _startMausScreen;
        private double _startOffsetH;
        private double _startOffsetV;

        private void Griffleiste_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Parent is not Popup popup) return;

            _zieht = true;
            _startMausScreen = PointToScreen(e.GetPosition(this));
            _startOffsetH = popup.HorizontalOffset;
            _startOffsetV = popup.VerticalOffset;
            BRD_Griffleiste.CaptureMouse();
            e.Handled = true;
        }

        private void Griffleiste_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_zieht || Parent is not Popup popup) return;

            Point jetztScreen = PointToScreen(e.GetPosition(this));
            popup.HorizontalOffset = _startOffsetH + (jetztScreen.X - _startMausScreen.X);
            popup.VerticalOffset = _startOffsetV + (jetztScreen.Y - _startMausScreen.Y);
        }

        private void Griffleiste_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_zieht) return;

            _zieht = false;
            BRD_Griffleiste.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void TXT_Suche_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Tab) return;
            if (DataContext is not AufgabeViewModel vm) return;
            if (string.IsNullOrEmpty(vm.SucheVorschlagRest)) return;

            vm.CommandExecuteVorschlagUebernehmenCommand.Execute(null);
            TXT_Suche.CaretIndex = TXT_Suche.Text.Length;
            e.Handled = true;
        }

        private void BegriffChip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is string chipText &&
                DataContext is AufgabeViewModel vm)
            {
                vm.CommandExecuteBegriffHeatmapCommand.Execute(chipText);
                e.Handled = true;
            }
        }

        private void Vorschlag_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is string wort &&
                DataContext is AufgabeViewModel vm)
            {
                vm.CommandExecuteVorschlagGewaehltCommand.Execute(wort);
                TXT_Suche.CaretIndex = TXT_Suche.Text.Length;
                TXT_Suche.Focus();
                e.Handled = true;
            }
        }

        private void BegriffChip_AlleAnzeigen(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi &&
                mi.DataContext is string chipText &&
                DataContext is AufgabeViewModel vm)
            {
                vm.CommandExecuteBegriffSucheCommand.Execute(chipText);
            }
        }
    }
}
