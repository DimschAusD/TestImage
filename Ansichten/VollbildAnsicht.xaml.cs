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
        /// Breite, die das aufgefahrene Eigenschaften-Feld belegt. Der Host holt sie hier
        /// ab, um das Fenster um genau diesen Betrag zu verbreitern — so können Spalte und
        /// Fensterzuwachs nicht auseinanderlaufen.
        /// </summary>
        public const double BildinfoSpaltenbreite = 300;

        /// <summary>Läuft gerade eine Fahrt, und wohin? Fängt Umschalten während der Fahrt ab.</summary>
        private bool? _bildinfoFahrtZiel;

        /// <summary>
        /// Blendet das Eigenschaften-Feld ein oder aus.
        ///
        /// Die Spalte selbst wird nicht animiert, sondern in einem Schritt gesetzt: Der Host
        /// ändert die Fensterbreite im selben Zug, und nur wenn beides in demselben
        /// Layout-Durchlauf passiert, bleibt der Bildbereich exakt gleich breit. Animiert
        /// sind Verschiebung und Deckkraft des Feldes — die kosten kein Layout und keine
        /// Fenstergrössenänderung, laufen also flüssig, egal wie gross das Bild ist.
        ///
        /// Beim Auffahren wird zuerst das Fenster breiter (<paramref name="fensterSchritt"/>),
        /// dann gleitet das Feld in den frei gewordenen Streifen; beim Zuklappen fährt es
        /// erst hinaus, danach gehen Spalte und Fensterbreite zusammen weg. Die
        /// Reihenfolge steht deshalb hier und nicht beim Host.
        /// </summary>
        /// <param name="dauer">Null oder kleiner: sofort setzen, ohne Fahrt.</param>
        /// <param name="fensterSchritt">Ändert die Fensterbreite; läuft im richtigen Moment.</param>
        public void SetzeBildinfoSpalte(bool sichtbar, TimeSpan dauer, Action? fensterSchritt = null)
        {
            _bildinfoFahrtZiel = sichtbar;

            BRD_Bildinfo.BeginAnimation(UIElement.OpacityProperty, null);
            TTF_Bildinfo.BeginAnimation(TranslateTransform.XProperty, null);

            // Ruhewerte sofort setzen; die Fahrt darunter ist nur der Weg dorthin und läuft
            // mit FillBehavior.Stop, damit am Ende nichts nachträglich festgeschrieben
            // werden muss und ein zweiter Tastendruck sofort richtig rechnet.
            BRD_Bildinfo.Opacity = 1;
            TTF_Bildinfo.X = 0;

            if (dauer <= TimeSpan.Zero)
            {
                HalteBildspalte(true);
                BRD_Bildinfo.Visibility = sichtbar ? Visibility.Visible : Visibility.Collapsed;
                fensterSchritt?.Invoke();
                HalteBildspalte(false);

                FeldInFahrt(false);
                _bildinfoFahrtZiel = null;
                return;
            }

            // Herein von rechts, hinaus nach rechts — die Seite, an der das Feld sitzt.
            double vonX = sichtbar ? BildinfoSpaltenbreite : 0;
            double bisX = sichtbar ? 0 : BildinfoSpaltenbreite;

            var beschleunigung = new CubicEase { EasingMode = EasingMode.EaseOut };

            var schieben = new DoubleAnimation(vonX, bisX, new Duration(dauer))
            {
                EasingFunction = beschleunigung,
                FillBehavior = FillBehavior.Stop
            };

            // Deckkraft mit: Ohne sie schöbe sich beim Zuklappen eine harte Kante über den
            // Rand hinaus, statt dass das Feld verschwindet.
            var blenden = new DoubleAnimation(sichtbar ? 0 : 1, sichtbar ? 1 : 0, new Duration(dauer))
            {
                EasingFunction = beschleunigung,
                FillBehavior = FillBehavior.Stop
            };

            schieben.Completed += (_, _) =>
            {
                // Nur aufräumen, wenn inzwischen nicht schon wieder umgeschaltet wurde.
                if (_bildinfoFahrtZiel != sichtbar)
                    return;

                _bildinfoFahrtZiel = null;
                FeldInFahrt(false);

                if (!sichtbar)
                {
                    // Spalte und Fensterbreite in einem Zug, mit festgehaltener Bildspalte:
                    // Dazwischen wird nichts gezeichnet, in dem das Bild anders läge.
                    HalteBildspalte(true);
                    BRD_Bildinfo.Visibility = Visibility.Collapsed;
                    fensterSchritt?.Invoke();
                    HalteBildspalte(false);
                }
            };

            if (sichtbar)
            {
                // Erst Platz schaffen, dann das Feld hineinfahren lassen. Die Bildspalte
                // wird dabei festgehalten, damit der Zuwachs vollständig an den Streifen
                // rechts geht statt für einen Moment an das Bild.
                HalteBildspalte(true);
                fensterSchritt?.Invoke();
                BRD_Bildinfo.Visibility = Visibility.Visible;
                HalteBildspalte(false);
            }
            else
            {
                // Beim Zuklappen muss das Feld bis zum Ende der Fahrt stehen bleiben, sonst
                // gäbe es nichts mehr, was hinausfahren könnte.
                BRD_Bildinfo.Visibility = Visibility.Visible;
            }

            FeldInFahrt(true);
            BRD_Bildinfo.BeginAnimation(UIElement.OpacityProperty, blenden);
            TTF_Bildinfo.BeginAnimation(TranslateTransform.XProperty, schieben);
        }

        /// <summary>
        /// Hält die Bildspalte auf ihrer jetzigen Breite fest, solange sich die
        /// Fensterbreite ändert.
        ///
        /// Eine Änderung von Window.Width zeichnet sofort ein Bild — noch bevor die Spalte
        /// daneben steht. Ohne diesen Halt blitzt das Vollbild darin einmal über die volle
        /// neue Breite skaliert auf. Festgehalten geht der Zuwachs vollständig an den
        /// leeren Streifen rechts, in den das Feld anschliessend hineingleitet, und das
        /// Bild bleibt in jedem einzelnen Bild der Folge gleich gross.
        /// </summary>
        private void HalteBildspalte(bool halten)
        {
            // Nur halten, was auch gemessen ist: Beim Ansichtswechsel ist die Spalte noch
            // ohne Breite, und eine festgeschriebene Null nähme dem Bild die Fläche.
            if (halten && COL_VollbildBild.ActualWidth <= 0)
                return;

            COL_VollbildBild.Width = halten
                ? new GridLength(COL_VollbildBild.ActualWidth, GridUnitType.Pixel)
                : new GridLength(1, GridUnitType.Star);
        }

        /// <summary>
        /// Legt das Feld für die Dauer der Fahrt in eine fertige Textur.
        ///
        /// Verschieben und Blenden sind dann reines Kopieren auf der Grafikkarte; ohne den
        /// Zwischenspeicher würde jedes Bild der Fahrt den Text neu gesetzt und die
        /// Deckkraft auf jedes Element einzeln gerechnet.
        /// </summary>
        private void FeldInFahrt(bool fahrtLäuft)
        {
            if (fahrtLäuft)
                BRD_Bildinfo.CacheMode ??= new BitmapCache();
            else
                BRD_Bildinfo.CacheMode = null;
        }

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

        /// <summary>
        /// Faktor je vollem Rasterschritt des Mausrads (Delta 120). 1,15 statt 1,2:
        /// kleinere Schritte lassen sich genauer treffen, und der Weg dorthin ist kürzer,
        /// also auch früher fertig.
        /// </summary>
        private const double ZoomSchritt = 1.15;

        /// <summary>
        /// Fahrzeit in Sekunden: die Zeit, nach der das Bild wieder steht. <b>Die eine
        /// Stellschraube für das Gefühl.</b>
        ///
        /// <b>Gefedert, nicht gleichmässig:</b> Gefahren wird als kritisch gedämpfte Feder —
        /// aus dem Stand beschleunigen, vor dem Ziel abbremsen, nicht überschwingen. Die
        /// Zwischenstufe mit unveränderlicher Geschwindigkeit war schlechter: Sie beginnt und
        /// endet hart, und gerade das Anhalten sieht man.
        ///
        /// 0,35 s: getragen, aber noch am Rad. Kurze Fahrten (0,09–0,20 s) wirken hastig;
        /// das Weiche daran war nie die Dauer, sondern das unscharfe Zwischenbild, siehe
        /// <see cref="SchnelleSkalierungWaehrendDerBewegung"/>. 0,45 s war der Schritt davor
        /// und lief einen Tick länger nach.
        /// </summary>
        private const double Fahrzeit = 0.35;

        /// <summary>
        /// Geschwindigkeit der Feder: Zoom in Log-Einheiten je Sekunde, Verschiebung in
        /// Bildschirmpunkten je Sekunde.
        ///
        /// Sie ist der Zustand, der eine Fahrt zusammenhält. Ein Radschritt mitten in der
        /// Bewegung biegt sie nur um, statt sie neu zu beginnen — ohne diesen Zustand gäbe
        /// es bei jedem Schritt einen Knick, und mehrere Rastungen würden nicht zu einer
        /// durchgehenden Fahrt verschmelzen.
        /// </summary>
        private double _zoomGeschw, _panGeschwX, _panGeschwY;

        /// <summary>
        /// Gilt der Zeigeranker? Gesetzt beim Rollen, gelöscht beim Ziehen und beim
        /// Zurücksetzen — dort bestimmt die Verschiebung jemand anderes.
        /// </summary>
        private bool _ankerGilt;

        /// <summary>
        /// Der Punkt, an dem das Rollen angesetzt hat: <see cref="_ankerSchirmX"/> in
        /// Bildschirmpunkten von der Mitte aus, <see cref="_ankerBildX"/> derselbe Punkt
        /// im unskalierten Bild.
        ///
        /// <b>Warum das nötig ist:</b> Die Verschiebung hängt <b>multiplikativ</b> an der
        /// Vergrösserung. Lässt man beide getrennt nachlaufen — so war es —, stimmt die
        /// Rechnung nur am Anfang und am Ende; dazwischen wandert der Bildpunkt unter dem
        /// Zeiger weg und kommt erst am Ziel zurück. Genau das sieht man als Davonrutschen.
        /// Deshalb wird die Verschiebung während der Fahrt nicht angenähert, sondern in
        /// jedem Bild aus der aktuellen Stufe neu gerechnet.
        /// </summary>
        private double _ankerSchirmX, _ankerSchirmY, _ankerBildX, _ankerBildY;

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
            // Ungedeckelt: Schnelles Rollen fasst mehrere Rasterschritte in ein Ereignis
            // zusammen und soll dann auch entsprechend weit zoomen. Der frühere Deckel
            // fing die Ausreisser eines defekten Rades ab; gegen zu grosse Sprünge
            // arbeitet ohnehin der Nachlauf, der jede Änderung ausfährt statt zu springen.
            double faktor = Math.Pow(ZoomSchritt, e.Delta / 120.0);
            double neu = Math.Clamp(_zoomZiel * faktor, ZoomMin, ZoomMax);

            if (Math.Abs(neu - _zoomZiel) < 0.0001)
                return;

            // Zum Mauszeiger hin vergrössern: Der Bildpunkt unter dem Zeiger soll dort
            // bleiben, wo er ist. Gemerkt wird er als Punkt im unskalierten Bild, und zwar
            // aus dem *angezeigten* Stand — das ist der, den man gerade sieht und trifft.
            // Ab hier hält ihn AnkerAnwenden in jedem Bild fest, statt ihn nur am Ziel
            // wieder stimmen zu lassen.
            var m = e.GetPosition(GRD_VollbildWurzel);
            _ankerSchirmX = m.X - imgVollbild.ActualWidth / 2;
            _ankerSchirmY = m.Y - imgVollbild.ActualHeight / 2;
            _ankerBildX = (_ankerSchirmX - _panIstX) / _zoomIst;
            _ankerBildY = (_ankerSchirmY - _panIstY) / _zoomIst;
            _ankerGilt = true;

            _zoomZiel = neu;

            // Das Ziel gleich mitführen: Steht der Nachlauf, muss die Verschiebung
            // dieselbe sein, die der Anker errechnet — sonst zuckt es zum Schluss.
            _panZielX = _ankerSchirmX - _ankerBildX * _zoomZiel;
            _panZielY = _ankerSchirmY - _ankerBildY * _zoomZiel;
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

            // Erst jetzt beginnt die Wartezeit bis zur feinen Skalierung. Sonst liefe sie
            // schon während der Fahrt ab — die Zeitkonstante ist länger als die Wartezeit —
            // und die teure Fant-Skalierung stocherte mitten in die Bewegung hinein.
            SchnelleSkalierungWaehrendDerBewegung();
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

            // Der Zoom wird in Log-Einheiten gefahren, weil ein Radschritt ein Faktor ist
            // und kein Betrag: Von 100 auf 200 % soll gleich lange dauern wie von 400 auf
            // 800 %. Linear gefahren schliche der untere Bereich und der obere risse davon.
            _zoomIst = Math.Exp(Feder(Math.Log(_zoomIst), Math.Log(_zoomZiel), ref _zoomGeschw, dt));

            if (_ankerGilt)
            {
                AnkerAnwenden();
            }
            else
            {
                _panIstX = Feder(_panIstX, _panZielX, ref _panGeschwX, dt);
                _panIstY = Feder(_panIstY, _panZielY, ref _panGeschwY, dt);
            }

            // Angekommen, wenn der Rest unter einem Bildpunkt liegt und die Feder zur Ruhe
            // gekommen ist. Die Geschwindigkeit gehört in die Frage: Mitten in der Fahrt
            // kommt der Stand am Ziel vorbei, und ohne sie hielte die Bewegung dort an.
            // Ohne Abbruch wiederum näherte sich die Rechnung endlos an und der Haken
            // bliebe für immer am Bildtakt.
            if (Math.Abs(_zoomZiel - _zoomIst) < 0.0005 && Math.Abs(_zoomGeschw) < 0.002
                && Math.Abs(_panZielX - _panIstX) < 0.05
                && Math.Abs(_panZielY - _panIstY) < 0.05)
            {
                _zoomIst = _zoomZiel;
                _zoomGeschw = 0;

                if (_ankerGilt)
                {
                    // Auf der Zielstufe sitzt der Anker dort, wo die Begrenzung ihn lässt.
                    AnkerAnwenden();
                    _panZielX = _panIstX;
                    _panZielY = _panIstY;
                }
                else
                {
                    _panIstX = _panZielX;
                    _panIstY = _panZielY;
                }

                _panGeschwX = 0;
                _panGeschwY = 0;
                NachlaufAnhalten();
            }

            StandAnwenden();
        }

        /// <summary>
        /// Ein Schritt einer kritisch gedämpften Feder: Sie zieht den Wert zum Ziel,
        /// beschleunigt aus dem Stand, bremst davor ab und schiesst nicht darüber hinaus.
        /// <paramref name="geschwindigkeit"/> wird mitgeführt — sie ist der Grund, warum
        /// mehrere Radschritte zu einer einzigen Fahrt verschmelzen.
        ///
        /// Die Näherung für die Dämpfung ist die übliche (Game Programming Gems 4): über
        /// den hier vorkommenden Bereich genau genug und ohne <c>Math.Exp</c> je Bild.
        /// </summary>
        private static double Feder(double ist, double ziel, ref double geschwindigkeit, double dt)
        {
            // 2 / Fahrzeit: Nach dieser Zeit ist bei kritischer Dämpfung praktisch nichts
            // mehr übrig. Kleiner heisst weichere Feder, also längere Fahrt.
            const double omega = 2.0 / Fahrzeit;

            double x = omega * dt;
            double daempfung = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);

            double abstand = ist - ziel;
            double schub = (geschwindigkeit + omega * abstand) * dt;

            geschwindigkeit = (geschwindigkeit - omega * schub) * daempfung;
            return ziel + (abstand + schub) * daempfung;
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
            (double grenzeX, double grenzeY) = PanGrenzen(_zoomZiel);

            _panZielX = Math.Clamp(_panZielX, -grenzeX, grenzeX);
            _panZielY = Math.Clamp(_panZielY, -grenzeY, grenzeY);
        }

        /// <summary>
        /// Wie weit der Ausschnitt bei dieser Stufe nach beiden Seiten wandern darf.
        /// Getrennt von <see cref="PanBegrenzen"/>, weil die laufende Fahrt gegen die
        /// <b>angezeigte</b> Stufe begrenzt werden muss und nicht gegen die angestrebte.
        /// </summary>
        private (double X, double Y) PanGrenzen(double stufe)
        {
            double breite = imgVollbild.ActualWidth;
            double hoehe = imgVollbild.ActualHeight;

            if (breite <= 0 || hoehe <= 0 || imgVollbild.Source is not ImageSource quelle)
            {
                return (0, 0);
            }

            // Stretch="Uniform": Das Bild füllt nur einen Teil des Elements, der Rest ist
            // Letterbox. Begrenzt wird auf das Bild, nicht auf das Element.
            double einpassung = quelle.Width > 0 && quelle.Height > 0
                ? Math.Min(breite / quelle.Width, hoehe / quelle.Height)
                : 1.0;

            return (Math.Max(0, (quelle.Width * einpassung * stufe - breite) / 2),
                    Math.Max(0, (quelle.Height * einpassung * stufe - hoehe) / 2));
        }

        /// <summary>
        /// Hält den Bildpunkt unter dem Zeiger fest: Die Verschiebung wird aus der gerade
        /// angezeigten Stufe gerechnet, nicht angenähert. Am Anschlag greift die Begrenzung
        /// — dann wandert der Punkt zwangsläufig, weiter ginge es nicht.
        /// </summary>
        private void AnkerAnwenden()
        {
            (double grenzeX, double grenzeY) = PanGrenzen(_zoomIst);

            _panIstX = Math.Clamp(_ankerSchirmX - _ankerBildX * _zoomIst, -grenzeX, grenzeX);
            _panIstY = Math.Clamp(_ankerSchirmY - _ankerBildY * _zoomIst, -grenzeY, grenzeY);
        }

        /// <summary>
        /// Während der Bewegung linear statt hochwertig skalieren. Fant-Skalierung
        /// (HighQuality) kostet bei grossen Bildern pro Einzelbild so viel, dass die
        /// Animation stockt; im Stillstand ist sie wieder gefragt.
        /// </summary>
        private void SchnelleSkalierungWaehrendDerBewegung()
        {
            // Während der Bewegung immer grob zeichnen. Der Versuch, für alles unter
            // 12 Millionen Bildpunkten durchgehend die hochwertige Fant-Skalierung zu
            // behalten, brachte genau die Ruckler zurück, gegen die sie einmal eingeführt
            // wurde: Sie kostet je Bild zu viel. Scharf wird gleich danach, siehe Wartezeit.
            RenderOptions.SetBitmapScalingMode(imgVollbild, BitmapScalingMode.Linear);

            if (_zoomFeinTimer is null)
            {
                _zoomFeinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
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

            // Der Zeigeranker des Rollens gilt nicht mehr: Jetzt bestimmt die Hand, wo das
            // Bild steht. Liesse man ihn stehen, zöge eine noch laufende Zoom-Annäherung
            // das Bild gegen die Bewegung zurück.
            _ankerGilt = false;

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

            // Zurück in die Mitte, nicht zum zuletzt angepeilten Punkt: Der Anker gilt
            // nicht mehr, sonst hielte er das Bild beim Herausfahren seitlich fest.
            _ankerGilt = false;

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

                // Sonst nähme die Feder ihre Fahrt mit ins nächste Bild — das eingepasste
                // Bild ruckte dann beim Wechsel kurz an.
                _zoomGeschw = 0;
                _panGeschwX = 0;
                _panGeschwY = 0;

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

            // Vergrössert gilt eine andere Bedienung; alles ohne eigene Erklärung erbt
            // diesen Text von der Wurzel, das Erklärfeld zieht ihn über die Bindung nach.
            Erklärung.SetText(GRD_VollbildWurzel, (string)FindResource(
                vergroessert ? "ErklärungBildVergrössert" : "ErklärungBildEingepasst"));

            // Vergrössert kommt die linke Taste dazu: Ziehen und Doppelklick.
            Erklärung.SetMaus(GRD_VollbildWurzel, vergroessert ? "Links Rad" : "Rad");
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
