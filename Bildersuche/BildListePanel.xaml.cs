using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Schnell-Liste: zeigt alle Bilder als Kachel-Raster, das zeilenweise
    /// virtualisiert wird (nur sichtbare Zeilen werden erzeugt). Ein Klick auf eine
    /// Kachel springt zum Bild und schließt das Popup (über den Command im ViewModel).
    /// Das aktuell gewählte Bild wird markiert und sichtbar gescrollt.
    /// </summary>
    public partial class BildListePanel : UserControl
    {
        public BildListePanel()
        {
            InitializeComponent();
        }

        private void LBX_BildListe_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ScrolleZurAuswahl();
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
                ScrolleZurAuswahl();
        }

        /// <summary>Holt die Zeile mit dem gewählten Bild nach dem Layout in den sichtbaren Bereich.</summary>
        private void ScrolleZurAuswahl()
        {
            if (LBX_BildListe.SelectedItem == null) return;

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new System.Action(() =>
            {
                if (LBX_BildListe.SelectedItem != null)
                    LBX_BildListe.ScrollIntoView(LBX_BildListe.SelectedItem);
            }));
        }
    }
}
