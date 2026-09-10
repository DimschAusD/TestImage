using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Merkt sich, welche Ordner einen CLIP-Index besitzen.
    ///
    /// Bisher kannte jede Suche genau einen Ordner und lud dessen Indexdatei. Für eine
    /// Suche über mehrere Ordner braucht es eine Liste — die gab es nirgends; die
    /// Anzeige „indexiert 1/1 Ordner" war fest verdrahtet.
    ///
    /// Die Liste liegt neben der Anwendung, nicht bei den Bildern: Sie beschreibt, was
    /// dieser Rechner kennt, und soll beim Verschieben von Bilderordnern nicht
    /// mitwandern. Aufgebaut wie die Wasserzeichen-Muster.
    /// </summary>
    internal static class IndexOrdnerVerzeichnis
    {
        internal const string DateiName = "index.ordner.json";

        private static List<IndexOrdnerEintrag>? _eintraege;

        internal static string Pfad =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DateiName);

        /// <summary>
        /// Alle bekannten Ordner, neueste zuerst. <see cref="IndexOrdnerEintrag.Existiert"/>
        /// ist dabei frisch geprüft.
        /// </summary>
        internal static IReadOnlyList<IndexOrdnerEintrag> Alle() => Alle(pruefen: true);

        /// <summary>
        /// Wie oben, aber mit der Wahl, ob das Dateisystem befragt wird.
        /// </summary>
        /// <param name="pruefen">
        /// False = <see cref="IndexOrdnerEintrag.Existiert"/> so lassen, wie es die letzte
        /// Prüfung hinterlassen hat.
        ///
        /// Die Prüfung kostet je Eintrag zwei Gänge ans Dateisystem, und einer davon kann
        /// lange dauern: Ein Ordner auf einer schlafenden USB-Platte weckt sie, ein
        /// getrennter Netzpfad läuft in einen Zeitablauf von Sekunden. Steht das im
        /// UI-Faden, steht die Anwendung.
        ///
        /// Für Beschriftungen und Anzahlen genügt der letzte bekannte Stand — die
        /// Einträge sind dieselben Objekte wie im Zwischenspeicher, tragen also das
        /// Ergebnis der letzten Auffrischung. Wo es wirklich darauf ankommt, wird ohnehin
        /// erneut geprüft: <c>SucheNachSerieInOrdnernAsync</c> übergeht jeden Ordner ohne
        /// Indexdatei, und zwar in einem Hintergrundfaden.
        /// </param>
        internal static IReadOnlyList<IndexOrdnerEintrag> Alle(bool pruefen)
        {
            var liste = Hole();

            if (pruefen)
            {
                foreach (var e in liste)
                {
                    e.Existiert = IstVorhanden(e.Pfad);
                }
            }

            return liste.OrderByDescending(e => e.Stand).ToList();
        }

        /// <summary>Anzahl der Ordner, die tatsächlich noch da sind.</summary>
        internal static int AnzahlVorhanden() => Alle().Count(e => e.Existiert);

        /// <summary>Bilder über alle vorhandenen Ordner.</summary>
        internal static int BilderGesamt() => Alle().Where(e => e.Existiert).Sum(e => e.Bilder);

        /// <summary>
        /// Nimmt einen Ordner auf oder frischt ihn auf. Wird nach dem Indexieren gerufen —
        /// und immer dann, wenn irgendwo eine vorhandene Indexdatei auffällt, damit auch
        /// vor dieser Änderung indexierte Ordner nachgetragen werden.
        /// </summary>
        internal static void Merke(string? ordner, int bilder)
        {
            if (string.IsNullOrWhiteSpace(ordner) || !IstVorhanden(ordner))
            {
                return;
            }

            var liste = Hole();
            var vorhanden = liste.FirstOrDefault(
                e => string.Equals(e.Pfad, ordner, StringComparison.OrdinalIgnoreCase));

            if (vorhanden is null)
            {
                liste.Add(new IndexOrdnerEintrag
                {
                    Pfad = ordner,
                    Bilder = bilder,
                    Stand = DateTime.Now
                });
            }
            else
            {
                // Bilderzahl 0 heisst „nur nachgetragen, nicht frisch gezählt" –
                // dann den bekannten Wert behalten statt ihn zu überschreiben.
                if (bilder > 0)
                {
                    vorhanden.Bilder = bilder;
                    vorhanden.Stand = DateTime.Now;
                }
            }

            Speichere(liste);
        }

        /// <summary>Nimmt einen Ordner aus der Liste. Die Indexdatei selbst bleibt liegen.</summary>
        internal static bool Entferne(string? ordner)
        {
            if (string.IsNullOrWhiteSpace(ordner))
            {
                return false;
            }

            var liste = Hole();
            if (liste.RemoveAll(e => string.Equals(e.Pfad, ordner, StringComparison.OrdinalIgnoreCase)) == 0)
            {
                return false;
            }

            Speichere(liste);
            return true;
        }

        /// <summary>Ordner und Indexdatei noch vorhanden?</summary>
        private static bool IstVorhanden(string ordner)
        {
            try
            {
                return Directory.Exists(ordner)
                    && File.Exists(Path.Combine(ordner, BildAnalyseService.CacheDateiName));
            }
            catch
            {
                return false;
            }
        }

        private static List<IndexOrdnerEintrag> Hole()
        {
            if (_eintraege is not null)
            {
                return _eintraege;
            }

            _eintraege = new List<IndexOrdnerEintrag>();

            try
            {
                if (!File.Exists(Pfad))
                {
                    return _eintraege;
                }

                using var fs = File.OpenRead(Pfad);
                var daten = JsonSerializer.Deserialize<List<IndexOrdnerEintrag>>(fs);

                if (daten is not null)
                {
                    _eintraege.AddRange(daten.Where(e => !string.IsNullOrWhiteSpace(e.Pfad)));
                }
            }
            catch
            {
                // beschädigte Datei → wie „noch nichts bekannt" behandeln
            }

            return _eintraege;
        }

        private static void Speichere(List<IndexOrdnerEintrag> liste)
        {
            try
            {
                using var fs = File.Create(Pfad);
                JsonSerializer.Serialize(fs, liste);
            }
            catch
            {
                // schreibgeschützter Programmordner – dann gilt es nur für diese Sitzung
            }
        }
    }
}
