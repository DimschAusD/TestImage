using CommunityToolkit.Mvvm.ComponentModel;

namespace TestImage
{
    public partial class MeinBildchen : ObservableObject
    {
        [ObservableProperty]
        private string _bName = "nulli";

        [ObservableProperty]
        private bool _bildFürLinks = false;

        /// <summary>
        /// True, wenn beim Indexieren ein Wasserzeichen gefunden wurde – sichtbar
        /// aufgeprägt oder als Markierung in den Metadaten. Steuert das Badge auf
        /// der Miniatur.
        /// </summary>
        [ObservableProperty]
        private bool _hatWasserzeichen = false;

        /// <summary>Begründung für den Tooltip des Wasserzeichen-Badges.</summary>
        [ObservableProperty]
        private string _wasserzeichenGrund = string.Empty;
    }
}