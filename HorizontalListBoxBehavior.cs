using System;
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
                    lb.SelectionChanged += OnSelectionChangedCenter;
                else
                    lb.SelectionChanged -= OnSelectionChangedCenter;
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

        /// <summary>Nachmessungen bei kurzer Strecke – dort ist meist nichts zu korrigieren.</summary>
        private const int NahsprungKorrekturen = 1;

        /// <summary>
        /// Nachmessungen nach einem weiten Sprung. Mehr, weil sich Behälter, Ausdehnung
        /// und der ScrollIntoView-Auftrag dort erst über mehrere Durchläufe einpendeln.
        /// </summary>
        private const int WeitsprungKorrekturen = 3;

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
                lb.ScrollIntoView(lb.SelectedItem);

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
                    weitsprung ? WeitsprungKorrekturen : NahsprungKorrekturen,
                    versuch: 0),
                DispatcherPriority.Background);
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

                animation.Completed += (_, _) =>
                    PlaneKorrektur(lb, ausgewaehlt, NachziehDauerMs, restKorrekturen);

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

            lb.Dispatcher.BeginInvoke(
                () => RolleZurMitte(lb, ausgewaehlt, dauerMs, restKorrekturen - 1, versuch: MaxVersuche),
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
