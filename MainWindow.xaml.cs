using Emgu.CV;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static System.Net.Mime.MediaTypeNames;

namespace TestImage
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private void SetTitleBarDark(bool dark)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int value = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }

        /// <summary>
        /// Platziert das Schnell-Listen-Popup oberhalb der Miniaturleiste und
        /// bündig mit deren rechter Kante (rechte Popup-Kante = rechte Leisten-Kante).
        /// </summary>
        private System.Windows.Controls.Primitives.CustomPopupPlacement[] BildListePopup_Platzierung(
            Size popupSize, Size zielSize, Point offset)
        {
            double x = zielSize.Width - popupSize.Width; // rechtsbündig
            double y = -popupSize.Height;                // oberhalb der Leiste
            return new[]
            {
                new System.Windows.Controls.Primitives.CustomPopupPlacement(
                    new Point(x, y),
                    System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)
            };
        }

        public MainWindow()
        {
            InitializeComponent();

            // Schnell-Listen-Popup rechtsbündig über der Miniaturleiste platzieren.
            POP_BildListe.CustomPopupPlacementCallback = BildListePopup_Platzierung;

            Loaded += (_, _) =>
            {
                if (DataContext is AufgabeViewModel vm)
                    vm.PropertyChanged += OnVmPropertyChanged;

                Listbox_SchwebeMiniaturen.IsVisibleChanged += (s, e) =>
                {
                    if ((bool)e.NewValue)
                        HorizontalListBoxBehavior.CenterNow(Listbox_SchwebeMiniaturen);
                };
            };

            // Converter als Resource hinzufügen
            // Resources.Add("BoolToBrushConverter", new BoolToBrushConverter());

            //// Beispielbilder laden (lokale Pfade oder URLs)
            //Images = new ObservableCollection<ImageItem>
            //{
            //    new ImageItem(@"C:\Users\Bill-6e\Desktop\ZL4\Test 1\he17_同人CG集2025-09-10\Bambi by DavidMnr on DeviantArt[1].jpg"),
            //    new ImageItem(@"C:\Users\Bill-6e\Desktop\ZL4\Test 1\he17_同人CG集2025-09-10\Crunch by DavidMnr on DeviantArt[1].jpg"),
            //    new ImageItem(@"C:\Users\Bill-6e\Desktop\ZL4\Test 1\he17_同人CG集2025-09-10\daim by DavidMnr on DeviantArt[1].jpg"),
            //    new ImageItem(@"C:\Users\Bill-6e\Desktop\ZL4\Test 1\he17_同人CG集2025-09-10\Happy by DavidMnr on DeviantArt[1].jpg"),
            //    //new ImageItem("https://via.placeholder.com/104"),
            //    //new ImageItem("https://via.placeholder.com/105"),
            //    //new ImageItem("https://via.placeholder.com/106"),
            //};


            //var ai=System.IO.Directory.GetFiles(@"C:\Users\Bill-6e\Desktop\ZL4\Test 1\he17_同人CG集2025-09-10\", "*.jpg");

            //Images = new ObservableCollection<ImageItem>(ai.Select(a => new ImageItem(a)));

            //imageList.ItemsSource = Images;
        }

        //private void ImageList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        //{
        //    if (e.OriginalSource is FrameworkElement fe && fe.DataContext is ImageItem item)
        //    {
        //        // Auswahl setzen
        //        foreach (var img in Images) img.IsSelected = false;
        //        item.IsSelected = true;
        //        selectedIndex = Images.IndexOf(item);

        //        // Zentrieren
        //       // CenterSelectedImage();
        //    }
        //}

        //private void CenterSelectedImage()
        //{
        //    if (selectedIndex < 0) return;

        //    var container = (FrameworkElement)imageList.ItemContainerGenerator.ContainerFromIndex(selectedIndex);
        //    if (container != null)
        //    {
        //        double itemCenter = container.TransformToAncestor(scrollViewer).Transform(new Point(container.ActualWidth / 2, 0)).X;
        //        double scrollTo = scrollViewer.HorizontalOffset + itemCenter - scrollViewer.ViewportWidth / 2;
        //        scrollViewer.ScrollToHorizontalOffset(scrollTo);
        //    }
        //}

        //public class ImageItem
        //{
        //    public string Path { get; set; }
        //    public bool IsSelected { get; set; }
        //    public ImageItem(string path) { Path = path; }
        //}

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AufgabeViewModel.IsImageMaximiert) && sender is AufgabeViewModel vm)
            {
                SetTitleBarDark(vm.IsImageMaximiert);
            }

            if (e.PropertyName == nameof(AufgabeViewModel.DisplayImage))
            {
                Überblenden(imgCurrent.Source);
            }
        }

        private bool _crossfadeLäuft;

        private void Überblenden(ImageSource? vorherigesBild)
        {
            imgAltesBild.Source = vorherigesBild;
            imgCurrent.BeginAnimation(OpacityProperty, null);
            imgCurrent.Opacity = 0;

            var einblenden = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            einblenden.Completed += (s, _) => imgAltesBild.Source = null;
            imgCurrent.BeginAnimation(OpacityProperty, einblenden);
        }

        private void imgCurrent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var image = sender as System.Windows.Controls.Image;

            if (image != null && image.Source is BitmapSource bmp)
            {
                double scaleX = image.ActualWidth / bmp.PixelWidth;
                double scaleY = image.ActualHeight / bmp.PixelHeight;
                txtScale.Text = $"  ScaleX: {scaleX:F2}, ScaleY: {scaleY:F2}  ";
            }
            else
            {
                txtScale.Text = " 0 ";
            }

            scdd.ScrollToHorizontalOffset(0);
            scdd.ScrollToVerticalOffset(0);
            _scrollZielH = 0;
            _scrollZielV = 0;
        }

        private void ShakeImage(bool nachRechts)
        {
            double d = nachRechts ? 1 : -1;
            var anim = new DoubleAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * 14,  KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(55))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * -10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * 7,   KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(185))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * -4,  KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(245))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0,        KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
            imgShakeTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var vm = DataContext as AufgabeViewModel;

            // Pfeiltasten: Bild navigieren (vor ListBox-Scroll abfangen)
            if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (vm?.CommandExecuteBildLinksCommand.CanExecute(null) == true)
                    vm.CommandExecuteBildLinksCommand.Execute(null);
                else if (vm?.IsImageMaximiert == true)
                    ShakeImage(nachRechts: false);
                e.Handled = true;
            }
            else if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (vm?.CommandExecuteBildNachRechtsCommand.CanExecute(null) == true)
                    vm.CommandExecuteBildNachRechtsCommand.Execute(null);
                else if (vm?.IsImageMaximiert == true)
                    ShakeImage(nachRechts: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (vm?.CommandExecuteBildInsKeinFavVerzeichnisVerschiebenCommand.CanExecute(null) == true)
                    vm.CommandExecuteBildInsKeinFavVerzeichnisVerschiebenCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && Keyboard.Modifiers == ModifierKeys.None)
            {
                // Aktuelles Bild zurück → sonst letzte Verschiebung rückgängig
                if (vm?.CommandExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand.CanExecute(null) == true)
                    vm.CommandExecuteBildInsHauptVerzeichnisZuruckVerschiebenCommand.Execute(null);
                else if (vm?.CommandExecuteVerschiebenZurückCommand.CanExecute(null) == true)
                    vm.CommandExecuteVerschiebenZurückCommand.Execute(null);
                e.Handled = true;
            }

            // Ctrl+A → Bild nach links (statt SelectAll)
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (vm?.CommandExecuteBildLinksCommand.CanExecute(null) == true)
                    vm.CommandExecuteBildLinksCommand.Execute(null);
                e.Handled = true;
            }

            // Ctrl+Z → Verschieben rückgängig (statt Undo)
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (vm?.CommandExecuteVerschiebenZurückCommand.CanExecute(null) == true)
                    vm.CommandExecuteVerschiebenZurückCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void scdd_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                BRD_DropOverlay.Visibility = Visibility.Visible;
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void scdd_DragLeave(object sender, DragEventArgs e)
        {
            BRD_DropOverlay.Visibility = Visibility.Collapsed;
        }

        private void scdd_Drop(object sender, DragEventArgs e)
        {
            BRD_DropOverlay.Visibility = Visibility.Collapsed;
        }

        private double _scrollZielH;
        private double _scrollZielV;
        private bool _scrollLäuft;

        private void Scdd_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = (ScrollViewer)sender;
            if (sv.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled)
                return;

            double viewW = sv.ViewportWidth;
            double viewH = sv.ViewportHeight;
            if (viewW <= 0 || viewH <= 0) return;

            Point maus = e.GetPosition(sv);
            double dx = maus.X - viewW / 2.0;
            double dy = maus.Y - viewH / 2.0;

            double absDx = Math.Abs(dx);
            double absDy = Math.Abs(dy);
            double summe = absDx + absDy;
            if (summe < 1) summe = 1;

            double anteilH = absDx / summe;
            double anteilV = absDy / summe;

            double stärke = -e.Delta * 0.8;

            if (!_scrollLäuft)
            {
                _scrollZielH = sv.HorizontalOffset;
                _scrollZielV = sv.VerticalOffset;
            }

            _scrollZielH += stärke * anteilH * Math.Sign(dx);
            _scrollZielV += stärke * anteilV * Math.Sign(dy);

            double maxH = sv.ScrollableWidth;
            double maxV = sv.ScrollableHeight;
            _scrollZielH = Math.Max(0, Math.Min(_scrollZielH, maxH));
            _scrollZielV = Math.Max(0, Math.Min(_scrollZielV, maxV));

            if (!_scrollLäuft)
            {
                _scrollLäuft = true;
                CompositionTarget.Rendering += SmoothScroll_Tick;
            }

            e.Handled = true;
        }

        private void SmoothScroll_Tick(object? sender, EventArgs e)
        {
            const double lerp = 0.18;
            const double schwelle = 0.5;

            double aktH = scdd.HorizontalOffset;
            double aktV = scdd.VerticalOffset;

            double neuH = aktH + (_scrollZielH - aktH) * lerp;
            double neuV = aktV + (_scrollZielV - aktV) * lerp;

            scdd.ScrollToHorizontalOffset(neuH);
            scdd.ScrollToVerticalOffset(neuV);

            if (Math.Abs(_scrollZielH - neuH) < schwelle &&
                Math.Abs(_scrollZielV - neuV) < schwelle)
            {
                scdd.ScrollToHorizontalOffset(_scrollZielH);
                scdd.ScrollToVerticalOffset(_scrollZielV);
                CompositionTarget.Rendering -= SmoothScroll_Tick;
                _scrollLäuft = false;
            }
        }
    }
}

