using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestImage.Bildersuche;

namespace TestImage
{
    /// <summary>
    /// Übersichtsleiste unter dem Bild: Bilder je Zeitraum, mit automatischem Wechsel
    /// zwischen Monats- und Jahreseinteilung.
    ///
    /// <b>Zur Datenquelle:</b> Verwendet wird das Änderungsdatum der Datei, nicht das
    /// EXIF-Aufnahmedatum. Grund: Das Material sind heruntergeladene Illustrationen, und
    /// die tragen so gut wie nie ein Aufnahmedatum – die Leiste bliebe für fast alle
    /// Bilder leer. Das Dateidatum beantwortet ausserdem die Frage, um die es hier
    /// tatsächlich geht: wann das Bild in die Sammlung kam. Es kommt zudem praktisch
    /// umsonst, während ein EXIF-Lesen jede Datei öffnen müsste – bei tausenden Bildern
    /// auf einer Festplatte spürbar.
    /// </summary>
    public partial class AufgabeViewModel
    {
        #region Zustand

        public ObservableCollection<ZeitAbschnitt> Zeitleiste { get; } = new();

        /// <summary>
        /// Feines Band: ein Balken je Monat über den ganzen Zeitraum, Jahre als
        /// Abschnitte. Ergänzt die grobe Leiste um die Verteilung innerhalb der Jahre.
        /// </summary>
        public ObservableCollection<ZeitBalken> ZeitBand { get; } = new();

        /// <summary>
        /// True, wenn ein feines Band überhaupt gebaut werden konnte.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteZeitleisteUmschaltenCommand))]
        private bool _zeitBandVorhanden;

        /// <summary>
        /// Welche der beiden Übersichten gezeigt wird: true = feines Monatsband,
        /// false = grobe Leiste.
        ///
        /// Wird beim Aufbau selbsttätig gesetzt — mehrere Jahre sprechen für das Band,
        /// ein einzelnes für die Monatsleiste. Von Hand umschaltbar bleibt es trotzdem;
        /// die Wahl gilt dann bis zum nächsten Ordnerwechsel.
        /// </summary>
        [ObservableProperty]
        private bool _zeitBandBevorzugt;

        private bool CanExecuteZeitleisteUmschalten() => ZeitBandVorhanden;

        /// <summary>Wechselt zwischen grober Leiste und feinem Monatsband.</summary>
        [RelayCommand(CanExecute = nameof(CanExecuteZeitleisteUmschalten))]
        private void CommandExecuteZeitleisteUmschalten()
        {
            ZeitBandBevorzugt = !ZeitBandBevorzugt;
            SetzeZeitleisteEinheit();
        }

        /// <summary>Beschriftung links der Leiste – nennt, was gerade gezeigt wird.</summary>
        private void SetzeZeitleisteEinheit()
            => ZeitleisteEinheit = ZeitBandBevorzugt
                ? "Monatsband"
                : (ZeitleisteNachJahren ? "Jahre" : "Monate");

        /// <summary>Zusammenfassung unter dem Band, z. B. „1.430 Bilder · 2011 bis 2015".</summary>
        [ObservableProperty]
        private string _zeitraumText = string.Empty;

        /// <summary>True, wenn nach Jahren eingeteilt ist – sonst nach Monaten.</summary>
        [ObservableProperty]
        private bool _zeitleisteNachJahren;

        /// <summary>Kurze Angabe der Einheit für die Beschriftung links der Leiste.</summary>
        [ObservableProperty]
        private string _zeitleisteEinheit = "Monate";

        /// <summary>Steuert, ob die Leiste überhaupt angezeigt wird.</summary>
        [ObservableProperty]
        private bool _zeitleisteVorhanden;

        #endregion

        #region Aufbau

        private CancellationTokenSource? _zeitleisteAbbruch;

        /// <summary>
        /// Baut die Leiste für die aktuelle Bildliste neu auf. Wird nach dem Laden eines
        /// Ordners gerufen.
        /// </summary>
        private void AktualisiereZeitleiste()
        {
            _zeitleisteAbbruch?.Cancel();

            var abbruch = new CancellationTokenSource();
            _zeitleisteAbbruch = abbruch;

            // Momentaufnahme der Pfade: Die Sammlung darf sich danach ändern, der
            // Hintergrund-Task arbeitet auf einer eigenen Kopie.
            var pfade = new List<string>(OcAufgabens.Count);
            foreach (var bild in OcAufgabens)
            {
                if (!string.IsNullOrWhiteSpace(bild.BName))
                {
                    pfade.Add(bild.BName);
                }
            }

            _ = BaueZeitleisteAsync(pfade, abbruch);
        }

        private async Task BaueZeitleisteAsync(List<string> pfade, CancellationTokenSource abbruch)
        {
            var token = abbruch.Token;

            try
            {
                // Rückgabetyp ausgeschrieben: Sonst verliert die Ableitung die Namen der
                // Tupelfelder, weil der vorzeitige Abbruch unten ein namenloses Tupel liefert.
                var ergebnis = await Task.Run<(List<ZeitAbschnitt> Abschnitte, bool NachJahren, List<ZeitBalken> Band, string Zeitraum)>(() =>
                {
                    var daten = new List<DateTime>(pfade.Count);

                    foreach (string pfad in pfade)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return (new List<ZeitAbschnitt>(), false, new List<ZeitBalken>(), string.Empty);
                        }

                        try
                        {
                            var zeit = File.GetLastWriteTime(pfad);

                            // 1601 ist der Wert, den Windows für „unbekannt" liefert.
                            if (zeit.Year > 1601)
                            {
                                daten.Add(zeit);
                            }
                        }
                        catch
                        {
                            // Nicht lesbare Datei zählt einfach nicht mit.
                        }
                    }

                    var grob = Bildersuche.Zeitleiste.Erstelle(daten);
                    var band = Bildersuche.Zeitleiste.ErstelleMonatsBand(daten);

                    string zeitraum = daten.Count == 0
                        ? string.Empty
                        : $"{daten.Count:N0} Bilder · {daten.Min():MM.yyyy} bis {daten.Max():MM.yyyy}";

                    return (grob.Abschnitte, grob.NachJahren, band, zeitraum);
                }).ConfigureAwait(true);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                Zeitleiste.Clear();
                foreach (var abschnitt in ergebnis.Abschnitte)
                {
                    Zeitleiste.Add(abschnitt);
                }

                ZeitBand.Clear();
                foreach (var balken in ergebnis.Band)
                {
                    ZeitBand.Add(balken);
                }

                ZeitBandVorhanden = ZeitBand.Count > 0;
                ZeitraumText = ergebnis.Zeitraum;

                ZeitleisteNachJahren = ergebnis.NachJahren;
                ZeitleisteVorhanden = Zeitleiste.Count > 0;

                // Selbsttätige Wahl: Über mehrere Jahre sagt das feine Band mehr — die
                // grobe Leiste zeigt dort nur Jahressummen. Bei einem einzelnen Jahr
                // wäre das Band nur eine dünnere Fassung derselben zwölf Monate.
                ZeitBandBevorzugt = ZeitBandVorhanden && ergebnis.NachJahren;
                SetzeZeitleisteEinheit();
            }
            catch
            {
                Zeitleiste.Clear();
                ZeitBand.Clear();
                ZeitleisteVorhanden = false;
                ZeitBandVorhanden = false;
                ZeitBandBevorzugt = false;
                ZeitraumText = string.Empty;
            }
            finally
            {
                if (ReferenceEquals(_zeitleisteAbbruch, abbruch))
                {
                    _zeitleisteAbbruch = null;
                }

                abbruch.Dispose();
            }
        }

        #endregion
    }
}
