using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using TestImage.Bildersuche;

namespace TestImage
{
    /// <summary>
    /// Konturansicht: zeigt statt des Bildes sein Kantenbild (Sobel), mit Schwellwert
    /// am Schieberegler – dasselbe Verfahren wie „Kontur A" im Grundprojekt
    /// BildKonturBerechnen.
    ///
    /// Gerechnet wird auf dem bereits geladenen <see cref="AufgabeViewModel.DisplayImage"/>,
    /// nicht auf der Datei: Das ist schon auf Anzeigegrösse dekodiert, spart den
    /// Plattenzugriff und hält die Rechnung in erträglicher Zeit.
    /// </summary>
    public partial class AufgabeViewModel
    {
        #region Zustand

        /// <summary>Kantenbild statt Originalbild anzeigen.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AnzeigeBild))]
        public partial bool ZeigeKontur { get; set; }

        /// <summary>Schwelle für die Gradientenstärke, 0 … 255.</summary>
        [ObservableProperty]
        public partial double KonturSchwelle { get; set; } = KonturBerechnung.SchwelleStandard;

        /// <summary>Vor der Kantensuche weichzeichnen – nimmt Rauschen aus dem Ergebnis.</summary>
        [ObservableProperty]
        public partial bool KonturWeichzeichnen { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AnzeigeBild))]
        public partial BitmapSource? KonturImage { get; set; }

        /// <summary>Läuft gerade eine Kantenberechnung?</summary>
        [ObservableProperty]
        public partial bool KonturLäuft { get; set; }

        /// <summary>Rechenzeit der letzten Kantenberechnung, für den ToolTip.</summary>
        [ObservableProperty]
        public partial string SWkonturBild { get; set; } = string.Empty;

        /// <summary>
        /// Was das grosse Bild tatsächlich zeigt. Eigene Eigenschaft, damit die
        /// zweistufige Ladestrecke (klein → gross) unangetastet bleibt und weiterhin
        /// nur <see cref="DisplayImage"/> setzt.
        /// </summary>
        public BitmapSource? AnzeigeBild =>
            ZeigeKontur && KonturImage is not null ? KonturImage : DisplayImage;

        #endregion

        #region Auslöser

        /// <summary>
        /// Läuft, sobald ein neues Bild fertig geladen ist. Beim ersten, kleinen
        /// Vorschaubild rechnet das mit — gewollt: So steht sofort etwas da, und das
        /// grosse Bild ersetzt es kurz darauf.
        /// </summary>
        partial void OnDisplayImageChanged(BitmapSource value)
        {
            OnPropertyChanged(nameof(AnzeigeBild));

            if (ZeigeKontur)
                PlaneKonturNeuberechnung();
            else
                KonturImage = null;   // veraltetes Kantenbild nicht aufheben
        }

        partial void OnZeigeKonturChanged(bool value)
        {
            if (value)
                PlaneKonturNeuberechnung();
            else
                BrichKonturAb();
        }

        partial void OnKonturSchwelleChanged(double value)
        {
            if (ZeigeKontur)
                PlaneKonturNeuberechnung();
        }

        partial void OnKonturWeichzeichnenChanged(bool value)
        {
            if (ZeigeKontur)
                PlaneKonturNeuberechnung();
        }

        #endregion

        #region Berechnung

        /// <summary>
        /// Wartezeit, bevor gerechnet wird. Beim Ziehen des Reglers kommen Dutzende
        /// Änderungen pro Sekunde; ohne diese Pause liefe für jede eine volle Faltung an.
        /// </summary>
        private const int KonturVerzoegerungMs = 150;

        private CancellationTokenSource? _konturAbbruch;

        private void BrichKonturAb()
        {
            _konturAbbruch?.Cancel();
            _konturAbbruch = null;
            KonturLäuft = false;
        }

        /// <summary>
        /// Stösst eine Neuberechnung an und verwirft eine noch laufende. Absichtlich
        /// kein Command: Ausgelöst wird über Eigenschaften (Regler, Schalter, Bildwechsel),
        /// nicht über einen Knopf.
        /// </summary>
        private void PlaneKonturNeuberechnung()
        {
            _konturAbbruch?.Cancel();

            var quelle = DisplayImage;
            if (quelle is null)
            {
                KonturImage = null;
                KonturLäuft = false;
                return;
            }

            var abbruch = new CancellationTokenSource();
            _konturAbbruch = abbruch;

            int schwelle = (int)Math.Round(KonturSchwelle);
            bool weich = KonturWeichzeichnen;

            _ = BerechneKonturAsync(quelle, schwelle, weich, abbruch);
        }

        private async Task BerechneKonturAsync(
            BitmapSource quelle, int schwelle, bool weichzeichnen, CancellationTokenSource abbruch)
        {
            var token = abbruch.Token;

            try
            {
                // Bewusst ohne Token: Ein Task.Delay mit Token würde bei jeder
                // Reglerbewegung eine Ausnahme werfen. Abbruch ist hier der Normalfall,
                // also wird er abgefragt statt geworfen.
                await Task.Delay(KonturVerzoegerungMs).ConfigureAwait(true);

                if (token.IsCancellationRequested)
                    return;

                KonturLäuft = true;
                var uhr = Stopwatch.StartNew();

                var kanten = await Task.Run(
                    () => KonturBerechnung.Sobel(quelle, schwelle, weichzeichnen, token))
                    .ConfigureAwait(true);

                // null heisst abgebrochen – dann hat der Nachfolger bereits übernommen.
                if (kanten is null || token.IsCancellationRequested)
                    return;

                KonturImage = kanten;
                SWkonturBild = uhr.Elapsed.TotalMilliseconds.ToString("F0") + " ms";
            }
            catch (Exception ex)
            {
                SWkonturBild = "Fehler: " + ex.Message;
                KonturImage = null;
            }
            finally
            {
                // Nur aufräumen, wenn inzwischen kein neuer Durchlauf gestartet wurde.
                if (ReferenceEquals(_konturAbbruch, abbruch))
                {
                    KonturLäuft = false;
                    _konturAbbruch = null;
                }

                abbruch.Dispose();
            }
        }

        #endregion
    }
}
