using System.Windows.Media;

namespace TestImage.Bildersuche
{
    /// <summary>Ein Suchtreffer der Freitextsuche: Bild, Ähnlichkeit, Pfad.</summary>
    public sealed class SuchErgebnis
    {
        public string Path { get; init; } = "";
        public string DateiName { get; init; } = "";
        public string ProzentText { get; init; } = "";
        public ImageSource? Thumb { get; init; }
    }
}
