using System.Windows.Controls;

namespace TestImage.Ansichten
{
    /// <summary>
    /// Byte-Dubletten-Ansicht (IsDublettenAnsicht = true). Erbt den DataContext
    /// vom Host (MainWindow) und teilt sich damit das AufgabeViewModel.
    /// Die gesamte Logik liegt im ViewModel (AufgabeViewModel.ByteDubletten.cs).
    /// </summary>
    public partial class DublettenAnsicht : UserControl
    {
        public DublettenAnsicht()
        {
            InitializeComponent();
        }
    }
}
