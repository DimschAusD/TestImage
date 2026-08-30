using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace TestImage
{
    /// <summary>
    /// IFileDragDropTarget Interface
    /// </summary>
    public interface IFileDragDropTarget
    {
        // https://stackoverflow.com/questions/5916154/how-to-handle-drag-drop-without-violating-mvvm-principals
        /*void*/  Task OnFileDrop(string[] filepaths);
    }

    /// <summary>
    /// FileDragDropHelper
    /// </summary>
    public static class FileDragDropHelper
    {
        public static bool GetIsFileDragDropEnabled(DependencyObject obj)
        {
            return (bool)obj?.GetValue(IsFileDragDropEnabledProperty);
        }

        public static void SetIsFileDragDropEnabled(DependencyObject obj, bool value)
        {
            obj?.SetValue(IsFileDragDropEnabledProperty, value);
        }

        public static bool GetFileDragDropTarget(DependencyObject obj)
        {
            return (bool)obj?.GetValue(FileDragDropTargetProperty);
        }

        public static void SetFileDragDropTarget(DependencyObject obj, bool value)
        {
            obj?.SetValue(FileDragDropTargetProperty, value);
        }

        public static readonly DependencyProperty IsFileDragDropEnabledProperty =
                DependencyProperty.RegisterAttached("IsFileDragDropEnabled", typeof(bool), typeof(FileDragDropHelper), new PropertyMetadata(OnFileDragDropEnabled));

        public static readonly DependencyProperty FileDragDropTargetProperty =
                DependencyProperty.RegisterAttached("FileDragDropTarget", typeof(object), typeof(FileDragDropHelper), null);

        private static void OnFileDragDropEnabled(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue == e.OldValue) return;

            // FrameworkElement, nicht Control: An einem Image oder Grid hängte sich der
            // Handler sonst wortlos nicht an — die Ablage war dort tot, ohne Hinweis.
            var element = d as FrameworkElement;
            if (element != null) element.Drop += OnDrop;
        }

        private static void OnDrop(object _sender, DragEventArgs _dragEventArgs)
        {
            var dataContext = ((FrameworkElement)_sender).DataContext;
            if (!(dataContext is IFileDragDropTarget filesDropped))
            {
                if (dataContext != null)
                {
                    System.Diagnostics.Trace.TraceError($"Binding error, '{dataContext.GetType().Name}' doesn't implement '{nameof(IFileDragDropTarget)}'.");
                }

                return;
            }

            if (!_dragEventArgs.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            if (_dragEventArgs.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                filesDropped.OnFileDrop(files);
            }
        }
    }
}
