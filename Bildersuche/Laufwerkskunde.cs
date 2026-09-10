using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Auskunft über das Speichergerät hinter einem Pfad: dreht dort eine Scheibe, ist es
    /// eine Netzfreigabe, und liegen zwei Pfade womöglich auf derselben physischen Platte?
    ///
    /// Gebraucht wird das für die Frage, wie viele Dateien gleichzeitig gelesen werden
    /// dürfen. Die Antwort hängt am Gerät und nicht an der Zahl der Prozessorkerne:
    /// Eine drehende Platte hat genau einen Lesekopf. Jeder zusätzliche Leser bringt ihn
    /// dazu, zwischen den Strömen hin und her zu springen — die Suchzeiten addieren sich,
    /// während die Übertragungsrate dieselbe bleibt. Eine SSD dagegen bedient mehrere
    /// Aufträge echt gleichzeitig und wird erst mit Tiefe schnell.
    /// </summary>
    internal static class Laufwerkskunde
    {
        /// <summary>Was über ein Speichergerät bekannt ist.</summary>
        /// <param name="Kennung">
        /// Gleiche Kennung = gleiches physisches Gerät. Zwei Partitionen einer Platte
        /// (C: und D:) bekommen dieselbe — sonst hielte man sie für zwei Geräte und
        /// liesse sie gleichzeitig lesen, obwohl ein einziger Kopf beides bedient.
        /// </param>
        /// <param name="IstNetz">Netzfreigabe: begrenzt nicht der Kopf, sondern die Leitung.</param>
        /// <param name="DrehendePlatte">
        /// Das Gerät hat Suchkosten (<c>IncursSeekPenalty</c>) — der Windows-Begriff für
        /// „hier bewegt sich etwas mechanisch".
        /// </param>
        /// <param name="Bekannt">
        /// False, wenn die Abfrage nichts hergab (etwa hinter manchen USB-Brücken oder
        /// virtuellen Laufwerken). Dann bleibt es beim bisherigen Verhalten, statt auf
        /// gut Glück zu bremsen.
        /// </param>
        internal sealed record Speichergeraet(
            string Kennung, bool IstNetz, bool DrehendePlatte, bool Bekannt);

        private static readonly Speichergeraet Unbekannt =
            new("?", IstNetz: false, DrehendePlatte: false, Bekannt: false);

        /// <summary>
        /// Einmal ermittelt, für den Lauf gemerkt. Die Abfrage öffnet ein Gerätehandle;
        /// das je Datei zu tun wäre teurer als das, was es einspart.
        /// </summary>
        private static readonly ConcurrentDictionary<string, Speichergeraet> Bekannte =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gerät hinter einem Pfad. Nie null; im Zweifel <see cref="Unbekannt"/>.</summary>
        internal static Speichergeraet BestimmeGeraet(string? pfad)
        {
            if (string.IsNullOrWhiteSpace(pfad))
                return Unbekannt;

            string? wurzel;
            try { wurzel = Path.GetPathRoot(Path.GetFullPath(pfad)); }
            catch { return Unbekannt; }

            if (string.IsNullOrEmpty(wurzel))
                return Unbekannt;

            return Bekannte.GetOrAdd(wurzel, Ermittle);
        }

        private static Speichergeraet Ermittle(string wurzel)
        {
            // Netzfreigabe: Der Lesekopf der Gegenstelle ist nicht zu erreichen und wäre
            // auch die falsche Grösse — hier zählt die Leitung.
            if (wurzel.StartsWith(@"\\", StringComparison.Ordinal))
                return new Speichergeraet("netz:" + wurzel, true, false, true);

            try
            {
                if (new DriveInfo(wurzel).DriveType == DriveType.Network)
                    return new Speichergeraet("netz:" + wurzel, true, false, true);
            }
            catch
            {
                // Laufwerksart nicht feststellbar – die Gerätefrage trotzdem stellen.
            }

            // "C:\" -> "\\.\C:" — der Pfad, unter dem der Datenträger selbst ansprechbar
            // ist. Mit Zugriffsrecht 0 geöffnet: Das genügt für Abfragen und verlangt
            // keine erhöhten Rechte, anders als ein Öffnen zum Lesen.
            string geraetePfad = @"\\.\" + wurzel.TrimEnd('\\', '/');

            using var handle = CreateFileW(
                geraetePfad, 0,
                FileShare.ReadWrite, IntPtr.Zero,
                FileMode.Open, 0, IntPtr.Zero);

            if (handle.IsInvalid)
                return Unbekannt;

            string kennung = LiesGeraeteNummer(handle) ?? "vol:" + wurzel;
            bool? sucht = LiesSuchkosten(handle);

            return sucht is null
                ? new Speichergeraet(kennung, false, false, false)
                : new Speichergeraet(kennung, false, sucht.Value, true);
        }

        /// <summary>
        /// Nummer der physischen Platte hinter dem Datenträger. Darüber fallen mehrere
        /// Partitionen desselben Geräts zusammen. Null, wenn die Abfrage nichts hergibt.
        /// </summary>
        private static string? LiesGeraeteNummer(SafeFileHandle handle)
        {
            int groesse = Marshal.SizeOf<STORAGE_DEVICE_NUMBER>();
            IntPtr puffer = Marshal.AllocHGlobal(groesse);

            try
            {
                if (!DeviceIoControl(
                        handle, IOCTL_STORAGE_GET_DEVICE_NUMBER,
                        IntPtr.Zero, 0, puffer, (uint)groesse, out _, IntPtr.Zero))
                {
                    return null;
                }

                var nummer = Marshal.PtrToStructure<STORAGE_DEVICE_NUMBER>(puffer);
                return $"disk:{nummer.DeviceType}:{nummer.DeviceNumber}";
            }
            catch
            {
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(puffer);
            }
        }

        /// <summary>
        /// True = mechanische Suchzeiten (Festplatte), false = keine (SSD/NVMe),
        /// null = das Gerät sagt es nicht.
        /// </summary>
        private static bool? LiesSuchkosten(SafeFileHandle handle)
        {
            var frage = new STORAGE_PROPERTY_QUERY
            {
                PropertyId = StorageDeviceSeekPenaltyProperty,
                QueryType = PropertyStandardQuery
            };

            int frageGroesse = Marshal.SizeOf<STORAGE_PROPERTY_QUERY>();
            int antwortGroesse = Marshal.SizeOf<DEVICE_SEEK_PENALTY_DESCRIPTOR>();

            IntPtr fragePuffer = Marshal.AllocHGlobal(frageGroesse);
            IntPtr antwortPuffer = Marshal.AllocHGlobal(antwortGroesse);

            try
            {
                Marshal.StructureToPtr(frage, fragePuffer, false);

                if (!DeviceIoControl(
                        handle, IOCTL_STORAGE_QUERY_PROPERTY,
                        fragePuffer, (uint)frageGroesse,
                        antwortPuffer, (uint)antwortGroesse, out _, IntPtr.Zero))
                {
                    return null;
                }

                var antwort = Marshal.PtrToStructure<DEVICE_SEEK_PENALTY_DESCRIPTOR>(antwortPuffer);
                return antwort.IncursSeekPenalty;
            }
            catch
            {
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(fragePuffer);
                Marshal.FreeHGlobal(antwortPuffer);
            }
        }

        #region Win32

        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        private const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;
        private const int StorageDeviceSeekPenaltyProperty = 7;
        private const int PropertyStandardQuery = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_PROPERTY_QUERY
        {
            public int PropertyId;
            public int QueryType;
            public byte AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
        {
            public uint Version;
            public uint Size;

            [MarshalAs(UnmanagedType.U1)]
            public bool IncursSeekPenalty;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_DEVICE_NUMBER
        {
            public int DeviceType;
            public int DeviceNumber;
            public int PartitionNumber;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string lpFileName, uint dwDesiredAccess, FileShare dwShareMode,
            IntPtr lpSecurityAttributes, FileMode dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice, uint dwIoControlCode,
            IntPtr lpInBuffer, uint nInBufferSize,
            IntPtr lpOutBuffer, uint nOutBufferSize,
            out uint lpBytesReturned, IntPtr lpOverlapped);

        #endregion
    }
}
