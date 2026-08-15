using System;
using System.IO;
using System.Windows.Threading;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Überwacht einen Ordner darauf, ob seine CLIP-Indexdatei auftaucht oder
    /// verschwindet.
    ///
    /// Anlass: Wird der Index von Hand gelöscht, merkte die Anwendung das bisher nicht —
    /// „Schema-ähnlich" blieb verfügbar und lief anschliessend ins Leere, und im
    /// Ordnerverzeichnis stand der Ordner weiter als vorhanden.
    ///
    /// Bewusst nur auf **diese eine Datei**. Ein Wächter über die Bilddateien würde auch
    /// jede Verschiebung melden, die die Anwendung selbst auslöst — bei „alle ins
    /// kein_Fav" wären das hunderte Meldungen, und sie arbeitete gegen sich selbst.
    /// </summary>
    internal sealed class IndexDateiWaechter : IDisposable
    {
        /// <summary>
        /// Sammelpause. Das Dateisystem meldet oft mehrfach für einen Vorgang —
        /// Erstellen und Ändern kurz hintereinander. Ohne diese Pause liefe die
        /// Auswertung mehrfach.
        /// </summary>
        private const int SammelPauseMs = 400;

        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly DispatcherTimer _sammler;

        private FileSystemWatcher? _waechter;
        private string? _ordner;

        /// <summary>
        /// Wird auf dem UI-Faden gerufen, wenn sich am Index etwas getan hat. Als
        /// Eigenschaft statt als Ereignis, damit mehrfaches Zuweisen unschädlich ist.
        /// </summary>
        internal Action? BeiAenderung { get; set; }

        internal IndexDateiWaechter()
        {
            _sammler = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(SammelPauseMs)
            };

            _sammler.Tick += (_, _) =>
            {
                _sammler.Stop();
                BeiAenderung?.Invoke();
            };
        }

        /// <summary>
        /// Richtet die Überwachung auf einen Ordner ein. Derselbe Ordner erneut ist
        /// wirkungslos, <c>null</c> schaltet ab.
        /// </summary>
        internal void Ueberwache(string? ordner)
        {
            if (string.Equals(_ordner, ordner, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ordner = ordner;

            _waechter?.Dispose();
            _waechter = null;

            if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
            {
                return;
            }

            try
            {
                _waechter = new FileSystemWatcher(ordner, BildAnalyseService.CacheDateiName)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false
                };

                _waechter.Created += Gemeldet;
                _waechter.Deleted += Gemeldet;
                _waechter.Changed += Gemeldet;
                _waechter.Renamed += Gemeldet;

                // Puffer übergelaufen oder Datenträger weg: Zustand einfach neu bewerten.
                _waechter.Error += (_, _) => Anstossen();

                _waechter.EnableRaisingEvents = true;
            }
            catch
            {
                // Netzlaufwerke und manche Wechseldatenträger lassen keine Überwachung zu.
                // Dann bleibt es beim bisherigen Verhalten — geprüft wird ohnehin bei
                // jedem Bildwechsel.
                _waechter = null;
            }
        }

        private void Gemeldet(object sender, FileSystemEventArgs e) => Anstossen();

        /// <summary>
        /// Startet die Sammelpause neu. Läuft auf einem fremden Faden, deshalb der
        /// Umweg über den Dispatcher — DispatcherTimer darf nur dort bedient werden.
        /// </summary>
        private void Anstossen()
        {
            _dispatcher.InvokeAsync(() =>
            {
                _sammler.Stop();
                _sammler.Start();
            });
        }

        public void Dispose()
        {
            _sammler.Stop();
            _waechter?.Dispose();
            _waechter = null;
        }
    }
}
