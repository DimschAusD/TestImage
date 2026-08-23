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

            // Dubletten-Ordner beim ersten Öffnen aus dem aktuellen Bild vorbelegen.
            if (string.IsNullOrWhiteSpace(DublettenOrdner))
                DublettenOrdner = AktuellerBildOrdner() ?? string.Empty;

            IsDublettenAnsicht = true;
        }

        [RelayCommand]
        private void CommandExecuteDublettenAnsichtSchliessen()
        {
            IsDublettenAnsicht = false;
        }

        /// <summary>Ordner des gerade angezeigten Bildes, sonst null.</summary>
        private string? AktuellerBildOrdner()
        {
            var pfad = SelectedBildchen?.BName;

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
        public partial string DublettenOrdner { get; set; } = string.Empty;

        /// <summary>Ordner, deren Dateien behalten werden (Bestand).</summary>
        public ObservableCollection<string> DublettenReferenzOrdner { get; } = new();

        /// <summary>In der Ordnerliste markierter Eintrag (zum Entfernen).</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteReferenzOrdnerEntfernenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteReferenzOrdnerEineEbeneHochCommand))]
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
            await ZeigeOrdnerInhaltAsync(DublettenOrdner, token);
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

        /// <summary>Text neben dem Entfernen-Knopf, wenn der Ordner noch Dateien enthält.</summary>
        public string DublettenOrdnerRestText => DublettenOrdnerRestDateien switch
        {
            < 0 => string.Empty,
            0 => string.Empty,
            1 => "noch 1 Datei im Dubletten-Ordner",
            _ => $"noch {DublettenOrdnerRestDateien} Dateien im Dubletten-Ordner"
        };

        partial void OnDublettenOrdnerRestDateienChanged(int value)
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
                DublettenOrdnerRestTooltip = string.Empty;
                DublettenOrdnerIstLeer = false;
                AktualisiereLeerHinweis();
                return;
            }

            if (probe.Count == 0)
            {
                DublettenOrdnerRestDateien = 0;
                DublettenOrdnerRestTooltip = string.Empty;
                DublettenOrdnerIstLeer = true;
                AktualisiereLeerHinweis();
                return;
            }

            DublettenOrdnerIstLeer = false;
            DublettenOrdnerRestDateien = ByteDublettenService.ZaehleVerbleibendeDateien(DublettenOrdner);

            var namen = probe.Select(Path.GetFileName).Take(10);
            DublettenOrdnerRestTooltip =
                "Diese Dateien liegen noch im Ordner (Auszug):\n" + string.Join("\n", namen)
                + "\n\nVersteckte Dateien wie desktop.ini oder Thumbs.db zählen mit – "
                + "im Explorer sind sie oft ausgeblendet.";

            AktualisiereLeerHinweis();
        }

        // Auch beim blossen Setzen des Pfades prüfen: Der Ordner kann längst leer sein,
        // etwa nach einem Aufräumen ausserhalb der Anwendung.
        partial void OnDublettenOrdnerChanged(string value) => PruefeDublettenOrdnerLeer();

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
        public partial bool DublettenSucheGelaufen { get; set; }

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

            DublettenLeerHinweis = DublettenSucheGelaufen
                ? "Keine byte-gleichen Dateien im Referenzbestand gefunden"
                : "Bereit – auf „Byte-Duplikate suchen“ klicken";
        }

        [ObservableProperty]
        public partial bool DublettenMitUnterordnern { get; set; } = true;

        [ObservableProperty]
        public partial string DublettenStatus { get; set; } = "Dubletten-Ordner und Referenzordner wählen, dann suchen.";

        [ObservableProperty]
        public partial int DublettenFortschritt { get; set; }

        [ObservableProperty]
        public partial int DublettenFortschrittMax { get; set; } = 100;

        [ObservableProperty]
        public partial string DublettenRestzeit { get; set; } = string.Empty;

        /// <summary>Sperrt die Commands, solange Suche oder Löschlauf aktiv ist.</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteByteDublettenSuchenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteMarkierteLoeschenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteReferenzOrdnerHinzufuegenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteReferenzOrdnerEntfernenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteDublettenOrdnerWaehlenCommand))]
        public partial bool IsDublettenAufgabeLäuft { get; set; }

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
            DublettenStatus = "Ordner wird gelesen …";

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

                var liste = new System.Collections.Generic.List<ByteDublettenTreffer>(dateien.Count);
                var uhr = Stopwatch.StartNew();

                for (int i = 0; i < dateien.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    long groesse;
                    try { groesse = new FileInfo(dateien[i]).Length; }
                    catch { groesse = 0; }

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
            DublettenRestzeit = string.Empty;

            var uhr = Stopwatch.StartNew();

            try
            {
                var fortschritt = new Progress<(int Erledigt, int Gesamt, string Text)>(p =>
                {
                    DublettenStatus = p.Text;

                    if (p.Gesamt > 0)
                    {
                        DublettenFortschrittMax = p.Gesamt;
                        DublettenFortschritt = p.Erledigt;
                        DublettenRestzeit = SchaetzeRestzeit(uhr.Elapsed, p.Erledigt, p.Gesamt);
                    }
                });

                var nichtLesbar = new System.Collections.Generic.List<string>();

                var treffer = await ByteDublettenService.FindeByteDublettenAsync(
                    DublettenOrdner,
                    DublettenReferenzOrdner.ToList(),
                    DublettenMitUnterordnern,
                    DublettenAlleDateitypen,
                    fortschritt,
                    token,
                    nichtLesbar);

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

                DublettenStatus = (treffer.Count == 0
                    ? "Keine Byte-Duplikate gefunden."
                    : $"{treffer.Count} Byte-Duplikate gefunden — {DublettenMarkierteGroesseText} können frei werden.")
                    + zusatz;
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
                IsDublettenAufgabeLäuft = false;

                // Stand des Ordners auffrischen – er kann sich seit dem Einlesen
                // geändert haben, etwa durch Aufräumen ausserhalb der Anwendung.
                PruefeDublettenOrdnerLeer();
            }
        }

        private static string SchaetzeRestzeit(TimeSpan verstrichen, int erledigt, int gesamt)
        {
            if (erledigt <= 0 || erledigt >= gesamt)
                return string.Empty;

            var proStueck = verstrichen.TotalSeconds / erledigt;
            int restSek = (int)Math.Ceiling(proStueck * (gesamt - erledigt));

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

            var antwort = MessageBox.Show(
                $"{zuLoeschen.Count} Dublette(n) in den Papierkorb verschieben?\n\n" +
                $"Es werden {DublettenMarkierteGroesseText} frei.\n" +
                "Der Referenzbestand bleibt unangetastet.\n\n" +
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

                // Nach dem Löschlauf ist der Ordner womöglich leer – dann darf er weg.
                PruefeDublettenOrdnerLeer();
            }
        }

        #endregion
    }
}
