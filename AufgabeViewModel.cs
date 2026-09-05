using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TestImage.Bildersuche;
using TestImage.Geraete;

namespace TestImage
{
    public partial class AufgabeViewModel : ObservableObject, IFileDragDropTarget    /*ModelBase,*/
    {
        // v2x.0.367.242 Beta 2026-01-31 (.NETCore v9.0)
        // v2x.0.300.842 Beta 2026-02-08 (.NETCore v9.0)
        // v2x.0.300.842 Beta 2026-02-08 (.NETCore v9.0)
        // v2x.0.195.838 Beta 2026-04-23 (.NETCore v10.0)
        // v2x.0.175.654 Beta 2026-04-24 (.NETCore v10.0)
        // v2x.0.172.205 Beta 2026-06-27 (.NETCore net10.0)
        // v2x.0.129.332 Beta 2026-07-18 (.NETCore net10.0)
        // v2x.0.80.860 Beta 2026-08-20 (.NETCore net10.0)
        // v2x.0.72.254 Beta 2026-08-30 (.NETCore net10.0)
        // v2x.0.73.140 Beta 2026-09-01 (.NETCore net10.0)
        // v2x.0.70.881 Beta 2026-09-02 (.NETCore net10.0)   
        // v2x.0.70.751 Beta 2026-09-03 (.NETCore net10.0)
        [ObservableProperty]
        public partial string Version { get; set; } = "v2x.0.70.751 Beta 2026-09-03 (.NETCore net10.0)";




        [ObservableProperty]
        public partial int CountInnerZählerTest { get; set; }


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteVerschiebenZurückCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildInsKeinFavVerzeichnisVerschiebenCommand))]
        public partial string BildchenVorher { get; set; } = string.Empty;

        private string _filterText = string.Empty;

        /// <summary>
        /// True, solange <c>AufgabenView.Refresh()</c> läuft. Solange wird
        /// <c>SelectedBildchen</c> nicht gemeldet — siehe
        /// <see cref="AktualisiereAufgabenView"/>.
        /// </summary>
        private bool _ansichtWirdAufgebaut;


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildInsKeinFavVerzeichnisVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildStretchAnpassenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildLinksCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildNachRechtsCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteAlleBilderInsKeinFavVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteSuchenGleichesBildByteVergleichCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteSuchenUngefährGleichesBildCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildInsKIFehlerVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildInsBesondersVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteAlleBilderMiteinanderAufByteGleichheitPrüfenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteAlleBilderNeuEinlesenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteOrdnerEineEbeneHochCommand))]
        // Gegenstück zur Sperre im Indexieren: Läuft ein Abgleich, ist der Index-Knopf aus.
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteOrdnerIndexierenCommand))]
        public partial bool PrüfungLäuft { get; set; } = false;


        /// <summary>
        /// True wenn alle Bilder nach kein_Fav verschoben wurden (kein BildFürLinks==false mehr).
        /// Hintergrund soll dann rot werden.
        /// </summary>
        [ObservableProperty]
        public partial bool AlleBilderVerschoben { get; set; } = false;

        [ObservableProperty]
        public partial int InnerZählerCount { get; set; } = 0;

        // ProzentAbgleich ist entfallen. Es war der fertig formatierte Prozenttext für
        // TXBL_ProzentProgressbar über der Miniaturleiste. Seit dieser Balken in
        // BRD_AufgabeFortschritt aufgegangen ist, war die Eigenschaft an keine Bindung
        // mehr geknüpft — sie wurde nur noch geschrieben.
        //
        // Schaden angerichtet hat sie trotzdem: Sie war eine zweite Wahrheit über
        // denselben Fortschritt und lief gegen PercentageValueVerschieben. Der Fortschritt
        // aller Prüfbefehle steht jetzt allein in PercentageValueVerschieben.

        /// <summary>
        /// Gets or sets a value indicating whether the image file is damaged.
        /// <br>  Ampelfarbe: ConverterAmpelFarbe — true heisst auffaellig.</br>
        /// </summary>
        [ObservableProperty]
        public partial bool? IsBildDateiBeschädigt { get; set; } = false;


        /// <summary>
        /// Gets or sets a value indicating whether the header matches the extension.
        /// <br>  Ampelfarbe: ConverterAmpelFarbe — positiv formuliert, daher ConverterParameter=Gut.</br>
        /// </summary>
        [ObservableProperty]
        public partial bool? IsHeaderPassendZurErweiterung { get; set; } = false;


        /// <summary>
        /// Gets or sets a value indicating whether a frame is present in the image.
        /// <br>  Ampelfarbe: ConverterAmpelFarbe — positiv formuliert, daher ConverterParameter=Gut.</br>
        /// </summary>
        [ObservableProperty]
        public partial bool? IsFrameImBildDrin { get; set; } = false;


        /// <summary>
        /// Gets or sets a value indicating whether the Bild download is corrupted.
        /// <br>  Ampelfarbe: ConverterAmpelFarbe — true heisst auffaellig.</br>
        /// </summary>
        [ObservableProperty]
        public partial bool? IsBildDownloadCorrupted { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether the image file is null or missing.
        /// <br>  Ampelfarbe: ConverterAmpelFarbe — true heisst auffaellig.</br>
        /// <br>  (Die alte Notiz nannte hier G2, gebunden war aber G5. G2 hätte die Farben
        /// vertauscht: eine 0-Byte-Datei wäre grün gewesen.)</br>
        /// </summary>
        [ObservableProperty]
        public partial bool? IsBildNullDatei { get; set; } = false;

        /// <summary>
        /// Meldungstext der Karte BRD_MeldungKarte im Streifen über dem Bild: „nix .jpg"
        /// bei einer abgelegten Nicht-Bilddatei, Restzeiten und Durchsätze der Sammelläufe.
        ///
        /// Leer heisst „nichts zu melden" — die Karte blendet sich dann aus. Deshalb hier
        /// auch kein Startwert mehr; „⓵ mvvmDrop" hätte sonst beim Programmstart im
        /// Streifen gestanden.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteMeldungSchliessenCommand))]
        public partial string LabelDropContent { get; set; } = string.Empty;

        /// <summary>
        /// Nur zu schliessen, wenn wirklich etwas steht — und nicht, während ein Vorgang
        /// läuft: Dann trägt die Karte Restzeit und Durchsatz, und die sollen nicht beim
        /// ersten Klick verschwinden.
        /// </summary>
        private bool CanExecuteMeldungSchliessen()
            => !string.IsNullOrEmpty(LabelDropContent) && !AufgabeLäuft;

        /// <summary>
        /// Blendet die Meldungskarte aus.
        ///
        /// Eine Meldung wie „Filter blendet dieses Bild aus" gehört zum letzten Drop.
        /// Bis hierher blieb sie stehen, bis der nächste Vorgang den Text überschrieb —
        /// sie stand also noch da, wenn längst weitergearbeitet wurde. Ausgelöst wird das
        /// aus NormalAnsicht.xaml.cs beim ersten Klick irgendwo in der Ansicht.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteMeldungSchliessen))]
        private void CommandExecuteMeldungSchliessen()
        {
            LabelDropContent = string.Empty;
        }

        [ObservableProperty]
        public partial ImageSource Bildchen { get; set; } = null;

        [ObservableProperty]
        public partial bool SollBildGeprüftWerden { get; set; } = false;

        [ObservableProperty]
        public partial double PercentageValueVerschieben { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteAlleBilderMiteinanderAufByteGleichheitPrüfenCommand))]
        public partial bool MultiByteParallelGleichheit { get; set; } = true;

        [ObservableProperty]
        public partial bool IsImageMaximiert { get; set; } = false;

        /// <summary>
        /// Tastenübersicht im Bildmodus eingeblendet (F1 oder ?). Dort gibt es keine
        /// sichtbaren Knöpfe – ohne diese Liste wären die Kürzel nicht auffindbar.
        /// </summary>
        [ObservableProperty]
        public partial bool IsVollbildHilfeOffen { get; set; }

        /// <summary>Blendet die Tastenübersicht ein oder aus (Hinweisfeld und F1).</summary>
        [RelayCommand]
        private void CommandExecuteVollbildHilfeToggle()
            => IsVollbildHilfeOffen = !IsVollbildHilfeOffen;

        [ObservableProperty]
        public partial bool IsWebcamAktiv { get; set; }

        [ObservableProperty]
        public partial bool IsMikrofonAktiv { get; set; }

        [ObservableProperty]
        public partial bool IsScreenShareAktiv { get; set; }

        /// <summary>Mindestens ein Gerät hängt über Bluetooth am Rechner.</summary>
        [ObservableProperty]
        public partial bool IsBluetoothAktiv { get; set; }

        /// <summary>
        /// Ein Eingabegerät ist seit dem Start neu dazugekommen. Eigene Anzeige neben
        /// <see cref="IsBluetoothAktiv"/>: Ein Kopfhörer, der sich anmeldet, ist Alltag;
        /// eine Tastatur, die sich anmeldet, kann tippen.
        /// </summary>
        [ObservableProperty]
        public partial bool IsBluetoothWarnung { get; set; }

        /// <summary>Was genau hängt dran – steht im Tooltip des Feldes.</summary>
        [ObservableProperty]
        public partial string BluetoothHinweis { get; set; } = "Bluetooth";

        private readonly System.Windows.Threading.DispatcherTimer _geraeteTimer;

        private void GeraeteTimerTick(object? sender, EventArgs e)
        {
            IsWebcamAktiv = GeraeteWaechter.IstAktiv("webcam");
            IsMikrofonAktiv = GeraeteWaechter.IstAktiv("microphone");
            IsScreenShareAktiv = GeraeteWaechter.IstAktiv("screenCapture");

            AktualisiereBluetooth();
        }

        /// <summary>
        /// Fragt den Bluetooth-Stand ab und formuliert den Tooltip.
        ///
        /// Anders als Kamera und Mikrofon lässt sich bei Bluetooth nicht sagen, ob gerade
        /// etwas übertragen wird – Windows führt dazu nichts. Angezeigt wird deshalb, was
        /// angemeldet ist, und hervorgehoben, was tippen könnte.
        /// </summary>
        private void AktualisiereBluetooth()
        {
            var stand = BluetoothWaechter.HoleStand();

            IsBluetoothAktiv = stand.HatGeraete;
            IsBluetoothWarnung = stand.HatWarnung;

            if (stand.HatWarnung)
            {
                BluetoothHinweis =
                    "Achtung: seit dem Start neu angemeldetes Eingabegerät\n"
                    + Aufzaehlung(stand.NeueEingabegeraete)
                    + "\nEin solches Gerät kann tippen und klicken. War das nicht du, "
                    + "Bluetooth abschalten und die Kopplungen durchsehen.";
                return;
            }

            if (!stand.AdapterVorhanden)
            {
                BluetoothHinweis = "Bluetooth — aus oder kein Adapter vorhanden";
                return;
            }

            if (!stand.HatGeraete)
            {
                BluetoothHinweis = "Bluetooth — an, kein Gerät angemeldet";
                return;
            }

            BluetoothHinweis = $"Bluetooth — {stand.Geraete.Count} angemeldet\n"
                               + Aufzaehlung(stand.Geraete);

            if (stand.Eingabegeraete.Count > 0)
            {
                BluetoothHinweis += "\nEingabegerät darunter (kann tippen und klicken): "
                                    + string.Join(", ", stand.Eingabegeraete);
            }
        }

        /// <summary>
        /// Geräteliste für den Tooltip: je Zeile eines, höchstens sechs.
        ///
        /// Vorher standen alle Namen durch Kommas getrennt in einer Zeile — mit ein paar
        /// angemeldeten Geräten lief der Tooltip quer über den Bildschirm. Untereinander
        /// bleibt er schmal, und die Obergrenze hält ihn auch dann kurz, wenn jemand seine
        /// halbe Wohnung gekoppelt hat.
        /// </summary>
        private static string Aufzaehlung(System.Collections.Generic.IReadOnlyList<string> namen)
        {
            const int hoechstens = 6;

            var zeilen = namen.Take(hoechstens).Select(n => "· " + n).ToList();

            if (namen.Count > hoechstens)
            {
                zeilen.Add($"· … und {namen.Count - hoechstens} weitere");
            }

            return string.Join("\n", zeilen);
        }

        #region UI_Output
        [ObservableProperty]
        public partial int OriginalImageWidth { get; set; } = -1;
        [ObservableProperty]
        public partial int OriginalImageHeight { get; set; } = -1;
        #endregion






        public AufgabeViewModel()
        {
            _geraeteTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _geraeteTimer.Tick += GeraeteTimerTick;
            _geraeteTimer.Start();
            GeraeteTimerTick(null, EventArgs.Empty);

            ocAufgabens = new ObservableCollection<MeinBildchen>();

            // Ändert sich die Bilderliste, ist der Schnell-Listen-Cache veraltet.
            ocAufgabens.CollectionChanged += (_, e) =>
            {
                _bildListeVeraltet = true;
                _ordnerEinheitVeraltet = true;

                // Nur beim kompletten Neuaufbau — anderer Ordner — den Vorrat wegwerfen.
                //
                // Beim Verschieben einzelner Bilder bleibt er gültig: Er ist nach Pfad
                // geschlüsselt, ein verschobenes Bild hat einen neuen, und sein alter
                // Eintrag wird nie mehr getroffen und fällt von selbst hinten heraus. Ihn
                // hier zu leeren hiesse, ausgerechnet im Sortierlauf jedes folgende Bild
                // wieder von der Platte zu holen.
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                {
                    VorratLeeren();
                }
            };

            AufgabenView = CollectionViewSource.GetDefaultView(ocAufgabens) as ListCollectionView;
            AufgabenView.SortDescriptions.Clear();
            AufgabenView.CustomSort = new NaturalStringComparer();

            AufgabenView.Filter += PersonViewSource_Filter;

            // Erkannten Text zum jeweils angezeigten Bild nachziehen.
            //
            // Bewusst hier und nicht im Setter von SelectedBildchen: Das Bild wechselt
            // auch über die Pfeiltasten, die Miniaturleiste und die Trefferliste, und
            // die gehen alle über die Ansicht, nicht über die Eigenschaft. CurrentChanged
            // ist die eine Stelle, an der jeder dieser Wege vorbeikommt.
            AufgabenView.CurrentChanged += (s, e) => OcrVolltextAnfordern();

            // Warte-Indikator der Vollbildansicht an das Bildladen hängen.
            WarteAnzeigeAnkoppeln();

        }

        private bool CanExecuteBildNachLinksCommand()
        {
            return !PrüfungLäuft
                    && AufgabenView != null
                    && AufgabenView.CurrentPosition > 0;

        }


        [RelayCommand(CanExecute = nameof(CanExecuteBildNachLinksCommand))]
        private void CommandExecuteBildLinks()
        {

            // Copilot Code

            // 422


            if (AufgabenView.CurrentPosition < 0)
            {
                return;
            }

            // Hier stand eine Existenzprüfung des aktuellen Bildes, die bei jedem
            // Blätterschritt auf die Platte ging — im UI-Faden. Schlief die Platte, kostete
            // allein das die ganze Anlaufzeit, und zwar auch dann, wenn alle Dateien da
            // waren. Verschwundene Dateien fallen jetzt dort auf, wo sie ohnehin auffallen:
            // beim Laden des Bildes, siehe EntferneVerschwundenesBild.

            // Kein aktuelles Element → nichts zu tun
            if (AufgabenView.CurrentPosition <= 0 || AufgabenView.Count == 0)
            {
                return;
            }

            // Eine eindeutige Ausgangsposition
            int startIndex = AufgabenView.CurrentPosition;

            // Der Vorauslauf soll dorthin arbeiten, wo man gerade hingeht.
            _blätterRichtung = -1;

            // Nach links suchen: startIndex - 1 → 0
            for (int i = startIndex - 1; i >= 0; i--)
            {
                if (AufgabenView.GetItemAt(i) is MeinBildchen mb &&
                    mb.BildFürLinks == false)
                {
                    AufgabenView.MoveCurrentToPosition(i);
                    return;
                }
            }

            // Falls links nichts mehr existiert → erstes Bild anzeigen
            if (AufgabenView.Count > 0)
            {
                AufgabenView.MoveCurrentToFirst();
            }


        }


        /// <summary>
        /// Zweite Sicherung gegen den Stillstand bei Position -1: Gibt es überhaupt Bilder
        /// und steht die Ansicht auf keinem, führt der Pfeil nach rechts zum ersten.
        ///
        /// <c>CurrentPosition &lt; 0</c> muss deshalb erlaubt bleiben. Verlangt diese
        /// Bedingung <c>&gt;= 0</c>, ist -1 eine Sackgasse — der Pfeil nach links verlangt
        /// <c>&gt; 0</c>, also sperren dann beide Richtungen.
        /// </summary>
        private bool CanExecuteBildNachRechtsCommand()
        {
            return !PrüfungLäuft
                 && AufgabenView != null
                 && AufgabenView.Count > 0
                 && (AufgabenView.CurrentPosition < 0
                     || AufgabenView.CurrentPosition < AufgabenView.Count - 1);
        }



        [RelayCommand(CanExecute = nameof(CanExecuteBildNachRechtsCommand))]
        private void CommandExecuteBildNachRechts()
        {
            // Copilot Code


            // Existenzprüfung entfernt — Begründung siehe CommandExecuteBildLinks.

            if (AufgabenView.Count == 0)
            {
                return;
            }

            // Kein aktuelles Element: zum ersten Bild, nicht bloss return. Ein return
            // macht die Position -1 endgültig — sie löst sich weder durch Blättern noch
            // durch Warten auf.
            if (AufgabenView.CurrentPosition < 0)
            {
                AufgabenView.MoveCurrentToFirst();
                return;
            }


            // Ausgangsposition festhalten (eine einzige Index-Wahrheit)
            int startIndex = AufgabenView.CurrentPosition;

            // Der Vorauslauf soll dorthin arbeiten, wo man gerade hingeht.
            _blätterRichtung = 1;

            // Nächstes Bild suchen, das noch nicht "für Links" markiert ist
            for (int i = startIndex + 1; i < AufgabenView.Count; i++)
            {
                if (AufgabenView.GetItemAt(i) is MeinBildchen mb &&
                    mb.BildFürLinks == false)
                {
                    AufgabenView.MoveCurrentToPosition(i);
                    return;
                }
            }

            // Falls rechts kein passendes Bild mehr existiert → letztes anzeigen
            if (AufgabenView.Count > 0)
            {
                AufgabenView.MoveCurrentToLast();
            }
        }


        /// <summary>
        /// Wie oft hintereinander schon zum nächsten Bild weitergesprungen wurde, weil die
        /// Datei fehlte. Zurückgesetzt, sobald ein Bild wieder lädt.
        /// </summary>
        private int _verschwundenHintereinander;

        /// <summary>Ab hier nicht mehr weiterspringen — der Hintergrundlauf räumt ohnehin auf.</summary>
        private const int MaxVerschwundenHintereinander = 3;

        /// <summary>
        /// Nimmt das eine Bild aus der Liste, dessen Datei beim Laden nicht mehr da war,
        /// und geht zum nächsten. Stösst zugleich den Hintergrundlauf an, der den Rest der
        /// Liste nachsieht.
        ///
        /// Hier fällt eine verschwundene Datei ohne zusätzlichen Plattenzugriff auf: Der
        /// Ladeweg musste sie ohnehin öffnen. Vorher fragte stattdessen jeder
        /// Blätterschritt vorsorglich nach — auch wenn alles da war.
        ///
        /// Die Sprungbremse ist für den Fall, dass jemand den ganzen Ordner gelöscht hat:
        /// Ohne sie ginge das Weiterspringen durch die komplette Liste, jedes Mal mit einem
        /// neuen Ladeversuch. Nach ein paar Fehlgriffen wird nur noch entfernt und der
        /// Aufräumlauf abgewartet.
        /// </summary>
        private void EntferneVerschwundenesBild(string pfad)
        {
            var bildchen = OcAufgabens.FirstOrDefault(
                b => string.Equals(b.BName, pfad, StringComparison.OrdinalIgnoreCase));

            bool weiterspringen = _verschwundenHintereinander < MaxVerschwundenHintereinander;
            _verschwundenHintereinander++;

            if (bildchen is not null)
            {
                // Einzeln entfernen, nicht Liste leeren und neu füllen: Die Ansicht rückt
                // dabei von selbst auf den Nachbarn und behält ihre Position.
                OcAufgabens.Remove(bildchen);
            }
            else if (weiterspringen)
            {
                AufgabenView?.MoveCurrentToNext();
            }

            // Am Ende der Liste bleibt die Ansicht sonst hinter dem letzten Eintrag stehen.
            if (AufgabenView is { IsCurrentAfterLast: true } view && OcAufgabens.Count > 0)
            {
                view.MoveCurrentToLast();
            }

            if (weiterspringen)
            {
                _ = PruefeListeAufVerschwundeneDateienAsync();
                LadeAktuellesBildNach();
                return;
            }

            // Sprungbremse erreicht: nicht länger Bild für Bild weitertasten, sondern die
            // Liste in einem Zug durchsehen — und erst danach nachladen.
            //
            // Das Nachladen fehlte hier. Der Eintrag war entfernt, die Ansicht auf den
            // Nachbarn gerückt, das grosse Bild aber blieb beim vorigen stehen. Sichtbar
            // wurde das, sobald im Explorer mehrere verstreute Bilder gelöscht wurden: Ab
            // dem vierten Fehlgriff zeigten Miniaturleiste und grosses Bild zwei
            // verschiedene Dateien.
            _ = SaeubereListeUndLadeNachAsync();
        }

        /// <summary>
        /// Erst die ganze Liste säubern, dann einmal nachladen. Nach dem Durchlauf steht
        /// die Ansicht auf einem Eintrag, den es wirklich gibt — der Ladeversuch trifft
        /// also, statt die nächste Runde des Weitertastens auszulösen.
        /// </summary>
        private async Task SaeubereListeUndLadeNachAsync()
        {
            await PruefeListeAufVerschwundeneDateienAsync(sofort: true);
            LadeAktuellesBildNach();
        }

        /// <summary>
        /// Stösst das Laden des gerade gewählten Bildes von Hand an.
        ///
        /// Nötig, weil der Auswahlwechsel beim Entfernen eines Eintrags auf ein CanExecute
        /// trifft, das gerade false ist: Der Ladebefehl lässt keinen zweiten Lauf zu,
        /// solange der erste arbeitet, und die Meldung geht ins Leere. Sichtbar war das als
        /// „Eintrag verschwindet, Anzeige bleibt beim alten Bild stehen".
        ///
        /// Deshalb über den Dispatcher mit niedriger Priorität: Bis dahin ist der laufende
        /// Befehl beendet und lässt den nächsten zu.
        /// </summary>
        private void LadeAktuellesBildNach()
        {
            Application.Current?.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (CommandExecuteKleinesBildGrossesBildLadenCommand.CanExecute(null))
                    {
                        CommandExecuteKleinesBildGrossesBildLadenCommand.Execute(null);
                    }
                }),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private bool _pruefeVerschwundeneLaeuft;
        private DateTime _letztePruefungVerschwundene = DateTime.MinValue;

        /// <summary>
        /// Wartezeit zwischen zwei Aufräumläufen. Ohne sie liefe bei einem geleerten Ordner
        /// mit jedem Fehlgriff ein neuer Durchlauf über die ganze Liste.
        /// </summary>
        private static readonly TimeSpan PauseZwischenAufraeumlaeufen = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Sieht die ganze Liste durch und entfernt Einträge, deren Datei es nicht mehr
        /// gibt — etwa weil sie im Explorer gelöscht wurde, während die Anwendung lief.
        ///
        /// <b>Was sich gegenüber der früheren Fassung ändert:</b> Geprüft wird im
        /// Hintergrund statt im UI-Faden, und entfernt wird einzeln statt über
        /// <c>Clear()</c> mit anschliessendem Neubefüllen. Damit entfällt zugleich die
        /// Positionsreparatur, die dort nötig war: <c>Clear()</c> setzte die Ansicht auf
        /// −1, und bei −1 sperrten beide Blätterbefehle — wer eine Datei im Explorer
        /// löschte und dann blätterte, stand fest.
        /// </summary>
        /// <param name="sofort">
        /// Übergeht die Wartezeit zwischen zwei Läufen. Gesetzt, wenn das Weitertasten
        /// aufgegeben hat: Dann ist dieser Durchlauf die einzige Stelle, die den Zustand
        /// noch klärt, und zehn Sekunden zu warten hiesse zehn Sekunden lang ein Bild zu
        /// zeigen, das nicht zur Auswahl gehört.
        /// </param>
        private async Task PruefeListeAufVerschwundeneDateienAsync(bool sofort = false)
        {
            if (_pruefeVerschwundeneLaeuft
                || (!sofort && DateTime.UtcNow - _letztePruefungVerschwundene < PauseZwischenAufraeumlaeufen))
            {
                return;
            }

            _pruefeVerschwundeneLaeuft = true;

            try
            {
                // Momentaufnahme der Pfade: Die Liste darf sich während der Prüfung ändern,
                // und die Sammlung selbst gehört dem UI-Faden.
                var pfade = OcAufgabens.Select(b => b.BName).ToList();

                var fehlende = await Task.Run(() =>
                {
                    var weg = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var p in pfade)
                    {
                        if (!string.IsNullOrEmpty(p) && !File.Exists(p))
                        {
                            weg.Add(p);
                        }
                    }

                    return weg;
                });

                if (fehlende.Count == 0)
                {
                    return;
                }

                // Womit die Ansicht in den Durchlauf geht. Danach der Vergleich: Nur wenn
                // sich das aktuelle Element geändert hat, muss das grosse Bild nachziehen.
                object? vorherAktuell = AufgabenView?.CurrentItem;

                foreach (var bildchen in OcAufgabens.Where(b => fehlende.Contains(b.BName)).ToList())
                {
                    OcAufgabens.Remove(bildchen);
                }

                // Steht die Ansicht danach hinter dem Ende, zurück auf das letzte Bild.
                if (AufgabenView is { IsCurrentAfterLast: true } view && OcAufgabens.Count > 0)
                {
                    view.MoveCurrentToLast();
                }

                // War das angezeigte Bild unter den entfernten, steht die Auswahl jetzt auf
                // einem anderen Eintrag — das grosse Bild aber noch beim alten. Von selbst
                // holt es das nicht nach: Der Auswahlwechsel kommt aus dem Entfernen, nicht
                // aus einem Klick, und trifft je nach Zeitpunkt auf einen belegten
                // Ladebefehl.
                if (!ReferenceEquals(vorherAktuell, AufgabenView?.CurrentItem))
                {
                    LadeAktuellesBildNach();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            finally
            {
                _letztePruefungVerschwundene = DateTime.UtcNow;
                _pruefeVerschwundeneLaeuft = false;
            }
        }





        private bool PersonViewSource_Filter(object obj)
        {
            //throw new NotImplementedException();

            InnerZählerCount++;

            var aufgabe = obj as MeinBildchen;
            if (aufgabe == null)
            {
                return false;
            }
            else
            {
                if (string.IsNullOrEmpty(FilterText))
                {
                    return true;
                    //if (AufgabenView.CurrentPosition == -1)
                    //{
                    //    return true;
                    //}

                }
                else
                {
                    // Nur der Dateiname mit Endung, nicht der ganze Pfad.
                    //
                    // Mit BName wurde der volle Pfad durchsucht, und damit traf jeder
                    // Ordnername mit: In C:\Bilder\Künstler\kein_Fav\ lieferte die Eingabe
                    // „kein" sämtliche Bilder des Ordners, obwohl kein einziger Dateiname
                    // das Wort enthält. Dasselbe galt für den Laufwerksbuchstaben und den
                    // Künstlernamen — nach dem zu filtern ergab nie eine Auswahl, sondern
                    // liess immer alles stehen.
                    string dateiname = Path.GetFileName(aufgabe.BName) ?? string.Empty;
                    return dateiname.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
        }

        private ObservableCollection<MeinBildchen> ocAufgabens { get; set; }







        /// <summary>
        /// Drop durch den Nutzer (IFileDragDropTarget). Wechselt dabei der Ordner, werden
        /// die Suchtreffer des alten Ordners verworfen — sonst führt ein Klick darauf
        /// zurück in den vorherigen Ordner.
        /// </summary>
        public Task OnFileDrop(string[] filepaths)
            => OnFileDrop(filepaths, verwerfeSuchtreffer: true);

        /// <summary>
        /// Erklärt, warum eine abgelegte Datei nicht angezeigt wird — für BRD_MeldungKarte.
        ///
        /// Die Liste der Endungen kommt von der Prüfung selbst und wird nicht zweitgeschrieben:
        /// Kommt dort ein Format dazu, steht es sofort in der Meldung.
        ///
        /// Der Ordner-Fall ist eigens genannt, weil er der wahrscheinlichste Fehlgriff ist.
        /// Ein Ordner hat keine Endung, und „Datei ohne Endung kann nicht angezeigt werden"
        /// hätte niemandem geholfen.
        /// </summary>
        private static string MeldungNichtAnzeigbar(string pfad, string[] endungen)
        {
            string erlaubt = string.Join(", ", endungen.Select(e => e.TrimStart('.').ToUpperInvariant()));

            if (Directory.Exists(pfad))
            {
                return $"Das ist ein Ordner – ziehe eine Bilddatei hierher ({erlaubt}).";
            }

            string endung = Path.GetExtension(pfad).TrimStart('.');
            string was = string.IsNullOrEmpty(endung)
                ? "Eine Datei ohne Endung"
                : endung.ToUpperInvariant() + "-Dateien";

            return $"{was} kann diese Anwendung nicht anzeigen. Möglich sind {erlaubt}.";
        }

        /// <param name="verwerfeSuchtreffer">
        /// False für interne Aufrufe: Beim Öffnen eines Suchtreffers aus einem anderen
        /// Ordner und beim Neu-Einlesen muss die Trefferliste stehen bleiben.
        /// </param>
        private async Task OnFileDrop(string[] filepaths, bool verwerfeSuchtreffer)
        {
            // 1570

            //throw new NotImplementedException();

            if (filepaths == null)
            {
                return;
            }

            InnerZählerCount++;

            // Mehrfachauswahl: Bisher fiel der Aufruf hier stumm hindurch — der Zweig
            // darunter greift nur bei genau einer Datei. Gezogen wird aber gern mit
            // Strg mehreres auf einmal, und dann tat die Anwendung schlicht nichts.
            //
            // Eine Datei genügt: Eingelesen wird ohnehin ihr ganzer Ordner. Deshalb die
            // Bitte statt einer stillen Auswahl der ersten Datei — die wäre geraten, und
            // bei Dateien aus zwei Ordnern läge die Wahl daneben.
            if (filepaths.Length > 1)
            {
                LabelDropContent =
                    $"{filepaths.Length} Dateien – bitte nur ein Bild ziehen. Der ganze Ordner wird geladen.";
                return;
            }

            if (filepaths.Length == 1)
            {
                // Unterstützte Bildformate
                var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

                var fullDateiName = filepaths[0];

                if (string.IsNullOrEmpty(fullDateiName))
                {
                    return;
                }

                // Nachschauen ob es eine pdf ist
                if (!extensions.Contains(Path.GetExtension(fullDateiName).ToLower()))
                {
                    LabelDropContent = MeldungNichtAnzeigbar(fullDateiName, extensions);
                    //KnalNenFehlerSoundRein();
                    return;
                }

                // !fullDateiName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)

                // Vor dem Überschreiben von DropDateiName prüfen, ob der Ordner wechselt.
                // Nur dann sind die alten Suchtreffer hinfällig; beim erneuten Drop aus
                // demselben Ordner bleiben sie brauchbar.
                if (verwerfeSuchtreffer)
                {
                    string? alterOrdner = string.IsNullOrEmpty(DropDateiName)
                        ? null : Path.GetDirectoryName(DropDateiName);
                    string? neuerOrdner = Path.GetDirectoryName(fullDateiName);

                    if (!string.Equals(alterOrdner, neuerOrdner, StringComparison.OrdinalIgnoreCase))
                    {
                        VerwerfeSuchtreffer();
                    }
                }

                // Leeren statt den Dateinamen zu setzen: Der steht in der Statuszeile
                // unter dem Bild. Nicht einfach weglassen — sonst bliebe hier die Meldung
                // des vorigen Vorgangs stehen, etwa eine alte Restzeit oder „nix .jpg".
                LabelDropContent = string.Empty;
                DropDateiName = fullDateiName;

                // Das abgelegte Bild sofort zeigen, bevor der Ordner durchlaufen wird.
                //
                // Sonst steht die Bildfläche vom Leeren der Liste bis zum Ende des
                // Einlesens leer da. Das ist kein Platzhalter: Es ist genau das Bild, das
                // gleich ausgewählt wird – nur in der 100-Pixel-Stufe, die die Ladestrecke
                // ohnehin als erstes erzeugt. Kurz darauf ersetzt sie es durch das grosse.
                //
                // Fehler hier bewusst still: Ist die Datei unlesbar, meldet das die
                // reguläre Ladestrecke gleich danach.
                try
                {
                    DisplayImage = await Task.Run(() => MieneServices.CreateBitmap(fullDateiName, 100));
                }
                catch
                {
                    // Vorschau ist nur Beiwerk – das Einlesen läuft trotzdem weiter.
                }

                ocAufgabens.Clear();
                OnPropertyChanged(nameof(CountBildchenFürLinks));

                // Über die interface Files einlesen
                var cl = new Files.CLdateienEnlesen();

                // Erst im Hintergrund sammeln, dann wie gehabt einfügen.
                //
                // Directory.EnumerateFiles liefert träge – die Platte wird erst beim
                // Durchlaufen gelesen. Ohne Task.Run lag diese Arbeit im UI-Faden, und bei
                // tausenden Dateien auf einer langsamen Platte stand die Oberfläche
                // sekundenlang. Ein Task.Yield hilft dagegen nicht: Es verlagert nichts,
                // sondern stellt die Fortsetzung nur zurück in die Dispatcher-Warteschlange.
                //
                // Bewusst nur dieser eine Eingriff: Die Schleife darunter bleibt Zeile für
                // Zeile die alte. Sie fügt weiter einzeln in die laufende Ansicht ein, und
                // damit bleibt auch erhalten, dass beim ersten Bild das aktuelle Element
                // gesetzt wird – daran hängt die Anzeige.
                string ordner = Path.GetDirectoryName(fullDateiName)!;

                // Den Ordner erst wiederfinden, bevor er gelesen wird.
                //
                // Zwischen dem Einlesen und dem Neu-Einlesen kann er verschwunden sein:
                // im Explorer gelöscht, Wechseldatenträger abgezogen, Netzpfad getrennt.
                // Directory.EnumerateFiles wirft dann DirectoryNotFoundException. Die kam
                // aus einem AsyncRelayCommand, den niemand abfing, und riss die ganze
                // Anwendung mit — nachgestellt mit: Bilder nach kein_Fav verschieben, den
                // Ordner im Explorer löschen, BTN_BilderAktualisieren drücken.
                //
                // Die Liste ist oben schon geleert; das bleibt so, denn es gibt wirklich
                // nichts mehr zu zeigen. Nur die Bildfläche muss mit — dort stünde sonst
                // weiter das Bild aus dem gelöschten Ordner.
                if (string.IsNullOrEmpty(ordner) || !Directory.Exists(ordner))
                {
                    LabelDropContent = $"Diesen Ordner gibt es nicht mehr: {ordner}";

                    AlterDropCount = 0;
                    AufgabenView.Refresh();
                    LeereBildAnzeige();

                    // Dieselben Nachläufer wie am Ende des Normalfalls, damit nichts vom
                    // vorigen Ordner stehen bleibt: Index-Zustand, Wasserzeichen-Befunde
                    // und Zeitleiste beziehen sich sonst weiter auf den verschwundenen.
                    PruefeAktuellerOrdnerIndiziert();
                    LadeWasserzeichenBefunde(null);
                    AktualisiereZeitleiste();
                    return;
                }

                var dateies = await Task.Run(async () =>
                {
                    var liste = new System.Collections.Generic.List<string>();

                    await foreach (var datei in cl.DateienEinlesenAsync(ordner, false))
                    {
                        liste.Add(datei);
                    }

                    return liste;
                });

                //int index = 0;
                foreach (var datei in dateies)
                {
                    await Task.Yield();
                    //Debug.WriteLine(datei);
                    if (extensions.Contains(System.IO.Path.GetExtension(datei).ToLower()))
                    {
                        ocAufgabens.Add(new MeinBildchen { BName = datei, BildFürLinks = false });
                        //if (datei == fullDateiName)
                        //{
                        //    _AufgabenViewIndex = index = ocAufgabens.Count - 1;
                        //}

                    }
                }

                AlterDropCount = ocAufgabens.Count;

                AufgabenView.Refresh();



                // Rabat Code
                // NEWYEAR2026
                // ITDEAL15

                //string ordner = Path.GetDirectoryName(fullDateiName);


                //var images = Directory
                //     .EnumerateFiles(ordner)
                //     .Where(file => extensions.Contains(Path.GetExtension(file).ToLower()));


                //var jpgs = Directory.EnumerateFiles(ordner, "*.jpg");

                //ocAufgabens.Clear();


                // Aufgaben view zu curent machen
                // OrdinalIgnoreCase wie in allen anderen Pfadvergleichen (siehe
                // CommandExecuteTrefferOeffnen). Links steht der Name so, wie ihn das
                // Dateisystem beim Einlesen geliefert hat, rechts so, wie ihn die
                // Drop-Quelle schreibt — der Explorer trifft die echte Schreibweise,
                // andere Quellen müssen das nicht. Ein reiner == liesse das abgelegte
                // Bild dann unausgewählt, obwohl es in der Liste steht.
                //
                // Ohne Risiko: Windows lässt im selben Ordner keine zwei Dateien zu, die
                // sich nur in der Gross-/Kleinschreibung unterscheiden.
                var bildchen = OcAufgabens.FirstOrDefault(
                    b => string.Equals(b.BName, fullDateiName, StringComparison.OrdinalIgnoreCase));
                if (bildchen != null)
                {
                    var ivm = AufgabenView.IndexOf(bildchen);
                    if (ivm != -1)
                    {
                        AufgabenView.MoveCurrentToPosition(ivm);
                    }
                }

                AufgabenView.Refresh();
                AlleBilderVerschoben = false;

                // Dieser Weg läuft an AktualisiereAufgabenView vorbei: Steht beim
                // Neu-Einlesen ein Filter, kann er im neuen Ordner treffen oder auch
                // nicht — ohne diese Meldung behielte das Feld die Farbe des alten.
                OnPropertyChanged(nameof(FilterOhneTreffer));

                // Index-Status des (neuen) Ordners bestimmen → steuert „Schema-ähnlich".
                PruefeAktuellerOrdnerIndiziert();

                // Bereits gespeicherte Wasserzeichen-Befunde übernehmen (Badges).
                LadeWasserzeichenBefunde(Path.GetDirectoryName(fullDateiName));

                // Übersichtsleiste (Bilder je Zeitraum) im Hintergrund neu aufbauen.
                AktualisiereZeitleiste();

                // Zuletzt: Sagen, wenn ein stehender Filter das abgelegte Bild versteckt.
                // Erst hier steht fest, was die Ansicht nach dem Einlesen zeigt.
                MeldeVersteckendenFilter(fullDateiName);
            }









        }


        /// <summary>
        /// Meldet nach einem Drop, wenn ein stehender Filter das abgelegte Bild versteckt.
        ///
        /// Wer eine Datei hierher zieht, will genau sie sehen. Steht aber noch ein Filter
        /// aus einer früheren Suche, entscheidet dessen Text mit: Passt er nicht auf den
        /// Dateinamen, fehlt das Bild in Liste und Miniaturleiste, obwohl der Ordner
        /// eingelesen wurde — MoveCurrentToPosition weiter oben findet es in der
        /// gefilterten Ansicht nicht. Zu sehen war davon nichts; es blieb kommentarlos
        /// die 100-Pixel-Vorschau aus OnFileDrop stehen, ein Bild also, das in keiner
        /// Liste steht.
        ///
        /// Der Filter bleibt bewusst stehen. Ihn im Vorbeigehen zu leeren würde eine von
        /// Hand gesetzte Auswahl verwerfen — gesagt wird stattdessen, was los ist.
        /// </summary>
        private void MeldeVersteckendenFilter(string fullDateiName)
        {
            if (string.IsNullOrEmpty(FilterText))
            {
                return;
            }

            string dateiname = Path.GetFileName(fullDateiName);

            // Dieselbe Prüfung wie in PersonViewSource_Filter. Läuft sie hier anders,
            // meldet die Karte etwas anderes, als die Liste zeigt.
            if (dateiname.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }

            if (AufgabenView.IsEmpty)
            {
                // Kein einziger Treffer: Liste und Miniaturleiste sind leer, dann darf
                // auch die Bildfläche nichts mehr zeigen.
                LeereBildAnzeige();
                LabelDropContent = $"Filter „{FilterText}“ passt hier auf keine Datei – die Liste ist leer.";
                return;
            }

            // Es gibt Treffer, nur nicht den gewünschten. Ohne gültige Auswahl bliebe die
            // Vorschau des abgelegten Bildes stehen — dann lieber den ersten Treffer
            // zeigen, denn den enthält die Liste wirklich.
            if (AufgabenView.CurrentItem is null)
            {
                AufgabenView.MoveCurrentToFirst();
            }

            LabelDropContent = $"Filter „{FilterText}“ blendet „{dateiname}“ aus – die Liste zeigt {AufgabenView.Count} andere Treffer.";
        }

        public ListCollectionView AufgabenView { get; }

        /// <summary>
        /// Setzt alles zurück, was das gerade angezeigte Bild beschreibt.
        ///
        /// Nötig, weil diese Werte allein von CommandExecuteKleinesBildGrossesBildLaden
        /// gefüllt werden, und das läuft nur bei einem Auswahlwechsel. Bleibt gar nichts
        /// mehr zum Auswählen übrig — ein Filter blendet alles aus —, gibt es keinen
        /// Wechsel mehr, und die Werte des letzten Bildes bleiben stehen.
        ///
        /// -1 ist bei den Abmessungen der Startwert und heisst hier wie dort „unbekannt".
        /// </summary>
        private void LeereBildAnzeige()
        {
            DisplayImage = null;
            OriginalImageWidth = -1;
            OriginalImageHeight = -1;
            BildFarbsignatur = null;
        }

        /// <summary>
        /// Anzahl der Bildchen mit BildFürLinks == true.
        /// </summary>
        public int CountBildchenFürLinks => ocAufgabens.Count(x => x.BildFürLinks);

        // FilterText
        public string FilterText
        {
            get
            {
                return _filterText;
            }
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    // Genau eine Aktualisierung je Tastendruck.
                    //
                    // Hier wurde zusätzlich der Filter an- und abgehängt, je nachdem ob
                    // Text da war. Das hatte zwei Folgen. Erstens löst schon das Zuweisen
                    // von Filter eine Aktualisierung aus — es liefen also zwei kurz
                    // hintereinander. Zweitens hängt += den Delegaten an, statt ihn zu
                    // ersetzen: Mit jedem getippten Zeichen kam eine weitere Kopie der
                    // Prüfung dazu, und -= nahm nur eine davon wieder weg.
                    //
                    // Der Filter hängt seit dem Konstruktor an der Ansicht und bleibt dort.
                    // Bei leerem Text lässt PersonViewSource_Filter ohnehin alles durch.
                    AktualisiereAufgabenView();
                    CommandExecuteFilterLeerenCommandCommand?.NotifyCanExecuteChanged();

                    // Die Bildfläche nachziehen.
                    //
                    // Sie hängt allein an CommandExecuteKleinesBildGrossesBildLaden, und
                    // das läuft nur bei einem Auswahlwechsel in der Liste. Filtert man
                    // alles weg, gibt es keinen Wechsel mehr — CanExecute verlangt eine
                    // vorhandene Datei und schaltet ab. Das zuletzt geladene Bild blieb
                    // deshalb stehen, während Liste und Leiste leer waren.
                    // Auch hier melden, nicht nur im Setter von SelectedBildchen: Filtert
                    // man von leer auf leer weiter — „fff" zu „ffff" —, wechselt die
                    // Auswahl nicht, die Ansicht bleibt aber leer.
                    CommandExecuteBildListeToggleCommand?.NotifyCanExecuteChanged();

                    // Den Cache der Schnell-Liste verwerfen.
                    //
                    // Er wird sonst nur von ocAufgabens.CollectionChanged verworfen, und
                    // ein Filterwechsel rührt die Quellliste gar nicht an. Befüllt wird
                    // die Liste aber aus AufgabenView, also gefiltert — ohne diese Zeile
                    // zeigte ein offenes Kachelpanel nach dem Leeren des Filters weiter
                    // die alte, engere Auswahl.
                    _bildListeVeraltet = true;

                    if (IsBildListeOffen)
                    {
                        // Ohne await: Der Setter ist nicht async, und FuelleBildListeAsync
                        // bricht einen noch laufenden Aufbau selbst ab und fängt seine
                        // Abbruch-Ausnahme. Beim Tippen im Filter lösen die Zwischenstände
                        // damit reihum ab, statt sich zu stapeln.
                        _ = FuelleBildListeAsync();
                    }

                    if (AufgabenView.IsEmpty)
                    {
                        LeereBildAnzeige();
                    }
                    else if (AufgabenView.CurrentItem is null)
                    {
                        // Umgekehrter Fall: Der Filter gibt wieder Bilder frei, aber das
                        // vorherige ist nicht mehr dabei. MoveCurrentToFirst löst den
                        // Auswahlwechsel aus, an dem das Laden hängt.
                        AufgabenView.MoveCurrentToFirst();
                    }
                }
            }
        }

        /// <summary>
        /// Läuft der Filter gerade ins Leere? Färbt TXB_DateinameFiltern rot.
        ///
        /// Nur wahr, solange wirklich gefiltert wird: Eine leere Ansicht ohne Filtertext
        /// ist kein Fehlschlag, sondern ein leerer Ordner — dort wäre Rot eine Falschmeldung.
        ///
        /// Gemeldet wird in <see cref="AktualisiereAufgabenView"/>, also überall dort, wo
        /// sich die gefilterte Auswahl ändert.
        /// </summary>
        public bool FilterOhneTreffer => !string.IsNullOrEmpty(FilterText) && AufgabenView is { IsEmpty: true };

        /// <summary>
        /// Baut die Bilderansicht neu auf und meldet <c>SelectedBildchen</c> erst danach.
        ///
        /// An derselben Ansicht hängen drei Listen — Dateiliste, Miniaturleiste,
        /// Vollbildleiste —, alle mit <c>IsSynchronizedWithCurrentItem</c> und einer
        /// Zweiwege-Bindung von <c>SelectedItem</c> auf <c>SelectedBildchen</c>.
        ///
        /// Beim Aufbau bekommt jede von ihnen der Reihe nach ein Reset gemeldet. Die
        /// erste sucht sich sofort eine neue Auswahl und schreibt sie über ihre Bindung
        /// ins ViewModel zurück — mitten in den laufenden Aufbau hinein. Wird von dort
        /// aus <c>SelectedBildchen</c> gemeldet, greift die zweite Liste danach, hat ihr
        /// eigenes Reset aber noch vor sich: Sie sucht die Position ihres alten Standes
        /// (etwa 16) in einer Ansicht, die schon auf ein Bild geschrumpft ist, und
        /// <c>Selector.SetCurrentToSelected</c> wirft eine ArgumentOutOfRangeException.
        ///
        /// Deshalb bleibt die Meldung während des Aufbaus aus und kommt einmal hinterher,
        /// wenn alle drei Listen umgestellt sind. Wer nur die Ansicht selbst aktualisiert
        /// (die übrigen <c>AufgabenView.Refresh()</c> im ViewModel), ist davon unberührt —
        /// dort kommt der Anstoss nicht aus einer Bindung, sondern aus dem Code.
        /// </summary>
        private void AktualisiereAufgabenView()
        {
            _ansichtWirdAufgebaut = true;
            try
            {
                AufgabenView.Refresh();
            }
            finally
            {
                _ansichtWirdAufgebaut = false;
            }

            OnPropertyChanged(nameof(SelectedBildchen));

            // Der Rot-Zustand des Filterfelds hängt daran, ob die Ansicht nach dem
            // Refresh leer ist — das steht erst hier fest.
            OnPropertyChanged(nameof(FilterOhneTreffer));
        }



        public ObservableCollection<MeinBildchen> OcAufgabens
        {
            get => ocAufgabens;
            //get;set;
        }


        //     public ObservableCollection<MeinBildchen> OcLinkeBilder { get; set; }




        /// <summary>
        /// A person to edit.
        /// </summary>
        public MeinBildchen? SelectedBildchen
        {
            get
            {
                // Fehler
                //if (_AufgabenViewIndex==-1)
                //{
                //    // Filter löschen
                //    AufgabenView.Filter -= PersonViewSource_Filter;
                //}
                //else
                //{
                //    // Filter setzen
                //    AufgabenView.Filter += PersonViewSource_Filter;
                //}

                //return AufgabenView.CurrentItem as MeinBildchen;
                return AufgabenView.CurrentItem as MeinBildchen;

            }
            set
            {

                // Fehler

                //if (_AufgabenViewIndex == -1)
                //{
                //    // Filter löschen
                //    AufgabenView.Filter -= PersonViewSource_Filter;
                //}
                //else
                //{
                //    // Filter setzen
                //    AufgabenView.Filter += PersonViewSource_Filter;
                //}


                // Nur bewegen, wenn sich wirklich etwas ändert.
                //
                // An derselben Ansicht hängen drei Listen (Dateiliste, Miniaturleiste,
                // Vollbildleiste), alle mit IsSynchronizedWithCurrentItem. Jede schreibt
                // ihre Auswahl über SelectedItem hierher zurück. Ohne den Vergleich
                // schickt jeder Klick eine Runde durch alle drei — und trifft dabei
                // womöglich eine, die gerade neu aufbaut.
                if (!ReferenceEquals(AufgabenView.CurrentItem, value))
                {
                    AufgabenView.MoveCurrentTo(value);
                }

                OnPropertyChanged(nameof(SelectedBildchen.BildFürLinks));

                // Während die Ansicht neu aufgebaut wird, nicht melden: Die Listen stehen
                // dann auf verschiedenen Ständen, und die Meldung schickte eine von ihnen
                // auf eine Position, die es nicht mehr gibt. AktualisiereAufgabenView holt
                // die Meldung nach, sobald alle umgestellt sind.
                if (!_ansichtWirdAufgebaut)
                {
                    OnPropertyChanged();
                }

                // Neuer Ordner? → prüfen, ob er indiziert ist (steuert „Schema-ähnlich").
                PruefeAktuellerOrdnerIndiziert();


                // Commands schauen
                CommandExecuteBildNachRechtsCommand?.NotifyCanExecuteChanged();
                CommandExecuteBildLinksCommand?.NotifyCanExecuteChanged();
                CommandExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand?.NotifyCanExecuteChanged();
                CommandExecuteBildInsKeinFavVerzeichnisVerschiebenCommand?.NotifyCanExecuteChanged();
                CommandExecuteOrdnerEineEbeneHochCommand?.NotifyCanExecuteChanged();
                CommandExecuteBildStretchAnpassenCommand?.NotifyCanExecuteChanged();
                CommandExecuteAlleBilderInsKeinFavVerschiebenCommand?.NotifyCanExecuteChanged();
                CommandExecuteSuchleisteToggleCommand?.NotifyCanExecuteChanged();
                CommandExecuteBildListeToggleCommand?.NotifyCanExecuteChanged();

            }
        }



        /// <summary>
        /// Prüft ob alle Bilder verschoben sind (kein BildFürLinks==false mehr).
        /// Setzt AlleBilderVerschoben auf true/false.
        /// </summary>
        private void UpdateAlleBilderVerschoben()
        {
            AlleBilderVerschoben = OcAufgabens.Count > 0
                && !OcAufgabens.Any(b => b.BildFürLinks == false);
        }

        #region Command Bild ins kein_Fav Verzeichnis verschieben

        private bool CanExecuteBildInsKeinFavVerzeichnisVerschiebenCommand()
        {
            if (SelectedBildchen == null)
            {
                return false;
            }
            else
            {
                // !IndexLaeuft: siehe die übrigen Verschiebe-Befehle – während des
                // Indexierens darf keine Datei wegwandern. Das Blättern bleibt frei.
                //
                // Kein File.Exists: ein Plattenzugriff im UI-Faden, bei jeder Auswertung.
                // Ausführlich steht das bei CanExecuteKleinesBildGrossesBildLaden. Hier
                // wog es schwerer als dort, weil die Verknüpfung & nicht kurzschliesst —
                // die Platte wurde also selbst dann gefragt, wenn schon PrüfungLäuft die
                // Antwort war. Ob die Datei noch da ist, prüft der Befehl selbst,
                // unmittelbar vor dem File.Move.
                return (OcAufgabens.Count > 0)
                     & (AufgabenView.CurrentPosition <= AufgabenView.Count
                    & (!SelectedBildchen.BName.Contains("kein_Fav")) & !PrüfungLäuft & !IndexLaeuft);
            }


        }

        [RelayCommand(CanExecute = nameof(CanExecuteBildInsKeinFavVerzeichnisVerschiebenCommand))]
        private async Task CommandExecuteBildInsKeinFavVerzeichnisVerschieben()
        {
            // Vor dem Setzen von PrüfungLäuft: Fehlt die Grundlage, ist nichts zu tun
            // und auch nichts zurückzunehmen. CanExecute deckt den Normalweg ab, aber
            // der Befehl ist auch aus dem Code aufrufbar, und GetDirectoryName gibt bei
            // einem Wurzelpfad wie „D:\" null zurück.
            string? source = SelectedBildchen?.BName;
            string? quellOrdner = Path.GetDirectoryName(source);

            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(quellOrdner))
            {
                return;
            }

            PrüfungLäuft = true;

            bool moveErfolgreich = false;

            string zielVerzeichnis = Path.Combine(quellOrdner, "kein_Fav");

            string zielDateiName = Path.Combine(
                zielVerzeichnis,
                Path.GetFileName(source));

            try
            {
                // Verzeichnis sicherstellen
                if (!Directory.Exists(zielVerzeichnis))
                {
                    Directory.CreateDirectory(zielVerzeichnis);
                }

                // Dateisystem === EINZIGER kritischer Teil
                if (!File.Exists(zielDateiName) && File.Exists(source))
                {
                    var länge = new FileInfo(source).Length;
                    if (länge > 0)
                    {
                        await Task.Run(() => File.Move(source, zielDateiName));
                        CLconverterStringZuKleinemImage.InvalidateCache(source);
                        moveErfolgreich = true;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Fehler beim Verschieben der Datei:\n\n" + ex.Message,
                    "Verschieben fehlgeschlagen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                PrüfungLäuft = false;
            }

            // ❗ Ab hier NUR weiter, wenn Move wirklich erfolgreich war
            if (!moveErfolgreich)
            {
                return;
            }

            // === MODEL / COLLECTION ===

            var bildchen = OcAufgabens
                .FirstOrDefault(b => b.BName == source);

            if (bildchen != null)
            {
                bildchen.BName = zielDateiName;
                bildchen.BildFürLinks = true;

                OnPropertyChanged(nameof(CountBildchenFürLinks));
            }

            BildchenVorher = zielDateiName;

            // === NAVIGATION ===
            MoveToNextNichtLinkesBild();

            AufgabenView.Refresh();
            UpdateAlleBilderVerschoben();

        }


        private void MoveToNextNichtLinkesBild()
        {
            var startIndex = AufgabenView.CurrentPosition;

            for (int i = startIndex + 1; i < AufgabenView.Count; i++)
            {
                if (AufgabenView.GetItemAt(i) is MeinBildchen mb &&
                    mb.BildFürLinks == false)
                {
                    AufgabenView.MoveCurrentToPosition(i);
                    return;
                }
            }

            // Falls nichts mehr gefunden
            AufgabenView.MoveCurrentToLast();
            CommandExecuteBildInsKeinFavVerzeichnisVerschiebenCommand
                ?.NotifyCanExecuteChanged();
        }


        #endregion

        #region Command Bild ins Haupt-Verzeichnis zurück verschieben

        /// <summary>
        /// Ordner eine Ebene über der Datei — das Ziel des Zurückverschiebens.
        /// <c>null</c>, wenn es keinen gibt (Datei direkt im Wurzelverzeichnis).
        /// </summary>
        private static string? ZielOrdnerEineEbeneHoeher(string? bildPfad)
        {
            if (string.IsNullOrEmpty(bildPfad))
            {
                return null;
            }

            string? ordner = Path.GetDirectoryName(bildPfad);
            if (string.IsNullOrEmpty(ordner))
            {
                return null;
            }

            string? darueber = Path.GetDirectoryName(ordner);
            return string.IsNullOrEmpty(darueber) ? null : darueber;
        }

        private bool CanExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand()
        {
            if (SelectedBildchen == null || string.IsNullOrEmpty(SelectedBildchen.BName))
            {
                return false;
            }

            // !IndexLaeuft aus demselben Grund wie beim Verschieben aller Bilder: Der
            // Index verweist auf Pfade, und was während des Laufs wegwandert, steht
            // hinterher falsch darin.
            //
            // Kein File.Exists: Plattenzugriff im UI-Faden bei jeder Auswertung, siehe
            // CanExecuteKleinesBildGrossesBildLaden. Der Befehl selbst prüft die Datei
            // und meldet sich, wenn sie fehlt — der Kommentar dort sagt es sogar schon:
            // Zwischen Prüfung und Druck kann sie ohnehin verschwinden.
            if (PrüfungLäuft || IndexLaeuft)
            {
                return false;
            }

            // Ohne Ordner darüber gibt es kein Ziel.
            if (ZielOrdnerEineEbeneHoeher(SelectedBildchen.BName) is null)
            {
                return false;
            }

            // Weg 1: In dieser Sitzung von hier weggelegt. Das war bisher die einzige
            // Bedingung.
            if (SelectedBildchen.BildFürLinks)
            {
                return true;
            }

            // Weg 2: Die Datei liegt in einer der Ablagen dieser Anwendung — kein_Fav,
            // KI_Fehler, Doppelt, Besonders, Wasserzeichen.
            //
            // Nötig, weil beim Laden eines Ordners jedes Bild BildFürLinks = false
            // bekommt. Öffnet man ein Bild aus einem kein_Fav-Ordner, war der Knopf
            // deshalb tot, obwohl gerade dort das Zurücklegen gebraucht wird.
            //
            // Die Prüfung ist bewusst am Ordner festgemacht und nicht einfach
            // weggelassen: Das Ziel ist der Elternordner der Datei, und das ist nur
            // richtig, solange sie in einer Ablage liegt. Bei einem gewöhnlichen
            // Künstlerbild würde ↑ es sonst aus dem Künstlerordner herausbefördern —
            // dass das bisher nicht passierte, lag allein an der Marke.
            string? eigenerOrdner = Path.GetDirectoryName(SelectedBildchen.BName);
            return !string.IsNullOrEmpty(eigenerOrdner) && IstAussortiert(eigenerOrdner);
        }
        [RelayCommand(CanExecute = nameof(CanExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand))]
        private async Task CommandExecuteBildInsHauptVerzeichnisZuruckVerschieben()
        {
            PrüfungLäuft = true;

            bool moveErfolgreich = false;

            var source = SelectedBildchen.BName;
            var dateiname = Path.GetFileName(source);
            var hauptVerzeichnis = ZielOrdnerEineEbeneHoeher(source);

            if (hauptVerzeichnis is null)
            {
                PrüfungLäuft = false;
                return;
            }

            var zielVollPfad = Path.Combine(hauptVerzeichnis, dateiname);

            try
            {
                if (File.Exists(zielVollPfad))
                {
                    // Vorher passierte hier gar nichts: keine Bewegung, keine Meldung,
                    // der Knopf wirkte kaputt. Umbenannt wird bewusst nicht — ein Bild
                    // mit angehängter Nummer findet man später nicht wieder.
                    MessageBox.Show(
                        "Im Ordner darüber liegt bereits eine Datei mit diesem Namen:\n\n"
                        + zielVollPfad + "\n\n"
                        + "Das Bild bleibt deshalb, wo es ist.",
                        "Nicht zurückverschoben",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else if (File.Exists(source))
                {
                    await Task.Run(() => File.Move(source, zielVollPfad));
                    CLconverterStringZuKleinemImage.InvalidateCache(source);
                    moveErfolgreich = true;
                }
                else
                {
                    // Aus demselben Grund wie der Zweig oben: Hier passierte bisher
                    // lautlos nichts. CanExecute prüft zwar auf Vorhandensein, aber
                    // zwischen der Prüfung und dem Druck kann die Datei verschwinden —
                    // umbenannt im Explorer, weggeräumt von einem anderen Programm,
                    // oder der Pfad in der Liste ist veraltet.
                    MessageBox.Show(
                        "Die Datei ist nicht mehr da, wo sie erwartet wurde:\n\n"
                        + source + "\n\n"
                        + "Vermutlich wurde sie ausserhalb der Anwendung verschoben "
                        + "oder umbenannt. Ein erneutes Einlesen des Ordners bringt "
                        + "die Liste wieder auf Stand.",
                        "Nicht zurückverschoben",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Fehler beim Zurückverschieben der Datei:\n\n" + ex.Message,
                    "Verschieben fehlgeschlagen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                PrüfungLäuft = false;
            }

            // ❗ nur bei echtem Erfolg
            if (!moveErfolgreich)
            {
                return;
            }

            // === MODEL / COLLECTION UPDATE ===
            //
            // Zwei Fälle, und sie sind gegensätzlich. Auseinandergehalten werden sie an
            // BildFürLinks, weil genau das die Herkunft des Bildes festhält:
            //
            //   true  – Das Bild kam aus DIESER Liste und wurde in dieser Sitzung in die
            //           Ablage gelegt. Zurückverschieben bringt es in den Ordner der
            //           Liste. Es gehört also weiter dazu: bleiben, Marke löschen.
            //
            //   false – Das Bild wurde mit dem Ordner geladen, und der Ordner IST die
            //           Ablage (ein kein_Fav-Ordner als Drop-Ziel). Zurückverschieben
            //           bringt es aus dem Listenordner heraus. Es gehört danach nicht
            //           mehr dazu: entfernen und weitergehen.

            var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == source);
            if (bildchen != null && bildchen.BildFürLinks)
            {
                var index = OcAufgabens.IndexOf(bildchen);
                var indexSelected = AufgabenView.CurrentPosition;

                bildchen.BName = zielVollPfad;
                bildchen.BildFürLinks = false;

                OnPropertyChanged(nameof(CountBildchenFürLinks));
                OcAufgabens.Move(index, indexSelected);

                BildchenVorher = string.Empty;

                AufgabenView.MoveCurrentToPosition(AufgabenView.CurrentPosition);
                AufgabenView.Refresh();
            }
            else if (bildchen != null)
            {
                // Stelle vorher merken: Nach dem Entfernen rückt genau das nächste Bild
                // auf diesen Platz. Das ist das „CurrentIndex + 1", nur ohne rechnen.
                int stelle = AufgabenView.CurrentPosition;

                OcAufgabens.Remove(bildchen);
                OnPropertyChanged(nameof(CountBildchenFürLinks));
                BildchenVorher = string.Empty;

                // Kein Refresh hier: Das Entfernen kommt über die ObservableCollection
                // ohnehin in der Ansicht an, und ein Refresh würde die Auswahl auf den
                // Anfang zurückwerfen — also genau das zunichte machen, was gleich folgt.
                //
                // War es das letzte Bild, bleibt nur das neue Ende. Ist die Liste leer,
                // gibt es nichts mehr auszuwählen.
                int neueStelle = Math.Min(stelle, AufgabenView.Count - 1);
                if (neueStelle >= 0)
                {
                    AufgabenView.MoveCurrentToPosition(neueStelle);
                }
            }

            UpdateAlleBilderVerschoben();

        }

        #endregion

        #region Command Filter löschen

        private bool CanExecuteFilterLeerenCommand()
        {
            return !string.IsNullOrEmpty(FilterText);
        }

        [RelayCommand(CanExecute = nameof(CanExecuteFilterLeerenCommand))]
        private void CommandExecuteFilterLeerenCommand()
        {
            FilterText = string.Empty;
        }

        #endregion

        #region Command VerschiebenZurück

        private bool CanExecuteVerschiebenZurück()
        {
            // !IndexLaeuft: Rückgängig verschiebt ebenfalls eine Datei.
            //
            // Kein File.Exists: Plattenzugriff im UI-Faden bei jeder Auswertung, siehe
            // CanExecuteKleinesBildGrossesBildLaden. Der Befehl prüft die Datei selbst,
            // bevor er sie bewegt.
            return !string.IsNullOrEmpty(BildchenVorher)
                & !IndexLaeuft;
        }
        [RelayCommand(CanExecute = nameof(CanExecuteVerschiebenZurück))]
        private void CommandExecuteVerschiebenZurück()
        {
            var vorherSelectedFullName = BildchenVorher;
            var vorherSelectedName = Path.GetFileName(vorherSelectedFullName);


            // Datei ins Haupt-Verzeichnis zurück verschieben
            var dateiname = Path.GetFileName(BildchenVorher);

            // Beispiel: C:\Beispiel\Bilder\Sammlung\kein_Fav\beispiel.jpg
            //
            // Zweimal GetDirectoryName führt von der Datei über die Ablage in den Ordner
            // darüber. Welche Ablage es war, spielt keine Rolle — dasselbe gilt für
            // KI_Fehler und Besonders, die BildchenVorher ebenfalls setzen.
            var elternVerzeichnis = Path.GetDirectoryName(Path.GetDirectoryName(BildchenVorher));
            string zielVollPfad = Path.Combine(elternVerzeichnis, dateiname);
            if (File.Exists(BildchenVorher) & !File.Exists(zielVollPfad))
            {
                File.Move(BildchenVorher, zielVollPfad);
                CLconverterStringZuKleinemImage.InvalidateCache(BildchenVorher);

                var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == BildchenVorher);
                //var indexSelected = AufgabenView.CurrentPosition;

                if (bildchen != null)
                {
                    bildchen.BName = zielVollPfad;
                    bildchen.BildFürLinks = false;
                    OnPropertyChanged(nameof(CountBildchenFürLinks));

                    //OcAufgabens.Move(index, indexSelected);
                }

                AufgabenView.Refresh();
            }

            BildchenVorher = string.Empty;

            UpdateAlleBilderVerschoben();

            // Curent Position wiederherstellen
            var wiederZuWählendesBildchen = OcAufgabens.FirstOrDefault(b => Path.GetFileName(b.BName) == vorherSelectedName);
            if (wiederZuWählendesBildchen != null)
            {
                SelectedBildchen = wiederZuWählendesBildchen;
            }

        }


        #endregion

        #region Command Ordner eine Ebene höher

        /// <summary>
        /// Bildendungen, die <see cref="OnFileDrop(string[])"/> annimmt.
        /// </summary>
        private static readonly string[] AnzeigbareEndungen =
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

        /// <summary>
        /// Das Bild aus <paramref name="ordner"/>, das in der Ansicht auf
        /// <paramref name="bezugsName"/> folgen würde — also die Stelle, an der das
        /// weggelegte Bild vorher stand.
        ///
        /// Das Bild in der Ablage stammt aus genau diesem Ordner, sein Name sortiert dort
        /// also weiterhin an seiner alten Stelle mit. Gesucht wird der erste Name, der
        /// dahinter liegt; gibt es keinen, war das Bild das letzte des Ordners, dann das
        /// letzte vorhandene. Ohne <paramref name="bezugsName"/> das erste.
        ///
        /// Sortiert mit demselben <see cref="NaturalStringComparer"/> und über denselben
        /// Schlüssel wie die Ansicht — Dateiname ohne Endung.
        ///
        /// Der gleiche Vergleicher ist hier keine Kosmetik: <see cref="OnFileDrop(string[])"/>
        /// wählt die übergebene Datei aus, und daran hängt der Ladebefehl für das grosse
        /// Bild. Eine anders sortierte Auswahl landet an der falschen Stelle, und ein
        /// Nachfassen mit MoveCurrentTo kommt zu spät: Der Ladebefehl läuft dann bereits
        /// und lehnt als AsyncRelayCommand den zweiten Anlauf ab — die Miniaturleiste
        /// spränge, die Bildfläche zeigte das andere Bild.
        ///
        /// <c>null</c>, wenn der Ordner kein Bild enthält oder nicht lesbar ist.
        /// </summary>
        private static string? NachfolgerInAnsichtsordnung(string ordner, string? bezugsName)
        {
            try
            {
                var vergleicher = new NaturalStringComparer();

                var kandidaten = Directory.EnumerateFiles(ordner)
                    .Where(d => AnzeigbareEndungen.Contains(Path.GetExtension(d).ToLowerInvariant()))
                    .OrderBy(d => Path.GetFileNameWithoutExtension(d), vergleicher)
                    .ToList();

                if (kandidaten.Count == 0)
                {
                    return null;
                }

                if (string.IsNullOrEmpty(bezugsName))
                {
                    return kandidaten[0];
                }

                // Streng grösser, nicht grösser-gleich: Trägt der Ordner zufällig einen
                // gleichnamigen Eintrag, ist das nicht das weggelegte Bild — der
                // Nachfolger ist dann trotzdem der richtige Landeplatz.
                int nachfolger = kandidaten.FindIndex(
                    d => vergleicher.Compare(Path.GetFileNameWithoutExtension(d), bezugsName) > 0);

                return nachfolger >= 0 ? kandidaten[nachfolger] : kandidaten[^1];
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Nur eingeschaltet, wenn das Bild in einer der Ablagen dieser Anwendung liegt —
        /// <c>kein_Fav</c>, <c>KI_Fehler</c>, <c>Doppelt</c>, <c>Besonders</c>,
        /// <c>Wasserzeichen</c>. Die Liste steht als <c>AussortiertOrdner</c> in
        /// AufgabeViewModel.IndexOrdner.cs.
        ///
        /// Ohne diese Bedingung führte der Knopf aus einem gewöhnlichen Künstlerordner
        /// heraus in dessen Elternordner — dieselbe Falle, gegen die auch
        /// <see cref="CanExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand"/> den
        /// Ordnernamen prüft.
        ///
        /// Bewusst ohne Zugriff auf die Platte: CanExecute wird bei jedem Bildwechsel
        /// ausgewertet, und ein Verzeichnislauf je Wechsel wäre auf Netzlaufwerken
        /// spürbar. Ob im Ordner darüber wirklich Bilder liegen, klärt erst der Klick.
        /// </summary>
        private bool CanExecuteOrdnerEineEbeneHoch()
        {
            if (PrüfungLäuft || IndexLaeuft || SelectedBildchen == null)
            {
                return false;
            }

            string? ordner = Path.GetDirectoryName(SelectedBildchen.BName);
            if (string.IsNullOrEmpty(ordner) || !IstAussortiert(ordner))
            {
                return false;
            }

            return !string.IsNullOrEmpty(Path.GetDirectoryName(ordner));
        }

        /// <summary>
        /// Verlässt die Ablage und zeigt den Ordner darüber an — das „.." des
        /// Dateimanagers, beschränkt auf die Ablagen dieser Anwendung.
        ///
        /// Gewählt wird dort das Bild, das dem weggelegten folgt, nicht das erste des
        /// Ordners: Man kommt an der Stelle heraus, an der man aufgehört hat.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteOrdnerEineEbeneHoch))]
        private async Task CommandExecuteOrdnerEineEbeneHoch()
        {
            string? ordner = Path.GetDirectoryName(SelectedBildchen?.BName);
            string? darueber = string.IsNullOrEmpty(ordner) ? null : Path.GetDirectoryName(ordner);
            if (string.IsNullOrEmpty(darueber))
            {
                return;
            }

            // An der Stelle landen, an der das weggelegte Bild vorher stand — nicht am
            // Anfang. In einem Ordner mit tausend Bildern ist der Weg zurück sonst weit.
            string? zielBild = NachfolgerInAnsichtsordnung(
                darueber,
                Path.GetFileNameWithoutExtension(SelectedBildchen?.BName));

            if (zielBild == null)
            {
                SucheStatus = $"Kein Bild in {Path.GetFileName(darueber)} – dort gibt es nichts anzuzeigen.";
                return;
            }

            // VOR dem Einlesen, sonst sortiert die Ansicht den neuen Ordner nicht so, wie
            // erstesBild es annimmt: Nach „Treffer übernehmen" steht CustomSort auf null,
            // damit die Rangfolge der Trefferliste stehen bleibt (siehe
            // CommandExecuteTrefferInListeUebernehmen). Hier wird ein ganzer Ordner frisch
            // geladen — die Rangfolge ist hinfällig, und ohne Vergleicher stünde der
            // Ordner in Einlesereihenfolge.
            AufgabenView.CustomSort ??= new NaturalStringComparer();

            // OnFileDrop liest den Ordner der übergebenen Datei ein und wählt sie danach
            // aus. Der öffentliche Weg (verwerfeSuchtreffer: true) ist hier richtig: Der
            // Ordner wechselt tatsächlich, alte Suchtreffer führten zurück in die Ablage.
            await OnFileDrop(new[] { zielBild });

            // Notnagel für den Fall, dass ein gesetzter Filter genau dieses Bild
            // ausblendet. Dann fände OnFileDrop es nicht und die Ansicht stünde auf -1 —
            // von dort käme man ohne Zutun nicht mehr weiter. Im Normalfall läuft dieser
            // Zweig nicht, und nur deshalb entsteht hier kein zweiter Ladeauftrag.
            if (AufgabenView.CurrentItem == null && AufgabenView.Count > 0)
            {
                AufgabenView.MoveCurrentToFirst();
                SelectedBildchen = AufgabenView.CurrentItem as MeinBildchen;
            }
        }

        #endregion

        #region Command Datei im Explorer anzeigen

        private bool CanExecuteDateiImExplorerÖffnen()
        {
            return true;
        }

        [RelayCommand(CanExecute = nameof(CanExecuteDateiImExplorerÖffnen))]
        private void CommandExecuteDateiImExplorerÖffnen()
        {
            if (SelectedBildchen == null)
            {
                return;
            }
            if (File.Exists(SelectedBildchen.BName))
            {
                string argument = "/select, \"" + SelectedBildchen.BName + "\"";
                Process.Start("explorer.exe", argument);
            }
        }

        #endregion


        #region Command Kleines Bild grosses Bild Laden mit Infos

        /// <summary>
        /// Nur noch: Ist überhaupt ein Bild gewählt?
        ///
        /// Vorher stand hier <c>File.Exists</c> — ein Plattenzugriff im UI-Faden, und zwar
        /// bei jeder Auswertung. Ausgelöst wird sie von beiden Miniaturleisten, die über
        /// <c>IsSynchronizedWithCurrentItem</c> an derselben Ansicht hängen. Auf einer
        /// schlafenden Platte kostet ein einziger solcher Zugriff die ganze Anlaufzeit,
        /// und solange steht die Oberfläche.
        ///
        /// Ob die Datei wirklich da ist, prüft der Befehl selbst — im Hintergrund und
        /// gebündelt mit dem Lesen der Bildmasse.
        /// </summary>
        private bool CanExecuteKleinesBildGrossesBildLaden()
            => !string.IsNullOrEmpty(SelectedBildchen?.BName);

        [RelayCommand(CanExecute = nameof(CanExecuteKleinesBildGrossesBildLaden))]
        private async Task CommandExecuteKleinesBildGrossesBildLaden()
        {
            // Sichere Kopie des Pfads. Ab hier gilt sie für den ganzen Durchlauf — auch
            // im Prüf-Zweig, der vorher an jeder Stelle SelectedBildchen neu las. Die
            // Auswahl kann während der Wartezeiten wechseln (ein Klick in die
            // Miniaturleiste verschiebt sie, selbst wenn er kein Laden auslöst); der
            // Zweig mischte dann Vorschau, grosses Bild und Prüfergebnis aus zwei
            // Bildern und rechnete die Dekodiergrösse aus einem dritten.
            var path = SelectedBildchen?.BName;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            // Ladebalken auf Anfang.
            //
            // Ohne das stand dort noch der Endwert des vorigen Bildes — der Balken
            // erschien also gefüllt und sprang dann auf die erste Stufe zurück. Er zeigte
            // beim Start jedes Bildes „fertig".
            ProgressValue = LadestufeStart;

            // Zeit stoppen
            var stopwatch = Stopwatch.StartNew();

            int decodeWidth = 0;
            int decodeHeight = 0;

            // Image pixel abfragen — im Hintergrund, zusammen mit der Frage, ob es die
            // Datei noch gibt.
            //
            // Beides öffnet die Datei. Genau hier stand die Oberfläche, wenn die Platte
            // erst anlaufen musste: ReadOriginalSize lief als einziger Schritt dieses
            // Befehls im UI-Faden, alles darunter war längst ausgelagert. Ein Klick, der
            // währenddessen kam — etwa auf den Bildmodus-Umschalter —, wurde erst danach
            // bearbeitet und sah aus, als reagiere die Anwendung nicht.
            // Vorher noch billiger: Hat der Vorauslauf dieses Bild schon geholt, sind die
            // Masse längst bekannt und die Platte bleibt ganz aussen vor. Siehe
            // AufgabeViewModel.Bildvorrat.cs.
            var vorrat = VorratNachschlagen(path);

            bool dateiDa;
            int breite, hoehe;

            if (vorrat is not null)
            {
                // Ohne Nachfrage bei der Platte als vorhanden geführt. Der Vorauslauf hat
                // die Datei vor wenigen Sekunden geöffnet; verschwindet sie in diesem
                // Fenster von aussen, fällt das eine Bild später auf — beim
                // Hintergrundlauf, der die Liste ohnehin nachsieht. Erneut nachzusehen
                // wäre genau der Zugriff, den der Vorrat einspart.
                dateiDa = true;
                breite = vorrat.OriginalBreite;
                hoehe = vorrat.OriginalHöhe;
            }
            else
            {
                (dateiDa, breite, hoehe) = await Task.Run(() =>
                {
                    if (!File.Exists(path))
                    {
                        return (false, 0, 0);
                    }

                    var (w, h) = MieneServices.ReadOriginalSize(path);
                    return (true, w, h);
                });
            }

            // Datei ist weg: Eintrag heraus, zum nächsten Bild, Rest im Hintergrund
            // nachsehen. Der Ladeweg ist die einzige Stelle, an der das ohne zusätzlichen
            // Plattenzugriff auffällt — er musste die Datei ohnehin öffnen.
            if (!dateiDa)
            {
                EntferneVerschwundenesBild(path);
                return;
            }

            // Es lädt wieder – die Sprungbremse darf von vorn zählen.
            _verschwundenHintereinander = 0;

            OriginalImageWidth = breite;
            OriginalImageHeight = hoehe;

            // Monitor‑Decode‑Größe
            (int monitorWidth, int monitorHeight) = MieneServices.GetMonitorDecodeSize();

            // Dieselbe Rechnung wie im Vorauslauf — sie steht deshalb nur einmal, in
            // AufgabeViewModel.Bildvorrat.cs. Träfe der Vorauslauf eine andere Grösse,
            // hätte er umsonst gelesen.
            (decodeWidth, decodeHeight) = DekodierGrösseRechnen(
                OriginalImageWidth, OriginalImageHeight, monitorWidth, monitorHeight);

            // Passt die Dekodiergrösse nicht zum Vorrat — anderer Bildschirm, Fenster auf
            // einen zweiten Monitor gezogen —, taugt das vorgeladene grosse Bild nicht
            // mehr. Vorschau und Masse bleiben brauchbar.
            bool grossAusVorrat = vorrat is not null
                && vorrat.DekodierBreite == decodeWidth
                && vorrat.DekodierHöhe == decodeHeight;



            // Nachschauen ob Bild geprüft werden soll
            // Bild soll nicht geprüft werden
            if (!SollBildGeprüftWerden)
            {
                IsBildDateiBeschädigt = null;
                IsHeaderPassendZurErweiterung = null;
                IsFrameImBildDrin = null;
                IsBildDownloadCorrupted = null;
                IsBildNullDatei = null;

                PrüfungLäuft = false;


                // Bild anzeigen

                PrüfungLäuft = true;
                IsDisplayImageLoading = true;

                // 1. Stufe: Kleines Vorschaubild laden (сто Pixel)
                var kl = vorrat?.Klein ?? await Task.Run(() => MieneServices.CreateBitmap(path, 100));
                ProgressValue = LadestufeVorschauGeladen;

                // Farbsignatur aus dem Vorschaubild – nicht aus dem grossen Bild.
                BildFarbsignatur = await Task.Run(() => Bildersuche.Farbsignatur.Erstelle(kl));

                SWkleinesBild = stopwatch.Elapsed.TotalMilliseconds.ToString("F3") + " ms";

                // Die Zwischenstufe nur zeigen, solange das grosse Bild fehlt. Liegt es im
                // Vorrat bereit, blitzte hier sonst das 100-Pixel-Bild auf, obwohl das
                // scharfe im selben Augenblick folgen kann.
                if (!grossAusVorrat)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        DisplayImage = kl;
                    });
                }

                ProgressValue = LadestufeVorschauSichtbar;

                // Bewertung erst NACH dem Anzeigen: Sie ändert nichts am Bild und darf es
                // deshalb nicht aufhalten. Läuft auch ohne Häkchen, siehe
                // AufgabeViewModel.Bildbewertung.cs.
                await BewerteAusVorschauAsync(path, kl);

                stopwatch = Stopwatch.StartNew();

                // Grosses Bild nicht laden wenn CommandExecuteAlleBilderInsKeinFavVerschieben läuft
                if (CommandExecuteAlleBilderInsKeinFavVerschiebenCommand.IsRunning /*|| SelectedBildchen!=null*/)
                {
                    // Abbrechen, wenn der andere Befehl läuft

                    PrüfungLäuft = false;
                    return;
                }

                // 2. Stufe: Volles Bild laden – oder aus dem Vorrat nehmen.
                var gr = grossAusVorrat
                    ? vorrat!.Gross
                    : await Task.Run(() => MieneServices.CreateBitmap(path, decodeWidth, decodeHeight));

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DisplayImage = gr;
                });

                // Jetzt steht das Bild – erst ab hier darf der Vorauslauf an die Platte.
                VorratNachfüllen(path, breite, hoehe, decodeWidth, decodeHeight, kl, gr);

                // SWgrossesBild
                SWgrossesBild = stopwatch.Elapsed.TotalMilliseconds.ToString("F3") + " ms";

                ProgressValue = LadestufeGrossSichtbar;

                await BewerteKantendichteAsync(gr);

                // Ohne Häkchen folgt keine Prüfung mehr — dann ist hier wirklich Schluss,
                // und der Balken darf ans Ende springen.
                ProgressValue = LadestufeFertig;

                IsDisplayImageLoading = false;
                PrüfungLäuft = false;

            }
            else
            {
                // Bild prüfen

                // Prüfkastchen zurücksetzen
                IsBildDateiBeschädigt = null;
                IsHeaderPassendZurErweiterung = null;
                IsFrameImBildDrin = null;
                IsBildDownloadCorrupted = null;
                IsBildNullDatei = null;


                if (!File.Exists(path))
                {
                    IsBildDateiBeschädigt = true;
                    IsHeaderPassendZurErweiterung = false;
                    IsFrameImBildDrin = false;
                    IsBildDownloadCorrupted = false;
                    IsBildNullDatei = true;

                    // Bildchen entfernen
                    var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == path);
                    //var indexSelected = AufgabenView.CurrentPosition;

                    if (bildchen != null)
                    {
                        //var index = OcAufgabens.IndexOf(bildchen);

                        //bildchen.BName = zielVollPfad;
                        //bildchen.BildFürLinks = false;

                        //OcAufgabens.Move(index, indexSelected);
                        OcAufgabens.Remove(bildchen);
                        AufgabenView.MoveCurrentToNext();

                        AufgabenView.Refresh();

                    }

                    return;
                }



                PrüfungLäuft = true;
                IsDisplayImageLoading = true;
                //  ProgressValue = двадцать; // Startwert

                // 1. Stufe: Kleines Vorschaubild laden (сто Pixel)
                var kl = vorrat?.Klein ?? await Task.Run(() => MieneServices.CreateBitmap(path, 100));
                ProgressValue = LadestufeVorschauGeladen;

                // Farbsignatur aus dem Vorschaubild – nicht aus dem grossen Bild.
                BildFarbsignatur = await Task.Run(() => Bildersuche.Farbsignatur.Erstelle(kl));

                // Zwischenstufe nur ohne Vorrat – Begründung im Zweig darüber.
                if (!grossAusVorrat)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        DisplayImage = kl;
                    });
                }

                ProgressValue = LadestufeVorschauSichtbar;

                // Bewertung erst NACH dem Anzeigen – siehe AufgabeViewModel.Bildbewertung.cs.
                await BewerteAusVorschauAsync(path, kl);

                // Künstliche Verzögerung, damit man den Fortschritt sieht
                //await Task.Delay(20);

                // 2. Stufe: Volles Bild laden – oder aus dem Vorrat nehmen.
                var gr = grossAusVorrat
                    ? vorrat!.Gross
                    : await Task.Run(() => MieneServices.CreateBitmap(path, decodeWidth, decodeHeight));

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DisplayImage = gr;
                });

                // Erst ab hier darf der Vorauslauf an die Platte. Die Dateiprüfung
                // weiter unten liest ebenfalls von ihr — beide zusammen sind immer noch
                // billiger als ein Bild, das erst mit dem Klick beginnt.
                VorratNachfüllen(path, breite, hoehe, decodeWidth, decodeHeight, kl, gr);

                ProgressValue = LadestufeGrossSichtbar;

                await BewerteKantendichteAsync(gr);

                SWgrossesBild = stopwatch.Elapsed.TotalMilliseconds.ToString("F3") + " ms";

                //    ProgressValue = сто; // Fertig
                //await Task.Delay(200); // Kurz anzeigen
                IsDisplayImageLoading = false;

                //// Mach den g1 In einen Task auslagern
                //await Task.Run(() =>
                //{
                //    var g1 = MieneServices.IsBildDateiBeschädigt(SelectedBildchen.BName);
                //    Application.Current.Dispatcher.Invoke(() =>
                //    {
                //        IsBildDateiBeschädigt = g1;
                //        Debug.WriteLine($"IsBildDateiBeschädigt: {g1}");
                //    });
                //});

                //ProgressValue = 3;

                //await Task.Run(() =>
                //{
                //    var g2 = MieneServices.IsHeaderPassendZurErweiterung(SelectedBildchen.BName);

                //    Application.Current.Dispatcher.InvokeAsync(() =>
                //    {
                //        IsHeaderPassendZurErweiterung = g2;
                //        Debug.WriteLine($"IsHeaderPassendZurErweiterung : {g2}");
                //    });
                //});
                //ProgressValue = 4;

                //await Task.Run(() =>
                //{
                //    var g3 = MieneServices.IsFrameImBildDrin(SelectedBildchen.BName);

                //    Application.Current.Dispatcher.InvokeAsync(() =>
                //    {
                //        IsFrameImBildDrin = g3;
                //        Debug.WriteLine($"IsFrameImBildDrin: {g3}");
                //    });
                //});
                //ProgressValue = 5;

                //await Task.Run(() =>
                //{
                //    var g4 = MieneServices.IsBildDownloadCorrupted(SelectedBildchen.BName);
                //    Application.Current.Dispatcher.Invoke(() =>
                //    {
                //        IsBildDownloadCorrupted = g4;
                //        Debug.WriteLine($"IsBildDownloadCorrupted: {g4}");
                //    });
                //});
                //ProgressValue = 6;

                //await Task.Run(() =>
                //{
                //    var g5 = MieneServices.IsBildNullDatei(SelectedBildchen.BName);
                //    Application.Current.Dispatcher.Invoke(() =>
                //    {
                //        IsBildNullDatei = g5;
                //        Debug.WriteLine($"g5  IsBildNullDatei: {g5}");
                //    });
                //});
                //ProgressValue = 7;

                //PrüfungLäuft = false;

                PrüfungLäuft = true;
                try
                {
                    var r = await Task.Run(() =>
                        BildCheckCopilot.PruefeBildDatei(path));
                    IsBildDateiBeschädigt = r.IstBeschädigt;
                    IsHeaderPassendZurErweiterung = r.HeaderPasst;
                    IsFrameImBildDrin = r.HatFrame;
                    IsBildDownloadCorrupted = r.DownloadKorrupt;
                    IsBildNullDatei = r.IstNullDatei;
                    ErkanntesFormat = r.DetektiertesFormat;
                    SetzeHeaderText(path, r.HeaderPasst, r.DetektiertesFormat);

                    Debug.WriteLine($"Header={r.HeaderPasst}, Frame={r.HatFrame}, " +
                                    $"Korrupt={r.DownloadKorrupt}, Null={r.IstNullDatei}, Format={r.DetektiertesFormat}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Bildprüfung Fehler: {ex}");

                    // IsHeaderPassendZurErweiterung, nicht HeaderPasstZurErweiterung: Das
                    // Ampelfeld bindet diese hier. Auf die zweite Eigenschaft fast gleichen
                    // Namens zu schreiben lässt im Fehlerfall den Wert des vorigen Bildes stehen.
                    IsHeaderPassendZurErweiterung = false;
                    HeaderText = "Prüfung fehlgeschlagen: " + ex.Message;
                    IsFrameImBildDrin = false;
                    IsBildDownloadCorrupted = true;
                    IsBildNullDatei = false;
                    ErkanntesFormat = "unknown";
                }
                finally
                {
                    PrüfungLäuft = false;
                    ProgressValue = LadestufeFertig;
                }

            }

            // Nachziehen, wenn die Auswahl während des Ladens weitergewandert ist.
            //
            // Der Befehl lässt keine zwei Durchläufe nebeneinander zu: Solange einer
            // läuft, meldet CanExecute false, und der SelectionChanged-Trigger der
            // Miniaturleiste läuft ins Leere. Das angeklickte Bildchen ist dann zwar
            // ausgewählt, geladen wird es aber nie — angezeigt bleibt das vorige. Ohne
            // Häkchen fällt das kaum auf, weil ein Durchlauf nur Millisekunden dauert;
            // mit CHK_BildPrüfen kommt die Dateiprüfung dazu, und das Fenster, in dem
            // Klicks verschluckt werden, wird gross genug zum Danebenklicken.
            //
            // Die Pfeiltasten sind davon nie betroffen: Ihre Befehle hängen an
            // PrüfungLäuft und sind währenddessen ohnehin gesperrt.
            //
            // Nicht awaiten und über den Dispatcher nachgestellt: Erst wenn diese
            // Methode zurückgekehrt ist, gilt der Durchlauf als beendet und CanExecute
            // wieder als true — ein Aufruf von hier aus liefe sonst in dieselbe Sperre.
            if (SelectedBildchen?.BName is string inzwischenGewählt && inzwischenGewählt != path)
            {
                _ = Application.Current.Dispatcher.InvokeAsync(
                    () => CommandExecuteKleinesBildGrossesBildLadenCommand.Execute(null),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        // HeaderPasstZurErweiterung ist entfallen. Eine zweite Eigenschaft fast gleichen
        // Namens neben IsHeaderPassendZurErweiterung, an die nichts gebunden war. Sie
        // wurde nur im Fehlerzweig der Bildprüfung gesetzt — das Ampelfeld behielt dort
        // also die Farbe des vorigen Bildes.

        [ObservableProperty]
        public partial string ErkanntesFormat { get; set; } = "unknown";

        #endregion




        /// <summary>
        /// Das grosse Bild. <c>null</c>, solange keines geladen ist — etwa wenn ein Filter
        /// alles ausblendet. Der Kontur-Zweig prüft darauf, AnzeigeBild ist ebenfalls
        /// nullable; nur die Deklaration behauptete bisher das Gegenteil.
        /// </summary>
        [ObservableProperty]
        public partial BitmapSource? DisplayImage { get; set; }

        [ObservableProperty]
        public partial bool IsDisplayImageLoading { get; set; }

        /// <summary>
        /// Stufen des Bildladens für PGB_BildLadenStufen.
        ///
        /// <b>Vier echte Stufen, in beiden Zweigen dieselben.</b> Vorher lief die Zählung
        /// bis 7, während der Balken auf 6 begrenzt war — und dazwischen fehlten 3 bis 6
        /// ganz. Die gehörten zu den fünf einzelnen Prüfaufrufen, die inzwischen ein
        /// einziger <c>PruefeBildDatei</c> erledigt; die Stufen verschwanden mit ihnen,
        /// die Zählung blieb stehen.
        ///
        /// Als benannte Werte statt blosser Zahlen, damit <c>Maximum</c> in der XAML und
        /// die Zuweisungen hier nicht wieder auseinanderlaufen.
        /// </summary>
        private const int LadestufeStart = 0;

        private const int LadestufeVorschauGeladen = 1;
        private const int LadestufeVorschauSichtbar = 2;
        private const int LadestufeGrossSichtbar = 3;

        /// <summary>Muss zu <c>Maximum</c> von PGB_BildLadenStufen passen.</summary>
        private const int LadestufeFertig = 4;

        [ObservableProperty]
        public partial int ProgressValue { get; set; }






        [ObservableProperty]
        public partial Stretch ImageStretch { get; set; } = Stretch.Uniform;

        [ObservableProperty]
        public partial ScrollBarVisibility MyHorizontalScrollBarVisibility { get; set; } = ScrollBarVisibility.Disabled;

        [ObservableProperty]
        public partial string SWkleinesBild { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string SWgrossesBild { get; set; } = string.Empty;





        #region Command Bild Stretch anpassen

        /// <summary>
        /// Ist überhaupt ein Bild gewählt?
        ///
        /// Hier stand File.Exists — ein Plattenzugriff im UI-Faden bei jeder Auswertung,
        /// und dieser hier für nichts: Der Befehl schaltet nur zwischen zwei Werten von
        /// ImageStretch um und fasst keine Datei an. Gemeint war „es gibt ein Bild", und
        /// das steht schon im Pfad.
        /// </summary>
        private bool CanExecuteBildStretchAnpassen()
        {
            return !string.IsNullOrEmpty(SelectedBildchen?.BName) && (!PrüfungLäuft);
        }

        [RelayCommand(CanExecute = nameof(CanExecuteBildStretchAnpassen))]
        private void CommandExecuteBildStretchAnpassen()
        {
            if (ImageStretch == Stretch.Uniform)
            {
                ImageStretch = Stretch.None;
                MyHorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            else if (ImageStretch == Stretch.None)
            {
                ImageStretch = Stretch.Uniform;
                MyHorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

            }
            else
            {
                ImageStretch = Stretch.Uniform;
            }
        }

        #endregion

        #region Command Alle Bilder ins kein Fav verschieben
        private bool CanExecuteAlleBilderInsKeinFavVerschieben()
        {
            // !IndexLaeuft: Während des Indexierens dürfen keine Dateien wegwandern.
            // Der Index wird gerade geschrieben und verweist auf Pfade — verschobene
            // Bilder machen ihn stellenweise unbrauchbar, ohne dass man es ihm ansieht.
            // Reihenfolge nach Kosten: erst die Schalter, dann die Frage am gewählten
            // Bild, zuletzt die beiden Durchläufe durch die Liste. && schliesst kurz,
            // also bleibt der teure Teil aus, sobald ein billiger schon nein sagt —
            // und ausgewertet wird das hier bei jedem Tastendruck.
            return (!PrüfungLäuft)
                && (!IndexLaeuft)
                && (SelectedBildchen != null && !SelectedBildchen.BName.Contains("kein_Fav"))
                && OcAufgabens.Any(b => b.BildFürLinks == false)
                && ListeStammtAusEinemOrdner;
        }

        /// <summary>
        /// True, solange alle Bilder der Liste aus demselben Ordner stammen.
        ///
        /// Die Einzelbild-Befehle leiten ihr Ziel aus dem Pfad des jeweiligen Bildes ab
        /// und sind deshalb gegen gemischte Listen unempfindlich.
        /// <see cref="CommandExecuteAlleBilderInsKeinFavVerschieben"/> berechnet das
        /// Zielverzeichnis dagegen **einmal** aus dem gewählten Bild und schöbe dann
        /// alles dorthin — bei einer Liste aus mehreren Ordnern würde er die Bilder
        /// quer über Ordnergrenzen in ein einziges kein_Fav zusammenkippen, in einem
        /// Rutsch und kaum rückgängig zu machen.
        ///
        /// Deshalb ist der Befehl dann gesperrt. Wieder verfügbar wird er über
        /// „Alle Bilder neu einlesen", das die Liste auf einen Ordner zurücksetzt.
        /// </summary>
        public bool ListeStammtAusEinemOrdner
        {
            get
            {
                // Zwischenstand, solange sich weder die Liste noch ein Pfad geändert hat.
                //
                // Diese Eigenschaft hängt in einem CanExecute, und CanExecute wird über
                // CommandManager.RequerySuggested bei jedem Tastendruck neu ausgewertet.
                // Ohne den Zwischenstand lief bei jedem Druck auf eine Pfeiltaste ein
                // Durchlauf durch die ganze Liste — und zwar der volle: Der Ausstieg
                // greift erst beim ersten abweichenden Ordner, im Normalfall stammt aber
                // alles aus einem. Jedes Element kostete dabei ein Path.GetDirectoryName,
                // also eine Zeichenkette, die gleich wieder weggeworfen wird.
                if (!_ordnerEinheitVeraltet && _ordnerEinheitGeneration == MeinBildchen.PfadGeneration)
                {
                    return _ordnerEinheitStand;
                }

                string? ersterOrdner = null;
                bool einheitlich = true;

                foreach (var bild in OcAufgabens)
                {
                    if (string.IsNullOrWhiteSpace(bild.BName))
                    {
                        continue;
                    }

                    string? ordner = Path.GetDirectoryName(bild.BName);

                    if (ersterOrdner is null)
                    {
                        ersterOrdner = ordner;
                    }
                    else if (!string.Equals(ersterOrdner, ordner, StringComparison.OrdinalIgnoreCase))
                    {
                        einheitlich = false;
                        break;
                    }
                }

                _ordnerEinheitStand = einheitlich;
                _ordnerEinheitGeneration = MeinBildchen.PfadGeneration;
                _ordnerEinheitVeraltet = false;

                return einheitlich;
            }
        }

        /// <summary>Zuletzt gerechnetes Ergebnis von <see cref="ListeStammtAusEinemOrdner"/>.</summary>
        private bool _ordnerEinheitStand;

        /// <summary>Stand von <see cref="MeinBildchen.PfadGeneration"/> bei dieser Rechnung.</summary>
        private int _ordnerEinheitGeneration = -1;

        /// <summary>
        /// Die Bilderliste hat sich geändert — der Zwischenstand gilt nicht mehr. Die
        /// Pfadgeneration allein genügt hier nicht: Ein Bild zu entfernen ändert keinen
        /// einzigen Pfad, kann aber sehr wohl aus einer gemischten Liste eine
        /// einheitliche machen.
        /// </summary>
        private bool _ordnerEinheitVeraltet = true;
        [RelayCommand(CanExecute = nameof(CanExecuteAlleBilderInsKeinFavVerschieben), IncludeCancelCommand = true)]
        private async Task CommandExecuteAlleBilderInsKeinFavVerschieben(CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                PrüfungLäuft = true;

                await MeTa_AlleBilderInsKeinFavVerschieben(token);
            }

            catch (OperationCanceledException)
            {
                // Abbruch ist ok
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

            finally
            {
                PrüfungLäuft = false;
                LabelDropContent = sw.Elapsed.TotalSeconds + " Sek";
                AufgabenView.Refresh();
                UpdateAlleBilderVerschoben();
            }


        }

        private async Task MeTa_AlleBilderInsKeinFavVerschieben(CancellationToken token)
        {

            // Copilot Code


            // ---------- SELECTION SNAPSHOT ----------
            string? vorherSelectedFullName = SelectedBildchen?.BName;
            string? vorherSelectedFileName = Path.GetFileName(vorherSelectedFullName);

            // ---------- GUARDS ----------
            if (vorherSelectedFullName is null)
            {
                return;
            }

            string? baseDir = Path.GetDirectoryName(vorherSelectedFullName);
            if (string.IsNullOrEmpty(baseDir))
            {
                return;
            }

            // ---------- ZIELVERZEICHNIS ----------
            string zielVerzeichnis = Path.Combine(baseDir, "kein_Fav");

            // CreateDirectory ist idempotent (existiert schon → kein Fehler)
            Directory.CreateDirectory(zielVerzeichnis);

            // ---------- SNAPSHOT DER ARBEITSLISTE ----------
            var bilderZuVerschieben = OcAufgabens.Where(b => b.BildFürLinks == false).ToList();

            if (bilderZuVerschieben.Count == 0)
            {
                return;
            }

            // ---------- PROGRESS ----------
            int total = bilderZuVerschieben.Count;
            int done = 0;
            DateTime started = DateTime.Now;

            IProgress<CLProgressStückzahl> progress = new Progress<CLProgressStückzahl>(p =>
                {
                    PercentageValueVerschieben = p.Percent;
                    LabelDropContent = p.Restzeit;
                });

            // ---------- HAUPTSCHLEIFE ----------
            foreach (var bildchen in bilderZuVerschieben)
            {
                token.ThrowIfCancellationRequested();

                string source = bildchen.BName;
                string zielDateiFullName = Path.Combine(zielVerzeichnis, Path.GetFileName(source));

                // Defensive Guards pro Datei
                if (!File.Exists(source) || File.Exists(zielDateiFullName))
                {
                    done++;
                    progress.Report(new CLProgressStückzahl(started, total, done, false));
                    continue;
                }

                try
                {
                    await MieneServices.CopyAndDeleteFileAsync(source, zielDateiFullName, token);

                    // ✅ NUR BEI ERFOLG Model ändern
                    bildchen.BName = zielDateiFullName;
                    bildchen.BildFürLinks = true;

                    done++;
                    OnPropertyChanged(nameof(CountBildchenFürLinks));

                    progress.Report(new CLProgressStückzahl(started, total, done, false));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Fehler beim Verschieben:\n{source}\n\n{ex.Message}",
                        "Verschieben fehlgeschlagen",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            // ---------- SELECTION WIEDERHERSTELLEN ----------
            if (!string.IsNullOrEmpty(vorherSelectedFileName))
            {
                var wiederZuWählendesBildchen = OcAufgabens.FirstOrDefault(b => Path.GetFileName(b.BName)
                .Equals(vorherSelectedFileName, StringComparison.OrdinalIgnoreCase));

                if (wiederZuWählendesBildchen != null)
                {
                    SelectedBildchen = wiederZuWählendesBildchen;
                }
                else if (OcAufgabens.Count > 0)
                {
                    // Fallback: erstes Bild
                    SelectedBildchen = OcAufgabens[0];
                }
            }

        }

        #endregion

        #region Command Gleiches Bild suchen, Byte vergleich

        private bool CanExecuteSuchenGleichesBildByteVergleich()
        {
            // !IndexLaeuft: Gegenstück zur Sperre am Indexieren – nur ein schwerer
            // Vorgang gleichzeitig, sonst zeigt die gemeinsame Leiste nur einen davon.
            return OcAufgabens.Count > 1 && (!PrüfungLäuft) && (!IndexLaeuft);
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSuchenGleichesBildByteVergleich), IncludeCancelCommand = true)]
        private async Task CommandExecuteSuchenGleichesBildByteVergleich(CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                PrüfungLäuft = true;

                await MeTa_SuchenGleichesBildByteVergleich(token);
            }
            catch (Exception)
            {

            }
            finally
            {
                PrüfungLäuft = false;
                PercentageValueVerschieben = 0.0;
                LabelDropContent = "Gs  " + sw.Elapsed.TotalSeconds.ToString("F2") + " Sek";
                AufgabenView.Refresh();
            }


        }

        private async Task MeTa_SuchenGleichesBildByteVergleich(CancellationToken token)
        {
            //throw new NotImplementedException();
            var sw = Stopwatch.StartNew();
            DateTime started = DateTime.Now;
            IProgress<CLProgressStückzahl> progressStück = new Progress<CLProgressStückzahl>(value => PercentageValueVerschieben = value.Percent);

            var bilder = OcAufgabens.ToList();
            long gszähler = bilder.Count;
            int zähler = 0;

            int total = /*(int)((gszähler * gszähler) + gszähler)*/(int)gszähler;
            object progressLock = new();
            int lastPercent = 0;
            CountInnerZählerTest = 1;

            if (MultiByteParallelGleichheit)
            {
                //
                // Aufgabe Paralleler Byte Vergleich:
                // Alle Bilder in der Collection mit dem ausgewählten Bild vergleichen und die Bilder entfernen,
                // die nicht gleich sind. Fortschrittsanzeige mit Prozent und Restzeit.

                var pcCount = Environment.ProcessorCount;
                var results = new ConcurrentBag<string>();

                //  /* var result= *//*await Task.WhenAll(*/
                await Parallel.ForEachAsync(bilder, new ParallelOptions { MaxDegreeOfParallelism = pcCount }, async (filep, _) =>
                {
                    //// Probieren Progress
                    //var pgs = new CLProgressStückzahl(started, gszähler, zähler++, false);

                    //progressStück?.Report(pgs);
                    //LabelDropContent = pgs.Restzeit;

                    //Console.WriteLine($"Task {item} gestartet.");
                    //await Task.Delay(1000); // Simuliert Arbeit
                    var gleich = await MieneServices.IsFileGleichAsync(SelectedBildchen.BName, filep.BName, token);

                    if (!gleich)
                    {
                        results.Add(filep.BName);
                    }

                    // 2060
                    int current = Interlocked.Increment(ref zähler);
                    int percent = (int)((double)current / total * 100);

                    bool shouldReport = false;
                    lock (progressLock)
                    {
                        if (percent > lastPercent)
                        {
                            lastPercent = percent;
                            shouldReport = true;
                        }
                    }

                    if (shouldReport)
                    {
                        CountInnerZählerTest++;
                        var pgs = new CLProgressStückzahl(started, total, current, false);
                        progressStück?.Report(pgs);
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            LabelDropContent = "Rest " + pgs.Restzeit + "  ( " + pgs.StückPerSecond.ToString("F0") + " Stk/Sek )";
                        });
                    }

                    //Console.WriteLine($"Task {item} beendet.");
                });

                foreach (var item in results)
                {
                    if (File.Exists(item) & (item != SelectedBildchen?.BName))
                    {
                        // Bildchen aus der Collection entfernen
                        var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == item);
                        if (bildchen != null)
                        {
                            OcAufgabens.Remove(bildchen);
                        }
                    }
                }
                //
            }
            else
            {
                //Einzelner Byte Vergleich, langsam, da jedes Bild nacheinander geprüft wird
                foreach (var item in bilder)
                {
                    var pgs = new CLProgressStückzahl(started, gszähler, zähler++, false);

                    progressStück?.Report(pgs);
                    LabelDropContent = pgs.Restzeit;

                    if (File.Exists(item.BName) & (item.BName != SelectedBildchen?.BName))
                    {
                        var gleich = await MieneServices.IsFileGleichAsync(SelectedBildchen?.BName, item.BName, token);
                        if (!gleich)
                        {
                            // Bildchen aus der Collection entfernen
                            OcAufgabens.Remove(item);
                        }
                    }
                }
            }








        }


        #endregion

        #region Command Ungefähr Gleiches Bild suchen, max 10 % Abweichung

        private bool CanExecuteSuchenUngefährGleichesBild()
        {
            return OcAufgabens.Count > 1 && (!PrüfungLäuft) && (!IndexLaeuft);

        }

        [RelayCommand(CanExecute = nameof(CanExecuteSuchenUngefährGleichesBild), IncludeCancelCommand = true)]
        private async Task CommandExecuteSuchenUngefährGleichesBild(CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                PrüfungLäuft = true;

                await MeTa_SuchenUngefährGleichesBild(token);
            }
            catch (Exception)
            {

            }
            finally
            {
                PrüfungLäuft = false;
                LabelDropContent = sw.Elapsed.TotalSeconds.ToString("F2") + " Sek";
                AufgabenView.Refresh();
            }


        }

        private async Task MeTa_SuchenUngefährGleichesBild(CancellationToken token)
        {
            //throw new NotImplementedException();
            var sw = Stopwatch.StartNew();
            DateTime started = DateTime.Now;
            IProgress<CLProgressStückzahl> progressStück = new Progress<CLProgressStückzahl>(value => PercentageValueVerschieben = value.Percent);

            var bilder = OcAufgabens.ToList();
            long gszähler = bilder.Count - 1;
            int zähler = 0;

            if (MultiByteParallelGleichheit)
            {
                // Paralleler Vergleich mit Hamming Distance:
                ulong hash2 = await MieneServices.GetImageHash(SelectedBildchen?.BName, token);
                var pcCount = Environment.ProcessorCount;
                var results = new ConcurrentBag<string>();

                await Parallel.ForEachAsync(bilder, new ParallelOptions { MaxDegreeOfParallelism = pcCount }, async (filep, _) =>
                {
                    var pgs = new CLProgressStückzahl(started, gszähler, zähler++, false);
                    progressStück?.Report(pgs);
                    LabelDropContent = "Rest  " + pgs.Restzeit;
                    if (File.Exists(filep.BName) & (filep.BName != SelectedBildchen?.BName))
                    {
                        ulong hash1 = await MieneServices.GetImageHash(filep.BName, token);
                        // ulong hash2 = await MieneServices.GetImageHash(SelectedBildchen?.BName);
                        int distance = await MieneServices.HammingDistance(hash1, hash2, token);
                        if (distance > 10)
                        {
                            results.Add(filep.BName);
                        }
                    }
                });

                foreach (var item in results)
                {
                    if (File.Exists(item) & (item != SelectedBildchen?.BName))
                    {
                        // Bildchen aus der Collection entfernen
                        var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == item);
                        if (bildchen != null)
                        {
                            OcAufgabens.Remove(bildchen);
                        }
                    }
                }

            }
            else
            {
                // Einzen prüfen, langsam, da jedes Bild nacheinander geprüft wird
                ulong hash2 = await MieneServices.GetImageHash(SelectedBildchen?.BName, token);

                foreach (var item in bilder)
                {
                    var pgs = new CLProgressStückzahl(started, gszähler, zähler++, false);

                    progressStück?.Report(pgs);

                    LabelDropContent = "Rest  " + pgs.Restzeit;

                    if (File.Exists(item.BName) & (item.BName != SelectedBildchen?.BName))
                    {
                        ulong hash1 = await MieneServices.GetImageHash(item.BName, token);
                        // ulong hash2 = await MieneServices.GetImageHash(SelectedBildchen?.BName);

                        int distance = await MieneServices.HammingDistance(hash1, hash2, token);

                        if (distance > 10)
                        {
                            // Bildchen aus der Collection entfernen
                            OcAufgabens.Remove(item);
                        }
                    }
                }
            }



        }

        #endregion

        #region Command Fenster Minimieren

        /// <summary>
        /// Überprüft, ob das Fenster minimiert werden kann.
        /// </summary>
        /// <returns></returns>
        private bool CanExecuteFensterMinimieren()
        {
            return true;
        }

        /// <summary>
        /// Minimizes the currently active window of the application.
        /// </summary>
        /// <remarks>This method ensures that the window state is updated on the UI thread by invoking the
        /// operation through the application's dispatcher. If no window is currently active, the method performs no
        /// action.</remarks>
        [RelayCommand(CanExecute = nameof(CanExecuteFensterMinimieren))]
        private void CommandExecuteFensterMinimieren()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Aktuelles Fenster minimieren
                var currentWindow = Application.Current.Windows.OfType<Window>().SingleOrDefault(w => w.IsActive);
                if (currentWindow != null)
                {
                    currentWindow.WindowState = WindowState.Minimized;
                }
            });
        }

        #endregion

        #region Command Alle Bilder miteinander auf Byte Gleichheit prüfen
        private bool CanExecuteAlleBilderMiteinanderAufByteGleichheitPrüfen()
        {
            return OcAufgabens.Count > 1 && (!PrüfungLäuft) && (!IndexLaeuft) /*&& (!MultiByteParallelGleichheit)*/;

        }

        [RelayCommand(CanExecute = nameof(CanExecuteAlleBilderMiteinanderAufByteGleichheitPrüfen), IncludeCancelCommand = true)]
        private async Task CommandExecuteAlleBilderMiteinanderAufByteGleichheitPrüfen(CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                PrüfungLäuft = true;

                await MeTa_AlleBilderMiteinanderAufByteGleichheitPrüfenAsync(token);
            }
            catch (Exception)
            {

            }
            finally
            {
                PrüfungLäuft = false;
                LabelDropContent = "Gs  " + sw.Elapsed.TotalSeconds.ToString("F2") + " Sek";
                PercentageValueVerschieben = 0.0;
                AufgabenView.Refresh();
            }


        }

        private async Task MeTa_AlleBilderMiteinanderAufByteGleichheitPrüfenAsync(CancellationToken token)
        {
            //throw new NotImplementedException();
            // 1574

            var sw = Stopwatch.StartNew();
            DateTime started = DateTime.Now;
            IProgress<CLProgressStückzahl> progressStück = new Progress<CLProgressStückzahl>(value => PercentageValueVerschieben = value.Percent);

            var bilder = OcAufgabens.ToList();
            long gszähler = bilder.Count;
            int zähler = 0;
            CountInnerZählerTest = 1;

            if (MultiByteParallelGleichheit)
            {
                //  return;
                //
                // Aufgabe Paralleler Byte Vergleich:
                // Alle Bilder in der Collection mit dem ausgewählten Bild vergleichen und die Bilder entfernen,
                // die nicht gleich sind. Fortschrittsanzeige mit Prozent und Restzeit.

                var pcCount = Environment.ProcessorCount;
                var results = new ConcurrentBag<string>();

                int total = (int)((gszähler * gszähler) + gszähler);
                object progressLock = new object();
                int lastPercent = 0;


                try
                {
                    foreach (var item1 in bilder)
                    {
                        using var stream1 = await Task.Run(() => new FileStream(item1.BName, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true));

                        await Parallel.ForEachAsync(bilder, new ParallelOptions { MaxDegreeOfParallelism = pcCount }, async (filep, _) =>
                        {
                            if (item1.BName != filep.BName)
                            {
                                if (File.Exists(item1.BName) & File.Exists(filep.BName))
                                {
                                    //var gleich = await MieneServices.IsFileGleichAsync(item1.BName, item2.BName, token);

                                    var gleich2 = await MieneServices.IsFileGleich2Async(stream1, filep.BName, token);
                                    if (gleich2)
                                    {
                                        results.Add(item1.BName);
                                    }
                                }
                            }


                            //zähler++;

                            // Vom Copilot gelöstes Problem mit der Progress Anzeige, da die Bilder parallel geprüft werden
                            // und somit die Fortschrittsanzeige nicht mehr linear ist,
                            // sondern je nach Geschwindigkeit der einzelnen Tasks variiert.
                            // Daher wird hier der Fortschritt anhand der Anzahl der geprüften Bilder berechnet und angezeigt.
                            // Kommentar ein bischen blödsinnig
                            int current = Interlocked.Increment(ref zähler);
                            int percent = (int)((double)current / (double)total * 100);

                            bool shouldReport = false;
                            lock (progressLock)
                            {
                                if (percent > lastPercent)
                                {
                                    lastPercent = percent;
                                    shouldReport = true;
                                }
                            }

                            if (shouldReport)
                            {
                                CountInnerZählerTest++;
                                var pgs = new CLProgressStückzahl(started, total, current, false);
                                progressStück?.Report(pgs);
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    LabelDropContent = "Rest " + pgs.Restzeit + "  ( " + pgs.StückPerSecond.ToString("F0") + " Stk/Sek )";
                                });
                            }

                        });
                    }

                }
                finally
                {
                    // Bildchen aus der Collection entfernen

                    // Rückwärts durchlaufen, damit Indizes nicht verschoben werden
                    for (int i = OcAufgabens.Count - 1; i >= 0; i--)
                    {
                        MeinBildchen? item = OcAufgabens[i];
                        if (!results.Contains(item.BName))
                        {
                            // Bildchen aus der Collection entfernen
                            OcAufgabens.Remove(item);
                        }
                    }
                }
            }
            else
            {
                //Einzelner Byte Vergleich, langsam, da jedes Bild nacheinander geprüft wird

                //List<MeinBildchen> li= new List<MeinBildchen>();
                var results = new ConcurrentBag<MeinBildchen>();
                foreach (var item1 in bilder)
                {
                    await Task.Run(async () =>
                    {
                        using var stream1 = new FileStream(item1.BName, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
                        foreach (var item2 in bilder)
                        {
                            var pgs = new CLProgressStückzahl(started, gszähler * gszähler - gszähler, zähler++, false);
                            progressStück?.Report(pgs);
                            LabelDropContent = pgs.Restzeit;

                            if (item1.BName != item2.BName)
                            {
                                if (File.Exists(item1.BName) & File.Exists(item2.BName))
                                {
                                    //var gleich = await MieneServices.IsFileGleichAsync(item1.BName, item2.BName, token);

                                    var gleich2 = await MieneServices.IsFileGleich2Async(stream1, item2.BName, token);
                                    if (gleich2)
                                    {
                                        results.Add(item1);

                                        // Position anpassen, damit die Bilder neben einander liegen, da sie gleich sind
                                        var index1 = OcAufgabens.IndexOf(item1);
                                        var index2 = OcAufgabens.IndexOf(item2);
                                        if (index1 != index2 & (OcAufgabens.Count > index1 + 1))
                                        {
                                            Application.Current.Dispatcher.Invoke(() =>
                                            {
                                                OcAufgabens.Move(index2, index1 + 1);
                                            });

                                        }
                                        else
                                        {
                                            //Debug.WriteLine("nicht gleich  " + item2.BName);
                                            //Debug.WriteLine("pgs  " + pgs.StückPerSecond);
                                        }
                                    }
                                }
                            }

                            Version = pgs.StückPerSecond.ToString("F0") + " Stk/Sek";
                        }

                    }, token);


                    if (!results.Contains(item1))
                    {
                        OcAufgabens.Remove(item1);

                    }
                }
            }
        }



        #endregion

        #region Command Bild ins KI Fehler verschieben
        private bool CanExecuteBildInsKIFehlerVerschiebenCommand()
        {
            return SelectedBildchen != null && !PrüfungLäuft && !IndexLaeuft;
        }

        [RelayCommand(CanExecute = nameof(CanExecuteBildInsKIFehlerVerschiebenCommand))]
        private async Task CommandExecuteBildInsKIFehlerVerschieben()
        {
            // copilot Lösung, die den Code aufräumt und die Logik beibehält

            // ---------- GUARDS (Null & State) ----------

            if (SelectedBildchen?.BName is not string source)
            {
                return;
            }

            string? baseDirectory = Path.GetDirectoryName(source);
            if (string.IsNullOrEmpty(baseDirectory))
            {
                return;
            }

            // ---------- PATHS (jetzt garantiert non-null) ----------

            string zielVerzeichnis = Path.Combine(baseDirectory, "KI_Fehler");
            string zielDateiFullName = Path.Combine(zielVerzeichnis, Path.GetFileName(source));

            PrüfungLäuft = true;
            bool moveErfolgreich = false;

            var sw = Stopwatch.StartNew();


            try
            {
                if (!File.Exists(source))
                {
                    return;
                }

                if (!Directory.Exists(zielVerzeichnis))
                {
                    Directory.CreateDirectory(zielVerzeichnis);
                }

                if (File.Exists(zielDateiFullName))
                {
                    MessageBox.Show(
                        "Die Datei existiert bereits im Zielverzeichnis:\n" + zielDateiFullName,
                        "Datei vorhanden",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                // ✅ Dateisystem async
                await Task.Run(() => File.Move(source, zielDateiFullName));
                CLconverterStringZuKleinemImage.InvalidateCache(source);
                moveErfolgreich = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Fehler beim Verschieben der Datei:\n\n" + ex.Message,
                    "Verschieben fehlgeschlagen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                sw.Stop();
                Debug.WriteLine($"Dauer: {sw.ElapsedMilliseconds} ms");
                PrüfungLäuft = false;
            }

            if (!moveErfolgreich)
            {
                return;
            }

            // ✅ Model / Collection Update NUR bei Erfolg
            var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == source);
            if (bildchen != null)
            {
                bildchen.BName = zielDateiFullName;
                bildchen.BildFürLinks = true;

                OnPropertyChanged(nameof(SelectedBildchen));
                OnPropertyChanged(nameof(CountBildchenFürLinks));
            }

            // Daran hängt BTN_VerschieberRückgängigMachen und Strg+Z. Ohne diese Zeile
            // holte Rückgängig die letzte kein_Fav-Verschiebung zurück statt dieser hier.
            BildchenVorher = zielDateiFullName;

            // Weiter zum nächsten noch nicht weggelegten Bild — wie beim ↓ nach kein_Fav.
            //
            // VOR dem Refresh, genau wie dort: MoveToNextNichtLinkesBild sucht über
            // BildFürLinks, und das ist oben schon gesetzt. Nach dem Refresh stünde die
            // aktuelle Position womöglich woanders.
            MoveToNextNichtLinkesBild();

            AufgabenView.Refresh();

        }

        #endregion

        #region Command Bild ins Besonders-Verzeichnis verschieben

        private bool CanExecuteBildInsBesondersVerschieben()
        {
            return SelectedBildchen != null && !PrüfungLäuft && !IndexLaeuft;
        }

        /// <summary>
        /// Verschiebt das gewählte Bild in den Unterordner „Besonders" (Shift+↓).
        /// Gegenstück zu kein_Fav (↓) und KI_Fehler (K), nur mit anderem Ziel.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteBildInsBesondersVerschieben))]
        private async Task CommandExecuteBildInsBesondersVerschieben()
        {
            await VerschiebeAktuellesBildInUnterordnerAsync("Besonders");
        }

        /// <summary>
        /// Verschiebt das gewählte Bild in einen Unterordner seines aktuellen Ordners
        /// und zieht die Liste nach. Der Ordner wird bei Bedarf angelegt; existiert die
        /// Datei am Ziel bereits, passiert nichts ausser einem Hinweis.
        ///
        /// <b>Springt danach zum nächsten noch nicht weggelegten Bild</b> — dasselbe tun
        /// auch ↓ (kein_Fav) und K (KI_Fehler). Das gehört zum Vertrag dieser Methode und
        /// nicht in den einzelnen Befehl, damit kein weiterer Aufrufer es vergisst.
        /// Bei einem gescheiterten Verschieben bleibt die Auswahl stehen.
        /// </summary>
        private async Task VerschiebeAktuellesBildInUnterordnerAsync(string unterordner)
        {
            if (SelectedBildchen?.BName is not string source)
            {
                return;
            }

            string? baseDirectory = Path.GetDirectoryName(source);
            if (string.IsNullOrEmpty(baseDirectory))
            {
                return;
            }

            string zielVerzeichnis = Path.Combine(baseDirectory, unterordner);
            string zielDateiFullName = Path.Combine(zielVerzeichnis, Path.GetFileName(source));

            PrüfungLäuft = true;
            bool moveErfolgreich = false;

            var sw = Stopwatch.StartNew();

            try
            {
                if (!File.Exists(source))
                {
                    return;
                }

                if (!Directory.Exists(zielVerzeichnis))
                {
                    Directory.CreateDirectory(zielVerzeichnis);
                }

                if (File.Exists(zielDateiFullName))
                {
                    MessageBox.Show(
                        "Die Datei existiert bereits im Zielverzeichnis:\n" + zielDateiFullName,
                        "Datei vorhanden",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                await Task.Run(() => File.Move(source, zielDateiFullName));
                CLconverterStringZuKleinemImage.InvalidateCache(source);
                moveErfolgreich = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Fehler beim Verschieben der Datei:\n\n" + ex.Message,
                    "Verschieben fehlgeschlagen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                sw.Stop();
                Debug.WriteLine($"Verschieben nach '{unterordner}': {sw.ElapsedMilliseconds} ms");
                PrüfungLäuft = false;
            }

            if (!moveErfolgreich)
            {
                return;
            }

            var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == source);
            if (bildchen != null)
            {
                bildchen.BName = zielDateiFullName;
                bildchen.BildFürLinks = true;

                OnPropertyChanged(nameof(SelectedBildchen));
                OnPropertyChanged(nameof(CountBildchenFürLinks));
            }

            // Daran hängt BTN_VerschieberRückgängigMachen und Strg+Z.
            BildchenVorher = zielDateiFullName;

            // Weiter zum nächsten noch nicht weggelegten Bild — wie bei ↓ und K.
            //
            // VOR dem Refresh, genau wie dort: MoveToNextNichtLinkesBild sucht über
            // BildFürLinks, und das ist oben schon gesetzt.
            MoveToNextNichtLinkesBild();

            AufgabenView.Refresh();
        }

        #endregion

        #region Command Alle Bilder SHA256 Abgleich prüfen
        private bool CanExecuteAlleBilderSHA256AbgleichPrüfen()
        {
            return OcAufgabens.Count > 1 && (!PrüfungLäuft) && (!IndexLaeuft) /*&& (!MultiByteParallelGleichheit)*/;
        }

        [RelayCommand(CanExecute = nameof(CanExecuteAlleBilderSHA256AbgleichPrüfen), IncludeCancelCommand = true)]
        private async Task CommandExecuteAlleBilderSHA256AbgleichPrüfen(CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                PrüfungLäuft = true;

                await MeTa_AlleBilderSHA256AbgleichPrüfenAsync(token);
            }
            catch (Exception)
            {

            }
            finally
            {
                PrüfungLäuft = false;
                LabelDropContent = "Gs  " + sw.Elapsed.TotalSeconds.ToString("F2") + " Sek";
                PercentageValueVerschieben = 0.0;
                AufgabenView.Refresh();
            }

        }

        private async Task MeTa_AlleBilderSHA256AbgleichPrüfenAsync(CancellationToken token)
        {
            // throw new NotImplementedException();
            // 2054

            var sw = Stopwatch.StartNew();
            DateTime started = DateTime.Now;
            IProgress<CLProgressStückzahl> progressStück = new Progress<CLProgressStückzahl>(value =>
            {
                // Wird auf dem UI-Thread ausgeführt – sichere, zentrale Aktualisierung
                PercentageValueVerschieben = value.Percent;
                LabelDropContent = "Rest " + value.Restzeit + "  ( " + value.StückPerSecond.ToString("F0") + " Stk/Sek )";
            });



            var bilder = OcAufgabens.ToList();
            long gszähler = bilder.Count;
            int zähler = 0;
            CountInnerZählerTest = 1;

            if (MultiByteParallelGleichheit)
            {
                //  return;
                //
                // Aufgabe Paralleler Byte Vergleich:
                // Alle Bilder in der Collection mit dem ausgewählten Bild vergleichen und die Bilder entfernen,
                // die nicht gleich sind. Fortschrittsanzeige mit Prozent und Restzeit.

                var pcCount = Environment.ProcessorCount;
                var results = new ConcurrentBag<CLSHA256Bild>();


                int total = (int)bilder.Count;
                object progressLock = new object();
                int lastPercent = 0;


                CountInnerZählerTest = 1;
                await Parallel.ForEachAsync(bilder, new ParallelOptions { MaxDegreeOfParallelism = pcCount }, async (filep, _) =>
                {

                    string hash2 = await MieneServices.GetFileHashSHA256Async(filep.BName, token);
                    var cl = new CLSHA256Bild();
                    cl.Name = filep.BName;
                    cl.Hash = hash2;
                    cl.PositionAnzeige = AufgabenView.IndexOf(filep);
                    results.Add(cl);


                    int current = Interlocked.Increment(ref zähler);
                    int percent = (int)((double)current / total * 100);

                    bool shouldReport = false;
                    lock (progressLock)
                    {
                        if (percent > lastPercent)
                        {
                            lastPercent = percent;
                            shouldReport = true;
                        }
                    }

                    if (shouldReport)
                    {
                        CountInnerZählerTest++;
                        var pgs = new CLProgressStückzahl(started, total, current, false);
                        progressStück?.Report(pgs);

                    }
                });


                // Paralleler Vergleich der Hashes
                zähler = 0;
                total = results.Count * results.Count + results.Count;
                CountInnerZählerTest = 1;
                var pgsL = new CLProgressStückzahl(started, 100, (long)(100D / 3D), false);
                progressStück?.Report(pgsL);

                var results2 = new ConcurrentBag<CLSHA256Bild>();
                await Task.Run(async () =>
                {
                    foreach (var item in results)
                    {

                        // Leider ohne Fortschrittsanzeige
                        //await Parallel.ForEachAsync(results, new ParallelOptions { MaxDegreeOfParallelism = pcCount-1 }, async (cl, _) =>
                        //{
                        //    if (item.Name != cl.Name)
                        //    {
                        //        if (item.Hash == cl.Hash)
                        //        {
                        //            if (!results2.Any(r => r.Name == item.Name))
                        //            {
                        //                results2.Add(cl);
                        //            }

                        //            if (!results2.Any(r => r.Name == item.Name))
                        //            {
                        //                results2.Add(item);
                        //            }
                        //        }
                        //    }

                        foreach (var cl in results)
                        {
                            if (item.Name != cl.Name)
                            {
                                if (item.Hash == cl.Hash)
                                {
                                    if (!results2.Any(r => r.Name == item.Name))
                                    {
                                        results2.Add(cl);
                                    }

                                    if (!results2.Any(r => r.Name == item.Name))
                                    {
                                        results2.Add(item);
                                    }
                                }
                            }
                        }

                        int current = Interlocked.Increment(ref zähler);
                        int percent = (int)((double)current / total * 100);

                        bool shouldReport = false;
                        lock (progressLock)
                        {
                            if (percent > lastPercent)
                            {
                                lastPercent = percent;
                                shouldReport = true;
                            }
                        }

                        if (shouldReport)
                        {
                            CountInnerZählerTest++;
                            var pgs = new CLProgressStückzahl(started, total, current, false);
                            progressStück?.Report(pgs);
                        }
                    }
                }, token);


                pgsL = new CLProgressStückzahl(started, 100, (long)(2D / 3D * 100D), false);
                progressStück?.Report(pgsL);


                CountInnerZählerTest = 0;
                // Bildchen aus der Collection entfernen

                // Rückwärts durchlaufen, damit Indizes nicht verschoben werden
                for (int i = OcAufgabens.Count - 1; i >= 0; i--)
                {
                    //MeinBildchen item = OcAufgabens[i];
                    MeinBildchen item = OcAufgabens.ElementAt(i);
                    var gh = item.BName;

                    if (!results2.Any(r => r.Name == gh))
                    {
                        // Bildchen aus der Collection entfernen
                        OcAufgabens.Remove(item);
                    }

                }

                pgsL = new CLProgressStückzahl(started, 100, (long)(3D / 3D * 100D), false);
                progressStück?.Report(pgsL);




            }
        }
        #endregion

        #region Bildersuche (Index-Leiste & Filter-Popover)

        /// <summary>Analysiert das gewählte Bild per CLIP (erkennt Begriffe).</summary>
        private readonly BildAnalyseService _bildAnalyse = new();

        /// <summary>True, wenn die schlanke Such-/Index-Leiste eingeblendet ist.</summary>
        [ObservableProperty]
        public partial bool IsSuchleisteOffen { get; set; }

        /// <summary>True, wenn die Schnell-Liste (alle Miniaturen im Popup) eingeblendet ist.</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildListeToggleCommand))]
        public partial bool IsBildListeOffen { get; set; }

        /// <summary>Die Zeilen der Schnell-Liste (je <see cref="BildListeSpalten"/> Kacheln) – ermöglicht zeilenweise Virtualisierung.</summary>
        [ObservableProperty]
        public partial ObservableCollection<System.Collections.Generic.IReadOnlyList<Bildersuche.BildListeItem>> BildListeZeilen { get; set; } = new();

        /// <summary>Die Zeile, die das aktuell gewählte Bild enthält (zum Sichtbar-Scrollen).</summary>
        [ObservableProperty]
        public partial System.Collections.Generic.IReadOnlyList<Bildersuche.BildListeItem>? AktuelleZeile { get; set; }

        /// <summary>Feste Spaltenzahl pro Zeile (die Popup-Breite ist fix).</summary>
        private const int BildListeSpalten = 5;

        /// <summary>True, während die Vorschaubilder der Schnell-Liste im Hintergrund laden.</summary>
        [ObservableProperty]
        public partial bool BildListeLaedt { get; set; }

        private CancellationTokenSource? _bildListeCts;

        /// <summary>True, sobald die Schnell-Liste einmal vollständig geladen wurde.</summary>
        private bool _bildListeGeladen;

        /// <summary>True, wenn sich die Bilderliste seither geändert hat (Cache verwerfen).</summary>
        private bool _bildListeVeraltet;

        // Beim Schließen (Button, Klick außerhalb, Kachelklick) das Befüllen abbrechen.
        partial void OnIsBildListeOffenChanged(bool value)
        {
            if (!value)
            {
                _bildListeCts?.Cancel();
            }
        }

        /// <summary>Kurzstatus der Bildanalyse (z. B. „Analysiere…", „6 Begriffe erkannt").</summary>
        [ObservableProperty]
        public partial string AnalyseStatus { get; set; } = string.Empty;

        /// <summary>True während die Analyse läuft (für einen Ladehinweis).</summary>
        [ObservableProperty]
        public partial bool AnalyseLaeuft { get; set; }

        /// <summary>Vorschaubild das gerade analysiert wurde (für die Anzeige im Popup).</summary>
        [ObservableProperty]
        public partial ImageSource? AnalyseBildVorschau { get; set; }

        /// <summary>Heatmap-Overlay (halbtransparent) über der Vorschau — zeigt wo der Begriff erkannt wurde.</summary>
        [ObservableProperty]
        public partial ImageSource? HeatmapOverlay { get; set; }

        /// <summary>True während die Heatmap berechnet wird.</summary>
        [ObservableProperty]
        public partial bool HeatmapLaeuft { get; set; }

        [ObservableProperty]
        public partial bool FilterLaeuft { get; set; }

        [ObservableProperty]
        public partial int SerieFortschritt { get; set; }

        /// <summary>True während Suche/BFS (Marquee), False während Thumbnails laden (echter %-Balken).</summary>
        [ObservableProperty]
        public partial bool SerieIndeterminate { get; set; }


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteErweiterteSerieSucheCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteDublettenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteSchemaAehnlichCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteFavSortierenCommand))]
        public partial bool SerieSucheLaeuft { get; set; }

        /// <summary>Die erkannten Begriffe des aktuellen Bildes (z. B. „Blume 34 %").</summary>
        public ObservableCollection<string> ErkannteBegriffe { get; } = new();

        /// <summary>Treffer der Freitextsuche (klickbare Miniaturen), nach Schwelle gefiltert.</summary>
        public ObservableCollection<SuchErgebnis> SuchErgebnisse { get; } = new();

        /// <summary>Alle Top-Treffer der letzten Suche (ungefiltert, mit Score) für das Live-Filtern.</summary>
        private readonly System.Collections.Generic.List<(SuchErgebnis Erg, float Score)> _alleSuchTreffer = new();

        /// <summary>
        /// True, sobald ein Treffersatz zum Filtern vorliegt. Steuert die Sichtbarkeit der
        /// Schwellen-Slider und bleibt beim Schieben stabil — anders als
        /// <see cref="SuchErgebnisse"/>.Count, das beim Hochziehen auf 0 fällt und den
        /// Slider sonst mitsamt seiner Karte ausblenden würde (er könnte nicht mehr
        /// zurückgezogen werden).
        /// </summary>
        [ObservableProperty]
        public partial bool HatTrefferCache { get; set; }

        /// <summary>
        /// True, wenn die Trefferliste wegen eines Ordnerwechsels verworfen wurde. Färbt
        /// IC_SuchErgebnisse ein, damit der Grund auch dann erkennbar ist, wenn die
        /// Suchleiste erst später über BTN_IndexSuchleiste wieder geöffnet wird.
        /// </summary>
        [ObservableProperty]
        public partial bool SuchErgebnisseVeraltet { get; set; }

        /// <summary>
        /// Restzeit lesbar aufbereiten: unter einer Minute in Sekunden, darüber in
        /// Minuten und Sekunden. „462 s“ sagt niemandem etwas, „7 min 42 s“ schon.
        /// Liefert leer, wenn keine sinnvolle Schätzung vorliegt.
        /// </summary>
        private static string FormatiereRestzeit(int sekunden)
        {
            if (sekunden <= 0)
            {
                return string.Empty;
            }

            if (sekunden < 60)
            {
                return $"{sekunden} s";
            }

            int minuten = sekunden / 60;
            int rest = sekunden % 60;

            return rest > 0 ? $"{minuten} min {rest} s" : $"{minuten} min";
        }

        /// <summary>Leert den Treffer-Cache und meldet den Zustand an die Oberfläche.</summary>
        private void LeereTrefferCache()
        {
            _alleSuchTreffer.Clear();
            HatTrefferCache = false;

            // Jede neue Suche ist keine FS-Sortierung mehr – sonst filterte der Regler
            // die frischen Treffer mit dem falschen Renderer.
            ErgebnisseSindFavSortierung = false;

            // Jeder Suchlauf beginnt hiermit → Einfärbung des letzten Wechsels aufheben.
            SuchErgebnisseVeraltet = false;

            // Ebenso der Wort-Hinweis: Er gehört zur Freitextsuche. Bliebe er stehen,
            // behauptete er bei einer Schema- oder Seriensuche etwas über Wörter, die
            // dort gar nicht vorkamen.
            SuchWortHinweisWoerter = string.Empty;
            SuchWortHinweisText = string.Empty;
        }

        /// <summary>
        /// Verwirft die angezeigten Suchtreffer samt Cache. Wird beim Ordnerwechsel per
        /// Drop gerufen: Treffer aus dem alten Ordner würden sonst beim Anklicken dorthin
        /// zurückspringen.
        /// </summary>
        /// <param name="grund">
        /// Meldung für die Statuszeile, wenn tatsächlich Treffer verworfen wurden. Ohne
        /// Angabe gilt der Ordnerwechsel als Grund.
        /// </param>
        private void VerwerfeSuchtreffer(string? grund = null)
        {
            bool hatteTreffer = SuchErgebnisse.Count > 0 || _alleSuchTreffer.Count > 0;

            SuchErgebnisse.Clear();
            LeereTrefferCache();
            ErgebnisseSindSchemaAehnlich = false;
            _letzteFrage = string.Empty;

            if (hatteTreffer)
            {
                SucheStatus = grund ?? "Neuer Ordner geladen – Suche bitte wiederholen.";

                // Nach LeereTrefferCache() setzen, das die Markierung zurücknimmt.
                SuchErgebnisseVeraltet = true;
            }

            CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
        }

        /// <summary>Letzte Suchanfrage (für die Statuszeile beim Neu-Filtern).</summary>
        private string _letzteFrage = string.Empty;

        /// <summary>
        /// Fortschritt der Freitextsuche in Prozent für die dünne Linie am unteren Rand
        /// des Suchfelds (PGB_SuchfeldLinie).
        /// </summary>
        [ObservableProperty]
        public partial int SuchfeldFortschritt { get; set; }

        /// <summary>
        /// True, solange die Dauer unbekannt ist (Modell laden, Index abfragen) — dann
        /// läuft die Linie als Marquee. Beim Laden der Miniaturen wird auf echten
        /// Prozent-Fortschritt umgeschaltet.
        /// </summary>
        [ObservableProperty]
        public partial bool SuchfeldIndeterminate { get; set; } = true;

        /// <summary>Kurzstatus der Freitextsuche.</summary>
        [ObservableProperty]
        public partial string SucheStatus { get; set; } = string.Empty;

        /// <summary>Der aktuell hervorgehobene Begriff (für visuelles Feedback im Chip).</summary>
        [ObservableProperty]
        public partial string? AktuellerHeatmapBegriff { get; set; }

        /// <summary>True = Begriffe auf Deutsch anzeigen, False = englische Originale.</summary>
        [ObservableProperty]
        public partial bool BegriffeAufDeutsch { get; set; } = true;

        /// <summary>Letzte Roh-Ergebnisse (englisch) für erneutes Rendern bei Sprachwechsel.</summary>
        private System.Collections.Generic.IReadOnlyList<(string Word, float Score)> _letzteBegriffe =
            System.Array.Empty<(string, float)>();

        partial void OnBegriffeAufDeutschChanged(bool value) => RenderBegriffe();


        /// <summary>Standardwert der Tag-Schwelle (Reset-Button).</summary>
        public const double TagSchwelleStandard = 0.23;

        /// <summary>
        /// Schwelle für die Auto-Tags (0..1).
        /// </summary>
        [ObservableProperty]
        public partial double TagSchwelle { get; set; } = TagSchwelleStandard;

        partial void OnTagSchwelleChanged(double value) => RenderBegriffe();

        /// <summary>Setzt die Tag-Schwelle auf den Standardwert zurück.</summary>
        [RelayCommand]
        private void CommandExecuteTagSchwelleZuruecksetzen() => TagSchwelle = TagSchwelleStandard;

        /// <summary>
        /// Füllt <see cref="ErkannteBegriffe"/> aus den Roh-Treffern, gefiltert nach Schwelle und Sprache.
        /// </summary>
        private void RenderBegriffe()
        {
            ErkannteBegriffe.Clear();
            foreach (var (wort, score) in _letzteBegriffe)
            {
                if (score < TagSchwelle)
                {
                    continue;
                }

                string anzeige = BegriffeAufDeutsch ? BegriffUebersetzer.ZuDeutsch(wort) : wort;
                ErkannteBegriffe.Add($"{anzeige}  {score * 100f:F0} %");
            }
            HeatmapOverlay = null;
            AktuellerHeatmapBegriff = null;
        }

        [RelayCommand]
        private async Task CommandExecuteBegriffHeatmap(string? chipText)
        {
            if (string.IsNullOrEmpty(chipText))
            {
                return;
            }

            string? pfad = SelectedBildchen?.BName;
            if (string.IsNullOrEmpty(pfad))
            {
                return;
            }

            // Aus dem Chip-Text den englischen Begriff extrahieren (Format: "Begriff  42 %")
            string anzeigeName = chipText.Contains("  ")
                ? chipText.Substring(0, chipText.LastIndexOf("  "))
                : chipText;
            string englisch = BegriffeAufDeutsch
                ? _letzteBegriffe.FirstOrDefault(b => BegriffUebersetzer.ZuDeutsch(b.Word) == anzeigeName).Word ?? anzeigeName
                : anzeigeName;

            if (AktuellerHeatmapBegriff == chipText)
            {
                HeatmapOverlay = null;
                AktuellerHeatmapBegriff = null;
                return;
            }

            AktuellerHeatmapBegriff = chipText;
            HeatmapLaeuft = true;
            try
            {
                var scores = await _bildAnalyse.HeatmapAsync(pfad, englisch, gridSize: 4);
                if (scores == null)
                { HeatmapOverlay = null; return; }

                // Seitenverhältnis des Originals übernehmen, damit Overlay bei Uniform passt.
                double aspW = 1, aspH = 1;
                if (AnalyseBildVorschau is BitmapSource bmpSrc && bmpSrc.PixelWidth > 0 && bmpSrc.PixelHeight > 0)
                { aspW = bmpSrc.PixelWidth; aspH = bmpSrc.PixelHeight; }
                HeatmapOverlay = ErzeugeHeatmapBild(scores, aspW, aspH);
            }
            finally { HeatmapLaeuft = false; }
        }

        [RelayCommand(IncludeCancelCommand = true)]
        private async Task CommandExecuteBegriffSuche(string? chipText, CancellationToken token)
        {
            if (string.IsNullOrEmpty(chipText))
            {
                return;
            }

            string anzeigeName = chipText.Contains("  ")
                ? chipText.Substring(0, chipText.LastIndexOf("  "))
                : chipText;
            string englisch = BegriffeAufDeutsch
                ? _letzteBegriffe.FirstOrDefault(b => BegriffUebersetzer.ZuDeutsch(b.Word) == anzeigeName).Word ?? anzeigeName
                : anzeigeName;

            string? ordner = Path.GetDirectoryName(SelectedBildchen?.BName);
            if (string.IsNullOrEmpty(ordner))
            {
                return;
            }

            // Auch hier: Sobald gesucht wird, gehört der Platz den Ergebnissen.
            SchliesseIndexOrdnerKarte();

            SuchErgebnisse.Clear();
            LeereTrefferCache();
            SucheStatus = $"Suche alle Bilder mit '{anzeigeName}'…";

            // Fortschrittsleiste GRD_SerieFortschritt mitbenutzen: erst Marquee für die
            // Index-Abfrage, danach echter Balken fürs Laden der Miniaturen.
            SerieFortschritt = 0;
            SerieIndeterminate = true;
            SerieSucheLaeuft = true;

            try
            {
                var pfade = await _bildAnalyse.SucheNachKonzeptAsync(ordner, englisch);

                token.ThrowIfCancellationRequested();

                // Karteileichen des Index aussortieren (siehe NurVorhandene).
                pfade = pfade.Where(File.Exists).ToList();

                if (pfade.Count == 0)
                {
                    SucheStatus = $"Kein Bild mit '{anzeigeName}' im Index gefunden.";
                    return;
                }

                _letzteFrage = anzeigeName;

                // Ab hier echter Prozent-Fortschritt: Miniaturen einzeln laden, damit
                // Balken und Restzeit den langsamen Teil abbilden.
                SerieIndeterminate = false;

                var ergebnisse = new System.Collections.Generic.List<SuchErgebnis>(pfade.Count);
                var uhr = System.Diagnostics.Stopwatch.StartNew();

                for (int i = 0; i < pfade.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    string p = pfade[i];
                    var thumb = await Task.Run(() => LadeThumb(p), token);

                    ergebnisse.Add(new SuchErgebnis
                    {
                        Path = p,
                        DateiName = Path.GetFileName(p),
                        ProzentText = "✓",
                        Thumb = thumb
                    });

                    int fertig = i + 1;
                    SerieFortschritt = (int)(fertig * 100.0 / pfade.Count);

                    // SchaetzeRestzeit liegt in AufgabeViewModel.ByteDubletten.cs (gleiche partial-Klasse).
                    string rest = SchaetzeRestzeit(uhr.Elapsed, fertig, pfade.Count);
                    SucheStatus = $"'{anzeigeName}': {fertig} / {pfade.Count}"
                        + (rest.Length > 0 ? " – " + rest : string.Empty);
                }

                await FuegeErgebnisseEinAsync(ergebnisse.Select(e => (e, 1f)).ToList());

                SucheStatus = $"{pfade.Count} Bilder mit '{anzeigeName}'.";
                CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
            }
            catch (OperationCanceledException)
            {
                SucheStatus = $"Suche nach '{anzeigeName}' abgebrochen.";
            }
            finally
            {
                SerieSucheLaeuft = false;
            }
        }


        private static ImageSource ErzeugeHeatmapBild(float[,] scores, double bildBreite = 1, double bildHoehe = 1)
        {
            int rows = scores.GetLength(0);
            int cols = scores.GetLength(1);

            // Zellgröße so wählen, dass das Seitenverhältnis des Originals erhalten bleibt.
            double aspect = bildBreite / bildHoehe;
            int cellW, cellH;
            if (aspect >= 1)
            { cellW = 64; cellH = (int)(64 / aspect); }
            else
            { cellH = 64; cellW = (int)(64 * aspect); }
            if (cellW < 4)
            {
                cellW = 4;
            }

            if (cellH < 4)
            {
                cellH = 4;
            }

            int w = cols * cellW;
            int h = rows * cellH;

            // Min/Max normieren für besseren Kontrast
            float min = float.MaxValue, max = float.MinValue;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (scores[r, c] < min)
                    {
                        min = scores[r, c];
                    }

                    if (scores[r, c] > max)
                    {
                        max = scores[r, c];
                    }
                }
            }

            float range = max - min;
            if (range < 0.001f)
            {
                range = 1f;
            }

            var pixels = new byte[w * h * 4]; // BGRA
            for (int r = 0; r < rows; r++)
            {
                float norm = (scores[r, 0] - min) / range; // will be set per col
                for (int c = 0; c < cols; c++)
                {
                    norm = (scores[r, c] - min) / range;
                    // Farbe: transparent(niedrig) → rot halbtransparent(hoch)
                    byte alpha = (byte)(norm * 160);
                    byte red = (byte)(200 + norm * 55);
                    byte green = (byte)((1f - norm) * 80);
                    byte blue = 0;

                    for (int py = r * cellH; py < (r + 1) * cellH; py++)
                    {
                        for (int px = c * cellW; px < (c + 1) * cellW; px++)
                        {
                            int i = (py * w + px) * 4;
                            pixels[i] = blue;
                            pixels[i + 1] = green;
                            pixels[i + 2] = red;
                            pixels[i + 3] = alpha;
                        }
                    }
                }
            }

            var bmp = BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, w * 4);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// True, wenn das Filter-Popover aufgeklappt ist.
        /// </summary>
        [ObservableProperty]
        public partial bool IsIndexPopoverOffen { get; set; }

        // Schließt die Leiste (auch per Klick daneben) → Einstellungen mit einklappen.
        partial void OnIsSuchleisteOffenChanged(bool value)
        {
            if (!value)
                IsIndexPopoverOffen = false;
        }

        /// <summary>
        /// Freitext für die Bildersuche (z. B. „Mann mit blauem Hut")
        /// </summary>
        [ObservableProperty]
        public partial string SucheText { get; set; } = string.Empty;

        /// <summary>
        /// Grauer Ghost-Rest der Autovervollständigung (nach dem Getippten).
        /// </summary>
        [ObservableProperty]
        public partial string SucheVorschlagRest { get; set; } = string.Empty;

        /// <summary>
        /// True während das CLIP-Modell (einmalig) geladen wird.
        /// </summary>
        [ObservableProperty]
        public partial bool ClipLaedt { get; set; }

        public ObservableCollection<string> SucheVorschlaege { get; } = new();

        [ObservableProperty]
        public partial bool VorschlaegeOffen { get; set; }

        partial void OnSucheTextChanged(string value)
        {
            string rest = string.Empty;
            SucheVorschlaege.Clear();

            if (!string.IsNullOrEmpty(value) && !value.EndsWith(" "))
            {
                int sp = value.LastIndexOf(' ');
                string letztes = sp >= 0 ? value[(sp + 1)..] : value;
                if (letztes.Length > 0)
                {
                    string? treffer = BegriffUebersetzer.Vervollstaendige(letztes);
                    if (treffer != null)
                        rest = treffer[letztes.Length..];

                    foreach (string v in BegriffUebersetzer.AlleVorschlaege(letztes))
                        SucheVorschlaege.Add(v);
                }
            }

            SucheVorschlagRest = rest;
            VorschlaegeOffen = SucheVorschlaege.Count > 0;
        }

        [RelayCommand]
        private void CommandExecuteVorschlagUebernehmen()
        {
            if (!string.IsNullOrEmpty(SucheVorschlagRest))
            {
                SucheText += SucheVorschlagRest;
                SucheVorschlagRest = string.Empty;
            }
        }

        [RelayCommand]
        private void CommandExecuteVorschlagGewaehlt(string wort)
        {
            if (string.IsNullOrEmpty(wort))
            {
                return;
            }

            int sp = SucheText.LastIndexOf(' ');
            string prefix = sp >= 0 ? SucheText[..(sp + 1)] : "";
            SucheText = prefix + wort + " ";
            SucheVorschlaege.Clear();
            VorschlaegeOffen = false;
            SucheVorschlagRest = string.Empty;
        }

        /// <summary>Hinweis während des Modellstarts – als Konstante, damit er sicher wiedererkannt wird.</summary>
        private const string ClipLadeHinweis = "Lade KI-Modell … (einmalig beim ersten Mal, dauert einen Moment)";

        /// <summary>Stellt sicher, dass CLIP geladen ist; zeigt dabei das Lade-Symbol.</summary>
        private async Task StelleClipBereitAsync()
        {
            if (_bildAnalyse.Bereit)
            {
                return;
            }

            // Der einmalige Modellstart dauert spürbar. Der Hinweis geht zusätzlich in
            // die Statuszeile unter den Buttons – STP_ClipLaedt sitzt weiter unten im
            // Panel und wird übersehen, wenn der Blick auf dem geklickten Knopf liegt.
            ClipLaedt = true;
            SucheStatus = ClipLadeHinweis;
            try
            { await _bildAnalyse.StelleSicherGeladenAsync(); }
            finally
            {
                ClipLaedt = false;

                // Den eigenen Hinweis selbst zurücknehmen: Nur einer der sechs Aufrufer
                // setzt danach eigenen Text, sonst bliebe „Lade KI-Modell …" stehen,
                // während das Lade-Symbol darunter längst weg ist.
                //
                // Der Vergleich räumt nur den eigenen Text weg — hat inzwischen jemand
                // anders etwas gesetzt, bleibt das stehen.
                if (SucheStatus == ClipLadeHinweis)
                {
                    SucheStatus = string.Empty;
                }
            }
        }

        /// <summary>
        /// Messzeile zum letzten Indexlauf: wie viel Zeit ins Laden von der Platte ging
        /// und wie viel ins Rechnen. Grundlage für die Frage, ob sich ein Überlappen von
        /// Laden und Rechnen lohnt — auf einer langsamen Platte sieht das anders aus als
        /// auf einer schnellen M.2.
        ///
        /// Leer, wenn nichts neu verarbeitet wurde (alle Bilder standen schon im Index).
        /// </summary>
        private string IndexMessung()
        {
            int bilder = _bildAnalyse.LetzteVerarbeiteteBilder;
            if (bilder <= 0)
            {
                return string.Empty;
            }

            double gesamt = _bildAnalyse.LetzteIndexDauer.TotalSeconds;
            double laden = _bildAnalyse.LetzteLadeDauer.TotalSeconds;
            double proBildMs = gesamt / bilder * 1000.0;
            double proSek = gesamt > 0.001 ? bilder / gesamt : 0;

            // „Laden" ist über alle Fäden aufsummiert und deshalb nicht mit der
            // Gesamtzeit verrechenbar – bei acht gleichzeitigen Bildern kann es
            // grösser sein als die Wanduhrzeit. Darum als eigener Wert ausgewiesen,
            // nicht als Anteil.
            return $"  |  Messung: {bilder} neu · {_bildAnalyse.LetzteParallelitaet} gleichzeitig · "
                 + $"gesamt {gesamt:F1} s · {proBildMs:F0} ms/Bild · {proSek:F1} Bilder/s · "
                 + $"Laden {laden:F1} s (Summe aller Fäden)";
        }

        /// <summary>Anzahl der indexierten Bilder, z. B. „1140 Bilder im Index".</summary>
        [ObservableProperty]
        public partial string IndexAnzahlText { get; set; } = "0 Bilder im Index";

        /// <summary>Ordner-Fortschritt, z. B. „indexiert 3/3 Ordner".</summary>
        [ObservableProperty]
        public partial string IndexOrdnerText { get; set; } = "indexiert 0/0 Ordner";



        /// <summary>Mindest-Ähnlichkeit der Suchtreffer in Prozent (0..100).</summary>
        [ObservableProperty]
        public partial double MindestAehnlichkeit { get; set; } = MindestAehnlichkeitStandard;

        /// <summary>Standardwert der Mindest-Ähnlichkeit in Prozent (Reset-Button).</summary>
        public const double MindestAehnlichkeitStandard = 23;

        /// <summary>Setzt die Mindest-Ähnlichkeit auf den Standardwert zurück.</summary>
        [RelayCommand]
        private void CommandExecuteMindestSchwelleZuruecksetzen()
            => MindestAehnlichkeit = MindestAehnlichkeitStandard;

        // Slider bewegt → gecachte Treffer neu filtern (ohne erneute Suche).
        // Nicht wenn gerade Schema-ähnlich-Treffer aktiv sind – die filtert ihr eigener Slider.
        partial void OnMindestAehnlichkeitChanged(double value)
        {
            if (!ErgebnisseSindSchemaAehnlich && _alleSuchTreffer.Count > 0)
                RenderSuchErgebnisse();
        }

        /// <summary>True während der Ordner indexiert wird.</summary>
        [ObservableProperty]
        // Sperrt die schweren Abgleiche, solange indexiert wird — Gegenstück zu der
        // Prüfung auf PrüfungLäuft in CanExecuteOrdnerIndexieren.
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteSuchenGleichesBildByteVergleichCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteSuchenUngefährGleichesBildCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteAlleBilderMiteinanderAufByteGleichheitPrüfenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteAlleBilderSHA256AbgleichPrüfenCommand))]
        // Und die Verschiebe-Befehle: Was während des Indexierens wegwandert, steht
        // hinterher falsch im gerade geschriebenen Index.
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteAlleBilderInsKeinFavVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildInsKeinFavVerzeichnisVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildInsBesondersVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildInsKIFehlerVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteVerschiebenZurückCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteFavSortierenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteOrdnerEineEbeneHochCommand))]
        public partial bool IndexLaeuft { get; set; }

        /// <summary>Fortschritt der Indexierung in Prozent (0..100).</summary>
        [ObservableProperty]
        public partial double IndexFortschritt { get; set; }

        /// <summary>Fortschritts-/Ergebnistext der Indexierung.</summary>
        [ObservableProperty]
        public partial string IndexFortschrittText { get; set; } = string.Empty;

        /// <summary>Filter-Kategorien (z. B. „Erkannt", „Ort" …).</summary>
        public ObservableCollection<string> FilterKategorien { get; } = new();

        [ObservableProperty]
        public partial string? SelectedFilterKategorie { get; set; }

        /// <summary>Mögliche Werte zur gewählten Kategorie (z. B. „flower").</summary>
        public ObservableCollection<string> FilterWerte { get; } = new();

        [ObservableProperty]
        public partial string? SelectedFilterWert { get; set; }

        private System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<string>> _tagOptionen = new();

        partial void OnSelectedFilterKategorieChanged(string? value)
        {
            FilterWerte.Clear();
            SelectedFilterWert = null;
            if (value != null && _tagOptionen.TryGetValue(value, out var werte))
            {
                foreach (var w in werte)
                    FilterWerte.Add(w);
            }
        }

        partial void OnSelectedFilterWertChanged(string? value)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(SelectedFilterKategorie))
                return;
            _ = FilterSucheAusfuehrenAsync(SelectedFilterKategorie, value);
        }

        private async Task FilterSucheAusfuehrenAsync(string kategorie, string wert)
        {
            string? pfad = SelectedBildchen?.BName;
            string? ordner = string.IsNullOrEmpty(pfad) ? null : Path.GetDirectoryName(pfad);
            if (string.IsNullOrEmpty(ordner))
            {
                return;
            }

            SuchErgebnisse.Clear();
            LeereTrefferCache();
            string anzeige = $"{kategorie}: {wert}";
            SucheStatus = $"Filtere '{anzeige}'…";
            FilterLaeuft = true;
            try
            {
                var treffer = await _bildAnalyse.SucheNachFilterAsync(ordner, kategorie, wert);

                // Karteileichen des Index aussortieren (siehe NurVorhandene).
                treffer = treffer.Where(File.Exists).ToList();

                if (treffer.Count == 0)
                {
                    SucheStatus = $"Keine Bilder mit '{anzeige}' im Index.";
                    return;
                }

                _letzteFrage = anzeige;

                var ergebnisse = await Task.Run(() =>
                    treffer.Select(p => new SuchErgebnis
                    {
                        Path = p,
                        DateiName = Path.GetFileName(p),
                        ProzentText = "✓",
                        Thumb = LadeThumb(p)
                    }).ToList());

                await FuegeErgebnisseEinAsync(ergebnisse.Select(e => (e, 1f)).ToList());

                SucheStatus = $"{treffer.Count} Bilder mit '{anzeige}'.";
                CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
            }
            finally { FilterLaeuft = false; }
        }

        private void AktualisiereFilterOptionen()
        {
            string? pfad = SelectedBildchen?.BName;
            string? ordner = string.IsNullOrEmpty(pfad) ? null : Path.GetDirectoryName(pfad);
            if (string.IsNullOrEmpty(ordner))
            {
                return;
            }

            _tagOptionen = _bildAnalyse.LadeFilterOptionen(ordner);
            FilterKategorien.Clear();
            foreach (var k in _tagOptionen.Keys)
            {
                FilterKategorien.Add(k);
            }

            if (FilterKategorien.Count > 0)
            {
                SelectedFilterKategorie = FilterKategorien[0];
            }
        }

        // Der unscheinbare Button ist nur klickbar, wenn ein Bild ausgewählt ist.
        private bool CanExecuteSuchleisteToggle() => SelectedBildchen != null;

        [RelayCommand(CanExecute = nameof(CanExecuteSuchleisteToggle))]
        private async Task CommandExecuteSuchleisteToggle()
        {
            IsSuchleisteOffen = !IsSuchleisteOffen;
            if (!IsSuchleisteOffen)
            {
                IsIndexPopoverOffen = false; // Popover mit der Leiste schließen
                return;
            }

            AktualisiereFilterOptionen();
            await AnalysiereAktuellesBildAsync();
        }

        /// <summary>
        /// Die Schnell-Liste füllt sich aus <c>AufgabenView</c>, also gefiltert. Ist die
        /// Ansicht leer, öffnete der Knopf ein leeres Kachelpanel.
        ///
        /// <c>IsBildListeOffen ||</c> ist kein Beiwerk, sondern die Falltür: Ohne diesen
        /// Teil bliebe ein bereits offenes Panel offen und der Knopf gesperrt, sobald ein
        /// Filter alles ausblendet — zumachen ginge dann nicht mehr.
        /// </summary>
        private bool CanExecuteBildListeToggle() =>
            IsBildListeOffen || (AufgabenView is not null && !AufgabenView.IsEmpty);

        /// <summary>Blendet die Schnell-Liste (alle Miniaturen im Popup) ein/aus.</summary>
        [RelayCommand(CanExecute = nameof(CanExecuteBildListeToggle))]
        private async Task CommandExecuteBildListeToggle()
        {
            IsBildListeOffen = !IsBildListeOffen;
            if (!IsBildListeOffen)
            {
                _bildListeCts?.Cancel(); // laufendes Befüllen abbrechen
                return;
            }

            await FuelleBildListeAsync();
        }

        /// <summary>
        /// Befüllt die Schnell-Liste: erst werden alle Kacheln leer angelegt (ein
        /// Layout-Durchgang, kein Reflow, sofort navigierbar), dann die Miniaturen im
        /// Hintergrund nachgeladen. Das Ergebnis wird gecacht – Wiederöffnen ist sofort
        /// da, solange sich die Bilderliste nicht geändert hat.
        /// </summary>
        private async Task FuelleBildListeAsync()
        {
            // Cache: unveränderte Liste nicht neu laden – nur zum aktuellen Bild markieren.
            // Wiederöffnen ist so sofort da, und die Ladephase passiert nur einmal.
            if (_bildListeGeladen && !_bildListeVeraltet)
            {
                MarkiereAktuellesBild();
                return;
            }

            _bildListeCts?.Cancel();
            _bildListeCts = new CancellationTokenSource();
            var token = _bildListeCts.Token;

            // Momentaufnahme in Ansichts-Reihenfolge (spiegelt verschobene/neue Bilder).
            var bilder = AufgabenView.Cast<MeinBildchen>().ToList();

            // 1) Kacheln anlegen und in Zeilen (feste Spaltenzahl) gruppieren.
            //    Die Liste virtualisiert zeilenweise -> nur sichtbare Zeilen werden erzeugt.
            var eintraege = bilder.Select(b => new Bildersuche.BildListeItem { Bild = b }).ToList();
            var zeilen = new ObservableCollection<System.Collections.Generic.IReadOnlyList<Bildersuche.BildListeItem>>();
            for (int i = 0; i < eintraege.Count; i += BildListeSpalten)
            {
                zeilen.Add(eintraege.GetRange(i, Math.Min(BildListeSpalten, eintraege.Count - i)));
            }
            BildListeZeilen = zeilen;

            // 2) Aktuelles Bild markieren und Zielzeile setzen.
            MarkiereAktuellesBild();

            // 3) Miniaturen im Hintergrund nachladen und in die bestehenden Kacheln setzen.
            BildListeLaedt = true;
            try
            {
                const int batch = 24;
                for (int i = 0; i < eintraege.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var eintrag = eintraege[i];
                    eintrag.Thumb = await Task.Run(() => LadeThumb(eintrag.Bild.BName), token);

                    if ((i + 1) % batch == 0)
                    {
                        await Task.Delay(1, token);
                    }
                }

                _bildListeGeladen = true;
                _bildListeVeraltet = false;
            }
            catch (OperationCanceledException)
            {
                // Popup geschlossen oder neu geöffnet – Cache bleibt „nicht geladen".
            }
            finally
            {
                BildListeLaedt = false;
            }
        }

        /// <summary>Markiert die Kachel des aktuellen Bildes und merkt sich deren Zeile zum Scrollen.</summary>
        private void MarkiereAktuellesBild()
        {
            AktuelleZeile = null;
            foreach (var zeile in BildListeZeilen)
            {
                foreach (var kachel in zeile)
                {
                    kachel.IsAktuell = kachel.Bild == SelectedBildchen;
                    if (kachel.IsAktuell)
                    {
                        AktuelleZeile = zeile;
                    }
                }
            }
        }

        /// <summary>Springt zum geklickten Bild und schließt die Schnell-Liste.</summary>
        [RelayCommand]
        private void CommandExecuteZuBildSpringen(MeinBildchen? bild)
        {
            if (bild == null)
            {
                return;
            }

            SelectedBildchen = bild;
            IsBildListeOffen = false;
        }

        /// <summary>Schickt das aktuell gewählte Bild durch CLIP und füllt <see cref="ErkannteBegriffe"/>.</summary>
        private async Task AnalysiereAktuellesBildAsync()
        {
            string? pfad = SelectedBildchen?.BName;
            if (string.IsNullOrEmpty(pfad) || !File.Exists(pfad))
            {
                AnalyseStatus = "Kein Bild ausgewählt.";
                ErkannteBegriffe.Clear();
                return;
            }

            AnalyseLaeuft = true;
            _letzteBegriffe = System.Array.Empty<(string, float)>();
            ErkannteBegriffe.Clear();

            // Kleine Vorschau des analysierten Bildes laden. Sie ist zugleich die Probe,
            // ob die Datei überhaupt ein Bild ist: Findet der Decoder hier nichts, findet
            // er in CLIP ebenso wenig — nur fliegt es dort erst nach dem Laden der
            // Modelle und mit einer Meldung, die die Ursache nicht verrät.
            bool bildLesbar = true;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(pfad);
                bmp.DecodePixelWidth = 72;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                AnalyseBildVorschau = bmp;
            }
            catch { AnalyseBildVorschau = null; bildLesbar = false; }

            try
            {
                if (!bildLesbar)
                {
                    AnalyseStatus = BildNichtLesbarText(pfad);
                    return;
                }

                await StelleClipBereitAsync();
                AnalyseStatus = "Analysiere…";
                var treffer = await _bildAnalyse.ErkenneAsync(pfad, minRelevance: 0.10f, topN: 20);

                if (!_bildAnalyse.Bereit)
                {
                    AnalyseStatus = "CLIP-Modelle nicht gefunden (models-Ordner).";
                    return;
                }

                _letzteBegriffe = treffer;
                RenderBegriffe();

                AnalyseStatus = treffer.Count == 0
                    ? "Nichts erkannt."
                    : $"{treffer.Count} Begriffe erkannt.";
            }
            catch (System.NotSupportedException)
            {
                // Dieselbe Ursache wie oben, nur bei einer Datei, aus der die Vorschau
                // noch etwas machen konnte — die Rohmeldung des Decoders („keine passende
                // Imagingkomponente") sagt niemandem, dass die Datei kaputt ist.
                AnalyseStatus = BildNichtLesbarText(pfad);
            }
            catch (Exception ex)
            {
                AnalyseStatus = "Fehler bei der Analyse: " + ex.Message;
            }
            finally
            {
                AnalyseLaeuft = false;
            }
        }

        /// <summary>
        /// Meldung für eine Datei, die eine Bildendung trägt, aber kein Bild ist.
        ///
        /// Häufigster Fall sind abgebrochene Downloads: In der Datei stehen dann ein paar
        /// Byte Fehlertext des Servers. Die Grösse gehört deshalb in die Meldung — sie
        /// verrät den Fall sofort, ohne dass jemand die Datei öffnen muss.
        /// </summary>
        private static string BildNichtLesbarText(string pfad)
        {
            long groesse = -1;
            try
            { groesse = new FileInfo(pfad).Length; }
            catch { }

            string menge = groesse < 0
                ? string.Empty
                : groesse < 1024
                    ? $" ({groesse} Byte)"
                    : $" ({groesse / 1024} KB)";

            return $"„{Path.GetFileName(pfad)}“ lässt sich nicht als Bild lesen{menge} – "
                   + "vermutlich ein abgebrochener Download. Die Analyse wurde übersprungen.";
        }

        /// <summary>
        /// Nicht indexieren, während ein Abgleich läuft.
        ///
        /// Beide belasten dieselbe Platte, und die gemeinsame Fortschrittsanzeige könnte
        /// nur einen von beiden zeigen — der andere liefe dann unsichtbar weiter. Bewusst
        /// nur gegen <c>PrüfungLäuft</c> geprüft und nicht gegen <c>IndexLaeuft</c>: Ein
        /// zweiter Start desselben Befehls ist schon dadurch ausgeschlossen, dass
        /// <c>AsyncRelayCommand</c> ohne <c>AllowConcurrentExecutions</c> währenddessen
        /// nicht ausführbar ist.
        /// </summary>
        private bool CanExecuteOrdnerIndexieren() => !PrüfungLäuft;

        [RelayCommand(CanExecute = nameof(CanExecuteOrdnerIndexieren), IncludeCancelCommand = true)]
        private async Task CommandExecuteOrdnerIndexieren(CancellationToken token)
        {
            string? pfad = SelectedBildchen?.BName;
            if (string.IsNullOrEmpty(pfad) || !File.Exists(pfad))
            {
                IndexFortschrittText = "Kein Bild ausgewählt.";
                return;
            }

            string? ordner = Path.GetDirectoryName(pfad);
            if (string.IsNullOrEmpty(ordner))
            {
                return;
            }

            IndexLaeuft = true;
            IndexFortschritt = 0;
            IndexFortschrittText = "Starte Indexierung…";

            // Restzeit aus der bisherigen Geschwindigkeit (fertige/gesamt) hochrechnen.
            var uhr = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                await StelleClipBereitAsync();

                var progress = new Progress<(int done, int total, string file)>(p =>
                {
                    IndexFortschritt = p.total > 0 ? 100.0 * p.done / p.total : 0;

                    int restSek = 0;
                    if (p.done > 0 && p.total > 0)
                    {
                        double proSek = p.done / Math.Max(uhr.Elapsed.TotalSeconds, 0.001);
                        restSek = (int)Math.Ceiling((p.total - p.done) / Math.Max(proSek, 0.001));
                    }
                    string restText = FormatiereRestzeit(restSek);
                    IndexFortschrittText = restText.Length > 0
                        ? $"Indexiere {p.done}/{p.total} – noch ~{restText}"
                        : $"Indexiere {p.done}/{p.total}…";
                });

                int anzahl = await _bildAnalyse.IndexiereOrdnerAsync(ordner, progress, token);

                if (!_bildAnalyse.Bereit)
                {
                    IndexFortschrittText = "CLIP-Modelle nicht gefunden (models-Ordner).";
                    return;
                }

                IndexAnzahlText = $"{anzahl} Bilder im Index";

                // Ordner ins Verzeichnis aufnehmen bzw. auffrischen. Grundlage für die
                // Suche über mehrere Ordner — und ersetzt den bisher fest verdrahteten
                // Text „indexiert 1/1 Ordner".
                Bildersuche.IndexOrdnerVerzeichnis.Merke(ordner, anzahl);
                AktualisiereIndexOrdner();

                // Im selben Durchgang auf Wasserzeichen prüfen (sichtbarer Aufdruck +
                // Metadaten-Markierungen). Eigener Fortschritt, damit die zweite Phase
                // nicht wie ein Hänger nach „100 %" aussieht.
                IndexFortschritt = 0;

                // Eigene Uhr für diese Phase. Die Uhr des Indexierens läuft seit dem
                // Start weiter; mit ihr gerechnet käme eine viel zu hohe Restzeit heraus,
                // weil die verstrichene Zeit das Indexieren enthält, die Stückzahl aber
                // nur die geprüften Bilder.
                var wzUhr = System.Diagnostics.Stopwatch.StartNew();

                var wzFortschritt = new Progress<(int Erledigt, int Gesamt)>(p =>
                {
                    IndexFortschritt = p.Gesamt > 0 ? 100.0 * p.Erledigt / p.Gesamt : 0;

                    int restSek = 0;
                    if (p.Erledigt > 0 && p.Gesamt > 0)
                    {
                        double proSek = p.Erledigt / Math.Max(wzUhr.Elapsed.TotalSeconds, 0.001);
                        restSek = (int)Math.Ceiling((p.Gesamt - p.Erledigt) / Math.Max(proSek, 0.001));
                    }

                    string restText = FormatiereRestzeit(restSek);
                    IndexFortschrittText = restText.Length > 0
                        ? $"Prüfe auf bekannte Wasserzeichen {p.Erledigt}/{p.Gesamt} – noch ~{restText}"
                        : $"Prüfe auf bekannte Wasserzeichen {p.Erledigt}/{p.Gesamt}…";
                });

                await PruefeWasserzeichenAsync(ordner, wzFortschritt, token);

                IndexFortschritt = 100;

                // Ausgeräumte Karteileichen nur nennen, wenn es welche gab – sonst stünde
                // bei jedem Lauf eine „0 entfernt"-Meldung im Weg.
                //
                // Wortwahl mit Bedacht: Entfernt wird der Eintrag, nicht das Bild. Von
                // „Bildern" zu lesen, während etwas entfernt wird, erschreckt — dabei hat
                // das Programm keine einzige Datei angerührt.
                int aufgeraeumt = _bildAnalyse.LetzteAufgeraeumteEintraege;
                string aufraeumText = aufgeraeumt > 0
                    ? aufgeraeumt == 1
                        ? " 1 Eintrag aus dem Index entfernt (nicht mehr im Ordner)."
                        : $" {aufgeraeumt} Einträge aus dem Index entfernt (nicht mehr im Ordner)."
                    : string.Empty;

                // Mitkopierte Indexdatei: Ihre Einträge zeigen auf den Herkunftsordner. Sie
                // werden verworfen, sonst stünde jedes Bild zweimal im Index. Das gehört
                // gesagt — sonst wundert man sich über eine Zahl, die kleiner ist als die
                // Datei vermuten liess.
                int fremd = _bildAnalyse.LetzteFremdeEintraege;
                string fremdText = fremd > 0
                    ? fremd == 1
                        ? " 1 Eintrag gehörte zu einem anderen Ordner (mitkopierte Indexdatei) und wurde verworfen."
                        : $" {fremd} Einträge gehörten zu einem anderen Ordner (mitkopierte Indexdatei) und wurden verworfen."
                    : string.Empty;

                IndexFortschrittText =
                    $"Fertig: {anzahl} Bilder indexiert.{aufraeumText}{fremdText} {WasserzeichenStatus}{IndexMessung()}";
                AktualisiereFilterOptionen();
                // Erzwungen: Der Ordner ist derselbe geblieben, nur die Indexdatei ist neu.
                PruefeAktuellerOrdnerIndiziert(erzwingen: true);   // Index existiert jetzt → „Schema-ähnlich" freischalten

                if (!string.IsNullOrWhiteSpace(SucheText) && _alleSuchTreffer.Count > 0)
                {
                    SucheStatus = "Index aktualisiert – Suche wird wiederholt…";
                    await CommandExecuteFreitextSuche(token);
                }
                else if (_alleSuchTreffer.Count > 0)
                {
                    SuchErgebnisse.Clear();
                    LeereTrefferCache();
                    SucheStatus = "Index aktualisiert – bitte erneut suchen/filtern.";
                }
            }
            catch (OperationCanceledException)
            {
                IndexFortschrittText = "Indexierung abgebrochen.";
            }
            catch (Exception ex)
            {
                IndexFortschrittText = "Fehler beim Indexieren: " + ex.Message;
            }
            finally
            {
                IndexLaeuft = false;
            }
        }

        [RelayCommand]
        private void CommandExecuteFilterPopoverToggle()
        {
            IsIndexPopoverOffen = !IsIndexPopoverOffen;
        }

        [RelayCommand(IncludeCancelCommand = true)]
        private async Task CommandExecuteFreitextSuche(CancellationToken token)
        {
            string frage = (SucheText ?? string.Empty).Trim();
            if (frage.Length == 0)
            {
                return;
            }

            // Ab hier geht es um Ergebnisse – die Ordnerverwaltung wäre nur im Weg.
            SchliesseIndexOrdnerKarte();

            string? pfad = SelectedBildchen?.BName;
            string? ordner = string.IsNullOrEmpty(pfad) ? null : Path.GetDirectoryName(pfad);
            if (string.IsNullOrEmpty(ordner))
            {
                SucheStatus = "Kein Ordner – erst ein Bild wählen und indexieren.";
                return;
            }

            SuchErgebnisse.Clear();
            LeereTrefferCache();
            CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
            ErgebnisseSindSchemaAehnlich = false;   // andere Suche → Schema-Slider ausblenden

            // Linie am Suchfeld: erst Marquee (Dauer unbekannt), später echter Balken.
            SuchfeldFortschritt = 0;
            SuchfeldIndeterminate = true;

            try
            {
                await StelleClipBereitAsync();
                SucheStatus = $"Suche '{frage}'…";

                // Alle Top-Treffer holen (Schwelle 0); gefiltert wird lokal per Slider.
                // SucheAsync nimmt keinen Token — der Abbruch greift erst danach.
                var treffer = await _bildAnalyse.SucheAsync(ordner, frage, topN: 60, minSim: 0f);

                token.ThrowIfCancellationRequested();

                // Karteileichen des Index aussortieren (siehe NurVorhandene).
                treffer = NurVorhandene(treffer);

                if (treffer.Count == 0)
                {
                    SucheStatus = "Keine Treffer – ist der Ordner schon indexiert?";
                    return;
                }

                _letzteFrage = frage;

                // Miniaturen einzeln laden, damit die Linie den langsamen Teil abbildet.
                SuchfeldIndeterminate = false;

                var ergebnisse = new System.Collections.Generic.List<(SuchErgebnis Erg, float Score)>(treffer.Count);

                for (int i = 0; i < treffer.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var t = treffer[i];
                    var thumb = await Task.Run(() => LadeThumb(t.Path), token);

                    ergebnisse.Add((new SuchErgebnis
                    {
                        Path = t.Path,
                        DateiName = Path.GetFileName(t.Path),
                        ProzentText = $"{t.Score * 100f:F0} %",
                        Thumb = thumb
                    }, t.Score));

                    SuchfeldFortschritt = (int)((i + 1) * 100.0 / treffer.Count);
                }

                await FuegeErgebnisseEinAsync(ergebnisse);

                RenderSuchErgebnisse();
            }
            catch (OperationCanceledException)
            {
                SucheStatus = $"Suche nach '{frage}' abgebrochen.";
            }
            catch (Exception ex)
            {
                SucheStatus = "Fehler bei der Suche: " + ex.Message;
            }
            finally
            {
                // Für den nächsten Lauf wieder unbestimmt starten.
                SuchfeldIndeterminate = true;
            }
        }

        /// <summary>
        /// Die nicht übersetzten Wörter, in Anführungszeichen und durch Komma getrennt.
        /// Leer, wenn alles übersetzt wurde — daran hängt auch die Sichtbarkeit der Zeile.
        /// </summary>
        [ObservableProperty]
        public partial string SuchWortHinweisWoerter { get; set; } = string.Empty;

        /// <summary>Der erklärende Nachsatz hinter den Wörtern.</summary>
        [ObservableProperty]
        public partial string SuchWortHinweisText { get; set; } = string.Empty;

        /// <summary>
        /// Setzt den Hinweis über Wörter, die der Übersetzer nicht kannte.
        ///
        /// In zwei Eigenschaften geteilt, damit die Oberfläche die Wörter selbst farblich
        /// hervorheben kann — in einer durchgehend grauen Zeile gehen sie unter, und
        /// genau sie sind die Auskunft, auf die es ankommt.
        ///
        /// Nur für die Freitextsuche gedacht. Andere Suchwege (Schema, Serie, Dubletten)
        /// fragen den Übersetzer gar nicht; dort bliebe der Wert von der letzten
        /// Freitextsuche stehen und wäre irreführend. Deshalb leert
        /// <see cref="LeereTrefferCache"/> ihn zu Beginn jedes Suchlaufs.
        /// </summary>
        private void SetzeHinweisNichtUebersetzt()
        {
            var unbekannt = _bildAnalyse?.LetzteNichtUebersetzt;
            if (unbekannt is null || unbekannt.Count == 0)
            {
                SuchWortHinweisWoerter = string.Empty;
                SuchWortHinweisText = string.Empty;
                return;
            }

            SuchWortHinweisWoerter = string.Join(", ", unbekannt.Select(w => $"„{w}“"));

            // Vorsichtig formuliert, weil der Hinweis irren kann: Ein deutsches Wort, das
            // im Englischen genauso heisst — Sofa, Hotel, Taxi —, geht unübersetzt durch
            // und wird von CLIP trotzdem verstanden. Offline lässt sich das nicht
            // unterscheiden; dafür fehlt eine englische Wortliste.
            SuchWortHinweisText = unbekannt.Count == 1
                ? " kennt der Übersetzer nicht — es trägt nichts zur Suche bei, ausser es heisst auf Englisch genauso."
                : " kennt der Übersetzer nicht — sie tragen nichts zur Suche bei, ausser sie heissen auf Englisch genauso.";
        }

        /// <summary>Gecachte Treffer nach der Mindest-Ähnlichkeit filtern und anzeigen.</summary>
        private void RenderSuchErgebnisse()
        {
            ErgebnisseSindSchemaAehnlich = false;   // Freitext-/Standardtreffer: Mindest-Slider filtert
            HatTrefferCache = _alleSuchTreffer.Count > 0;
            SuchErgebnisse.Clear();
            float min = (float)(MindestAehnlichkeit / 100.0);

            int gezeigt = 0;
            float bestScore = 0f;
            foreach (var (erg, score) in _alleSuchTreffer)
            {
                if (score > bestScore)
                {
                    bestScore = score;
                }

                if (score < min)
                {
                    continue;
                }

                SuchErgebnisse.Add(erg);
                gezeigt++;
            }

            string scoreHinweis = bestScore < 0.24f ? " · ⚠ Treffer unsicher, CLIP erkennt Fotos besser als Screenshots"
                                 : bestScore < 0.30f ? " · Treffer mäßig sicher"
                                 : " · gute Übereinstimmung";

            // Wörter melden, die der Übersetzer nicht kannte — in eigener Zeile.
            //
            // Ohne diesen Hinweis sieht ein leeres Ergebnis wie „solche Bilder gibt es
            // nicht" aus, obwohl in Wahrheit das Wort nie gesucht wurde: Unübersetztes
            // geht deutsch in den englischen Text-Encoder und trägt dort nichts bei.
            // „steine am strand" fand Bilder — aber allein wegen „at the beach".
            SetzeHinweisNichtUebersetzt();

            if (gezeigt == 0)
            {
                SucheStatus = $"Keine Treffer über {MindestAehnlichkeit:F0} % für '{_letzteFrage}'.";
            }
            else
            {
                SucheStatus = $"{gezeigt} Treffer für '{_letzteFrage}' (bester: {bestScore * 100f:F0} %){scoreHinweis}";
            }

            CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
        }

        private bool CanExecuteTrefferUebernehmen() => SuchErgebnisse.Count > 0;

        [RelayCommand(CanExecute = nameof(CanExecuteTrefferUebernehmen))]
        private void CommandExecuteTrefferUebernehmen()
        {
            if (SuchErgebnisse.Count == 0)
            {
                return;
            }

            // Nur die Treffer-Bilder in der Liste behalten (Reihenfolge = Ähnlichkeit).
            // Wiederherstellen über „Alle Bilder neu einlesen".
            //
            // Bewusst kein ToDictionary: Das wirft eine ArgumentException, sobald derselbe
            // Pfad zweimal in der Liste steht. Ein doppelter Eintrag ist zwar ein Fehler
            // an anderer Stelle, darf hier aber nicht zum Absturz führen — der erste
            // gewinnt, der zweite wird übergangen.
            var nachPfad = new System.Collections.Generic.Dictionary<string, MeinBildchen>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var b in ocAufgabens)
            {
                if (string.IsNullOrWhiteSpace(b.BName))
                {
                    continue;
                }

                if (!nachPfad.ContainsKey(b.BName))
                {
                    nachPfad[b.BName] = b;
                }
            }

            var behalten = new System.Collections.Generic.List<MeinBildchen>(SuchErgebnisse.Count);

            // Zusätzlich gegen Doppelungen sichern: Zwei Treffer können nach dem Abbilden
            // auf die Liste denselben Pfad tragen. Ohne diese Prüfung landeten sie beide
            // in ocAufgabens – und beim nächsten Übernehmen wäre der Pfad dann doppelt.
            var schonDrin = new System.Collections.Generic.HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var treffer in SuchErgebnisse)
            {
                if (string.IsNullOrWhiteSpace(treffer.Path) || !schonDrin.Add(treffer.Path))
                {
                    continue;
                }

                if (nachPfad.TryGetValue(treffer.Path, out var vorhanden))
                {
                    // Schon geladen: Eintrag mitsamt seinen Markierungen übernehmen.
                    behalten.Add(vorhanden);
                }
                else if (File.Exists(treffer.Path))
                {
                    // Treffer aus einem anderen Ordner. Der Index umfasst mehrere Ordner,
                    // ocAufgabens aber immer nur den gerade geladenen — solche Treffer
                    // fielen hier bisher stillschweigend heraus, und genau deshalb kamen
                    // nicht alle Bilder in der Liste an. Für sie wird ein Eintrag angelegt.
                    behalten.Add(new MeinBildchen { BName = treffer.Path, BildFürLinks = false });
                }

                // Datei existiert nicht mehr (Index veraltet) → auslassen.
            }

            if (behalten.Count == 0)
            {
                return;
            }

            // Der Sammel-Befehl „alle ins kein_Fav" kann durch das Übernehmen unzulässig
            // werden, wenn Treffer aus mehreren Ordnern hereinkommen.
            CommandExecuteAlleBilderInsKeinFavVerschiebenCommand?.NotifyCanExecuteChanged();

            // Filter und Sortierung abschalten. Die übernommene Liste IST bereits das
            // Ergebnis, in der Reihenfolge der Ähnlichkeit — bestes Bild zuerst.
            //
            // Der Filter würde sie wieder auseinanderreissen, und die natürliche
            // Sortierung würde sie nach Dateinamen umstellen und damit die Rangfolge
            // vernichten. Beides wird beim Neu-Einlesen zurückgesetzt, die
            // Explorer-Reihenfolge im Normalbetrieb bleibt also erhalten.
            AufgabenView.Filter = null;
            AufgabenView.CustomSort = null;

            ocAufgabens.Clear();
            foreach (var b in behalten)
            {
                ocAufgabens.Add(b);
            }

            if (ocAufgabens.Count > 0)
            {
                AufgabenView.MoveCurrentToFirst();
            }

            IsSuchleisteOffen = false; // Popup schließen, damit man die Liste sieht
        }

        [RelayCommand]
        private async Task CommandExecuteTrefferOeffnen(string? pfad)
        {
            if (string.IsNullOrEmpty(pfad))
            {
                return;
            }

            // 1) Bild ist in der geladenen Liste → direkt auswählen.
            var item = OcAufgabens.FirstOrDefault(
                b => string.Equals(b.BName, pfad, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                SelectedBildchen = item;
                await AnalysiereAktuellesBildAsync();
                return;
            }

            // 2) Nicht in der Liste (z. B. nach „In Liste übernehmen" eingedampft oder der
            //    ganze Ordner steckt nur im Index): Datei existiert noch → Ordner neu laden
            //    und das Bild auswählen, damit jeder Treffer anklickbar bleibt.
            if (File.Exists(pfad))
            {
                // Trefferliste behalten – der Nutzer klickt sich gerade durch sie hindurch.
                await OnFileDrop(new[] { pfad }, verwerfeSuchtreffer: false);
                var wieder = OcAufgabens.FirstOrDefault(
                    b => string.Equals(b.BName, pfad, StringComparison.OrdinalIgnoreCase));
                if (wieder != null)
                {
                    SelectedBildchen = wieder;
                    await AnalysiereAktuellesBildAsync();
                }
                return;
            }

            // 3) Datei ist nicht mehr am Ort (verschoben/gelöscht) → Hinweis + toten Treffer entfernen.
            SucheStatus = $"Bild nicht mehr am Ort: {Path.GetFileName(pfad)} (verschoben/gelöscht).";
            var veraltet = SuchErgebnisse.FirstOrDefault(
                e => string.Equals(e.Path, pfad, StringComparison.OrdinalIgnoreCase));
            if (veraltet != null)
            {
                SuchErgebnisse.Remove(veraltet);
                _alleSuchTreffer.RemoveAll(t => string.Equals(t.Erg.Path, pfad, StringComparison.OrdinalIgnoreCase));
                CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
            }
        }

        private async Task FuegeErgebnisseEinAsync(System.Collections.Generic.List<(SuchErgebnis Erg, float Score)> liste)
        {
            const int batch = 8;
            for (int i = 0; i < liste.Count; i++)
            {
                _alleSuchTreffer.Add(liste[i]);
                SuchErgebnisse.Add(liste[i].Erg);
                if ((i + 1) % batch == 0)
                {
                    await Task.Delay(1);
                }
            }
        }

        /// <summary>Lädt eine kleine, eingefrorene Vorschau für einen Treffer.</summary>
        /// <summary>
        /// Gemeinsamer Cache mit der Miniaturleiste (beide 120px): schon geladene
        /// Thumbnails werden wiederverwendet, neue landen dort für die Leiste.
        ///
        /// Die Rechnung selbst steht in <see cref="MiniaturLader.Dekodiere"/> — sie war
        /// hier und im Konverter Zeile für Zeile dieselbe.
        /// </summary>
        private static ImageSource? LadeThumb(string pfad) => MiniaturLader.Dekodiere(pfad);

        [RelayCommand]
        private void CommandExecuteUebersicht()
        {
            string? pfad = SelectedBildchen?.BName;
            if (string.IsNullOrEmpty(pfad))
            {
                return;
            }

            string? ordner = Path.GetDirectoryName(pfad);
            if (string.IsNullOrEmpty(ordner))
            {
                return;
            }

            string cache = Path.Combine(ordner, BildAnalyseService.CacheDateiName);
            if (!File.Exists(cache))
            {
                SucheStatus = "Kein Index vorhanden – erst den Ordner indexieren.";
                return;
            }

            var index = new ImageMatching.Core.ImageIndex(new ImageMatching.Cnn.CnnDescriptor());
            index.Load(cache, nurAusDiesemOrdner: true);
            if (index.Count == 0)
            {
                SucheStatus = "Index ist leer.";
                return;
            }

            // Concepts zählen + je Begriff ein Beispielbild merken.
            var stats = new System.Collections.Generic.Dictionary<string, (int Count, string ExamplePath)>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in index.Entries)
            {
                foreach (string concept in entry.Concepts)
                {
                    if (!stats.ContainsKey(concept))
                    {
                        stats[concept] = (1, entry.Path);
                    }
                    else
                    {
                        stats[concept] = (stats[concept].Count + 1, stats[concept].ExamplePath);
                    }
                }
            }

            var sortiert = stats
                .OrderByDescending(kv => kv.Value.Count)
                .ToList();

            ErgebnisseSindSchemaAehnlich = false;   // Übersicht: nicht der Schema-Slider
            LeereTrefferCache();
            SuchErgebnisse.Clear();

            foreach (var kv in sortiert)
            {
                string begriff = kv.Key;
                int count = kv.Value.Count;
                string beispiel = kv.Value.ExamplePath;
                string anzeige = BegriffeAufDeutsch ? BegriffUebersetzer.ZuDeutsch(begriff) : begriff;
                var erg = new SuchErgebnis
                {
                    Path = beispiel,
                    DateiName = anzeige,
                    ProzentText = $"{count}×",
                    Thumb = LadeThumb(beispiel)
                };
                SuchErgebnisse.Add(erg);
            }

            SucheStatus = $"Übersicht: {sortiert.Count} Begriffe in {index.Count} Bildern.";
            _letzteFrage = "";
            CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
        }

        private bool CanExecuteDubletten()
        {
            return SelectedBildchen != null
                && !string.IsNullOrEmpty(SelectedBildchen.BName)
                && AktuellerOrdnerIndiziert   // vergleicht die Embeddings aus dem Index
                && !SerieSucheLaeuft;
        }

        [RelayCommand(CanExecute = nameof(CanExecuteDubletten), IncludeCancelCommand = true)]
        private async Task CommandExecuteDubletten(CancellationToken token)
        {
            string? bildPfad = SelectedBildchen?.BName;
            if (string.IsNullOrEmpty(bildPfad))
            {
                return;
            }

            string? ordner = Path.GetDirectoryName(bildPfad);
            if (string.IsNullOrEmpty(ordner))
            {
                return;
            }

            string cache = Path.Combine(ordner, BildAnalyseService.CacheDateiName);
            if (!File.Exists(cache))
            {
                SucheStatus = "Kein Index vorhanden – erst den Ordner indexieren.";
                return;
            }

            SuchErgebnisse.Clear();
            LeereTrefferCache();
            CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
            ErgebnisseSindSchemaAehnlich = false;   // andere Suche → Schema-Slider ausblenden
            SucheStatus = "Suche Dubletten im Ordner…";
            SerieFortschritt = 0;
            SerieIndeterminate = false;   // echter %-Balken mit Restzeit
            SerieSucheLaeuft = true;

            // Fortschritt + Restzeit aus dem Hintergrund-Thread per Dispatcher sicher
            // in die Statuszeile umleiten (wie bei der erweiterten Seriensuche).
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            var fortschritt = new Progress<(int Prozent, int RestSekunden)>(p =>
            {
                dispatcher.Invoke(() =>
                {
                    SerieFortschritt = p.Prozent;
                    SucheStatus = p.RestSekunden > 0
                        ? $"Suche Dubletten… {p.Prozent} % – noch ~{p.RestSekunden} s"
                        : $"Suche Dubletten… {p.Prozent} %";
                });
            });

            try
            {
                await StelleClipBereitAsync();

                var gruppen = await _bildAnalyse.FindeDublettenAsync(ordner, fortschritt, token);

                // Dieselben Karteileichen wie bei der erweiterten Serie. Bleibt von einer
                // Gruppe nur ein Bild übrig, ist es keine Dublette mehr und fliegt raus.
                gruppen = gruppen
                    .Select(NurVorhandene)
                    .Where(g => g.Count > 1)
                    .ToList();

                if (gruppen.Count == 0)
                {
                    SucheStatus = "Keine Dubletten gefunden – alle Bilder im Ordner sind verschieden.";
                    return;
                }

                _letzteFrage = "Dubletten";
                int anzahlBilder = await ZeigeDublettenAsync(gruppen, token);

                SucheStatus = $"{anzahlBilder} Dubletten in {gruppen.Count} Gruppen (≥ 98 % Ähnlichkeit).";
                CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
            }
            catch (OperationCanceledException)
            {
                SucheStatus = "Dublettensuche abgebrochen.";
            }
            catch (Exception ex)
            {
                SucheStatus = "Fehler bei Dublettensuche: " + ex.Message;
            }
            finally { SerieSucheLaeuft = false; }
        }

        /// <summary>
        /// Zeigt die Dubletten-Gruppen als flache Trefferliste: Gruppen nacheinander,
        /// je Bild „Gr. N · xx %". Thumbnails werden einzeln geladen (Fortschritt +
        /// Restzeit in der Statuszeile). Rückgabe: Anzahl angezeigter Bilder.
        /// </summary>
        private async Task<int> ZeigeDublettenAsync(
            System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<(string Path, float Score)>> gruppen,
            CancellationToken token)
        {
            ErgebnisseSindSchemaAehnlich = false;   // Dublettentreffer: nicht der Schema-Slider
            SerieIndeterminate = false;   // ab jetzt echter Prozent-Fortschritt
            SerieFortschritt = 0;

            int gesamt = gruppen.Sum(g => g.Count);
            int fertig = 0;
            var uhr = System.Diagnostics.Stopwatch.StartNew();

            for (int g = 0; g < gruppen.Count; g++)
            {
                foreach (var t in gruppen[g])
                {
                    token.ThrowIfCancellationRequested();
                    var thumb = await Task.Run(() => LadeThumb(t.Path), token);

                    var erg = new SuchErgebnis
                    {
                        Path = t.Path,
                        DateiName = Path.GetFileName(t.Path),
                        ProzentText = $"Gr. {g + 1} · {t.Score * 100f:F0} %",
                        Thumb = thumb
                    };
                    _alleSuchTreffer.Add((erg, t.Score));
                    SuchErgebnisse.Add(erg);

                    fertig++;
                    SerieFortschritt = (int)(fertig * 100.0 / gesamt);
                    double proSek = fertig / Math.Max(uhr.Elapsed.TotalSeconds, 0.001);
                    int restSek = (int)Math.Ceiling((gesamt - fertig) / Math.Max(proSek, 0.001));
                    string restText = FormatiereRestzeit(restSek);
                    SucheStatus = restText.Length > 0
                        ? $"Lade Vorschaubilder… {fertig}/{gesamt} – noch ~{restText}"
                        : $"Lade Vorschaubilder… {fertig}/{gesamt}";
                }
            }
            return gesamt;
        }

        [RelayCommand]
        private void CommandExecuteQueryBild()
        {
            // TODO: Query-Bild wählen und ähnliche suchen
        }

        /// <summary>
        /// Schwelle der „Schema-ähnlich"-Suche (Bild-als-Anfrage) in Prozent, per
        /// Slider unter „Einstellungen" einstellbar. Bild→Bild-CLIP-Ähnlichkeit liegt
        /// hoch: Varianten desselben Motivs ~60–95 %, Fremdbilder darunter. Am Test
        /// eingemessen: echte Varianten bis ~75 %, darum Standard 74 % (knapp darunter,
        /// damit das bei „75 %" gerundete Grenzbild sicher drin bleibt).
        /// </summary>
        [ObservableProperty]
        public partial double SchemaAehnlichkeitProzent { get; set; } = SchemaAehnlichkeitStandard;

        /// <summary>Standardwert der Schema-Ähnlichkeit in Prozent (Reset-Button).</summary>
        public const double SchemaAehnlichkeitStandard = 74;

        /// <summary>Setzt die Schema-Ähnlichkeit auf den Standardwert zurück.</summary>
        [RelayCommand]
        private void CommandExecuteSchemaSchwelleZuruecksetzen() => SchemaAehnlichkeitProzent = AktuellerSchemaStandard;

        /// <summary>
        /// Untergrenze des geladenen Kandidatensatzes (= Slider-Minimum). Es werden
        /// alle Bilder ab dieser Ähnlichkeit geladen, damit der Schema-Slider die
        /// Anzeige live nach unten wie oben filtern kann, ohne neu zu suchen.
        /// </summary>
        private const float SchemaKandidatenFloor = 0.5f;

        #region Embedding-Kalibrierung (Stufe 1)

        /// <summary>
        /// Rohe CLIP-Embeddings vor dem Vergleich zentrieren und die stärksten
        /// Hauptkomponenten herausprojizieren (siehe <see cref="EmbeddingKalibrierung"/>).
        /// Standard aus, damit die am rohen Kosinus eingemessenen 74 % weiter gelten —
        /// zum Vergleichen bewusst umschalten.
        /// </summary>
        [ObservableProperty]
        public partial bool SchemaKalibrierungAktiv { get; set; }

        /// <summary>
        /// Wie viele Hauptkomponenten entfernt werden. 0 = nur zentrieren.
        /// 3 ist der übliche Ausgangswert; mehr entfernt zunehmend echtes Signal.
        /// </summary>
        [ObservableProperty]
        public partial int SchemaKalibrierungKomponenten { get; set; } = 3;

        /// <summary>
        /// Standardschwelle im kalibrierten Modus. Dort liegt die mittlere Ähnlichkeit
        /// bei 50 % (Kosinus 0), echte Treffer deutlich darüber — die 74 % vom rohen
        /// Kosinus sind hier bedeutungslos.
        /// </summary>
        public const double SchemaAehnlichkeitStandardKalibriert = 80;

        /// <summary>Der zum aktuellen Modus passende Standardwert (Reset-Button).</summary>
        private double AktuellerSchemaStandard => SchemaKalibrierungAktiv
            ? SchemaAehnlichkeitStandardKalibriert
            : SchemaAehnlichkeitStandard;

        // Moduswechsel: Skala ändert sich, also Schwelle auf den passenden Standard
        // stellen — sonst filtert der alte Wert im neuen Modus sinnlos.
        partial void OnSchemaKalibrierungAktivChanged(bool value)
        {
            SchemaAehnlichkeitProzent = AktuellerSchemaStandard;

            if (ErgebnisseSindSchemaAehnlich)
                SucheStatus = value
                    ? "Kalibrierung ein — Suche erneut ausführen, um sie anzuwenden."
                    : "Kalibrierung aus — Suche erneut ausführen.";
        }

        #endregion

        /// <summary>
        /// True, solange die angezeigten Treffer aus der „Schema-ähnlich"-Suche
        /// stammen – dann filtert der Schema-Slider live, sonst der Mindest-Ähnl.-Slider.
        /// Steuert zugleich, ob der Schema-Slider überhaupt eingeblendet wird (erst nach der Suche).
        /// </summary>
        [ObservableProperty]
        public partial bool ErgebnisseSindSchemaAehnlich { get; set; }

        /// <summary>
        /// True, wenn der Ordner des aktuell gewählten Bildes einen CLIP-Index besitzt.
        /// Voraussetzung für „Schema-ähnlich": ohne Index kann nicht gesucht werden, der
        /// Button ist dann deaktiviert. Wird bei Bildwechsel und nach dem Indexieren neu bestimmt.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteSchemaAehnlichCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteErweiterteSerieSucheCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteDublettenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteFavSortierenCommand))]
        public partial bool AktuellerOrdnerIndiziert { get; set; }

        /// <summary>
        /// Ordner, dessen Indexstand zuletzt bestimmt wurde. <c>null</c> heisst „keiner" —
        /// etwa vor dem ersten Bild oder nachdem der Ordner verschwunden ist.
        /// </summary>
        private string? _zuletztGepruefterIndexOrdner;

        /// <summary>
        /// Läuft bei jedem Bildwechsel: bestimmt den Indexstand des Ordners und zieht die
        /// bildbezogenen Anzeigen nach.
        ///
        /// Der Ordnerteil läuft nur bei einem Ordnerwechsel. Er kostet einen Gang ans
        /// Dateisystem, und der Aufruf hängt am Setter von <c>SelectedBildchen</c> — auf
        /// einem Netzlaufwerk wäre das eine Umlaufzeit je Pfeiltastendruck, für eine
        /// Antwort, die sich innerhalb eines Ordners nicht ändert. Dass die Indexdatei
        /// währenddessen auftaucht oder von Hand gelöscht wird, deckt
        /// <c>_indexWaechter</c> über <c>BeiIndexDateiAenderung</c> ab.
        ///
        /// Die Abkürzung gilt nur, solange dieser Wächter wirklich läuft. Netzlaufwerke
        /// und manche Wechseldatenträger lassen keine Überwachung zu; dort bleibt es bei
        /// der Prüfung zu jedem Bild, sonst fiele eine gelöschte Indexdatei überhaupt
        /// nicht mehr auf.
        ///
        /// Ein Ordnerwechsel ist keine Seltenheit: In einer Trefferliste stehen Bilder
        /// aus vielen Ordnern nebeneinander. Dort greift die Abkürzung nur, solange
        /// aufeinanderfolgende Treffer aus demselben Ordner stammen.
        /// </summary>
        /// <param name="erzwingen">
        /// True = den Ordnerteil auch bei gleichem Ordner ausführen. Nötig, wenn sich der
        /// Indexstand geändert hat, ohne dass das Bild gewechselt hat: direkt nach dem
        /// Indexieren und wenn der Wächter anschlägt.
        /// </param>
        private void PruefeAktuellerOrdnerIndiziert(bool erzwingen = false)
        {
            string? pfad = SelectedBildchen?.BName;
            string? ordner = string.IsNullOrEmpty(pfad) ? null : Path.GetDirectoryName(pfad);

            if (erzwingen
                || !_indexWaechter.IstAktiv
                || !string.Equals(ordner, _zuletztGepruefterIndexOrdner, StringComparison.OrdinalIgnoreCase))
            {
                _zuletztGepruefterIndexOrdner = ordner;

                AktuellerOrdnerIndiziert = !string.IsNullOrEmpty(ordner)
                    && File.Exists(Path.Combine(ordner, BildAnalyseService.CacheDateiName));

                // Gleich nachtragen, falls der Ordner noch nicht im Verzeichnis steht. So
                // füllt sich die Liste auch mit Ordnern, die vor dieser Funktion indexiert
                // wurden — es genügt, sie einmal zu öffnen.
                if (AktuellerOrdnerIndiziert)
                {
                    MerkeOrdnerFallsIndiziert(ordner);
                }

                // Diesen Ordner überwachen: Wird die Indexdatei von Hand gelöscht, merkt es
                // die Anwendung sonst erst beim nächsten Bildwechsel — und „Schema-ähnlich"
                // liefe bis dahin ins Leere.
                UeberwacheIndexDatei(ordner);
            }

            // Befund zum jetzt gewählten Bild in der Wasserzeichen-Karte nachziehen.
            // Diese Methode läuft bei jedem Bildwechsel, ist also der passende Ort.
            AktualisiereWasserzeichenBefundAnzeige();

            // Ebenso die Marke „fertig sortiert" und den Stand des gemeinsamen Profils.
            AktualisiereFavProfilAnzeige();
        }

        // Schema-Slider bewegt → Kandidaten neu filtern (nur bei aktiven Schema-Treffern).
        //
        // Die FS-Sortierung benutzt denselben Regler, aber einen eigenen Renderer: Dort
        // ist die Zahl kein Ähnlichkeitsmass, sondern der Rang im Ordner, und die
        // Statuszeile muss etwas anderes sagen.
        partial void OnSchemaAehnlichkeitProzentChanged(double value)
        {
            if (_alleSuchTreffer.Count == 0)
                return;

            if (ErgebnisseSindFavSortierung)
                RenderFavSortierung();
            else if (ErgebnisseSindSchemaAehnlich)
                RenderSchemaAehnlich();
        }

        private bool CanExecuteSchemaAehnlich()
        {
            return SelectedBildchen != null
                && !string.IsNullOrEmpty(SelectedBildchen.BName)
                && AktuellerOrdnerIndiziert
                && !SerieSucheLaeuft;
        }


        /// <summary>
        /// „Schema-ähnlich": nimmt das gewählte Bild als Anfrage und findet über die
        /// gespeicherten CLIP-Embeddings die visuell ähnlichsten Bilder im selben
        /// Ordner (ähnlicher Bildaufbau / dasselbe Motiv). Löst die alte, grobe
        /// Perceptual-Hash-Suche („ungefähr gleiches Bild") sauberer ab. Die Treffer
        /// landen im Ergebnispanel; per „In Liste übernehmen" wird die Navigations-
        /// liste auf genau diese Bilder eingedampft.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteSchemaAehnlich), IncludeCancelCommand = true)]
        private async Task CommandExecuteSchemaAehnlich(CancellationToken token)
        {
            string? bildPfad = SelectedBildchen?.BName;
            if (string.IsNullOrEmpty(bildPfad))
            {
                return;
            }

            string? ordner = Path.GetDirectoryName(bildPfad);
            if (string.IsNullOrEmpty(ordner))
            {
                return;
            }

            string cache = Path.Combine(ordner, BildAnalyseService.CacheDateiName);
            if (!File.Exists(cache))
            {
                SucheStatus = "Kein Index vorhanden – erst den Ordner indexieren.";
                return;
            }

            SuchErgebnisse.Clear();
            LeereTrefferCache();
            CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
            SchliesseIndexOrdnerKarte();   // Verwaltung schliessen, Ergebnisse übernehmen den Platz
            SucheStatus = $"Suche Schema-ähnliche Bilder zu '{Path.GetFileName(bildPfad)}'…";
            SerieFortschritt = 0;
            SerieIndeterminate = true;   // Marquee: Ähnlichkeitsberechnung läuft
            SerieSucheLaeuft = true;

            try
            {
                await StelleClipBereitAsync();

                // Bild als Anfrage: alle Bilder im Ordner nach Ähnlichkeit sortiert,
                // ab dem Slider-Minimum (breiter Kandidatensatz). Das Bild selbst ist
                // mit 100 % dabei. Angezeigt wird dann per Slider gefiltert.
                var suchOrdner = ErmittleSuchOrdner();
                bool ueberMehrere = suchOrdner.Count > 1;

                System.Collections.Generic.IReadOnlyList<(string Path, float Score)> treffer;

                if (ueberMehrere)
                {
                    var ordnerFortschritt = new Progress<(int Fertig, int Gesamt)>(p =>
                        SucheStatus = $"Durchsuche Ordner {p.Fertig}/{p.Gesamt}…");

                    treffer = await _bildAnalyse.SucheNachSerieInOrdnernAsync(
                        suchOrdner, bildPfad, topN: 200, minSim: SchemaKandidatenFloor,
                        ordnerFortschritt, token);
                }
                else
                {
                    // Einzelordner: unverändert der bisherige Weg, samt Kalibrierung.
                    treffer = await _bildAnalyse.SucheNachSerieAsync(
                        ordner, bildPfad, topN: 200, minSim: SchemaKandidatenFloor, token,
                        kalibrierKomponenten: SchemaKalibrierungAktiv ? SchemaKalibrierungKomponenten : -1);
                }

                treffer = AufListeAbbilden(treffer, nurAusListe: !ueberMehrere);

                if (treffer.Count <= 1)
                {
                    ErgebnisseSindSchemaAehnlich = true;
                    SucheStatus = "Keine schema-ähnlichen Bilder gefunden.";
                    return;
                }

                _letzteFrage = "Schema-ähnlich: " + Path.GetFileName(bildPfad);
                await LadeSchemaKandidatenAsync(treffer, token);

                ErgebnisseSindSchemaAehnlich = true;
                RenderSchemaAehnlich();   // nach aktuellem Slider-Wert anzeigen
                CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
            }
            catch (OperationCanceledException)
            {
                SucheStatus = "Schema-Ähnlichkeitssuche abgebrochen.";
            }
            catch (Exception ex)
            {
                SucheStatus = "Fehler bei Schema-Ähnlichkeitssuche: " + ex.Message;
            }
            finally { SerieSucheLaeuft = false; }
        }

        /// <summary>
        /// Lädt die Thumbnails des Kandidatensatzes einzeln in <see cref="_alleSuchTreffer"/>
        /// (Fortschritt + Restzeit in der Statuszeile). Die Anzeige selbst übernimmt
        /// danach <see cref="RenderSchemaAehnlich"/> nach dem Slider-Wert.
        /// </summary>
        /// <summary>
        /// Baut einen Vorauslader für Trefferminiaturen.
        ///
        /// Der zurückgegebene Aufruf liefert die Miniaturen **in der Reihenfolge der
        /// Trefferliste**, dekodiert aber bis zu <see cref="Environment.ProcessorCount"/>
        /// Bilder im Voraus. Damit bleibt die Anzeige fortlaufend und sortiert, während
        /// die Kerne ausgelastet werden.
        ///
        /// Absichtlich kein <c>Parallel.ForEach</c>: Das würde die Reihenfolge aufgeben,
        /// und genau die ist hier die Zusage an den Nutzer — bestes Bild zuerst.
        /// </summary>
        private Func<Task<ImageSource?>> ErzeugeVorauslader(
            System.Collections.Generic.IReadOnlyList<(string Path, float Score)> treffer,
            CancellationToken token)
        {
            int fenster = Math.Max(1, Environment.ProcessorCount);
            var laufend = new System.Collections.Generic.Queue<Task<ImageSource?>>(fenster);
            int naechster = 0;

            void Nachfuellen()
            {
                while (laufend.Count < fenster && naechster < treffer.Count)
                {
                    string pfad = treffer[naechster++].Path;
                    laufend.Enqueue(Task.Run(() => LadeThumb(pfad), token));
                }
            }

            Nachfuellen();

            return async () =>
            {
                if (laufend.Count == 0)
                {
                    return null;
                }

                var fertig = laufend.Dequeue();
                Nachfuellen();   // Lücke sofort wieder auffüllen, damit nie ein Kern leerläuft
                return await fertig;
            };
        }

        /// <summary>
        /// Bildet Index-Treffer auf die aktuelle Bildliste ab.
        ///
        /// Der Index kennt die Pfade vom Zeitpunkt des Indexierens. Diese App verschiebt
        /// Bilder aber – nach <c>kein_Fav</c> etwa –, und dabei bleibt der Eintrag in
        /// <c>ocAufgabens</c> erhalten, bekommt jedoch den neuen Pfad. Index und Liste
        /// laufen also mit jedem Verschieben weiter auseinander.
        ///
        /// Deshalb hier drei Fälle:
        /// 1. Pfad steht so in der Liste → unverändert übernehmen.
        /// 2. Pfad nicht, aber der Dateiname → das Bild wurde verschoben; der Treffer
        ///    bekommt den aktuellen Pfad und bleibt damit anklickbar.
        /// 3. Weder noch → die Datei ist fort. Weglassen; sie erschien bisher als
        ///    schwarzes, leeres Kästchen in den Ergebnissen.
        /// </summary>
        /// <param name="nurAusListe">
        /// True bei einer Suche im Einzelordner: Dann darf nur bestehen, was auch in der
        /// Liste steht — bisheriges Verhalten. Bei einer Suche über mehrere Ordner wäre
        /// das falsch, denn <c>ocAufgabens</c> enthält immer nur einen Ordner; es fielen
        /// sonst fast alle Treffer heraus. Dort genügt, dass die Datei existiert.
        /// </param>
        private System.Collections.Generic.IReadOnlyList<(string Path, float Score)> AufListeAbbilden(
            System.Collections.Generic.IReadOnlyList<(string Path, float Score)> treffer,
            bool nurAusListe = true)
        {
            var nachPfad = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nachName = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var bild in ocAufgabens)
            {
                if (string.IsNullOrWhiteSpace(bild.BName))
                {
                    continue;
                }

                nachPfad.Add(bild.BName);

                string name = Path.GetFileName(bild.BName);
                if (!nachName.ContainsKey(name))
                {
                    nachName[name] = bild.BName;
                }
            }

            var ergebnis = new System.Collections.Generic.List<(string Path, float Score)>(treffer.Count);

            // Das Abbilden kann zwei Treffer auf denselben Pfad führen – etwa wenn der
            // Index sowohl den alten Pfad als auch den in kein_Fav kennt. Der bessere
            // Wert steht wegen der Sortierung zuerst, der zweite wird übergangen.
            var schonDrin = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in treffer)
            {
                if (nachPfad.Contains(t.Path))
                {
                    if (schonDrin.Add(t.Path))
                    {
                        ergebnis.Add(t);
                    }
                }
                else if (nachName.TryGetValue(Path.GetFileName(t.Path), out string? neuerPfad)
                         && File.Exists(neuerPfad)
                         && schonDrin.Add(neuerPfad))
                {
                    ergebnis.Add((neuerPfad, t.Score));
                }
                else if (!nurAusListe && File.Exists(t.Path) && schonDrin.Add(t.Path))
                {
                    // Treffer aus einem anderen Ordner – bei ordnerübergreifender Suche
                    // der Normalfall und kein Grund, ihn zu verwerfen.
                    ergebnis.Add(t);
                }
            }

            return ergebnis;
        }

        /// <summary>
        /// Wirft Treffer weg, deren Datei es nicht mehr gibt.
        ///
        /// Nötig überall dort, wo Trefferlisten unmittelbar aus dem Index kommen und
        /// nicht durch <see cref="AufListeAbbilden"/> laufen: Der Index räumt gelöschte
        /// Dateien nicht aus, und ein toter Treffer erscheint in der Ergebnisleiste als
        /// schwarzes Kästchen — anklickbar, aber ohne Bild dahinter.
        /// </summary>
        private static System.Collections.Generic.IReadOnlyList<(string Path, float Score)> NurVorhandene(
            System.Collections.Generic.IReadOnlyList<(string Path, float Score)> treffer)
        {
            var ergebnis = new System.Collections.Generic.List<(string Path, float Score)>(treffer.Count);

            foreach (var t in treffer)
            {
                if (File.Exists(t.Path))
                {
                    ergebnis.Add(t);
                }
            }

            return ergebnis;
        }

        private async Task LadeSchemaKandidatenAsync(
            System.Collections.Generic.IReadOnlyList<(string Path, float Score)> treffer, CancellationToken token)
        {
            SerieIndeterminate = false;   // ab jetzt echter Prozent-Fortschritt
            SerieFortschritt = 0;

            // Wie bei der Seriensuche schon während des Ladens anzeigen: Der Kandidatensatz
            // ist absteigend sortiert, die Bilder über der Schwelle kommen also zuerst. Ohne
            // das erscheint das erste Bild erst, wenn alle bis zu 200 Kandidaten geladen sind
            // – auf einer HDD dauert das spürbar lange. Der Rest wird weiter geladen, damit
            // der Schema-Regler danach ohne neue Suche nach unten filtern kann.
            SuchErgebnisse.Clear();
            ErgebnisseSindSchemaAehnlich = true;   // Schema-Regler sofort einblenden
            float schwelle = (float)(SchemaAehnlichkeitProzent / 100.0);

            int gesamt = treffer.Count;
            int angezeigt = 0;
            var uhr = System.Diagnostics.Stopwatch.StartNew();

            // Vorausladen mit fester Fenstergrösse.
            //
            // Bisher wurde je Bild ein Task gestartet und sofort abgewartet – also
            // nacheinander, ein Kern. Jetzt laufen bis zu ProcessorCount Dekodierungen
            // gleichzeitig, abgewartet wird aber weiterhin streng der Reihe nach.
            //
            // Die Reihenfolge ist hier kein Schönheitsfehler, sondern die Zusage: Der
            // Kandidatensatz ist absteigend sortiert, die besten Treffer sollen zuerst
            // erscheinen. Ein einfaches Parallel.ForEach würde sie durcheinanderwürfeln.
            var vorauslader = ErzeugeVorauslader(treffer, token);

            for (int i = 0; i < gesamt; i++)
            {
                token.ThrowIfCancellationRequested();
                var t = treffer[i];
                var thumb = await vorauslader();

                var erg = new SuchErgebnis
                {
                    Path = t.Path,
                    DateiName = Path.GetFileName(t.Path),
                    ProzentText = $"{t.Score * 100f:F0} %",
                    Thumb = thumb
                };
                _alleSuchTreffer.Add((erg, t.Score));
                HatTrefferCache = true;

                if (t.Score >= schwelle)
                {
                    SuchErgebnisse.Add(erg);
                    angezeigt++;
                }

                int fertig = i + 1;
                SerieFortschritt = (int)(fertig * 100.0 / gesamt);
                double proSek = fertig / Math.Max(uhr.Elapsed.TotalSeconds, 0.001);
                int restSek = (int)Math.Ceiling((gesamt - fertig) / Math.Max(proSek, 0.001));

                // Sobald die Schwelle unterschritten ist, laufen nur noch Kandidaten für
                // den Regler ein – das ehrlich benennen, sonst wirkt es wie Stillstand.
                string was = t.Score >= schwelle
                    ? $"Lade Treffer… {angezeigt} angezeigt"
                    : $"Lade Reserve für den Regler… {fertig}/{gesamt}";

                string restText = FormatiereRestzeit(restSek);
                SucheStatus = restText.Length > 0 ? $"{was} – noch ~{restText}" : was;
            }
        }

        /// <summary>
        /// Zeigt aus dem geladenen Kandidatensatz (<see cref="_alleSuchTreffer"/>, absteigend
        /// sortiert) nur die Bilder ab der Schema-Slider-Schwelle. Wird beim Suchen und bei
        /// jeder Slider-Bewegung aufgerufen – ohne erneute Suche.
        /// </summary>
        private void RenderSchemaAehnlich()
        {
            HatTrefferCache = _alleSuchTreffer.Count > 0;
            SuchErgebnisse.Clear();
            float min = (float)(SchemaAehnlichkeitProzent / 100.0);

            int gezeigt = 0;
            float niedrigster = 0f;
            float hoechsterVerworfen = -1f;
            foreach (var (erg, score) in _alleSuchTreffer)
            {
                if (score < min)
                {
                    // Liste ist absteigend → der erste Verworfene ist der höchste.
                    if (hoechsterVerworfen < 0f)
                    {
                        hoechsterVerworfen = score;
                    }

                    continue;
                }

                SuchErgebnisse.Add(erg);
                niedrigster = score;   // Liste ist absteigend → letzter Treffer = niedrigster
                gezeigt++;
            }

            // Abstand zwischen letztem Treffer und erstem verworfenen Bild. Je grösser
            // die Lücke, desto sauberer trennt die Schwelle — die Kennzahl zum Vergleich
            // von rohem und kalibriertem Modus.
            string luecke = gezeigt > 1 && hoechsterVerworfen >= 0f
                ? $", Lücke {(niedrigster - hoechsterVerworfen) * 100f:F0} Pkt."
                : string.Empty;

            string modus = SchemaKalibrierungAktiv
                ? $" [kalibriert, {SchemaKalibrierungKomponenten} PC]"
                : " [roh]";

            SucheStatus = gezeigt <= 1
                ? $"Keine schema-ähnlichen Bilder ≥ {SchemaAehnlichkeitProzent:F0} %.{modus}"
                : $"{gezeigt} schema-ähnliche Bilder (≥ {SchemaAehnlichkeitProzent:F0} %, niedrigster {niedrigster * 100f:F0} %{luecke}).{modus}";

            CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
        }

        private bool CanExecuteErweiterteSerieSuche()
        {
            return SelectedBildchen != null
                && !string.IsNullOrEmpty(SelectedBildchen.BName)
                && AktuellerOrdnerIndiziert   // arbeitet ebenfalls auf dem Index
                && !SerieSucheLaeuft;
        }

        [RelayCommand(CanExecute = nameof(CanExecuteErweiterteSerieSuche), IncludeCancelCommand = true)]
        private async Task CommandExecuteErweiterteSerieSuche(CancellationToken token)
        {
            string? bildPfad = SelectedBildchen?.BName;
            if (string.IsNullOrEmpty(bildPfad))
            {
                return;
            }

            string? ordner = Path.GetDirectoryName(bildPfad);
            if (string.IsNullOrEmpty(ordner))
            {
                return;
            }

            SuchErgebnisse.Clear();
            LeereTrefferCache();
            CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
            ErgebnisseSindSchemaAehnlich = false;   // andere Suche → Schema-Slider ausblenden
            SchliesseIndexOrdnerKarte();
            SucheStatus = $"Erweiterte Seriensuche für '{Path.GetFileName(bildPfad)}'…";
            SerieFortschritt = 0;
            SerieIndeterminate = false;   // echter %-Balken mit Restzeit
            SerieSucheLaeuft = true;

            // Meldet Prozent + geschätzte Restzeit aus der BFS-Schleife live in die
            // Statuszeile – dort schaut man beim Suchen hin. Der Report kommt aus dem
            // Hintergrund-Thread; per Dispatcher sicher auf den UI-Thread umgeleitet
            // (sonst wirft das Setzen von SucheStatus eine stille Cross-Thread-Exception).
            string basisName = Path.GetFileName(bildPfad);
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            var fortschritt = new Progress<(int Prozent, int RestSekunden)>(p =>
            {
                dispatcher.Invoke(() =>
                {
                    SerieFortschritt = p.Prozent;
                    SucheStatus = p.RestSekunden > 0
                        ? $"Erweiterte Seriensuche für '{basisName}'… {p.Prozent} % – noch ~{p.RestSekunden} s"
                        : $"Erweiterte Seriensuche für '{basisName}'… {p.Prozent} %";
                });
            });

            try
            {
                await StelleClipBereitAsync();

                var treffer = await _bildAnalyse.SucheNachErweiterterSerieAsync(
                    ordner, bildPfad, minSim: 0.85f, fortschritt, token);

                // Karteileichen des Index aussortieren. Der Index behält Einträge zu
                // gelöschten oder weggeschobenen Dateien; ungefiltert standen sie hier
                // als schwarze, leere Kästchen zwischen den Treffern.
                treffer = NurVorhandene(treffer);

                if (treffer.Count <= 1)
                {
                    SucheStatus = "Keine erweiterte Serie gefunden – keine Kette visuell ähnlicher Bilder.";
                    return;
                }

                _letzteFrage = "Erweiterte Serie: " + Path.GetFileName(bildPfad);
                await ZeigeSerieTrefferAsync(treffer, token);

                SucheStatus = $"{treffer.Count} Bilder in erweiterter Serie (Kettensuche ≥ 85 %).";
                CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
            }
            catch (OperationCanceledException)
            {
                SucheStatus = "Erweiterte Seriensuche abgebrochen.";
            }
            catch (Exception ex)
            {
                SucheStatus = "Fehler bei erweiterter Seriensuche: " + ex.Message;
            }
            finally { SerieSucheLaeuft = false; }
        }

        /// <summary>
        /// Lädt die Thumbnails der Treffer nacheinander und fügt sie einzeln ein.
        /// Dabei zählt SerieFortschritt hoch und die Statuszeile zeigt „i/N … noch ~Xs".
        /// Diese Ladephase ist beim ersten Durchlauf der eigentlich lange, sichtbare
        /// Teil (jedes Bild muss dekodiert werden), daher liegt die Restzeit hier.
        /// </summary>
        private async Task ZeigeSerieTrefferAsync(
            System.Collections.Generic.IReadOnlyList<(string Path, float Score)> treffer, CancellationToken token)
        {
            ErgebnisseSindSchemaAehnlich = false;   // Serientreffer: nicht der Schema-Slider
            SerieIndeterminate = false;   // ab jetzt echter Prozent-Fortschritt
            SerieFortschritt = 0;

            int gesamt = treffer.Count;
            var uhr = System.Diagnostics.Stopwatch.StartNew();

            // Vorausladen wie bei den Schema-Kandidaten: mehrere Dekodierungen
            // gleichzeitig, abgewartet der Reihe nach – die Sortierung nach Ähnlichkeit
            // bleibt erhalten.
            var vorauslader = ErzeugeVorauslader(treffer, token);

            for (int i = 0; i < gesamt; i++)
            {
                token.ThrowIfCancellationRequested();
                var t = treffer[i];
                var thumb = await vorauslader();

                var erg = new SuchErgebnis
                {
                    Path = t.Path,
                    DateiName = Path.GetFileName(t.Path),
                    ProzentText = $"{t.Score * 100f:F0} %",
                    Thumb = thumb
                };
                _alleSuchTreffer.Add((erg, t.Score));
                SuchErgebnisse.Add(erg);

                int fertig = i + 1;
                SerieFortschritt = (int)(fertig * 100.0 / gesamt);

                // Restzeit aus bisheriger Ladegeschwindigkeit hochrechnen.
                double proSek = fertig / Math.Max(uhr.Elapsed.TotalSeconds, 0.001);
                int restSek = (int)Math.Ceiling((gesamt - fertig) / Math.Max(proSek, 0.001));
                string restText = FormatiereRestzeit(restSek);
                SucheStatus = restText.Length > 0
                    ? $"Lade Vorschaubilder… {fertig}/{gesamt} – noch ~{restText}"
                    : $"Lade Vorschaubilder… {fertig}/{gesamt}";
            }
        }

        #endregion

        #region Command Image Maximieren Toggle

        [RelayCommand]
        private void CommandExecuteImageMaximierenToggle()
        {
            IsImageMaximiert = !IsImageMaximiert;
        }

        #endregion

        #region Command Alle Bilder neu einlesen

        private bool CanExecuteCommandAlleBilderNeuEinlesen()
        {
            if (PrüfungLäuft)
            { return false; }

            return OcAufgabens.Any(x => x.BildFürLinks) || (AlterDropCount != OcAufgabens.Count);
        }

        [RelayCommand(CanExecute = nameof(CanExecuteCommandAlleBilderNeuEinlesen))]
        private async Task CommandExecuteAlleBilderNeuEinlesen()
        {
            // Pfad des zuletzt gewählten Bildes merken – nach dem Neu-Einlesen wieder auswählen.
            string? zuvorGewaehlt = SelectedBildchen?.BName;

            try
            {
                PrüfungLäuft = true;

                // Filter und Sortierung wieder scharf schalten. „In Liste übernehmen"
                // hatte beide abgeschaltet, damit die Trefferliste in ihrer Rangfolge
                // stehen bleibt. Ab hier gilt wieder die Explorer-Reihenfolge.
                //
                // Beim Filter zuweisen statt +=, sonst hinge das Prädikat nach jedem
                // Einlesen ein weiteres Mal daran.
                AufgabenView.Filter = PersonViewSource_Filter;
                AufgabenView.CustomSort = new NaturalStringComparer();

                // Vom zuletzt gewählten Bild ausgehen, nicht vom ursprünglich abgelegten.
                //
                // OnFileDrop zeigt den übergebenen Pfad sofort an und wählt ihn danach
                // aus. Mit DropDateiName sähe man beim Aktualisieren also kurz das alte
                // Drop-Bild aufblitzen und erst danach das Bild, bei dem man steht.
                //
                // Nur, wenn die Datei noch existiert und im selben Ordner liegt: Ein
                // bereits nach kein_Fav verschobenes Bild würde sonst den Unterordner
                // einlesen statt den eigentlichen.
                string startPfad = DropDateiName;

                if (!string.IsNullOrEmpty(zuvorGewaehlt)
                    && File.Exists(zuvorGewaehlt)
                    && string.Equals(
                        Path.GetDirectoryName(zuvorGewaehlt),
                        Path.GetDirectoryName(DropDateiName),
                        StringComparison.OrdinalIgnoreCase))
                {
                    startPfad = zuvorGewaehlt;
                }

                // OnFileDrop(string[] filepaths) neu initialisieren, um die Bilder neu einzulesen
                var dateien = new string[] { startPfad };

                // Derselbe Ordner wird neu eingelesen – Trefferliste bleibt gültig.
                await OnFileDrop(dateien, verwerfeSuchtreffer: false);
            }
            catch (Exception ex)
            {
                // Zweite Sicherung hinter der Ordnerprüfung in OnFileDrop.
                //
                // Dieser Befehl ist ein AsyncRelayCommand: Was hier hochkommt, wirft der
                // Befehl auf dem Oberflächen-Faden erneut, und weil die Anwendung keinen
                // DispatcherUnhandledException-Behandler hat, endet der Prozess. Beim
                // Neu-Einlesen sind Dateifehler aber der Normalfall — der Ordner liegt
                // auf einer Platte, die jemand anders gerade verändert. Ein Satz in der
                // Statuszeile ist die richtige Antwort darauf, kein Abbruch.
                LabelDropContent = "Neu-Einlesen fehlgeschlagen: " + ex.Message;
            }
            finally
            {
                PrüfungLäuft = false;
            }

            // Zuletzt gewähltes Bild wieder auswählen. OnFileDrop legt neue MeinBildchen-
            // Instanzen an, daher über den Pfad (BName) suchen statt über die Referenz.
            // Das Setzen von SelectedBildchen aktualisiert Anzeige und zentriert die Miniatur.
            var wieder = string.IsNullOrEmpty(zuvorGewaehlt)
                ? null
                : OcAufgabens.FirstOrDefault(
                    b => string.Equals(b.BName, zuvorGewaehlt, StringComparison.OrdinalIgnoreCase));

            if (wieder != null)
            {
                SelectedBildchen = wieder;
            }
            else if (OcAufgabens.Count > 0)
            {
                // Das zuletzt gewählte Bild ist nicht mehr da – etwa weil es gerade
                // verschoben wurde. Dann auf das erste Bild gehen, statt die Auswahl
                // stehen zu lassen: Sie zeigte sonst auf einen Eintrag, den es in der
                // neu eingelesenen Liste nicht mehr gibt.
                AufgabenView.MoveCurrentToFirst();
            }
        }

        [ObservableProperty]
        public partial string DropDateiName { get; set; }

        [ObservableProperty]
        public partial int AlterDropCount { get; set; }

        #endregion

        #region Command Ordner der Anwendung öffnen
        private bool CanExecuteCommandOrdnerDerAnwendungÖffnen()
        {
            return true;
        }
        [RelayCommand(CanExecute = nameof(CanExecuteCommandOrdnerDerAnwendungÖffnen))]
        private void CommandExecuteOrdnerDerAnwendungÖffnen()
        {
            //  string anwendungsOrdner = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                //if (Directory.Exists(anwendungsOrdner))
                //{
                //    Process.Start(new ProcessStartInfo
                //    {
                //        FileName = anwendungsOrdner,
                //        UseShellExecute = true,
                //        Verb = "open"
                //    });
                //}


                string exePath = Environment.ProcessPath!;
                string exeDir = Path.GetDirectoryName(exePath)!;

                Process.Start(new ProcessStartInfo
                {
                    FileName = exeDir,
                    UseShellExecute = true
                });

            }
            catch
            {
                // bewusst ignoriert (oder Logging) 
            }


        #endregion
        }
    }
}





