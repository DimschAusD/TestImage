// File: ListBoxCenterSelectedItemBehavior.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TestImage
{
    public static class ListBoxCenterSelectedItemBehavior
    {
        public static readonly DependencyProperty EnableCenterSelectedItemProperty =
            DependencyProperty.RegisterAttached(
                "EnableCenterSelectedItem",
                typeof(bool),
                typeof(ListBoxCenterSelectedItemBehavior),
                new PropertyMetadata(false, OnEnableChanged));

        public static void SetEnableCenterSelectedItem(DependencyObject d, bool value) =>
            d.SetValue(EnableCenterSelectedItemProperty, value);

        public static bool GetEnableCenterSelectedItem(DependencyObject d) =>
            (bool)d.GetValue(EnableCenterSelectedItemProperty);

        private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ListBox lb)
            {
                if ((bool)e.NewValue)
                    lb.SelectionChanged += ListBox_SelectionChanged;
                else
                    lb.SelectionChanged -= ListBox_SelectionChanged;
            }
        }

        private static void ListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListBox lb) return;
            if (lb.SelectedItem == null) return;

            // Verzögert ausführen, damit Virtualization/Template ausgelöst ist
            lb.Dispatcher.BeginInvoke((Action)(() =>
            {
                var item = lb.ItemContainerGenerator.ContainerFromItem(lb.SelectedItem) as FrameworkElement;
                if (item == null)
                {
                    lb.UpdateLayout();
                    item = lb.ItemContainerGenerator.ContainerFromItem(lb.SelectedItem) as FrameworkElement;
                    if (item == null) return;
                }

                // ScrollViewer in ListBox finden
                var sv = FindVisualChild<ScrollViewer>(lb);
                if (sv == null) return;

                // sicherstellen, dass Item sichtbar ist (realisiert)
                item.BringIntoView();

                // nochmals kurz verzögern, damit Layout aktualisiert ist
                lb.Dispatcher.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        var transform = item.TransformToAncestor(sv);
                        var pos = transform.Transform(new Point(0, 0));
                        double itemCenter = pos.X + item.ActualWidth / 2.0;
                        double target = itemCenter - sv.ViewportWidth / 2.0;

                        if (target < 0) target = 0;
                        double max = Math.Max(0, sv.ExtentWidth - sv.ViewportWidth);
                        if (target > max) target = max;

                        sv.ScrollToHorizontalOffset(target);
                    }
                    catch
                    {
                        // falls Transform fehlschlägt, ignorieren
                    }
                }), DispatcherPriority.Background);
            }), DispatcherPriority.Background);
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var res = FindVisualChild<T>(child);
                if (res != null) return res;
            }
            return null;
        }
    }
}