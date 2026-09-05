using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestImage.Bildersuche;

namespace TestImage
{
    /// <summary>
    /// Wasserzeichen-Erkennung: sichtbare Aufdrucke über eine gelernte Maske,
    /// unsichtbare Markierungen über die Dateimetadaten. Läuft beim Indexieren mit.
    /// </summary>
    public partial class AufgabeViewModel
    {
        #region Zustand

        [ObservableProperty]
        public partial string WasserzeichenStatus { get; set; } = string.Empty;

        /// <summary>Anzahl der im aktuellen Ordner gefundenen Bilder mit Markierung.</summary>
        [ObservableProperty]
        public partial int WasserzeichenTrefferAnzahl { get; set; }

        /// <summary>True, wenn mindestens ein Muster gelernt wurde – sonst greift nur die Metadatenprüfung.</summary>
        [ObservableProperty]
        public partial bool WasserzeichenMaskeVorhanden { get; set; } = WasserzeichenService.MaskeVorhanden;

        /// <summary>
        /// True, wenn mindestens ein Muster beim Prüfen überhaupt herangezogen wird.
        ///
        /// Eigene Eigenschaft neben <see cref="WasserzeichenMaskeVorhanden"/>, weil beide
        /// Fälle verschieden gelöst werden: „noch nichts gelernt" heisst anfangen, „nur
        /// nie nachgemessene Muster" heisst einmal neu lernen. Ohne die Unterscheidung
        /// blieb der Hinweis weg, sobald irgendein Muster existierte — auch wenn keines
        /// davon beim Prüfen verwendet wird.
        ///
        /// Schwache Muster zählen hier mit: Sie prüfen mit und finden einen Teil.
        /// </summary>
        [ObservableProperty]
        public partial bool WasserzeichenVerwendbaresMusterVorhanden { get; set; }
            = WasserzeichenService.VerwendbaresMusterVorhanden;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteWasserzeichenMaskeLernenCommand))]
        public partial bool WasserzeichenAufgabeLäuft { get; set; }

        /// <summary>
        /// Ordnerauswahl beim Lernen. Vorgabe aus: Gelernt wird der Ordner des gerade
        /// angezeigten Bildes – das ist fast immer der gemeinte, und der Dialog begann
        /// ohnehin dort. Angekreuzt kommt der Dialog wieder, für Beispielordner, die
        /// woanders liegen. Ist gar kein Bildordner bekannt, fragt der Dialog trotzdem.
        /// </summary>
        [ObservableProperty]
        public partial bool WasserzeichenLernOrdnerWaehlen { get; set; }

        /// <summary>
        /// Karte aufgeklappt. Gleiche Mechanik wie <c>IsIndexPopoverOffen</c> bei den
        /// Einstellungen: ein Knopf oben schaltet um, die Karte hängt an dieser Eigenschaft.
        /// </summary>
        [ObservableProperty]
        public partial bool IsWasserzeichenOffen { get; set; }

        /// <summary>
        /// Untergliederung „Muster lernen und verwalten" innerhalb der aufgeklappten Karte.
        ///
        /// Die Karte trägt zweierlei: den Befund zum gerade gewählten Bild — den sieht man
        /// beim Blättern immer wieder an — und die Einrichtung, die man einmal macht und
        /// danach jahrelang nicht mehr anfasst. Zugeklappt als Vorgabe, damit der Befund
        /// nicht unter Lernknöpfen und Musterliste verschwindet.
        /// </summary>
        [ObservableProperty]
        public partial bool IsWasserzeichenMusterOffen { get; set; }

        /// <summary>
        /// Auswahl der Stelle im Bild, als Index der Auswahlliste. 0 heisst „alle
        /// Bereiche" und ist die Vorgabe — dann sucht das Lernen die Stelle selbst.
        ///
        /// Die Liste beginnt bewusst mit „alle", die Aufzählung dagegen mit „Mitte":
        /// deren Zahlen stehen so in bereits gespeicherten Mustern und dürfen sich nicht
        /// verschieben. Deshalb wird hier umgerechnet statt einfach gecastet.
        /// </summary>
        [ObservableProperty]
        public partial int WasserzeichenLernBereich { get; set; }

        private WasserzeichenBereich GewaehlterLernBereich =>
            WasserzeichenLernBereich <= 0
                ? WasserzeichenBereich.Alle
                : (WasserzeichenBereich)(WasserzeichenLernBereich - 1);

        /// <summary>Klappt die Wasserzeichen-Karte auf und zu.</summary>
        [RelayCommand]
        private void CommandExecuteWasserzeichenToggle()
            => IsWasserzeichenOffen = !IsWasserzeichenOffen;

        /// <summary>Klappt die Untergliederung „Muster lernen und verwalten" auf und zu.</summary>
        [RelayCommand]
        private void CommandExecuteWasserzeichenMusterToggle()
            => IsWasserzeichenMusterOffen = !IsWasserzeichenMusterOffen;

        /// <summary>
        /// Gelernte Muster für die Anzeige. Mehrere sind der Normalfall: DeviantArt
        /// allein verwendet mindestens drei Zeichentypen, und jeder braucht ein eigenes
        /// Muster.
        /// </summary>
        public ObservableCollection<WasserzeichenMusterEintrag> WasserzeichenMuster { get; } =
            new(WasserzeichenService.Masken.Select(AbbildenAlsEintrag));

        #endregion

        /// <summary>
        /// Ordner der zuletzt abgelegten Datei. Rückfall, falls gerade kein Bild
        /// ausgewählt ist – nach einem Drop ist der Pfad trotzdem bekannt.
        /// </summary>
        private string? OrdnerVomDropBild()
        {
            if (string.IsNullOrWhiteSpace(DropDateiName))
                return null;

            try
            {
                string? ordner = Path.GetDirectoryName(DropDateiName);
                return Directory.Exists(ordner) ? ordner : null;
            }
            catch
            {
                return null;
            }
        }

        #region Maske lernen

        private bool CanExecuteWasserzeichenMaskeLernen() => !WasserzeichenAufgabeLäuft;

        /// <summary>
        /// Ab dieser Ordnergrösse wird vor dem Lernen zurückgefragt.
        ///
        /// Die Rückfrage stand früher bei 20 und warnte vor <i>zu vielen</i> Bildern —
        /// mehr aus demselben Ordner machten das Muster angeblich kaum besser. Das ist
        /// nachgemessen falsch: Die Trennschärfe wächst mit der Wurzel der Bilderzahl,
        /// und zwar aus einem Ordner schneller (r/√n ≈ 0,010) als über mehrere Künstler
        /// verteilt (≈ 0,0071). Ein schwaches Zeichen braucht rund 200 Bilder. Die
        /// Rückfrage riet also von genau dem ab, was hilft.
        ///
        /// Geblieben ist der einzige echte Einwand: die Wartezeit. Bei 300 Bildern und
        /// „alle Bereiche" sind das gut zwei Minuten — ab da lohnt sich die Frage.
        /// </summary>
        private const int WasserzeichenLernBilderRueckfrage = 300;

        /// <summary>
        /// Gemessene Dauer je Bild und Stelle: 23 Bilder über fünf Stellen in 11,3 s,
        /// 29 Bilder in 12,3 s — also rund 0,09 s. „Alle Bereiche" kostet das Fünffache.
        /// Grob, aber die Grössenordnung stimmt, und darum geht es in der Rückfrage.
        /// </summary>
        private const double LernSekundenJeBildUndStelle = 0.09;

        /// <summary>Geschätzte Lerndauer für die Rückfrage, in Worten.</summary>
        private string LernDauerText(int bilder)
        {
            int stellen = GewaehlterLernBereich == WasserzeichenBereich.Alle ? 5 : 1;
            double sekunden = bilder * LernSekundenJeBildUndStelle * stellen;

            return sekunden < 90
                ? $"rund {Math.Max(5, (int)Math.Round(sekunden / 5) * 5)} Sekunden"
                : $"rund {Math.Round(sekunden / 60.0):0} Minuten";
        }

        /// <summary>
        /// Lernt ein Muster aus einem Ordner, in dem alle Bilder denselben Zeichentyp
        /// tragen. Der Ordnername wird zum Namen des Musters — so entsteht die Sammlung
        /// nebenbei, ohne dass nach jedem Lernen noch ein Namensdialog kommt.
        /// Ohne mindestens ein Muster kann nur nach Metadaten gesucht werden.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteWasserzeichenMaskeLernen), IncludeCancelCommand = true)]
        private async Task CommandExecuteWasserzeichenMaskeLernen(CancellationToken token)
        {
            // Ohne Haken der Ordner des angezeigten Bildes, ohne jede Rückfrage. Der
            // Dialog stand hier bei jedem Lauf im Weg, obwohl er fast immer nur den
            // Ordner bestätigte, in dem er ohnehin schon aufging.
            string? lernOrdner = WasserzeichenLernOrdnerWaehlen
                ? null
                : AktuellerBildOrdner() ?? OrdnerVomDropBild();

            if (lernOrdner is null)
            {
                // Vor dem Dialog setzen, nicht danach. Der Dialog hat eine eigene
                // Nachrichtenschleife, die Zeile wird also noch gezeichnet — und sie ist die
                // einzige Rückmeldung, dass der Knopf überhaupt angekommen ist. Vorher stand
                // hier bis zum Dialogende die Meldung des vorigen Laufs, und wer abbrach,
                // behielt sie für immer.
                WasserzeichenStatus = "Ordner mit Beispielbildern wählen …";

                var dlg = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Ordner mit Beispielbildern – alle müssen denselben Zeichentyp tragen",

                    // Beim Ordner des angezeigten Bildes beginnen: Die Beispiele liegen fast
                    // immer dort oder gleich daneben, sonst müsste man jedes Mal neu dorthin
                    // navigieren.
                    InitialDirectory = AktuellerBildOrdner() ?? OrdnerVomDropBild() ?? string.Empty
                };

                if (dlg.ShowDialog() != true)
                {
                    WasserzeichenStatus = "Lernen abgebrochen – es wurde kein Ordner gewählt.";
                    return;
                }

                lernOrdner = dlg.FolderName;
            }

            // Ohne Dialog hat niemand gesehen, welcher Ordner gemeint ist – deshalb steht
            // er ab hier in jeder Meldung.
            string ordnerName = Path.GetFileName(lernOrdner.TrimEnd(Path.DirectorySeparatorChar));
            if (ordnerName.Length == 0)
                ordnerName = lernOrdner;

            // Das Zählen läuft über das Dateisystem und kann auf einer langsamen Platte
            // dauern. Ohne Meldung sähe der Knopf in dieser Zeit tot aus.
            WasserzeichenStatus = $"Ordner „{ordnerName}“ wird durchgesehen …";

            // Rückfrage nur noch wegen der Wartezeit, nicht wegen der Bilderzahl: Viele
            // Bilder sind das Ziel, nicht der Fehler. Erst nach der Ordnerwahl, weil die
            // Zahl vorher niemandem etwas sagt.
            int vorhandeneBilder = WasserzeichenService.ZähleBilder(lernOrdner);
            if (vorhandeneBilder > WasserzeichenLernBilderRueckfrage)
            {
                var antwort = System.Windows.MessageBox.Show(
                    $"Der Ordner „{ordnerName}“ enthält {vorhandeneBilder} Bilder.\n\n"
                    + "Jedes Bild wird einmal je Stelle gelesen"
                    + (GewaehlterLernBereich == WasserzeichenBereich.Alle
                        ? " – bei „alle Bereiche“ sind das fünf Durchgänge.\n"
                        : ".\n")
                    + $"Geschätzte Dauer: {LernDauerText(vorhandeneBilder)}. "
                    + "Abbrechen ist jederzeit möglich.\n\n"
                    + "Viele Bilder sind dabei kein Nachteil: Die Erkennung wird mit jedem "
                    + "Bild besser. Entscheidend ist nur, dass alle dasselbe Zeichen an "
                    + "derselben Stelle tragen – ein Ordner mit gemischten Zeichen wird "
                    + "auch mit tausend Bildern nicht brauchbar.\n\n"
                    + "Jetzt lernen?",
                    "Grosser Lernordner",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question,
                    System.Windows.MessageBoxResult.Yes);

                if (antwort != System.Windows.MessageBoxResult.Yes)
                {
                    WasserzeichenStatus = "Lernen abgebrochen.";
                    return;
                }
            }

            string name = NameAusOrdner(lernOrdner);

            WasserzeichenAufgabeLäuft = true;
            WasserzeichenStatus = $"Ordner „{ordnerName}“ wird gelesen …";

            try
            {
                // Restzeit über dieselben Helfer wie die Dubletten-Suche – gleiche
                // Rechnung, gleiche Formulierung („noch ca. …"), keine zweite Fassung.
                var uhr = System.Diagnostics.Stopwatch.StartNew();

                var fortschritt = new Progress<(int Erledigt, int Gesamt)>(p =>
                    WasserzeichenStatus = $"Ordner „{ordnerName}“ wird gelesen … {p.Erledigt}/{p.Gesamt}"
                                          + RestzeitZusatz(uhr.Elapsed, p.Erledigt, p.Gesamt));

                // Ein Weg für beides: Trägt der Ordner dasselbe Zeichen wie ein schon
                // gelerntes Muster, wird dieses ergänzt; sonst entsteht ein neues. Vorher
                // waren das zwei Knöpfe, und die Wahl dazwischen konnte niemand treffen —
                // ob der Ordner zu einem vorhandenen Muster passt, weiss man ja erst,
                // nachdem seine Bilder gelesen sind.
                var (musterName, bilder, istNeu, vorherBilder, vorherTrennschaerfe) =
                    await WasserzeichenService.ErgaenzeOderLerneAsync(
                        lernOrdner, name, GewaehlterLernBereich, fortschritt, token);

                AktualisiereWasserzeichenMuster();

                var eintrag = WasserzeichenMuster
                    .FirstOrDefault(m => string.Equals(m.MusterName, musterName, StringComparison.OrdinalIgnoreCase));

                // Bei „alle Bereiche" ist die gefundene Stelle das eigentlich Interessante.
                string stelle = eintrag?.BereichName ?? string.Empty;

                // Ein Muster entsteht auch aus einem Ordner ohne gemeinsames Zeichen — es
                // erkennt dann nur seine eigenen Lernbilder wieder. Das muss hier stehen:
                // Sonst meldet das Lernen Erfolg, und die Verwunderung kommt erst beim
                // Indexieren des nächsten Ordners, wo dasselbe Zeichen nicht gefunden wird.
                // Zwei sehr verschiedene Gründe, aus denen ein Muster nicht belegt ist:
                // Im Ordner steckt gar kein gemeinsames Zeichen — oder es steckt eines
                // drin, nur zu schwach für diese Bilderzahl. Das zweite ist keine
                // Sackgasse, sondern eine Mengenangabe, und muss auch so klingen.
                if (eintrag is { IstBelegt: false })
                {
                    WasserzeichenStatus = MitSpeicherhinweis(eintrag.BilderFuerBeleg is { } noetig
                        ? $"Muster „{musterName}“ aus {bilder} Bildern gelernt – noch schwach "
                          + $"(Trennschärfe {eintrag.TrennschaerfeText}). Es prüft ab jetzt mit, "
                          + "findet aber nur einen Teil: Das Zeichen ist da, geht im Motiv aber noch "
                          + $"unter. Für sichere Erkennung rund {noetig} Bilder desselben Zeichens – "
                          + "weitere Ordner dazulernen, die Bilder werden diesem Muster zugerechnet."
                        : $"Muster „{musterName}“ aus {bilder} Bildern gelernt – es erkennt praktisch "
                          + $"nur diese Bilder selbst wieder (Trennschärfe {eintrag.TrennschaerfeText}). "
                          + "Im Ordner steckt kein Zeichen, das bei allen Bildern gleich aussieht und "
                          + "an derselben Stelle sitzt.");
                    return;
                }

                WasserzeichenStatus = MitSpeicherhinweis(bilder switch
                {
                    0 => "Zu wenige oder unlesbare Bilder – es werden mindestens 5 gebraucht.",

                    _ when istNeu => $"Neues Muster „{musterName}“ aus {bilder} Bildern gelernt"
                                     + (stelle.Length > 0 ? $" – Stelle: {stelle}" : string.Empty)
                                     + $", Trennschärfe {eintrag?.TrennschaerfeText ?? "–"}."
                                     + WasserzeichenService.LetzteLernMeldung
                                     + " Ordner neu indexieren, um es anzuwenden.",

                    // Vorher-Nachher statt nur Endstand: „jetzt aus 156 Bildern,
                    // Trennschärfe 16,2 %" sagt nicht, ob der Ordner etwas gebracht hat.
                    // Genau das ist aber die Frage, wenn man Ordner für Ordner sammelt.
                    _ => $"Der Ordner trägt das bekannte Zeichen „{musterName}“ – Muster ergänzt: "
                         + $"{vorherBilder} → {bilder} Bilder, Trennschärfe "
                         + $"{ProzentText(vorherTrennschaerfe)} → {eintrag?.TrennschaerfeText ?? "–"}. "
                         + "Ordner neu indexieren, um es anzuwenden."
                });
            }
            catch (OperationCanceledException)
            {
                WasserzeichenStatus = "Lernen abgebrochen.";
            }
            catch (Exception ex)
            {
                WasserzeichenStatus = "Fehler beim Lernen: " + ex.Message;
            }
            finally
            {
                WasserzeichenAufgabeLäuft = false;
            }
        }

        /// <summary>Trennschärfe als Prozenttext; „–", wenn sie nicht gemessen wurde.</summary>
        private static string ProzentText(float? wert) =>
            wert is null ? "–" : $"{wert.Value * 100f:0.0} %";

        /// <summary>
        /// Hängt an eine Erfolgsmeldung den Hinweis, falls die Sammlung nicht auf die
        /// Platte kam. Ohne ihn stünde „Muster gelernt" da, und beim nächsten Start wäre
        /// es weg — ein Lernlauf über hunderte Bilder ist zu teuer, um so zu enden.
        /// </summary>
        private static string MitSpeicherhinweis(string text)
        {
            string fehler = WasserzeichenService.LetzterSpeicherFehler;

            return fehler.Length == 0
                ? text
                : text + $" ACHTUNG: nicht gespeichert ({fehler}) – gilt nur für diese Sitzung.";
        }

        /// <summary>Entfernt ein gelerntes Muster aus der Sammlung.</summary>
        [RelayCommand]
        private void CommandExecuteWasserzeichenMusterEntfernen(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            if (WasserzeichenService.EntferneMaske(name))
            {
                AktualisiereWasserzeichenMuster();

                // Auch hier: Ohne den Hinweis wäre das Muster nach einem Neustart wieder da.
                WasserzeichenStatus = MitSpeicherhinweis($"Muster „{name}“ entfernt.");
            }
        }

        /// <summary>
        /// Ordnername als Musternamen verwenden. Ein reiner Laufwerksbuchstabe oder ein
        /// leerer Name führt zu einer durchnummerierten Ersatzbezeichnung.
        /// </summary>
        private string NameAusOrdner(string ordner)
        {
            string name;
            try
            {
                name = new DirectoryInfo(ordner.TrimEnd(Path.DirectorySeparatorChar)).Name;
            }
            catch
            {
                name = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(name) || name.Contains(':'))
                name = $"Muster {WasserzeichenMuster.Count + 1}";

            return name;
        }

        private static WasserzeichenMusterEintrag AbbildenAlsEintrag(WasserzeichenMaske maske) => new()
        {
            MusterName = maske.Name,
            Grundmenge = maske.Grundmenge,
            StabilProzent = (int)Math.Round(maske.StabilerAnteil * 100.0),
            SchwelleProzent = (int)Math.Round(maske.Schwelle * 100.0),
            Staerke = maske.MusterStaerke,
            Trennschaerfe = maske.Trennschaerfe,
            IstBelegt = maske.IstBelegt,
            BilderFuerBeleg = maske.BilderFuerBeleg,
            BereichName = maske.BereichName,
            Vorschau = maske.ErzeugeVorschau()
        };

        /// <summary>Übernimmt die Musterliste des Dienstes in die Anzeige.</summary>
        private void AktualisiereWasserzeichenMuster()
        {
            WasserzeichenMuster.Clear();

            foreach (var maske in WasserzeichenService.Masken)
                WasserzeichenMuster.Add(AbbildenAlsEintrag(maske));

            WasserzeichenMaskeVorhanden = WasserzeichenMuster.Count > 0;
            WasserzeichenVerwendbaresMusterVorhanden =
                WasserzeichenMuster.Any(m => m.Trennschaerfe is not null);
        }

        #endregion

        #region Beim Indexieren mitlaufen

        /// <summary>
        /// Prüft alle Bilder des Ordners auf Wasserzeichen und überträgt die Befunde
        /// auf die Bildliste. Wird am Ende des Indexierens gerufen.
        /// </summary>
        private async Task PruefeWasserzeichenAsync(
            string ordner, IProgress<(int Erledigt, int Gesamt)>? fortschritt, CancellationToken token)
        {
            try
            {
                var befunde = await WasserzeichenService.PruefeOrdnerAsync(ordner, fortschritt, token);

                UebertrageWasserzeichenBefunde(befunde);

                // Auch der Befund-Kasten muss die frischen Ergebnisse bekommen, nicht nur
                // die Abzeichen auf den Miniaturen.
                //
                // Vorher wurden hier ausschliesslich die Abzeichen gesetzt. Der Kasten
                // liest aber aus _befundeDesOrdners, und das stand noch auf dem Stand vom
                // Öffnen des Ordners — bei einem erstmals indexierten Ordner also leer.
                // Deshalb blieb er gleich nach dem Indexieren ohne Musterbild und tauchte
                // erst auf, wenn die Befunde später wieder von der Platte gelesen wurden.
                _befundeDesOrdners = befunde;
                AktualisiereWasserzeichenBefundAnzeige();

                // Gezählt wird über die Befunde, nicht über die Bildliste.
                //
                // Vorher stand hier WasserzeichenTrefferAnzahl — und das ist die Zahl der
                // gesetzten Abzeichen, also nur der Bilder, die gerade in der Liste
                // stehen. Die Liste ist aber nicht der Ordner: Verschobene und gelöschte
                // Einträge fliegen heraus, gefiltert wird auch. Geprüft wurde trotzdem
                // der ganze Ordner. So konnte „Keine Wasserzeichen gefunden" über einem
                // Lauf stehen, dessen Befunddatei 23 von 23 Treffern enthielt.
                int gesamt = befunde.Count;
                int sichtbar = befunde.Values.Count(b => b.HatSichtbares);
                int metadaten = befunde.Values.Count(b => b.HatMetadaten);

                // Ohne gelerntes Muster sucht der Dienst nur nach Metadaten — sichtbare
                // Zeichen werden gar nicht geprüft. „Keine Wasserzeichen gefunden" behauptete
                // dort eine Prüfung, die nie stattgefunden hat, und beim ersten Indizieren
                // eines Ordners ist genau das der Normalfall.
                if (!WasserzeichenService.MaskeVorhanden)
                {
                    WasserzeichenStatus = metadaten == 0
                        ? $"{gesamt} Bild(er) nur auf Metadaten geprüft – für sichtbare Zeichen "
                          + "fehlt ein gelerntes Muster."
                        : $"{metadaten} von {gesamt} Bild(ern) mit Metadaten-Markierung. Sichtbare "
                          + "Zeichen wurden nicht geprüft – dafür fehlt ein gelerntes Muster.";
                    return;
                }

                // Muster sind da, aber keines davon erkennt mehr als seine eigenen
                // Lernbilder. Ohne diesen Fall stünde hier „Keine Wasserzeichen gefunden"
                // — eine Aussage über den Ordner, obwohl das Problem bei den Mustern liegt.
                if (!WasserzeichenService.VerwendbaresMusterVorhanden)
                {
                    WasserzeichenStatus =
                        "Kein verwendbares Muster – die vorhandenen stammen aus der Zeit vor der "
                        + "Nachmessung und lassen nur ihre eigenen Lernbilder durch. Bitte einmal "
                        + "neu lernen."
                        + (metadaten > 0 ? $" ({metadaten} Bild(er) mit Metadaten-Markierung.)" : string.Empty);
                    return;
                }

                WasserzeichenStatus = MitUebersprungenen(
                    gesamt == 0
                        ? "Im Ordner liegt kein Bild zum Prüfen."
                        : sichtbar == 0 && metadaten == 0
                            ? $"{gesamt} Bild(er) geprüft – kein bekanntes Wasserzeichen gefunden."
                            : $"{gesamt} Bild(er) geprüft: {sichtbar} mit sichtbarem Zeichen, "
                              + $"{metadaten} mit Metadaten-Markierung.");
            }
            catch (OperationCanceledException)
            {
                WasserzeichenStatus = "Prüfung auf bekannte Wasserzeichen abgebrochen.";
            }
            catch (Exception ex)
            {
                WasserzeichenStatus = "Fehler bei der Prüfung auf bekannte Wasserzeichen: " + ex.Message;
            }
        }

        /// <summary>
        /// Nennt die Muster, die bei diesem Lauf nicht mitgeprüft wurden.
        ///
        /// Ohne diesen Zusatz ist „kein Wasserzeichen gefunden" nicht einzuordnen: Es
        /// kann heissen, dass der Ordner sauber ist — oder dass gerade das Muster
        /// übersprungen wurde, das zu ihm gehört. Genannt wird deshalb, welche es waren,
        /// nicht nur dass es welche gab.
        /// </summary>
        private static string MitUebersprungenen(string text)
        {
            var uebersprungen = WasserzeichenService.UebersprungeneMuster;
            var schwach = WasserzeichenService.SchwacheMuster;

            if (uebersprungen.Count > 0)
                text += $" Übersprungen wurde{(uebersprungen.Count == 1 ? "" : "n")} dabei "
                      + string.Join(", ", uebersprungen.Select(n => $"„{n}“"))
                      + " – nie nachgemessen, bitte neu lernen.";

            // Schwache Muster prüfen mit, finden aber nur einen Teil. Das gehört dazu,
            // wenn wenig oder nichts gefunden wurde — sonst sucht man den Fehler im
            // Ordner statt in der Materialmenge.
            if (schwach.Count > 0)
                text += $" Noch schwach {(schwach.Count == 1 ? "ist" : "sind")} "
                      + string.Join(", ", schwach.Select(n => $"„{n}“"))
                      + " – findet nur einen Teil, mehr Bilder dazulernen.";

            return text;
        }

        /// <summary>Setzt die Badge-Flags auf den Bildern der aktuellen Liste.</summary>
        private void UebertrageWasserzeichenBefunde(
            System.Collections.Generic.IReadOnlyDictionary<string, WasserzeichenBefund> befunde)
        {
            int treffer = 0;

            foreach (var bild in OcAufgabens)
            {
                if (befunde.TryGetValue(bild.BName, out var b) && b.HatIrgendetwas)
                {
                    bild.HatWasserzeichen = true;
                    bild.WasserzeichenGrund = b.Begruendung();
                    treffer++;
                }
                else
                {
                    bild.HatWasserzeichen = false;
                    bild.WasserzeichenGrund = string.Empty;
                }
            }

            WasserzeichenTrefferAnzahl = treffer;
        }

        /// <summary>
        /// Lädt bereits gespeicherte Befunde des Ordners und setzt die Badges, ohne
        /// neu zu prüfen. Für den Bildwechsel und nach dem Laden eines Ordners.
        /// </summary>
        private void LadeWasserzeichenBefunde(string? ordner)
        {
            // Die Statuszeile gehört zum Ordner, nicht zur Sitzung. Sie blieb bisher
            // unberührt, wenn ein anderer Ordner geladen wurde — nach einem Drop stand
            // dort also weiter das Ergebnis des vorigen: „Keine Wasserzeichen gefunden",
            // über einem Ordner, der nie geprüft wurde. Solange nichts läuft, wird sie
            // deshalb hier neu gesetzt.
            if (!WasserzeichenAufgabeLäuft
                && !CommandExecuteWasserzeichenMaskeLernenCommand.IsRunning)
            {
                WasserzeichenStatus = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
            {
                WasserzeichenTrefferAnzahl = 0;
                _befundeDesOrdners = null;
                AktualisiereWasserzeichenBefundAnzeige();
                return;
            }

            var befunde = WasserzeichenService.Lade(ordner);
            _befundeDesOrdners = befunde;

            if (befunde.Count == 0)
            {
                // Auch hier übertragen, obwohl nichts drinsteht: Sonst blieben die
                // Abzeichen der vorigen Prüfung auf den Miniaturen kleben, wenn die
                // Befunddatei gelöscht wurde.
                UebertrageWasserzeichenBefunde(befunde);
                AktualisiereWasserzeichenBefundAnzeige();

                WasserzeichenStatus = "Dieser Ordner wurde noch nicht auf Wasserzeichen geprüft "
                                      + "– das läuft beim Indexieren mit.";
                return;
            }

            UebertrageWasserzeichenBefunde(befunde);
            AktualisiereWasserzeichenBefundAnzeige();

            // Der Befund stammt aus der Seitendatei und kann älter sein als die Muster.
            // Ohne diese Zeile wäre nicht zu sehen, dass hier überhaupt schon geprüft wurde.
            //
            // Auch hier über die Befunde zählen, nicht über WasserzeichenTrefferAnzahl:
            // Das ist die Zahl der Abzeichen in der sichtbaren Liste, und die enthält
            // weniger Bilder als der Ordner, sobald etwas verschoben oder gefiltert wurde.
            int markiert = befunde.Values.Count(b => b.HatIrgendetwas);

            WasserzeichenStatus = markiert == 0
                ? $"Geprüft: {befunde.Count} Bild(er), keine Markierung gefunden."
                : $"Geprüft: {markiert} von {befunde.Count} Bild(ern) markiert.";
        }

        #endregion

        #region Befund zum gewählten Bild

        /// <summary>
        /// Befunde des zuletzt geladenen Ordners. Gemerkt, um „noch nicht geprüft" von
        /// „geprüft und sauber" unterscheiden zu können — die Badge-Eigenschaften am Bild
        /// allein können das nicht, dort ist beides schlicht „false".
        /// </summary>
        private System.Collections.Generic.Dictionary<string, WasserzeichenBefund>? _befundeDesOrdners;

        /// <summary>Dateiname des Bildes, auf das sich der angezeigte Befund bezieht.</summary>
        [ObservableProperty]
        public partial string WasserzeichenBefundDatei { get; set; } = string.Empty;

        /// <summary>Urteil in einem Satz.</summary>
        [ObservableProperty]
        public partial string WasserzeichenBefundText { get; set; } = "Kein Bild gewählt.";

        /// <summary>Bestes Muster samt Stelle, z. B. „Künzler1 · oben rechts".</summary>
        [ObservableProperty]
        public partial string WasserzeichenBefundMuster { get; set; } = string.Empty;

        /// <summary>Übereinstimmung im Verhältnis zur Schwelle, im Klartext.</summary>
        [ObservableProperty]
        public partial string WasserzeichenBefundWert { get; set; } = string.Empty;

        /// <summary>Vorschaubild des Musters, das am besten passte.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(WasserzeichenBefundHatBild))]
        public partial System.Windows.Media.ImageSource? WasserzeichenBefundBild { get; set; }

        /// <summary>Steuert das Vorschaukästchen – ein eigener Konverter wäre dafür zu viel.</summary>
        public bool WasserzeichenBefundHatBild => WasserzeichenBefundBild is not null;

        /// <summary>Gefundene Metadaten-Markierungen, je Zeile eine.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(WasserzeichenBefundHatMetadaten))]
        public partial string WasserzeichenBefundMetadaten { get; set; } = string.Empty;

        /// <summary>
        /// Steuert das Blatt-Zeichen in der Titelzeile – wie <see cref="WasserzeichenBefundHatBild"/>
        /// ein Bool statt eines eigenen Konverters.
        /// </summary>
        public bool WasserzeichenBefundHatMetadaten => !string.IsNullOrEmpty(WasserzeichenBefundMetadaten);

        /// <summary>True, wenn das Bild tatsächlich eine Markierung trägt (färbt den Hinweis).</summary>
        [ObservableProperty]
        public partial bool WasserzeichenBefundIstTreffer { get; set; }

        /// <summary>
        /// True nur beim sichtbaren Zeichen — nicht bei einem reinen Metadaten-Fund.
        ///
        /// Steuert allein die Auszeichnung der Musterzeile: Beim Treffer nennt sie das
        /// gefundene Zeichen und steht so kräftig da wie der Dateiname; sonst nennt sie
        /// nur das ähnlichste Muster und muss sich zurücknehmen.
        /// </summary>
        [ObservableProperty]
        public partial bool WasserzeichenBefundIstSichtbarerTreffer { get; set; }

        /// <summary>
        /// Stellt den Befund zum gewählten Bild zusammen. Vier Fälle, die sich für den
        /// Nutzer deutlich unterscheiden — besonders „noch nicht geprüft" darf nicht wie
        /// „sauber" aussehen.
        /// </summary>
        private void AktualisiereWasserzeichenBefundAnzeige()
        {
            string? pfad = SelectedBildchen?.BName;

            // Die Trefferzone gehört zum vorigen Bild. Bliebe sie stehen, läse man beim
            // nächsten Bild eine Karte, die nie zu ihm gerechnet wurde.
            LeereTrefferzone();

            WasserzeichenBefundIstTreffer = false;
            WasserzeichenBefundDatei = string.IsNullOrEmpty(pfad)
                ? string.Empty
                : Path.GetFileName(pfad);

            if (string.IsNullOrEmpty(pfad))
            {
                WasserzeichenBefundText = "Kein Bild gewählt.";
                LeereBefundFelder();
                return;
            }

            if (_befundeDesOrdners is null || _befundeDesOrdners.Count == 0)
            {
                WasserzeichenBefundText =
                    "Dieser Ordner wurde noch nicht auf Wasserzeichen geprüft. "
                    + "Die Prüfung läuft am Ende des Indexierens mit.";
                LeereBefundFelder();
                return;
            }

            if (!_befundeDesOrdners.TryGetValue(pfad, out var befund))
            {
                WasserzeichenBefundText =
                    "Dieses Bild war beim letzten Prüflauf noch nicht dabei.";
                LeereBefundFelder();
                return;
            }

            ZeigeBefund(befund);
        }

        /// <summary>Räumt die Zusatzfelder ab, wenn es gar keinen Befund gibt.</summary>
        private void LeereBefundFelder()
        {
            WasserzeichenBefundMuster = string.Empty;
            WasserzeichenBefundWert = string.Empty;
            WasserzeichenBefundMetadaten = string.Empty;
            WasserzeichenBefundBild = null;
            WasserzeichenBefundIstSichtbarerTreffer = false;
        }

        /// <summary>
        /// Bereitet einen Befund für die Anzeige auf.
        ///
        /// Die blosse Prozentzahl war wenig wert: 23 % klingt nach wenig, liegt aber weit
        /// über der Schwelle von 10 %. Deshalb steht hier immer der Bezug zur Schwelle
        /// dabei, und auch unterhalb davon wird gezeigt, welches Muster am nächsten dran
        /// war — sonst lässt sich „knapp daneben" nicht von „gar nichts" unterscheiden.
        /// </summary>
        private void ZeigeBefund(WasserzeichenBefund befund)
        {
            float wert = befund.Aehnlichkeit;

            // Die Schwelle des Musters, gegen das verglichen wurde. Ältere Befunddateien
            // kennen sie nicht — dann gilt der allgemeine Wert.
            float schwelle = befund.VerwendeteSchwelle > 0f
                ? befund.VerwendeteSchwelle
                : WasserzeichenService.Schwelle;

            WasserzeichenBefundIstTreffer = befund.HatSichtbares;
            WasserzeichenBefundIstSichtbarerTreffer = befund.HatSichtbares;

            // Wann das ähnlichste Muster überhaupt etwas aussagt.
            //
            // Ein bestes Muster gibt es immer — auch bei 1 % gegen eine Schwelle von
            // 22 %. Dort ist der Name blosses Rauschen, und mit Musterbild daneben las
            // er sich wie ein Fund, obwohl darüber „nichts gefunden" stand. Gezeigt wird
            // er deshalb nur beim Treffer und im Bereich knapp darunter — dieselbe
            // 60-%-Grenze, ab der auch der Urteilstext „nahe an der Schwelle" meldet.
            bool musterSagtEtwas = !string.IsNullOrEmpty(befund.MaskenName)
                && (befund.HatSichtbares || wert >= schwelle * 0.6f);

            if (musterSagtEtwas)
            {
                // Muster samt Stelle – den Bereich holen wir aus der Musterliste.
                var eintrag = WasserzeichenMuster.FirstOrDefault(
                    m => string.Equals(m.MusterName, befund.MaskenName, StringComparison.OrdinalIgnoreCase));

                string bezeichnung = eintrag is null
                    ? befund.MaskenName
                    : $"{eintrag.MusterName} · {eintrag.BereichName}";

                // Ohne Treffer bekommt der Name die Ansage davor: Er beantwortet dann
                // die Frage „was war am nächsten dran", nicht „was wurde gefunden".
                WasserzeichenBefundMuster = befund.HatSichtbares
                    ? bezeichnung
                    : "Ähnlichstes Muster: " + bezeichnung;

                WasserzeichenBefundBild = eintrag?.Vorschau;

                WasserzeichenBefundWert =
                    $"Übereinstimmung {wert * 100f:F0} % · Schwelle {schwelle * 100f:F0} %";
            }
            else
            {
                WasserzeichenBefundMuster = string.Empty;
                WasserzeichenBefundWert = string.Empty;
                WasserzeichenBefundBild = null;
            }

            // Überschrift davor, sonst steht dort unvermittelt „Autor: …" und man
            // liest es als Angabe zum sichtbaren Zeichen. Metadaten stehen im
            // Dateikopf und haben mit dem Bildinhalt nichts zu tun.
            WasserzeichenBefundMetadaten = befund.MetadatenHinweise.Count == 0
                ? string.Empty
                : "Metadaten-Markierungen im Dateikopf:\n"
                  + string.Join("\n", befund.MetadatenHinweise);

            if (befund.HatSichtbares)
            {
                WasserzeichenBefundText = wert >= schwelle * 2
                    ? "Wasserzeichen erkannt – deutlich über der Schwelle."
                    : "Wasserzeichen erkannt – knapp über der Schwelle.";
            }
            else if (befund.HatMetadaten)
            {
                WasserzeichenBefundIstTreffer = true;
                WasserzeichenBefundText = "Kein sichtbares Zeichen, aber Metadaten-Markierungen gefunden.";
            }
            else if (string.IsNullOrEmpty(befund.MaskenName))
            {
                // Ohne Muster wird die Datei für die Sichtprüfung gar nicht erst geöffnet.
                // „Keine Markierung gefunden" wäre hier eine Behauptung über etwas, das
                // niemand nachgesehen hat — die Metadaten dagegen wurden geprüft.
                WasserzeichenBefundText =
                    "Keine Metadaten-Markierungen. Auf sichtbare Zeichen wurde nicht "
                    + "geprüft – es ist noch kein Muster gelernt.";
            }
            else if (wert >= schwelle * 0.6f)
            {
                WasserzeichenBefundText =
                    "Kein Treffer, aber nahe an der Schwelle – ansehen lohnt sich. "
                    + "Keine Metadaten-Markierungen.";
            }
            else
            {
                WasserzeichenBefundText =
                    "Kein sichtbares Zeichen und keine Metadaten-Markierungen gefunden.";
            }
        }

        #endregion

        #region Trefferzone

        /// <summary>
        /// Der geprüfte Bildausschnitt – die Stelle, an der das Muster gesucht hat.
        /// <c>null</c>, solange die Zone nicht abgerufen wurde; steuert zugleich die
        /// Anzeige, siehe <see cref="WasserzeichenTrefferzoneVorhanden"/>.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(WasserzeichenTrefferzoneVorhanden))]
        public partial System.Windows.Media.ImageSource? WasserzeichenTrefferzoneBild { get; set; }

        /// <summary>Beitragskarte über dem Ausschnitt – rot, wo die Übereinstimmung herkommt.</summary>
        [ObservableProperty]
        public partial System.Windows.Media.ImageSource? WasserzeichenTrefferzoneKarte { get; set; }

        /// <summary>Steuert Anzeige und Knopfbeschriftung – ein eigener Konverter wäre dafür zu viel.</summary>
        public bool WasserzeichenTrefferzoneVorhanden => WasserzeichenTrefferzoneBild is not null;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteWasserzeichenTrefferzoneCommand))]
        public partial bool WasserzeichenTrefferzoneLaeuft { get; set; }

        private bool CanExecuteWasserzeichenTrefferzone() => !WasserzeichenTrefferzoneLaeuft;

        /// <summary>Blendet die Trefferzone aus.</summary>
        private void LeereTrefferzone()
        {
            WasserzeichenTrefferzoneKarte = null;
            WasserzeichenTrefferzoneBild = null;
        }

        /// <summary>
        /// Zeigt, woher die Übereinstimmung kommt: der geprüfte Ausschnitt, darüber rot
        /// die Bildpunkte, die zur Zahl beigetragen haben.
        ///
        /// Kein Abbrechen-Knopf und kein Token: Gerechnet wird ein einziges Bild, das
        /// dauert Sekundenbruchteile. Gegen den zweiten Klick währenddessen genügt der
        /// CanExecute-Riegel.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteWasserzeichenTrefferzone))]
        private async Task CommandExecuteWasserzeichenTrefferzone()
        {
            // Zweiter Klick blendet wieder aus – wie der Begriff-Chip seine Heatmap.
            if (WasserzeichenTrefferzoneVorhanden)
            {
                LeereTrefferzone();
                return;
            }

            string? pfad = SelectedBildchen?.BName;

            if (string.IsNullOrEmpty(pfad)
                || _befundeDesOrdners is null
                || !_befundeDesOrdners.TryGetValue(pfad, out var befund)
                || string.IsNullOrEmpty(befund.MaskenName))
            {
                return;
            }

            string datei = pfad;

            // Gerechnet wird gegen das Muster, das beim Prüflauf am besten passte – ein
            // anderes würde eine Karte zu einer Zahl zeigen, die nirgends steht.
            var maske = WasserzeichenService.Masken.FirstOrDefault(
                m => string.Equals(m.Name, befund.MaskenName, StringComparison.OrdinalIgnoreCase));

            if (maske is null)
            {
                WasserzeichenStatus =
                    $"Das Muster „{befund.MaskenName}“ gibt es nicht mehr – zu diesem Befund "
                    + "lässt sich keine Trefferzone zeigen.";
                return;
            }

            WasserzeichenTrefferzoneLaeuft = true;
            try
            {
                var ergebnis = await Task.Run(() => maske.Trefferkarte(datei));

                if (ergebnis is null)
                {
                    WasserzeichenStatus = "Trefferzone nicht möglich – das Bild liess sich nicht lesen.";
                    return;
                }

                // Karte zuerst, Ausschnitt zuletzt: Am Ausschnitt hängt die Sichtbarkeit,
                // sonst blitzt das Kästchen einen Anlauf lang ohne Karte auf.
                WasserzeichenTrefferzoneKarte = ErzeugeTrefferkartenBild(ergebnis.Value.Beitrag);
                WasserzeichenTrefferzoneBild = ergebnis.Value.Ausschnitt;
            }
            catch (Exception ex)
            {
                WasserzeichenStatus = "Fehler bei der Trefferzone: " + ex.Message;
            }
            finally
            {
                WasserzeichenTrefferzoneLaeuft = false;
            }
        }

        /// <summary>
        /// Macht aus der Beitragskarte ein halbdurchsichtiges Bild in Musterauflösung.
        /// Gleiche Farbgebung wie die Begriffs-Heatmap: durchsichtig, wo nichts beiträgt,
        /// rot, wo viel beiträgt.
        /// </summary>
        private static System.Windows.Media.ImageSource? ErzeugeTrefferkartenBild(float[] beitrag)
        {
            int kante = WasserzeichenMaske.Kante;

            if (beitrag.Length != kante * kante)
                return null;

            // Bezugswert ist nicht der grösste Beitrag, sondern das 98. Perzentil der
            // positiven: Ein einzelner Ausreisser würde sonst alles Übrige dunkel
            // erscheinen lassen, obwohl gerade die Fläche darum die Zahl trägt.
            var positiv = beitrag.Where(w => w > 0f).OrderBy(w => w).ToArray();

            if (positiv.Length == 0)
                return null;

            float bezug = positiv[Math.Min(positiv.Length - 1, (int)(positiv.Length * 0.98f))];

            if (bezug <= 0f)
                return null;

            var pixel = new byte[kante * kante * 4];   // BGRA

            for (int i = 0; i < beitrag.Length; i++)
            {
                // Nur positive Beiträge. Negative heissen „hier passt es gerade nicht"
                // und liegen über das ganze Feld verstreut – eingefärbt wäre das Bild
                // nur noch bunt, ohne dass man die Trefferstellen noch fände.
                float norm = beitrag[i] <= 0f ? 0f : Math.Min(1f, beitrag[i] / bezug);

                // Wurzel statt linear: Die Beiträge sind spitz verteilt; linear bliebe
                // alles ausser den stärksten Punkten unsichtbar.
                norm = (float)Math.Sqrt(norm);

                int k = i * 4;
                pixel[k] = 0;                                // Blau
                pixel[k + 1] = (byte)((1f - norm) * 70f);    // Grün
                pixel[k + 2] = (byte)(200f + norm * 55f);    // Rot
                pixel[k + 3] = (byte)(norm * 210f);          // Deckung
            }

            var bild = System.Windows.Media.Imaging.BitmapSource.Create(
                kante, kante, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, pixel, kante * 4);

            bild.Freeze();
            return bild;
        }

        #endregion
    }
}
