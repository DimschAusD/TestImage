using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

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

        /// <summary>
        /// Breite einmalig aus dem Inhalt übernehmen, danach dem Nutzer überlassen.
        ///
        /// <c>SizeToContent="Width"</c> im XAML lässt das Fenster beim ersten Anzeigen
        /// genau so breit werden, wie die Werkzeugleiste es braucht — keine von Hand
        /// gepflegte Zahl, die bei jedem neuen Knopf in der Leiste nachgezogen werden
        /// müsste und dabei mal zu knapp (rechts angeschnitten), mal zu weit (Karten
        /// ragen über die Leiste hinaus) ausfällt.
        ///
        /// Danach zurück auf <c>Manual</c>, sonst zöge das Fenster jede Breitenänderung
        /// des Nutzers sofort wieder auf die Inhaltsbreite zurück. Die gemessene Breite
        /// wird zur Untergrenze: schmaler würde die Leiste angeschnitten.
        /// </summary>
        private void Fenster_Geladen(object sender, RoutedEventArgs e)
        {
            if (SizeToContent == SizeToContent.Manual)
            {
                return;
            }

            SizeToContent = SizeToContent.Manual;
            MinWidth = ActualWidth;
        }

        /// <summary>
        /// Der eigene Schliessknopf in der Titelleiste. Bewusst <c>Close</c> und nicht
        /// <c>Hide</c>: So läuft er durch <see cref="OnClosing"/> wie das System-Kreuz
        /// zuvor — Grösse und Position bleiben, und <c>IsSuchleisteOffen</c> wird
        /// nachgezogen.
        /// </summary>
        private void FensterSchliessen_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Nimmt gezogene Dateien überhaupt erst an.
        ///
        /// Ohne dieses Zutun meldet die Ablage <c>None</c> zurück, Windows zeigt das
        /// Verbotszeichen und liefert <c>Drop</c> gar nicht aus — das Fenster bliebe offen.
        ///
        /// <b>Bewusst als steigendes Ereignis, nicht als Preview:</b> Die Index-Ordner-Karte
        /// im Panel setzt <c>Handled</c>, während etwas über ihr schwebt. Ihr Ziehen erreicht
        /// dieses Fenster also nie, und der Ordner-Abwurf auf die Karte bleibt unberührt —
        /// samt der Möglichkeit, eine Datei darauf zu ziehen und deren Ordner zu nehmen.
        /// </summary>
        private void Fenster_DragOver(object sender, DragEventArgs e)
        {
            if (!IstDateiZiehen(e))
            {
                return;
            }

            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        /// <summary>
        /// Ein Abwurf irgendwo auf dem Suchfenster: Fenster zu, und die Dateien gehen an die
        /// Normalansicht, als wären sie dort abgelegt worden.
        ///
        /// <b>Das Problem:</b> Das Suchfenster liegt über dem Hauptfenster. Wer eine Datei
        /// aus dem Explorer auf die Bildfläche ziehen will, während die Suche offen ist,
        /// trifft das Suchfenster — und der Abwurf verschwand wortlos.
        ///
        /// <b>Warum weitergeben und nicht bloss schliessen:</b> Beim reinen Schliessen wäre
        /// die gezogene Datei verloren, weil der Ziehvorgang mit dem Abwurf endet. Man müsste
        /// sie ein zweites Mal holen. Das Fenster bekommt dasselbe ViewModel wie die
        /// Normalansicht (siehe <c>NormalAnsicht.ZeigeSuchfenster</c>), also führt derselbe
        /// Aufruf hierhin, den <c>FileDragDropHelper</c> an der Bildfläche auslöst.
        ///
        /// <b>Warum nicht schon beim Hereinziehen verstecken</b>, damit derselbe Zug auf der
        /// Bildfläche landet: Dann käme man mit einem Ordner nicht mehr zur Index-Karte, weil
        /// das Fenster verschwindet, bevor man sie erreicht.
        ///
        /// <c>Close</c> statt <c>Hide</c>, damit derselbe Weg wie beim Kreuz gilt: Das
        /// Fenster wird nur versteckt, Grösse und Position bleiben, und
        /// <c>IsSuchleisteOffen</c> wird nachgezogen.
        ///
        /// <b>Warum nachgereicht statt sofort:</b> Zum Zeitpunkt des Aufrufs hängt der
        /// Ziehvorgang noch am Explorer. Ein Fenster mitten in dieser Rückmeldung
        /// verschwinden zu lassen, kann den Abwurf hängen lassen — deshalb erst, wenn er
        /// abgeschlossen ist.
        /// </summary>
        private void Fenster_Drop(object sender, DragEventArgs e)
        {
            if (!IstDateiZiehen(e))
            {
                return;
            }

            e.Handled = true;

            string[]? pfade = HolePfade(e);

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    Close();

                    if (pfade is { Length: > 0 } && DataContext is IFileDragDropTarget ziel)
                    {
                        // Absichtlich nicht abgewartet – genau wie im FileDragDropHelper an
                        // der Bildfläche. Das Einlesen des Ordners meldet seinen Fortschritt
                        // selbst; hier gibt es nichts, was danach noch zu tun wäre.
                        _ = ziel.OnFileDrop(pfade);
                    }
                }),
                DispatcherPriority.Background);
        }

        /// <summary>
        /// Gezogene Pfade herausholen, oder null. Muss noch im Ereignis geschehen: Nach dem
        /// Abwurf sind die Zugdaten nicht mehr zuverlässig zu lesen.
        /// </summary>
        private static string[]? HolePfade(DragEventArgs e)
        {
            try
            {
                return e.Data.GetData(DataFormats.FileDrop) as string[];
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// True, wenn Dateien oder Ordner gezogen werden. Der Zugriff auf die Zugdaten
        /// steckt in einem Fangblock, weil er von fremden Quellen auch scheitern kann.
        /// </summary>
        private static bool IstDateiZiehen(DragEventArgs e)
        {
            try
            {
                return e.Data.GetDataPresent(DataFormats.FileDrop);
            }
            catch
            {
                return false;
            }
        }
    }
}
