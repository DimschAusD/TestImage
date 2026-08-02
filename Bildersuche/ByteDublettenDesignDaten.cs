using System.Collections.ObjectModel;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Beispieldaten für den XAML-Designer (nur über d: eingebunden, zur Laufzeit
    /// ungenutzt). Deckt bewusst alle vier Zustände einer Trefferzeile ab, damit sich
    /// Darstellung und Trigger im Designer prüfen lassen, ohne die App zu starten:
    /// bestätigt+markiert, bestätigt+abgewählt, ungeprüft (aus der Ordner-Auflistung)
    /// und bereits im Papierkorb.
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
