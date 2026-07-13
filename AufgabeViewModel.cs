using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emgu.CV;
using Emgu.CV.CvEnum;
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
using WebcamMikroMonitor.Common.Devices;

namespace TestImage
{
    public partial class AufgabeViewModel : ObservableObject, IFileDragDropTarget    /*ModelBase,*/
    {
        // v2x.0.367.242 Beta 2026-01-31 (.NETCore v9.0)
        // v2x.0.300.842 Beta 2026-02-08 (.NETCore v9.0)
        // v2x.0.300.842 Beta 2026-02-08 (.NETCore v9.0)
        // v2x.0.195.838 Beta 2026-04-23 (.NETCore v10.0)
        // v2x.0.175.654 Beta 2026-04-24 (.NETCore v10.0)
        [ObservableProperty]
        public partial string Version { get; set; } = "v2x.0.172.205 Beta 2026-06-27 (.NETCore net10.0)";

        [ObservableProperty]
        private int _CountInnerZählerTest;


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteVerschiebenZurückCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildInsKeinFavVerzeichnisVerschiebenCommand))]
        public partial string BildchenVorher { get; set; } = string.Empty;

        private string _filterText = string.Empty;


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildInsKeinFavVerzeichnisVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildStretchAnpassenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildLinksCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildNachRechtsCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteAlleBilderInsKeinFavVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteSuchenGleichesBildByteVergleichCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteSuchenUngefährGleichesBildCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteSuchenUngefährGleichesBildEmguCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteBildInsKIFehlerVerschiebenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteAlleBilderMiteinanderAufByteGleichheitPrüfenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteAlleBilderNeuEinlesenCommand))]
        public partial bool PrüfungLäuft { get; set; } = false;


        /// <summary>
        /// True wenn alle Bilder nach kein_Fav verschoben wurden (kein BildFürLinks==false mehr).
        /// Hintergrund soll dann rot werden.
        /// </summary>
        [ObservableProperty]
        public partial bool AlleBilderVerschoben { get; set; } = false;

        [ObservableProperty]
        public partial int InnerZählerCount { get; set; } = 0;

        [ObservableProperty]
        public partial string ProzentAbgleich { get; set; } = "0";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteSuchenUngefährGleichesBildEmguCommand))]
        public partial int BildAbgleichProzent { get; set; } = 80;

        /// <summary>
        /// Gets or sets a value indicating whether the image file is damaged.
        /// <br>  Converter dazu:  CLconverterBrushesBoolianG1</br>
        /// </summary>
        [ObservableProperty]
        private bool? _IsBildDateiBeschädigt = false;


        /// <summary>
        /// Gets or sets a value indicating whether the header matches the extension.
        /// <br>  Converter dazu:  CLconverterBrushesBoolianG2</br>
        /// </summary>
        [ObservableProperty]
        private bool? _IsHeaderPassendZurErweiterung = false;


        /// <summary>
        /// Gets or sets a value indicating whether a frame is present in the image.
        /// <br>  Converter dazu:  CLconverterBrushesBoolianG2</br>
        /// </summary>
        [ObservableProperty]
        private bool? _IsFrameImBildDrin = false;


        /// <summary>
        /// Gets or sets a value indicating whether the Bild download is corrupted.
        /// <br>    Converter dazu:  CLconverterBrushesBoolianG1</br>
        /// </summary>
        [ObservableProperty]
        private bool? _IsBildDownloadCorrupted = false;

        /// <summary>
        /// Gets or sets a value indicating whether the image file is null or missing.
        /// <br>  Converter dazu:  CLconverterBrushesBoolianG2</br>
        /// </summary>
        [ObservableProperty]
        private bool? _IsBildNullDatei = false;


        [ObservableProperty]
        private string _LabelDropContent = "⓵ mvvmDrop";
        private bool _zehnBilderAnzeigen = false;

        [ObservableProperty]
        private ImageSource _Bildchen = null;

        [ObservableProperty]
        private bool _SollBildGeprüftWerden = false;

        [ObservableProperty]
        private double _PercentageValueVerschieben;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteAlleBilderMiteinanderAufByteGleichheitPrüfenCommand))]
        private bool _MultiByteParallelGleichheit = true;


        [ObservableProperty]
        private bool _IsObenMinimiert = true;

        [ObservableProperty]
        private bool _IsImageMaximiert = false;

        [ObservableProperty]
        private bool _isWebcamAktiv;

        [ObservableProperty]
        private bool _isMikrofonAktiv;

        [ObservableProperty]
        private bool _isScreenShareAktiv;

        private readonly System.Windows.Threading.DispatcherTimer _geraeteTimer;

        private void GeraeteTimerTick(object? sender, EventArgs e)
        {
            IsWebcamAktiv = DeviceMonitor.IstAktiv("webcam");
            IsMikrofonAktiv = DeviceMonitor.IstAktiv("microphone");
            IsScreenShareAktiv = DeviceMonitor.IstAktiv("screenCapture");
        }

        #region UI_Output
        [ObservableProperty]
        public partial int OriginalImageWidth { get; set; } = -1;
        [ObservableProperty]
        public partial int OriginalImageHeight { get; set; } = -1;
        #endregion

        // [ObservableProperty]
        private int _aufgabenViewIndex = 0;
        private int _AufgabenViewIndex
        {
            get => _aufgabenViewIndex;
            set
            {
                if (SetProperty(ref _aufgabenViewIndex, value))
                {
                    // 10 Bilder in die View bringen
                    //MeUI_10BilderInViewBringen();
                }
            }
        }




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

            ocAufgabensKlein = new ObservableCollection<MeinBildchen>();
            ocAufgabensKlein.Add(new MeinBildchen() { BName = @".\.\HeavyO.jpg", BildFürLinks = false });
            ocAufgabensKlein.Add(new MeinBildchen() { BName = @".\.\HeavyO.jpg", BildFürLinks = true });
            //OcLinkeBilder = new ObservableCollection<MeinBildchen>();
            //{
            //    @"C:\Users\Bill-6e\Desktop\ZL4\Test 1\20200619_184646.jpg"
            //};
            //BName = @"C:\Users\Bill-6e\Desktop\ZL4\Test 1\20200619_184646.jpg"
            ocAufgabens.Add(new MeinBildchen() { BName = @".\.\HeavyO.jpg", BildFürLinks = false });
            ocAufgabens.Add(new MeinBildchen() { BName = @".\.\HeavyO.jpg", BildFürLinks = true });

            //  DisplayImage = MieneServices.CreateBitmap(SelectedBildchen?.BName);


            AufgabenView = CollectionViewSource.GetDefaultView(ocAufgabens) as ListCollectionView;
            AufgabenView.SortDescriptions.Clear();
            AufgabenView.CustomSort = new NaturalStringComparer();

            // initialisieren der AufgabenViewKlein

            //ocAufgabensKlein = ocAufgabens;
            AufgabenViewKlein = new ListCollectionView(ocAufgabensKlein);


            //AufgabenViewKlein = CollectionViewSource.GetDefaultView(ocAufgabensKlein) as ListCollectionView;
            // Läuft Halbwegs
            //AufgabenView.CurrentChanged += new EventHandler(TerminsView_CurrentChanged);
            //AufgabenView.CurrentChanged += (s, e) =>
            //{
            //    //RaisePropertyChanged(() => TerminModel);
            //    //base. OnPropertyChanged("TerminModel");
            //    // neuer Versuch
            //    base.OnPropertyChanged(() => MeinBildchen);
            //};
            //foreach (var item in ocAufgabens)
            //{
            //    item.PropertyChanged += PersonsOnPropertyChanged;
            //}
            AufgabenView.Filter += PersonViewSource_Filter;
            //AufgabenView.SortDescriptions.Clear();
            //AufgabenView.SortDescriptions.Add(new SortDescription(nameof(MeinBildchen.BName), ListSortDirection.Descending));
            // Grupieren
            ////CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(lvUsers.ItemsSource);
            //PropertyGroupDescription groupDescription = new PropertyGroupDescription(nameof(TerminModel.Terminbezeichnung));
            //AufgabenView.GroupDescriptions.Add(groupDescription);

            // </-------


        }

        private bool CanExecuteBildNachLinksCommand()
        {
            //if (((AufgabenView.CurrentPosition) > 0) & (!PrüfungLäuft))
            //{
            //    return true;
            //}
            //else
            //{
            //    return false;
            //}


            return !PrüfungLäuft
                    && AufgabenView != null
                    && AufgabenView.CurrentPosition > 0;

        }







        [RelayCommand(CanExecute = nameof(CanExecuteBildNachLinksCommand))]
        private void CommandExecuteBildLinks()
        {
            ////AufgabenView.MoveCurrentToPrevious();

            //// nächstes Bild anzeigen
            //var indexBild = AufgabenView.CurrentPosition;
            //if (AufgabenView.Count >= indexBild + 1)
            //{
            //    while ((AufgabenView.CurrentPosition > -1) & (indexBild > 0))
            //    {
            //        var pos = AufgabenView.GetItemAt(indexBild - 1) as MeinBildchen;
            //        indexBild--;

            //        if (pos != null && pos.BildFürLinks == false)
            //        {
            //            AufgabenView.MoveCurrentToPosition(indexBild);
            //            break;
            //        }

            //        //144 if Anfang erreicht, dann erstes Bild anzeigen
            //        if (indexBild == 0)
            //        {
            //            AufgabenView.MoveCurrentToPosition(0);

            //            AufgabenView.Refresh();

            //        }

            //        // Bildchen entfernen
            //        var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == SelectedBildchen?.BName);
            //        //var indexSelected = AufgabenView.CurrentPosition;

            //        if (bildchen != null)
            //        {
            //            // Löschen if Bild nicht mehr da ist
            //            if (!File.Exists(bildchen.BName))
            //            {
            //                OcAufgabens.Remove(bildchen);
            //                //AufgabenView.MoveCurrentToNext();

            //                //AufgabenView.Refresh();

            //                // Spruch
            //                // If Not Program.isWorking Then Code.Debug Else Code.DoNotTouch
            //            }

            //        }


            //    }
            //}

            // Copilot Code

            // 422


            if (AufgabenView.CurrentPosition < 0)
            {
                return;
            }

            //// Falls aktuelles Bild physisch nicht mehr existiert → entfernen
            //var current = SelectedBildchen;
            //if (current != null && !File.Exists(current.BName))
            //{
            //    OcAufgabens.Remove(current);
            //}

            // Falls das aktuell selektierte Bild physisch nicht mehr existiert → entfernen
            var current = SelectedBildchen;
            if (current != null && !File.Exists(current.BName))
            {
                if (RemoveMissingFilesBulk())
                {
                    // Zustand wurde repariert → nächste Aktion bewusst im nächsten Klick
                    // return;
                }
            }

            // Kein aktuelles Element → nichts zu tun
            if (AufgabenView.CurrentPosition <= 0 || AufgabenView.Count == 0)
            {
                return;
            }

            // Eine eindeutige Ausgangsposition
            int startIndex = AufgabenView.CurrentPosition;

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


        private bool CanExecuteBildNachRechtsCommand()
        {
            return !PrüfungLäuft && AufgabenView != null && AufgabenView.CurrentPosition >= 0
                 && AufgabenView.CurrentPosition < AufgabenView.Count - 1;
        }



        [RelayCommand(CanExecute = nameof(CanExecuteBildNachRechtsCommand))]
        private void CommandExecuteBildNachRechts()
        {
            // Copilot Code


            // Falls das aktuell selektierte Bild physisch nicht mehr existiert → entfernen
            var current = SelectedBildchen;
            if (current != null && !File.Exists(current.BName))
            {
                if (RemoveMissingFilesBulk())
                {
                    // Zustand wurde repariert → nächste Aktion bewusst im nächsten Klick
                    // return;
                }
            }

            // Kein aktuelles Element → nichts zu tun
            if (AufgabenView.CurrentPosition < 0 || AufgabenView.Count == 0)
            {
                return;
            }


            // Ausgangsposition festhalten (eine einzige Index-Wahrheit)
            int startIndex = AufgabenView.CurrentPosition;

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


        private bool RemoveMissingFilesBulk()
        {
            var neueListe = OcAufgabens
                   .Where(b => File.Exists(b.BName))
                   .ToList();

            if (neueListe.Count == OcAufgabens.Count)
            {
                return false; // nichts geändert
            }

            OcAufgabens.Clear();

            PrüfungLäuft = true;

            foreach (var item in neueListe)
            {
                OcAufgabens.Add(item);
            }

            PrüfungLäuft = false;

            return true;

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

                    // auf 10 Einträge begrenzen

                    //var indexSelecded = _AufgabenViewIndex;
                    //var hhh = AufgabenView.Count;

                    //if (indexSelecded == -1) return true;


                    //var kl = indexSelecded - 2;
                    //if (kl < 0)
                    //{
                    //    kl = 0;
                    //}

                    //var gr = indexSelecded + 2;
                    //if (gr > ocAufgabens.Count)
                    //{
                    //    gr = ocAufgabens.Count - 1;
                    //}


                    //var test = ocAufgabens.IndexOf(aufgabe);

                    //if (test >= kl & test <= gr)
                    //{

                    //    Debug.WriteLine($"Index: {indexSelecded} kl: {kl} gr: {gr}  test: {test}");
                    //    return true;
                    //}
                    //else
                    //{
                    //    return false;
                    //}
                }

                else
                {
                    //    return aufgabe.BName.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) >= 0;
                    return aufgabe.BName.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
        }

        private ObservableCollection<MeinBildchen> ocAufgabens { get; set; }
        private ObservableCollection<MeinBildchen> ocAufgabensKlein { get; set; }







        public async Task OnFileDrop(string[] filepaths)
        {
            // 1570

            //throw new NotImplementedException();

            if (filepaths == null)
            {
                return;
            }

            InnerZählerCount++;


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
                    LabelDropContent = "nix .jpg";
                    //KnalNenFehlerSoundRein();
                    return;
                }

                // !fullDateiName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)

                LabelDropContent = Path.GetFileName(fullDateiName);
                DropDateiName = fullDateiName;

                ocAufgabens.Clear();
                OnPropertyChanged(nameof(CountBildchenFürLinks));

                // Über die interface Files einlesen
                var cl = new Files.CLdateienEnlesen();
                var dateies = cl.DateienEinlesenAsync(Path.GetDirectoryName(fullDateiName), false);

                int index = 0;
                await foreach (var datei in dateies)
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

                //ocAufgabens?.OrderBy(k => System.IO.Path.GetFileName(k.BName));
                //MeUI_10BilderInViewBringen();


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
                var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == fullDateiName);
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
            }









        }


        public ListCollectionView AufgabenView { get; }
        public ListCollectionView AufgabenViewKlein { get; }

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
                    AufgabenView.Refresh();
                    CommandExecuteFilterLeerenCommandCommand?.NotifyCanExecuteChanged();

                    // evtl Filter löschen oder setzen
                    if (string.IsNullOrEmpty(_filterText))
                    {
                        // Filter löschen
                        AufgabenView.Filter -= PersonViewSource_Filter;
                    }
                    else
                    {
                        // Filter setzen
                        AufgabenView.Filter += PersonViewSource_Filter;
                    }


                }
            }
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



                _AufgabenViewIndex = ocAufgabens.IndexOf(value);

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



                //AufgabenView.MoveCurrentTo(value);
                AufgabenView.MoveCurrentTo(value);
                OnPropertyChanged(nameof(SelectedBildchen.BildFürLinks));

                OnPropertyChanged();


                // Commands schauen
                CommandExecuteBildNachRechtsCommand?.NotifyCanExecuteChanged();
                CommandExecuteBildLinksCommand?.NotifyCanExecuteChanged();
                CommandExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand?.NotifyCanExecuteChanged();
                CommandExecuteBildInsKeinFavVerzeichnisVerschiebenCommand?.NotifyCanExecuteChanged();
                CommandExecuteBildStretchAnpassenCommand?.NotifyCanExecuteChanged();
                CommandExecuteAlleBilderInsKeinFavVerschiebenCommand?.NotifyCanExecuteChanged();
                CommandExecuteSuchleisteToggleCommand?.NotifyCanExecuteChanged();
                //  CommandExecuteAlleBilderMiteinanderAufByteGleichheitPrüfenCommand?.NotifyCanExecuteChanged();

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
                return (OcAufgabens.Count > 0) & File.Exists(SelectedBildchen.BName)
                     & (AufgabenView.CurrentPosition <= AufgabenView.Count
                    & (!SelectedBildchen.BName.Contains("kein_Fav")) & !PrüfungLäuft);
            }


        }

        [RelayCommand(CanExecute = nameof(CanExecuteBildInsKeinFavVerzeichnisVerschiebenCommand))]
        private async Task CommandExecuteBildInsKeinFavVerzeichnisVerschieben()
        {
            //PrüfungLäuft = true;

            //// Verzeichnis kein_Fav anlegen wenn nicht vorhanden
            //string zielVerzeichnis = Path.Combine(Path.GetDirectoryName(SelectedBildchen.BName), "kein_Fav");
            //if (!Directory.Exists(zielVerzeichnis))
            //{
            //    Directory.CreateDirectory(zielVerzeichnis);
            //}
            //// Datei verschieben
            //var source = SelectedBildchen.BName;
            //string zielDateiName = Path.Combine(zielVerzeichnis, Path.GetFileName(source));
            //try
            //{
            //    // In die linke Collection hinzufügen
            //    // an die erste stelle
            //    //OcLinkeBilder.Insert(0, new MeinBildchen { BName = zielDateiName, BildFürLinks = true });

            //    if (!File.Exists(zielDateiName) & File.Exists(source))
            //    {
            //        var länge = new FileInfo(source).Length;
            //        if (länge != 0)
            //        {
            //            await Task.Run(() => File.Move(source, zielDateiName));
            //        }


            //        //// Aus der Collection entfernen
            //        //OcAufgabens.Remove(SelectedBildchen);
            //        //BildchenVorher = zielDateiName;

            //        var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == SelectedBildchen.BName);
            //        var indexSelected = AufgabenView.CurrentPosition;

            //        if (bildchen != null)
            //        {
            //            var index = OcAufgabens.IndexOf(bildchen);

            //            bildchen.BName = zielDateiName;
            //            bildchen.BildFürLinks = true;
            //            OnPropertyChanged(nameof(CountBildchenFürLinks));

            //            // Funktioniert so nicht
            //            //// den nächsten BildFürLinks = false finden und dort hin verschieben

            //            //// var nextIndex = OcAufgabens.IndexOf(bildchen) + 1;
            //            //var nextIndex = OcAufgabens.Skip(index).FirstOrDefault(x=>x.BildFürLinks==false);
            //            //if (nextIndex != null)
            //            //{
            //            //    var ni = OcAufgabens.IndexOf(nextIndex);
            //            //    OcAufgabens.Move(index, ni);
            //            //}
            //            //else
            //            //{
            //            //    // ans Ende verschieben
            //            //    OcAufgabens.Move(index, OcAufgabens.Count - 1);
            //            //}
            //            //12


            //            //OcAufgabens.Move(index, indexSelected);
            //        }

            //        // hier evtl eine checkbox einfügen ob zum nächsten Bild gesprungen werden soll

            //        var indexBild = AufgabenView.CurrentPosition;
            //        if (AufgabenView.Count >= indexBild + 1)
            //        {
            //            while ((AufgabenView.CurrentPosition + 1 < AufgabenView.Count) & (AufgabenView.Count > indexBild + 1))
            //            {
            //                var pos = AufgabenView.GetItemAt(indexBild + 1) as MeinBildchen;
            //                indexBild++;

            //                if (pos != null && pos.BildFürLinks == false)
            //                {
            //                    AufgabenView.MoveCurrentToPosition(indexBild);
            //                    break;
            //                }

            //                // if Ende erreicht, dann letzes Bild anzeigen
            //                if (AufgabenView.Count <= indexBild + 1)
            //                {
            //                    AufgabenView.MoveCurrentToPosition(AufgabenView.Count - 1);
            //                    CommandExecuteBildInsKeinFavVerzeichnisVerschiebenCommand?.NotifyCanExecuteChanged();
            //                }
            //                else
            //                {
            //                    ;
            //                }
            //            }
            //            AufgabenView.Refresh();
            //            //AufgabenViewKlein.Refresh();
            //        }
            //    }



            //}
            //catch (Exception ex)
            //{
            //    MessageBox.Show("Fehler beim Verschieben der Datei: " + ex.Message);
            //}
            //finally
            //{
            //    BildchenVorher = zielDateiName;

            //    AufgabenView.Refresh();
            //    UpdateAlleBilderVerschoben();

            //    PrüfungLäuft = false;
            //}

            // Copilot Code


            PrüfungLäuft = true;

            bool moveErfolgreich = false;

            var source = SelectedBildchen.BName;
            string zielVerzeichnis = Path.Combine(
                Path.GetDirectoryName(source),
                "kein_Fav");

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
        private bool CanExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand()
        {
            if (SelectedBildchen == null)
            {
                return false;
            }

            return /*(OcLinkeBilder.Count > 0)*/
                 !string.IsNullOrEmpty(SelectedBildchen.BName)
                & File.Exists(SelectedBildchen.BName)
                & (SelectedBildchen.BildFürLinks == true & (!PrüfungLäuft));
        }
        [RelayCommand(CanExecute = nameof(CanExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand))]
        private async Task CommandExecuteBildInsHauptVerzeichnisZuruckVerschieben()
        {
            //PrüfungLäuft = true;

            //// Datei ins Haupt-Verzeichnis zurück verschieben

            //var dateiname = Path.GetFileName(SelectedBildchen.BName);
            //// "C:\...\kein_Fav\lala[1].jpg"
            //var hauptVerzeichnis = Path.GetDirectoryName(Path.GetDirectoryName(SelectedBildchen.BName));

            //string zielVollPfad = Path.Combine(hauptVerzeichnis, dateiname);
            //try
            //{
            //    File.Move(SelectedBildchen.BName, zielVollPfad);
            //    // Aus der Collection entfernen
            //    //var altesBildchen = OcLinkeBilder.FirstOrDefault(b => b.BName == BildchenVorher);
            //    //OcLinkeBilder.Remove(altesBildchen);

            //    //SelectedBildchen.BName = zielVollPfad;
            //    //SelectedBildchen.BildFürLinks = false;

            //    //AufgabenView.Refresh();

            //    //aus
            //    //var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == SelectedBildchen.BName);
            //    //var indexSelected = AufgabenView.CurrentPosition;

            //    //if (bildchen != null)
            //    //{
            //    //    var index = OcAufgabens.IndexOf(bildchen);

            //    //    bildchen.BName = zielDateiName;
            //    //    bildchen.BildFürLinks = true;

            //    //    OcAufgabens.Move(index, indexSelected);
            //    //}

            //    var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == SelectedBildchen.BName);
            //    var indexSelected = AufgabenView.CurrentPosition;

            //    if (bildchen != null)
            //    {
            //        var index = OcAufgabens.IndexOf(bildchen);

            //        bildchen.BName = zielVollPfad;
            //        bildchen.BildFürLinks = false;
            //        OnPropertyChanged(nameof(CountBildchenFürLinks));

            //        OcAufgabens.Move(index, indexSelected);
            //    }


            //    //// Position in der OcAufgabens finden, es ist die SelctedBildchen
            //    //// und dort einfügen
            //    //var insertIndex = 0;
            //    //if (SelectedBildchen != null)
            //    //{
            //    //    insertIndex = AufgabenView.CurrentPosition;

            //    //    // In die rechte Collection hinzufügen
            //    //    // an die erste stelle
            //    //    //OcAufgabens.Insert(insertIndex, new MeinBildchen { BName = zielVollPfad });



            //    //}

            //    BildchenVorher = string.Empty;

            //    //AufgabenView.MoveCurrentToPosition(0);
            //    SelectedBildchen = null;
            //    AufgabenView.MoveCurrentToPosition(indexSelected);
            //    //AufgabenView.Refresh();
            //    //AufgabenView.Refresh();



            //}
            //catch (Exception ex)
            //{
            //    MessageBox.Show("Fehler beim Verschieben der Datei: " + ex.Message);
            //}

            //finally
            //{
            //    AufgabenView.Refresh();
            //    UpdateAlleBilderVerschoben();

            //    PrüfungLäuft = false;
            //    //AufgabenViewKlein.Refresh();
            //}


            PrüfungLäuft = true;

            bool moveErfolgreich = false;

            var source = SelectedBildchen.BName;
            var dateiname = Path.GetFileName(source);
            var hauptVerzeichnis = Path.GetDirectoryName(Path.GetDirectoryName(source));
            var zielVollPfad = Path.Combine(hauptVerzeichnis, dateiname);

            try
            {
                if (!File.Exists(zielVollPfad) && File.Exists(source))
                {
                    await Task.Run(() => File.Move(source, zielVollPfad));
                    CLconverterStringZuKleinemImage.InvalidateCache(source);
                    moveErfolgreich = true;
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

            var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == source);
            if (bildchen != null)
            {
                var index = OcAufgabens.IndexOf(bildchen);
                var indexSelected = AufgabenView.CurrentPosition;

                bildchen.BName = zielVollPfad;
                bildchen.BildFürLinks = false;

                OnPropertyChanged(nameof(CountBildchenFürLinks));
                OcAufgabens.Move(index, indexSelected);
            }

            BildchenVorher = string.Empty;

            AufgabenView.MoveCurrentToPosition(AufgabenView.CurrentPosition);
            AufgabenView.Refresh();
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
            return !string.IsNullOrEmpty(BildchenVorher)
                & File.Exists(BildchenVorher);
        }
        [RelayCommand(CanExecute = nameof(CanExecuteVerschiebenZurück))]
        private void CommandExecuteVerschiebenZurück()
        {
            var vorherSelectedFullName = BildchenVorher;
            var vorherSelectedName = Path.GetFileName(vorherSelectedFullName);


            // Datei ins Haupt-Verzeichnis zurück verschieben
            var dateiname = Path.GetFileName(BildchenVorher);
            // "C:\Users\Bill-6e\Desktop\ZL4\Test 1\he17_同人CG集2025-09-10\kein_Fav\Printemps by DavidMnr on DeviantArt[1].jpg"
            var keinFavVerzeichnis = Path.GetDirectoryName(Path.GetDirectoryName(BildchenVorher));
            string zielVollPfad = Path.Combine(keinFavVerzeichnis, dateiname);
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

        private bool CanExecuteKleinesBildGrossesBildLaden()
        {
            return File.Exists(SelectedBildchen?.BName);
        }

        [RelayCommand(CanExecute = nameof(CanExecuteKleinesBildGrossesBildLaden))]
        private async Task CommandExecuteKleinesBildGrossesBildLaden()
        {
            // sichere Kopie des Pfads
            var path = SelectedBildchen?.BName;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            // Zeit stoppen
            var stopwatch = Stopwatch.StartNew();

            int decodeWidth = 0;
            int decodeHeight = 0;

            // Image pixel abfragen
            (OriginalImageWidth, OriginalImageHeight) = MieneServices.ReadOriginalSize(path);

            // Monitor‑Decode‑Größe
            (int monitorWidth, int monitorHeight) = MieneServices.GetMonitorDecodeSize();

            // Sicherheitsprüfung VOR Berechnung
            if (OriginalImageWidth <= 0 || OriginalImageHeight <= 0)
            {
                decodeWidth = monitorWidth;
                decodeHeight = monitorHeight;
            }
            else
            {
                double scale = Math.Min(
                    (double)monitorWidth / OriginalImageWidth,
                    (double)monitorHeight / OriginalImageHeight);

                // Nie hochskalieren
                scale = Math.Min(scale, 1.0);

                decodeWidth = (int)Math.Round(OriginalImageWidth * scale);
                decodeHeight = (int)Math.Round(OriginalImageHeight * scale);
            }



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

                // Lade Balcke
                ProgressValue = 1;

                // 1. Stufe: Kleines Vorschaubild laden (сто Pixel)
                var kl = await Task.Run(() => MieneServices.CreateBitmap(path, 100));
                ProgressValue = 1;

                SWkleinesBild = stopwatch.Elapsed.TotalMilliseconds.ToString("F3") + " ms";

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DisplayImage = kl;

                });

                // Lade Balcke
                ProgressValue = 2;

                stopwatch = Stopwatch.StartNew();

                // Grosses Bild nicht laden wenn CommandExecuteAlleBilderInsKeinFavVerschieben läuft
                if (CommandExecuteAlleBilderInsKeinFavVerschiebenCommand.IsRunning /*|| SelectedBildchen!=null*/)
                {
                    // Abbrechen, wenn der andere Befehl läuft

                    PrüfungLäuft = false;
                    return;
                }

                // 2. Stufe: Volles Bild laden
                var gr = await Task.Run(() => MieneServices.CreateBitmap(path, decodeWidth, decodeHeight));

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DisplayImage = gr;
                });

                // SWgrossesBild 
                SWgrossesBild = stopwatch.Elapsed.TotalMilliseconds.ToString("F3") + " ms";


                // Lade Balcke
                ProgressValue = 6;

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


                if (!File.Exists(SelectedBildchen?.BName))
                {
                    IsBildDateiBeschädigt = true;
                    IsHeaderPassendZurErweiterung = false;
                    IsFrameImBildDrin = false;
                    IsBildDownloadCorrupted = false;
                    IsBildNullDatei = true;

                    // Bildchen entfernen
                    var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == SelectedBildchen?.BName);
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
                var kl = await Task.Run(() => MieneServices.CreateBitmap(SelectedBildchen.BName, 100));
                ProgressValue = 1;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DisplayImage = kl;
                });

                //  ProgressValue = пятьдесят; // Vorschau ist da

                // Künstliche Verzögerung, damit man den Fortschritt sieht
                //await Task.Delay(20);

                // 2. Stufe: Volles Bild laden
                var gr = await Task.Run(() => MieneServices.CreateBitmap(SelectedBildchen.BName, decodeWidth, decodeHeight));
                ProgressValue = 2;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DisplayImage = gr;
                });

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
                        BildCheckCopilot.PruefeBildDatei(SelectedBildchen.BName));
                    IsBildDateiBeschädigt = r.IstBeschädigt;
                    IsHeaderPassendZurErweiterung = r.HeaderPasst;
                    IsFrameImBildDrin = r.HatFrame;
                    IsBildDownloadCorrupted = r.DownloadKorrupt;
                    IsBildNullDatei = r.IstNullDatei;
                    ErkanntesFormat = r.DetektiertesFormat;

                    Debug.WriteLine($"Header={r.HeaderPasst}, Frame={r.HatFrame}, " +
                                    $"Korrupt={r.DownloadKorrupt}, Null={r.IstNullDatei}, Format={r.DetektiertesFormat}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Bildprüfung Fehler: {ex}");
                    HeaderPasstZurErweiterung = false;
                    IsFrameImBildDrin = false;
                    IsBildDownloadCorrupted = true;
                    IsBildNullDatei = false;
                    ErkanntesFormat = "unknown";
                }
                finally
                {
                    PrüfungLäuft = false;
                    ProgressValue = 7;
                }

            }




        }

        [ObservableProperty]
        public partial bool HeaderPasstZurErweiterung { get; set; }

        [ObservableProperty]
        public partial string ErkanntesFormat { get; set; } = "unknown";

        #endregion




        [ObservableProperty]
        private BitmapSource _DisplayImage;

        [ObservableProperty]
        private bool _IsDisplayImageLoading;

        [ObservableProperty]
        private int _ProgressValue;






        [ObservableProperty]
        private Stretch _ImageStretch = Stretch.Uniform;

        [ObservableProperty]
        private ScrollBarVisibility _MyHorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

        [ObservableProperty]
        private string _SWkleinesBild = string.Empty;

        [ObservableProperty]
        private string _SWgrossesBild = string.Empty;





        #region Command Bild Stretch anpassen

        private bool CanExecuteBildStretchAnpassen()
        {
            return File.Exists(SelectedBildchen?.BName) && (!PrüfungLäuft);
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
            return OcAufgabens.Any(b => b.BildFürLinks == false)
                && (!PrüfungLäuft)
                && (SelectedBildchen != null && !SelectedBildchen.BName.Contains("kein_Fav"));
        }
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
            //var vorherSelectedFullName = SelectedBildchen?.BName;
            //var vorherSelectedName = Path.GetFileName(vorherSelectedFullName);
            //// 755
            //// Verzeichnis kein_Fav anlegen wenn nicht vorhanden
            //string zielVerzeichnis = Path.Combine(Path.GetDirectoryName(SelectedBildchen?.BName), "kein_Fav");
            //if (!Directory.Exists(zielVerzeichnis))
            //{
            //    Directory.CreateDirectory(zielVerzeichnis);
            //}

            //// Zur vorletzten Position springen
            //// Geht so schneller und der Abbruch Button wird nicht blockiert
            //if (ocAufgabens.Count > 2)
            //{
            //    // später wieder setzen
            //    // AufgabenView.MoveCurrentToPosition(AufgabenView.Count - 1);
            //}

            //// Bau mir mal eine Progressbar ein , butte
            //var sw = Stopwatch.StartNew();
            //DateTime started = DateTime.Now;
            //IProgress<CLProgressStückzahl> progressStück = new Progress<CLProgressStückzahl>(value => PercentageValueVerschieben = value.Percent);



            //var bilderZuVerschieben = OcAufgabens.Where(b => b.BildFürLinks == false).ToList();
            //long gszähler = bilderZuVerschieben.Count;
            //int zähler = 0;
            //foreach (var bildchen in bilderZuVerschieben)
            //{

            //    try
            //    {
            //        // Datei verschieben
            //        //string zielDateiName = Path.Combine(zielVerzeichnis, Path.GetFileName(SelectedBildchen.BName));
            //        string quellDateiFullName = bildchen.BName;
            //        var zielDateiName = Path.GetFileName(quellDateiFullName);
            //        //var zielPfad= Path.Combine(zielVerzeichnis, zielDateiName);
            //        var zielDateiFullName = Path.Combine(zielVerzeichnis, zielDateiName);

            //        // In die linke Collection hinzufügen
            //        // an die erste stelle
            //        //OcLinkeBilder.Insert(0, new MeinBildchen { BName = zielDateiName, BildFürLinks = true });



            //        if (File.Exists(quellDateiFullName) & !File.Exists(zielDateiFullName))
            //        {
            //            var länge = new FileInfo(quellDateiFullName).Length;
            //            if (länge != 0)
            //            {
            //                // Datei verschieben mit visual basic
            //                // in Task auslagern


            //                // Langsam
            //                //await Task.Run(() =>
            //                //{
            //                //    Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(quellDateiFullName, zielDateiFullName, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
            //                //}, token);

            //                // Schneller
            //                await MieneServices.CopyAndDeleteFileAsync(quellDateiFullName, zielDateiFullName, token);

            //                // Langsam
            //                //await Task.Run(() => File.Move(quellDateiFullName, zielDateiFullName));

            //                bildchen.BName = zielDateiFullName;
            //                bildchen.BildFürLinks = true;
            //                OnPropertyChanged(nameof(CountBildchenFürLinks));

            //                var pgs = new CLProgressStückzahl(started, gszähler, zähler++, false);

            //                progressStück?.Report(pgs);

            //                LabelDropContent = pgs.Restzeit;

            //                // prüfen ob original datei gelöscht wurde, sonnst lösche
            //                if (File.Exists(quellDateiFullName))
            //                {
            //                    // byte abglech der original Datei mit ziel Datei


            //                }
            //            }
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        MessageBox.Show("Fehler beim Verschieben der Datei: " + ex.Message);
            //    }
            //    finally
            //    {
            //        //BildchenVorher = zielVerzeichnis;
            //        //AufgabenView.Refresh();
            //    }


            //    // Kaufen
            //    // roborock Saros Z70 Saugroboter mit Wischfunktion, OmniGrip Arm, KI-gestützt
            //    // Roomba® Max 706 Combo-Roboter + AutoWash™ Dock – Schwarz

            //}

            //// Curent Position wiederherstellen
            //var wiederZuWählendesBildchen = OcAufgabens.FirstOrDefault(b => Path.GetFileName(b.BName) == vorherSelectedName);
            //if (wiederZuWählendesBildchen != null)
            //{
            //    SelectedBildchen = wiederZuWählendesBildchen;
            //}

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
            return OcAufgabens.Count > 1 && (!PrüfungLäuft);
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
                            ProzentAbgleich = pgs.Percent.ToString("F2");
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
            return OcAufgabens.Count > 1 && (!PrüfungLäuft);

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

        //Emgu.CV
        #region Command Ungefähr Gleiches Bild suchen, mit Emgu.CV ( max 10 % Abweichung  )

        private bool CanExecuteSuchenUngefährGleichesBildEmgu()
        {
            return OcAufgabens.Count > 1 && (!PrüfungLäuft);

        }

        [RelayCommand(CanExecute = nameof(CanExecuteSuchenUngefährGleichesBildEmgu), IncludeCancelCommand = true)]
        private async Task CommandExecuteSuchenUngefährGleichesBildEmgu(CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                PrüfungLäuft = true;

                await MeTa_SuchenUngefährGleichesBildEmgu(token);
            }
            catch (Exception)
            {

            }
            finally
            {
                PrüfungLäuft = false;
                PercentageValueVerschieben = 0.0;
                LabelDropContent = "Gs  " + sw.Elapsed.TotalSeconds.ToString("F2") + " Sek";
                ProzentAbgleich = string.Empty;

                AufgabenView.Refresh();
            }


        }

        private async Task MeTa_SuchenUngefährGleichesBildEmgu(CancellationToken token)
        {
            //throw new NotImplementedException();

            var sw = Stopwatch.StartNew();
            DateTime started = DateTime.Now;
            IProgress<CLProgressStückzahl> progressStück = new Progress<CLProgressStückzahl>(value => PercentageValueVerschieben = value.Percent);

            var bilder = OcAufgabens.ToList();
            long gszähler = bilder.Count - 1;
            int zähler = 0;

            Mat image1 = CvInvoke.Imread(SelectedBildchen?.BName, ImreadModes.AnyColor);

            foreach (var item in bilder)
            {
                // Command CommandExecuteSuchenUngefährGleichesBildEmgu Abbrechen
                // >>> Abbruch prüfen <<<
                token.ThrowIfCancellationRequested();


                var pgs = new CLProgressStückzahl(started, gszähler, zähler++, false);

                progressStück?.Report(pgs);

                LabelDropContent = pgs.Restzeit;
                ProzentAbgleich = pgs.Percent.ToString("F2");

                if (File.Exists(item.BName) & (item.BName != SelectedBildchen?.BName))
                {
                    //var gleich = await MieneServices.IsFileGleichAsync(SelectedBildchen?.BName, item.BName, token);
                    //if (!gleich)
                    //{
                    //    // Bildchen aus der Collection entfernen
                    //    OcAufgabens.Remove(item);
                    //}

                    //ulong hash1 = await MieneServices.GetImageHash(item.BName);
                    //ulong hash2 = await MieneServices.GetImageHash(SelectedBildchen?.BName);

                    //int distance = await MieneServices.HammingDistance(hash1, hash2);

                    try
                    {
                        //using var img1 = new Image<Bgr, byte>(SelectedBildchen?.BName);
                        //using var img2 = new Image<Bgr, byte>(item.BName);



                        //// Load two images (grayscale for feature detection)
                        //Mat img1 = CvInvoke.Imread(SelectedBildchen?.BName, ImreadModes.Grayscale);
                        //Mat img2 = CvInvoke.Imread(item.BName, ImreadModes.Grayscale);

                        // double similarity = await MieneServices.CompareImagesORB(img1, img2);


                        //// Lade zwei Bilder
                        //Mat image1 = CvInvoke.Imread(SelectedBildchen?.BName, ImreadModes.AnyColor);
                        //Mat image2 = CvInvoke.Imread(item.BName, ImreadModes.AnyColor);

                        //if (image1.IsEmpty || image2.IsEmpty)
                        //{
                        //    Console.WriteLine("Fehler: Eines der Bilder konnte nicht geladen werden.");
                        //    continue;
                        //}

                        //// Falls Größen unterschiedlich → skalieren
                        //if (image1.Size != image2.Size)
                        //    CvInvoke.Resize(image2, image2, image1.Size);

                        // double similarity = CalculateSSIM(image1, image2);




                        double similarity = await MieneServices.CompareBilderGleichheitORB(image1, item.BName);
                        Console.WriteLine($"Ähnlichkeit: {similarity * 100:F2}%");

                        //Console.WriteLine($"Ähnlichkeitswert: {similarity:F2}%");

                        if ((similarity * 100) >= BildAbgleichProzent)
                        {
                            Console.WriteLine("Bilder sind wahrscheinlich ähnlich.");
                        }
                        else
                        {
                            Console.WriteLine("Bilder unterscheiden sich deutlich.");

                            // Bildchen aus der Collection entfernen
                            OcAufgabens.Remove(item);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Fehler: {ex.Message}");
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
            return OcAufgabens.Count > 1 && (!PrüfungLäuft) /*&& (!MultiByteParallelGleichheit)*/;

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
                //double zweihunderterZähler = ((double)gszähler * (double)gszähler + (double)gszähler) / 100D;
                //double wert = zweihunderterZähler;

                int total = (int)((gszähler * gszähler) + gszähler);
                object progressLock = new object();
                int lastPercent = 0;
                // var cbzweihunderterZähler = new ConcurrentBag<int>() { zweihunderterZähler };

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
                                        // Bildchen aus der Collection entfernen
                                        //OcAufgabens.Remove(item2);
                                        results.Add(item1.BName);
                                        //Debug.WriteLine("gleich  " + item2.BName);
                                        //Debug.WriteLine("pgs  " + pgs.StückPerSecond);
                                        //Version = pgs.StückPerSecond.ToString("F0") + " Stk/Sek";

                                        // Aus Performace Gründen
                                        //// Position anpassen, damit die Bilder neben einander liegen, da sie gleich sind
                                        //var index1 = OcAufgabens.IndexOf(item1);
                                        //var index2 = OcAufgabens.IndexOf(filep);
                                        //if (index1 != index2 & (OcAufgabens.Count > index1 + 1))
                                        //{
                                        //    // Mach dies mit await
                                        //    await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                        //    {
                                        //        OcAufgabens.Move(index2, index1 + 1);
                                        //    }));

                                        //}
                                        //else
                                        //{
                                        //    //Debug.WriteLine("nicht gleich  " + item2.BName);
                                        //    //Debug.WriteLine("pgs  " + pgs.StückPerSecond);
                                        //}
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
                                    ProzentAbgleich = pgs.Percent.ToString("F2");
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





                ////  /* var result= *//*await Task.WhenAll(*/
                //await Parallel.ForEachAsync(bilder, new ParallelOptions { MaxDegreeOfParallelism = pcCount }, async (filep, _) =>
                //{

                //});

                //foreach (var item in results)
                //{
                //    if (File.Exists(item) & (item != SelectedBildchen?.BName))
                //    {
                //        // Bildchen aus der Collection entfernen
                //        var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == item);
                //        if (bildchen != null)
                //        {
                //            OcAufgabens.Remove(bildchen);
                //        }
                //    }
                //}
                //
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
                                        // Bildchen aus der Collection entfernen
                                        //OcAufgabens.Remove(item2);
                                        results.Add(item1);
                                        //Debug.WriteLine("gleich  " + item2.BName);
                                        //Debug.WriteLine("pgs  " + pgs.StückPerSecond);
                                        //Version = pgs.StückPerSecond.ToString("F0") + " Stk/Sek";

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
            return SelectedBildchen != null && !PrüfungLäuft;
        }

        [RelayCommand(CanExecute = nameof(CanExecuteBildInsKIFehlerVerschiebenCommand))]
        private async Task CommandExecuteBildInsKIFehlerVerschieben()
        {
            //if (SelectedBildchen == null)
            //{
            //    return;
            //}
            //else
            //{
            //    var sw = Stopwatch.StartNew();
            //    try
            //    {
            //        string quellDateiFullName = SelectedBildchen.BName;
            //        string zielVerzeichnis = Path.Combine(Path.GetDirectoryName(quellDateiFullName), "KI_Fehler");
            //        string zielDateiFullName = Path.Combine(zielVerzeichnis, Path.GetFileName(quellDateiFullName));
            //        if (File.Exists(quellDateiFullName))
            //        {
            //            if (!Directory.Exists(zielVerzeichnis))
            //            {
            //                Directory.CreateDirectory(zielVerzeichnis);
            //            }
            //            if (File.Exists(zielDateiFullName))
            //            {
            //                MessageBox.Show("Die Datei existiert bereits im Zielverzeichnis: " + zielDateiFullName);
            //            }
            //            else
            //            {
            //                File.Move(quellDateiFullName, zielDateiFullName);

            //                // 820
            //                var bildchen = OcAufgabens.FirstOrDefault(b => b.BName == SelectedBildchen.BName);
            //                var indexSelected = AufgabenView.CurrentPosition;

            //                if (bildchen != null)
            //                {
            //                    var index = OcAufgabens.IndexOf(bildchen);

            //                    bildchen.BName = zielDateiFullName;
            //                    bildchen.BildFürLinks = true;
            //                    OnPropertyChanged(nameof(SelectedBildchen));
            //                    OnPropertyChanged(nameof(CountBildchenFürLinks));

            //                }
            //                Debug.WriteLine($"Datei verschoben von {quellDateiFullName} nach {zielDateiFullName}");
            //            }
            //        }
            //    }
            //    finally
            //    {
            //        sw.Stop();
            //        Debug.WriteLine($"Dauer: {sw.ElapsedMilliseconds} ms");
            //        AufgabenView.Refresh();
            //    }
            //}

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

            AufgabenView.Refresh();

        }

        #endregion

        #region Command Alle Bilder SHA256 Abgleich prüfen
        private bool CanExecuteAlleBilderSHA256AbgleichPrüfen()
        {
            return OcAufgabens.Count > 1 && (!PrüfungLäuft) /*&& (!MultiByteParallelGleichheit)*/;
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
                //PercentageValueVerschieben = value.Percent);
                // Wird auf dem UI-Thread ausgeführt – sichere, zentrale Aktualisierung
                PercentageValueVerschieben = value.Percent;
                LabelDropContent = "Rest " + value.Restzeit + "  ( " + value.StückPerSecond.ToString("F0") + " Stk/Sek )";
                ProzentAbgleich = value.Percent.ToString("F2");

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
                //double zweihunderterZähler = ((double)gszähler * (double)gszähler + (double)gszähler) / 100D;
                //double wert = zweihunderterZähler;

                int total = (int)bilder.Count;
                object progressLock = new object();
                int lastPercent = 0;
                // var cbzweihunderterZähler = new ConcurrentBag<int>() { zweihunderterZähler };



                //await Parallel.ForEachAsync(bilder, new ParallelOptions { MaxDegreeOfParallelism = pcCount }, async (filep, _) =>
                //{

                //        if ( File.Exists(filep.BName))
                //        {

                //        }

                //});

                //foreach (var item in bilder)
                //{
                //    string hash2 = await MieneServices.GetFileHashSHA256Async(item.BName, token);

                //    var cl = new CLSHA256Bild();
                //    cl.Name= item.BName;
                //    cl.Hash = hash2;
                //    cl.PositionAnzeige = AufgabenView.IndexOf(item);
                //    results.Add(cl);    
                //}

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
                        //await Application.Current.Dispatcher.InvokeAsync(() =>
                        //{
                        //    LabelDropContent = "Rest " + pgs.Restzeit + "  ( " + pgs.StückPerSecond.ToString("F0") + " Stk/Sek )";
                        //});
                        //LabelDropContent = pgs.Restzeit;
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
                            //await Application.Current.Dispatcher.InvokeAsync(() =>
                            //{
                            //    LabelDropContent = "Rest " + pgs.Restzeit + "  ( " + pgs.StückPerSecond.ToString("F0") + " Stk/Sek )";
                            //});
                            //LabelDropContent = pgs.Restzeit;
                        }
                        // });
                    }
                }, token);



                //// Paralleler Vergleich der Hashes
                //var results2 = new ConcurrentBag<CLSHA256Bild>();
                //foreach (var item in results)
                //{
                //    foreach (var cl in results)
                //    {
                //        if (item.Name != cl.Name)
                //        {
                //            if (item.Hash == cl.Hash)
                //            {
                //                if (!results2.Any(r => r.Name == item.Name))
                //                {
                //                    results2.Add(cl);
                //                }

                //                if (!results2.Any(r => r.Name == item.Name))
                //                {
                //                    results2.Add(item);
                //                }
                //            }
                //        }
                //    }
                //}

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

        #region Command Oben Minimieren
        private static bool CanExecuteObenMinimieren() { return true; }

        [RelayCommand(CanExecute = nameof(CanExecuteObenMinimieren))]
        private void CommandExecuteObenMinimieren()
        {


            // in abhängigkeit vom zustand IsObenMinimiert
            // immer ins gegenteile wechseln
            if (IsObenMinimiert)
            {
                IsObenMinimiert = false;
            }
            else
            {
                IsObenMinimiert = true;
            }
        }
        #endregion

        #region Bildersuche (Index-Leiste & Filter-Popover)

        /// <summary>Analysiert das gewählte Bild per CLIP (erkennt Begriffe).</summary>
        private readonly BildAnalyseService _bildAnalyse = new();

        /// <summary>True, wenn die schlanke Such-/Index-Leiste eingeblendet ist.</summary>
        [ObservableProperty]
        private bool _isSuchleisteOffen;

        /// <summary>Kurzstatus der Bildanalyse (z. B. „Analysiere…", „6 Begriffe erkannt").</summary>
        [ObservableProperty]
        private string _analyseStatus = string.Empty;

        /// <summary>True während die Analyse läuft (für einen Ladehinweis).</summary>
        [ObservableProperty]
        private bool _analyseLaeuft;

        /// <summary>Vorschaubild das gerade analysiert wurde (für die Anzeige im Popup).</summary>
        [ObservableProperty]
        private ImageSource? _analyseBildVorschau;

        /// <summary>Heatmap-Overlay (halbtransparent) über der Vorschau — zeigt wo der Begriff erkannt wurde.</summary>
        [ObservableProperty]
        private ImageSource? _heatmapOverlay;

        /// <summary>True während die Heatmap berechnet wird.</summary>
        [ObservableProperty]
        private bool _heatmapLaeuft;

        [ObservableProperty]
        private bool _filterLaeuft;

        /// <summary>Die erkannten Begriffe des aktuellen Bildes (z. B. „Blume 34 %").</summary>
        public ObservableCollection<string> ErkannteBegriffe { get; } = new();

        /// <summary>Treffer der Freitextsuche (klickbare Miniaturen), nach Schwelle gefiltert.</summary>
        public ObservableCollection<SuchErgebnis> SuchErgebnisse { get; } = new();

        /// <summary>Alle Top-Treffer der letzten Suche (ungefiltert, mit Score) für das Live-Filtern.</summary>
        private readonly System.Collections.Generic.List<(SuchErgebnis Erg, float Score)> _alleSuchTreffer = new();

        /// <summary>Letzte Suchanfrage (für die Statuszeile beim Neu-Filtern).</summary>
        private string _letzteFrage = string.Empty;

        /// <summary>Kurzstatus der Freitextsuche.</summary>
        [ObservableProperty]
        private string _sucheStatus = string.Empty;

        /// <summary>Der aktuell hervorgehobene Begriff (für visuelles Feedback im Chip).</summary>
        [ObservableProperty]
        private string? _aktuellerHeatmapBegriff;

        /// <summary>True = Begriffe auf Deutsch anzeigen, False = englische Originale.</summary>
        [ObservableProperty]
        private bool _begriffeAufDeutsch = true;

        /// <summary>Letzte Roh-Ergebnisse (englisch) für erneutes Rendern bei Sprachwechsel.</summary>
        private System.Collections.Generic.IReadOnlyList<(string Word, float Score)> _letzteBegriffe =
            System.Array.Empty<(string, float)>();

        partial void OnBegriffeAufDeutschChanged(bool value) => RenderBegriffe();


        /// <summary>
        /// Schwelle für die Auto-Tags (0..1).
        /// </summary>
        [ObservableProperty]
        public partial double TagSchwelle { get; set; } = 0.23;

        partial void OnTagSchwelleChanged(double value) => RenderBegriffe();

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

        [RelayCommand]
        private async Task CommandExecuteBegriffSuche(string? chipText)
        {
            if (string.IsNullOrEmpty(chipText)) return;

            string anzeigeName = chipText.Contains("  ")
                ? chipText.Substring(0, chipText.LastIndexOf("  "))
                : chipText;
            string englisch = BegriffeAufDeutsch
                ? _letzteBegriffe.FirstOrDefault(b => BegriffUebersetzer.ZuDeutsch(b.Word) == anzeigeName).Word ?? anzeigeName
                : anzeigeName;

            string? ordner = Path.GetDirectoryName(SelectedBildchen?.BName);
            if (string.IsNullOrEmpty(ordner)) return;

            SuchErgebnisse.Clear();
            _alleSuchTreffer.Clear();
            SucheStatus = $"Suche alle Bilder mit '{anzeigeName}'…";

            var pfade = await _bildAnalyse.SucheNachKonzeptAsync(ordner, englisch);
            if (pfade.Count == 0)
            {
                SucheStatus = $"Kein Bild mit '{anzeigeName}' im Index gefunden.";
                return;
            }

            _letzteFrage = anzeigeName;
            var ergebnisse = await Task.Run(() =>
                pfade.Select(p => new SuchErgebnis
                {
                    Path = p,
                    DateiName = Path.GetFileName(p),
                    ProzentText = "✓",
                    Thumb = LadeThumb(p)
                }).ToList());

            foreach (var erg in ergebnisse)
            {
                _alleSuchTreffer.Add((erg, 1f));
                SuchErgebnisse.Add(erg);
            }

            SucheStatus = $"{pfade.Count} Bilder mit '{anzeigeName}'.";
            CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
        }

        private static ImageSource ErzeugeHeatmapBild(float[,] scores, double bildBreite = 1, double bildHoehe = 1)
        {
            int rows = scores.GetLength(0);
            int cols = scores.GetLength(1);

            // Zellgröße so wählen, dass das Seitenverhältnis des Originals erhalten bleibt.
            double aspect = bildBreite / bildHoehe;
            int cellW, cellH;
            if (aspect >= 1) { cellW = 64; cellH = (int)(64 / aspect); }
            else             { cellH = 64; cellW = (int)(64 * aspect); }
            if (cellW < 4) cellW = 4;
            if (cellH < 4) cellH = 4;
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

        /// <summary>True, wenn das Filter-Popover aufgeklappt ist.</summary>
        [ObservableProperty]
        private bool _isIndexPopoverOffen;

        // Schließt die Leiste (auch per Klick daneben) → Einstellungen mit einklappen.
        partial void OnIsSuchleisteOffenChanged(bool value)
        {
            if (!value)
                IsIndexPopoverOffen = false;
        }

        /// <summary>Freitext für die Bildersuche (z. B. „mädchen am strand").</summary>
        [ObservableProperty]
        private string _sucheText = string.Empty;

        /// <summary>Grauer Ghost-Rest der Autovervollständigung (nach dem Getippten).</summary>
        [ObservableProperty]
        private string _sucheVorschlagRest = string.Empty;

        /// <summary>True während das CLIP-Modell (einmalig) geladen wird.</summary>
        [ObservableProperty]
        private bool _clipLaedt;

        public ObservableCollection<string> SucheVorschlaege { get; } = new();

        [ObservableProperty]
        private bool _vorschlaegeOffen;

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
            if (string.IsNullOrEmpty(wort)) return;
            int sp = SucheText.LastIndexOf(' ');
            string prefix = sp >= 0 ? SucheText[..(sp + 1)] : "";
            SucheText = prefix + wort + " ";
            SucheVorschlaege.Clear();
            VorschlaegeOffen = false;
            SucheVorschlagRest = string.Empty;
        }

        /// <summary>Stellt sicher, dass CLIP geladen ist; zeigt dabei das Lade-Symbol.</summary>
        private async Task StelleClipBereitAsync()
        {
            if (_bildAnalyse.Bereit)
            {
                return;
            }

            ClipLaedt = true;
            try
            { await _bildAnalyse.StelleSicherGeladenAsync(); }
            finally { ClipLaedt = false; }
        }

        /// <summary>Anzahl der indexierten Bilder, z. B. „1140 Bilder im Index".</summary>
        [ObservableProperty]
        private string _indexAnzahlText = "0 Bilder im Index";

        /// <summary>Ordner-Fortschritt, z. B. „indexiert 3/3 Ordner".</summary>
        [ObservableProperty]
        private string _indexOrdnerText = "indexiert 0/0 Ordner";



        /// <summary>Mindest-Ähnlichkeit der Suchtreffer in Prozent (0..100).</summary>
        [ObservableProperty]
        private double _mindestAehnlichkeit = 23;

        // Slider bewegt → gecachte Treffer neu filtern (ohne erneute Suche).
        partial void OnMindestAehnlichkeitChanged(double value)
        {
            if (_alleSuchTreffer.Count > 0)
                RenderSuchErgebnisse();
        }

        /// <summary>True während der Ordner indexiert wird.</summary>
        [ObservableProperty]
        private bool _indexLaeuft;

        /// <summary>Fortschritt der Indexierung in Prozent (0..100).</summary>
        [ObservableProperty]
        private double _indexFortschritt;

        /// <summary>Fortschritts-/Ergebnistext der Indexierung.</summary>
        [ObservableProperty]
        private string _indexFortschrittText = string.Empty;

        /// <summary>Filter-Kategorien (z. B. „Erkannt", „Ort" …).</summary>
        public ObservableCollection<string> FilterKategorien { get; } = new();

        [ObservableProperty]
        private string? _selectedFilterKategorie;

        /// <summary>Mögliche Werte zur gewählten Kategorie (z. B. „flower").</summary>
        public ObservableCollection<string> FilterWerte { get; } = new();

        [ObservableProperty]
        private string? _selectedFilterWert;

        private System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<string>> _tagOptionen = new();

        partial void OnSelectedFilterKategorieChanged(string? value)
        {
            FilterWerte.Clear();
            SelectedFilterWert = null;
            if (value != null && _tagOptionen.TryGetValue(value, out var werte))
            {
                foreach (var w in werte) FilterWerte.Add(w);
            }
        }

        partial void OnSelectedFilterWertChanged(string? value)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(SelectedFilterKategorie)) return;
            _ = FilterSucheAusfuehrenAsync(SelectedFilterKategorie, value);
        }

        private async Task FilterSucheAusfuehrenAsync(string kategorie, string wert)
        {
            string? pfad = SelectedBildchen?.BName;
            string? ordner = string.IsNullOrEmpty(pfad) ? null : Path.GetDirectoryName(pfad);
            if (string.IsNullOrEmpty(ordner)) return;

            SuchErgebnisse.Clear();
            _alleSuchTreffer.Clear();
            string anzeige = $"{kategorie}: {wert}";
            SucheStatus = $"Filtere '{anzeige}'…";
            FilterLaeuft = true;
            try
            {
                var treffer = await _bildAnalyse.SucheNachFilterAsync(ordner, kategorie, wert);
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

                foreach (var erg in ergebnisse)
                {
                    _alleSuchTreffer.Add((erg, 1f));
                    SuchErgebnisse.Add(erg);
                }

                SucheStatus = $"{treffer.Count} Bilder mit '{anzeige}'.";
                CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
            }
            finally { FilterLaeuft = false; }
        }

        private void AktualisiereFilterOptionen()
        {
            string? pfad = SelectedBildchen?.BName;
            string? ordner = string.IsNullOrEmpty(pfad) ? null : Path.GetDirectoryName(pfad);
            if (string.IsNullOrEmpty(ordner)) return;

            _tagOptionen = _bildAnalyse.LadeFilterOptionen(ordner);
            FilterKategorien.Clear();
            foreach (var k in _tagOptionen.Keys) FilterKategorien.Add(k);
            if (FilterKategorien.Count > 0) SelectedFilterKategorie = FilterKategorien[0];
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

            // Kleine Vorschau des analysierten Bildes laden.
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
            catch { AnalyseBildVorschau = null; }

            try
            {
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
            catch (Exception ex)
            {
                AnalyseStatus = "Fehler bei der Analyse: " + ex.Message;
            }
            finally
            {
                AnalyseLaeuft = false;
            }
        }

        [RelayCommand]
        private async Task CommandExecuteOrdnerIndexieren()
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

            try
            {
                await StelleClipBereitAsync();

                var progress = new Progress<(int done, int total, string file)>(p =>
                {
                    IndexFortschritt = p.total > 0 ? 100.0 * p.done / p.total : 0;
                    IndexFortschrittText = $"Indexiere {p.done}/{p.total}: {Path.GetFileName(p.file)}";
                });

                int anzahl = await _bildAnalyse.IndexiereOrdnerAsync(ordner, progress);

                if (!_bildAnalyse.Bereit)
                {
                    IndexFortschrittText = "CLIP-Modelle nicht gefunden (models-Ordner).";
                    return;
                }

                IndexFortschritt = 100;
                IndexAnzahlText = $"{anzahl} Bilder im Index";
                IndexOrdnerText = "indexiert 1/1 Ordner";
                IndexFortschrittText = $"Fertig: {anzahl} Bilder im Ordner '{Path.GetFileName(ordner)}' indexiert.";
                AktualisiereFilterOptionen();
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

        [RelayCommand]
        private async Task CommandExecuteFreitextSuche()
        {
            string frage = (SucheText ?? string.Empty).Trim();
            if (frage.Length == 0)
            {
                return;
            }

            string? pfad = SelectedBildchen?.BName;
            string? ordner = string.IsNullOrEmpty(pfad) ? null : Path.GetDirectoryName(pfad);
            if (string.IsNullOrEmpty(ordner))
            {
                SucheStatus = "Kein Ordner – erst ein Bild wählen und indexieren.";
                return;
            }

            SuchErgebnisse.Clear();
            _alleSuchTreffer.Clear();
            try
            {
                await StelleClipBereitAsync();
                SucheStatus = $"Suche '{frage}'…";

                // Alle Top-Treffer holen (Schwelle 0); gefiltert wird lokal per Slider.
                var treffer = await _bildAnalyse.SucheAsync(ordner, frage, topN: 60, minSim: 0f);
                if (treffer.Count == 0)
                {
                    SucheStatus = "Keine Treffer – ist der Ordner schon indexiert?";
                    return;
                }

                _letzteFrage = frage;
                var ergebnisse = await Task.Run(() =>
                    treffer.Select(t => (Erg: new SuchErgebnis
                    {
                        Path = t.Path,
                        DateiName = Path.GetFileName(t.Path),
                        ProzentText = $"{t.Score * 100f:F0} %",
                        Thumb = LadeThumb(t.Path)
                    }, t.Score)).ToList());

                foreach (var (erg, score) in ergebnisse)
                    _alleSuchTreffer.Add((erg, score));

                RenderSuchErgebnisse();
            }
            catch (Exception ex)
            {
                SucheStatus = "Fehler bei der Suche: " + ex.Message;
            }
        }

        /// <summary>Gecachte Treffer nach der Mindest-Ähnlichkeit filtern und anzeigen.</summary>
        private void RenderSuchErgebnisse()
        {
            SuchErgebnisse.Clear();
            float min = (float)(MindestAehnlichkeit / 100.0);

            int gezeigt = 0;
            foreach (var (erg, score) in _alleSuchTreffer)
            {
                if (score < min)
                {
                    continue;
                }

                SuchErgebnisse.Add(erg);
                gezeigt++;
            }

            SucheStatus = gezeigt == 0
                ? $"Keine Treffer über {MindestAehnlichkeit:F0} % für '{_letzteFrage}'."
                : $"{gezeigt} Treffer für '{_letzteFrage}' (ab {MindestAehnlichkeit:F0} %).";

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
            var nachPfad = ocAufgabens
                .Where(b => b.BName != null)
                .ToDictionary(b => b.BName, System.StringComparer.OrdinalIgnoreCase);

            var behalten = SuchErgebnisse
                .Select(e => nachPfad.TryGetValue(e.Path, out var b) ? b : null)
                .Where(b => b != null)
                .ToList();

            ocAufgabens.Clear();
            foreach (var b in behalten)
            {
                ocAufgabens.Add(b!);
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

            var item = OcAufgabens.FirstOrDefault(
                b => string.Equals(b.BName, pfad, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                SelectedBildchen = item;
                await AnalysiereAktuellesBildAsync();
            }
        }

        /// <summary>Lädt eine kleine, eingefrorene Vorschau für einen Treffer.</summary>
        private static ImageSource? LadeThumb(string pfad)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(pfad);
                bmp.DecodePixelWidth = 120;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        [RelayCommand]
        private void CommandExecuteUebersicht()
        {
            string? pfad = SelectedBildchen?.BName;
            if (string.IsNullOrEmpty(pfad)) return;
            string? ordner = Path.GetDirectoryName(pfad);
            if (string.IsNullOrEmpty(ordner)) return;

            string cache = Path.Combine(ordner, BildAnalyseService.CacheDateiName);
            if (!File.Exists(cache))
            {
                SucheStatus = "Kein Index vorhanden – erst den Ordner indexieren.";
                return;
            }

            var index = new ImageMatching.Core.ImageIndex(new ImageMatching.Cnn.CnnDescriptor());
            index.Load(cache);
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
                        stats[concept] = (1, entry.Path);
                    else
                        stats[concept] = (stats[concept].Count + 1, stats[concept].ExamplePath);
                }
            }

            var sortiert = stats
                .OrderByDescending(kv => kv.Value.Count)
                .ToList();

            _alleSuchTreffer.Clear();
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

        [RelayCommand]
        private void CommandExecuteDubletten()
        {
            // TODO: Dubletten-Gruppen anzeigen
        }

        [RelayCommand]
        private void CommandExecuteQueryBild()
        {
            // TODO: Query-Bild wählen und ähnliche suchen
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

            try
            {
                PrüfungLäuft = true;

                // OnFileDrop(string[] filepaths) neu initialisieren, um die Bilder neu einzulesen
                var dateien = new string[] { DropDateiName };

                await OnFileDrop(dateien);
            }
            finally
            {
                PrüfungLäuft = false;
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





