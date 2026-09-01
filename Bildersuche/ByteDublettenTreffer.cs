using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Ein gefundenes Byte-Duplikat: <see cref="DublettenDatei"/> liegt im
    /// Dubletten-Ordner und wird gelöscht, <see cref="ReferenzDatei"/> ist das
    /// byte-identische Gegenstück in einem Referenzordner und bleibt unangetastet.
    /// </summary>
    public partial class ByteDublettenTreffer : ObservableObject
    {
        /// <summary>Gegenstück in einem Referenzordner — wird immer behalten.</summary>
        public string ReferenzDatei { get; init; } = string.Empty;

        /// <summary>Datei im Dubletten-Ordner — Löschkandidat.</summary>
        public string DublettenDatei { get; init; } = string.Empty;

        /// <summary>Dateigrösse in Bytes (bei Byte-Gleichheit für beide identisch).</summary>
        public long GroesseBytes { get; init; }

        /// <summary>
        /// Grösse des Gegenstücks im Bestand. Weicht sie ab, war es ein reiner
        /// Namenstreffer — bei geprüftem Inhalt sind beide Werte zwangsläufig gleich.
        /// </summary>
        public long ReferenzGroesseBytes { get; init; }

        /// <summary>
        /// True, wenn allein der Dateiname übereingestimmt hat und der Inhalt
        /// <b>nicht</b> gelesen wurde (Tiefenprüfung abgeschaltet). Der Eintrag ist
        /// löschbar wie jeder andere, aber er behauptet nichts über den Inhalt.
        /// </summary>
        public bool IstNurNamensTreffer { get; init; }

        /// <summary>
        /// True, wenn die beiden Dateien verschieden gross sind. Nur bei Namenstreffern
        /// möglich und dort das deutlichste Zeichen, dass es eben doch nicht dieselbe
        /// Datei ist — etwa eine andere Auflösung oder ein anderer Bearbeitungsstand.
        /// </summary>
        public bool HatAbweichendeGroesse
            => IstNurNamensTreffer && ReferenzGroesseBytes != GroesseBytes;

        /// <summary>
        /// Kurzer Hinweis für die Trefferliste, wie sicher der Treffer ist.
        /// Leer, wenn der Inhalt geprüft wurde — dann gibt es nichts anzumerken.
        /// </summary>
        public string PruefungHinweis => !IstNurNamensTreffer
            ? string.Empty
            : HatAbweichendeGroesse
                ? $"nur Name — Bestand {LesbareGroesse(ReferenzGroesseBytes)}"
                : "nur Name";

        /// <summary>
        /// Tooltip zum Hinweis — nennt den Grund, nicht nur den Zustand.
        ///
        /// Erste Bedingung ist kein Beiwerk: Einträge aus der reinen Ordner-Auflistung
        /// haben kein Gegenstück und wurden mit nichts verglichen. Ohne diesen Zweig
        /// behauptete der Text bei ihnen „Inhalt geprüft".
        /// </summary>
        public string PruefungTooltip => !IstBestaetigt
            ? "Nur aufgelistet — mit dem Referenzbestand noch nicht verglichen."
            : !IstNurNamensTreffer
            ? "Inhalt geprüft — die Dateien sind identisch."
            : HatAbweichendeGroesse
                ? "Nur der Dateiname stimmt überein, der Inhalt wurde nicht gelesen.\n"
                  + $"Die Datei im Bestand ist {LesbareGroesse(ReferenzGroesseBytes)} gross, "
                  + $"diese hier {LesbareGroesse(GroesseBytes)} — es ist also mit Sicherheit "
                  + "nicht dieselbe Datei.\n"
                  + "Vor dem Löschen ansehen oder die Suche mit Tiefenprüfung wiederholen."
                : "Nur der Dateiname stimmt überein, der Inhalt wurde nicht gelesen.\n"
                  + "Die Grössen passen zusammen, was für dieselbe Datei spricht — "
                  + "beweisen kann das nur die Tiefenprüfung.";

        /// <summary>Zum Löschen vorgemerkt. Standard: ja, denn genau dafür wurde gesucht.</summary>
        [ObservableProperty]
        public partial bool IstMarkiert { get; set; } = true;

        /// <summary>
        /// True, sobald ein byte-identisches Gegenstück im Referenzbestand gefunden wurde.
        /// Einträge ohne Referenz stammen aus der reinen Ordner-Auflistung nach dem Drop:
        /// Sie sind sichtbar, aber weder markierbar noch löschbar — geprüft wurde nichts.
        /// </summary>
        public bool IstBestaetigt => !string.IsNullOrEmpty(ReferenzDatei);

        /// <summary>True, sobald die Dublette gelöscht (in den Papierkorb verschoben) wurde.</summary>
        [ObservableProperty]
        public partial bool IstGeloescht { get; set; }

        public string ReferenzDateiName => Path.GetFileName(ReferenzDatei);

        public string DublettenDateiName => Path.GetFileName(DublettenDatei);

        public string DublettenOrdner => Path.GetDirectoryName(DublettenDatei) ?? string.Empty;

        public string ReferenzOrdner => Path.GetDirectoryName(ReferenzDatei) ?? string.Empty;

        /// <summary>Grösse lesbar aufbereitet (KB/MB).</summary>
        public string GroesseText => LesbareGroesse(GroesseBytes);

        private static string LesbareGroesse(long bytes) => bytes >= 1024 * 1024
            ? $"{bytes / 1024.0 / 1024.0:0.0} MB"
            : $"{bytes / 1024.0:0} KB";
    }
}
