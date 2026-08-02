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
    /// Byte-Dubletten-Ansicht: sucht byte-identische Bilder des Basisordners in
    /// beliebigen Vergleichsordnern und räumt die Duplikate dort weg.
    /// </summary>
    public partial class AufgabeViewModel
    {
        #region Ansicht umschalten

        /// <summary>Dritte Ansicht (Byte-Dubletten aufräumen) aktiv.</summary>
        [ObservableProperty]
        private bool _isDublettenAnsicht;

        [RelayCommand]
        private void CommandExecuteDublettenAnsichtOeffnen()
        {
            // Bildmodus verlassen, sonst liegen zwei Vollflächen-Ansichten übereinander.
            IsImageMaximiert = false;

            // Basisordner beim ersten Öffnen aus dem aktuellen Bild vorbelegen.
            if (string.IsNullOrWhiteSpace(DublettenBasisOrdner))
                DublettenBasisOrdner = AktuellerBildOrdner() ?? string.Empty;

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

        /// <summary>Ordner, dessen Bilder behalten werden.</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteByteDublettenSuchenCommand))]
        private string _dublettenBasisOrdner = string.Empty;

        /// <summary>Ordner, in denen Duplikate gesucht und gelöscht werden dürfen.</summary>
        public ObservableCollection<string> DublettenVergleichsOrdner { get; } = new();

        /// <summary>In der Ordnerliste markierter Eintrag (zum Entfernen).</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteVergleichsOrdnerEntfernenCommand))]
        private string? _ausgewaehlterVergleichsOrdner;

        /// <summary>Gefundene Duplikate.</summary>
        public ObservableCollection<ByteDublettenTreffer> ByteDublettenTreffer { get; } = new();

        [ObservableProperty]
        private bool _dublettenMitUnterordnern = true;

        [ObservableProperty]
        private string _dublettenStatus = "Basisordner und Vergleichsordner wählen, dann suchen.";

        [ObservableProperty]
        private int _dublettenFortschritt;

        [ObservableProperty]
        private int _dublettenFortschrittMax = 100;

        [ObservableProperty]
        private string _dublettenRestzeit = string.Empty;

        /// <summary>Sperrt die Commands, solange Suche oder Löschlauf aktiv ist.</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteByteDublettenSuchenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteMarkierteLoeschenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteVergleichsOrdnerHinzufuegenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteVergleichsOrdnerEntfernenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBasisOrdnerWaehlenCommand))]
        private bool _isDublettenAufgabeLäuft;

        /// <summary>Anzahl der zum Löschen vorgemerkten Treffer.</summary>
        public int DublettenMarkierteAnzahl =>
            ByteDublettenTreffer.Count(t => t.IstMarkiert && !t.IstGeloescht);

        /// <summary>Speicherplatz, der beim Löschen frei wird.</summary>
        public string DublettenMarkierteGroesseText
        {
            get
            {
                long summe = ByteDublettenTreffer
                    .Where(t => t.IstMarkiert && !t.IstGeloescht)
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
        private void CommandExecuteBasisOrdnerWaehlen()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Basisordner wählen — diese Bilder werden behalten",
                InitialDirectory = OrdnerOderLeer(DublettenBasisOrdner) ?? AktuellerBildOrdner() ?? string.Empty
            };

            if (dlg.ShowDialog() == true)
                DublettenBasisOrdner = dlg.FolderName;
        }

        [RelayCommand(CanExecute = nameof(CanExecuteOrdnerBearbeiten))]
        private void CommandExecuteVergleichsOrdnerHinzufuegen()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Vergleichsordner wählen — hier werden Duplikate gelöscht",
                Multiselect = true,
                InitialDirectory = OrdnerOderLeer(DublettenBasisOrdner) ?? string.Empty
            };

            if (dlg.ShowDialog() != true)
                return;

            foreach (var ordner in dlg.FolderNames)
            {
                if (!DublettenVergleichsOrdner.Contains(ordner, StringComparer.OrdinalIgnoreCase))
                    DublettenVergleichsOrdner.Add(ordner);
            }

            CommandExecuteByteDublettenSuchenCommand.NotifyCanExecuteChanged();
        }

        private bool CanExecuteVergleichsOrdnerEntfernen()
            => !IsDublettenAufgabeLäuft && !string.IsNullOrEmpty(AusgewaehlterVergleichsOrdner);

        [RelayCommand(CanExecute = nameof(CanExecuteVergleichsOrdnerEntfernen))]
        private void CommandExecuteVergleichsOrdnerEntfernen()
        {
            if (AusgewaehlterVergleichsOrdner is null)
                return;

            DublettenVergleichsOrdner.Remove(AusgewaehlterVergleichsOrdner);
            AusgewaehlterVergleichsOrdner = null;
            CommandExecuteByteDublettenSuchenCommand.NotifyCanExecuteChanged();
        }

        private static string? OrdnerOderLeer(string ordner)
            => !string.IsNullOrWhiteSpace(ordner) && Directory.Exists(ordner) ? ordner : null;

        #endregion

        #region Suche

        private bool CanExecuteByteDublettenSuchen()
            => !IsDublettenAufgabeLäuft
               && !string.IsNullOrWhiteSpace(DublettenBasisOrdner)
               && Directory.Exists(DublettenBasisOrdner)
               && DublettenVergleichsOrdner.Count > 0;

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

                var treffer = await ByteDublettenService.FindeByteDublettenAsync(
                    DublettenBasisOrdner,
                    DublettenVergleichsOrdner.ToList(),
                    DublettenMitUnterordnern,
                    fortschritt,
                    token);

                token.ThrowIfCancellationRequested();

                SetzeTreffer(treffer);

                DublettenStatus = treffer.Count == 0
                    ? "Keine Byte-Duplikate gefunden."
                    : $"{treffer.Count} Byte-Duplikate gefunden — {DublettenMarkierteGroesseText} können frei werden.";
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
            foreach (var t in ByteDublettenTreffer.Where(t => !t.IstGeloescht))
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
            var zuLoeschen = ByteDublettenTreffer
                .Where(t => t.IstMarkiert && !t.IstGeloescht)
                .ToList();

            if (zuLoeschen.Count == 0)
                return;

            var antwort = MessageBox.Show(
                $"{zuLoeschen.Count} Dublette(n) in den Papierkorb verschieben?\n\n" +
                $"Es werden {DublettenMarkierteGroesseText} frei.\n" +
                "Die Dateien im Basisordner bleiben unangetastet.",
                "Byte-Duplikate aufräumen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (antwort != MessageBoxResult.Yes)
                return;

            IsDublettenAufgabeLäuft = true;
            DublettenFortschritt = 0;
            DublettenFortschrittMax = zuLoeschen.Count;

            int erledigt = 0;
            int fehler = 0;

            try
            {
                foreach (var treffer in zuLoeschen)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        // Sicherheitsnetz: nie löschen, wenn das Gegenstück fehlt.
                        if (!File.Exists(treffer.BasisDatei))
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
                    DublettenStatus = $"Papierkorb: {erledigt} / {zuLoeschen.Count}";
                }

                DublettenStatus = fehler == 0
                    ? $"{erledigt} Dublette(n) in den Papierkorb verschoben."
                    : $"{erledigt - fehler} verschoben, {fehler} übersprungen (gesperrt oder Basisdatei fehlt).";
            }
            catch (OperationCanceledException)
            {
                DublettenStatus = $"Abgebrochen — {erledigt} Dublette(n) bereits im Papierkorb.";
            }
            finally
            {
                MeldeMarkierungGeaendert();
                IsDublettenAufgabeLäuft = false;
            }
        }

        #endregion
    }
}
