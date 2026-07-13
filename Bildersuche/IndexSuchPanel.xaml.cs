using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TestImage.Bildersuche
{
    public partial class IndexSuchPanel : UserControl
    {
        public IndexSuchPanel()
        {
            InitializeComponent();
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
