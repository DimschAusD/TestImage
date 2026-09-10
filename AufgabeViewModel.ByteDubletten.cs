using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestImage.Bildersuche;

namespace TestImage
{
    /// <summary>
    /// Byte-Dubletten-Ansicht: sucht im Dubletten-Ordner alles, was byte-identisch
    /// auch im Referenzbestand liegt, und räumt es weg.
    /// </summary>
    public partial class AufgabeViewModel
    {
        #region Ansicht umschalten

        /// <summary>Dritte Ansicht (Byte-Dubletten aufräumen) aktiv.</summary>
        [ObservableProperty]
        public partial bool IsDublettenAnsicht { get; set; }

        [RelayCommand]
        private void CommandExecuteDublettenAnsichtOeffnen()
        {
            // Bildmodus verlassen, sonst liegen zwei Vollflächen-Ansichten übereinander.
            IsImageMaximiert = false;

            // Erst sichtbar machen, dann vorbelegen: Das Einlesen des Ordners läuft
            // asynchron, und so sieht man die Arbeitsanzeige von Anfang an.
            IsDublettenAnsicht = true;

            UebernimmBildOrdnerAlsDublettenOrdner();
        }

        /// <summary>
        /// Pfad, den die Ansicht zuletzt selbst eingesetzt hat. Nur daran lässt sich eine
        /// eigene Vorbelegung von einer Angabe des Nutzers unterscheiden — getippt,
        /// gezogen oder im Dialog gewählt. Fremde Angaben werden nie überschrieben.
        /// </summary>
        private string? _vorbelegterDublettenOrdner;

        /// <summary>
        /// Belegt den Dubletten-Ordner mit dem Ordner des gerade angezeigten Bildes vor.
        ///
        /// Bisher geschah das nur, solange das Feld leer war. Nach dem ersten Öffnen
        /// blieb der Pfad damit für immer stehen: Wer danach ein Bild aus einem anderen
        /// Ordner ablegte und die Ansicht erneut öffnete, sah weiter den alten Ordner —
        /// und darunter eine Trefferliste, die zu einem dritten Ordner gehörte.
        ///
        /// Jetzt zieht die Vorbelegung mit, solange im Feld noch der zuletzt selbst
        /// eingesetzte Pfad steht. Sobald der Nutzer selbst etwas einträgt, zieht oder
        /// wählt, bleibt seine Angabe unangetastet.
        /// </summary>
        private void UebernimmBildOrdnerAlsDublettenOrdner()
        {
            // Die Ansicht lässt sich schliessen, während Suche oder Löschlauf noch
            // laufen. Wird sie dann erneut geöffnet, darf ihr nicht der Ordner unter
            // den Füssen weggezogen werden.
            if (IsDublettenAufgabeLäuft)
                return;

            string? bildOrdner = AktuellerBildOrdner();

            if (string.IsNullOrWhiteSpace(bildOrdner))
                return;

            bool stammtVonUns =
                string.IsNullOrWhiteSpace(DublettenOrdner)
                || string.Equals(DublettenOrdner, _vorbelegterDublettenOrdner,
                                 StringComparison.OrdinalIgnoreCase);

            if (!stammtVonUns)
                return;

            // Steht schon da – nicht bei jedem Öffnen denselben Ordner neu einlesen.
            if (string.Equals(DublettenOrdner, bildOrdner, StringComparison.OrdinalIgnoreCase))
                return;

            DublettenOrdner = bildOrdner;
            _vorbelegterDublettenOrdner = bildOrdner;

            // Denselben Weg nehmen wie ein Drop auf die Zeile: Der Inhalt des neuen
            // Ordners wird aufgelistet, und ein Suchergebnis zum alten Ordner
            // verschwindet dabei, statt mit fremden Pfaden stehen zu bleiben.
            CommandExecuteDublettenOrdnerNeuLesenCommand.Execute(null);
        }

        [RelayCommand]
        private void CommandExecuteDublettenAnsichtSchliessen()
        {
            IsDublettenAnsicht = false;
        }

        /// <summary>
        /// Ordner des gerade angezeigten Bildes, sonst null.
        ///
        /// Zweite Quelle <see cref="DropDateiName"/>: Beim Ablegen eines Bildes steht der
        /// Pfad sofort fest, <see cref="SelectedBildchen"/> wird aber erst gesetzt, wenn
        /// der Ordner durchgelaufen ist und der Eintrag in der Liste steht. Dazwischen
        /// liegt bei einem grossen Ordner spürbar Zeit, und wer in dieser Spanne die
        /// Dubletten-Ansicht öffnet, bekam einen leeren Pfad — obwohl das Bild sichtbar
        /// auf dem Schirm lag. Dasselbe gilt, wenn die Auswahl leer ist, weil das
        /// abgelegte Bild gar nicht in der Liste landet (ausgefiltert, Einlesen
        /// abgebrochen).
        /// </summary>
        private string? AktuellerBildOrdner()
        {
            var pfad = SelectedBildchen?.BName;

            if (string.IsNullOrWhiteSpace(pfad))
                pfad = DropDateiName;

            if (string.IsNullOrWhiteSpace(pfad))
                return null;

            try { return Path.GetDirectoryName(pfad); }
            catch { return null; }
        }

        #endregion

        #region Zustand

        /// <summary>
        /// Ordner, aus dem gelöscht wird. Alles darin, was byte-identisch auch in einem
        /// Referenzordner liegt, kommt weg — die Seite, die im Dateimanager „links" wäre.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteByteDublettenSuchenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteOrdnerseitenTauschenCommand))]
        public partial string DublettenOrdner { get; set; } = string.Empty;

        /// <summary>Ordner, deren Dateien behalten werden (Bestand).</summary>
        public ObservableCollection<string> DublettenReferenzOrdner { get; } = new();

        /// <summary>In der Ordnerliste markierter Eintrag (zum Entfernen).</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteReferenzOrdnerEntfernenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteReferenzOrdnerEineEbeneHochCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteOrdnerseitenTauschenCommand))]
        public partial string? AusgewaehlterReferenzOrdner { get; set; }

        /// <summary>False = nur Bilddateien (Standard), True = alle Dateitypen.</summary>
        [ObservableProperty]
        public partial bool DublettenAlleDateitypen { get; set; }

        // Beide Schalter ändern, was im Ordner überhaupt gefunden wird. Ohne erneutes
        // Einlesen zeigte die Liste weiter den alten Stand – der Haken hätte scheinbar
        // keine Wirkung.
        partial void OnDublettenAlleDateitypenChanged(bool value) => LiesDublettenOrdnerNeu();

        partial void OnDublettenMitUnterordnernChanged(bool value) => LiesDublettenOrdnerNeu();

        /// <summary>
        /// True = eine Datei gilt nur dann als Dublette, wenn im Bestand eine Datei
        /// <b>gleichen Namens</b> liegt. Die Ordnernamen dürfen sich unterscheiden.
        ///
        /// Das ist die Denkweise der Dublettensuche im Dateimanager und zugleich der
        /// mit Abstand schnellste Weg: Statt einer ganzen Grössengruppe bleibt pro
        /// Kandidat meist genau ein Gegenstück übrig.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DublettenTiefenpruefungBedienbar))]
        public partial bool DublettenNurGleicherName { get; set; }

        /// <summary>
        /// True (Vorgabe) = der Inhalt wird gelesen und geprüft.
        /// False = allein der Dateiname entscheidet, keine Datei wird geöffnet.
        /// </summary>
        [ObservableProperty]
        public partial bool DublettenTiefenpruefung { get; set; } = true;

        /// <summary>
        /// Ohne Namensbezug darf die Tiefenprüfung nicht abgeschaltet werden — übrig
        /// bliebe sonst „alles gleicher Grösse ist eine Dublette", und das räumt bei
        /// Bildern und Dokumenten wahllos ab. Der Haken wird deshalb zusammen mit dem
        /// Namensvergleich zurückgesetzt; die Ansicht sperrt ihn zusätzlich.
        /// </summary>
        partial void OnDublettenNurGleicherNameChanged(bool value)
        {
            if (!value)
                DublettenTiefenpruefung = true;

            VerwirfSuchergebnis();
        }

        /// <summary>
        /// Ein Suchergebnis gehört zu dem Kriterium, mit dem es entstanden ist. Bleibt es
        /// beim Umschalten stehen, behauptet die Überschrift „Gefundene Duplikate" ein
        /// Ergebnis zu Einstellungen, die inzwischen andere sind — beim Wechsel auf den
        /// reinen Namensvergleich sogar ein geprüftes Ergebnis, das niemand so geprüft
        /// hat. Die Liste fällt deshalb auf den ungeprüften Ordnerinhalt zurück; für das
        /// neue Kriterium muss ohnehin erneut gesucht werden.
        /// </summary>
        partial void OnDublettenTiefenpruefungChanged(bool value) => VerwirfSuchergebnis();

        /// <summary>
        /// Stösst das Neu-Einlesen an, sofern überhaupt ein gültiger Ordner eingestellt
        /// ist und gerade nichts anderes läuft.
        /// </summary>
        private void LiesDublettenOrdnerNeu()
        {
            if (IsDublettenAufgabeLäuft)
                return;

            if (string.IsNullOrWhiteSpace(DublettenOrdner) || !Directory.Exists(DublettenOrdner))
                return;

            CommandExecuteDublettenOrdnerNeuLesenCommand.Execute(null);
        }

        /// <summary>Liest den Dubletten-Ordner erneut ein (nach Optionswechsel).</summary>
        [RelayCommand(IncludeCancelCommand = true)]
        private async Task CommandExecuteDublettenOrdnerNeuLesen(CancellationToken token)
        {
            // Wer hier ankommt, liest jetzt — ein vorgemerkter Lauf aus dem Tippfeld
            // wäre danach nur ein zweiter Durchgang über denselben Ordner.
            _ordnerEntpreller?.Stop();

            await ZeigeOrdnerInhaltAsync(DublettenOrdner, token);
        }

        /// <summary>
        /// Wartet nach der letzten Änderung am Pfadfeld kurz ab, bevor eingelesen wird.
        /// Der Text kommt zeichenweise an (UpdateSourceTrigger=PropertyChanged); ohne
        /// diese Pause liefe für jeden Zwischenstand ein eigener Ordner-Durchgang.
        /// </summary>
        private System.Windows.Threading.DispatcherTimer? _ordnerEntpreller;

        /// <summary>Merkt einen Einlesevorgang vor und schiebt einen bereits vorgemerkten nach hinten.</summary>
        private void PlaneDublettenOrdnerNeuLesen()
        {
            _ordnerEntpreller ??= ErzeugeOrdnerEntpreller();

            _ordnerEntpreller.Stop();
            _ordnerEntpreller.Start();
        }

        private System.Windows.Threading.DispatcherTimer ErzeugeOrdnerEntpreller()
        {
            var uhr = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(700)
            };

            uhr.Tick += (_, _) =>
            {
                // Läuft gerade etwas, bleibt die Uhr an und versucht es beim nächsten
                // Schlag erneut – sonst bliebe der neue Pfad ungelesen liegen.
                if (IsDublettenAufgabeLäuft)
                    return;

                uhr.Stop();
                PruefeDublettenOrdnerLeer();
                LiesDublettenOrdnerNeu();
            };

            return uhr;
        }

        /// <summary>
        /// Leert die Trefferliste und nimmt den Vermerk „gesucht" zurück.
        ///
        /// Nötig, sobald sich eine Eingangsgrösse ändert: Sonst stünde dort weiter ein
        /// Ergebnis zu Pfaden, die gar nicht mehr eingestellt sind — und „Markierte
        /// löschen" arbeitet auf den Pfaden <b>in den Treffern</b>, hätte also im alten
        /// Ordner gelöscht, während die Ansicht längst den neuen zeigte.
        /// </summary>
        private void VerwirfAngezeigteTreffer()
        {
            if (ByteDublettenTreffer.Count > 0)
                SetzeTreffer(Array.Empty<ByteDublettenTreffer>());

            DublettenSucheGelaufen = false;
            LeereGleichstand();
            AktualisiereLeerHinweis();
        }

        /// <summary>
        /// Ein Suchergebnis gilt nur für die Referenzordner, gegen die verglichen wurde.
        /// Ändert sich diese Seite, fällt es weg und die Liste zeigt wieder den
        /// ungeprüften Inhalt des Dubletten-Ordners. Der Ordnerinhalt selbst ist von der
        /// Referenzseite unberührt — solange nur er dasteht, bleibt alles stehen.
        /// </summary>
        private void VerwirfSuchergebnis()
        {
            if (!DublettenSucheGelaufen || IsDublettenAufgabeLäuft)
                return;

            VerwirfAngezeigteTreffer();
            LiesDublettenOrdnerNeu();
        }

        /// <summary>
        /// True, wenn im Dubletten-Ordner keine Datei mehr liegt. Steuert das Angebot,
        /// die leere Hülle gleich mit zu entfernen — die bleibt nach dem Aufräumen übrig.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteLeerenDublettenOrdnerLoeschenCommand))]
        public partial bool DublettenOrdnerIstLeer { get; set; }

        /// <summary>
        /// Anzahl der Dateien, die noch im Dubletten-Ordner liegen. −1 = unbekannt.
        /// Macht sichtbar, warum der Ordner gegebenenfalls nicht als leer gilt.
        /// </summary>
        [ObservableProperty]
        public partial int DublettenOrdnerRestDateien { get; set; } = -1;

        /// <summary>
        /// Gesamtgrösse dessen, was noch im Dubletten-Ordner liegt. 0 = unbekannt oder leer.
        /// </summary>
        [ObservableProperty]
        public partial long DublettenOrdnerRestBytes { get; set; }

        /// <summary>
        /// Text neben dem Entfernen-Knopf, wenn der Ordner noch Dateien enthält.
        ///
        /// Mit Gesamtgrösse: Die Statuszeile der Suche nennt darunter, wie viel davon
        /// abgeglichen wird — ohne die Gesamtmenge daneben lässt sich das nicht einordnen.
        /// </summary>
        public string DublettenOrdnerRestText => DublettenOrdnerRestDateien switch
        {
            < 0 => string.Empty,
            0 => string.Empty,
            1 => "noch 1 Datei im Dubletten-Ordner" + RestGroesseZusatz,
            _ => $"noch {DublettenOrdnerRestDateien} Dateien im Dubletten-Ordner" + RestGroesseZusatz
        };

        private string RestGroesseZusatz
            => DublettenOrdnerRestBytes > 0 ? $" · {GroesseText(DublettenOrdnerRestBytes)}" : string.Empty;

        partial void OnDublettenOrdnerRestDateienChanged(int value)
            => OnPropertyChanged(nameof(DublettenOrdnerRestText));

        partial void OnDublettenOrdnerRestBytesChanged(long value)
            => OnPropertyChanged(nameof(DublettenOrdnerRestText));

        /// <summary>
        /// Nennt die ersten verbliebenen Dateien beim Namen. Ohne das rätselt man, warum
        /// ein scheinbar leerer Ordner nicht als leer gilt — meist sind es versteckte
        /// Dateien wie desktop.ini oder Thumbs.db.
        /// </summary>
        [ObservableProperty]
        public partial string DublettenOrdnerRestTooltip { get; set; } = string.Empty;

        /// <summary>Bestimmt neu, ob der Dubletten-Ordner leer ist.</summary>
        private void PruefeDublettenOrdnerLeer()
        {
            // Erst eine kleine Stichprobe: Für Anzeige und Tooltip reichen ein paar
            // Namen, und bei riesigen Ordnern spart es das vollständige Durchzählen.
            var probe = ByteDublettenService.ListeVerbleibendeDateien(DublettenOrdner, 12);

            if (probe is null)
            {
                DublettenOrdnerRestDateien = -1;
                DublettenOrdnerRestBytes = 0;
                DublettenOrdnerRestTooltip = string.Empty;
                DublettenOrdnerIstLeer = false;
                AktualisiereLeerHinweis();
                return;
            }

            if (probe.Count == 0)
            {
                DublettenOrdnerRestDateien = 0;
                DublettenOrdnerRestBytes = 0;
                DublettenOrdnerRestTooltip = string.Empty;
                DublettenOrdnerIstLeer = true;
                AktualisiereLeerHinweis();
                return;
            }

            DublettenOrdnerIstLeer = false;

            // Zählen und Messen in einem Durchgang: Die Dateigrösse steht im
            // Verzeichniseintrag und kostet keinen zusätzlichen Zugriff.
            var stand = ByteDublettenService.MisstVerbleibendeDateien(DublettenOrdner);
            DublettenOrdnerRestDateien = stand.Anzahl;
            DublettenOrdnerRestBytes = stand.Bytes;

            var namen = probe.Select(Path.GetFileName).Take(10);
            DublettenOrdnerRestTooltip =
                "Diese Dateien liegen noch im Ordner (Auszug):\n" + string.Join("\n", namen)
                + "\n\nVersteckte Dateien wie desktop.ini oder Thumbs.db zählen mit – "
                + "im Explorer sind sie oft ausgeblendet.";

            AktualisiereLeerHinweis();
        }

        /// <summary>
        /// Ein neuer Pfad macht beides hinfällig: die aufgelistete Ordnerübersicht und
        /// ein etwaiges Suchergebnis. Beides gehörte zum vorherigen Ordner.
        ///
        /// Vorher blieb die Liste einfach stehen. Wer nach einer Suche die Pfade
        /// umstellte, sah weiter die alten Treffer — die nächste Aktualisierung kam erst,
        /// wenn zufällig ein Optionshaken angefasst wurde. Und „Markierte löschen" hätte
        /// in diesem Zustand nach dem alten Stand gelöscht.
        ///
        /// Der Leerstand wird jetzt zusammen mit dem Einlesen geprüft: Beides greift auf
        /// die Platte zu, und beim Tippen kam das bisher für jedes einzelne Zeichen.
        /// </summary>
        partial void OnDublettenOrdnerChanged(string value)
        {
            VerwirfAngezeigteTreffer();
            PlaneDublettenOrdnerNeuLesen();
        }

        private bool CanExecuteLeerenDublettenOrdnerLoeschen()
            => !IsDublettenAufgabeLäuft && DublettenOrdnerIstLeer;

        /// <summary>
        /// Verschiebt den leeren Dubletten-Ordner in den Papierkorb. Nur möglich, wenn
        /// wirklich keine Datei mehr darin liegt; der Service prüft das nochmals selbst.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteLeerenDublettenOrdnerLoeschen))]
        private void CommandExecuteLeerenDublettenOrdnerLoeschen()
        {
            string ordner = DublettenOrdner;

            if (!ByteDublettenService.IstOrdnerLeer(ordner))
            {
                DublettenStatus = "Der Ordner ist nicht mehr leer – bitte neu einlesen.";
                PruefeDublettenOrdnerLeer();
                return;
            }

            string warnung = ByteDublettenService.PapierkorbWarnung(ordner);

            var antwort = MessageBox.Show(
                $"Diesen Ordner in den Papierkorb verschieben?\n\n{ordner}\n\n"
                + "In dem Ordner liegt keine einzige Datei mehr.\n"
                + "Falls darin noch leere Unterordner stecken, wandern diese mit in den Papierkorb.\n\n"
                + "Zurückholen geht so: Papierkorb auf dem Desktop öffnen,\n"
                + "den Ordner markieren, Rechtsklick, „Wiederherstellen\"."
                + warnung,
                "Leeren Ordner entfernen",
                MessageBoxButton.YesNo,

                // Bei Papierkorb-Zweifel das Warnzeichen statt des Fragezeichens.
                warnung.Length == 0 ? MessageBoxImage.Question : MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (antwort != MessageBoxResult.Yes)
                return;

            if (ByteDublettenService.OrdnerInDenPapierkorb(ordner))
            {
                DublettenStatus = $"Ordner in den Papierkorb verschoben: {ordner}";
                DublettenOrdner = string.Empty;
                SetzeTreffer(Array.Empty<ByteDublettenTreffer>());
            }
            else
            {
                DublettenStatus = "Ordner konnte nicht entfernt werden (gesperrt oder kein Zugriff).";
            }

            PruefeDublettenOrdnerLeer();
        }

        /// <summary>Gefundene Duplikate.</summary>
        public ObservableCollection<ByteDublettenTreffer> ByteDublettenTreffer { get; } = new();

        /// <summary>True, sobald einmal gesucht wurde. Unterscheidet „noch nicht gesucht" von „nichts gefunden".</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DublettenTrefferTitel))]
        [NotifyPropertyChangedFor(nameof(DublettenTrefferUntertitel))]
        public partial bool DublettenSucheGelaufen { get; set; }

        /// <summary>
        /// Überschrift der Trefferkarte. Beim Öffnen der Ansicht wird der Ordner des
        /// angezeigten Bildes übernommen und sein Inhalt aufgelistet — verglichen ist
        /// da noch nichts. „Gefundene Duplikate" behauptete über dieser Liste ein
        /// Ergebnis, das es noch gar nicht gab; der Titel benennt jetzt, was wirklich
        /// dasteht.
        /// </summary>
        public string DublettenTrefferTitel =>
            DublettenSucheGelaufen ? "Gefundene Duplikate" : "Inhalt des Dubletten-Ordners";

        /// <summary>Zusatz zur Überschrift, der den Stand der Liste in einem Halbsatz nennt.</summary>
        public string DublettenTrefferUntertitel =>
            DublettenSucheGelaufen ? string.Empty : "ungeprüft – erst die Suche bestätigt Duplikate";

        /// <summary>
        /// Hinweis in der leeren Trefferliste. Nennt den jeweils nächsten sinnvollen
        /// Schritt statt pauschal „noch keine Duplikate" — das behauptete auch nach einer
        /// erfolglosen Suche, es sei noch nichts geschehen.
        /// </summary>
        [ObservableProperty]
        public partial string DublettenLeerHinweis { get; set; } = "Dubletten-Ordner wählen oder hineinziehen";

        /// <summary>Bestimmt den Hinweistext aus dem aktuellen Zustand.</summary>
        private void AktualisiereLeerHinweis()
        {
            if (string.IsNullOrWhiteSpace(DublettenOrdner) || !Directory.Exists(DublettenOrdner))
            {
                DublettenLeerHinweis = "Dubletten-Ordner wählen oder hineinziehen";
                return;
            }

            if (DublettenOrdnerRestDateien == 0)
            {
                DublettenLeerHinweis = "Im Dubletten-Ordner liegt keine Datei mehr";
                return;
            }

            if (DublettenReferenzOrdner.Count == 0)
            {
                DublettenLeerHinweis = "Referenzordner hinzufügen – dagegen wird verglichen";
                return;
            }

            if (DublettenSucheGelaufen)
            {
                DublettenLeerHinweis = DublettenTiefenpruefung
                    ? "Keine byte-gleichen Dateien im Referenzbestand gefunden"
                    : "Keine gleichnamigen Dateien im Referenzbestand gefunden";
                return;
            }

            DublettenLeerHinweis = "Bereit – auf „Byte-Duplikate suchen“ klicken";
        }

        [ObservableProperty]
        public partial bool DublettenMitUnterordnern { get; set; } = true;

        [ObservableProperty]
        public partial string DublettenStatus { get; set; } = "Dubletten-Ordner und Referenzordner wählen, dann suchen.";

        /// <summary>
        /// Text neben dem Ring, solange der Dubletten-Ordner eingelesen wird. Nennt die
        /// bisher gefundene Menge — die Gesamtgrösse des Ordners baut sich hier auf.
        /// </summary>
        [ObservableProperty]
        public partial string DublettenEinlesenHinweis { get; set; } = "liest …";

        /// <summary>
        /// Auskunft zur Statuszeile: welcher Leseplan galt und warum, welcher
        /// Vergleichsweg getragen hat, Menge und Rate.
        ///
        /// Bewusst null statt leer, solange es nichts zu sagen gibt — WPF zeigt sonst ein
        /// leeres Kästchen, sobald man die Zeile streift.
        /// </summary>
        [ObservableProperty]
        public partial string? DublettenStatusAuskunft { get; set; }

        [ObservableProperty]
        public partial int DublettenFortschritt { get; set; }

        [ObservableProperty]
        public partial int DublettenFortschrittMax { get; set; } = 100;

        /// <summary>
        /// Balken läuft ohne festen Wert. Gilt, solange überhaupt nicht feststeht, wie
        /// viel Arbeit ansteht (Dateien werden erfasst, Grössen verglichen) — ein Balken,
        /// der minutenlang auf 0 steht, sieht aus wie ein Hänger.
        /// </summary>
        [ObservableProperty]
        public partial bool DublettenFortschrittUnbestimmt { get; set; }

        [ObservableProperty]
        public partial string DublettenRestzeit { get; set; } = string.Empty;

        /// <summary>Sperrt die Commands, solange Suche oder Löschlauf aktiv ist.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DublettenOptionenBedienbar))]
        [NotifyPropertyChangedFor(nameof(DublettenTiefenpruefungBedienbar))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteByteDublettenSuchenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteMarkierteLoeschenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteReferenzOrdnerHinzufuegenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteReferenzOrdnerEntfernenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteReferenzOrdnerEineEbeneHochCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteDublettenOrdnerWaehlenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteOrdnerseitenTauschenCommand))]
        public partial bool IsDublettenAufgabeLäuft { get; set; }

        /// <summary>
        /// Die vier Optionshaken sind nur bedienbar, solange nichts läuft — wie die
        /// Knöpfe daneben, die über ihr CanExecute ohnehin gesperrt sind.
        ///
        /// Ohne diese Sperre liess sich mitten im Lauf umschalten. Der laufenden Suche
        /// machte das nichts aus (sie bekommt ihre Einstellungen beim Start übergeben),
        /// aber die Anzeige log anschliessend: „Umfang" wechselte den Haken, ohne die
        /// Liste neu einzulesen — <see cref="LiesDublettenOrdnerNeu"/> steigt bei
        /// laufender Aufgabe aus —, und beim „Kriterium" wurde der Ergebnissatz am Ende
        /// aus dem <b>aktuellen</b> Stand gebildet. Wer während eines reinen
        /// Namensvergleichs den Namenshaken löste, schaltete damit die Tiefenprüfung ein
        /// und bekam „N Duplikate gefunden" gemeldet — eine Aussage über den Inhalt, den
        /// niemand gelesen hatte, und das unmittelbar vor dem Löschen.
        /// </summary>
        public bool DublettenOptionenBedienbar => !IsDublettenAufgabeLäuft;

        /// <summary>
        /// Die Tiefenprüfung zusätzlich an den Namensvergleich gebunden: Ohne Namensbezug
        /// bliebe „alles gleicher Grösse ist eine Dublette" übrig.
        /// </summary>
        public bool DublettenTiefenpruefungBedienbar
            => DublettenNurGleicherName && !IsDublettenAufgabeLäuft;

        /// <summary>
        /// Anzahl der zum Löschen vorgemerkten Treffer. Nur bestätigte zählen —
        /// Einträge aus der reinen Ordner-Auflistung wurden nie verglichen.
        /// </summary>
        public int DublettenMarkierteAnzahl =>
            ByteDublettenTreffer.Count(t => t.IstMarkiert && t.IstBestaetigt && !t.IstGeloescht);

        /// <summary>Speicherplatz, der beim Löschen frei wird.</summary>
        public string DublettenMarkierteGroesseText
        {
            get
            {
                long summe = ByteDublettenTreffer
                    .Where(t => t.IstMarkiert && t.IstBestaetigt && !t.IstGeloescht)
                    .Sum(t => t.GroesseBytes);

                return summe >= 1024L * 1024 * 1024
                    ? $"{summe / 1024.0 / 1024.0 / 1024.0:0.00} GB"
                    : $"{summe / 1024.0 / 1024.0:0.0} MB";
            }
        }

        private void MeldeMarkierungGeaendert()
        {
            OnPropertyChanged(nameof(DublettenMarkierteAnzahl));
            OnPropertyChanged(nameof(DublettenMarkierteGroesseText));
            CommandExecuteMarkierteLoeschenCommand.NotifyCanExecuteChanged();
        }

        private void TrefferGeaendert(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(Bildersuche.ByteDublettenTreffer.IstMarkiert)
                or nameof(Bildersuche.ByteDublettenTreffer.IstGeloescht))
            {
                MeldeMarkierungGeaendert();
            }
        }

        private void SetzeTreffer(System.Collections.Generic.IEnumerable<ByteDublettenTreffer> neue)
        {
            foreach (var alt in ByteDublettenTreffer)
                alt.PropertyChanged -= TrefferGeaendert;

            ByteDublettenTreffer.Clear();

            foreach (var t in neue)
            {
                t.PropertyChanged += TrefferGeaendert;
                ByteDublettenTreffer.Add(t);
            }

            MeldeMarkierungGeaendert();
        }

        #endregion

        #region Ordner wählen

        private bool CanExecuteOrdnerBearbeiten() => !IsDublettenAufgabeLäuft;

        [RelayCommand(CanExecute = nameof(CanExecuteOrdnerBearbeiten))]
        private void CommandExecuteDublettenOrdnerWaehlen()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Dubletten-Ordner wählen — hieraus wird gelöscht",
                InitialDirectory = OrdnerOderLeer(DublettenOrdner) ?? AktuellerBildOrdner() ?? string.Empty
            };

            if (dlg.ShowDialog() == true)
                DublettenOrdner = dlg.FolderName;
        }

        [RelayCommand(CanExecute = nameof(CanExecuteOrdnerBearbeiten))]
        private void CommandExecuteReferenzOrdnerHinzufuegen()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Referenzordner wählen — dieser Bestand bleibt unangetastet",
                Multiselect = true,
                InitialDirectory = OrdnerOderLeer(DublettenOrdner) ?? string.Empty
            };

            if (dlg.ShowDialog() != true)
                return;

            foreach (var ordner in dlg.FolderNames)
            {
                if (!DublettenReferenzOrdner.Contains(ordner, StringComparer.OrdinalIgnoreCase))
                    DublettenReferenzOrdner.Add(ordner);
            }

            AktualisiereLeerHinweis();

            CommandExecuteByteDublettenSuchenCommand.NotifyCanExecuteChanged();
            VerwirfSuchergebnis();
        }

        private bool CanExecuteReferenzOrdnerEntfernen()
            => !IsDublettenAufgabeLäuft && !string.IsNullOrEmpty(AusgewaehlterReferenzOrdner);

        [RelayCommand(CanExecute = nameof(CanExecuteReferenzOrdnerEntfernen))]
        private void CommandExecuteReferenzOrdnerEntfernen()
        {
            if (AusgewaehlterReferenzOrdner is null)
                return;

            DublettenReferenzOrdner.Remove(AusgewaehlterReferenzOrdner);
            AusgewaehlterReferenzOrdner = null;
            AktualisiereLeerHinweis();
            CommandExecuteByteDublettenSuchenCommand.NotifyCanExecuteChanged();
            VerwirfSuchergebnis();
        }

        private static string? OrdnerOderLeer(string ordner)
            => !string.IsNullOrWhiteSpace(ordner) && Directory.Exists(ordner) ? ordner : null;

        /// <summary>Übergeordneter Ordner, null bei Laufwerkswurzel oder ungültigem Pfad.</summary>
        private static string? ElternOrdner(string? pfad)
        {
            if (string.IsNullOrWhiteSpace(pfad))
                return null;

            try { return new DirectoryInfo(pfad).Parent?.FullName; }
            catch { return null; }
        }

        private bool CanExecuteReferenzOrdnerEineEbeneHoch()
            => !IsDublettenAufgabeLäuft
               && !string.IsNullOrEmpty(AusgewaehlterReferenzOrdner)
               && ElternOrdner(AusgewaehlterReferenzOrdner) is not null;

        /// <summary>
        /// Ersetzt den markierten Referenzordner durch seinen übergeordneten — das „..“
        /// aus dem Dateimanager. Praktisch, wenn der Bestand eine Ebene höher liegt als
        /// der Ordner, den man gerade hineingezogen hat.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteReferenzOrdnerEineEbeneHoch))]
        private void CommandExecuteReferenzOrdnerEineEbeneHoch()
        {
            string? aktuell = AusgewaehlterReferenzOrdner;
            string? eltern = ElternOrdner(aktuell);

            if (aktuell is null || eltern is null)
                return;

            int index = DublettenReferenzOrdner.IndexOf(aktuell);
            if (index < 0)
                return;

            // Liegt der übergeordnete Ordner schon in der Liste, würde ein Ersetzen ihn
            // doppeln – dann reicht es, den engeren Eintrag zu entfernen.
            if (DublettenReferenzOrdner.Contains(eltern, StringComparer.OrdinalIgnoreCase))
            {
                DublettenReferenzOrdner.RemoveAt(index);
                DublettenStatus = $"Bereits enthalten – Eintrag zusammengefasst zu: {eltern}";
            }
            else
            {
                DublettenReferenzOrdner[index] = eltern;
                DublettenStatus = $"Eine Ebene höher: {eltern}";
            }

            AusgewaehlterReferenzOrdner = eltern;
            CommandExecuteByteDublettenSuchenCommand.NotifyCanExecuteChanged();
            VerwirfSuchergebnis();
        }

        private bool CanExecuteOrdnerseitenTauschen()
            => !IsDublettenAufgabeLäuft
               && OrdnerOderLeer(DublettenOrdner) is not null
               && !string.IsNullOrEmpty(AusgewaehlterReferenzOrdner)
               && !string.Equals(DublettenOrdner, AusgewaehlterReferenzOrdner,
                                 StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Vertauscht die beiden Seiten: Der markierte Referenzordner wird zum
        /// Dubletten-Ordner (dort wird künftig gelöscht), der bisherige Dubletten-Ordner
        /// rückt an dessen Stelle in die Referenzliste.
        ///
        /// Gedacht für den Fall, dass man beim Ziehen die Seiten verwechselt hat — ohne
        /// den Knopf müsste man beide Pfade neu heraussuchen.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteOrdnerseitenTauschen))]
        private void CommandExecuteOrdnerseitenTauschen()
        {
            string bisherigerLoeschOrdner = DublettenOrdner;
            string? neuerLoeschOrdner = AusgewaehlterReferenzOrdner;

            if (neuerLoeschOrdner is null || !Directory.Exists(bisherigerLoeschOrdner))
                return;

            int index = DublettenReferenzOrdner.IndexOf(neuerLoeschOrdner);
            if (index < 0)
                return;

            // Stand der bisherige Lösch-Ordner schon in der Referenzliste, würde das
            // Ersetzen ihn doppeln – dann fällt der markierte Eintrag nur weg.
            if (DublettenReferenzOrdner.Contains(bisherigerLoeschOrdner, StringComparer.OrdinalIgnoreCase))
                DublettenReferenzOrdner.RemoveAt(index);
            else
                DublettenReferenzOrdner[index] = bisherigerLoeschOrdner;

            DublettenOrdner = neuerLoeschOrdner;

            // Auf den Listeneintrag markieren, nicht auf die eigene Schreibweise: Bei
            // abweichender Gross-/Kleinschreibung fände die ListBox sonst nichts.
            AusgewaehlterReferenzOrdner = DublettenReferenzOrdner
                .FirstOrDefault(o => string.Equals(o, bisherigerLoeschOrdner,
                                                   StringComparison.OrdinalIgnoreCase));

            AktualisiereLeerHinweis();
            DublettenStatus = $"Seiten getauscht – gelöscht wird jetzt in: {neuerLoeschOrdner}";
            CommandExecuteByteDublettenSuchenCommand.NotifyCanExecuteChanged();

            // Wie beim Drop über denselben Command: genau ein Einlesevorgang, den der
            // Abbrechen-Knopf sicher trifft.
            CommandExecuteDublettenOrdnerNeuLesenCommand.Execute(null);
        }

        /// <summary>
        /// Übernimmt einen per Drag &amp; Drop abgelegten Ordner als Dubletten-Ordner.
        /// Aufgerufen von <see cref="OrdnerDropHelper"/>; gezogen wird eine Datei daraus
        /// oder der Ordner selbst.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteOrdnerBearbeiten))]
        private void CommandExecuteDublettenOrdnerAusDrop(string? ordner)
        {
            if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
                return;

            DublettenOrdner = ordner;

            // Bewusst über denselben Command wie der Optionswechsel: So gibt es genau
            // einen Einlesevorgang, den der Abbrechen-Knopf sicher trifft.
            CommandExecuteDublettenOrdnerNeuLesenCommand.Execute(null);
        }

        /// <summary>
        /// Listet den Inhalt des Dubletten-Ordners in der Trefferliste auf — als Übersicht,
        /// was auf der Löschseite liegt. Die Einträge sind noch <b>nicht</b> geprüft und
        /// deshalb nicht markierbar; erst die Suche bestätigt echte Duplikate.
        ///
        /// Bewusst ohne Vorschaubilder und in Blöcken: Auf einer langsamen Platte würde
        /// das Einlesen sonst die Oberfläche blockieren.
        /// </summary>
        private async Task ZeigeOrdnerInhaltAsync(string ordner, CancellationToken token)
        {
            IsDublettenAufgabeLäuft = true;
            SetzeTreffer(Array.Empty<ByteDublettenTreffer>());
            DublettenFortschritt = 0;

            // Wie viele Dateien es sind, weiss erst die Auflistung selbst.
            DublettenFortschrittUnbestimmt = true;
            DublettenStatus = "Ordner wird gelesen …";
            DublettenEinlesenHinweis = "liest …";
            DublettenStatusAuskunft = null;

            // Neuer Ordnerinhalt: Ein früheres Suchergebnis gilt nicht mehr.
            DublettenSucheGelaufen = false;

            try
            {
                // Verzeichnis-Auflistung selbst kann auf HDD dauern → in den Hintergrund.
                var dateien = await Task.Run(
                    () => ByteDublettenService.ListeDateien(
                        ordner, DublettenMitUnterordnern, DublettenAlleDateitypen, token),
                    token);

                DublettenFortschrittMax = Math.Max(1, dateien.Count);
                DublettenFortschrittUnbestimmt = false;

                var liste = new System.Collections.Generic.List<ByteDublettenTreffer>(dateien.Count);
                var uhr = Stopwatch.StartNew();
                long summeBisher = 0;

                for (int i = 0; i < dateien.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    long groesse;
                    try { groesse = new FileInfo(dateien[i]).Length; }
                    catch { groesse = 0; }

                    summeBisher += groesse;

                    liste.Add(new ByteDublettenTreffer
                    {
                        ReferenzDatei = string.Empty,   // noch nicht verglichen
                        DublettenDatei = dateien[i],
                        GroesseBytes = groesse,
                        IstMarkiert = false             // nichts vormerken, was ungeprüft ist
                    });

                    // Nur gelegentlich melden und Luft lassen, sonst erstickt die UI.
                    if ((i + 1) % 200 == 0 || i == dateien.Count - 1)
                    {
                        DublettenFortschritt = i + 1;
                        DublettenStatus = $"Ordner wird gelesen … {i + 1} / {dateien.Count}"
                            + RestzeitZusatz(uhr.Elapsed, i + 1, dateien.Count);
                        DublettenEinlesenHinweis = $"liest … {i + 1} / {dateien.Count} · {GroesseText(summeBisher)}";
                        await Task.Delay(1, token);
                    }
                }

                SetzeTreffer(liste);

                long summe = liste.Sum(t => t.GroesseBytes);
                DublettenStatus = liste.Count == 0
                    ? $"Im Dubletten-Ordner liegen keine {(DublettenAlleDateitypen ? "Dateien" : "Bilder")}."
                    : $"{liste.Count} Einträge im Dubletten-Ordner ({GroesseText(summe)}) — noch nicht geprüft. "
                      + "Referenzordner wählen und suchen.";
            }
            catch (OperationCanceledException)
            {
                DublettenStatus = "Einlesen abgebrochen.";
            }
            catch (Exception ex)
            {
                DublettenStatus = "Fehler beim Einlesen: " + ex.Message;
            }
            finally
            {
                DublettenFortschritt = 0;
                DublettenFortschrittUnbestimmt = false;
                IsDublettenAufgabeLäuft = false;
                PruefeDublettenOrdnerLeer();
            }
        }

        private static string RestzeitZusatz(TimeSpan verstrichen, int erledigt, int gesamt)
        {
            string rest = SchaetzeRestzeit(verstrichen, erledigt, gesamt);
            return rest.Length > 0 ? " – " + rest : string.Empty;
        }

        private static string GroesseText(long bytes)
            => bytes >= 1024L * 1024 * 1024
                ? $"{bytes / 1024.0 / 1024.0 / 1024.0:0.00} GB"
                : $"{bytes / 1024.0 / 1024.0:0.0} MB";

        /// <summary>Fügt einen per Drag &amp; Drop abgelegten Ordner der Referenzliste hinzu.</summary>
        [RelayCommand(CanExecute = nameof(CanExecuteOrdnerBearbeiten))]
        private void CommandExecuteReferenzOrdnerAusDrop(string? ordner)
        {
            if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner))
                return;

            if (DublettenReferenzOrdner.Contains(ordner, StringComparer.OrdinalIgnoreCase))
            {
                DublettenStatus = "Dieser Referenzordner ist bereits in der Liste.";
                return;
            }

            DublettenReferenzOrdner.Add(ordner);
            DublettenStatus = $"Referenzordner hinzugefügt: {ordner}";
            AktualisiereLeerHinweis();
            CommandExecuteByteDublettenSuchenCommand.NotifyCanExecuteChanged();
        }

        #endregion

        #region Suche

        private bool CanExecuteByteDublettenSuchen()
            => !IsDublettenAufgabeLäuft
               && !string.IsNullOrWhiteSpace(DublettenOrdner)
               && Directory.Exists(DublettenOrdner)
               && DublettenReferenzOrdner.Count > 0;

        [RelayCommand(CanExecute = nameof(CanExecuteByteDublettenSuchen), IncludeCancelCommand = true)]
        private async Task CommandExecuteByteDublettenSuchen(CancellationToken token)
        {
            IsDublettenAufgabeLäuft = true;
            SetzeTreffer(Array.Empty<ByteDublettenTreffer>());
            DublettenFortschritt = 0;
            DublettenFortschrittMax = 1000;
            DublettenFortschrittUnbestimmt = true;   // Umfang noch unbekannt
            DublettenRestzeit = string.Empty;

            // Das Urteil des vorigen Laufs gilt nicht mehr, sobald neu gesucht wird.
            LeereGleichstand();

            // Die Auskunft des vorigen Laufs gilt nicht mehr — sie stünde sonst am
            // laufenden Balken und beschriebe einen Leseplan, der gar nicht mehr gilt.
            DublettenStatusAuskunft = null;

            var uhr = Stopwatch.StartNew();

            try
            {
                // Der Dienst rechnet in Bytes; angezeigt wird daraus ein Promille-Stand.
                var fortschritt = new Progress<(long Erledigt, long Gesamt, string Text)>(p =>
                {
                    DublettenStatus = p.Text;

                    if (p.Gesamt <= 0)
                    {
                        // Dateien werden noch erfasst – Umfang steht nicht fest.
                        DublettenFortschrittUnbestimmt = true;
                        return;
                    }

                    DublettenFortschrittUnbestimmt = false;

                    // Der Balken geht nur vorwärts. Der Umfang steht zwar seit dem
                    // Vorfiltern fest, aber gemeldet wird aus mehreren Lesern heraus —
                    // ein einzelner Rücksetzer sähe nach einem Fehler aus.
                    int stand = (int)(p.Erledigt * 1000 / p.Gesamt);
                    if (stand > DublettenFortschritt)
                        DublettenFortschritt = stand;

                    DublettenRestzeit = SchaetzeRestzeit(uhr.Elapsed, p.Erledigt, p.Gesamt);
                });

                var nichtLesbar = new System.Collections.Generic.List<string>();

                // Die Schlussmeldung des Dienstes wird hier gleich überschrieben. Was er
                // über sein eigenes Lesen zu sagen hat, kommt deshalb getrennt zurück.
                var protokoll = new ByteDublettenService.Leseprotokoll();

                // Der Ergebnissatz muss den Lauf beschreiben, der stattgefunden hat, nicht
                // den Stand der Haken bei seinem Ende. Die Ansicht sperrt sie zwar während
                // des Laufs; verlassen sollte sich der Satz darauf nicht.
                bool tiefenpruefungImLauf = DublettenTiefenpruefung;
                bool mitUnterordnernImLauf = DublettenMitUnterordnern;
                bool alleDateitypenImLauf = DublettenAlleDateitypen;

                var treffer = await ByteDublettenService.FindeByteDublettenAsync(
                    DublettenOrdner,
                    DublettenReferenzOrdner.ToList(),
                    DublettenMitUnterordnern,
                    DublettenAlleDateitypen,
                    DublettenNurGleicherName,
                    DublettenTiefenpruefung,
                    fortschritt,
                    token,
                    nichtLesbar,
                    protokoll);

                token.ThrowIfCancellationRequested();

                SetzeTreffer(treffer);

                // Ab jetzt heisst „leere Liste" wirklich „nichts gefunden".
                DublettenSucheGelaufen = true;
                AktualisiereLeerHinweis();

                // Gesperrte Dateien ausdrücklich nennen: Sie wurden nicht geprüft und
                // könnten trotzdem Duplikate sein.
                string zusatz = nichtLesbar.Count == 0
                    ? string.Empty
                    : $" — {nichtLesbar.Count} Datei(en) waren gesperrt und wurden nicht geprüft, Suche später wiederholen";

                // Ohne Tiefenprüfung wäre „Byte-Duplikate" eine Behauptung über den
                // Inhalt, die gar nicht geprüft wurde. Der Text nennt deshalb genau das
                // Kriterium, nach dem gesucht wurde.
                int ungeprueft = treffer.Count(t => t.HatAbweichendeGroesse);

                string groessenHinweis = ungeprueft == 0
                    ? string.Empty
                    : $" Bei {ungeprueft} davon ist das Gegenstück im Bestand unterschiedlich gross —"
                      + " gleicher Name heisst dort nicht gleicher Inhalt.";

                string gefunden = tiefenpruefungImLauf
                    ? $"{treffer.Count} Duplikate gefunden"
                    : $"{treffer.Count} gleichnamige Dateien gefunden (Inhalt nicht geprüft)";

                DublettenStatus = (treffer.Count == 0
                    ? (tiefenpruefungImLauf
                        ? "Keine Duplikate gefunden."
                        : "Keine gleichnamigen Dateien gefunden.")
                    : $"{gefunden} — {DublettenMarkierteGroesseText} können frei werden.")
                    + groessenHinweis
                    + zusatz
                    + protokoll.Text;

                DublettenStatusAuskunft =
                    protokoll.Auskunft.Length == 0 ? null : protokoll.Auskunft;

                BestimmeOrdnerGleichstand(
                    treffer, protokoll, tiefenpruefungImLauf,
                    mitUnterordnernImLauf, alleDateitypenImLauf, nichtLesbar.Count);
            }
            catch (OperationCanceledException)
            {
                DublettenStatus = "Suche abgebrochen.";
            }
            catch (Exception ex)
            {
                DublettenStatus = $"Fehler bei der Suche: {ex.Message}";
            }
            finally
            {
                DublettenRestzeit = string.Empty;
                DublettenFortschrittUnbestimmt = false;
                IsDublettenAufgabeLäuft = false;

                // Stand des Ordners auffrischen – er kann sich seit dem Einlesen
                // geändert haben, etwa durch Aufräumen ausserhalb der Anwendung.
                PruefeDublettenOrdnerLeer();
            }
        }

        #region Gleichstand zweier Ordner

        /// <summary>
        /// Aussage über den Vergleich der beiden Startordner nach einem Suchlauf.
        /// Leer = keine Aussage möglich; die Zeile bleibt dann unsichtbar.
        /// </summary>
        [ObservableProperty]
        public partial string DublettenGleichstandText { get; set; } = string.Empty;

        /// <summary>Zweite Zeile mit den Zahlen und Einschränkungen dahinter.</summary>
        [ObservableProperty]
        public partial string DublettenGleichstandDetail { get; set; } = string.Empty;

        /// <summary>
        /// True = beide Startordner enthalten dieselben Dateien, auf keiner Seite bleibt
        /// etwas übrig. Färbt die Zeile grün.
        /// </summary>
        [ObservableProperty]
        public partial bool DublettenOrdnerDeckungsgleich { get; set; }

        private void LeereGleichstand()
        {
            DublettenOrdnerDeckungsgleich = false;
            DublettenGleichstandText = string.Empty;
            DublettenGleichstandDetail = string.Empty;
        }

        /// <summary>
        /// Beurteilt nach einem Suchlauf, ob Dubletten-Ordner und Referenzordner denselben
        /// Bestand enthalten — der Ordnervergleich, wie ihn ein Dateimanager-Abgleich
        /// liefert. Die Ordner selbst dürfen dabei verschieden heissen; verglichen wird,
        /// was darin liegt.
        ///
        /// Die Trefferzahl allein reicht dafür nicht: Sie sagt nur, wie viel von der
        /// Löschseite drüben liegt. Ob drüben <b>mehr</b> liegt, ergibt sich erst aus der
        /// Zahl der Referenzdateien und daraus, wie viele davon überhaupt als Gegenstück
        /// gedient haben.
        ///
        /// Im Zweifel wird nichts behauptet: Bleibt ein Rest unklar — gesperrte Dateien,
        /// mehrere Referenzordner, ungeprüfter Inhalt —, nennt die Zeile den Stand, aber
        /// keine Deckungsgleichheit.
        /// </summary>
        private void BestimmeOrdnerGleichstand(
            System.Collections.Generic.IReadOnlyList<ByteDublettenTreffer> treffer,
            ByteDublettenService.Leseprotokoll protokoll,
            bool tiefenpruefungImLauf,
            bool mitUnterordnernImLauf,
            bool alleDateitypenImLauf,
            int nichtLesbar)
        {
            LeereGleichstand();

            int links = protokoll.KandidatenDateien;
            int rechts = protokoll.ReferenzDateien;

            // Ohne Bestand auf einer der beiden Seiten gibt es nichts zu vergleichen.
            if (links == 0 || rechts == 0)
                return;

            // Ohne Tiefenprüfung wurde keine einzige Datei geöffnet. „Gleiche Ordner"
            // wäre dann eine Aussage über Inhalte, die niemand gelesen hat.
            if (!tiefenpruefungImLauf)
            {
                DublettenGleichstandText = $"Namensabgleich: {treffer.Count} von {links} Dateien haben drüben einen Partner";
                DublettenGleichstandDetail =
                    "Ob beide Ordner denselben Inhalt haben, sagt das nicht – dafür die Tiefenprüfung einschalten.";
                return;
            }

            int ohneGegenstueck = links - treffer.Count;

            // Wie viele Dateien des Bestands tatsächlich als Gegenstück gedient haben.
            // Zwei gleiche Dateien auf der Löschseite können auf dieselbe Referenzdatei
            // zeigen — dann bleibt drüben eine andere übrig, und genau das soll auffallen.
            int getroffen = treffer
                .Select(t => t.ReferenzDatei)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            int nurRechts = Math.Max(0, rechts - getroffen);

            // „Beide Ordner" setzt genau eine Gegenseite voraus. Gegen mehrere Ordner
            // verglichen bleibt nur die Teilmengen-Aussage.
            if (DublettenReferenzOrdner.Count != 1)
            {
                DublettenGleichstandText = ohneGegenstueck == 0
                    ? $"Alle {links} Dateien des Dubletten-Ordners liegen im Referenzbestand"
                    : $"{ohneGegenstueck} von {links} Dateien haben im Referenzbestand kein Gegenstück";

                DublettenGleichstandDetail =
                    "Verglichen wurde gegen mehrere Referenzordner – für den Vergleich zweier Ordner darf nur einer eingestellt sein.";
                return;
            }

            if (ohneGegenstueck == 0 && nurRechts == 0 && nichtLesbar == 0)
            {
                DublettenOrdnerDeckungsgleich = true;
                DublettenGleichstandText = $"Deckungsgleich – beide Ordner enthalten dieselben {links} Dateien";

                // Der Umfang gehört an die Aussage: „Deckungsgleich" heisst nur
                // deckungsgleich in dem, was überhaupt eingesammelt wurde. Bei „nur
                // Bilder" oder ohne Unterordner kann drüben trotzdem einiges liegen.
                DublettenGleichstandDetail =
                    (AblageSatz(treffer) + " " + UmfangSatz(mitUnterordnernImLauf, alleDateitypenImLauf)).Trim();
                return;
            }

            var teile = new System.Collections.Generic.List<string>();

            if (ohneGegenstueck > 0)
                teile.Add($"{ohneGegenstueck} nur im Dubletten-Ordner");

            if (nurRechts > 0)
                teile.Add($"{nurRechts} nur im Referenzordner");

            if (nichtLesbar > 0)
                teile.Add($"{nichtLesbar} gesperrt und ungeprüft");

            DublettenGleichstandText = "Nicht deckungsgleich – " + string.Join(", ", teile);
            DublettenGleichstandDetail =
                $"Dubletten-Ordner {links} Dateien, Referenzordner {rechts} Dateien.";
        }

        /// <summary>
        /// Sagt bei deckungsgleichen Ordnern zusätzlich, ob auch die Ablage übereinstimmt:
        /// ob jede Datei drüben im selben Unterordner liegt. Verglichen wird ab der
        /// jeweiligen Wurzel — die beiden Startordner dürfen verschieden heissen.
        /// </summary>
        private string AblageSatz(System.Collections.Generic.IReadOnlyList<ByteDublettenTreffer> treffer)
        {
            if (DublettenReferenzOrdner.Count != 1)
                return string.Empty;

            string linkeWurzel = DublettenOrdner;
            string rechteWurzel = DublettenReferenzOrdner[0];

            bool gleicheAblage = treffer.All(t =>
                string.Equals(
                    RelativerPfad(linkeWurzel, t.DublettenDatei),
                    RelativerPfad(rechteWurzel, t.ReferenzDatei),
                    StringComparison.OrdinalIgnoreCase));

            return gleicheAblage
                ? "Auch die Ablage stimmt überein – jede Datei liegt auf beiden Seiten im selben Unterordner."
                : "Die Dateien sind dieselben, liegen drüben aber teils in anderen Unterordnern.";
        }

        /// <summary>
        /// Nennt die Grenzen des Laufs, sobald er nicht alles erfasst hat. Ohne diesen
        /// Zusatz läse sich „deckungsgleich" als Aussage über den ganzen Ordner, obwohl
        /// vielleicht nur die Bilder der obersten Ebene verglichen wurden.
        /// </summary>
        private static string UmfangSatz(bool mitUnterordnern, bool alleDateitypen)
        {
            if (mitUnterordnern && alleDateitypen)
                return string.Empty;

            string was = alleDateitypen ? "alle Dateitypen" : "nur Bilddateien";
            string wo = mitUnterordnern ? "mit allen Unterordnern" : "nur in der obersten Ebene";

            return $"Verglichen wurde {was}, {wo}.";
        }

        private static string RelativerPfad(string wurzel, string datei)
        {
            try { return Path.GetRelativePath(wurzel, datei); }
            catch { return datei; }
        }

        #endregion

        /// <summary>
        /// Restzeit aus dem bisherigen Tempo. Die Mengen sind <c>long</c>, weil hier auch
        /// in Bytes gerechnet wird — bei Stückzahlen greift dieselbe Rechnung.
        /// </summary>
        private static string SchaetzeRestzeit(TimeSpan verstrichen, long erledigt, long gesamt)
        {
            if (erledigt <= 0 || erledigt >= gesamt)
                return string.Empty;

            double proEinheit = verstrichen.TotalSeconds / erledigt;
            int restSek = (int)Math.Ceiling(proEinheit * (gesamt - erledigt));

            string text = FormatiereRestzeit(restSek);
            return text.Length > 0 ? $"noch ca. {text}" : string.Empty;
        }

        #endregion

        #region Markierung

        [RelayCommand]
        private void CommandExecuteAlleDublettenMarkieren()
        {
            // Nur bestätigte Duplikate – ungeprüfte Auflistungseinträge bleiben unberührt.
            foreach (var t in ByteDublettenTreffer.Where(t => t.IstBestaetigt && !t.IstGeloescht))
                t.IstMarkiert = true;
        }

        [RelayCommand]
        private void CommandExecuteKeineDublettenMarkieren()
        {
            foreach (var t in ByteDublettenTreffer)
                t.IstMarkiert = false;
        }

        [RelayCommand]
        private void CommandExecuteDubletteImExplorerZeigen(ByteDublettenTreffer? treffer)
        {
            if (treffer is null || !File.Exists(treffer.DublettenDatei))
                return;

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{treffer.DublettenDatei}\"")
            {
                UseShellExecute = true
            });
        }

        #endregion

        #region Löschen

        private bool CanExecuteMarkierteLoeschen()
            => !IsDublettenAufgabeLäuft && DublettenMarkierteAnzahl > 0;

        [RelayCommand(CanExecute = nameof(CanExecuteMarkierteLoeschen), IncludeCancelCommand = true)]
        private async Task CommandExecuteMarkierteLoeschen(CancellationToken token)
        {
            // IstBestaetigt ist die Sicherheitsschranke: Ohne geprüftes Gegenstück
            // darf nichts gelöscht werden, auch wenn die Markierung gesetzt wäre.
            var zuLoeschen = ByteDublettenTreffer
                .Where(t => t.IstMarkiert && t.IstBestaetigt && !t.IstGeloescht)
                .ToList();

            if (zuLoeschen.Count == 0)
                return;

            // Der Weg zurück steht ausdrücklich mit dabei.
            //
            // Dass es der Papierkorb ist, sagt die Ansicht an mehreren Stellen — wie man
            // von dort etwas zurückholt, weiss aber nicht jeder. Und gefragt wird genau
            // in dem Moment, in dem es zählt: bevor geklickt wird, nicht danach.
            //
            // Die Laufwerksprüfung hängt an der ersten Datei, nicht am Ordner: Gelöscht
            // werden die Dateien, und die liegen alle im selben Dubletten-Ordner.
            string warnung = ByteDublettenService.PapierkorbWarnung(zuLoeschen[0].DublettenDatei);

            // Namenstreffer sagen nichts über den Inhalt. Das gehört in die Rückfrage,
            // und zwar bevor geklickt wird: Bei abweichender Grösse steht sogar fest,
            // dass es nicht dieselbe Datei ist.
            int nurName = zuLoeschen.Count(t => t.IstNurNamensTreffer);
            int abweichend = zuLoeschen.Count(t => t.HatAbweichendeGroesse);

            string namensWarnung = nurName == 0
                ? string.Empty
                : $"\n\nACHTUNG — bei {nurName} Datei(en) wurde nur der Name verglichen,\n"
                  + "der Inhalt wurde nicht gelesen. Gleicher Name heisst nicht gleiche Datei."
                  + (abweichend == 0
                      ? string.Empty
                      : $"\nBei {abweichend} davon ist das Gegenstück im Bestand sogar\n"
                        + "unterschiedlich gross — dort ist es sicher nicht dieselbe Datei.");

            var antwort = MessageBox.Show(
                $"{zuLoeschen.Count} Dublette(n) in den Papierkorb verschieben?\n\n" +
                $"Es werden {DublettenMarkierteGroesseText} frei.\n" +
                "Der Referenzbestand bleibt unangetastet." +
                namensWarnung + "\n\n" +
                "Nichts wird endgültig gelöscht. Zurückholen geht so:\n" +
                "Papierkorb auf dem Desktop öffnen, die Dateien markieren,\n" +
                "Rechtsklick, „Wiederherstellen\" — sie landen wieder\n" +
                "an ihrem ursprünglichen Ort." +
                warnung,
                "Byte-Duplikate aufräumen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (antwort != MessageBoxResult.Yes)
                return;

            IsDublettenAufgabeLäuft = true;
            DublettenFortschritt = 0;
            DublettenFortschrittMax = zuLoeschen.Count;
            DublettenRestzeit = string.Empty;
            DublettenStatus = $"Wird in den Papierkorb verschoben … 0 von {zuLoeschen.Count}";

            int erledigt = 0;
            int fehler = 0;
            var uhr = Stopwatch.StartNew();

            try
            {
                foreach (var treffer in zuLoeschen)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        // Sicherheitsnetz: nie löschen, wenn das Gegenstück fehlt.
                        if (!File.Exists(treffer.ReferenzDatei))
                        {
                            fehler++;
                            continue;
                        }

                        await Task.Run(() => ByteDublettenService.InDenPapierkorb(treffer.DublettenDatei), token);
                        treffer.IstGeloescht = true;
                        treffer.IstMarkiert = false;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        fehler++;
                    }

                    DublettenFortschritt = ++erledigt;
                    DublettenStatus =
                        $"Wird in den Papierkorb verschoben … {erledigt} von {zuLoeschen.Count}";
                    DublettenRestzeit = SchaetzeRestzeit(uhr.Elapsed, erledigt, zuLoeschen.Count);
                }

                DublettenStatus = fehler == 0
                    ? $"{erledigt} Dublette(n) in den Papierkorb verschoben."
                    : $"{erledigt - fehler} verschoben, {fehler} übersprungen (gesperrt oder Referenzdatei fehlt).";
            }
            catch (OperationCanceledException)
            {
                DublettenStatus = $"Abgebrochen — {erledigt} Dublette(n) bereits im Papierkorb.";
            }
            finally
            {
                DublettenRestzeit = string.Empty;
                DublettenFortschritt = 0;
                MeldeMarkierungGeaendert();
                IsDublettenAufgabeLäuft = false;

                // Der Ordnervergleich beschrieb den Stand vor dem Löschen. Sobald etwas
                // weg ist, gilt er nicht mehr – „deckungsgleich" stünde sonst über einem
                // Ordner, aus dem gerade alles verschwunden ist.
                if (erledigt > 0)
                    LeereGleichstand();

                // Nach dem Löschlauf ist der Ordner womöglich leer – dann darf er weg.
                PruefeDublettenOrdnerLeer();
            }
        }

        #endregion
    }
}
