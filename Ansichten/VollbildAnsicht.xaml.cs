using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TestImage.Ansichten
{
    /// <summary>
    /// Vollbildansicht (Bildmodus, IsImageMaximiert = true). Erbt den DataContext
    /// vom Host (MainWindow) und teilt sich damit das AufgabeViewModel.
    /// </summary>
    public partial class VollbildAnsicht : UserControl
    {
        /// <summary>
        /// Eine Kachel des Filmstrips hat eine andere Datei bekommen — Miniatur anfordern,
        /// alten Auftrag zurücknehmen. Wortgleich zur Normalansicht; die Begründung steht
        /// in <see cref="MiniaturLader"/>.
        /// </summary>
        private void Miniatur_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            MiniaturLader.Abmelden(e.OldValue as MeinBildchen);
            MiniaturLader.Anfordern(e.NewValue as MeinBildchen);
        }

        public VollbildAnsicht()
        {
            InitializeComponent();

            // Beim Einblenden des Filmstrips das aktuelle Bild zentrieren.
            Listbox_SchwebeMiniaturen.IsVisibleChanged += (s, e) =>
            {
                if ((bool)e.NewValue)
                {
                    HorizontalListBoxBehavior.CenterNow(Listbox_SchwebeMiniaturen);
                    MiniaturenNachfordern();
                }
            };

            // Miniaturen nachfordern, wie in der Normalansicht. Begründung dort und in
            // MiniaturLader.FordereSichtbareAn.
            Listbox_SchwebeMiniaturen.AddHandler(
                ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((_, _) => MiniaturenNachfordern()));

            if (Listbox_SchwebeMiniaturen.Items is INotifyCollectionChanged beobachtbar)
            {
                beobachtbar.CollectionChanged += (_, _) => MiniaturenNachfordern();
            }
        }

        private bool _nachforderungSteht;

        /// <summary>Gesammeltes Nachfordern der sichtbaren Miniaturen, bei Background-Rang.</summary>
        private void MiniaturenNachfordern()
        {
            if (_nachforderungSteht)
            {
                return;
            }

            _nachforderungSteht = true;

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    _nachforderungSteht = false;
                    MiniaturLader.FordereSichtbareAn(Listbox_SchwebeMiniaturen);
                }),
                DispatcherPriority.Background);
        }

        private void Vollbild_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                BRD_DropOverlay.Visibility = Visibility.Visible;
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void Vollbild_DragLeave(object sender, DragEventArgs e)
        {
            BRD_DropOverlay.Visibility = Visibility.Collapsed;
        }

        private void Vollbild_Drop(object sender, DragEventArgs e)
        {
            BRD_DropOverlay.Visibility = Visibility.Collapsed;
        }

        #region Zoom per Mausrad

        /// <summary>Kleinste Stufe: das eingepasste Bild.</summary>
        private const double ZoomMin = 1.0;

        private const double ZoomMax = 8.0;

        /// <summary>Faktor je Rasterschritt des Mausrads.</summary>
        private const double ZoomSchritt = 1.2;

        /// <summary>
        /// Mausrad über dem Bild ändert die Vergrösserung. Bewusst als bubbelndes
        /// MouseWheel am Wurzel-Grid: Rollt man über dem Filmstrip, hat dessen
        /// eigenes Rad-Verhalten Vorrang und markiert das Ereignis vorher als erledigt.
        /// </summary>
        private void Vollbild_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta == 0 || imgVollbild.Source is null)
                return;

            // Zum Mauszeiger hin vergrössern. Ohne das liesse sich nur die Bildmitte
            // betrachten, denn Verschieben ist nicht vorgesehen.
            if (imgVollbild.ActualWidth > 0 && imgVollbild.ActualHeight > 0)
            {
                var p = e.GetPosition(imgVollbild);
                imgVollbild.RenderTransformOrigin = new Point(
                    Math.Clamp(p.X / imgVollbild.ActualWidth, 0, 1),
                    Math.Clamp(p.Y / imgVollbild.ActualHeight, 0, 1));
            }

            double faktor = e.Delta > 0 ? ZoomSchritt : 1.0 / ZoomSchritt;
            double neu = Math.Clamp(imgZoomTransform.ScaleX * faktor, ZoomMin, ZoomMax);

            imgZoomTransform.ScaleX = neu;
            imgZoomTransform.ScaleY = neu;

            ZeigeZoomStufe(neu);
            e.Handled = true;
        }

        /// <summary>
        /// Klick auf das Bild stellt die eingepasste Ansicht wieder her. Die Klickzonen
        /// links und rechts fangen ihre Klicks selbst ab, dort wird also navigiert.
        /// </summary>
        private void Vollbild_ZoomZuruecksetzen(object sender, MouseButtonEventArgs e)
        {
            SetzeZoomZurueck();
        }

        /// <summary>Bildwechsel: nie im Zoom des vorherigen Bildes hängen bleiben.</summary>
        private void Vollbild_BildGewechselt(object sender, DataTransferEventArgs e)
        {
            SetzeZoomZurueck();
        }

        private void SetzeZoomZurueck()
        {
            imgZoomTransform.ScaleX = ZoomMin;
            imgZoomTransform.ScaleY = ZoomMin;
            imgVollbild.RenderTransformOrigin = new Point(0.5, 0.5);

            ZeigeZoomStufe(ZoomMin);
        }

        /// <summary>Anzeige nur bei vergrössertem Bild – bei 100 % wäre sie nur Ablenkung.</summary>
        private void ZeigeZoomStufe(double stufe)
        {
            bool vergroessert = stufe > ZoomMin + 0.001;

            TXT_ZoomWert.Text = $"{stufe * 100:F0} %";
            BRD_ZoomAnzeige.Visibility = vergroessert ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        /// <summary>
        /// Kurzes Wackeln des Vollbilds (Feedback am Anfang/Ende der Navigation).
        /// Wird vom Host (MainWindow) im Bildmodus aufgerufen.
        /// </summary>
        public void ShakeImage(bool nachRechts)
            => Wackeln(TranslateTransform.XProperty, nachRechts ? 1 : -1);

        /// <summary>
        /// Senkrechtes Wackeln – Rückmeldung, wenn das Verschieben nach unten gerade
        /// nicht geht. Gleiche Bewegung wie waagerecht, nur auf der anderen Achse.
        /// </summary>
        public void ShakeImageSenkrecht(bool nachUnten)
            => Wackeln(TranslateTransform.YProperty, nachUnten ? 1 : -1);

        private void Wackeln(DependencyProperty achse, double richtung)
        {
            double d = richtung;
            var anim = new DoubleAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * 14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(55))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * -10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * 7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(185))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * -4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(245))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
            imgShakeTransform.BeginAnimation(achse, anim);
        }
    }
}
