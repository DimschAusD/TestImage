using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace TestImage
{
    /// <summary>
    /// Macht ein beliebiges Element zur Ablagefläche für einen Ordner: Zieht man eine
    /// Datei darauf, wird deren Ordner ermittelt und an <see cref="CommandProperty"/>
    /// übergeben; zieht man einen Ordner darauf, wird dieser direkt genommen.
    ///
    /// Absichtlich getrennt von <see cref="FileDragDropHelper"/>: Der ruft
    /// <c>IFileDragDropTarget.OnFileDrop</c> auf und lädt damit die ganze Bildliste neu.
    /// Hier soll nur ein Pfad gesetzt werden.
    /// </summary>
    public static class OrdnerDropHelper
    {
        /// <summary>Command, das den ermittelten Ordnerpfad als Parameter erhält.</summary>
        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.RegisterAttached(
                "Command",
                typeof(ICommand),
                typeof(OrdnerDropHelper),
                new PropertyMetadata(null, AufCommandGeaendert));

        public static ICommand? GetCommand(DependencyObject d) => (ICommand?)d.GetValue(CommandProperty);

        public static void SetCommand(DependencyObject d, ICommand? wert) => d.SetValue(CommandProperty, wert);

        /// <summary>
        /// True, solange ein gültiger Ordner über dem Element schwebt. Für Style-Trigger
        /// gedacht, damit die Ablagefläche sichtbar reagiert.
        /// </summary>
        private static readonly DependencyPropertyKey IstZielAktivKey =
            DependencyProperty.RegisterAttachedReadOnly(
                "IstZielAktiv",
                typeof(bool),
                typeof(OrdnerDropHelper),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IstZielAktivProperty = IstZielAktivKey.DependencyProperty;

        public static bool GetIstZielAktiv(DependencyObject d) => (bool)d.GetValue(IstZielAktivProperty);

        private static void SetzeZielAktiv(DependencyObject d, bool wert) =>
            d.SetValue(IstZielAktivKey, wert);

        private static void AufCommandGeaendert(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element)
                return;

            // Alte Anmeldungen lösen, damit ein erneutes Setzen nicht doppelt registriert.
            element.DragEnter -= BeimZiehenUeber;
            element.DragOver -= BeimZiehenUeber;
            element.DragLeave -= BeimVerlassen;
            element.Drop -= BeimAblegen;

            if (e.NewValue is null)
            {
                element.AllowDrop = false;
                return;
            }

            element.AllowDrop = true;
            element.DragEnter += BeimZiehenUeber;
            element.DragOver += BeimZiehenUeber;
            element.DragLeave += BeimVerlassen;
            element.Drop += BeimAblegen;
        }

        private static void BeimZiehenUeber(object sender, DragEventArgs e)
        {
            bool passt = HoleOrdner(e) != null;

            e.Effects = passt ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;

            if (sender is DependencyObject d)
                SetzeZielAktiv(d, passt);
        }

        private static void BeimVerlassen(object sender, DragEventArgs e)
        {
            if (sender is DependencyObject d)
                SetzeZielAktiv(d, false);
        }

        private static void BeimAblegen(object sender, DragEventArgs e)
        {
            if (sender is not DependencyObject d)
                return;

            SetzeZielAktiv(d, false);
            e.Handled = true;

            string? ordner = HoleOrdner(e);
            if (ordner is null)
                return;

            var command = GetCommand(d);
            if (command?.CanExecute(ordner) == true)
                command.Execute(ordner);
        }

        /// <summary>
        /// Ordner aus den gezogenen Daten bestimmen: bei einem Verzeichnis dieses selbst,
        /// bei einer Datei deren Verzeichnis. Null, wenn nichts Brauchbares dabei ist.
        /// </summary>
        private static string? HoleOrdner(DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                    return null;

                if (e.Data.GetData(DataFormats.FileDrop) is not string[] pfade || pfade.Length == 0)
                    return null;

                string erster = pfade[0];

                if (Directory.Exists(erster))
                    return erster;

                if (File.Exists(erster))
                {
                    string? ordner = Path.GetDirectoryName(erster);
                    return string.IsNullOrEmpty(ordner) ? null : ordner;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
