using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TestImage
{
    /// <summary>
    /// Hält die Auswahl einer senkrechten ListBox sichtbar.
    ///
    /// <c>IsSynchronizedWithCurrentItem</c> markiert die Zeile zwar, rollt aber nicht zu
    /// ihr. In einer 70 Punkte hohen Liste steht die markierte Zeile deshalb meistens
    /// ausserhalb des Sichtfensters – man sieht die Auswahl erst, wenn man von Hand
    /// hinrollt.
    ///
    /// Bewusst nur <c>ScrollIntoView</c> und kein Zentrieren: Hier soll die Zeile
    /// schlicht zu sehen sein. Steht sie bereits im Sichtfenster, geschieht gar nichts —
    /// die Liste bleibt also ruhig, solange man sich innerhalb der sichtbaren Zeilen
    /// bewegt. Das Gegenstück für die waagerechte Miniaturleiste ist
    /// <see cref="HorizontalListBoxBehavior"/>.
    /// </summary>
    public static class ListBoxAuswahlBehavior
    {
        public static readonly DependencyProperty SichtbarHaltenProperty =
            DependencyProperty.RegisterAttached(
                "SichtbarHalten",
                typeof(bool),
                typeof(ListBoxAuswahlBehavior),
                new PropertyMetadata(false, OnSichtbarHaltenChanged));

        public static bool GetSichtbarHalten(DependencyObject d)
            => (bool)d.GetValue(SichtbarHaltenProperty);

        public static void SetSichtbarHalten(DependencyObject d, bool value)
            => d.SetValue(SichtbarHaltenProperty, value);

        private static void OnSichtbarHaltenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ListBox lb)
            {
                return;
            }

            if ((bool)e.NewValue)
            {
                lb.SelectionChanged += OnSelectionChanged;
            }
            else
            {
                lb.SelectionChanged -= OnSelectionChanged;
            }
        }

        private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListBox lb || lb.SelectedItem is null)
            {
                return;
            }

            // Eingeklappte Liste in Ruhe lassen: Sie hat kein Sichtfenster, würde aber
            // Layoutarbeit anstossen – und zwar genau dann, wenn anderswo gerollt wird.
            if (!lb.IsVisible)
            {
                return;
            }

            // Nach dem laufenden Layoutdurchlauf, sonst greift ScrollIntoView bei
            // virtualisierten Listen ins Leere: Der Behälter der neuen Zeile ist zum
            // Zeitpunkt des Ereignisses unter Umständen noch gar nicht erzeugt.
            lb.Dispatcher.BeginInvoke(
                () =>
                {
                    if (lb.IsVisible && lb.SelectedItem is not null)
                    {
                        lb.ScrollIntoView(lb.SelectedItem);
                    }
                },
                DispatcherPriority.Background);
        }
    }
}
