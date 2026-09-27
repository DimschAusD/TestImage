using System.Windows;
using System.Windows.Input;

namespace TestImage.Ansichten
{
    /// <summary>
    /// Fenster der Rahmenansicht. Enthält absichtlich nur Fensterkram: Was die Ansicht
    /// zeigt und rechnet, steht in <see cref="OcrTextAnsichtViewModel"/>.
    ///
    /// <b>Eigenes Fenster und kein Einschub in der Vollbildansicht:</b> Die Ansicht
    /// braucht Bildfläche, einen Streifen darunter und eine Liste daneben — als Auflage
    /// über dem Bild verdeckte sie genau das, was man beurteilen will. Sie wird je Bild
    /// geöffnet und beim Schliessen weggeworfen; ihre Bitmaps sollen nicht über die
    /// Sitzung im Speicher stehen bleiben.
    /// </summary>
    public partial class OcrTextAnsicht : Window
    {
        internal OcrTextAnsicht(OcrTextAnsichtViewModel modell)
        {
            InitializeComponent();

            DataContext = modell;

            // Der Titel nennt die Datei: Es können mehrere dieser Fenster offen sein,
            // und in der Fensterliste sind sie sonst nicht zu unterscheiden.
            Title = $"Text mit Rahmen — {modell.Dateiname}";
        }

        /// <summary>
        /// Öffnet die Ansicht für ein Bild.
        ///
        /// Fensterverwaltung gehört nicht ins ViewModel — deshalb steht sie hier, und
        /// das ViewModel der Anwendung ruft diese eine Zeile. Ohne Besitzer läge das
        /// Fenster hinter dem Hauptfenster, sobald man dort einmal klickt.
        /// </summary>
        internal static void Zeige(string bildPfad)
        {
            var fenster = new OcrTextAnsicht(new OcrTextAnsichtViewModel(bildPfad))
            {
                Owner = Application.Current?.MainWindow
            };

            fenster.Show();
        }

        /// <summary>Esc schliesst — wie bei jedem Fenster, das man nur zum Ansehen öffnet.</summary>
        private void Fenster_TasteGedrueckt(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }
    }
}
