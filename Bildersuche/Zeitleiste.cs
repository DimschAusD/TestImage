using System;
using System.Collections.Generic;
using System.Linq;

namespace TestImage.Bildersuche
{
    /// <summary>Ein Feld der Zeitleiste – ein Monat oder ein Jahr.</summary>
    public sealed class ZeitAbschnitt
    {
        /// <summary>„Jan" bzw. „2019".</summary>
        public string Beschriftung { get; set; } = string.Empty;

        public int Anzahl { get; set; }

        /// <summary>Anteil am stärksten besetzten Feld, 0 … 1.</summary>
        public double Anteil { get; set; }

        /// <summary>
        /// Deckkraft der Hintergrundfläche. Aus <see cref="Anteil"/> abgeleitet, aber
        /// gestaucht: Volle Deckkraft würde die Beschriftung darauf unlesbar machen.
        /// Als fertiger Wert, damit die Oberfläche keinen Konverter braucht.
        /// </summary>
        public double Deckkraft { get; set; }

        public string Tooltip { get; set; } = string.Empty;
    }

    /// <summary>
    /// Baut die Übersichtsleiste unter dem Bild: wie viele Bilder auf welchen Zeitraum
    /// entfallen.
    ///
    /// Die Einheit wechselt automatisch. Liegt alles in einem Jahr, werden die zwölf
    /// Monate gezeigt. Sobald mehrere Jahre vorkommen, wird auf Jahre umgestellt – eine
    /// Monatsleiste würde dann Januar 2019, 2020 und 2021 in eine Zahl werfen, und die
    /// sagt nichts mehr aus.
    /// </summary>
    internal static class Zeitleiste
    {
        /// <summary>Fest verdrahtet statt über die Kultur – damit die Beschriftung überall gleich ausfällt.</summary>
        private static readonly string[] Monatsnamen =
            { "Jan", "Feb", "Mär", "Apr", "Mai", "Jun", "Jul", "Aug", "Sep", "Okt", "Nov", "Dez" };

        /// <summary>
        /// Ab so vielen Jahresspalten werden leere Jahre weggelassen. Bei einer Sammlung
        /// von 2003 bis heute wären die Felder sonst fingerbreit und grösstenteils leer.
        /// </summary>
        private const int MaxJahresSpalten = 20;

        /// <summary>Grundhelligkeit, damit auch leere Felder als Feld erkennbar bleiben.</summary>
        private const double DeckkraftBasis = 0.06;

        /// <summary>Zuschlag für das am stärksten besetzte Feld.</summary>
        private const double DeckkraftSpanne = 0.44;

        internal static (List<ZeitAbschnitt> Abschnitte, bool NachJahren) Erstelle(IReadOnlyList<DateTime> daten)
        {
            var leer = new List<ZeitAbschnitt>();
            if (daten is null || daten.Count == 0)
                return (leer, false);

            var jahre = daten.Select(d => d.Year).Distinct().OrderBy(j => j).ToList();
            bool nachJahren = jahre.Count > 1;

            List<ZeitAbschnitt> abschnitte = nachJahren
                ? BaueJahre(daten, jahre)
                : BaueMonate(daten);

            SetzeDeckkraft(abschnitte);
            return (abschnitte, nachJahren);
        }

        private static List<ZeitAbschnitt> BaueMonate(IReadOnlyList<DateTime> daten)
        {
            var zaehler = new int[12];
            foreach (var d in daten)
                zaehler[d.Month - 1]++;

            int jahr = daten[0].Year;

            var liste = new List<ZeitAbschnitt>(12);
            for (int m = 0; m < 12; m++)
            {
                liste.Add(new ZeitAbschnitt
                {
                    Beschriftung = Monatsnamen[m],
                    Anzahl = zaehler[m],
                    Tooltip = $"{Monatsnamen[m]} {jahr}: {Bilder(zaehler[m])}"
                });
            }

            return liste;
        }

        private static List<ZeitAbschnitt> BaueJahre(IReadOnlyList<DateTime> daten, List<int> jahre)
        {
            var zaehler = new Dictionary<int, int>();
            foreach (var d in daten)
                zaehler[d.Year] = zaehler.TryGetValue(d.Year, out int n) ? n + 1 : 1;

            // Lückenlos von erstem bis letztem Jahr: Ein Jahr ohne Bilder ist selbst eine
            // Aussage. Wird die Reihe zu lang, bleiben nur die belegten Jahre übrig.
            int von = jahre[0], bis = jahre[^1];
            IEnumerable<int> spalten = (bis - von + 1) <= MaxJahresSpalten
                ? Enumerable.Range(von, bis - von + 1)
                : jahre;

            var liste = new List<ZeitAbschnitt>();
            foreach (int j in spalten)
            {
                zaehler.TryGetValue(j, out int anzahl);
                liste.Add(new ZeitAbschnitt
                {
                    Beschriftung = j.ToString(),
                    Anzahl = anzahl,
                    Tooltip = $"{j}: {Bilder(anzahl)}"
                });
            }

            return liste;
        }

        private static void SetzeDeckkraft(List<ZeitAbschnitt> abschnitte)
        {
            int hoechste = abschnitte.Count == 0 ? 0 : abschnitte.Max(a => a.Anzahl);
            if (hoechste <= 0)
                return;

            foreach (var a in abschnitte)
            {
                a.Anteil = (double)a.Anzahl / hoechste;
                a.Deckkraft = a.Anzahl == 0 ? 0.0 : DeckkraftBasis + DeckkraftSpanne * a.Anteil;
            }
        }

        private static string Bilder(int anzahl) => anzahl == 1 ? "1 Bild" : $"{anzahl} Bilder";
    }
}
