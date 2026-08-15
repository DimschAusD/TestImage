using System.ComponentModel;
using System.Windows;

namespace TestImage.Ansichten
{
    /// <summary>
    /// Werkzeugfenster für die Bildersuche. Nimmt dasselbe <c>IndexSuchPanel</c> auf,
    /// das zuvor in einem Popup steckte — das Panel selbst ist unverändert.
    /// </summary>
    public partial class IndexSuchFenster : Window
    {
        /// <summary>
        /// Auf true setzen, bevor das Fenster endgültig geschlossen werden soll —
        /// beim Beenden der Anwendung. Sonst wird das Schliessen abgefangen und das
        /// Fenster nur versteckt.
        /// </summary>
        public bool DarfEndgueltigSchliessen { get; set; }

        public IndexSuchFenster()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Beim Schliessen über das Kreuz nur verstecken, nicht zerstören.
        ///
        /// So bleiben Grösse und Position über die Sitzung erhalten, und der Knopf in
        /// der Normalansicht öffnet dasselbe Fenster wieder, statt jedes Mal ein neues
        /// in der Mitte aufzuziehen.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!DarfEndgueltigSchliessen)
            {
                e.Cancel = true;
                Hide();

                // Zustand im ViewModel nachziehen, damit der Knopf wieder „zu" meldet.
                if (DataContext is AufgabeViewModel vm)
                {
                    vm.IsSuchleisteOffen = false;
                }
            }

            base.OnClosing(e);
        }
    }
}
