using System.IO;

namespace TestImage.Geraete
{
    /// <summary>
    /// Macht aus dem Registrierungsnamen einer Anwendung einen lesbaren Kurznamen.
    /// </summary>
    internal static class AnwendungsNameHelper
    {
        public static string HoleAnzeigeName(string anwendung)
        {
            if (string.IsNullOrWhiteSpace(anwendung))
            {
                return "Unbekannt";
            }

            // Desktop-Anwendung: enthält einen Backslash, ist also ein Pfad zur .exe.
            // "C:\Program Files\Zoom\bin\Zoom.exe" → "Zoom"
            if (anwendung.Contains('\\'))
            {
                var dateiname = Path.GetFileNameWithoutExtension(anwendung);
                return string.IsNullOrWhiteSpace(dateiname) ? anwendung : dateiname;
            }

            // Store-Anwendung: "Microsoft.SkypeApp_kzf8qxf38zg5c"
            // → alles vor dem ersten Unterstrich, davon der Teil nach dem letzten Punkt.
            var unterstrich = anwendung.IndexOf('_');
            if (unterstrich > 0)
            {
                var ohneHash = anwendung[..unterstrich];

                var punkt = ohneHash.LastIndexOf('.');
                return punkt > 0 ? ohneHash[(punkt + 1)..] : ohneHash;
            }

            return anwendung;
        }
    }
}
