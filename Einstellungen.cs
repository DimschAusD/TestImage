using System;
using System.IO;
using System.Text.Json;

namespace TestImage
{
    /// <summary>
    /// Die wenigen Schalter, die eine Sitzung überdauern sollen.
    ///
    /// Aufgebaut wie <see cref="Bildersuche.IndexOrdnerVerzeichnis"/>: eine kleine
    /// JSON-Datei neben der Anwendung, im Speicher gehalten und beim Ändern
    /// geschrieben. Sie beschreibt, wie dieser Rechner bedient wird, und hat bei den
    /// Bildern nichts verloren — beim Verschieben eines Bilderordners soll sie nicht
    /// mitwandern.
    ///
    /// Alles andere bleibt bewusst draussen: Schwellen und Filter gehören zum
    /// jeweiligen Suchlauf und sollen beim nächsten Start wieder auf ihrem Standard
    /// stehen.
    /// </summary>
    internal static class Einstellungen
    {
        internal const string DateiName = "einstellungen.json";

        private static Inhalt? _inhalt;

        internal static string Pfad =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DateiName);

        /// <summary>
        /// Was in der Datei steht. Eigene Klasse, damit die Namen der JSON-Felder von
        /// den Eigenschaften des ViewModels unabhängig bleiben.
        /// </summary>
        internal sealed class Inhalt
        {
            public bool SortierenWieExplorer { get; set; }

            public bool ErklärungEingeklappt { get; set; }
        }

        /// <summary>
        /// True = Namen sortieren wie der Windows-Explorer, False = wie der
        /// SpeedCommander. Schreiben speichert sofort; ein unveränderter Wert rührt die
        /// Datei nicht an.
        /// </summary>
        internal static bool SortierenWieExplorer
        {
            get => Hole().SortierenWieExplorer;
            set
            {
                var inhalt = Hole();
                if (inhalt.SortierenWieExplorer == value)
                {
                    return;
                }

                inhalt.SortierenWieExplorer = value;
                Speichere();
            }
        }

        /// <summary>
        /// Erklärfeld im Eigenschaften-Feld eingeklappt. Wer die Bedienung kennt, klappt
        /// es einmal zu und will es beim nächsten Start nicht wieder offen vorfinden.
        /// </summary>
        internal static bool ErklärungEingeklappt
        {
            get => Hole().ErklärungEingeklappt;
            set
            {
                var inhalt = Hole();
                if (inhalt.ErklärungEingeklappt == value)
                {
                    return;
                }

                inhalt.ErklärungEingeklappt = value;
                Speichere();
            }
        }

        private static Inhalt Hole()
        {
            if (_inhalt is not null)
            {
                return _inhalt;
            }

            _inhalt = new Inhalt();

            try
            {
                if (!File.Exists(Pfad))
                {
                    return _inhalt;
                }

                using var fs = File.OpenRead(Pfad);
                var daten = JsonSerializer.Deserialize<Inhalt>(fs);

                if (daten is not null)
                {
                    _inhalt = daten;
                }
            }
            catch
            {
                // beschädigte Datei → wie „noch nie etwas eingestellt" behandeln
            }

            return _inhalt;
        }

        private static void Speichere()
        {
            try
            {
                using var fs = File.Create(Pfad);
                JsonSerializer.Serialize(fs, _inhalt);
            }
            catch
            {
                // schreibgeschützter Programmordner – dann gilt es nur für diese Sitzung
            }
        }
    }
}
