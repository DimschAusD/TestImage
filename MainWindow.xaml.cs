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

        public MainWindow()
        {
            InitializeComponent();
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
        }

        private void imgCurrent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var image = sender as System.Windows.Controls.Image;

            if (image != null && image.Source is BitmapSource bmp)
            {
                double scaleX = image.ActualWidth / bmp.PixelWidth;
                double scaleY = image.ActualHeight / bmp.PixelHeight;
                txtScale.Text = $"  ScaleX: {scaleX:F2}, ScaleY: {scaleY:F2}  ";

                //var _vm = this.DataContext as AufgabeViewModel;
                //_vm.UpdateScale(e.NewSize.Height);
            }
            else
            {
                txtScale.Text = " 0 ";
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var vm = DataContext as AufgabeViewModel;

            // Pfeiltasten: Bild navigieren (vor ListBox-Scroll abfangen)
            if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (vm?.CommandExecuteBildLinksCommand.CanExecute(null) == true)
                    vm.CommandExecuteBildLinksCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (vm?.CommandExecuteBildNachRechtsCommand.CanExecute(null) == true)
                    vm.CommandExecuteBildNachRechtsCommand.Execute(null);
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
    }
}

