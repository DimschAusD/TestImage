using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestImage.Bildersuche;

namespace TestImage
{
    /// <summary>
    /// Verzeichnis der indizierten Ordner — Anzeige und Pflege.
    ///
    /// Erster Baustein der geplanten Suche über mehrere Ordner: Zuerst muss die
    /// Anwendung überhaupt wissen, welche Ordner einen Index haben. Die Suche selbst
    /// kommt danach.
    /// </summary>
    public partial class AufgabeViewModel
    {
        /// <summary>
        /// Bekannte Ordner mit Index, neueste zuerst. Bereits beim Erzeugen gefüllt,
        /// damit die Liste gleich nach dem Start dasteht und nicht erst, wenn zufällig
        /// ein indizierter Ordner geöffnet wird.
        /// </summary>
        public ObservableCollection<IndexOrdnerEintrag> IndexOrdnerListe { get; } =
            new(IndexOrdnerVerzeichnis.Alle());

        /// <summary>
        /// Zusammenfassung über der Liste, z. B. „12 Ordner · 8.430 Bilder".
        /// </summary>
        [ObservableProperty]
        public partial string IndexOrdnerZusammenfassung { get; set; } = FasseIndexOrdnerZusammen();

        /// <summary>Steuert, ob die Liste überhaupt angezeigt wird.</summary>
        [ObservableProperty]
        public partial bool HatIndexOrdner { get; set; } = IndexOrdnerVerzeichnis.Alle().Count > 0;

        private static string FasseIndexOrdnerZusammen()
        {
            int gesamt = IndexOrdnerVerzeichnis.Alle().Count;
            if (gesamt == 0)
            {
                return "keine Ordner bekannt";
            }

            int vorhanden = IndexOrdnerVerzeichnis.AnzahlVorhanden();
            int bilder = IndexOrdnerVerzeichnis.BilderGesamt();
            int fehlend = gesamt - vorhanden;

            return $"{vorhanden} Ordner · {bilder:N0} Bilder"
                 + (fehlend > 0 ? $" · {fehlend} fehlen" : string.Empty);
        }

        /// <summary>Liest das Verzeichnis neu ein und aktualisiert die Anzeige.</summary>
        private void AktualisiereIndexOrdner()
        {
            IndexOrdnerListe.Clear();

            foreach (var eintrag in IndexOrdnerVerzeichnis.Alle())
            {
                IndexOrdnerListe.Add(eintrag);
            }

            HatIndexOrdner = IndexOrdnerListe.Count > 0;
            IndexOrdnerZusammenfassung = FasseIndexOrdnerZusammen();
            OnPropertyChanged(nameof(OrdnerVerwaltenText));

            // Die Ordnerzahlen im Bereichsmenü hängen an dieser Liste.
            AktualisiereSuchbereichText();

            // Die Kopfzeile der Einstellungen zeigte bisher einen festen Text
            // („indexiert 1/1 Ordner"), unabhängig von der Wirklichkeit.
            int vorhanden = IndexOrdnerVerzeichnis.AnzahlVorhanden();
            IndexOrdnerText = IndexOrdnerListe.Count == 0
                ? "kein Ordner indexiert"
                : $"{vorhanden} Ordner indexiert";
        }

        /// <summary>
        /// Trägt einen Ordner nach, wenn dort eine Indexdatei liegt. Dadurch tauchen auch
        /// Ordner auf, die schon vor dieser Funktion indexiert wurden — man muss sie
        /// nur einmal geöffnet haben.
        /// </summary>
        private void MerkeOrdnerFallsIndiziert(string? ordner)
        {
            IndexOrdnerVerzeichnis.Merke(ordner, bilder: 0);
            AktualisiereIndexOrdner();
        }

        #region Wächter auf die Indexdatei

        private readonly IndexDateiWaechter _indexWaechter = new();

        /// <summary>
        /// Richtet die Überwachung auf den Ordner des angezeigten Bildes ein. Wird bei
        /// jedem Bildwechsel gerufen; derselbe Ordner erneut ist wirkungslos.
        /// </summary>
        private void UeberwacheIndexDatei(string? ordner)
        {
            // Zuweisen statt Ereignis-Abonnement: mehrfaches Setzen bleibt harmlos, und
            // es braucht keinen Konstruktor in dieser Teilklasse.
            _indexWaechter.BeiAenderung = BeiIndexDateiAenderung;
            _indexWaechter.Ueberwache(ordner);
        }

        /// <summary>
        /// Die Indexdatei des überwachten Ordners ist aufgetaucht oder verschwunden —
        /// etwa weil sie von Hand gelöscht wurde.
        /// </summary>
        private void BeiIndexDateiAenderung()
        {
            // Setzt AktuellerOrdnerIndiziert neu; darüber werden „Schema-ähnlich" und
            // die verwandten Befehle freigegeben oder gesperrt.
            //
            // Erzwungen: Es ist genau der Fall, dass sich der Indexstand ändert, ohne
            // dass das Bild oder der Ordner gewechselt hätte.
            PruefeAktuellerOrdnerIndiziert(erzwingen: true);

            // Und die Liste nachziehen: Ein Ordner ohne Indexdatei gilt dort als fehlend.
            AktualisiereIndexOrdner();

            // Der Wächter meldet auch das *Auftauchen* der Datei. Dann hat gerade ein
            // Indexlauf geschrieben, und dessen Meldungen dürfen nicht abgeräumt werden.
            if (AktuellerOrdnerIndiziert || IndexLaeuft)
                return;

            // Index gelöscht, Meldungen blieben stehen.
            //
            // Bisher wurde hier nur der Zustand der Befehle nachgezogen — der Knopf
            // sperrte sich also richtig, während daneben unverändert „Fertig: 293 Bilder
            // indexiert" stand. Die Texte gehören zu einer Datei, die es nicht mehr gibt.
            IndexAnzahlText = string.Empty;
            IndexFortschrittText = "Index gelöscht – der Ordner ist nicht mehr indexiert.";
            WasserzeichenStatus = string.Empty;

            // Auch die Trefferliste gehört zu dem Index, den es nicht mehr gibt. Sie blieb
            // stehen, während die Knöpfe schon gesperrt waren — anklickbare Kacheln zu
            // einer Suche, die sich nicht wiederholen lässt.
            VerwerfeSuchtreffer("Index gelöscht – die Treffer dazu wurden verworfen.");

            // Wasserzeichen-Befunde neu von der Platte lesen statt blind zu leeren:
            // Die Befunddatei ist eine eigene Datei. Liegt sie noch, bleibt sie gültig;
            // wurde sie mitgelöscht, räumt das Laden Abzeichen und Kasten auf.
            LadeWasserzeichenBefunde(AktuellerBildOrdner());
        }

        #endregion

        /// <summary>Nimmt einen Ordner aus dem Verzeichnis. Die Indexdatei bleibt liegen.</summary>
        [RelayCommand]
        private void CommandExecuteIndexOrdnerEntfernen(string? pfad)
        {
            if (IndexOrdnerVerzeichnis.Entferne(pfad))
            {
                AktualisiereIndexOrdner();
            }
        }

        /// <summary>Liest das Verzeichnis neu ein — für den Auffrischen-Knopf in der Liste.</summary>
        [RelayCommand]
        private void CommandExecuteIndexOrdnerAuffrischen() => AktualisiereIndexOrdner();

        /// <summary>Beschriftung des Menüpunktes, der zur Ordnerliste führt.</summary>
        public string OrdnerVerwaltenText =>
            $"Ordner verwalten … ({IndexOrdnerListe.Count})";

        /// <summary>
        /// Ordnerkarte sichtbar. Standardmässig zu — sie gehört zum Verwalten, nicht
        /// zum Suchen, und würde sonst dauerhaft Platz im Panel belegen.
        /// </summary>
        [ObservableProperty]
        public partial bool IsIndexOrdnerKarteOffen { get; set; }

        /// <summary>Menüpunkt „Ordner verwalten …" im Bereichsmenü.</summary>
        [RelayCommand]
        private void CommandExecuteIndexOrdnerZeigen()
        {
            AktualisiereIndexOrdner();
            IsIndexOrdnerKarteOffen = true;
        }

        /// <summary>
        /// Blendet die Ordnerkarte wieder aus. Wird beim Starten einer Suche gerufen:
        /// Ab da geht es um Ergebnisse, und die Verwaltung wäre nur im Weg.
        /// </summary>
        private void SchliesseIndexOrdnerKarte() => IsIndexOrdnerKarteOffen = false;

        /// <summary>
        /// Schliessknopf auf der Karte selbst. Ohne ihn bliebe sie stehen, bis man
        /// irgendetwas sucht — man will sie aber auch einfach wieder wegräumen können.
        /// </summary>
        [RelayCommand]
        private void CommandExecuteIndexOrdnerKarteSchliessen() => SchliesseIndexOrdnerKarte();

        /// <summary>Rückmeldung zum letzten Ordner-Drop. Leer, solange nichts abgelegt wurde.</summary>
        [ObservableProperty]
        public partial string IndexOrdnerHinweis { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteIndexOrdnerAusDropCommand))]
        public partial bool IndexOrdnerSucheLäuft { get; set; }

        private bool CanExecuteIndexOrdnerAusDrop() => !IndexOrdnerSucheLäuft;

        /// <summary>
        /// Nimmt einen abgelegten Ordner samt allen Unterordnern auf, soweit dort
        /// Indexdateien liegen.
        ///
        /// Damit lässt sich eine gewachsene Sammlung in einem Zug nachtragen, statt jeden
        /// Ordner einmal öffnen zu müssen. Indexiert wird dabei nichts — es wird nur
        /// gefunden, was schon einen Index hat.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteIndexOrdnerAusDrop), IncludeCancelCommand = true)]
        private async Task CommandExecuteIndexOrdnerAusDrop(string? wurzel, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(wurzel) || !Directory.Exists(wurzel))
            {
                return;
            }

            IndexOrdnerSucheLäuft = true;
            IndexOrdnerHinweis = "Suche Indexdateien …";

            try
            {
                var gefunden = await Task.Run(() => SucheIndizierteOrdner(wurzel, token), token);

                int neu = 0;
                foreach (string ordner in gefunden)
                {
                    if (IndexOrdnerVerzeichnis.Alle().All(
                            e => !string.Equals(e.Pfad, ordner, StringComparison.OrdinalIgnoreCase)))
                    {
                        neu++;
                    }

                    IndexOrdnerVerzeichnis.Merke(ordner, bilder: 0);
                }

                AktualisiereIndexOrdner();

                IndexOrdnerHinweis = gefunden.Count == 0
                    ? "Dort liegen keine indizierten Ordner."
                    : $"{gefunden.Count} indizierte Ordner gefunden, {neu} neu aufgenommen.";
            }
            catch (OperationCanceledException)
            {
                IndexOrdnerHinweis = "Suche abgebrochen.";
            }
            catch (Exception ex)
            {
                IndexOrdnerHinweis = "Fehler bei der Suche: " + ex.Message;
            }
            finally
            {
                IndexOrdnerSucheLäuft = false;
            }
        }

        /// <summary>
        /// Durchläuft den Baum und sammelt alle Ordner mit Indexdatei.
        ///
        /// Bewusst über einen eigenen Stapel statt <c>EnumerateDirectories</c> mit
        /// <c>AllDirectories</c>: Das bricht bei einem einzigen unzugänglichen
        /// Unterordner den gesamten Durchlauf ab. So wird nur der betroffene Zweig
        /// übersprungen.
        /// </summary>
        private static List<string> SucheIndizierteOrdner(string wurzel, CancellationToken token)
        {
            var treffer = new List<string>();
            var offen = new Stack<string>();
            offen.Push(wurzel);

            while (offen.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string aktuell = offen.Pop();

                try
                {
                    if (File.Exists(Path.Combine(aktuell, BildAnalyseService.CacheDateiName)))
                    {
                        treffer.Add(aktuell);
                    }

                    foreach (string unter in Directory.EnumerateDirectories(aktuell))
                    {
                        offen.Push(unter);
                    }
                }
                catch
                {
                    // Zugriff verweigert o. ä. – diesen Zweig auslassen, Rest weiterlaufen.
                }
            }

            return treffer;
        }

        #region Suchbereich

        /// <summary>
        /// Ordnernamen, die bei „mit Unterordnern" nicht mitgesucht werden.
        ///
        /// Das sind die Ablagen, die diese Anwendung selbst anlegt. Ohne diese Ausnahme
        /// käme genau das wieder in jedes Ergebnis, was du zuvor aussortiert hast.
        /// </summary>
        private static readonly string[] AussortiertOrdner =
            { "kein_Fav", "KI_Fehler", "Doppelt", "Besonders", "Wasserzeichen" };

        /// <summary>Nur der Ordner des angezeigten Bildes (bisheriges Verhalten).</summary>
        public const int BereichOrdner = 0;

        /// <summary>Der Ordner des Bildes und alles darunter, was indiziert ist.</summary>
        public const int BereichZweig = 1;

        /// <summary>Alle bekannten indizierten Ordner.</summary>
        public const int BereichAlle = 2;

        /// <summary>
        /// Suchbereich für „Schema-ähnlich". Absichtlich ein einzelner Schalter am Knopf
        /// statt mehrerer Knöpfe — sonst gäbe es je Bereich einen eigenen Befehl, einen
        /// eigenen Abbrechen-Knopf und einen eigenen Fortschritt für dieselbe Sache.
        /// </summary>
        [ObservableProperty]
        public partial int Suchbereich { get; set; } = BereichOrdner;

        partial void OnSuchbereichChanged(int value) => AktualisiereSuchbereichText();

        /// <summary>
        /// Meldet alles, was vom Bereich abhängt. Auch nach einem Ordnerwechsel zu rufen,
        /// weil sich dann die Anzahl der betroffenen Ordner ändert.
        /// </summary>
        private void AktualisiereSuchbereichText()
        {
            OnPropertyChanged(nameof(SuchbereichText));
            OnPropertyChanged(nameof(SchemaKnopfText));
            OnPropertyChanged(nameof(BereichZweigText));
            OnPropertyChanged(nameof(BereichAlleText));
            OnPropertyChanged(nameof(BereichIstOrdner));
            OnPropertyChanged(nameof(BereichIstZweig));
            OnPropertyChanged(nameof(BereichIstAlle));
        }

        /// <summary>Beschriftung des Schalters, mit Anzahl — damit der Zustand sichtbar ist.</summary>
        public string SuchbereichText => Suchbereich switch
        {
            BereichZweig => $"mit Unterordnern ({ErmittleSuchOrdner().Count})",
            BereichAlle => $"alle Ordner ({ErmittleSuchOrdner().Count})",
            _ => "dieser Ordner"
        };

        /// <summary>
        /// Beschriftung des Suchknopfes. Im Normalfall kurz, sonst mit dem Bereich —
        /// so bleibt der Zustand sichtbar, ohne dass ein eigenes Feld dafür Platz kostet.
        /// </summary>
        public string SchemaKnopfText => Suchbereich switch
        {
            BereichZweig => $"Schema-ähnlich · Zweig ({ErmittleSuchOrdner().Count})",
            BereichAlle => $"Schema-ähnlich · alle ({ErmittleSuchOrdner().Count})",
            _ => "Schema-ähnlich"
        };

        public string BereichZweigText => $"mit Unterordnern ({ZaehleOrdner(BereichZweig)})";

        public string BereichAlleText => $"alle Ordner ({ZaehleOrdner(BereichAlle)})";

        /// <summary>
        /// Anzahl der Ordner, die ein Bereich umfassen würde — ohne den aktuellen Bereich
        /// anzufassen.
        ///
        /// Vorher setzte diese Funktion <c>Suchbereich</c> kurzzeitig um. Das löste die
        /// Benachrichtigung für die Beschriftungen aus, die Bindung las sie neu, und dabei
        /// wurde wieder hier hereingerufen — eine Endlosschleife, die sofort beim Start
        /// zum Stapelüberlauf führte.
        /// </summary>
        private int ZaehleOrdner(int bereich) => ErmittleSuchOrdner(bereich).Count;

        // Für die Häkchen im Aufklappmenü. Als drei Eigenschaften statt eines Konverters:
        // MenuItem.IsChecked ist ein bool, und das Setzen auf false beim Abwählen soll
        // nichts bewirken – die Auswahl wird über das jeweils angeklickte Element gesetzt.
        public bool BereichIstOrdner
        {
            get => Suchbereich == BereichOrdner;
            set
            {
                if (value)
                {
                    Suchbereich = BereichOrdner;
                }
            }
        }

        public bool BereichIstZweig
        {
            get => Suchbereich == BereichZweig;
            set
            {
                if (value)
                {
                    Suchbereich = BereichZweig;
                }
            }
        }

        public bool BereichIstAlle
        {
            get => Suchbereich == BereichAlle;
            set
            {
                if (value)
                {
                    Suchbereich = BereichAlle;
                }
            }
        }

        /// <summary>
        /// Welche Ordner der gewählte Bereich tatsächlich umfasst — fehlende sind bereits
        /// aussortiert, ebenso die Ablagen dieser Anwendung.
        /// </summary>
        public System.Collections.Generic.List<string> ErmittleSuchOrdner()
            => ErmittleSuchOrdner(Suchbereich);

        /// <summary>
        /// Wie oben, aber für einen ausdrücklich angegebenen Bereich — damit sich die
        /// Ordnerzahlen der Menüpunkte berechnen lassen, ohne die Auswahl zu verändern.
        /// </summary>
        public System.Collections.Generic.List<string> ErmittleSuchOrdner(int bereich)
        {
            var leer = new System.Collections.Generic.List<string>();

            string? heimat = AktuellerBildOrdner();
            if (string.IsNullOrEmpty(heimat))
            {
                return leer;
            }

            if (bereich == BereichOrdner)
            {
                return new System.Collections.Generic.List<string> { heimat };
            }

            var bekannt = IndexOrdnerVerzeichnis.Alle()
                .Where(e => e.Existiert)
                .Select(e => e.Pfad);

            if (bereich == BereichZweig)
            {
                string wurzel = heimat.TrimEnd(System.IO.Path.DirectorySeparatorChar)
                                + System.IO.Path.DirectorySeparatorChar;

                bekannt = bekannt.Where(p =>
                    string.Equals(p, heimat, System.StringComparison.OrdinalIgnoreCase)
                    || p.StartsWith(wurzel, System.StringComparison.OrdinalIgnoreCase));
            }

            var ergebnis = bekannt.Where(p => !IstAussortiert(p)).ToList();

            // Der eigene Ordner gehört immer dazu, auch wenn er noch nicht im
            // Verzeichnis steht – sonst fehlte ausgerechnet das Anfragebild.
            if (!ergebnis.Contains(heimat, System.StringComparer.OrdinalIgnoreCase))
            {
                ergebnis.Insert(0, heimat);
            }

            return ergebnis;
        }

        /// <summary>True, wenn der Ordner eine der Ablagen dieser Anwendung ist.</summary>
        private static bool IstAussortiert(string pfad)
        {
            string name = System.IO.Path.GetFileName(
                pfad.TrimEnd(System.IO.Path.DirectorySeparatorChar));

            return AussortiertOrdner.Contains(name, System.StringComparer.OrdinalIgnoreCase);
        }

        #endregion
    }
}
