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
    /// Ein einzelner Monat der feinen Leiste. Viele davon nebeneinander ergeben das
    /// Band, in dem die Jahre als Abschnitte erkennbar sind.
    /// </summary>
    public sealed class ZeitBalken
    {
        public int Jahr { get; set; }
        public int Monat { get; set; }
        public int Anzahl { get; set; }

        /// <summary>Höhe des Balkens in Punkten – vorberechnet, damit die Oberfläche keinen Konverter braucht.</summary>
        public double Hoehe { get; set; }

        /// <summary>Erster Monat eines Jahres: bekommt Trennstrich und Jahreszahl.</summary>
        public bool IstJahresAnfang { get; set; }

        /// <summary>Jahreszahl, nur am Jahresanfang gefüllt.</summary>
        public string JahrText { get; set; } = string.Empty;

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
            {
                return (leer, false);
            }

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
            {
                zaehler[d.Month - 1]++;
            }

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
            {
                zaehler[d.Year] = zaehler.TryGetValue(d.Year, out int n) ? n + 1 : 1;
            }

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
            {
                return;
            }

            foreach (var a in abschnitte)
            {
                a.Anteil = (double)a.Anzahl / hoechste;
                a.Deckkraft = a.Anzahl == 0 ? 0.0 : DeckkraftBasis + DeckkraftSpanne * a.Anteil;
            }
        }

        private static string Bilder(int anzahl) => anzahl == 1 ? "1 Bild" : $"{anzahl} Bilder";

        /// <summary>Höhe des Bandes in Punkten.</summary>
        internal const double BalkenHoehe = 20;

        /// <summary>Grundhöhe, damit auch leere Monate als Stelle im Band sichtbar bleiben.</summary>
        private const double BalkenSockel = 2;

        /// <summary>
        /// Ab so vielen Monaten wird das Band nicht mehr gezeigt. Bei 20 Jahren sind die
        /// Balken schon unter einem halben Punkt breit — darunter ist es kein Bild mehr,
        /// sondern Rauschen.
        /// </summary>
        private const int MaxMonate = 240;

        /// <summary>
        /// Feine Leiste: ein Balken je Monat über den gesamten Zeitraum, mit den Jahren
        /// als erkennbaren Abschnitten.
        ///
        /// Ergänzt die grobe Leiste, statt sie zu ersetzen: Dort sieht man auf einen Blick
        /// die Verteilung über die Jahre, hier wo innerhalb eines Jahres die Bilder liegen.
        /// </summary>
        internal static List<ZeitBalken> ErstelleMonatsBand(IReadOnlyList<DateTime> daten)
        {
            var liste = new List<ZeitBalken>();
            if (daten is null || daten.Count == 0)
            {
                return liste;
            }

            var erste = daten.Min();
            var letzte = daten.Max();

            int monate = (letzte.Year - erste.Year) * 12 + (letzte.Month - erste.Month) + 1;
            if (monate <= 1 || monate > MaxMonate)
            {
                return liste;
            }

            // Zählen je Jahr und Monat.
            var zaehler = new Dictionary<(int Jahr, int Monat), int>();
            foreach (var d in daten)
            {
                var schluessel = (d.Year, d.Month);
                zaehler[schluessel] = zaehler.TryGetValue(schluessel, out int n) ? n + 1 : 1;
            }

            int hoechste = zaehler.Values.Max();

            var lauf = new DateTime(erste.Year, erste.Month, 1);
            for (int i = 0; i < monate; i++)
            {
                zaehler.TryGetValue((lauf.Year, lauf.Month), out int anzahl);

                // Logarithmisch statt linear.
                //
                // Bei heruntergeladenen Sammlungen liegt der grösste Teil der Dateien auf
                // wenigen Tagen — ein Monat mit 900 Bildern neben Monaten mit drei. Linear
                // skaliert bliebe von allem ausser dem einen Ausschlag nur der Sockel
                // übrig, und das Band wäre leer. Der Logarithmus staucht den Ausreisser
                // und lässt die kleinen Monate sichtbar werden.
                double anteil = hoechste > 0
                    ? Math.Log(1 + anzahl) / Math.Log(1 + hoechste)
                    : 0;

                liste.Add(new ZeitBalken
                {
                    Jahr = lauf.Year,
                    Monat = lauf.Month,
                    Anzahl = anzahl,
                    Hoehe = anzahl == 0 ? BalkenSockel : BalkenSockel + (BalkenHoehe - BalkenSockel) * anteil,
                    IstJahresAnfang = lauf.Month == 1 || i == 0,
                    JahrText = (lauf.Month == 1 || i == 0) ? lauf.Year.ToString() : string.Empty,
                    Tooltip = $"{Monatsnamen[lauf.Month - 1]} {lauf.Year}: {Bilder(anzahl)}"
                });

                lauf = lauf.AddMonths(1);
            }

            return liste;
        }
    }
}
