using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace TestImage
{
    /// <summary>
    /// Farbsignatur des angezeigten Bildes – der senkrechte Streifen neben dem Bild
    /// (<c>LLL</c> in der Normalansicht).
    ///
    /// Berechnet wird sie in der Ladestrecke, sobald das 100-px-Vorschaubild vorliegt.
    /// Bewusst kein Converter an <see cref="AufgabeViewModel.DisplayImage"/>: Diese
    /// Eigenschaft wird je Bild zweimal gesetzt (klein, dann gross), ein Converter liefe
    /// also auch auf dem vollen Bild – und zwar im UI-Faden.
    /// </summary>
    public partial class AufgabeViewModel
    {
        /// <summary>
        /// Fertiger, eingefrorener Verlauf. <c>null</c>, solange kein brauchbares Bild
        /// vorliegt – die Fläche bleibt dann leer, der Rahmen zeigt den Streifen weiterhin an.
        /// </summary>
        [ObservableProperty]
        public partial Brush? BildFarbsignatur { get; set; }
    }
}
