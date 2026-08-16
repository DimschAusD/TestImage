using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Shell;

namespace TestImage.Converters
{
    /// <summary>
    /// Prozentwert (0 … 100) in den Anteil (0 … 1), den die Taskleiste erwartet.
    ///
    /// Der Fortschritt liegt im ViewModel als Prozentwert vor, weil ihn die ProgressBar
    /// so braucht. <see cref="TaskbarItemInfo.ProgressValue"/> rechnet dagegen mit einem
    /// Anteil. Umrechnen im Konverter statt einer zweiten Eigenschaft im ViewModel –
    /// beide Werte müssten sonst von Hand gleichgehalten werden.
    /// </summary>
    internal sealed class ConverterProzentZuAnteil : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double prozent = value switch
            {
                double d => d,
                int i => i,
                _ => 0
            };

            return Math.Clamp(prozent / 100.0, 0.0, 1.0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Zustand der Fortschrittsanzeige im Taskleisten-Symbol, aus „läuft" und dem
    /// bisherigen Fortschritt.
    ///
    /// Solange noch nichts gezählt wurde, wird bewusst <c>Indeterminate</c> gemeldet:
    /// Vor dem ersten Bild lädt CLIP seine Modelle, das dauert einige Sekunden. Ein
    /// Balken, der so lange auf 0 % steht, sieht aus wie ein Hänger; die laufende
    /// Schraffur sagt richtigerweise „beschäftigt, Dauer noch unbekannt".
    /// </summary>
    internal sealed class ConverterTaskleistenZustand : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool laeuft = values.Length > 0 && values[0] is bool b && b;
            if (!laeuft)
            {
                return TaskbarItemProgressState.None;
            }

            double prozent = values.Length > 1 && values[1] is double d ? d : 0;
            return prozent > 0
                ? TaskbarItemProgressState.Normal
                : TaskbarItemProgressState.Indeterminate;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
