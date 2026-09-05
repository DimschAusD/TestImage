using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TestImage.Bildersuche
{
    /// <summary>Befund zu einem einzelnen Bild.</summary>
    public sealed class WasserzeichenBefund
    {
        public string Pfad { get; set; } = string.Empty;

        /// <summary>Übereinstimmung mit der gelernten Maske, −1 … +1.</summary>
        public float Aehnlichkeit { get; set; }

        /// <summary>True, wenn die Übereinstimmung über der Schwelle liegt.</summary>
        public bool HatSichtbares { get; set; }

        /// <summary>Name des Musters, das am besten passte. Leer, wenn keines passte.</summary>
        public string MaskenName { get; set; } = string.Empty;

        /// <summary>
        /// Schwelle, gegen die verglichen wurde — die des jeweiligen Musters. Ohne sie
        /// wäre die Ähnlichkeit nicht einzuordnen, da jedes Muster seine eigene hat.
        /// </summary>
        public float VerwendeteSchwelle { get; set; }

        /// <summary>Gefundene Metadaten-Markierungen (Autor, Copyright, XMP, C2PA …).</summary>
        public List<string> MetadatenHinweise { get; set; } = new();

        public bool HatMetadaten => MetadatenHinweise.Count > 0;

        public bool HatIrgendetwas => HatSichtbares || HatMetadaten;

        /// <summary>Kurzbegründung für den Tooltip am Badge.</summary>
        public string Begruendung()
        {
            var teile = new List<string>();

            if (HatSichtbares)
            {
                string muster = string.IsNullOrWhiteSpace(MaskenName) ? "Wasserzeichen" : MaskenName;
                teile.Add($"Sichtbares Wasserzeichen erkannt – Muster „{muster}“ ({Aehnlichkeit * 100f:F0} % Übereinstimmung)");
            }

            teile.AddRange(MetadatenHinweise);

            return teile.Count == 0 ? "Keine Markierung gefunden" : string.Join("\n", teile);
        }
    }

    /// <summary>
    /// Prüft Bilder auf Wasserzeichen — sichtbar aufgeprägte über die gelernte
    /// <see cref="WasserzeichenMaske"/>, unsichtbare über <see cref="MetadatenPruefer"/>.
    /// Die Befunde liegen als Seitendatei neben dem CLIP-Index, damit das Fremdprojekt
    /// ImageMatching.Core unverändert bleibt.
    /// </summary>
    internal static class WasserzeichenService
    {
        /// <summary>Befunddatei je Bildordner.</summary>
        internal const string CacheDateiName = ".bildwasserzeichen.json";

        /// <summary>
        /// Sammlung der gelernten Muster, gilt anwendungsweit (nicht je Ordner).
        /// Mehrzahl, weil ein Anbieter durchaus mehrere Zeichentypen verwendet —
        /// DeviantArt etwa mindestens drei.
        /// </summary>
        internal const string MaskenDateiName = "wasserzeichen.masken.json";

        /// <summary>Einzelmaske der ersten Fassung. Wird beim ersten Laden übernommen.</summary>
        internal const string MaskenDateiNameAlt = "wasserzeichen.maske.json";

        /// <summary>
        /// Ab dieser Korrelation gilt ein Wasserzeichen als erkannt.
        ///
        /// Eingemessen an 32 DeviantArt-Bildern gegen 120 unmarkierte: Treffer lagen
        /// bei 0,154 … 0,279, unmarkierte Bilder bei −0,071 … 0,050. Der Wert liegt
        /// mittig in dieser Lücke und trennte beide Mengen fehlerfrei.
        /// </summary>
        internal const float Schwelle = 0.10f;

        private static List<WasserzeichenMaske>? _masken;

        internal static string MaskenPfad =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MaskenDateiName);

        private static string MaskenPfadAlt =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MaskenDateiNameAlt);

        /// <summary>Alle gelernten Muster. Ohne mindestens eines greift nur die Metadatenprüfung.</summary>
        internal static IReadOnlyList<WasserzeichenMaske> Masken => HoleMasken();

        /// <summary>Mindestens ein Muster vorhanden?</summary>
        internal static bool MaskeVorhanden => HoleMasken().Count > 0;

        /// <summary>
        /// Mindestens ein Muster, das beim Prüfen herangezogen wird. Ohne das wird zwar
        /// geprüft, aber jedes Bild fällt durch.
        /// </summary>
        internal static bool VerwendbaresMusterVorhanden => HoleMasken().Any(m => m.IstVerwendbar);

        /// <summary>Mindestens ein Muster, das die Belegt-Grenze erreicht.</summary>
        internal static bool BelegtesMusterVorhanden => HoleMasken().Any(m => m.IstBelegt);

        /// <summary>Namen der Muster, die beim Prüfen übersprungen werden — nur die nie nachgemessenen.</summary>
        internal static IReadOnlyList<string> UebersprungeneMuster =>
            HoleMasken().Where(m => !m.IstVerwendbar).Select(m => m.Name).ToList();

        /// <summary>
        /// Namen der Muster, die zwar mitprüfen, aber die Belegt-Grenze noch nicht
        /// erreichen. Sie finden weniger und gelegentlich daneben — das gehört gesagt,
        /// ohne sie deshalb abzuschalten.
        /// </summary>
        internal static IReadOnlyList<string> SchwacheMuster =>
            HoleMasken().Where(m => m.IstVerwendbar && !m.IstBelegt).Select(m => m.Name).ToList();

        /// <summary>Summe der Bilder, aus denen die Muster gelernt wurden.</summary>
        internal static int MaskenGrundmenge => HoleMasken().Sum(m => m.Grundmenge);

        private static List<WasserzeichenMaske> HoleMasken()
        {
            if (_masken is null)
            {
                _masken = LadeMasken();

                // Einzelmaske der ersten Fassung übernehmen, damit ein bereits
                // gelerntes Muster beim Umstieg nicht verlorengeht.
                if (_masken.Count == 0 && File.Exists(MaskenPfadAlt))
                {
                    var alt = WasserzeichenMaske.Laden(MaskenPfadAlt);
                    if (alt is not null)
                    {
                        alt.Name = string.IsNullOrWhiteSpace(alt.Name) ? "Muster 1" : alt.Name;
                        _masken.Add(alt);
                        SpeichereMasken(_masken);
                    }
                }
            }

            return _masken;
        }

        /// <summary>Erzwingt das Neuladen, nachdem sich die Muster geändert haben.</summary>
        internal static void MaskeVergessen() => _masken = null;

        #region Muster lernen und verwalten

        /// <summary>
        /// Lernt ein Muster aus einem Ordner, in dem <b>alle</b> Bilder denselben
        /// Zeichentyp tragen, und legt es unter <paramref name="name"/> in der Sammlung
        /// ab. Ein gleichnamiges Muster wird ersetzt — nochmal lernen heisst auffrischen.
        /// </summary>
        /// <returns>Anzahl der verwendeten Bilder, 0 bei Misserfolg.</returns>
        /// <param name="bereich">
        /// Stelle im Bild, an der das Zeichen sitzt. Wird im Muster gespeichert und beim
        /// Prüfen wieder verwendet — ein Zeichen oben rechts findet man nicht, wenn man
        /// in der Bildmitte sucht.
        /// </param>
        internal static async Task<int> LerneMaskeAsync(
            string ordner,
            string name,
            WasserzeichenBereich bereich,
            IProgress<(int Erledigt, int Gesamt)>? fortschritt,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
                return 0;

            var dateien = SammleBilder(ordner);
            if (dateien.Count < 5)
                return 0;

            string grundName = string.IsNullOrWhiteSpace(name) ? "Muster" : name.Trim();

            var ergebnis = await Task.Run(
                () => LerneUndTeile(dateien, bereich, fortschritt, token), token).ConfigureAwait(false);

            if (ergebnis.Count == 0)
                return 0;

            var alle = HoleMasken();

            // Gleichnamige ersetzen – auch die nummerierten aus einem früheren Lauf.
            alle.RemoveAll(m => string.Equals(m.Name, grundName, StringComparison.OrdinalIgnoreCase)
                             || m.Name.StartsWith(grundName + " ", StringComparison.OrdinalIgnoreCase));

            if (ergebnis.Count == 1)
            {
                ergebnis[0].Name = grundName;
                LetzteLernMeldung = string.Empty;
            }
            else
            {
                for (int i = 0; i < ergebnis.Count; i++)
                    ergebnis[i].Name = $"{grundName} {i + 1}";

                LetzteLernMeldung =
                    $" Der Ordner enthielt zweierlei Zeichen – aufgeteilt in "
                    + string.Join(" und ", ergebnis.Select(m => $"„{m.Name}“ ({m.Grundmenge})"))
                    + ".";
            }

            alle.AddRange(ergebnis);
            SpeichereMasken(alle);

            return ergebnis.Sum(m => m.Grundmenge);
        }

        /// <summary>Zusatz zur Statusmeldung des letzten Lernvorgangs; leer, wenn nichts zu sagen war.</summary>
        internal static string LetzteLernMeldung { get; private set; } = string.Empty;

        /// <summary>
        /// Anteil der Bilder eines Ordners, die zu einem vorhandenen Muster passen müssen,
        /// damit der Ordner als <b>dasselbe Zeichen</b> gilt und dort dazugelernt wird.
        ///
        /// Nicht alle, sondern eine deutliche Mehrheit: In einem echten Ordner sind
        /// erfahrungsgemäss einzelne Bilder ohne Zeichen dabei, und ein einzelnes davon
        /// darf die Zuordnung nicht kippen. Umgekehrt ist ein fremder Zeichentyp weit von
        /// diesem Wert entfernt — er liegt beim Prüfen nahe null.
        /// </summary>
        private const double ZuordnungsAnteil = 0.6;

        /// <summary>
        /// Lernt aus einem Ordner und ordnet ihn dabei selbst ein: Passen seine Bilder zu
        /// einem bereits gelernten Muster, wird dieses <b>ergänzt</b>; sonst entsteht ein
        /// neues.
        ///
        /// Das ist der Weg zum gemeinsamen Muster. Was in allen Ordnern gleich aussieht,
        /// behält sein Gewicht; was je Ordner wechselt, verliert es. Die Prüfung davor ist
        /// dabei kein Beiwerk: Ein Anbieter verwendet durchaus mehrere Zeichentypen ohne
        /// Gemeinsamkeit — DeviantArt allein drei. Würde man die blind zusammenrechnen,
        /// entstünde ein Muster, das keines davon mehr erkennt.
        /// </summary>
        /// <returns>
        /// Name des betroffenen Musters, seine neue Grundmenge, ob es neu angelegt wurde,
        /// sowie der Stand <b>davor</b> — Bilderzahl und Trennschärfe des ergänzten
        /// Musters. Beim neuen Muster sind die beiden 0 und <c>null</c>. Name leer und
        /// Anzahl 0 heisst: zu wenige oder unlesbare Bilder.
        /// </returns>
        internal static async Task<(string Name, int Bilder, bool IstNeu,
                                    int VorherBilder, float? VorherTrennschaerfe)>
            ErgaenzeOderLerneAsync(
                string ordner,
                string name,
                WasserzeichenBereich bereich,
                IProgress<(int Erledigt, int Gesamt)>? fortschritt,
                CancellationToken token)
        {
            // Zurücksetzen, sonst hinge der Hinweis eines früheren Fehlversuchs auch an
            // einem Lauf, der gar nichts zu speichern hatte.
            LetzterSpeicherFehler = string.Empty;

            if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
                return (string.Empty, 0, false, 0, null);

            var dateien = SammleBilder(ordner);
            if (dateien.Count < 5)
                return (string.Empty, 0, false, 0, null);

            // Grund, falls eine mögliche Ergänzung abgelehnt wurde. Wird der Meldung
            // angehängt — sonst taucht unerklärt ein zweites Muster auf.
            string abgelehnt = string.Empty;

            // Muster aus Dateien vor dieser Erweiterung tragen keine Summen und können
            // nicht fortgeschrieben werden. Sie stehen hier gar nicht erst zur Wahl.
            var erweiterbare = HoleMasken().Where(m => m.KannErweitertWerden).ToList();

            if (erweiterbare.Count > 0)
            {
                var (beste, felder) = await Task.Run(
                    () => SucheBestesMuster(dateien, erweiterbare, fortschritt, token), token)
                    .ConfigureAwait(false);

                if (beste is not null && felder is not null)
                {
                    var erweitert = beste.Erweitere(felder);

                    // Die Nutzenprüfung sitzt hier, unmittelbar vor dem Speichern, und
                    // damit im gemeinsamen Weg beider Zweige. Lohnt es nicht, entsteht
                    // unten ein eigenes Muster — der Ordner geht also nicht verloren, er
                    // reisst nur kein anderes mit herunter.
                    if (erweitert is not null && ErgaenzungLohnt(beste, erweitert, out abgelehnt))
                    {
                        var alle = HoleMasken();
                        int platz = alle.IndexOf(beste);
                        if (platz >= 0)
                            alle[platz] = erweitert;
                        else
                            alle.Add(erweitert);

                        SpeichereMasken(alle);

                        LetzteLernMeldung = string.Empty;

                        return (erweitert.Name, erweitert.Grundmenge, false,
                                beste.Grundmenge, beste.Trennschaerfe);
                    }
                }
            }

            int anzahl = await LerneMaskeAsync(ordner, name, bereich, fortschritt, token)
                .ConfigureAwait(false);

            // Nach LerneMaskeAsync anhängen, nicht davor: Die Methode setzt die Meldung
            // selbst (Aufteilung in zwei Muster) und würde einen früheren Text überschreiben.
            if (abgelehnt.Length > 0)
                LetzteLernMeldung = (" " + abgelehnt + " Deshalb ein eigenes Muster."
                                     + LetzteLernMeldung).TrimEnd();

            return anzahl > 0 ? (name, anzahl, true, 0, null) : (string.Empty, 0, false, 0, null);
        }

        /// <summary>
        /// Sucht das Muster, zu dem die Bilder des Ordners am besten passen — und gibt die
        /// dabei berechneten Merkmalsfelder gleich mit zurück, weil das Dazulernen sie
        /// unmittelbar wieder braucht.
        ///
        /// Die Felder hängen an Vorverarbeitung und Bildstelle des jeweiligen Musters, ein
        /// Zeichen oben rechts wird in der Bildmitte nicht gefunden. Deshalb je Kombination
        /// aus beidem ein eigener Durchgang — bei gleichartigen Mustern also nur einer.
        /// </summary>
        private static (WasserzeichenMaske? Beste, List<float[]>? Felder) SucheBestesMuster(
            List<string> dateien,
            List<WasserzeichenMaske> masken,
            IProgress<(int Erledigt, int Gesamt)>? fortschritt,
            CancellationToken token)
        {
            WasserzeichenMaske? beste = null;
            List<float[]>? besteFelder = null;
            double besterAnteil = 0;

            // Zweite Wahl: ein noch nicht belegtes Muster, das durch diesen Ordner
            // messbar besser wird. Es braucht einen eigenen Weg, weil seine Schwelle
            // nichts taugt — sie liegt auf der Untergrenze, und ob 60 % des Ordners sie
            // reissen, ist dort Zufall. Ohne das entstünde bei jedem Ordner ein neues
            // schwaches Muster, und genau die Bilder, die zusammengehören, kämen nie
            // zusammen — obwohl das Sammeln bei einem schwachen Zeichen der einzige Weg
            // ist (die Trennschärfe wächst mit √n).
            WasserzeichenMaske? kandidat = null;
            List<float[]>? kandidatFelder = null;
            double besterZuwachs = 0;

            foreach (var gruppe in masken.GroupBy(m => (m.Modus, m.Bereich)))
            {
                var felder = new List<float[]>(dateien.Count);

                for (int i = 0; i < dateien.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var feld = WasserzeichenMaske.Merkmalsfeld(
                        dateien[i], gruppe.Key.Modus, gruppe.Key.Bereich);

                    if (feld is not null)
                        felder.Add(feld);

                    fortschritt?.Report((i + 1, dateien.Count));
                }

                if (felder.Count < 5)
                    continue;

                foreach (var maske in gruppe)
                {
                    if (maske.IstBelegt)
                    {
                        float schwelle = maske.Schwelle > 0f ? maske.Schwelle : Schwelle;
                        int passend = felder.Count(f => maske.Pruefe(f) >= schwelle);
                        double anteil = (double)passend / felder.Count;

                        if (anteil >= ZuordnungsAnteil && anteil > besterAnteil)
                        {
                            besterAnteil = anteil;
                            beste = maske;
                            besteFelder = felder;
                        }

                        continue;
                    }

                    double erreicht = BewerteErgaenzung(maske, felder);

                    if (erreicht > besterZuwachs)
                    {
                        besterZuwachs = erreicht;
                        kandidat = maske;
                        kandidatFelder = felder;
                    }
                }
            }

            // Ein belegtes Muster hat Vorrang: Dort ist bewiesen, dass der Ordner
            // dasselbe Zeichen trägt, nicht nur, dass er dem Muster guttut.
            return beste is not null ? (beste, besteFelder) : (kandidat, kandidatFelder);
        }

        /// <summary>
        /// Trennschärfe, die ein noch schwaches Muster mit diesem Ordner erreicht —
        /// 0, wenn sich gar kein Muster bilden lässt. <b>Nur zur Auswahl</b>, ob dieser
        /// Ordner zu diesem Muster gehört; ob die Ergänzung sich lohnt, entscheidet
        /// danach <see cref="ErgaenzungLohnt"/> für alle Wege gleich.
        /// </summary>
        private static double BewerteErgaenzung(WasserzeichenMaske maske, List<float[]> felder)
        {
            if (!maske.KannErweitertWerden || maske.Trennschaerfe is not { } alt || alt <= 0f)
                return 0;

            var probe = maske.Erweitere(felder);
            return probe?.Trennschaerfe ?? 0;
        }

        /// <summary>
        /// Lohnt sich die Ergänzung, oder verwässert sie das Muster?
        ///
        /// Diese Prüfung gilt für <b>jede</b> Ergänzung. Sie stand vorher nur im Zweig
        /// der schwachen Muster, und damit war ausgerechnet dort keine, wo am meisten zu
        /// verlieren ist: Belegte Muster wurden allein über die 60-%-Regel zugeordnet und
        /// anschliessend ungeprüft gespeichert. Die 60-%-Regel beantwortet aber „gehört
        /// der Ordner zu diesem Zeichen?" — nicht „wird das Muster dadurch besser?". Ein
        /// zugehöriger Ordner kann trotzdem überwiegend Rauschen mitbringen. Genau so ist
        /// ein Muster von 0,288 (n=38) über mehrere je einzeln unauffällige Schritte auf
        /// 0,155 (n=212) heruntergewandert.
        ///
        /// Verlangt wird nur, dass die Signalrate nicht deutlich <b>fällt</b>. Das klingt
        /// milde, trennt aber sauber, und ein strengeres Mass wäre nachweislich falsch:
        ///
        /// Ein Ordner desselben Künstlers bringt den vollen Zuwachs (16 + 19 Bilder:
        /// 0,041 → 0,058 bei 0,061 Erwartung). Ein Ordner eines <i>anderen</i> Künstlers
        /// mit demselben Zeichen bringt zunächst fast nichts (35 + 29 Bilder:
        /// 0,058 → 0,059), weil der Künstlerschriftzug des ersten dabei wegmittelt,
        /// während erst das gemeinsame Logo übrig bleibt. Es lohnt sich trotzdem:
        /// Über sechs Ordner von vier Künstlern hinweg wuchs ein gemeinsames Muster
        /// durchgehend weiter, 0,037 bei 16 Bildern auf 0,077 bei 118 — nur mit
        /// r/√n ≈ 0,0071 statt 0,010. Eine Regel, die den vollen Zuwachs verlangt, würde
        /// genau diesen Weg abschneiden — und er ist der einzige, wenn je Künstler nur
        /// rund 20 Bilder vorliegen.
        ///
        /// Ein fremdes Zeichen fällt dagegen klar durch: derselbe Versuch mit dem
        /// Eckbanner-Ordner ergab 0,041 → 0,012, also ein Fünftel. Zwischen einem
        /// Fünftel und „bleibt ungefähr stehen" liegt genug Platz.
        /// </summary>
        private static bool ErgaenzungLohnt(
            WasserzeichenMaske alt, WasserzeichenMaske erweitert, out string begruendung)
        {
            begruendung = string.Empty;

            // Nie nachgemessen: Es gibt nichts zu vergleichen, also auch nichts zu
            // verhindern. Solche Muster müssen ohnehin einmal neu gelernt werden.
            if (alt.Trennschaerfe is not { } vorher || vorher <= 0f
                || erweitert.Trennschaerfe is not { } nachher)
                return true;

            // Ratsche: Ein belegtes Muster darf durch eine Ergänzung nicht unbelegt
            // werden. Sonst kostet ein einziger Ordner die Erkennung, die vorher
            // nachweislich da war.
            //
            // Diese Bedingung stand schon einmal hier, aber innerhalb der Bewertung, die
            // nur für nicht belegte Muster lief — sie konnte also nie zutreffen.
            if (alt.IstBelegt && !erweitert.IstBelegt)
            {
                begruendung = $"Das Muster wäre dadurch unter die Belegt-Grenze gefallen "
                            + $"({vorher * 100f:0.0} % → {nachher * 100f:0.0} %).";
                return false;
            }

            // Verglichen wird die Signalrate r/√n, nicht der rohe Wert. Der Wert muss bei
            // gleichem Zeichen ohnehin mit √n wachsen; ein Ordner, der die Bilderzahl
            // verdreifacht und den Wert nur hält, hat nichts beigetragen, sondern
            // verdünnt. Die Rate ist gegen die Bilderzahl unempfindlich und trennt
            // sauber: gemessen an sieben Ergänzungen lag sie bei gleichem Zeichen
            // zwischen 0,76 und 1,16 der bisherigen, bei fremdem Zeichen bei 0,48 und 0,51.
            double rateAlt = vorher / Math.Sqrt(alt.Grundmenge);
            double rateNeu = nachher / Math.Sqrt(erweitert.Grundmenge);

            if (rateNeu >= rateAlt * ErgaenzungMindestanteil)
                return true;

            begruendung = $"Der Ordner hätte das Muster verwässert – Signalrate {rateNeu / rateAlt:0.00}× "
                        + $"(nötig {ErgaenzungMindestanteil:0.00}×), Trennschärfe "
                        + $"{vorher * 100f:0.0} % → {nachher * 100f:0.0} % bei "
                        + $"{alt.Grundmenge} → {erweitert.Grundmenge} Bildern.";
            return false;
        }

        /// <summary>
        /// Anteil der bisherigen Signalrate, den ein ergänzter Ordner mindestens halten
        /// muss. Gemessen: gleiches Zeichen 0,76 … 1,16, fremdes Zeichen 0,48 … 0,51 —
        /// dazwischen ist reichlich Platz.
        /// </summary>
        private const double ErgaenzungMindestanteil = 0.75;

        /// <summary>
        /// Mindestabstand zwischen eigener und fremder Übereinstimmung, damit eine
        /// Aufteilung als bewiesen gilt.
        ///
        /// Gemessen an einem Ordner mit zwei Zeichen: 0,214 gegen 0,003 und 0,196 gegen
        /// 0,003 — der Abstand lag also bei rund 0,20. Bei sortenreinem Material passen
        /// beide Hälften auf beides, der Abstand geht gegen null. Ein Zehntel ist deshalb
        /// weit genug von beiden Fällen entfernt.
        /// </summary>
        private const float TrennAbstand = 0.10f;

        /// <summary>
        /// Lernt aus dem Ordner und teilt auf, wenn darin zweierlei Zeichen stecken.
        ///
        /// Warum nicht einfach an der grössten Lücke schneiden: Die Messung an echtem
        /// Material zeigte nur eine Lücke von 1,0 Prozentpunkten in einem einzigen
        /// breiten Berg — kein verlässliches Kriterium. Der Schnitt wird deshalb nur
        /// versuchsweise gemacht und danach <b>überprüft</b>: Passt jede Hälfte deutlich
        /// besser zu ihrem eigenen Muster als zum anderen, war es wirklich zweierlei.
        /// Sonst bleibt es beim einen Muster.
        /// </summary>
        private static List<WasserzeichenMaske> LerneUndTeile(
            List<string> dateien,
            WasserzeichenBereich bereich,
            IProgress<(int Erledigt, int Gesamt)>? fortschritt,
            CancellationToken token)
        {
            var leer = new List<WasserzeichenMaske>();

            // Bei „alle Bereiche" zuerst die Stelle finden – danach steht sie fest.
            if (bereich == WasserzeichenBereich.Alle)
            {
                var beste = LerneBesteStelle(dateien, fortschritt, token);
                if (beste is null) return leer;
                bereich = beste.Bereich;
            }

            // Merkmalsfelder einmal berechnen. Alles Weitere rechnet nur noch darauf –
            // kein Bild wird ein zweites Mal von der Platte geholt.
            var felder = new List<float[]>(dateien.Count);
            for (int i = 0; i < dateien.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var feld = WasserzeichenMaske.Merkmalsfeld(
                    dateien[i], WasserzeichenVorverarbeitung.Hochpass, bereich);

                if (feld is not null)
                    felder.Add(feld);

                fortschritt?.Report((i + 1, dateien.Count));
            }

            var basis = WasserzeichenMaske.LerneAusFeldern(
                felder, WasserzeichenVorverarbeitung.Hochpass, bereich);

            if (basis is null) return leer;

            var geteilt = VersucheAufteilung(basis, felder, bereich, token);
            return geteilt ?? new List<WasserzeichenMaske> { basis };
        }

        /// <summary>
        /// Versucht die Aufteilung und gibt sie nur zurück, wenn die Kreuzprüfung sie
        /// bestätigt. <c>null</c> heisst: eine Sorte, beim Grundmuster bleiben.
        /// </summary>
        private static List<WasserzeichenMaske>? VersucheAufteilung(
            WasserzeichenMaske basis,
            List<float[]> felder,
            WasserzeichenBereich bereich,
            CancellationToken token)
        {
            if (felder.Count < 12)
                return null;   // zu wenig, um zwei brauchbare Muster daraus zu machen

            var bewertet = felder
                .Select(f => (Feld: f, Wert: basis.Pruefe(f)))
                .OrderBy(p => p.Wert)
                .ToList();

            // Grösste Lücke im mittleren Bereich – Ausreisser an den Rändern zählen nicht.
            int von = (int)(bewertet.Count * 0.10);
            int bis = (int)(bewertet.Count * 0.90);

            float besteLuecke = 0;
            int schnitt = -1;

            for (int i = von; i < bis && i + 1 < bewertet.Count; i++)
            {
                float luecke = bewertet[i + 1].Wert - bewertet[i].Wert;
                if (luecke > besteLuecke) { besteLuecke = luecke; schnitt = i; }
            }

            if (schnitt < 0) return null;

            var unten = bewertet.Take(schnitt + 1).Select(p => p.Feld).ToList();
            var oben = bewertet.Skip(schnitt + 1).Select(p => p.Feld).ToList();

            if (unten.Count < 5 || oben.Count < 5)
                return null;

            token.ThrowIfCancellationRequested();

            var maskeA = WasserzeichenMaske.LerneAusFeldern(oben, WasserzeichenVorverarbeitung.Hochpass, bereich);
            var maskeB = WasserzeichenMaske.LerneAusFeldern(unten, WasserzeichenVorverarbeitung.Hochpass, bereich);

            if (maskeA is null || maskeB is null)
                return null;

            // Kreuzprüfung: Jede Hälfte muss zu ihrem eigenen Muster deutlich besser
            // passen als zum anderen. Bei sortenreinem Material passt beides auf beides,
            // die Abstände gehen gegen null und die Aufteilung wird verworfen.
            //
            // Der eigene Wert wird dabei ohne Selbstbewertung gemessen — siehe
            // EigenwertOhneSelbstbewertung. Vorher stand hier
            // oben.Average(f => maskeA.Pruefe(f)), und das war der Fehler: Ein Muster aus
            // wenigen Bildern hat deren Motivrauschen eingebacken und erkennt genau diese
            // Bilder wieder. Gemessen an 19 sortenreinen DeviantArt-Bildern: eigen 0,32
            // gegen fremd 0,007 — „bewiesen", obwohl alle dasselbe Zeichen tragen. Der
            // Ordner zerfiel in zwei Muster mit Schwellen von 0,33 und 0,21, die danach
            // nichts mehr fanden. Mit der Korrektur bleiben 0,04 und −0,01 übrig, und es
            // bleibt richtigerweise bei einem Muster.
            double aFremd = oben.Average(f => (double)maskeB.Pruefe(f));
            double bFremd = unten.Average(f => (double)maskeA.Pruefe(f));
            double aEigen = EigenwertOhneSelbstbewertung(oben, bereich);
            double bEigen = EigenwertOhneSelbstbewertung(unten, bereich);

            bool bewiesen = (aEigen - aFremd) >= TrennAbstand
                         && (bEigen - bFremd) >= TrennAbstand;

            return bewiesen ? new List<WasserzeichenMaske> { maskeA, maskeB } : null;
        }

        /// <summary>
        /// Wie gut eine Hälfte zu ihrem eigenen Muster passt — gemessen an Bildern, die
        /// beim Lernen dieses Musters <b>nicht</b> dabei waren (leave-one-out).
        ///
        /// Der Gegenwert („fremd") braucht das nicht: Das andere Muster kennt diese Bilder
        /// ohnehin nicht. Erst dadurch werden beide Zahlen vergleichbar.
        /// </summary>
        /// <remarks>
        /// Nicht jedes Bild einzeln, sondern höchstens <see cref="LooStichprobe"/> gleichmässig
        /// verteilte: Jede Auslassung kostet ein neu gemitteltes Muster, das wäre sonst
        /// quadratisch in der Bilderzahl. Ein Dutzend Stichproben genügt für einen
        /// Mittelwert, der nur gegen eine Schwelle verglichen wird.
        ///
        /// Bleiben nach dem Auslassen weniger als fünf Bilder, entsteht kein Muster
        /// (<c>LerneAusFeldern</c> liefert dann null). Der Rückgabewert 0 heisst in dem
        /// Fall „nicht belegbar" – die Aufteilung unterbleibt, und das ist bei einer
        /// Handvoll Bildern auch richtig.
        /// </remarks>
        private static double EigenwertOhneSelbstbewertung(
            List<float[]> haelfte, WasserzeichenBereich bereich)
        {
            int schritt = Math.Max(1, haelfte.Count / LooStichprobe);

            double summe = 0;
            int gezaehlt = 0;
            var ohne = new List<float[]>(haelfte.Count);

            for (int k = 0; k < haelfte.Count; k += schritt)
            {
                ohne.Clear();
                for (int i = 0; i < haelfte.Count; i++)
                {
                    if (i != k)
                        ohne.Add(haelfte[i]);
                }

                var maske = WasserzeichenMaske.LerneAusFeldern(
                    ohne, WasserzeichenVorverarbeitung.Hochpass, bereich);

                if (maske is null)
                    continue;

                summe += maske.Pruefe(haelfte[k]);
                gezaehlt++;
            }

            return gezaehlt == 0 ? 0 : summe / gezaehlt;
        }

        /// <summary>Höchstzahl ausgelassener Bilder je Hälfte – hält die Kreuzprüfung linear.</summary>
        private const int LooStichprobe = 12;

        /// <summary>
        /// Lernt an allen fünf Stellen und behält die mit dem deutlichsten Muster.
        ///
        /// Der Vergleich läuft über <see cref="WasserzeichenMaske.MusterStaerke"/>: Wo
        /// wirklich ein Zeichen liegt, bleibt nach dem Mitteln Struktur übrig; eine leere
        /// Ecke wird flach. Am Ende trägt die Maske die gefundene Stelle, die Prüfung
        /// kostet also nicht mehr als sonst.
        /// </summary>
        private static WasserzeichenMaske? LerneBesteStelle(
            List<string> dateien,
            IProgress<(int Erledigt, int Gesamt)>? fortschritt,
            CancellationToken token)
        {
            var stellen = new[]
            {
                WasserzeichenBereich.Mitte,
                WasserzeichenBereich.ObenLinks,
                WasserzeichenBereich.ObenRechts,
                WasserzeichenBereich.UntenLinks,
                WasserzeichenBereich.UntenRechts
            };

            WasserzeichenMaske? beste = null;
            double besteStaerke = double.NegativeInfinity;

            int gesamt = dateien.Count * stellen.Length;

            for (int i = 0; i < stellen.Length; i++)
            {
                token.ThrowIfCancellationRequested();

                // Fortschritt über alle Durchgänge hinweg zählen, sonst spränge die
                // Anzeige fünfmal auf null zurück.
                int versatz = i * dateien.Count;
                var teilFortschritt = new Progress<(int Erledigt, int Gesamt)>(
                    p => fortschritt?.Report((versatz + p.Erledigt, gesamt)));

                var kandidat = WasserzeichenMaske.Lerne(
                    dateien, teilFortschritt, token,
                    WasserzeichenVorverarbeitung.Hochpass, stellen[i]);

                if (kandidat is null)
                    continue;

                double staerke = kandidat.MusterStaerke;
                if (staerke > besteStaerke)
                {
                    besteStaerke = staerke;
                    beste = kandidat;
                }
            }

            return beste;
        }

        /// <summary>Entfernt ein Muster aus der Sammlung.</summary>
        internal static bool EntferneMaske(string name)
        {
            var alle = HoleMasken();
            if (alle.RemoveAll(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) == 0)
                return false;

            SpeichereMasken(alle);
            return true;
        }

        private static List<WasserzeichenMaske> LadeMasken()
        {
            var liste = new List<WasserzeichenMaske>();

            try
            {
                if (!File.Exists(MaskenPfad))
                    return liste;

                using var fs = File.OpenRead(MaskenPfad);
                var daten = JsonSerializer.Deserialize<List<WasserzeichenMaske.MaskenDatei>>(fs);

                if (daten is null)
                    return liste;

                foreach (var d in daten)
                {
                    var maske = WasserzeichenMaske.AusDatensatz(d);
                    if (maske is not null)
                        liste.Add(maske);
                }
            }
            catch
            {
                // beschädigte Datei → wie „noch nichts gelernt" behandeln
            }

            return liste;
        }

        /// <summary>
        /// Fehlermeldung des letzten Speicherversuchs; leer, wenn es geklappt hat.
        ///
        /// Vorher wurde der Fehler nur verschluckt. Die Ansicht meldete dann „Muster
        /// gelernt", und beim nächsten Start war es weg — ohne dass irgendwo gestanden
        /// hätte, warum. Ein Lernlauf über hunderte Bilder ist zu teuer, um so zu enden.
        /// </summary>
        internal static string LetzterSpeicherFehler { get; private set; } = string.Empty;

        /// <summary>
        /// Schreibt die Sammlung. <c>false</c>, wenn das nicht gelang — dann gelten die
        /// Muster nur noch für diese Sitzung.
        ///
        /// Geschrieben wird über eine Nebendatei, die erst zum Schluss an die Stelle der
        /// alten tritt. Direkt in die Zieldatei zu schreiben hiess: Wer den Vorgang
        /// unterbricht — Absturz, Stromausfall, volle Platte —, hat eine halbe Datei, und
        /// die zählt beim Laden als „nichts gelernt". Bei rund 2 MB je Muster ist das
        /// Fenster nicht theoretisch, und verloren wäre die Arbeit aller Lernläufe.
        /// </summary>
        private static bool SpeichereMasken(List<WasserzeichenMaske> masken)
        {
            string neben = MaskenPfad + ".neu";

            try
            {
                using (var fs = File.Create(neben))
                {
                    JsonSerializer.Serialize(fs, masken.Select(m => m.AlsDatensatz()).ToList());
                    fs.Flush(true);   // auf die Platte, nicht nur in den Zwischenspeicher
                }

                if (File.Exists(MaskenPfad))
                    File.Replace(neben, MaskenPfad, null);
                else
                    File.Move(neben, MaskenPfad);

                LetzterSpeicherFehler = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                LetzterSpeicherFehler = ex.Message;

                try { if (File.Exists(neben)) File.Delete(neben); }
                catch { /* Rest der Nebendatei stört nicht, sie wird überschrieben */ }

                return false;
            }
        }

        #endregion

        #region Ordner prüfen

        /// <summary>
        /// Prüft alle Bilder eines Ordners und schreibt die Befunde in die Seitendatei.
        /// Wird beim Indexieren mitgerufen.
        /// </summary>
        internal static async Task<Dictionary<string, WasserzeichenBefund>> PruefeOrdnerAsync(
            string ordner,
            IProgress<(int Erledigt, int Gesamt)>? fortschritt,
            CancellationToken token)
        {
            var ergebnis = new Dictionary<string, WasserzeichenBefund>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
                return ergebnis;

            var dateien = SammleBilder(ordner);
            if (dateien.Count == 0)
                return ergebnis;

            // Alle verwendbaren Muster gehen in den Lauf — auch die schwachen. Sind es
            // keine, bleibt die Liste leer und PruefeDatei lädt gar kein Bild erst; sonst
            // liefe ein vollständiger Dekodierdurchlauf über den Ordner, dessen Ergebnis
            // ohnehin verworfen würde.
            var masken = HoleMasken().Where(m => m.IstVerwendbar).ToList();

            // Wie beim Indexieren mehrere Bilder gleichzeitig: Jedes Bild wird für sich
            // geladen und gerechnet, es gibt keine gemeinsamen Zwischenstände. Der Aufwand
            // liegt im Dekodieren und in der Korrelation, beides skaliert über die Kerne.
            int grad = Math.Max(1, Environment.ProcessorCount);
            var gesammelt = new System.Collections.Concurrent.ConcurrentDictionary<string, WasserzeichenBefund>(
                StringComparer.OrdinalIgnoreCase);

            int erledigt = 0;

            await Task.Run(() =>
            {
                Parallel.ForEach(
                    dateien,
                    new ParallelOptions { MaxDegreeOfParallelism = grad },
                    datei =>
                    {
                        // Abbruch ohne Ausnahme, damit die bereits geprüften Bilder
                        // erhalten bleiben – wie beim Indexieren.
                        if (token.IsCancellationRequested)
                            return;

                        var befund = PruefeDatei(datei, masken);
                        gesammelt[befund.Pfad] = befund;

                        fortschritt?.Report((Interlocked.Increment(ref erledigt), dateien.Count));
                    });
            }, token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();

            foreach (var paar in gesammelt)
                ergebnis[paar.Key] = paar.Value;

            Speichere(ordner, ergebnis);
            return ergebnis;
        }

        /// <summary>
        /// Einzelnes Bild prüfen (sichtbares Wasserzeichen + Metadaten). Es gewinnt das
        /// Muster mit der höchsten Übereinstimmung — die Zeichentypen schliessen sich
        /// gegenseitig aus, ein Bild trägt nur einen davon.
        /// </summary>
        internal static WasserzeichenBefund PruefeDatei(string pfad, IReadOnlyList<WasserzeichenMaske> masken)
        {
            var befund = new WasserzeichenBefund { Pfad = pfad };

            float beste = 0f;
            float besteSchwelle = Schwelle;
            string besterName = string.Empty;

            // Verglichen wird das Verhältnis Wert zu eigener Schwelle, nicht der rohe
            // Wert. Sonst gewänne immer das Muster mit der niedrigsten Schwelle, auch
            // wenn ein anderes seine eigene Schwelle deutlicher überschreitet.
            float besterAbstand = float.NegativeInfinity;

            // Ohne Muster gibt es nichts zu vergleichen – dann die Datei auch nicht laden.
            //
            // Vorher wurde jedes Bild des Ordners dekodiert, selbst wenn noch kein
            // einziges Muster gelernt war. Bei jedem Indexieren lief damit ein
            // vollständiger zweiter Dekodierdurchlauf über den ganzen Ordner, dessen
            // Ergebnis sofort verworfen wurde.
            if (masken.Count > 0)
            {
                // Einmal dekodieren, alle Muster gegen dasselbe Bild prüfen. Jedes
                // schneidet sich daraus seinen eigenen Bereich – Mitte, Ecke, wo auch immer.
                var bild = LadeBild(pfad);

                if (bild is not null)
                {
                    foreach (var maske in masken)
                    {
                        // Aussen vor bleiben nur nie nachgemessene Muster: Deren Schwelle
                        // stammt aus der selbstbezüglichen Einmessung und lässt allein
                        // ihre eigenen Lernbilder durch. Schwache, aber gemessene Muster
                        // prüfen mit — siehe WasserzeichenMaske.IstVerwendbar.
                        if (!maske.IstVerwendbar)
                            continue;

                        float wert = maske.Pruefe(bild);

                        // Muster ohne eigene Schwelle (aus der Zeit davor) nutzen die allgemeine.
                        float eigene = maske.Schwelle > 0f ? maske.Schwelle : Schwelle;
                        float abstand = wert / Math.Max(eigene, 0.0001f);

                        if (abstand > besterAbstand)
                        {
                            besterAbstand = abstand;
                            beste = wert;
                            besteSchwelle = eigene;
                            besterName = maske.Name;
                        }
                    }
                }
            }

            befund.Aehnlichkeit = beste;
            befund.VerwendeteSchwelle = besteSchwelle;
            befund.HatSichtbares = beste >= besteSchwelle;

            // Den besten Namen auch unterhalb der Schwelle festhalten. Ohne ihn liesse
            // sich später nicht mehr sagen, welches Muster überhaupt am nächsten dran war
            // — und genau das braucht man, um zu beurteilen, ob knapp danebenlag oder
            // schlicht nichts da war.
            befund.MaskenName = besterName;

            befund.MetadatenHinweise = MetadatenPruefer.Pruefe(pfad).ToList();
            return befund;
        }

        /// <summary>
        /// Lädt das Bild einmal, eingefroren, damit es über Fadengrenzen hinweg benutzt
        /// werden darf. <c>null</c>, wenn die Datei nicht lesbar ist.
        /// </summary>
        private static System.Windows.Media.Imaging.BitmapSource? LadeBild(string pfad)
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(pfad);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Befunde speichern und laden

        private sealed class BefundDatei
        {
            public int Version { get; set; } = 1;
            public List<WasserzeichenBefund> Befunde { get; set; } = new();
        }

        private static void Speichere(string ordner, Dictionary<string, WasserzeichenBefund> befunde)
        {
            try
            {
                var datei = new BefundDatei { Befunde = befunde.Values.ToList() };
                using var fs = File.Create(Path.Combine(ordner, CacheDateiName));
                JsonSerializer.Serialize(fs, datei);
            }
            catch
            {
                // Schreibgeschützter Ordner o. ä. – der Befund geht dann nur verloren.
            }
        }

        /// <summary>Gespeicherte Befunde eines Ordners, leer wenn noch nicht geprüft.</summary>
        internal static Dictionary<string, WasserzeichenBefund> Lade(string ordner)
        {
            var map = new Dictionary<string, WasserzeichenBefund>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string pfad = Path.Combine(ordner, CacheDateiName);
                if (!File.Exists(pfad))
                    return map;

                using var fs = File.OpenRead(pfad);
                var datei = JsonSerializer.Deserialize<BefundDatei>(fs);

                if (datei?.Befunde is null)
                    return map;

                foreach (var b in datei.Befunde)
                    map[b.Pfad] = b;
            }
            catch
            {
                // beschädigte Datei → wie „nicht geprüft" behandeln
            }

            return map;
        }

        #endregion

        private static readonly string[] Bildendungen =
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

        /// <summary>
        /// Anzahl der Bilder in einem Ordner – für den Hinweis vor dem Lernen, ohne
        /// dafür eine zweite Endungsliste zu pflegen.
        /// </summary>
        public static int ZähleBilder(string ordner) => SammleBilder(ordner).Count;

        private static List<string> SammleBilder(string ordner)
        {
            try
            {
                return Directory
                    .EnumerateFiles(ordner, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(d => Bildendungen.Contains(Path.GetExtension(d).ToLowerInvariant()))
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }
    }
}
