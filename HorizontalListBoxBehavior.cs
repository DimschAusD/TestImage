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

        private static void OnSelectionChangedCenter(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListBox lb) return;
            if (lb.SelectedItem == null) return;

            // Erst sicherstellen, dass das Element geladen/realisiert ist
            lb.ScrollIntoView(lb.SelectedItem);

            // Verzögert ausführen, damit Virtualisierung & Layout abgeschlossen sind
            lb.Dispatcher.BeginInvoke(() =>
            {
                var sv = FindVisualChild<ScrollViewer>(lb);
                if (sv == null) return;

                var container = lb.ItemContainerGenerator.ContainerFromItem(lb.SelectedItem) as FrameworkElement;
                if (container == null)
                {
                    lb.UpdateLayout();
                    container = lb.ItemContainerGenerator.ContainerFromItem(lb.SelectedItem) as FrameworkElement;
                    if (container == null) return;
                }

                try
                {
                    // Position des Items relativ zum ScrollViewer berechnen
                    double itemCenter = container
                        .TransformToAncestor(sv)
                        .Transform(new Point(container.ActualWidth / 2.0, 0))
                        .X;

                    // Ziel-Offset: Item in die Mitte des sichtbaren Bereichs
                    double targetOffset = sv.HorizontalOffset + itemCenter - (sv.ViewportWidth / 2.0);

                    // Begrenzen
                    targetOffset = Math.Max(0, Math.Min(targetOffset, sv.ExtentWidth - sv.ViewportWidth));

                    // Animiert scrollen
                    var animation = new DoubleAnimation
                    {
                        From = sv.HorizontalOffset,
                        To = targetOffset,
                        Duration = TimeSpan.FromMilliseconds(480),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    AnimatableScrollOffset.SetOffset(sv, sv.HorizontalOffset);
                    sv.BeginAnimation(AnimatableScrollOffset.OffsetProperty, animation);
                }
                catch
                {
                    // TransformToAncestor kann fehlschlagen wenn Baum nicht bereit
                }
            }, DispatcherPriority.Background);
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
