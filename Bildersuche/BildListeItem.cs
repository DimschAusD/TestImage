using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Ein Eintrag der Schnell-Liste: Verweis auf das Bild plus eine (im Hintergrund
    /// nachgeladene) Miniatur. Die Miniatur wird erst nach dem Anlegen gesetzt, damit
    /// nichts auf dem UI-Thread dekodiert wird.
    /// </summary>
    public partial class BildListeItem : ObservableObject
    {
        public MeinBildchen Bild { get; init; } = null!;

        [ObservableProperty]
        private ImageSource? _thumb;

        /// <summary>True, wenn dies das aktuell gewählte Bild ist (für die Markierung).</summary>
        [ObservableProperty]
        private bool _isAktuell;
    }
}
