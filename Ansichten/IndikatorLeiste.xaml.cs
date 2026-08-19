using System.Windows.Controls;

namespace TestImage.Ansichten
{
    /// <summary>
    /// Die drei Aufnahme-Anzeigen (Webcam, Mikrofon, Bildschirm) als wiederverwendbares
    /// Stück Oberfläche.
    ///
    /// Kein eigener Zustand und kein eigener DataContext: Das Steuerelement erbt ihn von
    /// der Ansicht, in der es steckt, und liest nur die drei Schalter des ViewModels.
    /// Deshalb bleibt das Code-Behind leer.
    /// </summary>
    public partial class IndikatorLeiste : UserControl
    {
        public IndikatorLeiste()
        {
            InitializeComponent();
        }
    }
}
