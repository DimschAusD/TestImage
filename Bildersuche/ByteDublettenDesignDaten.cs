using System.Collections.ObjectModel;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Beispieldaten für den XAML-Designer (nur über d: eingebunden, zur Laufzeit
    /// ungenutzt). Deckt bewusst alle Zustände einer Trefferzeile ab, damit sich
    /// Darstellung und Trigger im Designer prüfen lassen, ohne die App zu starten:
    /// bestätigt+markiert, bestätigt+abgewählt, ungeprüft (aus der Ordner-Auflistung),
    /// bereits im Papierkorb sowie die beiden Namenstreffer ohne Inhaltsprüfung —
    /// einmal mit passender, einmal mit abweichender Grösse.
    /// </summary>
    public sealed class ByteDublettenDesignDaten : ObservableCollection<ByteDublettenTreffer>
    {
        public ByteDublettenDesignDaten()
        {
            // Bestätigtes Duplikat, zum Löschen vorgemerkt.
            Add(new ByteDublettenTreffer
            {
                DublettenDatei = @"C:\Downloads\Neu\Urlaub_2024_0815.jpg",
                ReferenzDatei = @"C:\Bilder\Sammlung\Urlaub_2024_0815.jpg",
                GroesseBytes = 3_486_725
            });

            // Bestätigt, aber vom Nutzer abgewählt.
            Add(new ByteDublettenTreffer
            {
                DublettenDatei = @"C:\Downloads\Neu\Rechnung_Januar.pdf",
                ReferenzDatei = @"C:\Dokumente\Archiv\Rechnung_Januar.pdf",
                GroesseBytes = 214_880,
                IstMarkiert = false
            });

            // Nur aufgelistet, noch nicht verglichen: Haken gesperrt, kein Gegenstück.
            Add(new ByteDublettenTreffer
            {
                DublettenDatei = @"C:\Downloads\Neu\Sicherung_alt.zip",
                ReferenzDatei = string.Empty,
                GroesseBytes = 1_073_741_824,
                IstMarkiert = false
            });

            // Namenstreffer ohne Inhaltsprüfung, Grössen passen zusammen.
            Add(new ByteDublettenTreffer
            {
                DublettenDatei = @"C:\Downloads\Neu\Protokoll_Mai.docx",
                ReferenzDatei = @"C:\Dokumente\Archiv\Protokoll_Mai.docx",
                GroesseBytes = 48_112,
                ReferenzGroesseBytes = 48_112,
                IstNurNamensTreffer = true
            });

            // Namenstreffer mit abweichender Grösse: gleicher Name, sicher nicht
            // dieselbe Datei — hier muss die Warnung ins Auge fallen.
            Add(new ByteDublettenTreffer
            {
                DublettenDatei = @"C:\Downloads\Neu\Titelbild.jpg",
                ReferenzDatei = @"C:\Bilder\Sammlung\Titelbild.jpg",
                GroesseBytes = 2_517_632,
                ReferenzGroesseBytes = 384_204,
                IstNurNamensTreffer = true
            });

            // Schon im Papierkorb: ausgegraut und durchgestrichen.
            Add(new ByteDublettenTreffer
            {
                DublettenDatei = @"C:\Downloads\Neu\Katze_Kopie.png",
                ReferenzDatei = @"C:\Bilder\Sammlung\Katze.png",
                GroesseBytes = 892_311,
                IstMarkiert = false,
                IstGeloescht = true
            });
        }
    }
}
