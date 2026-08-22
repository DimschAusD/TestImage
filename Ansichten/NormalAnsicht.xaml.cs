using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
        /// <summary>
        /// Eine Kachel der Bildleiste hat eine andere Datei zugewiesen bekommen.
        ///
        /// Feuert bei der ersten Erzeugung und — anders als <c>Loaded</c> — auch beim
        /// Recycling, wo die Kachel bestehen bleibt und nur ihren DataContext wechselt.
        ///
        /// Der alte Auftrag wird abgemeldet: Beim Ziehen der Bildlaufleiste laufen sonst
        /// Dutzende Anforderungen für Bilder auf, die längst wieder aus dem Sichtfenster
        /// gescrollt sind, und blockieren die, die gerade zu sehen sind.
        /// </summary>
        private void Miniatur_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            MiniaturLader.Abmelden(e.OldValue as MeinBildchen);
            MiniaturLader.Anfordern(e.NewValue as MeinBildchen);
        }

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

            // Beim Zurückkehren aus dem Bildmodus das aktuelle Bild wieder zentrieren.
            //
            // Solange diese Ansicht eingeklappt ist, überspringt das Zentrier-Verhalten
            // jede Auswahländerung — die unsichtbare Leiste hat weder Sichtfenster noch
            // erzeugte Behälter. Im Vollbild kann man aber blättern und Bilder
            // verschieben; die Leiste steht danach irgendwo. Ohne diesen Aufruf bliebe
            // sie dort stehen. Die Vollbildansicht macht es umgekehrt genauso.
            Listbox_MiniaturBilder.IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is true)
                {
                    HorizontalListBoxBehavior.CenterNow(Listbox_MiniaturBilder);
                    MiniaturenNachfordern();
                }
            };

            // Miniaturen nachfordern: beim Scrollen und bei jeder Änderung der Liste.
            //
            // DataContextChanged an der Kachel allein reichte nicht — ohne Scrollen blieben
            // die Kacheln leer. Warum es dort ausfällt, weiss ich nicht abschliessend;
            // diese Nachforderung macht die Frage gegenstandslos, weil sie billig und
            // beliebig oft wiederholbar ist.
            Listbox_MiniaturBilder.AddHandler(
                ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((_, _) => MiniaturenNachfordern()));

            if (Listbox_MiniaturBilder.Items is INotifyCollectionChanged beobachtbar)
            {
                beobachtbar.CollectionChanged += (_, _) => MiniaturenNachfordern();
            }
        }

        private bool _nachforderungSteht;

        /// <summary>
        /// Stösst das Nachfordern der sichtbaren Miniaturen an — gesammelt, nicht je
        /// Ereignis.
        ///
        /// Beim Einlesen eines Ordners kommen tausende Änderungsmeldungen; ohne das Sammeln
        /// liefe die Schleife tausendfach. Und <c>Background</c> statt sofort, weil die
        /// Behälter erst nach dem Layout existieren — vorher fände die Schleife nichts.
        /// </summary>
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
                    MiniaturLader.FordereSichtbareAn(Listbox_MiniaturBilder);
                }),
                DispatcherPriority.Background);
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

            if (e.PropertyName == nameof(AufgabeViewModel.IsSuchleisteOffen))
                ZeigeSuchfenster((sender as AufgabeViewModel)?.IsSuchleisteOffen == true);
        }

        #region Werkzeugfenster Bildersuche

        /// <summary>
        /// Ein einziges Fenster für die ganze Sitzung. Beim Schliessen wird es nur
        /// versteckt, damit Grösse und Position erhalten bleiben.
        /// </summary>
        private IndexSuchFenster? _suchFenster;

        /// <summary>
        /// Öffnet oder versteckt das Werkzeugfenster mit der Bildersuche.
        ///
        /// Fensterverwaltung gehört nicht ins ViewModel — das weiss nichts von Fenstern.
        /// Es setzt nur <c>IsSuchleisteOffen</c>, und die Ansicht reagiert darauf.
        /// </summary>
        private void ZeigeSuchfenster(bool zeigen)
        {
            if (!zeigen)
            {
                _suchFenster?.Hide();
                return;
            }

            if (_suchFenster is null)
            {
                var besitzer = Window.GetWindow(this);

                _suchFenster = new IndexSuchFenster
                {
                    Owner = besitzer,

                    // Ein Fenster erbt den DataContext nicht vom Besitzer – hier
                    // ausdrücklich weiterreichen, damit das Panel dasselbe ViewModel sieht.
                    DataContext = DataContext
                };

                // Beim Beenden der Anwendung wirklich schliessen, sonst bliebe das
                // abgefangene Schliessen hängen und die Anwendung liefe weiter.
                if (besitzer is not null)
                {
                    besitzer.Closing += (_, _) =>
                    {
                        if (_suchFenster is null) return;
                        _suchFenster.DarfEndgueltigSchliessen = true;
                        _suchFenster.Close();
                    };
                }
            }

            _suchFenster.Show();

            if (_suchFenster.WindowState == WindowState.Minimized)
                _suchFenster.WindowState = WindowState.Normal;

            _suchFenster.Activate();
        }

        #endregion

        /// <summary>
        /// Senkrechtes Wackeln des Bildes – Rückmeldung, wenn das Verschieben nach unten
        /// gerade nicht geht. Gleiche Bewegung wie das waagerechte Wackeln der
        /// Vollbildansicht; der Transform dafür sitzt bereits auf <c>imgCurrent</c>.
        /// </summary>
        public void ShakeImageSenkrecht(bool nachUnten)
        {
            double d = nachUnten ? 1 : -1;
            var anim = new DoubleAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * 14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(55))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * -10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * 7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(185))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(d * -4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(245))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
            imgShakeTransform.BeginAnimation(TranslateTransform.YProperty, anim);
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
                // Eine Zahl statt zweier: Bei Stretch.Uniform sind beide Achsen gleich
                // skaliert, bei Stretch.None stehen beide auf 1. „ScaleX: 0,42, ScaleY:
                // 0,42" nannte denselben Wert zweimal und las sich wie Debug-Ausgabe.
                double massstab = image.ActualWidth / bmp.PixelWidth;
                TXT_Bildmassstab.Text = $"{massstab * 100:F0} %";
            }
            else
            {
                TXT_Bildmassstab.Text = "—";
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
