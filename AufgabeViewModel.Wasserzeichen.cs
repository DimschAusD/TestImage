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
        private string _wasserzeichenStatus = string.Empty;

        /// <summary>Anzahl der im aktuellen Ordner gefundenen Bilder mit Markierung.</summary>
        [ObservableProperty]
        private int _wasserzeichenTrefferAnzahl;

        /// <summary>True, wenn mindestens ein Muster gelernt wurde – sonst greift nur die Metadatenprüfung.</summary>
        [ObservableProperty]
        private bool _wasserzeichenMaskeVorhanden = WasserzeichenService.MaskeVorhanden;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteWasserzeichenMaskeLernenCommand))]
        private bool _wasserzeichenAufgabeLäuft;

        /// <summary>
        /// Karte aufgeklappt. Gleiche Mechanik wie <c>IsIndexPopoverOffen</c> bei den
        /// Einstellungen: ein Knopf oben schaltet um, die Karte hängt an dieser Eigenschaft.
        /// </summary>
        [ObservableProperty]
        private bool _isWasserzeichenOffen;

        /// <summary>
        /// Auswahl der Stelle im Bild, als Index der Auswahlliste. 0 heisst „alle
        /// Bereiche" und ist die Vorgabe — dann sucht das Lernen die Stelle selbst.
        ///
        /// Die Liste beginnt bewusst mit „alle", die Aufzählung dagegen mit „Mitte":
        /// deren Zahlen stehen so in bereits gespeicherten Mustern und dürfen sich nicht
        /// verschieben. Deshalb wird hier umgerechnet statt einfach gecastet.
        /// </summary>
        [ObservableProperty]
        private int _wasserzeichenLernBereich;

        private WasserzeichenBereich GewaehlterLernBereich =>
            WasserzeichenLernBereich <= 0
                ? WasserzeichenBereich.Alle
                : (WasserzeichenBereich)(WasserzeichenLernBereich - 1);

        /// <summary>Klappt die Wasserzeichen-Karte auf und zu.</summary>
        [RelayCommand]
        private void CommandExecuteWasserzeichenToggle()
            => IsWasserzeichenOffen = !IsWasserzeichenOffen;

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
        /// Lernt ein Muster aus einem Ordner, in dem alle Bilder denselben Zeichentyp
        /// tragen. Der Ordnername wird zum Namen des Musters — so entsteht die Sammlung
        /// nebenbei, ohne dass nach jedem Lernen noch ein Namensdialog kommt.
        /// Ohne mindestens ein Muster kann nur nach Metadaten gesucht werden.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteWasserzeichenMaskeLernen), IncludeCancelCommand = true)]
        private async Task CommandExecuteWasserzeichenMaskeLernen(CancellationToken token)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Ordner mit Beispielbildern – alle müssen denselben Zeichentyp tragen",

                // Beim Ordner des angezeigten Bildes beginnen: Die Beispiele liegen fast
                // immer dort oder gleich daneben, sonst müsste man jedes Mal neu dorthin
                // navigieren.
                InitialDirectory = AktuellerBildOrdner() ?? OrdnerVomDropBild() ?? string.Empty
            };

            if (dlg.ShowDialog() != true)
                return;

            string name = NameAusOrdner(dlg.FolderName);

            WasserzeichenAufgabeLäuft = true;
            WasserzeichenStatus = $"Lerne Muster „{name}“ …";

            try
            {
                // Restzeit über dieselben Helfer wie die Dubletten-Suche – gleiche
                // Rechnung, gleiche Formulierung („noch ca. …"), keine zweite Fassung.
                var uhr = System.Diagnostics.Stopwatch.StartNew();

                var fortschritt = new Progress<(int Erledigt, int Gesamt)>(p =>
                    WasserzeichenStatus = $"Lerne Muster „{name}“ … {p.Erledigt}/{p.Gesamt}"
                                          + RestzeitZusatz(uhr.Elapsed, p.Erledigt, p.Gesamt));

                int anzahl = await WasserzeichenService.LerneMaskeAsync(
                    dlg.FolderName, name, GewaehlterLernBereich, fortschritt, token);

                AktualisiereWasserzeichenMuster();

                // Bei „alle Bereiche" ist die gefundene Stelle das eigentlich Interessante.
                string stelle = WasserzeichenMuster
                    .FirstOrDefault(m => string.Equals(m.MusterName, name, StringComparison.OrdinalIgnoreCase))
                    ?.BereichName ?? string.Empty;

                WasserzeichenStatus = anzahl > 0
                    ? $"Muster „{name}“ aus {anzahl} Bildern gelernt"
                      + (stelle.Length > 0 ? $" – Stelle: {stelle}" : string.Empty)
                      + "."
                      + WasserzeichenService.LetzteLernMeldung
                      + " Ordner neu indexieren, um es anzuwenden."
                    : "Zu wenige oder unlesbare Bilder – es werden mindestens 5 gebraucht.";
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

        /// <summary>Entfernt ein gelerntes Muster aus der Sammlung.</summary>
        [RelayCommand]
        private void CommandExecuteWasserzeichenMusterEntfernen(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            if (WasserzeichenService.EntferneMaske(name))
            {
                AktualisiereWasserzeichenMuster();
                WasserzeichenStatus = $"Muster „{name}“ entfernt.";
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

                int treffer = WasserzeichenTrefferAnzahl;
                WasserzeichenStatus = treffer == 0
                    ? "Keine Wasserzeichen gefunden."
                    : $"{treffer} Bild(er) mit Wasserzeichen oder Metadaten-Markierung.";
            }
            catch (OperationCanceledException)
            {
                WasserzeichenStatus = "Wasserzeichen-Prüfung abgebrochen.";
            }
            catch (Exception ex)
            {
                WasserzeichenStatus = "Fehler bei der Wasserzeichen-Prüfung: " + ex.Message;
            }
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
                WasserzeichenTrefferAnzahl = 0;
                AktualisiereWasserzeichenBefundAnzeige();
                return;
            }

            UebertrageWasserzeichenBefunde(befunde);
            AktualisiereWasserzeichenBefundAnzeige();
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
        private string _wasserzeichenBefundDatei = string.Empty;

        /// <summary>Urteil in einem Satz.</summary>
        [ObservableProperty]
        private string _wasserzeichenBefundText = "Kein Bild gewählt.";

        /// <summary>Bestes Muster samt Stelle, z. B. „anilvlai · oben rechts".</summary>
        [ObservableProperty]
        private string _wasserzeichenBefundMuster = string.Empty;

        /// <summary>Übereinstimmung im Verhältnis zur Schwelle, im Klartext.</summary>
        [ObservableProperty]
        private string _wasserzeichenBefundWert = string.Empty;

        /// <summary>Vorschaubild des Musters, das am besten passte.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(WasserzeichenBefundHatBild))]
        private System.Windows.Media.ImageSource? _wasserzeichenBefundBild;

        /// <summary>Steuert das Vorschaukästchen – ein eigener Konverter wäre dafür zu viel.</summary>
        public bool WasserzeichenBefundHatBild => WasserzeichenBefundBild is not null;

        /// <summary>Gefundene Metadaten-Markierungen, je Zeile eine.</summary>
        [ObservableProperty]
        private string _wasserzeichenBefundMetadaten = string.Empty;

        /// <summary>True, wenn das Bild tatsächlich eine Markierung trägt (färbt den Hinweis).</summary>
        [ObservableProperty]
        private bool _wasserzeichenBefundIstTreffer;

        /// <summary>
        /// Stellt den Befund zum gewählten Bild zusammen. Vier Fälle, die sich für den
        /// Nutzer deutlich unterscheiden — besonders „noch nicht geprüft" darf nicht wie
        /// „sauber" aussehen.
        /// </summary>
        private void AktualisiereWasserzeichenBefundAnzeige()
        {
            string? pfad = SelectedBildchen?.BName;

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

            // Muster samt Stelle – den Bereich holen wir aus der Musterliste.
            var eintrag = WasserzeichenMuster.FirstOrDefault(
                m => string.Equals(m.MusterName, befund.MaskenName, StringComparison.OrdinalIgnoreCase));

            WasserzeichenBefundMuster = eintrag is null
                ? befund.MaskenName
                : $"{eintrag.MusterName} · {eintrag.BereichName}";

            WasserzeichenBefundBild = eintrag?.Vorschau;

            WasserzeichenBefundWert = string.IsNullOrEmpty(befund.MaskenName)
                ? string.Empty
                : $"Übereinstimmung {wert * 100f:F0} % · Schwelle {schwelle * 100f:F0} %";

            WasserzeichenBefundMetadaten = befund.MetadatenHinweise.Count == 0
                ? string.Empty
                : string.Join("\n", befund.MetadatenHinweise);

            if (befund.HatSichtbares)
            {
                WasserzeichenBefundText = wert >= schwelle * 2
                    ? "Wasserzeichen erkannt – deutlich über der Schwelle."
                    : "Wasserzeichen erkannt – knapp über der Schwelle.";
            }
            else if (befund.HatMetadaten)
            {
                WasserzeichenBefundIstTreffer = true;
                WasserzeichenBefundText = "Kein sichtbares Zeichen, aber Markierungen in den Metadaten.";
            }
            else if (string.IsNullOrEmpty(befund.MaskenName))
            {
                WasserzeichenBefundText = "Keine Markierung gefunden – es ist noch kein Muster gelernt.";
            }
            else if (wert >= schwelle * 0.6f)
            {
                WasserzeichenBefundText = "Kein Treffer, aber nahe an der Schwelle – ansehen lohnt sich.";
            }
            else
            {
                WasserzeichenBefundText = "Keine Markierung gefunden.";
            }
        }

        #endregion
    }
}
