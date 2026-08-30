using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TestImage
{
    /// <summary>
    /// Attached Behaviors für horizontale ListBoxen:
    /// 1) MouseWheel → horizontales Scrollen
    /// 2) SelectionChanged → ausgewähltes Item animiert zentrieren
    /// </summary>
    public static class HorizontalListBoxBehavior
    {
        #region EnableHorizontalMouseWheel

        public static readonly DependencyProperty EnableHorizontalMouseWheelProperty =
            DependencyProperty.RegisterAttached(
                "EnableHorizontalMouseWheel",
                typeof(bool),
                typeof(HorizontalListBoxBehavior),
                new PropertyMetadata(false, OnEnableHorizontalMouseWheelChanged));

        public static bool GetEnableHorizontalMouseWheel(DependencyObject d) =>
            (bool)d.GetValue(EnableHorizontalMouseWheelProperty);

        public static void SetEnableHorizontalMouseWheel(DependencyObject d, bool value) =>
            d.SetValue(EnableHorizontalMouseWheelProperty, value);

        private static void OnEnableHorizontalMouseWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ListBox lb)
            {
                if ((bool)e.NewValue)
                    lb.PreviewMouseWheel += OnPreviewMouseWheel;
                else
                    lb.PreviewMouseWheel -= OnPreviewMouseWheel;
            }
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ListBox lb) return;

            var sv = FindVisualChild<ScrollViewer>(lb);
            if (sv == null) return;

            // Mausrad-Delta in horizontale Richtung umwandeln
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        #endregion

        #region CenterSelectedItem (mit Animation)

        public static readonly DependencyProperty CenterSelectedItemProperty =
            DependencyProperty.RegisterAttached(
                "CenterSelectedItem",
                typeof(bool),
                typeof(HorizontalListBoxBehavior),
                new PropertyMetadata(false, OnCenterSelectedItemChanged));

        public static bool GetCenterSelectedItem(DependencyObject d) =>
            (bool)d.GetValue(CenterSelectedItemProperty);

        public static void SetCenterSelectedItem(DependencyObject d, bool value) =>
            d.SetValue(CenterSelectedItemProperty, value);

        private static void OnCenterSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ListBox lb)
            {
                if ((bool)e.NewValue)
                {
                    lb.SelectionChanged += OnSelectionChangedCenter;
                    BeobachteNeuaufbau(lb);
                }
                else
                {
                    lb.SelectionChanged -= OnSelectionChangedCenter;
                    BeendeNeuaufbauBeobachtung(lb);
                }
            }
        }

        /// <summary>Merker je Leiste: Eine angemeldete Zentrierung genügt für einen Neuaufbau.</summary>
        private static readonly DependencyProperty ZentrierungStehtProperty =
            DependencyProperty.RegisterAttached(
                "ZentrierungSteht",
                typeof(bool),
                typeof(HorizontalListBoxBehavior),
                new PropertyMetadata(false));

        /// <summary>
        /// Der angemeldete Beobachter, damit er beim Abschalten wieder abgehängt werden
        /// kann — die Sammlung selbst führt nicht zur ListBox zurück.
        /// </summary>
        private static readonly DependencyProperty SammlungsBeobachterProperty =
            DependencyProperty.RegisterAttached(
                "SammlungsBeobachter",
                typeof(NotifyCollectionChangedEventHandler),
                typeof(HorizontalListBoxBehavior),
                new PropertyMetadata(null));

        /// <summary>
        /// Auch auf den Neuaufbau der Liste hören, nicht nur auf den Auswahlwechsel.
        ///
        /// Beim Ablegen einer Datei aus einem anderen Ordner wird die Liste geleert, neu
        /// gefüllt und zuletzt aufgefrischt (<c>ListCollectionView.Refresh</c>). Nach dieser
        /// Auffrischung steht die Auswahl wieder auf demselben Element wie davor — für die
        /// ListBox ist das kein Wechsel, also kommt kein SelectionChanged mehr. Die Behälter
        /// sind aber sämtlich neu und der Versatz gehört noch zum vorigen Ordner. Ohne
        /// diesen Anstoss bleibt die Leiste dort stehen, wo sie der alte Ordner verlassen
        /// hat — sichtbar vor allem, wenn das zuletzt gewählte Bild das letzte der Reihe war.
        /// </summary>
        private static void BeobachteNeuaufbau(ListBox lb)
        {
            if (lb.Items is not INotifyCollectionChanged beobachtbar) return;

            void AufSammlungGeaendert(object? _, NotifyCollectionChangedEventArgs e)
            {
                // Nur der vollständige Neuaufbau. Das Einlesen fügt Bild für Bild ein; als
                // Add darf das die Leiste nicht bei jeder einzelnen Datei anstossen.
                if (e.Action != NotifyCollectionChangedAction.Reset) return;

                if ((bool)lb.GetValue(ZentrierungStehtProperty)) return;
                lb.SetValue(ZentrierungStehtProperty, true);

                // ContextIdle: Erst wenn die ListBox den Neuaufbau verarbeitet, die Auswahl
                // nachgezogen und das Panel angeordnet hat, gibt es etwas zu messen.
                lb.Dispatcher.BeginInvoke(
                    () =>
                    {
                        lb.SetValue(ZentrierungStehtProperty, false);
                        ZentriereAusgewaehltes(lb);
                    },
                    DispatcherPriority.ContextIdle);
            }

            NotifyCollectionChangedEventHandler beobachter = AufSammlungGeaendert;

            beobachtbar.CollectionChanged += beobachter;
            lb.SetValue(SammlungsBeobachterProperty, beobachter);
        }

        private static void BeendeNeuaufbauBeobachtung(ListBox lb)
        {
            if (lb.GetValue(SammlungsBeobachterProperty) is NotifyCollectionChangedEventHandler beobachter
                && lb.Items is INotifyCollectionChanged beobachtbar)
            {
                beobachtbar.CollectionChanged -= beobachter;
                lb.ClearValue(SammlungsBeobachterProperty);
            }
        }

        /// <summary>Dauer des eigentlichen Zentrierens.</summary>
        private const int RollDauerMs = 480;

        /// <summary>
        /// Dauer der Nachkorrektur. Kurz, weil dabei meist nur wenige Pixel fehlen –
        /// und wenn doch mehr, will man nicht noch einmal eine halbe Sekunde zusehen.
        /// </summary>
        private const int NachziehDauerMs = 90;

        /// <summary>Ab dieser Abweichung in Pixeln gilt die Mitte als nicht getroffen.</summary>
        private const double MittenToleranz = 1.5;

        /// <summary>
        /// Wie oft auf den Behälter des Elements gewartet wird. Bei weiten Sprüngen
        /// braucht die Virtualisierung mehrere Layoutdurchläufe, bis er da ist.
        /// </summary>
        private const int MaxVersuche = 6;

        /// <summary>
        /// Obergrenze der Nachmessungen. Notbremse, kein Regelfall: Sobald die Mitte
        /// innerhalb von <see cref="MittenToleranz"/> getroffen ist, hört die Kette von
        /// selbst auf – auch dann, wenn das Element am Listenende gar nicht mittig
        /// stehen kann, denn dort ist der geklemmte Zielwert nach einem Durchgang erreicht.
        ///
        /// Vorher gab es zwei feste Vorräte, 1 bei kurzer und 3 bei weiter Strecke. Das
        /// war zu knapp und genau die Ursache dafür, dass die Leiste zwar rollte, aber
        /// neben dem gesuchten Bild stehen blieb: Bei Pixel-Virtualisierung ist
        /// <c>ExtentWidth</c> eine Hochrechnung, die sich mit jedem neu erzeugten Element
        /// ändert. Ein vorher berechneter Versatz bedeutet danach etwas anderes. War der
        /// Vorrat aufgebraucht, blieb der Rest stehen — und erst der nächste Klick brachte
        /// zwei weitere Messungen mit. Deshalb wurde es „nach mehreren Klicks von selbst
        /// richtig". Jetzt wird nachgemessen, bis es stimmt.
        /// </summary>
        private const int MaxKorrekturen = 10;

        private static void OnSelectionChangedCenter(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListBox lb) return;

            ZentriereAusgewaehltes(lb);
        }

        /// <summary>
        /// Bringt das ausgewählte Element in die Mitte des sichtbaren Bereichs.
        /// </summary>
        private static void ZentriereAusgewaehltes(ListBox lb)
        {
            if (lb?.SelectedItem == null) return;

            // Unsichtbare Leiste in Ruhe lassen.
            //
            // Es gibt zwei Miniaturleisten auf derselben AufgabenView, eine in der
            // Normal- und eine in der Vollbildansicht. Immer nur eine davon ist sichtbar,
            // beide bekommen aber über IsSynchronizedWithCurrentItem jede Auswahländerung
            // mit. Zentrieren kann die eingeklappte ohnehin nichts – sie hat weder
            // Sichtfenster noch erzeugte Behälter. Sie würde dabei aber Layoutarbeit
            // anstossen, und zwar genau während die sichtbare Leiste noch rollt.
            //
            // Beim Sichtbarwerden ruft die Vollbildansicht CenterNow auf, das Zentrieren
            // wird also nicht verschluckt, sondern nur auf den richtigen Zeitpunkt gelegt.
            if (!lb.IsVisible) return;

            // ScrollIntoView NUR, wenn der Behälter fehlt – das Element also gar nicht
            // im Sichtfenster liegt und sonst nicht messbar wäre.
            //
            // Bei einem bereits erzeugten Element wäre der Aufruf nicht nur überflüssig,
            // sondern schädlich: ScrollIntoView legt bei virtualisierten Listen einen
            // Auftrag an, der das Element „gerade eben sichtbar" schieben will und über
            // mehrere Layoutdurchläufe hinweg nachfasst. Ist das Element vollständig
            // sichtbar, ist der Auftrag sofort erfüllt und tut nichts. Ist es aber – wie
            // das erste und das letzte der Reihe – nur teilweise sichtbar, rechnet der
            // Auftrag immer wieder einen Rest aus und zieht gegen unsere Rollbewegung
            // zurück an den Rand. Genau das sieht wie ein Wackler aus, und genau deshalb
            // trifft es nur die beiden äusseren Elemente und keines der mittleren.
            bool weitsprung = lb.ItemContainerGenerator.ContainerFromItem(lb.SelectedItem) is null;
            if (weitsprung)
            {
                // Weiter Sprung ohne ScrollIntoView – siehe SpringeGrobZuIndex.
                //
                // ScrollIntoView war hier bis zuletzt der Grund, warum ausgerechnet der
                // Sprung aus der Kachelliste beim ersten Mal danebenging: Sein Auftrag
                // arbeitet weiter, nachdem wir zentriert haben, und zieht das Element
                // zurück an den Rand. Erst der nächste Klick lief sauber, weil das Ziel
                // dann schon erzeugt war und dieser Zweig gar nicht mehr betreten wurde.
                var svSprung = FindVisualChild<ScrollViewer>(lb);
                if (svSprung is null || !SpringeGrobZuIndex(lb, svSprung, lb.SelectedItem))
                {
                    // Nur als Rückfall, wenn sich die Lage nicht rechnen lässt – etwa
                    // bevor die Leiste das erste Mal gemessen wurde.
                    lb.ScrollIntoView(lb.SelectedItem);
                }
            }

            // Weiter Sprung wird gesetzt, nicht gerollt.
            //
            // Fehlt der Behälter, liegt das Ziel ausserhalb – etwa beim Sprung aus der
            // Kachelliste. Dann arbeitet der ScrollIntoView-Auftrag noch über mehrere
            // Layoutdurchläufe, und eine halbe Sekunde Animation läuft die ganze Zeit
            // dagegen an. Ausserdem ist eine Rollbewegung über tausend Bilder hinweg
            // nicht zu verfolgen; sie sieht nur zäh aus. Deshalb dort direkt setzen und
            // dafür öfter nachkorrigieren, bis sich der Auftrag beruhigt hat.
            lb.Dispatcher.BeginInvoke(
                () => RolleZurMitte(
                    lb,
                    lb.SelectedItem,
                    weitsprung ? 0 : RollDauerMs,
                    MaxKorrekturen,
                    versuch: 0),
                DispatcherPriority.Background);

            if (weitsprung)
                PlaneSpaeteKontrollen(lb, lb.SelectedItem);
        }

        /// <summary>
        /// Springt grob an die Stelle des Elements, ohne <c>ScrollIntoView</c> zu benutzen.
        ///
        /// <b>Warum nicht ScrollIntoView:</b> Es legt einen Auftrag an, der das Element
        /// „gerade eben sichtbar" schieben will und dafür über mehrere Layoutdurchläufe
        /// nachfasst. Über tausende Kacheln hinweg ist er nach unserem Zentrieren noch
        /// nicht fertig und zieht das Element anschliessend an den Rand des Sichtfensters
        /// zurück – aus der Mitte heraus. Dagegen anzumessen ist ein Wettlauf, den mal der
        /// eine und mal der andere gewinnt.
        ///
        /// <b>Warum eine Rechnung genügt:</b> Die Kacheln sind alle gleich breit – die
        /// Vorlage setzt das Bild fest auf 80 × 80. Damit ist die Breite einer Kachel
        /// schlicht <c>ExtentWidth / Anzahl</c>, und die Lage der n-ten Kachel ist exakt
        /// bestimmt. Es wird also direkt an den Zielversatz gesetzt, ohne dass jemand
        /// hinterher daran zieht. Den Rest – ein paar Pixel aus der Hochrechnung der
        /// Gesamtbreite – erledigt das anschliessende Nachmessen wie bei jedem anderen
        /// Sprung auch.
        /// </summary>
        /// <returns>False, wenn sich die Lage nicht rechnen lässt; dann bleibt nur ScrollIntoView.</returns>
        private static bool SpringeGrobZuIndex(ListBox lb, ScrollViewer sv, object ausgewaehlt)
        {
            int anzahl = lb.Items.Count;
            if (anzahl <= 0 || sv.ExtentWidth <= 0 || sv.ViewportWidth <= 0)
                return false;

            int index = lb.Items.IndexOf(ausgewaehlt);
            if (index < 0)
                return false;

            double kachelBreite = sv.ExtentWidth / anzahl;
            double ziel = ((index + 0.5) * kachelBreite) - (sv.ViewportWidth / 2.0);

            SetzeVersatz(sv, Math.Max(0, Math.Min(ziel, sv.ExtentWidth - sv.ViewportWidth)));
            return true;
        }

        /// <summary>
        /// Späte Kontrollen nach einem weiten Sprung, in Millisekunden.
        ///
        /// Die Kette der Nachmessungen läuft über <see cref="DispatcherPriority.ContextIdle"/>
        /// und ist nach wenigen Bildwechseln abgearbeitet. Der ScrollIntoView-Auftrag
        /// arbeitet aber länger: Über tausende Elemente hinweg fasst er mehrfach nach,
        /// korrigiert dabei die Hochrechnung der Gesamtbreite und zieht das Element
        /// zuletzt an den Rand des Sichtfensters — also aus unserer Mitte heraus. Beim
        /// Sprung aus der Kachelliste kommt hinzu, dass das Popup mit einer Blende
        /// schliesst und dabei erneut Layout anstösst.
        ///
        /// Beides passiert nach unserem letzten Messpunkt. Zwei späte Kontrollen fangen
        /// es ab; sie kosten nichts, wenn ohnehin alles stimmt, denn
        /// <see cref="RolleZurMitte"/> steigt bei getroffener Mitte sofort wieder aus.
        /// </summary>
        private static readonly int[] SpaeteKontrollenMs = { 150, 400 };

        /// <summary>
        /// Meldet die späten Kontrollen an. Ändert sich die Auswahl vorher, laufen sie
        /// ins Leere — <see cref="RolleZurMitte"/> prüft das selbst.
        /// </summary>
        private static void PlaneSpaeteKontrollen(ListBox lb, object ausgewaehlt)
        {
            foreach (int ms in SpaeteKontrollenMs)
            {
                var uhr = new DispatcherTimer(DispatcherPriority.ContextIdle, lb.Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(ms)
                };

                uhr.Tick += (_, _) =>
                {
                    uhr.Stop();
                    RolleZurMitte(lb, ausgewaehlt, dauerMs: 0, restKorrekturen: 2, versuch: 0);
                };

                uhr.Start();
            }
        }

        /// <summary>
        /// Rollt das Element mittig und misst nach dem Rollen noch einmal nach.
        ///
        /// Das Nachmessen ist nötig, weil die Liste virtualisiert mit Pixel-Einheiten
        /// arbeitet: <c>ExtentWidth</c> ist dann keine gemessene Grösse, sondern eine
        /// Hochrechnung aus den bereits erzeugten Elementen. Während des Rollens kommen
        /// neue Elemente dazu, WPF korrigiert die Hochrechnung – und der vorher
        /// berechnete Zielwert bedeutet hinterher etwas anderes. Beim ersten Durchgang
        /// durch einen Listenabschnitt landet man deshalb neben der Mitte, beim zweiten
        /// stimmt es. Statt diese Verschiebung vorherzusagen, wird am Ende schlicht die
        /// dann gültige Lage gemessen und der Rest korrigiert.
        /// </summary>
        /// <param name="ausgewaehlt">
        /// Das Element, um das es ging. Hat sich die Auswahl inzwischen geändert, wird
        /// nicht mehr nachgezogen – sonst kämpfte die Korrektur gegen den neuen Lauf.
        /// </param>
        /// <param name="dauerMs">Dauer der Rollbewegung; 0 setzt den Versatz ohne Animation.</param>
        /// <param name="restKorrekturen">Wie oft danach noch nachgemessen werden darf.</param>
        /// <param name="versuch">
        /// Laufender Versuch, solange der Behälter noch fehlt. Siehe <see cref="MaxVersuche"/>.
        /// </param>
        private static void RolleZurMitte(
            ListBox lb, object ausgewaehlt, int dauerMs, int restKorrekturen, int versuch)
        {
            if (!lb.IsVisible) return;
            if (!ReferenceEquals(lb.SelectedItem, ausgewaehlt)) return;

            var sv = FindVisualChild<ScrollViewer>(lb);
            if (sv == null || sv.ViewportWidth <= 0) return;

            if (lb.ItemContainerGenerator.ContainerFromItem(ausgewaehlt) is not FrameworkElement container)
            {
                // Behälter noch nicht erzeugt. Das ist der Normalfall beim Sprung zu einem
                // weit entfernten Bild – etwa aus der Schnell-Liste heraus: ScrollIntoView
                // erzeugt den Behälter nicht sofort, sondern über mehrere Layoutdurchläufe.
                //
                // Deshalb mehrfach nachfassen. Ein einzelner zweiter Versuch reicht bei
                // weiten Sprüngen nicht, und mit UpdateLayout einen vollständigen
                // Layoutdurchlauf zu erzwingen ist keine Lösung: Der trifft den ganzen
                // Fensterbaum und bringt laufende Rollbewegungen anderswo aus dem Tritt.
                if (versuch < MaxVersuche)
                {
                    lb.Dispatcher.BeginInvoke(
                        () => RolleZurMitte(lb, ausgewaehlt, dauerMs, restKorrekturen, versuch + 1),
                        DispatcherPriority.ContextIdle);
                }

                return;
            }

            // Behälter da, aber noch nicht unserer oder noch nicht an seiner Stelle.
            //
            // Die Leiste läuft mit VirtualizationMode="Recycling": Ein Behälter wird für
            // ein neues Element wiederverwendet, und zwischen dem Umhängen und dem neuen
            // Anordnen liegt ein Layoutdurchlauf. Wer ihn dazwischen misst, bekommt die
            // Lage des vorigen Elements — und rollt an eine Stelle, die mit dem gesuchten
            // Bild nichts zu tun hat. Dasselbe gilt, solange auf dem Panel noch ein
            // Rollauftrag offen ist: Dann gehören Versatz und gemessene Lage nicht
            // zusammen. In beiden Fällen lieber noch einen Durchlauf warten.
            bool gehoertDazu = ReferenceEquals(
                lb.ItemContainerGenerator.ItemFromContainer(container), ausgewaehlt);
            bool angeordnet = VisualTreeHelper.GetParent(container) is not UIElement panel
                              || panel.IsArrangeValid;

            if (!gehoertDazu || !angeordnet)
            {
                if (versuch < MaxVersuche)
                {
                    lb.Dispatcher.BeginInvoke(
                        () => RolleZurMitte(lb, ausgewaehlt, dauerMs, restKorrekturen, versuch + 1),
                        DispatcherPriority.ContextIdle);
                }

                return;
            }

            try
            {
                double ziel = BerechneMittenVersatz(sv, container);

                // Schon mittig – dann weder rollen noch nachziehen.
                if (Math.Abs(ziel - sv.HorizontalOffset) < MittenToleranz)
                    return;

                if (dauerMs <= 0)
                {
                    SetzeVersatz(sv, ziel);
                    PlaneKorrektur(lb, ausgewaehlt, 0, restKorrekturen);
                    return;
                }

                var animation = new DoubleAnimation
                {
                    From = sv.HorizontalOffset,
                    To = ziel,
                    Duration = TimeSpan.FromMilliseconds(dauerMs),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                // Erste Nachkorrektur noch mit kurzer Bewegung, danach ohne: Eine Kette
                // vieler 90-ms-Bewegungen sieht aus wie Zittern, und ab der zweiten
                // Korrektur geht es ohnehin nur noch um wenige Pixel.
                int folgeDauer = dauerMs == RollDauerMs ? NachziehDauerMs : 0;

                animation.Completed += (_, _) =>
                    PlaneKorrektur(lb, ausgewaehlt, folgeDauer, restKorrekturen);

                AnimatableScrollOffset.SetOffset(sv, sv.HorizontalOffset);
                sv.BeginAnimation(AnimatableScrollOffset.OffsetProperty, animation);
            }
            catch
            {
                // TransformToAncestor kann fehlschlagen wenn Baum nicht bereit
            }
        }

        /// <summary>
        /// Meldet einen weiteren Messdurchgang an. Der Vorrat ist begrenzt, damit sich
        /// die Korrektur nicht endlos selbst nachjustiert.
        /// </summary>
        private static void PlaneKorrektur(ListBox lb, object ausgewaehlt, int dauerMs, int restKorrekturen)
        {
            if (restKorrekturen <= 0) return;

            // versuch: 0, nicht MaxVersuche – auch eine Nachmessung darf auf den Behälter
            // warten. Mit aufgebrauchtem Vorrat bricht ein gerade wiederverwendeter oder
            // noch nicht erzeugter Behälter die Kette sofort ab.
            lb.Dispatcher.BeginInvoke(
                () => RolleZurMitte(lb, ausgewaehlt, dauerMs, restKorrekturen - 1, versuch: 0),
                DispatcherPriority.ContextIdle);
        }

        /// <summary>
        /// Setzt den Versatz ohne Animation.
        ///
        /// Der Umweg über die Hilfseigenschaft ist nötig, weil eine zuvor gelaufene
        /// Animation den Wert festhält. Erst den Grundwert auf den Istwert setzen, dann
        /// die Animation entfernen – sonst spränge sie beim Entfernen auf ihren alten
        /// Grundwert zurück –, und erst danach das Ziel setzen.
        /// </summary>
        private static void SetzeVersatz(ScrollViewer sv, double ziel)
        {
            AnimatableScrollOffset.SetOffset(sv, sv.HorizontalOffset);
            sv.BeginAnimation(AnimatableScrollOffset.OffsetProperty, null);
            AnimatableScrollOffset.SetOffset(sv, ziel);
        }

        /// <summary>Versatz, bei dem das Element mittig im sichtbaren Bereich steht.</summary>
        private static double BerechneMittenVersatz(ScrollViewer sv, FrameworkElement container)
        {
            // TransformToAncestor liefert die Lage im sichtbaren Bereich, nicht im
            // Gesamtinhalt – der aktuelle Versatz muss daher dazugerechnet werden.
            double itemCenter = container
                .TransformToAncestor(sv)
                .Transform(new Point(container.ActualWidth / 2.0, 0))
                .X;

            double ziel = sv.HorizontalOffset + itemCenter - (sv.ViewportWidth / 2.0);

            return Math.Max(0, Math.Min(ziel, sv.ExtentWidth - sv.ViewportWidth));
        }

        #endregion

        #region AnimatableScrollOffset (Hilfs-Attached-Property für Animation)

        /// <summary>
        /// Da ScrollViewer.HorizontalOffset nicht direkt animierbar ist,
        /// nutzen wir eine Attached DP, deren PropertyChanged-Callback
        /// ScrollToHorizontalOffset aufruft.
        /// </summary>
        private static class AnimatableScrollOffset
        {
            public static readonly DependencyProperty OffsetProperty =
                DependencyProperty.RegisterAttached(
                    "Offset",
                    typeof(double),
                    typeof(AnimatableScrollOffset),
                    new PropertyMetadata(0.0, OnOffsetChanged));

            public static double GetOffset(DependencyObject d) => (double)d.GetValue(OffsetProperty);
            public static void SetOffset(DependencyObject d, double value) => d.SetValue(OffsetProperty, value);

            private static void OnOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            {
                if (d is ScrollViewer sv)
                    sv.ScrollToHorizontalOffset((double)e.NewValue);
            }
        }

        #endregion

        #region CenterNow (manuell aufrufbar)

        /// <summary>
        /// Zentriert von Hand. Läuft über denselben Weg wie das Ereignis, damit es
        /// nur eine Fassung dieser Rechnung gibt – vorher stand sie zweimal da, und
        /// eine Korrektur an einer Stelle wäre an der anderen wirkungslos geblieben.
        /// </summary>
        public static void CenterNow(ListBox lb) => ZentriereAusgewaehltes(lb);

        #endregion

        #region Helpers

        private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        #endregion
    }
}
