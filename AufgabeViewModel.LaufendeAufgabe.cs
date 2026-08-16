using CommunityToolkit.Mvvm.Input;

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
        /// <summary>True, solange irgendein langlaufender Vorgang arbeitet.</summary>
        public bool AufgabeLäuft => IndexLaeuft || PrüfungLäuft || WasserzeichenAufgabeLäuft;

        /// <summary>Fortschritt des laufenden Vorgangs in Prozent, 0 … 100.</summary>
        public double AufgabeFortschritt =>
            IndexLaeuft ? IndexFortschritt :
            PrüfungLäuft ? PercentageValueVerschieben :
            0;

        /// <summary>
        /// True, solange es keinen zählbaren Fortschritt gibt — beim Wasserzeichen-Lernen
        /// und in der Anlaufphase des Indexierens, in der CLIP seine Modelle lädt. Der
        /// Balken läuft dann als Schraffur durch, statt bei null zu stehen und wie ein
        /// Hänger auszusehen.
        /// </summary>
        public bool AufgabeUnbestimmt =>
            AufgabeLäuft && (WasserzeichenAufgabeLäuft || AufgabeFortschritt <= 0);

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

                if (PrüfungLäuft)
                {
                    return $"Prüfe Bilder … {ProzentAbgleich} %";
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
        partial void OnProzentAbgleichChanged(string value) => MeldeAufgabeGeaendert();
        partial void OnWasserzeichenAufgabeLäuftChanged(bool value) => MeldeAufgabeGeaendert();
        partial void OnWasserzeichenStatusChanged(string value) => MeldeAufgabeGeaendert();
    }
}
