using System;
using System.Collections.Generic;
using System.Threading;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Nachbearbeitung roher CLIP-Embeddings ("Whitening light").
    ///
    /// Kontrastiv trainierte Embeddings zeigen alle in einen engen Kegel: es gibt eine
    /// gemeinsame Vorzugsrichtung, und die stärksten Hauptkomponenten tragen meist
    /// Belangloses (Gesamthelligkeit, Bildformat, "ist es überhaupt ein Foto"). Beides
    /// dominiert jeden Kosinus-Wert und drückt sämtliche Ähnlichkeiten in ein schmales
    /// Band — feine Unterschiede werden dadurch unsichtbar.
    ///
    /// Zwei Schritte dagegen:
    /// 1. Mittelvektor abziehen (entfernt die gemeinsame Vorzugsrichtung).
    /// 2. Die stärksten Hauptkomponenten herausprojizieren (entfernt das Belanglose).
    ///
    /// Die Hauptkomponenten kommen aus einer Power-Iteration mit Orthogonalisierung —
    /// portiert aus dem Lottojenerator-Netz (Ausgabeschicht.GrößterEigenvektor), aber
    /// ohne explizite Kovarianzmatrix: bei 512 Dimensionen wäre die 512×512 gross und
    /// ihr Aufbau O(n·512²). Direkt auf den Daten kostet ein Schritt nur O(n·512).
    /// </summary>
    public sealed class EmbeddingKalibrierung
    {
        private const int Iterationen = 60;

        private readonly float[] _mittel;
        private readonly List<float[]> _hauptkomponenten;

        public int Dimension => _mittel.Length;

        /// <summary>Anzahl der herausprojizierten Hauptkomponenten.</summary>
        public int KomponentenAnzahl => _hauptkomponenten.Count;

        /// <summary>Anzahl der Vektoren, aus denen die Kalibrierung gewonnen wurde.</summary>
        public int Grundmenge { get; }

        private EmbeddingKalibrierung(float[] mittel, List<float[]> hauptkomponenten, int grundmenge)
        {
            _mittel = mittel;
            _hauptkomponenten = hauptkomponenten;
            Grundmenge = grundmenge;
        }

        /// <summary>
        /// Berechnet Mittelvektor und die stärksten Hauptkomponenten über die
        /// übergebenen Vektoren. Die Eingabe wird nicht verändert.
        /// </summary>
        /// <param name="vektoren">Rohe Embeddings, alle gleich lang.</param>
        /// <param name="komponenten">Wie viele Hauptrichtungen entfernt werden (0 = nur zentrieren).</param>
        public static EmbeddingKalibrierung? Erstelle(
            IReadOnlyList<float[]> vektoren, int komponenten, CancellationToken token = default)
        {
            if (vektoren is null || vektoren.Count < 3)
                return null;

            int dim = vektoren[0].Length;
            if (dim == 0)
                return null;

            // --- Mittelvektor ---
            var mittel = new double[dim];
            int gezaehlt = 0;

            foreach (var v in vektoren)
            {
                if (v.Length != dim) continue;
                for (int d = 0; d < dim; d++) mittel[d] += v[d];
                gezaehlt++;
            }

            if (gezaehlt < 3)
                return null;

            for (int d = 0; d < dim; d++) mittel[d] /= gezaehlt;

            var mittelF = new float[dim];
            for (int d = 0; d < dim; d++) mittelF[d] = (float)mittel[d];

            // --- Zentrierte Arbeitskopie (Eingabe bleibt unangetastet) ---
            var zentriert = new List<float[]>(gezaehlt);
            foreach (var v in vektoren)
            {
                if (v.Length != dim) continue;

                var kopie = new float[dim];
                for (int d = 0; d < dim; d++) kopie[d] = v[d] - mittelF[d];
                zentriert.Add(kopie);
            }

            // --- Hauptkomponenten per Power-Iteration ---
            var pcs = new List<float[]>();
            int gewuenscht = Math.Max(0, Math.Min(komponenten, dim - 1));

            for (int k = 0; k < gewuenscht; k++)
            {
                token.ThrowIfCancellationRequested();

                var pc = NaechsteHauptkomponente(zentriert, dim, pcs, k, token);
                if (pc is null) break;

                pcs.Add(pc);
            }

            return new EmbeddingKalibrierung(mittelF, pcs, gezaehlt);
        }

        /// <summary>
        /// Power-Iteration auf der Kovarianz, ohne diese je aufzustellen:
        /// C·v entspricht der Summe über x·(x·v). In jedem Schritt werden die bereits
        /// gefundenen Richtungen herausprojiziert, so entsteht die nächste Komponente.
        /// </summary>
        private static float[]? NaechsteHauptkomponente(
            List<float[]> zentriert, int dim, List<float[]> bisherige, int seed, CancellationToken token)
        {
            var zufall = new Random(7 + seed);
            var v = new double[dim];
            for (int d = 0; d < dim; d++) v[d] = zufall.NextDouble() - 0.5;

            OrthogonalisiereUndNormiere(v, dim, bisherige);

            var neu = new double[dim];

            for (int schritt = 0; schritt < Iterationen; schritt++)
            {
                token.ThrowIfCancellationRequested();
                Array.Clear(neu);

                foreach (var x in zentriert)
                {
                    double s = 0;
                    for (int d = 0; d < dim; d++) s += x[d] * v[d];
                    if (s == 0) continue;
                    for (int d = 0; d < dim; d++) neu[d] += s * x[d];
                }

                if (!OrthogonalisiereUndNormiere(neu, dim, bisherige))
                    return null;

                Array.Copy(neu, v, dim);
            }

            var ergebnis = new float[dim];
            for (int d = 0; d < dim; d++) ergebnis[d] = (float)v[d];
            return ergebnis;
        }

        /// <summary>Projiziert die bereits gefundenen Richtungen heraus und normiert. False bei Entartung.</summary>
        private static bool OrthogonalisiereUndNormiere(double[] v, int dim, List<float[]> bisherige)
        {
            foreach (var pc in bisherige)
            {
                double s = 0;
                for (int d = 0; d < dim; d++) s += v[d] * pc[d];
                for (int d = 0; d < dim; d++) v[d] -= s * pc[d];
            }

            double norm = 0;
            for (int d = 0; d < dim; d++) norm += v[d] * v[d];
            norm = Math.Sqrt(norm);

            if (norm < 1e-9)
                return false;

            for (int d = 0; d < dim; d++) v[d] /= norm;
            return true;
        }

        /// <summary>
        /// Wendet die Kalibrierung an: zentrieren, Hauptkomponenten herausprojizieren,
        /// L2-normieren. Liefert stets eine <b>neue</b> Kopie — der Embedding-Cache im
        /// Index darf nicht verändert werden.
        /// </summary>
        public float[] Anwenden(float[] roh)
        {
            if (roh is null || roh.Length != _mittel.Length)
                return roh ?? Array.Empty<float>();

            int dim = _mittel.Length;
            var v = new float[dim];
            for (int d = 0; d < dim; d++) v[d] = roh[d] - _mittel[d];

            foreach (var pc in _hauptkomponenten)
            {
                double s = 0;
                for (int d = 0; d < dim; d++) s += v[d] * pc[d];
                for (int d = 0; d < dim; d++) v[d] -= (float)(s * pc[d]);
            }

            double norm = 0;
            for (int d = 0; d < dim; d++) norm += v[d] * (double)v[d];
            norm = Math.Sqrt(norm);

            if (norm > 1e-9)
                for (int d = 0; d < dim; d++) v[d] = (float)(v[d] / norm);

            return v;
        }

        /// <summary>
        /// Ähnlichkeit zweier kalibrierter Vektoren, abgebildet auf 0…1.
        ///
        /// Nach dem Zentrieren sind negative Kosinus-Werte normal und aussagekräftig
        /// (sie bedeuten "unähnlicher als der Durchschnitt"). Deshalb wird hier nicht
        /// wie in CnnDescriptor.Similarity bei 0 abgeschnitten, sondern der volle
        /// Bereich −1…+1 linear auf 0…1 gelegt: 0,5 entspricht mittlerer Ähnlichkeit.
        /// Die Skala ist damit eine andere als beim rohen CLIP-Wert — der Schwellwert
        /// muss für diesen Modus neu eingestellt werden.
        /// </summary>
        public static float Aehnlichkeit(float[] a, float[] b)
        {
            if (a.Length != b.Length || a.Length == 0)
                return 0f;

            double skalar = 0, normA = 0, normB = 0;
            for (int d = 0; d < a.Length; d++)
            {
                skalar += a[d] * (double)b[d];
                normA += a[d] * (double)a[d];
                normB += b[d] * (double)b[d];
            }

            double nenner = Math.Sqrt(normA) * Math.Sqrt(normB);
            if (nenner < 1e-9)
                return 0f;

            double cos = skalar / nenner;
            return (float)Math.Clamp((cos + 1.0) / 2.0, 0.0, 1.0);
        }
    }
}
