using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TestImage
{
    internal class NaturalStringComparer : IComparer<string>, System.Collections.IComparer
    {
        private static readonly Regex _regex = new Regex(@"\d+", RegexOptions.Compiled);

        public int Compare(string? x, string? y)
        {
            if (x == null) return y == null ? 0 : -1;
            if (y == null) return 1;

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

        // Nicht-generische Implementierung, damit ListCollectionView.CustomSort die Instanz akzeptiert
        int System.Collections.IComparer.Compare(object x, object y)
        {
            // null-Behandlung
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var teil1 =System.IO.Path.GetFileNameWithoutExtension( (x as MeinBildchen)?.BName);
            var teil2 = System.IO.Path.GetFileNameWithoutExtension((y as MeinBildchen)?.BName);

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