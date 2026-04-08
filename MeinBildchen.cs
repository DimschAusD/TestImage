using CommunityToolkit.Mvvm.ComponentModel;

namespace TestImage
{
    public partial class MeinBildchen : ObservableObject
    {
        [ObservableProperty]
        private string _bName = "nulli";

        [ObservableProperty]
        private bool _bildFürLinks = false;
    }
}