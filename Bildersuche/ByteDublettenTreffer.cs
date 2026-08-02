using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Ein gefundenes Byte-Duplikat: <see cref="BasisDatei"/> wird behalten,
    /// <see cref="DublettenDatei"/> liegt in einem der Vergleichsordner und
    /// kann gelöscht werden.
    /// </summary>
    public partial class ByteDublettenTreffer : ObservableObject
    {
        /// <summary>Datei im Basisordner — wird immer behalten.</summary>
        public string BasisDatei { get; init; } = string.Empty;

        /// <summary>Byte-identische Datei in einem Vergleichsordner — Löschkandidat.</summary>
        public string DublettenDatei { get; init; } = string.Empty;

        /// <summary>Dateigrösse in Bytes (bei Byte-Gleichheit für beide identisch).</summary>
        public long GroesseBytes { get; init; }

        /// <summary>Zum Löschen vorgemerkt. Standard: ja, denn genau dafür wurde gesucht.</summary>
        [ObservableProperty]
        public partial bool IstMarkiert { get; set; } = true;

        /// <summary>True, sobald die Dublette gelöscht (in den Papierkorb verschoben) wurde.</summary>
        [ObservableProperty]
        public partial bool IstGeloescht { get; set; }

        public string BasisDateiName => Path.GetFileName(BasisDatei);

        public string DublettenDateiName => Path.GetFileName(DublettenDatei);

        public string DublettenOrdner => Path.GetDirectoryName(DublettenDatei) ?? string.Empty;

        public string BasisOrdner => Path.GetDirectoryName(BasisDatei) ?? string.Empty;

        /// <summary>Grösse lesbar aufbereitet (KB/MB).</summary>
        public string GroesseText => GroesseBytes >= 1024 * 1024
            ? $"{GroesseBytes / 1024.0 / 1024.0:0.0} MB"
            : $"{GroesseBytes / 1024.0:0} KB";
    }
}
