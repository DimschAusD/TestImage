using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestImage
{
    /// <summary>
    /// Lädt die 120-Pixel-Miniaturen der Bildleiste im Hintergrund nach.
    ///
    /// <b>Das Problem:</b> Die Leiste band ihre Bilder über
    /// <c>CLconverterStringZuKleinemImage</c>. Ein Konverter läuft im UI-Faden, und zwar
    /// genau dann, wenn eine Kachel sichtbar wird. Ein Zug an der Bildlaufleiste erzeugt
    /// mit <c>VirtualizationMode="Recycling"</c> Dutzende Kacheln je Bild — jede dekodierte
    /// mitten im UI-Faden eine Datei. Bei kleinen JPEGs merkt man das kaum, bei einem
    /// grossen PNG steht die Oberfläche: <c>DecodePixelWidth</c> begrenzt nur das Ergebnis,
    /// auspacken muss der Decoder das ganze Bild.
    ///
    /// <b>Der Weg:</b> Derselbe Aufbau wie bei der Kachel-Liste, die schon immer flüssig
    /// war — die Kachel bindet auf eine Eigenschaft (<c>MeinBildchen.Miniatur</c>), und die
    /// wird von hier aus gefüllt. Die Kachel ist kurz leer statt dass alles ruckelt.
    ///
    /// <b>Träge, nicht im Voraus:</b> Die Kachel-Liste lädt ihren ganzen Satz vorweg. Für
    /// die Leiste wäre das falsch — 120er-Miniaturen sind rund 57 KB, bei 5000 Bildern im
    /// Ordner also fast 300 MB, von denen man meist nur die ersten Dutzend ansieht.
    /// Angefordert wird deshalb erst, wenn eine Kachel eine Datei zugewiesen bekommt.
    /// </summary>
    internal static class MiniaturLader
    {
        /// <summary>Breite der Miniaturen. Muss zum gemeinsamen Cache passen.</summary>
        private const int Breite = 120;

        /// <summary>
        /// Zwei Fäden. Einer lässt die Warteschlange bei grossen Dateien zu langsam
        /// abfliessen, viele bringen nichts: Die Platte liest ohnehin nacheinander, und
        /// jeder Faden hält währenddessen ein ganzes ausgepacktes Bild im Speicher.
        /// </summary>
        private const int MaxFaeden = 2;

        private static readonly object _tor = new();

        /// <summary>
        /// Offene Aufträge. Bewusst als Stapel benutzt — der jüngste zuerst.
        ///
        /// Beim Ziehen der Bildlaufleiste laufen Dutzende Anforderungen ein. Zuerst
        /// bedient werden soll, was der Nutzer <i>jetzt</i> sieht, nicht was er vor drei
        /// Sekunden überflogen hat.
        /// </summary>
        private static readonly List<MeinBildchen> _warteschlange = new();

        private static int _faeden;

        /// <summary>
        /// Fordert die Miniatur für ein Bild an.
        ///
        /// Liegt sie im gemeinsamen Cache, wird sie sofort gesetzt — beim zweiten Blick auf
        /// denselben Ordner gibt es also kein Flackern und keinen Hintergrundlauf.
        /// </summary>
        internal static void Anfordern(MeinBildchen? bild)
        {
            if (bild is null || string.IsNullOrEmpty(bild.BName))
            {
                return;
            }

            if (CLconverterStringZuKleinemImage.TryHoleAusCache(bild.BName, out var vorhanden))
            {
                bild.Miniatur = vorhanden;
                return;
            }

            if (bild.Miniatur is not null)
            {
                return;
            }

            lock (_tor)
            {
                _warteschlange.Remove(bild);
                _warteschlange.Add(bild);

                if (_faeden >= MaxFaeden)
                {
                    return;
                }

                _faeden++;
            }

            ThreadPool.QueueUserWorkItem(_ => Arbeite());
        }

        /// <summary>
        /// Fordert die Miniaturen aller <b>erzeugten</b> Kacheln einer Liste an.
        ///
        /// <b>Warum es das braucht:</b> <c>DataContextChanged</c> allein genügt nicht. Es
        /// feuert einmal je Zuweisung — verpasst es eine (weil der Behälter schon stand,
        /// bevor der Haken hing, oder weil ein Auftrag abgemeldet und nie neu gestellt
        /// wurde), bleibt die Kachel leer, bis man scrollt. Genau das war zu sehen: ohne
        /// Scrollen keine Bilder.
        ///
        /// Der Aufruf ist billig und beliebig oft wiederholbar: Für jede Kachel, die schon
        /// ein Bild hat, kehrt <see cref="Anfordern"/> sofort um.
        ///
        /// <c>ContainerFromIndex</c> liefert bei virtualisierten Listen nur für erzeugte
        /// Zeilen einen Behälter. Damit trifft die Schleife genau die sichtbaren, ohne
        /// Scrollpositionen und Sichtfensterbreiten nachzurechnen.
        /// </summary>
        internal static void FordereSichtbareAn(System.Windows.Controls.ItemsControl? liste)
        {
            if (liste is null)
            {
                return;
            }

            var erzeuger = liste.ItemContainerGenerator;

            for (int i = 0; i < liste.Items.Count; i++)
            {
                if (erzeuger.ContainerFromIndex(i) is null)
                {
                    continue;
                }

                Anfordern(liste.Items[i] as MeinBildchen);
            }
        }

        /// <summary>
        /// Nimmt einen Auftrag zurück, weil die Kachel eine andere Datei bekommen hat.
        ///
        /// Ohne das arbeitet die Warteschlange nach einem langen Zug noch minutenlang an
        /// Bildern, die längst aus dem Sichtfenster gescrollt sind — und blockiert dabei
        /// die, die gerade zu sehen sind.
        /// </summary>
        internal static void Abmelden(MeinBildchen? bild)
        {
            if (bild is null)
            {
                return;
            }

            lock (_tor)
            {
                _warteschlange.Remove(bild);
            }
        }

        private static void Arbeite()
        {
            while (true)
            {
                MeinBildchen? bild;

                lock (_tor)
                {
                    int letzter = _warteschlange.Count - 1;
                    if (letzter < 0)
                    {
                        _faeden--;
                        return;
                    }

                    bild = _warteschlange[letzter];
                    _warteschlange.RemoveAt(letzter);
                }

                string pfad = bild.BName;
                if (string.IsNullOrEmpty(pfad) || bild.Miniatur is not null)
                {
                    continue;
                }

                // Bei einer unlesbaren Datei den gelben Platzhalter zeigen, nicht ein leeres
                // Feld. Der Konverter tat das vorher auch — ohne diesen Zweig wäre eine
                // beschädigte Datei von einer noch nicht geladenen nicht zu unterscheiden.
                var bmp = Dekodiere(pfad) ?? CLconverterStringZuKleinemImage.HolePlatzhalter();

                // Zuweisung über den Dispatcher: Die Bindung hängt an der Oberfläche.
                // Das Bild selbst ist eingefroren und darf den Faden wechseln.
                var oberflaeche = Application.Current?.Dispatcher;
                if (oberflaeche is null)
                {
                    bild.Miniatur = bmp;
                    continue;
                }

                var ziel = bild;
                oberflaeche.BeginInvoke(new Action(() =>
                {
                    // Nur setzen, wenn die Kachel noch dieselbe Datei zeigt. Beim Recycling
                    // kann sie inzwischen eine andere bekommen haben.
                    if (string.Equals(ziel.BName, pfad, StringComparison.OrdinalIgnoreCase))
                    {
                        ziel.Miniatur = bmp;
                    }
                }));
            }
        }

        /// <summary>
        /// Dekodiert eine Miniatur und legt sie in den gemeinsamen Cache. Läuft auf einem
        /// beliebigen Faden; das Ergebnis ist eingefroren und darf an die Oberfläche.
        ///
        /// Dieselbe Rechnung wie im Konverter und in <c>AufgabeViewModel.LadeThumb</c> —
        /// alle drei Wege füllen einen Cache, deshalb steht sie hier nur einmal.
        /// </summary>
        internal static ImageSource? Dekodiere(string pfad)
        {
            if (string.IsNullOrEmpty(pfad))
            {
                return null;
            }

            if (CLconverterStringZuKleinemImage.TryHoleAusCache(pfad, out var vorhanden))
            {
                return vorhanden;
            }

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(pfad);
                bmp.DecodePixelWidth = Breite;
                bmp.EndInit();
                bmp.Freeze();

                CLconverterStringZuKleinemImage.LegeInCache(pfad, bmp);
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }
}
