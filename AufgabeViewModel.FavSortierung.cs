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
    /// <b>Überwiegend der eigene Ordner.</b> An 14 Künstlerordnern gemessen, jeweils
    /// ganze Ordner zurückgehalten:
    ///
    /// <list type="bullet">
    /// <item>Richtung aus allen <i>anderen</i> Ordnern: AUC 0,53 — also Münzwurf.
    ///       Auch mit einem richtig trainierten Trenner nur 0,55.</item>
    /// <item>Richtung aus dem <i>eigenen</i> Ordner: AUC 0,76, und zwar in jedem
    ///       einzelnen Ordner über dem Zufall.</item>
    /// </list>
    ///
    /// Ein starkes künstlerübergreifendes Muster gibt es also nicht. Der Grund liegt in
    /// CLIP selbst: Die 512 Zahlen beschreiben Inhalt und Stil — „was ist da drauf" —,
    /// nicht den persönlichen Geschmack. Der ist an den Künstler gebunden.
    ///
    /// <b>Nachgemessen am 31.08.2026</b>, inzwischen 53 Ordner mit beiden Seiten (gut
    /// drei Viertel davon aussortiert), diesmal mit Kreuzvalidierung im Ordner und dem
    /// gemeinsamen Profil ohne den jeweils geprüften Ordner:
    ///
    /// <list type="bullet">
    /// <item>eigener Ordner allein: <b>AUC 0,672</b> — nicht 0,76. Die ältere Zahl ist
    ///       entweder ohne Kreuzvalidierung entstanden oder am anderen Bestand gemessen
    ///       (damals überwog das Behaltene, heute das Aussortierte); das ist nicht
    ///       auseinandergehalten.</item>
    /// <item>eigener Ordner <i>und</i> gemeinsames Profil gemischt: <b>AUC 0,687</b>.</item>
    /// <item>gemeinsames Profil allein: AUC 0,618.</item>
    /// </list>
    ///
    /// Der geteilte Kern ist klein, aber vorhanden: Die Trennrichtungen zweier Ordner
    /// ähneln sich nur mit Kosinus 0,108 (Zufallsniveau 0,044), ihre Bildinhalte dagegen
    /// mit 0,871 — überall dasselbe Motiv, verschiedener Geschmack. Zur <i>gemeinsamen</i>
    /// Richtung steigt die Ähnlichkeit auf 0,275, und sie wächst mit der Ordnergrösse.
    /// Deshalb wird gemischt statt gewählt, mit <see cref="FavMischGewicht"/>.
    ///
    /// In gründlich getrennten Ordnern (über 70 % aussortiert) ist der Gewinn fast
    /// doppelt so gross wie im Schnitt: 0,675 → 0,699. Halbfertige Ordner verwässern ihn.
    ///
    /// <b>Ohne kein_Fav geht es auch</b>, nur schwächer: Der Schwerpunkt der Behalter
    /// allein bringt AUC 0,68. Je mehr aussortiert wurde, desto wichtiger wird das
    /// Gegenbeispiel — bei einem Ordner mit 90 % Ausschuss trennt der Schwerpunkt der
    /// Behalter gar nichts mehr, weil er mitten in der Masse liegt.
    ///
    /// <b>Kein CLIP nötig.</b> Gerechnet wird ausschliesslich auf den Vektoren, die
    /// schon in den Indexdateien stehen. Kein Modell wird geladen, kein Bild dekodiert.
    /// Seit die Richtung über <see cref="RidgeRichtung"/> entsteht, ist die Sortierung
    /// nicht mehr in Millisekunden fertig, sondern in Bruchteilen einer Sekunde bis
    /// gut einer Sekunde — der Preis für eine 512×512-Matrix samt Zerlegung.
    ///
    /// <b>Was die Trennschärfe ausmacht</b>, gemessen am 31.08.2026 über 53 Ordner
    /// (kreuzvalidiert, gemeinsames Profil jeweils ohne den geprüften Ordner):
    ///
    /// <list type="table">
    /// <item><term>Schwerpunkt-Differenz allein</term><description>AUC 0,674</description></item>
    /// <item><term>+ gemeinsames Profil</term><description>AUC 0,688</description></item>
    /// <item><term>Ridge allein</term><description>AUC 0,698</description></item>
    /// <item><term><b>Ridge + gemeinsames Profil</b></term><description><b>AUC 0,714</b></description></item>
    /// </list>
    ///
    /// Die beiden Zugewinne überlappen sich kaum, weil sie verschiedene Enden des
    /// Bestands bedienen: Ridge liest grosse Ordner besser aus (dort 0,673 → 0,730),
    /// das gemeinsame Profil stützt kleine (dort 0,672 → 0,699). Ein k-nächste-Nachbarn-
    /// Ansatz wurde mitgemessen und bringt nichts (0,66) — eine nichtlineare Struktur,
    /// die sich über Nachbarschaft fassen liesse, gibt es in diesen Vektoren nicht.
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
        /// Mischgewicht zwischen eigener und gemeinsamer Richtung: <c>a = n / (n + k)</c>
        /// mit <c>n</c> = kleinere Seite dieses Ordners. Bei genau so vielen Bildern
        /// zählen beide Quellen gleich viel.
        ///
        /// Eingemessen am 31.08.2026 über 53 Ordner (siehe Klassendoku). Seit die eigene
        /// Richtung über <see cref="RidgeRichtung"/> entsteht, liegt das Optimum bei 5
        /// statt bei 12: Der bessere Lerner nutzt den eigenen Ordner weiter aus und
        /// braucht entsprechend weniger Stützung von aussen.
        /// </summary>
        private const int FavMischGewicht = 5;

        /// <summary>
        /// Dämpfung der Ridge-Rechnung (das λ in <c>(C + λI)⁻¹</c>).
        ///
        /// Eingemessen am 31.08.2026: unterhalb von 0,001 passt sich die Richtung an das
        /// Rauschen der einzelnen Bilder an (bei 0,00003 fällt die Trennschärfe auf 0,658),
        /// oberhalb von 0,1 wird sie so stark gebremst, dass wieder die blosse
        /// Schwerpunkt-Differenz übrig bleibt. Zwischen 0,003 und 0,01 liegt ein breites
        /// Plateau bei AUC 0,70.
        /// </summary>
        private const double FavRidgeDaempfung = 0.003;

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

            // Beide Quellen zusammen, nicht die eine oder die andere — so hat es sich
            // gemessen (31.08.2026, 53 Ordner, kreuzvalidiert):
            //   eigener Ordner allein                AUC 0,672   (der frühere Zustand)
            //   beide gemischt                       AUC 0,687
            //   gemeinsames Profil allein            AUC 0,618
            //
            // Warum überhaupt mischen, wo doch die Trennrichtungen zweier Künstler kaum
            // etwas gemein haben (Kosinus 0,108 bei Zufallsniveau 0,044, jedes vierte Paar
            // zeigt sogar gegeneinander): Die Ähnlichkeit zur *gemeinsamen* Richtung wächst
            // mit der Zahl der Beispiele — 0,21 bei unter 20 Bildern je Seite, 0,43 bei
            // über 150. Was in kleinen Ordnern nach eigenwilligem Geschmack aussieht, ist
            // also überwiegend Rauschen. Genau dort hilft das gemeinsame Profil, und genau
            // dort hat der frühere Code es weggeworfen, sobald fünf Bilder im kein_Fav
            // lagen. Grosse Ordner bleiben praktisch unberührt (0,6904 → 0,6906).
            //
            // Beide Richtungen müssen dafür auf Länge 1: Sie entstehen aus Mittelwerten
            // verschieden grosser Mengen, sonst entschiede ihre zufällige Länge das
            // Gewicht statt a.
            float[] richtung;
            string hinweis;

            float[]? eigene = Normiert(profil.Richtung());

            // Die Schwerpunkt-Differenz ist erst der Ausgangspunkt: Sie verbindet stur die
            // beiden Mittelpunkte und nimmt dabei an, alle 512 Zahlen streuten gleich stark
            // und unabhängig voneinander. Das tun sie nicht. RidgeRichtung rechnet die
            // Streuung heraus und hebt damit die Zahlen hervor, die wirklich trennen.
            if (eigene is not null)
            {
                eigene = Normiert(RidgeRichtung(behalter, aussortiert, dim, eigene, token)) ?? eigene;
            }

            float[]? gemeinsam = Normiert(Bildersuche.FavProfilVerzeichnis.GemeinsameRichtung(
                out int pOrdner, out int pBilder, ausser: ordner));

            if (gemeinsam is not null && gemeinsam.Length != dim)
            {
                gemeinsam = null;   // anderer Indexstand – nicht mischbar
            }

            if (eigene is not null && gemeinsam is not null)
            {
                int n = Math.Min(behalter.Count, aussortiert.Count);
                double a = (double)n / (n + FavMischGewicht);

                richtung = new float[dim];
                for (int i = 0; i < dim; i++)
                {
                    richtung[i] = (float)(a * eigene[i] + (1 - a) * gemeinsam[i]);
                }

                hinweis = $"Gelernt aus {behalter.Count} behaltenen und {aussortiert.Count} aussortierten Bildern dieses Ordners; das gemeinsame Profil steuert {1 - a:P0} bei ({pOrdner} Ordner, {pBilder:N0} Bilder).";
            }
            else if (eigene is not null)
            {
                richtung = eigene;
                hinweis = $"Gelernt aus {behalter.Count} behaltenen und {aussortiert.Count} aussortierten Bildern dieses Ordners.";
            }
            else if (gemeinsam is not null)
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

        /// <summary>
        /// Trennrichtung nach Ridge: <c>(C + λI)⁻¹ · Schwerpunkt-Differenz</c>, wobei
        /// <c>C</c> die Streumatrix beider Seiten ist, jede Seite gleich gewichtet.
        ///
        /// <b>Warum das mehr ist als die Schwerpunkt-Differenz</b>, obwohl beides eine
        /// Gerade durch denselben Raum legt: Die 512 CLIP-Zahlen streuen unterschiedlich
        /// stark und hängen miteinander zusammen. Eine Richtung, in der ohnehin alles weit
        /// auseinanderliegt, trennt nichts — dort ist der Abstand zwischen den Schwerpunkten
        /// nur Rauschen. <c>C⁻¹</c> rechnet genau das heraus. Gemessen am 31.08.2026 über
        /// 53 Ordner: AUC 0,674 → 0,698, bei Ordnern mit über 150 Bildern je Seite sogar
        /// 0,673 → 0,730. Bei kleinen Ordnern bringt es wenig — dort fehlt das Material
        /// für eine brauchbare Schätzung von <c>C</c>, und stattdessen trägt das
        /// gemeinsame Profil (siehe <see cref="FavMischGewicht"/>).
        ///
        /// <b>Kosten:</b> eine 512×512-Matrix und eine Cholesky-Zerlegung, zusammen unter
        /// einer Sekunde. Das ist der Grund, warum die Sortierung nicht mehr in
        /// Millisekunden fertig ist.
        ///
        /// Gibt <c>null</c> zurück, wenn die Zerlegung nicht durchläuft; der Aufrufer
        /// bleibt dann bei der Schwerpunkt-Differenz.
        /// </summary>
        private static float[]? RidgeRichtung(
            List<(string Pfad, float[] V)> behalter,
            List<(string Pfad, float[] V)> aussortiert,
            int dim,
            float[] schwerpunktDifferenz,
            CancellationToken token)
        {
            if (behalter.Count == 0 || aussortiert.Count == 0)
            {
                return null;
            }

            // Nur die untere Dreieckshälfte füllen – die Matrix ist symmetrisch.
            var a = new double[dim][];
            for (int i = 0; i < dim; i++)
            {
                a[i] = new double[i + 1];
            }

            void Sammle(List<(string Pfad, float[] V)> menge)
            {
                double gewicht = 1.0 / menge.Count;   // beide Seiten zählen gleich viel
                foreach (var (_, v) in menge)
                {
                    token.ThrowIfCancellationRequested();

                    for (int i = 0; i < dim; i++)
                    {
                        double zeile = gewicht * v[i];
                        if (zeile == 0)
                        {
                            continue;
                        }

                        var ai = a[i];
                        for (int j = 0; j <= i; j++)
                        {
                            ai[j] += zeile * v[j];
                        }
                    }
                }
            }

            Sammle(behalter);
            Sammle(aussortiert);

            for (int i = 0; i < dim; i++)
            {
                a[i][i] += FavRidgeDaempfung;
            }

            var b = new double[dim];
            for (int i = 0; i < dim; i++)
            {
                b[i] = schwerpunktDifferenz[i];
            }

            var loesung = LoeseSymmetrisch(a, b, token);
            if (loesung is null)
            {
                return null;
            }

            var r = new float[dim];
            for (int i = 0; i < dim; i++)
            {
                r[i] = (float)loesung[i];
            }

            return r;
        }

        /// <summary>
        /// Löst <c>A x = b</c> für eine symmetrische, positiv definite Matrix über die
        /// Cholesky-Zerlegung. <paramref name="a"/> enthält nur die untere Dreieckshälfte
        /// und wird dabei überschrieben.
        ///
        /// <c>null</c> heisst „nicht positiv definit" – kann bei einer Dämpfung über null
        /// eigentlich nicht vorkommen, wird aber abgefangen statt auf gut Glück
        /// weitergerechnet.
        /// </summary>
        private static double[]? LoeseSymmetrisch(double[][] a, double[] b, CancellationToken token)
        {
            int n = b.Length;

            for (int i = 0; i < n; i++)
            {
                token.ThrowIfCancellationRequested();

                var ai = a[i];
                for (int j = 0; j <= i; j++)
                {
                    var aj = a[j];
                    double s = ai[j];
                    for (int k = 0; k < j; k++)
                    {
                        s -= ai[k] * aj[k];
                    }

                    if (i == j)
                    {
                        if (s <= 1e-12)
                        {
                            return null;
                        }

                        ai[i] = Math.Sqrt(s);
                    }
                    else
                    {
                        ai[j] = s / aj[j];
                    }
                }
            }

            // Vorwärts einsetzen: L y = b
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                var ai = a[i];
                double s = b[i];
                for (int k = 0; k < i; k++)
                {
                    s -= ai[k] * y[k];
                }

                y[i] = s / ai[i];
            }

            // Rückwärts einsetzen: Lᵀ x = y
            var x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                double s = y[i];
                for (int k = i + 1; k < n; k++)
                {
                    s -= a[k][i] * x[k];
                }

                x[i] = s / a[i][i];
            }

            return x;
        }

        /// <summary>
        /// Bringt eine Richtung auf Länge 1. <c>null</c> bleibt <c>null</c>, ebenso eine
        /// Richtung ohne Länge – beides heisst „diese Quelle steht nicht zur Verfügung".
        /// </summary>
        private static float[]? Normiert(float[]? v)
        {
            if (v is null || v.Length == 0)
            {
                return null;
            }

            double laenge = 0;
            for (int i = 0; i < v.Length; i++)
            {
                laenge += v[i] * (double)v[i];
            }

            laenge = Math.Sqrt(laenge);
            if (laenge <= 1e-9)
            {
                return null;
            }

            var r = new float[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                r[i] = (float)(v[i] / laenge);
            }

            return r;
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
