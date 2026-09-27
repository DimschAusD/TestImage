using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Devices.Radios;

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

        /// <summary>
        /// Der eigene Funkchip: Name, Bluetooth-Version, Treiberstand. <c>null</c>, wenn kein
        /// Adapter da ist.
        /// </summary>
        public BluetoothAdapter? Adapter { get; init; }

        /// <summary>
        /// Der Adapter ist da, aber Bluetooth ist ausgeschaltet (Schnelleinstellungen oder
        /// Flugmodus). Nur <c>true</c>, wenn Windows das sicher meldet; unbekannt gilt als an.
        /// </summary>
        public bool IstAusgeschaltet { get; init; }

        public bool HatGeraete => Geraete.Count > 0;

        public bool HatWarnung => NeueEingabegeraete.Count > 0;
    }

    /// <summary>
    /// Angaben zum eigenen Bluetooth-Adapter.
    ///
    /// Das Treiberdatum ist die eigentlich wichtige Angabe: Lücken im Funkchip selbst
    /// (KNOB, BIAS, BLUFFS, BrakTooth …) schliesst nicht Windows Update, sondern die
    /// Firmware, und die kommt beim Laden des Herstellertreibers mit. Ein Chip, für den
    /// der Hersteller keine Treiber mehr baut, bleibt auf seinem letzten Stand stehen.
    /// </summary>
    /// <param name="Version">Bluetooth-Version aus der LMP-Kennung, etwa „4.2"; <c>null</c>, wenn unbekannt.</param>
    public sealed record BluetoothAdapter(
        string InstanzId, string Name, string? Version, DateTime? TreiberDatum, string? TreiberVersion);

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

                // Die Angaben zum Chip ändern sich nur mit dem Adapter, deshalb einmal je
                // Adapter gelesen statt in jedem Takt.
                var adapterAngaben = adapter
                    ? HoleAdapterAngaben(bluetooth.First(g => !IstUeberBluetoothAngebunden(g.Id)).Id)
                    : null;

                // Der Adapterknoten bleibt auch bei ausgeschaltetem Bluetooth vorhanden;
                // ob der Funk an ist, weiss nur der Schalter von Windows.
                bool ausgeschaltet = adapter && IstFunkAus();

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
                        Adapter = adapterAngaben,
                        IstAusgeschaltet = ausgeschaltet,
                        Geraete = geraete,
                        Eingabegeraete = eingabeNamen
                    };
                }

                var neue = NamenJeGeraet(eingabe.Where(e => !_beimStartVerbunden.Contains(e.Id)));

                return new BluetoothStand
                {
                    AdapterVorhanden = adapter,
                    Adapter = adapterAngaben,
                    IstAusgeschaltet = ausgeschaltet,
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

        /// <summary>Der Bluetooth-Funk aus Sicht der Windows-Schnellschalter; einmal gesucht.</summary>
        private static Radio? _funk;

        private static Task? _funkSuche;

        /// <summary>
        /// Ist Bluetooth über den Windows-Schalter oder den Flugmodus ausgeschaltet?
        ///
        /// Die Suche nach dem Funk ist nur asynchron zu haben, der Takt der Leiste aber
        /// synchron. Deshalb läuft sie beim ersten Aufruf einmal los, und bis sie fertig
        /// ist, gilt der Funk als an — das ist der Stand vor dieser Abfrage. Danach ist
        /// der Zustand eine blosse Eigenschaft und kostet nichts.
        /// </summary>
        private static bool IstFunkAus()
        {
            _funkSuche ??= SucheFunkAsync();

            try
            {
                return _funk?.State is RadioState.Off or RadioState.Disabled;
            }
            catch
            {
                // Adapter abgezogen: Das alte Funkobjekt gilt nicht mehr, neu suchen.
                _funk = null;
                _funkSuche = null;
                return false;
            }
        }

        private static async Task SucheFunkAsync()
        {
            try
            {
                var funke = await Radio.GetRadiosAsync();
                _funk = funke.FirstOrDefault(f => f.Kind == RadioKind.Bluetooth);
            }
            catch
            {
                // Ohne Funkabfrage bleibt es beim bisherigen Verhalten: gilt als an.
            }
        }

        /// <summary>Zuletzt gelesene Adapterangaben; gilt, solange derselbe Adapter steckt.</summary>
        private static BluetoothAdapter? _adapterAngaben;

        /// <summary>
        /// Name, Bluetooth-Version und Treiberstand des Adapters mit dieser Instanzkennung.
        /// Die Version steht als LMP-Kennung in den Funkeigenschaften, die Windows am
        /// Adapterknoten führt — dieselbe Zahl, die der Geräte-Manager unter „Erweitert"
        /// als Firmware-Stand zeigt.
        /// </summary>
        private static BluetoothAdapter? HoleAdapterAngaben(string instanzId)
        {
            if (string.Equals(_adapterAngaben?.InstanzId, instanzId, StringComparison.OrdinalIgnoreCase))
                return _adapterAngaben;

            IntPtr satz = SetupDiGetClassDevsW(ref _klasseBluetooth, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);
            if (satz == IntPtr.Zero || satz == new IntPtr(-1))
                return null;

            try
            {
                var eintrag = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };

                for (int i = 0; SetupDiEnumDeviceInfo(satz, i, ref eintrag); i++)
                {
                    if (!string.Equals(HoleInstanzId(satz, ref eintrag), instanzId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string name = HoleText(satz, ref eintrag, SPDRP_FRIENDLYNAME);
                    if (name.Length == 0)
                        name = HoleText(satz, ref eintrag, SPDRP_DEVICEDESC);

                    var lmp = HoleEigenschaft(satz, ref eintrag, _schluesselLmpVersion, out uint lmpTyp);
                    var datum = HoleEigenschaft(satz, ref eintrag, _schluesselTreiberDatum, out uint datumTyp);
                    var version = HoleEigenschaft(satz, ref eintrag, _schluesselTreiberVersion, out uint versionTyp);

                    uint? lmpWert = lmp is null ? null : lmpTyp switch
                    {
                        DEVPROP_TYPE_BYTE when lmp.Length >= 1 => lmp[0],
                        DEVPROP_TYPE_UINT16 when lmp.Length >= 2 => BitConverter.ToUInt16(lmp),
                        DEVPROP_TYPE_UINT32 when lmp.Length >= 4 => BitConverter.ToUInt32(lmp),
                        _ => null
                    };

                    // Das Treiberdatum liegt als FILETIME auf Mitternacht UTC; in
                    // Ortszeit gelesen, rutschte es westlich von Greenwich auf den Vortag.
                    DateTime? treiberDatum = datum is { Length: >= 8 } && datumTyp == DEVPROP_TYPE_FILETIME
                        ? DateTime.FromFileTimeUtc(BitConverter.ToInt64(datum)).Date
                        : null;

                    string? treiberVersion = version is not null && versionTyp == DEVPROP_TYPE_STRING
                        ? System.Text.Encoding.Unicode.GetString(version).TrimEnd('\0')
                        : null;

                    _adapterAngaben = new BluetoothAdapter(
                        instanzId, name, BluetoothVersion(lmpWert), treiberDatum, treiberVersion);

                    return _adapterAngaben;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(satz);
            }

            return null;
        }

        /// <summary>
        /// Bluetooth-Version zur LMP-Kennung aus der Kernspezifikation (Assigned Numbers).
        /// </summary>
        private static string? BluetoothVersion(uint? lmp) => lmp switch
        {
            0 => "1.0b", 1 => "1.1", 2 => "1.2", 3 => "2.0", 4 => "2.1", 5 => "3.0",
            6 => "4.0", 7 => "4.1", 8 => "4.2", 9 => "5.0", 10 => "5.1", 11 => "5.2",
            12 => "5.3", 13 => "5.4", 14 => "6.0",
            _ => null
        };

        /// <summary>Rohwert einer Geräteeigenschaft samt Typkennung; <c>null</c>, wenn sie fehlt.</summary>
        private static byte[]? HoleEigenschaft(
            IntPtr satz, ref SP_DEVINFO_DATA eintrag, DEVPROPKEY schluessel, out uint typ)
        {
            var puffer = new byte[256];

            if (!SetupDiGetDevicePropertyW(
                    satz, ref eintrag, ref schluessel, out typ, puffer, (uint)puffer.Length, out uint laenge, 0)
                || laenge == 0)
            {
                return null;
            }

            return puffer[..(int)Math.Min(laenge, (uint)puffer.Length)];
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

        private const uint DEVPROP_TYPE_BYTE = 0x03;
        private const uint DEVPROP_TYPE_UINT16 = 0x05;
        private const uint DEVPROP_TYPE_UINT32 = 0x07;
        private const uint DEVPROP_TYPE_FILETIME = 0x10;
        private const uint DEVPROP_TYPE_STRING = 0x12;

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        // DEVPKEY_Device_DriverDate und DEVPKEY_Device_DriverVersion (devpkey.h).
        private static readonly DEVPROPKEY _schluesselTreiberDatum =
            new() { fmtid = new("a8b865dd-2e3d-4094-ad97-e593a70c75d6"), pid = 2 };
        private static readonly DEVPROPKEY _schluesselTreiberVersion =
            new() { fmtid = new("a8b865dd-2e3d-4094-ad97-e593a70c75d6"), pid = 3 };

        // DEVPKEY_BluetoothRadio_LMPVersion (bthdef.h): die Funkeigenschaften am Adapterknoten.
        private static readonly DEVPROPKEY _schluesselLmpVersion =
            new() { fmtid = new("a92f26ca-eda7-4b1d-9db2-27b68aa5a2eb"), pid = 4 };

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

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDevicePropertyW(
            IntPtr satz, ref SP_DEVINFO_DATA eintrag, ref DEVPROPKEY schluessel,
            out uint typ, byte[] puffer, uint puffergroesse, out uint laenge, uint merkmale);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr satz);

        #endregion
    }
}
