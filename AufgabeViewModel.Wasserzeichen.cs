using System;
using System.IO;
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

        /// <summary>True, wenn eine Maske gelernt wurde – sonst greift nur die Metadatenprüfung.</summary>
        [ObservableProperty]
        private bool _wasserzeichenMaskeVorhanden = WasserzeichenService.MaskeVorhanden;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteWasserzeichenMaskeLernenCommand))]
        private bool _wasserzeichenAufgabeLäuft;

        #endregion

        #region Maske lernen

        private bool CanExecuteWasserzeichenMaskeLernen() => !WasserzeichenAufgabeLäuft;

        /// <summary>
        /// Lernt die Maske aus einem Ordner, in dem alle Bilder dasselbe Wasserzeichen
        /// tragen. Ohne diesen Schritt kann nur nach Metadaten gesucht werden.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteWasserzeichenMaskeLernen), IncludeCancelCommand = true)]
        private async Task CommandExecuteWasserzeichenMaskeLernen(CancellationToken token)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Ordner mit Beispielbildern – alle müssen dasselbe Wasserzeichen tragen"
            };

            if (dlg.ShowDialog() != true)
                return;

            WasserzeichenAufgabeLäuft = true;
            WasserzeichenStatus = "Lerne Wasserzeichen-Muster …";

            try
            {
                var fortschritt = new Progress<(int Erledigt, int Gesamt)>(p =>
                    WasserzeichenStatus = $"Lerne Muster … {p.Erledigt}/{p.Gesamt}");

                int anzahl = await WasserzeichenService.LerneMaskeAsync(dlg.FolderName, fortschritt, token);

                WasserzeichenMaskeVorhanden = WasserzeichenService.MaskeVorhanden;

                WasserzeichenStatus = anzahl > 0
                    ? $"Muster aus {anzahl} Bildern gelernt. Ordner neu indexieren, um es anzuwenden."
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
