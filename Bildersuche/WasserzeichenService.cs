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
        /// Name des betroffenen Musters, seine neue Grundmenge, und ob es neu angelegt
        /// wurde. Name leer und Anzahl 0 heisst: zu wenige oder unlesbare Bilder.
        /// </returns>
        internal static async Task<(string Name, int Bilder, bool IstNeu)> ErgaenzeOderLerneAsync(
            string ordner,
            string name,
            WasserzeichenBereich bereich,
            IProgress<(int Erledigt, int Gesamt)>? fortschritt,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
                return (string.Empty, 0, false);

            var dateien = SammleBilder(ordner);
            if (dateien.Count < 5)
                return (string.Empty, 0, false);

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
                    if (erweitert is not null)
                    {
                        var alle = HoleMasken();
                        int platz = alle.IndexOf(beste);
                        if (platz >= 0)
                            alle[platz] = erweitert;
                        else
                            alle.Add(erweitert);

                        SpeichereMasken(alle);

                        LetzteLernMeldung = string.Empty;
                        return (erweitert.Name, erweitert.Grundmenge, false);
                    }
                }
            }

            int anzahl = await LerneMaskeAsync(ordner, name, bereich, fortschritt, token)
                .ConfigureAwait(false);

            return anzahl > 0 ? (name, anzahl, true) : (string.Empty, 0, false);
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
                    float schwelle = maske.Schwelle > 0f ? maske.Schwelle : Schwelle;
                    int passend = felder.Count(f => maske.Pruefe(f) >= schwelle);
                    double anteil = (double)passend / felder.Count;

                    if (anteil >= ZuordnungsAnteil && anteil > besterAnteil)
                    {
                        besterAnteil = anteil;
                        beste = maske;
                        besteFelder = felder;
                    }
                }
            }

            return (beste, besteFelder);
        }

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

        private static void SpeichereMasken(List<WasserzeichenMaske> masken)
        {
            try
            {
                using var fs = File.Create(MaskenPfad);
                JsonSerializer.Serialize(fs, masken.Select(m => m.AlsDatensatz()).ToList());
            }
            catch
            {
                // schreibgeschützter Programmordner – dann bleibt es bei dieser Sitzung
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

            var masken = HoleMasken();

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
