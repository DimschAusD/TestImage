using System;
using System.Windows;
using System.Windows.Controls;
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
        public VollbildAnsicht()
        {
            InitializeComponent();

            // Beim Einblenden des Filmstrips das aktuelle Bild zentrieren.
            Listbox_SchwebeMiniaturen.IsVisibleChanged += (s, e) =>
            {
                if ((bool)e.NewValue)
                    HorizontalListBoxBehavior.CenterNow(Listbox_SchwebeMiniaturen);
            };
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

        /// <summary>
        /// Kurzes Wackeln des Vollbilds (Feedback am Anfang/Ende der Navigation).
        /// Wird vom Host (MainWindow) im Bildmodus aufgerufen.
        /// </summary>
        public void ShakeImage(bool nachRechts)
        {
            double d = nachRechts ? 1 : -1;
            var anim = new DoubleAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * 14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(55))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * -10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * 7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(185))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * -4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(245))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
            imgShakeTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }
}
