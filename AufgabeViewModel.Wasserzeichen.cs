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
                var fortschritt = new Progress<(int Erledigt, int Gesamt)>(p =>
                    WasserzeichenStatus = $"Lerne Muster „{name}“ … {p.Erledigt}/{p.Gesamt}");

                int anzahl = await WasserzeichenService.LerneMaskeAsync(dlg.FolderName, name, fortschritt, token);

                AktualisiereWasserzeichenMuster();

                WasserzeichenStatus = anzahl > 0
                    ? $"Muster „{name}“ aus {anzahl} Bildern gelernt. Ordner neu indexieren, um es anzuwenden."
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
                return;
            }

            var befunde = WasserzeichenService.Lade(ordner);
            if (befunde.Count == 0)
            {
                WasserzeichenTrefferAnzahl = 0;
                return;
            }

            UebertrageWasserzeichenBefunde(befunde);
        }

        #endregion
    }
}
