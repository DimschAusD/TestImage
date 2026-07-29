using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace TestImage.Ansichten
{
    /// <summary>
    /// Normalansicht (Bearbeitungsmodus, IsImageMaximiert = false). Erbt den DataContext
    /// vom Host (MainWindow) und teilt sich das AufgabeViewModel. Enthält die aus MainWindow
    /// verschobenen Handler für Bildanzeige, Drag/Drop, Mausrad-Scroll und Popup-Platzierung.
    /// </summary>
    public partial class NormalAnsicht : UserControl
    {
        public NormalAnsicht()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                // Schnell-Listen-Popup rechtsbündig über der Miniaturleiste platzieren.
                POP_BildListe.CustomPopupPlacementCallback = BildListePopup_Platzierung;

                // Sanftes Einblenden bei jedem Bildwechsel.
                if (DataContext is AufgabeViewModel vm)
                    vm.PropertyChanged += OnVmPropertyChanged;
            };
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

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AufgabeViewModel.DisplayImage))
                EinblendenBild();
        }

        /// <summary>Blendet das aktuelle Bild bei jedem Bildwechsel sanft ein.</summary>
        private void EinblendenBild()
        {
            imgCurrent.BeginAnimation(UIElement.OpacityProperty, null);
            imgCurrent.Opacity = 0;

            var einblenden = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            imgCurrent.BeginAnimation(UIElement.OpacityProperty, einblenden);
        }

        private void imgCurrent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var image = sender as Image;

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
