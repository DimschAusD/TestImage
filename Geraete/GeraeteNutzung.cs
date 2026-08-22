namespace TestImage.Geraete
{
    /// <summary>
    /// Eine Anwendung und ihr Zugriff auf ein Gerät (Kamera, Mikrofon, Bildschirmaufnahme).
    /// </summary>
    /// <param name="Anwendung">
    /// So, wie Windows sie in der Registrierung führt: Pfad zur .exe bei Desktop-Anwendungen,
    /// Paketname bei Store-Anwendungen. Für die Anzeige <see cref="AnzeigeName"/> nehmen.
    /// </param>
    /// <param name="AktivGerade">True, solange der Zugriff läuft.</param>
    /// <param name="Geraet">"webcam", "microphone" oder "screenCapture".</param>
    public record GeraeteNutzung(string Anwendung, bool AktivGerade, string Geraet)
    {
        /// <summary>Lesbarer Kurzname — „Zoom" statt vollem Pfad oder Paketname.</summary>
        public string AnzeigeName => AnwendungsNameHelper.HoleAnzeigeName(Anwendung);
    }
}
