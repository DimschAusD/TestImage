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
        public string GroesseText => GroesseBytes >= 1024 * 1024
            ? $"{GroesseBytes / 1024.0 / 1024.0:0.0} MB"
            : $"{GroesseBytes / 1024.0:0} KB";
    }
}
