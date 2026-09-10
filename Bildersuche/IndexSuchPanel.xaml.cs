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

        /// <summary>
        /// Hält die Rollposition fest, wenn eine Miniatur angeklickt wird.
        ///
        /// Der angeklickte Knopf bekommt den Tastaturfokus, und WPF schiebt ihn
        /// daraufhin in den Blick. Diese Anfrage bleibt nicht in der Trefferfläche:
        /// Sie wandert hoch bis zum Rollbereich des Suchfensters, der dann das ganze
        /// Panel verschiebt. Bei einer Miniatur aus den unteren Reihen sprang die
        /// Fläche deshalb weg und zeigte oben und unten angeschnittene Reihen.
        ///
        /// Preis: Der Fokus wandert beim Tabben weiter, die Fläche rollt ihm nur
        /// nicht mehr nach. Die Miniaturen tragen ohnehin keinen Fokusrahmen.
        /// </summary>
        private void SuchErgebnisse_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
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
