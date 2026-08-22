using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace TestImage.Geraete
{
    /// <summary>
    /// Sagt, ob gerade jemand Kamera, Mikrofon oder Bildschirmaufnahme benutzt. Speist die
    /// drei Anzeigen in IndikatorLeiste.
    ///
    /// Gelesen wird der ConsentStore unter HKEY_CURRENT_USER. Windows trägt dort je
    /// Anwendung ein, wann ein Zugriff endete; steht dieser Zeitpunkt auf 0, läuft er noch.
    /// Kein Abhorchen des Geräts, keine besonderen Rechte — nur zwei Registrierungsschlüssel.
    /// </summary>
    public static class GeraeteWaechter
    {
        public static IReadOnlyList<GeraeteNutzung> HoleNutzungen(string geraet)
        {
            var ergebnis = new List<GeraeteNutzung>();

            // Zwei Zweige: Store-Anwendungen stehen direkt darunter, gewöhnliche
            // Programme in NonPackaged. Ohne den zweiten fehlten alle .exe-Anwendungen.
            string[] wurzeln =
            {
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{geraet}",
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{geraet}\NonPackaged"
            };

            foreach (var wurzel in wurzeln)
            {
                using var wurzelSchluessel = Registry.CurrentUser.OpenSubKey(wurzel);
                if (wurzelSchluessel == null)
                {
                    continue;
                }

                foreach (var anwendung in wurzelSchluessel.GetSubKeyNames())
                {
                    using var anwendungsSchluessel = wurzelSchluessel.OpenSubKey(anwendung);
                    bool aktiv = anwendungsSchluessel?.GetValue("LastUsedTimeStop") is long ende && ende == 0;
                    ergebnis.Add(new GeraeteNutzung(anwendung, aktiv, geraet));
                }
            }

            return ergebnis;
        }

        public static IReadOnlyList<GeraeteNutzung> HoleAlleNutzungen()
        {
            return
            [.. HoleNutzungen("webcam"), .. HoleNutzungen("microphone"), .. HoleNutzungen("screenCapture")];
        }

        public static bool IstAktiv(string geraet) =>
            HoleNutzungen(geraet).Any(n => n.AktivGerade);

        public static bool KameraAktiv => IstAktiv("webcam");
        public static bool MikroAktiv => IstAktiv("microphone");
        public static bool ScreenShareAktiv => IstAktiv("screenCapture");
    }
}
