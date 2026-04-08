using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;



namespace TestImage
{
    public static class MouseWheelForwardBehavior
    {
        public static readonly DependencyProperty ForwardMouseWheelProperty =
            DependencyProperty.RegisterAttached(
                "ForwardMouseWheel",
                typeof(bool),
                typeof(MouseWheelForwardBehavior),
                new UIPropertyMetadata(false, OnForwardMouseWheelChanged));

        public static bool GetForwardMouseWheel(DependencyObject obj)
            => (bool)obj.GetValue(ForwardMouseWheelProperty);

        public static void SetForwardMouseWheel(DependencyObject obj, bool value)
            => obj.SetValue(ForwardMouseWheelProperty, value);

        private static void OnForwardMouseWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                if ((bool)e.NewValue)
                    element.PreviewMouseWheel += OnPreviewMouseWheel;
                else
                    element.PreviewMouseWheel -= OnPreviewMouseWheel;
            }
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;

                var parentScrollViewer = FindParent<ScrollViewer>((DependencyObject)sender);
                if (parentScrollViewer != null)
                {
                    var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                    {
                        RoutedEvent = UIElement.MouseWheelEvent,
                        Source = sender
                    };
                    parentScrollViewer.RaiseEvent(args);
                }
            }
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            if (parent is T t) return t;
            return FindParent<T>(parent);
        }
    }

}

