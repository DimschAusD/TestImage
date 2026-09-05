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

        #region Zoom per Mausrad, Verschieben mit der linken Maustaste

        /// <summary>Kleinste Stufe: das eingepasste Bild.</summary>
        private const double ZoomMin = 1.0;

        private const double ZoomMax = 8.0;

        /// <summary>Faktor je vollem Rasterschritt des Mausrads (Delta 120).</summary>
        private const double ZoomSchritt = 1.2;

        /// <summary>
        /// Zeitkonstante des Nachlaufs in Sekunden: Nach dieser Zeit ist rund zwei Drittel
        /// der Strecke zurückgelegt, nach dem Dreifachen praktisch alles. Kleiner wirkt
        /// härter, grösser träger.
        /// </summary>
        private const double NachlaufZeitkonstante = 0.070;

        /// <summary>
        /// Grösster Radausschlag, der in einem Ereignis gewertet wird. Ein Rasterschritt
        /// meldet 120; schnelles Rollen fasst zusammen. Ein defektes Rad meldet gelegentlich
        /// ein Vielfaches — ohne Deckel spränge die Ansicht davon einmal an den Anschlag.
        /// </summary>
        private const int MaxDeltaJeSchritt = 240;

        /// <summary>
        /// Angestrebte Vergrösserung. Massgeblich ist dieser Wert, nicht ScaleX: der
        /// angezeigte Stand läuft ihm nach, sonst würde schnelles Rollen den jeweils
        /// halbfertigen Zwischenstand als neue Grundlage nehmen.
        /// </summary>
        private double _zoomZiel = ZoomMin;

        /// <summary>Angezeigter Stand. Folgt den Zielwerten Bildschirmbild für Bildschirmbild.</summary>
        private double _zoomIst = ZoomMin, _panIstX, _panIstY;

        /// <summary>Hängt der Nachlauf gerade am Bildtakt?</summary>
        private bool _nachlaufLaeuft;

        /// <summary>Zeitstempel des zuletzt gezeichneten Bildes; TimeSpan.MinValue heisst „noch keiner".</summary>
        private TimeSpan _letzteBildzeit = TimeSpan.MinValue;

        /// <summary>
        /// Angestrebte Verschiebung des Ausschnitts in Bildschirmpunkten (0,0 = Bild
        /// mittig). Wie <see cref="_zoomZiel"/> die verbindliche Grösse; der angezeigte
        /// Stand läuft nach — beim Ziehen allerdings ohne Verzug, siehe Vollbild_Ziehen.
        /// </summary>
        private double _panZielX, _panZielY;

        /// <summary>
        /// Läuft nach der letzten Radbewegung ab und stellt die feine (teure)
        /// Skalierung wieder her.
        /// </summary>
        private DispatcherTimer? _zoomFeinTimer;

        /// <summary>
        /// Mausrad über dem Bild ändert die Vergrösserung. Bewusst als bubbelndes
        /// MouseWheel am Wurzel-Grid: Rollt man über dem Filmstrip, hat dessen
        /// eigenes Rad-Verhalten Vorrang und markiert das Ereignis vorher als erledigt.
        /// </summary>
        private void Vollbild_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta == 0 || imgVollbild.Source is null)
                return;

            e.Handled = true;

            // Stufenlos statt fester Rasterschritte: Räder mit feiner Rasterung und
            // Touchpads liefern Bruchteile von 120 und zoomen damit entsprechend fein.
            // Nach oben gedeckelt, siehe MaxDeltaJeSchritt.
            double ausschlag = Math.Clamp(e.Delta, -MaxDeltaJeSchritt, MaxDeltaJeSchritt);
            double faktor = Math.Pow(ZoomSchritt, ausschlag / 120.0);
            double neu = Math.Clamp(_zoomZiel * faktor, ZoomMin, ZoomMax);

            if (Math.Abs(neu - _zoomZiel) < 0.0001)
                return;

            // Zum Mauszeiger hin vergrössern: Der Bildpunkt unter dem Zeiger soll dort
            // bleiben, wo er ist. Skaliert wird um die Mitte, also muss die Verschiebung
            // den Rest ausgleichen — mit q als Verhältnis neuer zu bisheriger Stufe:
            // t' = m - q * (m - t), gemessen von der Mitte aus.
            double q = neu / _zoomZiel;
            var m = e.GetPosition(GRD_VollbildWurzel);
            double mx = m.X - imgVollbild.ActualWidth / 2;
            double my = m.Y - imgVollbild.ActualHeight / 2;

            _zoomZiel = neu;
            _panZielX = mx - q * (mx - _panZielX);
            _panZielY = my - q * (my - _panZielY);
            PanBegrenzen();

            SchnelleSkalierungWaehrendDerBewegung();
            NachlaufAnstossen();
            ZoomZustandAnwenden(neu);
        }

        /// <summary>
        /// Hängt den Nachlauf an den Bildtakt.
        ///
        /// Statt je Radschritt eine eigene Animation zu starten, läuft der angezeigte Stand
        /// den Zielwerten dauernd hinterher. Der Unterschied zeigt sich genau bei
        /// unregelmässigem Rad: Einzelne Animationen werden von jedem neuen Schritt
        /// abgebrochen und neu begonnen, jedes Mal mit neuer Kurve — bei zittrigen oder
        /// stossweisen Ereignissen sieht man das als Rucken. Der Nachlauf kennt keine
        /// Neustarts; ein Radschritt verschiebt nur das Ziel, die Bewegung dorthin bleibt
        /// dieselbe. Mehrere Schritte kurz hintereinander verschmelzen dadurch zu einer
        /// einzigen ruhigen Fahrt, und ein verschluckter oder doppelt gemeldeter Schritt
        /// fällt nicht mehr auf.
        /// </summary>
        private void NachlaufAnstossen()
        {
            if (_nachlaufLaeuft)
                return;

            _nachlaufLaeuft = true;
            _letzteBildzeit = TimeSpan.MinValue;
            CompositionTarget.Rendering += AufNeuesBildschirmbild;
        }

        private void NachlaufAnhalten()
        {
            if (!_nachlaufLaeuft)
                return;

            _nachlaufLaeuft = false;
            CompositionTarget.Rendering -= AufNeuesBildschirmbild;
        }

        /// <summary>
        /// Ein Schritt der Annäherung, einmal je gezeichnetem Bild.
        ///
        /// Der Anteil wird aus der vergangenen Zeit gerechnet, nicht fest gewählt: Sonst
        /// hinge die Geschwindigkeit an der Bildwiederholrate und wäre auf einem 144-Hz-Gerät
        /// mehr als doppelt so schnell wie auf einem 60-Hz-Gerät.
        /// </summary>
        private void AufNeuesBildschirmbild(object? sender, EventArgs e)
        {
            if (e is not RenderingEventArgs daten)
                return;

            // Der Haken wird gelegentlich mehrfach zum selben Takt gerufen.
            if (daten.RenderingTime == _letzteBildzeit)
                return;

            if (_letzteBildzeit == TimeSpan.MinValue)
            {
                _letzteBildzeit = daten.RenderingTime;
                return;
            }

            // Nach einer Pause – Fenster verdeckt, Anwendung im Hintergrund – käme sonst ein
            // Sprung von Sekunden, und die Annäherung wäre in einem einzigen Bild fertig.
            double dt = Math.Clamp((daten.RenderingTime - _letzteBildzeit).TotalSeconds, 0, 0.1);
            _letzteBildzeit = daten.RenderingTime;

            double anteil = 1 - Math.Exp(-dt / NachlaufZeitkonstante);

            _zoomIst += (_zoomZiel - _zoomIst) * anteil;
            _panIstX += (_panZielX - _panIstX) * anteil;
            _panIstY += (_panZielY - _panIstY) * anteil;

            // Angekommen, wenn der Rest unter einem Bildpunkt liegt. Ohne Abbruch näherte
            // sich die Rechnung endlos an und der Haken bliebe für immer am Bildtakt.
            if (Math.Abs(_zoomZiel - _zoomIst) < 0.0005
                && Math.Abs(_panZielX - _panIstX) < 0.05
                && Math.Abs(_panZielY - _panIstY) < 0.05)
            {
                _zoomIst = _zoomZiel;
                _panIstX = _panZielX;
                _panIstY = _panZielY;
                NachlaufAnhalten();
            }

            StandAnwenden();
        }

        /// <summary>Schreibt den angezeigten Stand in die Transformationen.</summary>
        private void StandAnwenden()
        {
            imgZoomTransform.ScaleX = _zoomIst;
            imgZoomTransform.ScaleY = _zoomIst;
            imgPanTransform.X = _panIstX;
            imgPanTransform.Y = _panIstY;
        }

        /// <summary>
        /// Hält die Verschiebung so, dass kein Rand über den sichtbaren Bereich hinaus
        /// wandert. Achsen, auf denen das vergrösserte Bild noch in den Rahmen passt,
        /// bleiben mittig — dort gibt es nichts zu verschieben.
        /// </summary>
        private void PanBegrenzen()
        {
            double breite = imgVollbild.ActualWidth;
            double hoehe = imgVollbild.ActualHeight;

            if (breite <= 0 || hoehe <= 0 || imgVollbild.Source is not ImageSource quelle)
            {
                _panZielX = 0;
                _panZielY = 0;
                return;
            }

            // Stretch="Uniform": Das Bild füllt nur einen Teil des Elements, der Rest ist
            // Letterbox. Begrenzt wird auf das Bild, nicht auf das Element.
            double einpassung = quelle.Width > 0 && quelle.Height > 0
                ? Math.Min(breite / quelle.Width, hoehe / quelle.Height)
                : 1.0;

            double grenzeX = Math.Max(0, (quelle.Width * einpassung * _zoomZiel - breite) / 2);
            double grenzeY = Math.Max(0, (quelle.Height * einpassung * _zoomZiel - hoehe) / 2);

            _panZielX = Math.Clamp(_panZielX, -grenzeX, grenzeX);
            _panZielY = Math.Clamp(_panZielY, -grenzeY, grenzeY);
        }

        /// <summary>
        /// Während der Bewegung linear statt hochwertig skalieren. Fant-Skalierung
        /// (HighQuality) kostet bei grossen Bildern pro Einzelbild so viel, dass die
        /// Animation stockt; im Stillstand ist sie wieder gefragt.
        /// </summary>
        private void SchnelleSkalierungWaehrendDerBewegung()
        {
            RenderOptions.SetBitmapScalingMode(imgVollbild, BitmapScalingMode.Linear);

            if (_zoomFeinTimer is null)
            {
                _zoomFeinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                _zoomFeinTimer.Tick += (_, _) =>
                {
                    _zoomFeinTimer!.Stop();
                    RenderOptions.SetBitmapScalingMode(imgVollbild, BitmapScalingMode.HighQuality);
                };
            }

            _zoomFeinTimer.Stop();
            _zoomFeinTimer.Start();
        }

        /// <summary>Läuft ein Ziehen mit gedrückter linker Maustaste?</summary>
        private bool _ziehtGerade;

        /// <summary>Mausposition und Verschiebung beim Aufsetzen – der Rest ist Differenz.</summary>
        private Point _ziehStart;

        private double _panBeimZiehStartX, _panBeimZiehStartY;

        /// <summary>
        /// Linke Maustaste im vergrösserten Bild beginnt das Verschieben, Doppelklick
        /// stellt das eingepasste Bild wieder her. Bei 100 % passiert hier nichts: Dann
        /// bleibt der Klick den Navigationszonen links und rechts.
        /// </summary>
        private void Vollbild_ZiehenStart(object sender, MouseButtonEventArgs e)
        {
            if (_zoomZiel <= ZoomMin || imgVollbild.Source is null)
                return;

            // Bedienelemente behalten ihre Klicks auch im Zoom: die Miniaturleiste ihre
            // Auswahl, der Umschalter oben rechts sein Command. Ohne diese Ausnahme
            // fienge das Verschieben den Klick vorher ab.
            if (LiegtIn(e.OriginalSource as DependencyObject, Listbox_SchwebeMiniaturen, BTN_BildmodusVollbild))
                return;

            // Der erste Klick des Doppelklicks hat bereits ein Ziehen begonnen und wieder
            // beendet; verschoben wurde dabei nichts, solange die Maus stillstand.
            if (e.ClickCount == 2)
            {
                ZiehenBeenden();
                SetzeZoomZurueck(weich: true);
                e.Handled = true;
                return;
            }

            _ziehtGerade = true;
            _ziehStart = e.GetPosition(GRD_VollbildWurzel);
            _panBeimZiehStartX = _panZielX;
            _panBeimZiehStartY = _panZielY;

            GRD_VollbildWurzel.CaptureMouse();

            // Der Verschiebe-Zeiger erscheint erst mit gedrückter Taste: Als Dauerzustand
            // im Zoom verdeckt er mehr, als er ankündigt.
            GRD_VollbildWurzel.Cursor = Cursors.SizeAll;

            e.Handled = true;
        }

        /// <summary>
        /// Bewegung bei gedrückter Taste: Das Bild folgt der Maus 1:1, ohne Animation —
        /// jede Verzögerung wäre hier ein Nachziehen unter dem Zeiger.
        /// </summary>
        private void Vollbild_Ziehen(object sender, MouseEventArgs e)
        {
            if (!_ziehtGerade)
                return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                ZiehenBeenden();
                return;
            }

            var p = e.GetPosition(GRD_VollbildWurzel);

            _panZielX = _panBeimZiehStartX + (p.X - _ziehStart.X);
            _panZielY = _panBeimZiehStartY + (p.Y - _ziehStart.Y);
            PanBegrenzen();

            // Hier ohne Nachlauf: Was man mit der Maus festhält, muss unter dem Zeiger
            // bleiben. Eine noch laufende Zoom-Annäherung übernimmt diese Werte einfach
            // als neues Ziel und läuft ihrerseits weiter.
            _panIstX = _panZielX;
            _panIstY = _panZielY;

            SchnelleSkalierungWaehrendDerBewegung();
            StandAnwenden();

            e.Handled = true;
        }

        private void Vollbild_ZiehenEnde(object sender, MouseButtonEventArgs e)
        {
            if (!_ziehtGerade)
                return;

            ZiehenBeenden();
            e.Handled = true;
        }

        /// <summary>Fenster verloren, Alt+Tab, Kontextmenü: Der Zug ist dann vorbei.</summary>
        private void Vollbild_ZiehenAbgebrochen(object sender, MouseEventArgs e)
        {
            _ziehtGerade = false;
            GRD_VollbildWurzel.Cursor = null;
        }

        private void ZiehenBeenden()
        {
            _ziehtGerade = false;
            GRD_VollbildWurzel.Cursor = null;

            if (GRD_VollbildWurzel.IsMouseCaptured)
            {
                GRD_VollbildWurzel.ReleaseMouseCapture();
            }
        }

        /// <summary>
        /// Steckt das angeklickte Element in einem der genannten Bedienelemente? Der Weg
        /// nach oben geht über den visuellen Baum, weil die Vorlagen der Kacheln und des
        /// Knopfes im logischen Baum nicht durchgängig sind.
        /// </summary>
        private static bool LiegtIn(DependencyObject? element, params DependencyObject[] bedienelemente)
        {
            while (element is not null)
            {
                foreach (var bedienelement in bedienelemente)
                {
                    if (ReferenceEquals(element, bedienelement))
                        return true;
                }

                element = element is Visual
                    ? VisualTreeHelper.GetParent(element)
                    : LogicalTreeHelper.GetParent(element);
            }

            return false;
        }

        /// <summary>Bildwechsel: nie im Zoom des vorherigen Bildes hängen bleiben.</summary>
        private void Vollbild_BildGewechselt(object sender, DataTransferEventArgs e)
        {
            SetzeZoomZurueck(weich: false);
        }

        /// <summary>
        /// Zurück auf das eingepasste Bild. Beim Doppelklick weich – die Bewegung zeigt,
        /// dass er angekommen ist, und man sieht, wohin der Ausschnitt gehört. Beim
        /// Bildwechsel hart: Der Zoom gehört zum vorherigen Bild und darf nicht mit
        /// hinüberlaufen.
        /// </summary>
        private void SetzeZoomZurueck(bool weich)
        {
            ZiehenBeenden();

            _zoomZiel = ZoomMin;
            _panZielX = 0;
            _panZielY = 0;

            if (weich)
            {
                SchnelleSkalierungWaehrendDerBewegung();
                NachlaufAnstossen();
            }
            else
            {
                NachlaufAnhalten();

                _zoomIst = ZoomMin;
                _panIstX = 0;
                _panIstY = 0;
                StandAnwenden();

                _zoomFeinTimer?.Stop();
                RenderOptions.SetBitmapScalingMode(imgVollbild, BitmapScalingMode.HighQuality);
            }

            ZoomZustandAnwenden(ZoomMin);
        }

        /// <summary>
        /// Anzeige und Bedienung an die Zoomstufe anpassen. Im vergrösserten Bild gehört
        /// die Maus dem Ausschnitt: Die Navigationszonen und die Hover-Zone der
        /// Miniaturleiste sind dann taub — sonst käme die Leiste beim Ziehen nach unten
        /// jedes Mal hoch und legte sich über das Bild. Der Zeiger bleibt der normale;
        /// der Verschiebe-Zeiger kommt erst mit gedrückter Taste (siehe Vollbild_ZiehenStart).
        /// </summary>
        private void ZoomZustandAnwenden(double stufe)
        {
            bool vergroessert = stufe > ZoomMin + 0.001;

            TXT_ZoomWert.Text = $"{stufe * 100:F0} %";
            BRD_ZoomAnzeige.Visibility = vergroessert ? Visibility.Visible : Visibility.Collapsed;

            BTN_VollbildLinks.IsHitTestVisible = !vergroessert;
            BTN_VollbildRechts.IsHitTestVisible = !vergroessert;
            BRD_HoverZoneUnten.IsHitTestVisible = !vergroessert;
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

        /// <summary>
        /// Wohin ein Bild gerade gegangen ist. Bestimmt Kante und Farbe des Scheins.
        ///
        /// Die Zuordnung steht bewusst hier und nicht beim Aufrufer: Welche Farbe ein
        /// Ziel trägt, ist eine Frage der Ansicht, nicht der Tastenbehandlung.
        /// </summary>
        public enum Bildablage
        {
            /// <summary>Pfeil nach unten — aussortiert.</summary>
            KeinFav,

            /// <summary>Umschalt + Pfeil nach unten — in den Ordner „Besonders".</summary>
            Besonders,

            /// <summary>K — in den KI-Fehler-Ordner.</summary>
            KIFehler,

            /// <summary>Pfeil nach oben — wieder zurückgeholt.</summary>
            Zurückgeholt,
        }

        /// <summary>
        /// Deckkraft im Scheitel. Darüber wird der Schein zum Farbschleier über dem Bild,
        /// darunter geht er im Motiv unter.
        /// </summary>
        private const double KantenscheinStaerke = 0.55;

        /// <summary>
        /// Lässt den Kantenschein einmal aufleuchten. Wird vom Host (MainWindow)
        /// gerufen, sobald eine Taste das Bild tatsächlich verschoben hat.
        ///
        /// Erneutes Aufrufen setzt die laufende Bewegung zurück und beginnt von vorn —
        /// beim schnellen Sortieren blinkt es also im Takt der Tasten, statt sich zu
        /// stapeln.
        /// </summary>
        public void ZeigeKantenschein(Bildablage ablage)
        {
            // Zurückgeholt kommt oben heraus, alles andere geht unten hinaus.
            if (ablage == Bildablage.Zurückgeholt)
            {
                Aufleuchten(BRD_KantenscheinOben);
                return;
            }

            var farbe = ablage switch
            {
                // Gedecktes Ziegelrot: aussortiert, aber kein Alarm.
                Bildablage.KeinFav => Color.FromRgb(0xB3, 0x3A, 0x2B),

                // Dasselbe warme Gold wie Zoomanzeige und Warte-Ring — in dieser Ansicht
                // die Farbe für „hervorgehoben".
                Bildablage.Besonders => Color.FromRgb(0xFF, 0xC4, 0x6B),

                // Violett: der einzige Ton, der weder für gut noch für schlecht steht.
                _ => Color.FromRgb(0x8E, 0x5A, 0xA8),
            };

            GRS_KantenscheinUntenVoll.Color = farbe;
            GRS_KantenscheinUntenLeer.Color = Color.FromArgb(0, farbe.R, farbe.G, farbe.B);

            Aufleuchten(BRD_KantenscheinUnten);
        }

        /// <summary>
        /// Auf und wieder ab in 200 ms. Schneller Anstieg, längeres Abklingen: Der
        /// Einsatz soll auf die Taste fallen, das Nachleuchten darf das Auge einholen.
        /// </summary>
        private static void Aufleuchten(Border kante)
        {
            var anim = new DoubleAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(KantenscheinStaerke, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));

            kante.BeginAnimation(OpacityProperty, anim);
        }

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
