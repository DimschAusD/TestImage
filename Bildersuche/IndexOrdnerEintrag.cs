using System;
using System.IO;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Ein Ordner, der einen CLIP-Index besitzt. Grundlage für die geplante Suche über
    /// mehrere Ordner: Ohne dieses Verzeichnis weiss die Anwendung nicht, worüber sie
    /// überhaupt suchen könnte.
    ///
    /// Eigener Typ statt einer Klasse im ViewModel, damit XAML ihn für Entwurfsdaten
    /// benennen kann.
    /// </summary>
    public sealed class IndexOrdnerEintrag
    {
        /// <summary>Vollständiger Pfad des Ordners.</summary>
        public string Pfad { get; set; } = string.Empty;

        /// <summary>Bilder im Index, Stand der letzten Indexierung.</summary>
        public int Bilder { get; set; }

        /// <summary>Wann zuletzt indexiert wurde.</summary>
        public DateTime Stand { get; set; }

        /// <summary>
        /// False, wenn der Ordner oder seine Indexdatei nicht mehr da ist. Wird beim
        /// Einlesen gesetzt, nicht gespeichert — der Zustand kann sich jederzeit ändern.
        /// </summary>
        public bool Existiert { get; set; } = true;

        /// <summary>
        /// Kurzform für die Liste: die letzten beiden Pfadteile. Der vollständige Pfad
        /// steht im Tooltip — bei tief verschachtelten Ordnern wäre er sonst unlesbar.
        /// </summary>
        public string Anzeigename
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Pfad))
                {
                    return string.Empty;
                }

                try
                {
                    string name = Path.GetFileName(Pfad.TrimEnd(Path.DirectorySeparatorChar));
                    string? oben = Path.GetDirectoryName(Pfad.TrimEnd(Path.DirectorySeparatorChar));
                    string obenName = string.IsNullOrEmpty(oben)
                        ? string.Empty
                        : Path.GetFileName(oben.TrimEnd(Path.DirectorySeparatorChar));

                    if (string.IsNullOrEmpty(name))
                    {
                        return Pfad;
                    }

                    return string.IsNullOrEmpty(obenName) ? name : obenName + " \\ " + name;
                }
                catch
                {
                    return Pfad;
                }
            }
        }

        /// <summary>Rechte Spalte der Zeile.</summary>
        public string Beschreibung => Existiert
            ? $"{Bilder} Bilder · {Stand:dd.MM.yy}"
            : "fehlt";

        public string Tooltip => Existiert
            ? $"{Pfad}\n{Bilder} Bilder · indexiert am {Stand:dd.MM.yyyy HH:mm}"
            : $"{Pfad}\nOrdner oder Indexdatei nicht mehr vorhanden.\n"
              + "Wird bei einer Suche über mehrere Ordner übersprungen – mit ✕ entfernen.";
    }
}
