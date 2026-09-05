using CommunityToolkit.Mvvm.ComponentModel;

namespace TestImage
{
    public partial class MeinBildchen : ObservableObject
    {
        [ObservableProperty]
        private string _bName = "nulli";

        /// <summary>
        /// Zählt jede Pfadänderung irgendeines Bildchens mit.
        ///
        /// Damit kann ein Verbraucher ein aus allen Pfaden abgeleitetes Ergebnis
        /// zwischenspeichern und mit einem einzigen Vergleich feststellen, ob es noch
        /// gilt — ohne jedes Element einzeln abonnieren zu müssen und ohne dass jemand
        /// daran denken muss, an einer neuen Verschiebe-Stelle Bescheid zu geben.
        /// Verwendet von <c>AufgabeViewModel.ListeStammtAusEinemOrdner</c>.
        ///
        /// Statisch, weil die Zahl nichts über ein einzelnes Bildchen aussagt, sondern
        /// nur „irgendwo hat sich ein Pfad geändert". Ein Überlauf ist bedeutungslos:
        /// Verglichen wird auf Gleichheit, und zwei aufeinanderfolgende Stände sind auch
        /// dann verschieden.
        /// </summary>
        internal static int PfadGeneration { get; private set; }

        partial void OnBNameChanged(string value) => PfadGeneration++;

        [ObservableProperty]
        private bool _bildFürLinks = false;

        /// <summary>
        /// True, wenn beim Indexieren ein Wasserzeichen gefunden wurde – sichtbar
        /// aufgeprägt oder als Markierung in den Metadaten. Steuert das Badge auf
        /// der Miniatur.
        /// </summary>
        [ObservableProperty]
        private bool _hatWasserzeichen = false;

        /// <summary>Begründung für den Tooltip des Wasserzeichen-Badges.</summary>
        [ObservableProperty]
        private string _wasserzeichenGrund = string.Empty;

        /// <summary>
        /// Die 120-Pixel-Miniatur für die Bildleiste, oder <c>null</c>, solange sie noch
        /// nicht geladen ist.
        ///
        /// Gefüllt wird sie von <see cref="MiniaturLader"/>, sobald eine Kachel diese Datei
        /// zugewiesen bekommt. Vorher band die Leiste über einen Konverter auf
        /// <see cref="BName"/> — und dekodierte damit im UI-Faden, während man scrollte.
        /// Die Kachel ist jetzt kurz leer, statt dass die Leiste ruckelt.
        /// </summary>
        [ObservableProperty]
        public partial System.Windows.Media.ImageSource? Miniatur { get; set; }
    }
}