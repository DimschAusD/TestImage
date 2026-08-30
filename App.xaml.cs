using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TestImage
{
    /// <summary>
    /// Interaction logic for App.xaml
    ///
    /// Hier hängt zusätzlich das Fangnetz für Ausnahmen, die sonst niemand fängt.
    ///
    /// <b>Es ist ausdrücklich nur ein Netz.</b> Der eigentliche Schutz bleibt das
    /// <c>try/catch</c> in jedem einzelnen async-Command — dort weiss man, was
    /// schiefgehen kann, und kann etwas Sinnvolles dazu sagen. Hier landet nur, was
    /// jemand künftig zu ergänzen vergisst.
    ///
    /// Warum das nötig ist: Ein <c>AsyncRelayCommand</c> wirft eine durchgereichte
    /// Ausnahme auf dem Oberflächen-Faden erneut. Ohne Behandler endet der Vorgang
    /// dann sofort und wortlos — genau so ist die Anwendung beim Neu-Einlesen eines
    /// gelöschten Ordners gestorben.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Die drei Behandler decken drei verschiedene Orte ab und dürfen deshalb
        /// nicht dasselbe tun — siehe die Kommentare bei den jeweiligen Methoden.
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += BeiOberflaechenAusnahme;
            AppDomain.CurrentDomain.UnhandledException += BeiToedlicherAusnahme;
            TaskScheduler.UnobservedTaskException += BeiUnbeachteterAufgabe;
        }

        #region Oberflächen-Faden

        /// <summary>Beginn des laufenden Zählfensters der Wiederhol-Bremse.</summary>
        private DateTime _bremsFensterBeginn = DateTime.MinValue;

        /// <summary>Abgefangene Ausnahmen im laufenden Zählfenster.</summary>
        private int _bremsZaehler;

        /// <summary>Länge des Zählfensters.</summary>
        private static readonly TimeSpan BremsFenster = TimeSpan.FromSeconds(5);

        /// <summary>So viele Ausnahmen je Fenster werden abgefangen, danach greift die Bremse.</summary>
        private const int BremsGrenze = 5;

        /// <summary>
        /// Ausnahme auf dem Oberflächen-Faden.
        ///
        /// <b>Hier darf nichts blockieren.</b> Kein MessageBox, kein ShowDialog, kein
        /// Warten. Ein modaler Dialog an dieser Stelle pumpt eine verschachtelte
        /// Nachrichtenschleife mitten in der fehlgeschlagenen Operation und sperrt das
        /// Hauptfenster: Nachgemessen liess sich der Vorgang danach nicht mehr über
        /// „Task beenden" schliessen, sondern nur noch hart abschiessen — und dafür
        /// reichte eine einzige Ausnahme. Die Meldung geht deshalb in die Statuszeile,
        /// und zwar nachgereicht (siehe <see cref="MeldeInStatuszeile"/>).
        ///
        /// Die Bremse ist der zweite Teil: Wirft eine Stelle immer wieder — eine
        /// defekte Bindung, ein Timer, ein Layout-Durchlauf —, dann bedeutet
        /// <c>Handled = true</c>, dass sofort dieselbe Stelle erneut dran ist. Ab
        /// <see cref="BremsGrenze"/> Ausnahmen je Fenster wird deshalb nicht mehr
        /// abgefangen: Ein sauberer Absturz ist besser als ein totes Fenster.
        /// </summary>
        private void BeiOberflaechenAusnahme(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Protokolliere("Oberfläche", e.Exception);

            var jetzt = DateTime.Now;
            if (jetzt - _bremsFensterBeginn > BremsFenster)
            {
                _bremsFensterBeginn = jetzt;
                _bremsZaehler = 0;
            }

            _bremsZaehler++;

            if (_bremsZaehler > BremsGrenze)
            {
                Protokolliere("Oberfläche",
                    new Exception($"Mehr als {BremsGrenze} Ausnahmen in {BremsFenster.TotalSeconds:0} Sekunden – "
                                  + "die Anwendung wird nicht weiter am Leben gehalten."));

                e.Handled = false;
                return;
            }

            MeldeInStatuszeile(e.Exception);
            e.Handled = true;
        }

        /// <summary>
        /// Schreibt die Meldung in die Statuszeile der Anwendung — <b>nachgereicht</b>.
        ///
        /// Das <c>BeginInvoke</c> mit niedriger Rangstufe ist der Kern: So läuft das
        /// Setzen erst, wenn sich die fehlgeschlagene Operation abgewickelt hat, und
        /// nicht mitten in ihr. Direkt gesetzt könnte es in einem Layout- oder
        /// Zeichnen-Durchlauf landen und dort erneut werfen.
        /// </summary>
        private void MeldeInStatuszeile(Exception ausnahme)
        {
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    if (MainWindow?.DataContext is AufgabeViewModel vm)
                    {
                        vm.LabelDropContent =
                            $"Unerwarteter Fehler ({ausnahme.GetType().Name}): {ausnahme.Message} "
                            + $"– Näheres in {ProtokollDatei}";
                    }
                });
            }
            catch
            {
                // Der Behandler selbst darf unter keinen Umständen werfen.
            }
        }

        #endregion

        #region Die beiden anderen Orte

        /// <summary>
        /// Ausnahme ausserhalb des Oberflächen-Fadens, die niemand gefangen hat.
        ///
        /// <b>Hier ist nichts mehr zu retten:</b> Die Laufzeit beendet den Vorgang
        /// danach in jedem Fall, ein „Handled" gibt es nicht. Deshalb nur schreiben und
        /// sofort zurück — wer hier noch Dialoge öffnet oder wartet, hängt im
        /// Abbruchpfad fest, und dann lässt sich der Vorgang gar nicht mehr beenden.
        /// </summary>
        private void BeiToedlicherAusnahme(object sender, UnhandledExceptionEventArgs e)
        {
            Protokolliere("Tödlich", e.ExceptionObject as Exception);
        }

        /// <summary>
        /// Eine Aufgabe ist mit einer Ausnahme geendet, ohne dass jemand hingesehen hat
        /// (kein await, kein ContinueWith) — etwa die abgekoppelten Läufe wie
        /// <c>BaueZeitleisteAsync</c>.
        ///
        /// <b>Das läuft auf dem Finalizer-Faden.</b> Wer den blockiert, legt die
        /// Speicherbereinigung des ganzen Vorgangs still. Also ausschliesslich
        /// schreiben, keine Oberfläche, nichts Wartendes.
        /// </summary>
        private void BeiUnbeachteterAufgabe(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Protokolliere("Unbeachtete Aufgabe", e.Exception);

            // Ohne SetObserved bleibt es bei der Vorgabe der Laufzeit; das ist heute
            // zwar kein Prozessende mehr, aber die Ausnahme gilt als offen.
            e.SetObserved();
        }

        #endregion

        #region Protokoll

        private static string? _protokollDatei;

        /// <summary>
        /// Ziel des Protokolls: immer neben der Anwendung — dort sucht man zuerst und
        /// findet es ohne Umweg.
        ///
        /// Bewusst ohne Ausweichort unter den lokalen Anwendungsdaten: Die Anwendung ist
        /// portabel und soll ausserhalb ihres eigenen Ordners nichts hinterlassen. Liegt
        /// sie schreibgeschützt (Stick, Netzfreigabe), entfällt das Protokoll ersatzlos —
        /// <see cref="Protokolliere"/> schluckt den Schreibfehler.
        /// </summary>
        private static string ProtokollDatei =>
            _protokollDatei ??= Path.Combine(AppContext.BaseDirectory, "TestImage-Fehler.log");

        /// <summary>
        /// Hängt einen Eintrag an das Protokoll an. Schlägt das fehl, geschieht nichts —
        /// ein Fehler beim Melden eines Fehlers darf nicht seinerseits die Anwendung
        /// beenden.
        /// </summary>
        private static void Protokolliere(string herkunft, Exception? ausnahme)
        {
            try
            {
                var text = new System.Text.StringBuilder();
                text.AppendLine();
                text.AppendLine($"===== {DateTime.Now:dd.MM.yyyy HH:mm:ss}  [{herkunft}] =====");

                if (ausnahme is null)
                {
                    // AppDomain.UnhandledException liefert ein object, das nicht
                    // zwingend eine Exception ist — dann bleibt nur der Zeitstempel.
                    text.AppendLine("(keine Ausnahme mitgeliefert)");
                }

                for (var e = ausnahme; e is not null; e = e.InnerException)
                {
                    text.AppendLine(e.GetType().FullName + ": " + e.Message);
                    text.AppendLine(e.StackTrace);
                }

                File.AppendAllText(ProtokollDatei, text.ToString());
            }
            catch
            {
                // Absicht: siehe Beschreibung.
            }
        }

        #endregion
    }
}
