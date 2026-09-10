using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace TestImage
{
    /// <summary>
    /// Eine Anzeige für alle langlaufenden Vorgänge.
    ///
    /// Vorher gab es vier Fortschrittsanzeigen mit vier eigenen Wahrheiten: den Balken
    /// über der Miniaturleiste (Byte-/SHA-/Grau-Abgleich), zwei Instanzen der
    /// Indexierungs-Anzeige und den reinen Statustext des Wasserzeichen-Lernens. Dazu
    /// ein Abbrechen-Knopf, der gar kein Command hatte.
    ///
    /// <b>Diese Klasse ändert an den Vorgängen selbst nichts.</b> Sie liest nur die
    /// vorhandenen Eigenschaften und fasst sie zusammen. Damit kann beim Zusammenlegen
    /// nichts von dem verlorengehen, was die einzelnen Befehle tun.
    ///
    /// Dass immer nur <i>ein</i> Vorgang laufen kann, sichern die CanExecute-Prüfungen
    /// der schweren Befehle: <c>PrüfungLäuft</c> sperrt das Indexieren, <c>IndexLaeuft</c>
    /// sperrt die Abgleiche. Ohne diese Sperre müsste hier entschieden werden, welcher
    /// von zwei gleichzeitigen Vorgängen angezeigt wird — und der andere liefe unsichtbar
    /// weiter.
    /// </summary>
    public partial class AufgabeViewModel
    {
        /// <summary>
        /// <c>PrüfungLäuft</c>, aber ohne das blosse Anzeigen eines Bildes.
        ///
        /// Der Schalter bedeutet zweierlei. Die langen Vorgänge setzen ihn — Byte- und
        /// SHA-Abgleich, Grau-Abgleich, Verschieben aller Bilder. <b>Das Laden des
        /// gerade gewählten Bildes setzt ihn aber auch</b>, in beiden Zweigen von
        /// <c>CommandExecuteKleinesBildGrossesBildLaden</c>.
        ///
        /// Ohne diese Unterscheidung blitzte der Fortschrittsstreifen in der Kopfleiste
        /// bei jedem Pfeildruck auf, mit „Prüfe Bilder …" und laufender Schraffur — für
        /// einen Vorgang von Millisekunden, der seine eigene Anzeige hat:
        /// <c>PGB_BildLadenStufen</c> neben der Ampel.
        ///
        /// Der Streifen ist für das gedacht, was dauert und was man abbrechen können soll.
        /// </summary>
        private bool PruefungOhneBildladen =>
            PrüfungLäuft && !CommandExecuteKleinesBildGrossesBildLadenCommand.IsRunning;

        /// <summary>True, solange irgendein langlaufender Vorgang arbeitet.</summary>
        public bool AufgabeLäuft => IndexLaeuft || PruefungOhneBildladen || WasserzeichenAufgabeLäuft;

        /// <summary>
        /// Es wird auf etwas gewartet — anders als bei <see cref="AufgabeLäuft"/> zählt
        /// hier das Laden des Bildes mit.
        ///
        /// Dass die beiden sich unterscheiden, ist Absicht. Der Fortschrittsstreifen der
        /// Kopfleiste zeigt nur, was dauert und was man abbrechen kann; nähme er das
        /// Bildladen dazu, blitzte er bei jedem Pfeildruck auf (Begründung bei
        /// <see cref="PruefungOhneBildladen"/>). Die Vollbildansicht hat aber weder diesen
        /// Streifen noch <c>PGB_BildLadenStufen</c> — dort steht das Bild auf einer
        /// langsamen Platte kommentarlos still, und genau dafür ist der Warte-Indikator da.
        /// </summary>
        public bool WartenLäuft =>
            AufgabeLäuft
            || CommandExecuteKleinesBildGrossesBildLadenCommand.IsRunning
            || OrdnerEinlesenLäuft;

        /// <summary>
        /// True, solange ein abgelegter Ordner eingelesen wird (<c>OnFileDrop</c>).
        ///
        /// Der Vorgang hatte bis dahin keine Anzeige: Er setzt weder <c>PrüfungLäuft</c>
        /// noch <c>IndexLaeuft</c>, läuft aber je nach Laufwerk ein bis zwei Sekunden.
        /// Zu sehen war in dieser Zeit nichts als das alte Bild — auf einem Netz- oder
        /// Wechsellaufwerk sah das aus, als sei die Ablage ins Leere gegangen.
        ///
        /// Bewusst nicht in <see cref="AufgabeLäuft"/>: Der Fortschrittsstreifen der
        /// Kopfleiste zeigt nur, was man abbrechen kann, und das Einlesen kann man
        /// nicht abbrechen. Es gehört zum Warten, nicht zu den Aufgaben.
        /// </summary>
        [ObservableProperty]
        public partial bool OrdnerEinlesenLäuft { get; set; }

        partial void OnOrdnerEinlesenLäuftChanged(bool value) =>
            OnPropertyChanged(nameof(WartenLäuft));

        /// <summary>
        /// Hängt <see cref="WartenLäuft"/> an das Bildladen. Aufruf aus dem Konstruktor.
        ///
        /// <c>IsRunning</c> meldet sich am Befehl selbst, nicht am ViewModel — ohne dieses
        /// Weiterreichen erführe die Oberfläche nie, dass ein Ladevorgang begonnen oder
        /// geendet hat.
        /// </summary>
        private void WarteAnzeigeAnkoppeln()
        {
            CommandExecuteKleinesBildGrossesBildLadenCommand.PropertyChanged += (_, e) =>
            {
                // Leerer Name heisst „alles hat sich geändert" – dann gilt es auch hier.
                if (string.IsNullOrEmpty(e.PropertyName)
                    || e.PropertyName == nameof(IAsyncRelayCommand.IsRunning))
                {
                    OnPropertyChanged(nameof(WartenLäuft));
                }
            };
        }

        /// <summary>Fortschritt des laufenden Vorgangs in Prozent, 0 … 100.</summary>
        public double AufgabeFortschritt
        {
            get
            {
                if (IndexLaeuft)
                {
                    return IndexFortschritt;
                }

                if (WasserzeichenAufgabeLäuft)
                {
                    return WasserzeichenFortschritt;
                }

                // Eine Quelle für alle Prüfbefehle. Vorher gab es daneben noch
                // ProzentAbgleich — einen fertig formatierten Anzeigetext, den diese
                // Eigenschaft zurücklesen musste. Er ist entfallen; jede Prüfung meldet
                // jetzt an PercentageValueVerschieben.
                return PruefungOhneBildladen ? PercentageValueVerschieben : 0;
            }
        }

        /// <summary>
        /// True, solange es keinen zählbaren Fortschritt gibt — in der Anlaufphase des
        /// Indexierens, in der CLIP seine Modelle lädt, und bis zur ersten Stückmeldung
        /// der übrigen Vorgänge. Der Balken läuft dann als Schraffur durch, statt bei
        /// null zu stehen und wie ein Hänger auszusehen.
        ///
        /// Das Wasserzeichen-Lernen stand hier früher fest drin, weil es nichts Zählbares
        /// meldete. Es meldet jetzt (<see cref="WasserzeichenFortschritt"/>) und läuft
        /// deshalb über dieselbe Regel wie alle anderen.
        /// </summary>
        public bool AufgabeUnbestimmt => AufgabeLäuft && AufgabeFortschritt <= 0;

        /// <summary>Statustext des laufenden Vorgangs.</summary>
        public string AufgabeText
        {
            get
            {
                if (IndexLaeuft)
                {
                    return IndexFortschrittText;
                }

                if (WasserzeichenAufgabeLäuft)
                {
                    return WasserzeichenStatus;
                }

                if (PruefungOhneBildladen)
                {
                    // Keine Zahl nennen, solange keine da ist. Text und Balken müssen
                    // beide aus AufgabeFortschritt lesen — greift der Text auf eine
                    // andere Quelle (ProzentAbgleich, PercentageValueVerschieben),
                    // läuft der Balken als Schraffur, während daneben „0 %" steht.
                    return AufgabeUnbestimmt
                        ? "Prüfe Bilder …"
                        : $"Prüfe Bilder … {AufgabeFortschritt:F0} %";
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// True, wenn der laufende Vorgang abgebrochen werden kann. Alle langlaufenden
        /// Befehle sind mit <c>IncludeCancelCommand</c> gebaut, das trifft also zu —
        /// die Eigenschaft blendet den Knopf aber sauber aus, wenn nichts läuft.
        /// </summary>
        public bool AufgabeAbbrechbar => AufgabeLäuft && FindeAbbruch() is not null;

        private bool CanExecuteAufgabeAbbrechen() => AufgabeLäuft;

        /// <summary>Bricht den gerade laufenden Vorgang ab, welcher es auch sei.</summary>
        [RelayCommand(CanExecute = nameof(CanExecuteAufgabeAbbrechen))]
        private void CommandExecuteAufgabeAbbrechen()
        {
            var abbruch = FindeAbbruch();
            if (abbruch is not null && abbruch.CanExecute(null))
            {
                abbruch.Execute(null);
            }
        }

        /// <summary>
        /// Sucht den Abbrechen-Befehl des laufenden Vorgangs.
        ///
        /// Gefragt wird nach <c>IsRunning</c> des jeweiligen Befehls, nicht nach den
        /// Zustandsschaltern: <c>PrüfungLäuft</c> wird von einem knappen Dutzend Befehle
        /// gesetzt, und nur der tatsächlich laufende darf abgebrochen werden.
        /// </summary>
        private System.Windows.Input.ICommand? FindeAbbruch()
        {
            if (CommandExecuteOrdnerIndexierenCommand.IsRunning)
            {
                return CommandExecuteOrdnerIndexierenCancelCommand;
            }

            if (CommandExecuteWasserzeichenMaskeLernenCommand.IsRunning)
            {
                return CommandExecuteWasserzeichenMaskeLernenCancelCommand;
            }

            if (CommandExecuteSuchenGleichesBildByteVergleichCommand.IsRunning)
            {
                return CommandExecuteSuchenGleichesBildByteVergleichCancelCommand;
            }

            if (CommandExecuteAlleBilderMiteinanderAufByteGleichheitPrüfenCommand.IsRunning)
            {
                return CommandExecuteAlleBilderMiteinanderAufByteGleichheitPrüfenCancelCommand;
            }

            if (CommandExecuteAlleBilderSHA256AbgleichPrüfenCommand.IsRunning)
            {
                return CommandExecuteAlleBilderSHA256AbgleichPrüfenCancelCommand;
            }

            if (CommandExecuteSuchenUngefährGleichesBildCommand.IsRunning)
            {
                return CommandExecuteSuchenUngefährGleichesBildCancelCommand;
            }

            if (CommandExecuteAlleBilderInsKeinFavVerschiebenCommand.IsRunning)
            {
                return CommandExecuteAlleBilderInsKeinFavVerschiebenCancelCommand;
            }

            return null;
        }

        /// <summary>
        /// Meldet der Oberfläche, dass sich die zusammengefassten Werte geändert haben.
        /// Wird aus den <c>On…Changed</c>-Haken der Quelleigenschaften gerufen.
        /// </summary>
        private void MeldeAufgabeGeaendert()
        {
            OnPropertyChanged(nameof(AufgabeLäuft));
            OnPropertyChanged(nameof(WartenLäuft));
            OnPropertyChanged(nameof(AufgabeFortschritt));
            OnPropertyChanged(nameof(AufgabeUnbestimmt));
            OnPropertyChanged(nameof(AufgabeText));
            OnPropertyChanged(nameof(AufgabeAbbrechbar));
            CommandExecuteAufgabeAbbrechenCommand.NotifyCanExecuteChanged();
        }

        partial void OnPrüfungLäuftChanged(bool value) => MeldeAufgabeGeaendert();
        partial void OnIndexLaeuftChanged(bool value) => MeldeAufgabeGeaendert();
        partial void OnIndexFortschrittChanged(double value) => MeldeAufgabeGeaendert();
        partial void OnIndexFortschrittTextChanged(string value) => MeldeAufgabeGeaendert();
        partial void OnPercentageValueVerschiebenChanged(double value) => MeldeAufgabeGeaendert();
        partial void OnWasserzeichenAufgabeLäuftChanged(bool value)
        {
            OnPropertyChanged(nameof(WasserzeichenUnbestimmt));
            MeldeAufgabeGeaendert();
        }
        partial void OnWasserzeichenStatusChanged(string value) => MeldeAufgabeGeaendert();
    }
}
