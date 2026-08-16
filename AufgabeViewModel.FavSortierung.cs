using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TestImage.Bildersuche;

namespace TestImage
{
    /// <summary>
    /// „FS" — Bilder nach Fav sortieren.
    ///
    /// Ordnet den offenen Ordner so, dass die wahrscheinlichen Ausschussbilder oben
    /// stehen. Gelernt wird aus den eigenen Entscheidungen: was im Ordner liegt, war
    /// gut genug; was in <c>kein_Fav</c> liegt, nicht.
    ///
    /// <b>Bewusst nur der eigene Ordner.</b> An 14 Ordnern mit zusammen 24.089 Bildern
    /// gemessen (ganze Künstlerordner zurückgehalten):
    ///
    /// <list type="bullet">
    /// <item>Richtung aus allen <i>anderen</i> Ordnern: AUC 0,53 — also Münzwurf.
    ///       Auch mit einem richtig trainierten Trenner nur 0,55.</item>
    /// <item>Richtung aus dem <i>eigenen</i> Ordner: AUC 0,76, und zwar in jedem
    ///       einzelnen Ordner über dem Zufall.</item>
    /// </list>
    ///
    /// Ein künstlerübergreifendes Muster gibt es also nicht. Der Grund liegt in CLIP
    /// selbst: Die 512 Zahlen beschreiben Inhalt und Stil — „was ist da drauf" —, nicht
    /// den persönlichen Geschmack. Der ist an den Künstler gebunden.
    ///
    /// <b>Ohne kein_Fav geht es auch</b>, nur schwächer: Der Schwerpunkt der Behalter
    /// allein bringt AUC 0,68. Je mehr aussortiert wurde, desto wichtiger wird das
    /// Gegenbeispiel — bei einem Ordner mit 90 % Ausschuss trennt der Schwerpunkt der
    /// Behalter gar nichts mehr, weil er mitten in der Masse liegt.
    ///
    /// <b>Kein CLIP nötig.</b> Gerechnet wird ausschliesslich auf den Vektoren, die
    /// schon in den Indexdateien stehen. Kein Modell wird geladen, kein Bild dekodiert —
    /// die ganze Sortierung eines Ordners dauert Millisekunden.
    /// </summary>
    public partial class AufgabeViewModel
    {
        /// <summary>
        /// True, solange die Trefferliste aus einem FS-Lauf stammt. Steuert, welcher
        /// Renderer auf eine Bewegung des Schwellenreglers reagiert.
        /// </summary>
        [ObservableProperty]
        public partial bool ErgebnisseSindFavSortierung { get; set; }

        /// <summary>Mindestzahl an Beispielen je Seite, unter der die Richtung nur Rauschen wäre.</summary>
        private const int FavMindestBeispiele = 5;

        /// <summary>
        /// Marke „in diesem Ordner ist alles geprüft" für den offenen Ordner.
        ///
        /// Sie ist keine Kleinigkeit, sondern der Hebel: Aus allen Ordnern zusammen
        /// ergibt das gemeinsame Muster AUC 0,53 — Münzwurf. Aus den gründlich
        /// sortierten allein 0,66. Wer halbfertig lässt, verdirbt die gute Seite, denn
        /// dort liegt dann ungeprüfter Ausschuss zwischen den Behaltern.
        /// </summary>
        [ObservableProperty]
        public partial bool OrdnerFertigSortiert { get; set; }

        /// <summary>Beschriftung neben der Marke – nennt den Stand des gemeinsamen Profils.</summary>
        [ObservableProperty]
        public partial string FavProfilText { get; set; } = string.Empty;

        partial void OnOrdnerFertigSortiertChanged(bool value)
        {
            string? ordner = AktuellerBildOrdner();
            if (string.IsNullOrEmpty(ordner))
            {
                return;
            }

            if (Bildersuche.FavProfilVerzeichnis.IstFertig(ordner) == value)
            {
                return;   // kommt vom Nachziehen beim Ordnerwechsel, nicht vom Nutzer
            }

            Bildersuche.FavProfilVerzeichnis.SetzeFertig(ordner, value);
            AktualisiereFavProfilText();
        }

        /// <summary>
        /// Zieht Marke und Beschriftung auf den offenen Ordner nach. Wird beim
        /// Bildwechsel gerufen.
        /// </summary>
        private void AktualisiereFavProfilAnzeige()
        {
            string? ordner = AktuellerBildOrdner();
            OrdnerFertigSortiert = Bildersuche.FavProfilVerzeichnis.IstFertig(ordner);
            AktualisiereFavProfilText();
        }

        private void AktualisiereFavProfilText()
        {
            var w = Bildersuche.FavProfilVerzeichnis.GemeinsameRichtung(out int ordner, out int bilder);

            FavProfilText = w is null
                ? "Gemeinsames Profil: noch kein Ordner als fertig markiert."
                : $"Gemeinsames Profil: {ordner} Ordner, {bilder:N0} Bilder.";
        }

        private bool CanExecuteFavSortieren()
            => AktuellerOrdnerIndiziert && !SerieSucheLaeuft && !IndexLaeuft;

        /// <summary>
        /// Sortiert den offenen Ordner nach der gelernten Richtung. Das Ergebnis landet
        /// in derselben Trefferliste wie die übrigen Suchen — übernommen wird es mit
        /// <c>BTN_TrefferUebernehmen</c>, verschoben wird also nichts von selbst.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteFavSortieren), IncludeCancelCommand = true)]
        private async Task CommandExecuteFavSortieren(CancellationToken token)
        {
            string? bildPfad = SelectedBildchen?.BName;
            string? ordner = string.IsNullOrEmpty(bildPfad) ? null : Path.GetDirectoryName(bildPfad);
            if (string.IsNullOrEmpty(ordner))
            {
                return;
            }

            string eigenerIndex = Path.Combine(ordner, BildAnalyseService.CacheDateiName);
            if (!File.Exists(eigenerIndex))
            {
                SucheStatus = "Kein Index vorhanden – erst den Ordner indexieren.";
                return;
            }

            SuchErgebnisse.Clear();
            LeereTrefferCache();
            ErgebnisseSindFavSortierung = false;
            CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
            SchliesseIndexOrdnerKarte();

            SucheStatus = "Sortiere nach deinen bisherigen Entscheidungen…";
            SerieFortschritt = 0;
            SerieIndeterminate = true;
            SerieSucheLaeuft = true;

            try
            {
                string keinFavIndex = Path.Combine(ordner, "kein_Fav", BildAnalyseService.CacheDateiName);

                var (treffer, hinweis) = await Task.Run(
                    () => BerechneFavReihenfolge(ordner, eigenerIndex, keinFavIndex, token), token);

                if (treffer.Count == 0)
                {
                    SucheStatus = hinweis;
                    return;
                }

                // Auf die geladene Liste abbilden: Der Index kann Pfade kennen, die
                // inzwischen verschoben oder gelöscht sind. Ohne diesen Schritt liefe
                // die Übernahme auf Bilder, die es in der Liste gar nicht gibt.
                var gefiltert = AufListeAbbilden(treffer, nurAusListe: true);

                if (gefiltert.Count == 0)
                {
                    SucheStatus = "Der Index passt nicht zur geladenen Liste – Ordner neu indexieren.";
                    return;
                }

                _letzteFrage = "Fav-Sortierung";
                await LadeSchemaKandidatenAsync(gefiltert, token);

                ErgebnisseSindFavSortierung = true;
                ErgebnisseSindSchemaAehnlich = true;   // blendet denselben Schwellenregler ein

                // Das Profil dieses Ordners ist gerade dazugekommen – Stand neu ermitteln
                // und mit anzeigen, damit man den Aufbau des gemeinsamen Profils mitliest.
                AktualisiereFavProfilText();
                _favHinweis = hinweis + " " + FavProfilText;

                RenderFavSortierung();
                CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
            }
            catch (OperationCanceledException)
            {
                SucheStatus = "Fav-Sortierung abgebrochen.";
            }
            catch (Exception ex)
            {
                SucheStatus = "Fehler bei der Fav-Sortierung: " + ex.Message;
            }
            finally
            {
                SerieSucheLaeuft = false;
            }
        }

        /// <summary>Woraus gelernt wurde – für die Statuszeile, damit der Wert einzuordnen ist.</summary>
        private string _favHinweis = string.Empty;

        /// <summary>
        /// Zeigt aus dem sortierten Satz nur die Bilder ab der Reglerschwelle.
        ///
        /// Eigener Renderer statt <see cref="RenderSchemaAehnlich"/>, weil die Zahl hier
        /// etwas anderes bedeutet: keine Ähnlichkeit zu einem Anfragebild, sondern der
        /// Rang innerhalb dieses Ordners. 100 % ist das verdächtigste Bild, 0 % das
        /// unverdächtigste — der Regler schneidet also einen Anteil ab, keine absolute
        /// Güte.
        /// </summary>
        private void RenderFavSortierung()
        {
            HatTrefferCache = _alleSuchTreffer.Count > 0;
            SuchErgebnisse.Clear();

            float min = (float)(SchemaAehnlichkeitProzent / 100.0);
            int gezeigt = 0;

            foreach (var (erg, score) in _alleSuchTreffer)
            {
                if (score < min)
                {
                    continue;   // Liste ist absteigend sortiert
                }

                SuchErgebnisse.Add(erg);
                gezeigt++;
            }

            SucheStatus = gezeigt == 0
                ? $"Keine Bilder über {SchemaAehnlichkeitProzent:F0} %. {_favHinweis}"
                : $"{gezeigt} Bilder als Ausschuss-Kandidaten (Rang ≥ {SchemaAehnlichkeitProzent:F0} %). {_favHinweis}";

            CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Bildet die Trennrichtung und bewertet damit jedes Bild des Ordners.
        /// Läuft im Hintergrund; rührt keine Oberfläche an.
        /// </summary>
        private static (IReadOnlyList<(string Path, float Score)> Treffer, string Hinweis)
            BerechneFavReihenfolge(string ordner, string eigenerIndex, string keinFavIndex, CancellationToken token)
        {
            var leer = Array.Empty<(string, float)>();

            var behalter = LiesIndexVektoren(eigenerIndex, token);
            if (behalter.Count < FavMindestBeispiele)
            {
                return (leer, $"Zu wenige Bilder im Index – es werden mindestens {FavMindestBeispiele} gebraucht.");
            }

            int dim = behalter[0].V.Length;

            var aussortiert = File.Exists(keinFavIndex)
                ? LiesIndexVektoren(keinFavIndex, token)
                : new List<(string Pfad, float[] V)>();

            // Erst merken, dann rechnen.
            //
            // Gespeichert werden Anzahl und Summe je Seite, nicht die fertige Richtung:
            // Nur so lassen sich die Ordner später zu einem gemeinsamen Profil addieren,
            // in dem jedes Bild gleich viel zählt statt jeder Ordner.
            var profil = Bildersuche.FavProfilVerzeichnis.Merke(
                ordner,
                behalter.Count, Summe(behalter.Select(b => b.V), dim),
                aussortiert.Count, Summe(aussortiert.Select(b => b.V), dim));

            token.ThrowIfCancellationRequested();

            // Reihenfolge der Quellen — so hat es sich gemessen:
            //   eigener Ordner mit beiden Seiten   AUC 0,76
            //   gemeinsames Profil (fertige Ordner) AUC 0,66
            //   nur die Behalter dieses Ordners     AUC 0,68, bricht aber ein, sobald
            //                                       viel aussortiert wurde (dort 0,53)
            float[] richtung;
            string hinweis;

            if (profil.Richtung() is float[] eigene)
            {
                richtung = eigene;
                hinweis = $"Gelernt aus {behalter.Count} behaltenen und {aussortiert.Count} aussortierten Bildern dieses Ordners.";
            }
            else if (Bildersuche.FavProfilVerzeichnis.GemeinsameRichtung(
                         out int pOrdner, out int pBilder, ausser: ordner) is float[] gemeinsam
                     && gemeinsam.Length == dim)
            {
                richtung = gemeinsam;
                hinweis = $"Kein kein_Fav in diesem Ordner – gemeinsames Profil aus {pOrdner} fertig sortierten Ordnern ({pBilder:N0} Bilder). Trennt schwächer als eigene Beispiele.";
            }
            else
            {
                // Letzter Rückfall: Abstand zum Schwerpunkt der Behalter. Ehrlich
                // benennen, dass das der schwächste Weg ist.
                var gut = Mittelwert(behalter.Select(b => b.V), dim);

                richtung = new float[dim];
                for (int i = 0; i < dim; i++)
                {
                    richtung[i] = -gut[i];
                }

                hinweis = $"Nur aus {behalter.Count} behaltenen Bildern gelernt – ohne Gegenbeispiele trennt es deutlich schwächer.";
            }

            token.ThrowIfCancellationRequested();

            var werte = new List<(string Pfad, double Wert)>(behalter.Count);
            foreach (var b in behalter)
            {
                double s = 0;
                for (int i = 0; i < dim; i++)
                {
                    s += richtung[i] * (double)b.V[i];
                }

                werte.Add((b.Pfad, s));
            }

            // Auf 0 … 1 strecken. Der Rohwert ist ein Skalarprodukt ohne feste Spanne;
            // erst gestreckt lässt er sich am vorhandenen Prozentregler abschneiden.
            double kleinster = werte.Min(w => w.Wert);
            double groesster = werte.Max(w => w.Wert);
            double spanne = groesster - kleinster;

            var treffer = werte
                .Select(w => (w.Pfad, Score: (float)(spanne > 1e-9 ? (w.Wert - kleinster) / spanne : 0.5)))
                .OrderByDescending(t => t.Score)
                .ToList();

            return (treffer, hinweis);
        }

        /// <summary>
        /// Liest Pfade und Vektoren aus einer Indexdatei.
        ///
        /// Bewusst als eigener Leser statt über <c>ImageIndex</c>: Dessen Erzeugung
        /// verlangt ein Beschreibungsverfahren, und das hiesse CLIP laden — 600 MB und
        /// mehrere Sekunden für Zahlen, die längst auf der Platte stehen.
        /// </summary>
        private static List<(string Pfad, float[] V)> LiesIndexVektoren(string indexPfad, CancellationToken token)
        {
            var liste = new List<(string Pfad, float[] V)>();

            using var fs = File.OpenRead(indexPfad);
            using var doc = JsonDocument.Parse(fs);

            if (!doc.RootElement.TryGetProperty("Entries", out var eintraege)
                || eintraege.ValueKind != JsonValueKind.Array)
            {
                return liste;
            }

            foreach (var e in eintraege.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();

                if (!e.TryGetProperty("Path", out var p)
                    || !e.TryGetProperty("Descriptor", out var d)
                    || d.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var v = new float[d.GetArrayLength()];
                int k = 0;
                foreach (var f in d.EnumerateArray())
                {
                    v[k++] = f.GetSingle();
                }

                // Auf Einheitslänge bringen: Sonst zählt ein Bild mit längerem Vektor
                // mehr als die anderen, ohne dass das etwas bedeutet.
                double laenge = 0;
                for (int i = 0; i < v.Length; i++)
                {
                    laenge += v[i] * (double)v[i];
                }

                laenge = Math.Sqrt(laenge);
                if (laenge <= 1e-9)
                {
                    continue;
                }

                for (int i = 0; i < v.Length; i++)
                {
                    v[i] = (float)(v[i] / laenge);
                }

                liste.Add((p.GetString() ?? string.Empty, v));
            }

            return liste;
        }

        /// <summary>Summe der Vektoren – Grundlage des gemerkten Profils.</summary>
        private static float[] Summe(IEnumerable<float[]> menge, int dim)
        {
            var summe = new double[dim];

            foreach (var v in menge)
            {
                for (int i = 0; i < dim && i < v.Length; i++)
                {
                    summe[i] += v[i];
                }
            }

            var r = new float[dim];
            for (int i = 0; i < dim; i++)
            {
                r[i] = (float)summe[i];
            }

            return r;
        }

        private static float[] Mittelwert(IEnumerable<float[]> menge, int dim)
        {
            var summe = new double[dim];
            int n = 0;

            foreach (var v in menge)
            {
                for (int i = 0; i < dim; i++)
                {
                    summe[i] += v[i];
                }

                n++;
            }

            var mittel = new float[dim];
            if (n > 0)
            {
                for (int i = 0; i < dim; i++)
                {
                    mittel[i] = (float)(summe[i] / n);
                }
            }

            return mittel;
        }
    }
}
