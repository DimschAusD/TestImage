using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Was die Anwendung sich zu einem Ordner gemerkt hat: wie viele Bilder auf welcher
    /// Seite standen und wie ihre Vektoren zusammengezählt aussahen.
    ///
    /// <b>Warum Summen und nicht die Richtung selbst:</b> Aus Summe und Anzahl lässt
    /// sich die Richtung jederzeit ausrechnen — aber eben auch die <i>gemeinsame</i>
    /// Richtung über mehrere Ordner, indem man die Summen addiert. Mit einer fertigen
    /// Richtung je Ordner ginge das nicht; ein Mittelwert von Mittelwerten gewichtet
    /// einen Ordner mit 20 Bildern genauso wie einen mit 2000.
    ///
    /// Platzbedarf: zwei mal 512 Zahlen, rund 4 KB je Ordner. Bei vierhundert Künstlern
    /// also weniger als zwei Megabyte — die Vektoren selbst liegen weiterhin nur in den
    /// Indexdateien.
    /// </summary>
    public sealed class FavProfil
    {
        public string Ordner { get; set; } = string.Empty;

        /// <summary>
        /// Vom Nutzer gesetzt: In diesem Ordner ist alles geprüft, was noch daliegt, ist
        /// gut. Nur solche Ordner fliessen ins gemeinsame Profil ein.
        ///
        /// Gemessen: Nimmt man alle Ordner, liegt das gemeinsame Muster bei AUC 0,53 —
        /// Münzwurf. Nimmt man nur die gründlich sortierten, sind es 0,66. Der
        /// Unterschied ist also nicht das Verfahren, sondern halbfertige Arbeit.
        /// </summary>
        public bool FertigSortiert { get; set; }

        public int AnzahlGut { get; set; }
        public int AnzahlMies { get; set; }

        public float[] SummeGut { get; set; } = Array.Empty<float>();
        public float[] SummeMies { get; set; } = Array.Empty<float>();

        public DateTime Stand { get; set; }

        /// <summary>True, wenn beide Seiten belegt sind – erst dann trägt die Richtung.</summary>
        public bool HatBeideSeiten =>
            AnzahlGut >= FavProfilVerzeichnis.MindestBeispiele
            && AnzahlMies >= FavProfilVerzeichnis.MindestBeispiele
            && SummeGut.Length > 0
            && SummeGut.Length == SummeMies.Length;

        /// <summary>
        /// Trennrichtung dieses Ordners: von den Behaltern weg, zu den Aussortierten hin.
        /// Ein hoher Wert heisst „sieht aus wie das, was du weggeworfen hast".
        /// </summary>
        public float[]? Richtung()
        {
            if (!HatBeideSeiten)
            {
                return null;
            }

            int dim = SummeGut.Length;
            var w = new float[dim];

            for (int i = 0; i < dim; i++)
            {
                w[i] = (SummeMies[i] / AnzahlMies) - (SummeGut[i] / AnzahlGut);
            }

            return w;
        }
    }

    /// <summary>
    /// Verwaltet die gemerkten Profile aller Ordner in einer Datei neben der Anwendung.
    ///
    /// Zweck ist das, was ohne Gedächtnis jedes Mal verlorenginge: FS wirkt sofort, das
    /// Gelernte überlebt das Aufräumen eines <c>kein_Fav</c>, und aus den als fertig
    /// markierten Ordnern entsteht nebenbei ein gemeinsames Profil für Künstler, zu
    /// denen es noch gar keine Historie gibt.
    /// </summary>
    internal static class FavProfilVerzeichnis
    {
        private const string DateiName = "fav.profile.json";

        /// <summary>Unter so vielen Beispielen je Seite wäre die Richtung nur Rauschen.</summary>
        internal const int MindestBeispiele = 5;

        internal static string Pfad =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DateiName);

        private static List<FavProfil>? _profile;

        private sealed class Datei
        {
            public int Version { get; set; } = 1;
            public List<FavProfil> Profile { get; set; } = new();
        }

        private static List<FavProfil> Hole()
        {
            if (_profile is not null)
            {
                return _profile;
            }

            try
            {
                if (File.Exists(Pfad))
                {
                    var d = JsonSerializer.Deserialize<Datei>(File.ReadAllText(Pfad));
                    _profile = d?.Profile ?? new List<FavProfil>();
                }
                else
                {
                    _profile = new List<FavProfil>();
                }
            }
            catch
            {
                // Beschädigte Datei soll die Anwendung nicht aufhalten – dann eben leer.
                _profile = new List<FavProfil>();
            }

            return _profile;
        }

        private static void Speichere()
        {
            try
            {
                var d = new Datei { Profile = Hole() };
                File.WriteAllText(Pfad, JsonSerializer.Serialize(d));
            }
            catch
            {
                // Nicht schreibbar (z. B. Programmordner ohne Rechte) – kein Grund zum Abbruch.
            }
        }

        internal static IReadOnlyList<FavProfil> Alle() => Hole();

        internal static FavProfil? Finde(string? ordner)
        {
            if (string.IsNullOrWhiteSpace(ordner))
            {
                return null;
            }

            return Hole().FirstOrDefault(
                p => string.Equals(p.Ordner, ordner, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Legt das Profil eines Ordners an oder frischt es auf. Die Marke
        /// <see cref="FavProfil.FertigSortiert"/> bleibt dabei erhalten — sie ist eine
        /// Aussage des Nutzers, keine Messgrösse.
        /// </summary>
        internal static FavProfil Merke(
            string ordner, int anzahlGut, float[] summeGut, int anzahlMies, float[] summeMies)
        {
            var p = Finde(ordner);

            if (p is null)
            {
                p = new FavProfil { Ordner = ordner };
                Hole().Add(p);
            }

            p.AnzahlGut = anzahlGut;
            p.SummeGut = summeGut;
            p.AnzahlMies = anzahlMies;
            p.SummeMies = summeMies;
            p.Stand = DateTime.Now;

            Speichere();
            return p;
        }

        /// <summary>Setzt die Marke „fertig sortiert" und legt bei Bedarf einen Eintrag an.</summary>
        internal static void SetzeFertig(string ordner, bool fertig)
        {
            if (string.IsNullOrWhiteSpace(ordner))
            {
                return;
            }

            var p = Finde(ordner);
            if (p is null)
            {
                p = new FavProfil { Ordner = ordner, Stand = DateTime.Now };
                Hole().Add(p);
            }

            p.FertigSortiert = fertig;
            Speichere();
        }

        internal static bool IstFertig(string? ordner) => Finde(ordner)?.FertigSortiert == true;

        /// <summary>
        /// Gemeinsame Richtung aus allen als fertig markierten Ordnern.
        ///
        /// Gebildet aus den <b>zusammengezählten</b> Summen, nicht aus den einzelnen
        /// Richtungen: So zählt jedes Bild gleich viel, statt jeder Ordner. Ein Ordner
        /// mit 20 Bildern soll das Ergebnis nicht so stark bestimmen wie einer mit 2000.
        ///
        /// <c>null</c>, wenn noch kein Ordner als fertig markiert ist oder zu wenig
        /// zusammenkommt.
        /// </summary>
        internal static float[]? GemeinsameRichtung(out int ordnerAnzahl, out int bilder, string? ausser = null)
        {
            ordnerAnzahl = 0;
            bilder = 0;

            var tauglich = Hole()
                .Where(p => p.FertigSortiert && p.HatBeideSeiten)
                .Where(p => ausser is null
                            || !string.Equals(p.Ordner, ausser, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (tauglich.Count == 0)
            {
                return null;
            }

            int dim = tauglich[0].SummeGut.Length;
            var sGut = new double[dim];
            var sMies = new double[dim];
            long nGut = 0, nMies = 0;

            foreach (var p in tauglich)
            {
                if (p.SummeGut.Length != dim || p.SummeMies.Length != dim)
                {
                    continue;   // andere Vektorlänge – anderer Indexstand, überspringen
                }

                for (int i = 0; i < dim; i++)
                {
                    sGut[i] += p.SummeGut[i];
                    sMies[i] += p.SummeMies[i];
                }

                nGut += p.AnzahlGut;
                nMies += p.AnzahlMies;
                ordnerAnzahl++;
                bilder += p.AnzahlGut + p.AnzahlMies;
            }

            if (nGut < MindestBeispiele || nMies < MindestBeispiele)
            {
                return null;
            }

            var w = new float[dim];
            for (int i = 0; i < dim; i++)
            {
                w[i] = (float)((sMies[i] / nMies) - (sGut[i] / nGut));
            }

            return w;
        }
    }
}
