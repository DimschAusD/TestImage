using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TestImage.Bildersuche
{
    /// <summary>Der erkannte Text eines Bildes samt Merkmalen zum Erkennen von Änderungen.</summary>
    internal sealed class OcrEintrag
    {
        public string Pfad { get; set; } = string.Empty;

        /// <summary>Dateigrösse und Änderungszeit — ändert sich eines, ist der Text veraltet.</summary>
        public long Dateigroesse { get; set; }

        public long AenderungTicks { get; set; }

        /// <summary>Der erkannte Text. Leer heisst: erkannt, aber kein Text im Bild.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Sprache, in der erkannt wurde — für den Fall, dass sie später wechselt.</summary>
        public string Sprache { get; set; } = string.Empty;
    }

    /// <summary>
    /// Erkannter Text je Bildordner, abgelegt als <c>.bildocr.json</c> im Ordner selbst —
    /// genau wie der CLIP-Index daneben. Damit wandert er mit, wenn der Bilderordner
    /// verschoben wird, und muss nicht neu erkannt werden.
    ///
    /// <b>Der Inhalt liegt derzeit im Klartext.</b> Wer Bildschirmfotos mit Kennwörtern
    /// oder Adressen in seiner Sammlung hat, hat sie danach ein zweites Mal lesbar auf
    /// der Platte. Eine Verschlüsselung ist vorgesehen, aber noch nicht gebaut — deshalb
    /// trägt die Datei eine Version: Eine verschlüsselte Fassung bekommt eine neue, und
    /// alte Klartext-Dateien werden dann nicht mehr gelesen, sondern neu erzeugt.
    /// </summary>
    internal sealed class OcrCache
    {
        internal const string DateiName = ".bildocr.json";

        /// <summary>
        /// Erhöhen, wenn sich das Format ändert — etwa beim Umstieg auf Verschlüsselung.
        /// Ältere Dateien werden dann verworfen statt falsch gelesen.
        /// </summary>
        private const int Version = 1;

        private readonly Dictionary<string, OcrEintrag> _eintraege =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Anzahl der gespeicherten Einträge.</summary>
        internal int Anzahl => _eintraege.Count;

        internal static string PfadFuerOrdner(string ordner) => Path.Combine(ordner, DateiName);

        /// <summary>
        /// Lädt den Cache eines Ordners. Fehlt die Datei, ist sie beschädigt oder trägt
        /// eine andere Version, bleibt der Cache einfach leer — dann wird neu erkannt.
        /// </summary>
        internal void Laden(string ordner)
        {
            _eintraege.Clear();

            string pfad = PfadFuerOrdner(ordner);
            if (!File.Exists(pfad))
            {
                return;
            }

            try
            {
                using FileStream fs = File.OpenRead(pfad);
                CacheDatei? datei = JsonSerializer.Deserialize<CacheDatei>(fs);
                if (datei is null || datei.Version != Version)
                {
                    return;
                }

                foreach (OcrEintrag e in datei.Eintraege)
                {
                    _eintraege[e.Pfad] = e;
                }
            }
            catch (Exception)
            {
                // Beschädigt oder alt — wie ein leerer Cache behandeln.
            }
        }

        /// <summary>
        /// Schreibt den Cache. Gibt <c>false</c> zurück, wenn der Ordner schreibgeschützt
        /// ist oder die Datei gesperrt war — das ist kein Grund, den Lauf abzubrechen.
        /// </summary>
        internal bool Speichern(string ordner)
        {
            try
            {
                string pfad = PfadFuerOrdner(ordner);

                // Bewusst NICHT versteckt — wie .bildindex.clip.json daneben.
                //
                // Die sichtbare Datei ist das Zeichen dafür, dass dieser Ordner gelesen
                // wurde; man sieht es beim Durchblättern im Explorer ohne nachzusehen.
                //
                // Ein früherer Versuch mit dem Versteckt-Attribut hatte einen bösen
                // Nebeneffekt: File.Create scheitert an einer versteckten Datei mit
                // UnauthorizedAccessException. Nur das allererste Speichern gelang, jedes
                // weitere fiel still in den catch-Zweig — im Ordner blieben für immer die
                // zuerst gelesenen 25 Bilder stehen.
                using (FileStream fs = File.Create(pfad))
                {
                    JsonSerializer.Serialize(fs, new CacheDatei
                    {
                        Version = Version,
                        Eintraege = _eintraege.Values.ToList()
                    });
                }

                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// True, wenn für diese Datei bereits ein Text vorliegt, der zu ihrem aktuellen
        /// Stand passt. Grösse und Änderungszeit müssen übereinstimmen — sonst wurde das
        /// Bild seither bearbeitet und der Text ist hinfällig.
        /// </summary>
        internal bool IstAktuell(string bildPfad)
        {
            if (!_eintraege.TryGetValue(bildPfad, out OcrEintrag? e))
            {
                return false;
            }

            try
            {
                var info = new FileInfo(bildPfad);
                return info.Exists
                    && info.Length == e.Dateigroesse
                    && info.LastWriteTimeUtc.Ticks == e.AenderungTicks;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Der gespeicherte Text, oder <c>null</c>, wenn keiner vorliegt.</summary>
        internal string? Hole(string bildPfad) =>
            _eintraege.TryGetValue(bildPfad, out OcrEintrag? e) ? e.Text : null;

        /// <summary>Nimmt einen erkannten Text auf. Ein vorhandener Eintrag wird ersetzt.</summary>
        internal void Setze(string bildPfad, string text, string sprache)
        {
            try
            {
                var info = new FileInfo(bildPfad);

                _eintraege[bildPfad] = new OcrEintrag
                {
                    Pfad = bildPfad,
                    Dateigroesse = info.Length,
                    AenderungTicks = info.LastWriteTimeUtc.Ticks,
                    Text = text,
                    Sprache = sprache
                };
            }
            catch (Exception)
            {
                // Datei verschwunden zwischen Erkennung und Ablage — Eintrag entfällt.
            }
        }

        /// <summary>
        /// Alle Bilder, deren erkannter Text die Zeichenfolge enthält. Ohne Rücksicht auf
        /// Gross- und Kleinschreibung, weil OCR sie ohnehin nicht verlässlich trifft.
        /// </summary>
        internal IReadOnlyList<string> Suche(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<string>();
            }

            return _eintraege.Values
                .Where(e => e.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Pfad)
                .ToList();
        }

        /// <summary>Wirft Einträge weg, deren Datei es nicht mehr gibt.</summary>
        internal int EntferneVerwaiste()
        {
            var weg = _eintraege.Keys.Where(p => !File.Exists(p)).ToList();
            foreach (string p in weg)
            {
                _eintraege.Remove(p);
            }

            return weg.Count;
        }

        private sealed class CacheDatei
        {
            public int Version { get; set; }
            public List<OcrEintrag> Eintraege { get; set; } = new();
        }
    }
}
