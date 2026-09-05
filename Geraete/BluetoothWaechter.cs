using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace TestImage.Geraete
{
    /// <summary>Was gerade über Bluetooth am Rechner hängt.</summary>
    public sealed class BluetoothStand
    {
        /// <summary>Ein Bluetooth-Adapter ist vorhanden und eingeschaltet.</summary>
        public bool AdapterVorhanden { get; init; }

        /// <summary>Namen der angemeldeten Geräte — Kopfhörer, Maus, Telefon.</summary>
        public IReadOnlyList<string> Geraete { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Davon die Eingabegeräte (Tastatur, Maus, sonstige HID). Eigene Liste, weil nur
        /// sie tippen und klicken können — ein Kopfhörer kann das nicht.
        /// </summary>
        public IReadOnlyList<string> Eingabegeraete { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Eingabegeräte, die beim Start der Anwendung noch nicht da waren. Das ist der
        /// Fall, der zählt: Ein Gerät, das sich im laufenden Betrieb als Tastatur meldet,
        /// kann tippen, ohne dass jemand am Rechner sitzt.
        /// </summary>
        public IReadOnlyList<string> NeueEingabegeraete { get; init; } = Array.Empty<string>();

        public bool HatGeraete => Geraete.Count > 0;

        public bool HatWarnung => NeueEingabegeraete.Count > 0;
    }

    /// <summary>
    /// Liest den Bluetooth-Zustand über die Geräteverwaltung von Windows (SetupAPI) —
    /// dieselbe Quelle, aus der der Geräte-Manager seine Liste nimmt. Rein lesend, ohne
    /// erhöhte Rechte.
    ///
    /// <b>Warum nicht wie bei Kamera und Mikrofon?</b> Der ConsentStore, aus dem
    /// <see cref="GeraeteWaechter"/> liest, führt für Bluetooth keine Nutzungszeiten: Die
    /// Schlüssel <c>bluetooth</c> und <c>bluetoothSync</c> enthalten nur Berechtigungen von
    /// Store-Anwendungen, kein <c>LastUsedTimeStop</c>. Ein „wird gerade benutzt" gibt es
    /// dort also nicht. Was sich sagen lässt, ist: welche Geräte angemeldet sind.
    ///
    /// <b>Was nicht geht.</b> Die gekoppelten Geräte samt Schlüsseln stehen unter
    /// <c>HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices</c> und gehören
    /// SYSTEM — ohne erhöhte Rechte nicht lesbar. Deshalb wird hier nur gezählt, was
    /// tatsächlich verbunden ist.
    /// </summary>
    public static class BluetoothWaechter
    {
        // Geräteklassen aus der Windows-Geräteverwaltung.
        private static Guid _klasseBluetooth = new("e0cbf06c-cd8b-4647-bb8a-263b43f0f974");
        private static Guid _klasseHid = new("745a17a0-74d3-11d0-b6fe-00a0c90f57da");
        private static Guid _klasseTastatur = new("4d36e96b-e325-11ce-bfc1-08002be10318");
        private static Guid _klasseMaus = new("4d36e96f-e325-11ce-bfc1-08002be10318");

        /// <summary>
        /// Eingabegeräte, die schon beim ersten Blick verbunden waren. Sie gelten als
        /// bekannt — sonst schlüge die Anzeige bei jedem Start wegen der eigenen
        /// Bluetooth-Maus an, und die Warnung wäre nach zwei Tagen nur noch Hintergrund.
        /// </summary>
        private static HashSet<string>? _beimStartVerbunden;

        /// <summary>
        /// Ein Blick auf den aktuellen Stand. Kostet ein paar Millisekunden und ist für
        /// den Zwei-Sekunden-Takt der Indikatorleiste gedacht.
        /// </summary>
        public static BluetoothStand HoleStand()
        {
            try
            {
                var bluetooth = HoleGeraete(ref _klasseBluetooth);

                // Der Adapter selbst hängt an USB oder PCI, die angemeldeten Geräte
                // dagegen am Bluetooth-Enumerator. Ohne diese Trennung zählte der eigene
                // Adapter als „verbundenes Gerät", und das Feld stünde immer auf grün.
                bool adapter = bluetooth.Any(g => !IstUeberBluetoothAngebunden(g.Id));

                // Ein Gerät, mehrere Knoten: Ein Kopfhörer meldet sich als Freisprech-,
                // Stereo- und Fernbedienungsdienst, jeder mit eigenem Namen. Ungefiltert
                // stand im Tooltip dreimal derselbe Kopfhörer. Zusammengefasst wird über
                // die Geräteadresse, die in jeder Instanzkennung steckt.
                var geraete = bluetooth
                    .Where(g => IstUeberBluetoothAngebunden(g.Id))
                    .GroupBy(g => GeraeteAdresse(g.Id) ?? g.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(gruppe => gruppe.OrderBy(g => g.Name.Length).First().Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Eingabegeräte melden sich in eigenen Klassen, nicht unter Bluetooth.
                var eingabe = new List<(string Id, string Name)>();
                foreach (var klasse in new[] { _klasseHid, _klasseTastatur, _klasseMaus })
                {
                    var g = klasse;
                    eingabe.AddRange(HoleGeraete(ref g).Where(x => IstUeberBluetoothAngebunden(x.Id)));
                }

                // Dieselbe Tastatur erscheint als HID- und als Tastatur-Knoten.
                var eingabeIds = new HashSet<string>(eingabe.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
                var eingabeNamen = NamenJeGeraet(eingabe);

                if (_beimStartVerbunden is null)
                {
                    _beimStartVerbunden = eingabeIds;
                    return new BluetoothStand
                    {
                        AdapterVorhanden = adapter,
                        Geraete = geraete,
                        Eingabegeraete = eingabeNamen
                    };
                }

                var neue = NamenJeGeraet(eingabe.Where(e => !_beimStartVerbunden.Contains(e.Id)));

                return new BluetoothStand
                {
                    AdapterVorhanden = adapter,
                    Geraete = geraete,
                    Eingabegeraete = eingabeNamen,
                    NeueEingabegeraete = neue
                };
            }
            catch
            {
                // Die Leiste ist Beiwerk. Fällt die Abfrage aus, bleibt das Feld grau,
                // statt die Anwendung mit einem Interop-Fehler anzuhalten.
                return new BluetoothStand();
            }
        }

        /// <summary>
        /// Ein Name je Gerät statt je Geräteknoten — dieselbe Zusammenfassung wie oben,
        /// nur für die Eingabegeräte: Eine Bluetooth-Tastatur erscheint als HID- und als
        /// Tastatur-Knoten und stünde sonst doppelt im Tooltip.
        /// </summary>
        private static List<string> NamenJeGeraet(IEnumerable<(string Id, string Name)> geraete) =>
            geraete
                .GroupBy(g => GeraeteAdresse(g.Id) ?? g.Name, StringComparer.OrdinalIgnoreCase)
                .Select(gruppe => gruppe.OrderBy(g => g.Name.Length).First().Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// Die Bluetooth-Adresse aus der Instanzkennung — zwölf Hexstellen hinter dem
        /// letzten kaufmännischen Und, etwa <c>…&amp;0&amp;00219188B0F8_C00000000</c>. Sie ist
        /// dasselbe Gerät über alle seine Dienste hinweg. <c>null</c>, wenn die Kennung
        /// anders aufgebaut ist; dann bleibt der Name das Ordnungsmerkmal.
        /// </summary>
        private static string? GeraeteAdresse(string instanzId)
        {
            var treffer = System.Text.RegularExpressions.Regex.Match(
                instanzId, @"&([0-9A-Fa-f]{12})(?:_|$|\\)");

            return treffer.Success ? treffer.Groups[1].Value : null;
        }

        /// <summary>
        /// Hängt das Gerät am Bluetooth-Funk? Klassisches Bluetooth meldet sich als
        /// <c>BTHENUM</c>, Bluetooth LE als <c>BTHLEDEVICE</c>; ein HID-Knoten darüber
        /// trägt die Dienstkennung 0x1812 (Human Interface Device) im Namen.
        /// </summary>
        private static bool IstUeberBluetoothAngebunden(string instanzId) =>
            instanzId.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase)
            || instanzId.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase)
            || instanzId.Contains("00001812", StringComparison.OrdinalIgnoreCase)
            || instanzId.Contains("00001124", StringComparison.OrdinalIgnoreCase);

        /// <summary>Alle vorhandenen Geräte einer Klasse, mit Instanzkennung und Anzeigenamen.</summary>
        private static List<(string Id, string Name)> HoleGeraete(ref Guid klasse)
        {
            var ergebnis = new List<(string, string)>();

            IntPtr satz = SetupDiGetClassDevsW(ref klasse, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);
            if (satz == IntPtr.Zero || satz == new IntPtr(-1))
                return ergebnis;

            try
            {
                var eintrag = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };

                for (int i = 0; SetupDiEnumDeviceInfo(satz, i, ref eintrag); i++)
                {
                    string id = HoleInstanzId(satz, ref eintrag);
                    if (id.Length == 0)
                        continue;

                    // Anzeigename bevorzugt, sonst die Gerätebeschreibung: Nicht jedes
                    // Gerät trägt einen Anzeigenamen, und „unbekanntes Gerät" im Tooltip
                    // hilft niemandem.
                    string name = HoleText(satz, ref eintrag, SPDRP_FRIENDLYNAME);
                    if (name.Length == 0)
                        name = HoleText(satz, ref eintrag, SPDRP_DEVICEDESC);
                    if (name.Length == 0)
                        name = id;

                    ergebnis.Add((id, name));
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(satz);
            }

            return ergebnis;
        }

        private static string HoleInstanzId(IntPtr satz, ref SP_DEVINFO_DATA eintrag)
        {
            var puffer = new char[512];

            return SetupDiGetDeviceInstanceIdW(satz, ref eintrag, puffer, puffer.Length, out int laenge)
                   && laenge > 1
                ? new string(puffer, 0, laenge - 1)
                : string.Empty;
        }

        private static string HoleText(IntPtr satz, ref SP_DEVINFO_DATA eintrag, uint eigenschaft)
        {
            var puffer = new byte[512];

            if (!SetupDiGetDeviceRegistryPropertyW(
                    satz, ref eintrag, eigenschaft, out _, puffer, (uint)puffer.Length, out uint laenge)
                || laenge < 2)
            {
                return string.Empty;
            }

            // Der Puffer enthält eine nullterminierte Zeichenkette in UTF-16.
            return System.Text.Encoding.Unicode
                .GetString(puffer, 0, Math.Min((int)laenge, puffer.Length))
                .TrimEnd('\0');
        }

        #region Windows-Geräteverwaltung (SetupAPI)

        private const int DIGCF_PRESENT = 0x02;
        private const uint SPDRP_DEVICEDESC = 0x00;
        private const uint SPDRP_FRIENDLYNAME = 0x0C;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(
            ref Guid klasse, IntPtr enumerator, IntPtr fenster, int merkmale);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr satz, int index, ref SP_DEVINFO_DATA eintrag);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceInstanceIdW(
            IntPtr satz, ref SP_DEVINFO_DATA eintrag, char[] puffer, int puffergroesse, out int laenge);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceRegistryPropertyW(
            IntPtr satz, ref SP_DEVINFO_DATA eintrag, uint eigenschaft,
            out uint datentyp, byte[] puffer, uint puffergroesse, out uint laenge);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr satz);

        #endregion
    }
}
