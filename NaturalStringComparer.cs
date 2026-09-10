using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace TestImage
{
    /// <summary>
    /// Welche Namenssortierung die Ansicht verwendet.
    /// </summary>
    internal enum Sortierweise
    {
        /// <summary>
        /// Wie der SpeedCommander: Der Name wird an den Ziffernblöcken zerlegt, die
        /// Textstücke werden nach Kultur verglichen (Wortsortierung — Bindestrich und
        /// Leerzeichen wiegen leicht), die Zahlenblöcke rein numerisch.
        /// </summary>
        SpeedCommander,

        /// <summary>
        /// Wie der Windows-Explorer: <c>StrCmpLogicalW</c> aus der shlwapi — genau die
        /// Funktion, mit der der Explorer selbst sortiert. Interpunktion und
        /// Sonderzeichen wiegen dort schwerer, führende Nullen anders.
        /// </summary>
        WindowsExplorer
    }

    internal class NaturalStringComparer : IComparer<string>, System.Collections.IComparer
    {
        private static readonly Regex _regex = new Regex(@"\d+", RegexOptions.Compiled);

        /// <summary>
        /// Die Sortierung des Windows-Explorers. Die Reihenfolge stammt nicht aus einer
        /// Nachbildung, sondern aus derselben Systemfunktion, die auch der Explorer ruft —
        /// nur so stimmen die Randfälle (Interpunktion, führende Nullen) wirklich überein.
        /// </summary>
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int StrCmpLogicalW(string x, string y);

        /// <summary>
        /// Weise, die neu erzeugte Vergleicher übernehmen. Wird beim Umschalten in den
        /// Einstellungen gesetzt; jede Instanz merkt sich den Wert, den sie bei ihrer
        /// Erzeugung vorfindet, damit eine laufende Sortierung nicht mitten im Lauf die
        /// Regel wechselt.
        /// </summary>
        public static Sortierweise Standard { get; set; } = Sortierweise.SpeedCommander;

        private readonly Sortierweise _weise;

        public NaturalStringComparer()
            : this(Standard)
        {
        }

        public NaturalStringComparer(Sortierweise weise)
        {
            _weise = weise;
        }

        public int Compare(string? x, string? y)
        {
            if (x == null) return y == null ? 0 : -1;
            if (y == null) return 1;

            if (_weise == Sortierweise.WindowsExplorer)
            {
                return StrCmpLogicalW(x, y);
            }

            var xParts = _regex.Split(x);
            var yParts = _regex.Split(y);
            var xNums = _regex.Matches(x);
            var yNums = _regex.Matches(y);

            int partCount = Math.Max(xParts.Length, yParts.Length);
            for (int i = 0; i < partCount; i++)
            {
                // Textteile vergleichen
                if (i < xParts.Length && i < yParts.Length)
                {
                    int cmp = string.Compare(xParts[i], yParts[i], true, CultureInfo.CurrentCulture);
                    if (cmp != 0) return cmp;
                }
                else if (i < xParts.Length) return 1;
                else if (i < yParts.Length) return -1;

                // Zahlenteile vergleichen
                if (i < xNums.Count && i < yNums.Count)
                {
                    if (!int.TryParse(xNums[i].Value, out int numX)) numX = 0;
                    if (!int.TryParse(yNums[i].Value, out int numY)) numY = 0;
                    if (numX != numY) return numX.CompareTo(numY);
                }
                else if (i < xNums.Count) return 1;
                else if (i < yNums.Count) return -1;
            }

            return 0;
        }

        /// <summary>
        /// Der Teil eines Pfades, nach dem sortiert wird — die eine Stelle, an der der
        /// Sortierschlüssel der Ansicht festgelegt ist.
        ///
        /// SpeedCommander: Name <b>ohne</b> Endung. So war es immer; unverändert, damit
        /// die gewohnte Reihenfolge der Ordner Bild für Bild dieselbe bleibt.
        ///
        /// Explorer: Name <b>mit</b> Endung. Der Explorer sortiert nach dem vollen
        /// Dateinamen; ohne Endung stünden „bild1.jpg" und „bild1.png" in
        /// Einlesereihenfolge zueinander, und der Modus hielte nicht, was er verspricht.
        ///
        /// Wer ausserhalb der Ansicht dieselbe Reihenfolge herstellen muss — etwa
        /// <c>NachfolgerInAnsichtsordnung</c> —, sortiert über diese Methode. Ein eigener
        /// Schlüssel dort läuft sonst auseinander, sobald hier etwas geändert wird.
        /// </summary>
        public string? Schlüssel(string? pfad)
        {
            if (pfad == null)
            {
                return null;
            }

            return _weise == Sortierweise.WindowsExplorer
                ? System.IO.Path.GetFileName(pfad)
                : System.IO.Path.GetFileNameWithoutExtension(pfad);
        }

        // Nicht-generische Implementierung, damit ListCollectionView.CustomSort die Instanz akzeptiert
        int System.Collections.IComparer.Compare(object x, object y)
        {
            // null-Behandlung
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var teil1 = Schlüssel((x as MeinBildchen)?.BName);
            var teil2 = Schlüssel((y as MeinBildchen)?.BName);

            // Wenn beide strings sind, die natürliche Sortierung verwenden
            if (teil1 is string sx && teil2 is string sy)
            {
                return Compare(sx, sy);
            }

            // Falls die Objekte vergleichbar sind, auf deren CompareTo zurückgreifen
            if (x is IComparable cx && y != null)
            {
                try
                {
                    return cx.CompareTo(y);
                }
                catch (ArgumentException)
                {
                    // Fallback weiter unten
                }
            }

            return 0;
            // Kein string und nicht IComparable kompatibel → sinnvolle Ausnahme
     //       throw new ArgumentException("Objects are not comparable by NaturalStringComparer. Expected strings or IComparable objects.");
        }
    }
}
