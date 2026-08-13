using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
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
                    pfade.Add(bild.BName);
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
                var ergebnis = await Task.Run<(List<ZeitAbschnitt> Abschnitte, bool NachJahren)>(() =>
                {
                    var daten = new List<DateTime>(pfade.Count);

                    foreach (string pfad in pfade)
                    {
                        if (token.IsCancellationRequested)
                            return (new List<ZeitAbschnitt>(), false);

                        try
                        {
                            var zeit = File.GetLastWriteTime(pfad);

                            // 1601 ist der Wert, den Windows für „unbekannt" liefert.
                            if (zeit.Year > 1601)
                                daten.Add(zeit);
                        }
                        catch
                        {
                            // Nicht lesbare Datei zählt einfach nicht mit.
                        }
                    }

                    return Bildersuche.Zeitleiste.Erstelle(daten);
                }).ConfigureAwait(true);

                if (token.IsCancellationRequested)
                    return;

                Zeitleiste.Clear();
                foreach (var abschnitt in ergebnis.Abschnitte)
                    Zeitleiste.Add(abschnitt);

                ZeitleisteNachJahren = ergebnis.NachJahren;
                ZeitleisteEinheit = ergebnis.NachJahren ? "Jahre" : "Monate";
                ZeitleisteVorhanden = Zeitleiste.Count > 0;
            }
            catch
            {
                Zeitleiste.Clear();
                ZeitleisteVorhanden = false;
            }
            finally
            {
                if (ReferenceEquals(_zeitleisteAbbruch, abbruch))
                    _zeitleisteAbbruch = null;

                abbruch.Dispose();
            }
        }

        #endregion
    }
}
