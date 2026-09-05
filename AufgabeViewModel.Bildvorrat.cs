using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace TestImage
{
    /// <summary>
    /// Bildvorrat: hält die Nachbarn des gezeigten Bildes fertig dekodiert bereit.
    ///
    /// <b>Warum.</b> Ein Blätterschritt kostete drei Zugriffe auf dieselbe Datei — Masse
    /// lesen, Vorschau dekodieren, grosses Bild dekodieren —, und alle drei begannen erst
    /// mit dem Klick. Auf einer schnellen Platte fällt das nicht auf; auf einer langsamen
    /// (USB, Netzlaufwerk, gerade erst angelaufener Schlafmodus) ist genau das die ganze
    /// Wartezeit, und zwar bei jedem Bild neu. Gewartet wird dabei nicht auf Rechenzeit,
    /// sondern auf die Platte — mehr Fäden bringen deshalb nichts.
    ///
    /// <b>Was der Vorrat ändert.</b> Er verschiebt die Arbeit vor den Klick: Sobald ein
    /// Bild steht, holt ein Hintergrundfaden die nächsten in Blätterrichtung. Trifft der
    /// nächste Schritt, kommt er ganz ohne Platte aus — und ohne die Zwischenstufe mit
    /// dem 100-Pixel-Vorschaubild, die sonst kurz aufblitzt.
    ///
    /// <b>Grenzen.</b> Wer schneller blättert, als der Vorauslauf nachkommt, wartet
    /// weiterhin auf die Platte; der Vorrat macht sie nicht schneller, er nutzt nur die
    /// Zeit, in der man ein Bild ansieht. Die Bilderliste ändert sich (verschieben,
    /// löschen, neuer Ordner) → Vorrat weg, siehe <see cref="VorratLeeren"/>.
    /// </summary>
    public partial class AufgabeViewModel
    {
        #region Vorrat

        /// <summary>So viele Bilder in Blätterrichtung werden vorausgeladen.</summary>
        private const int VorratVoraus = 3;

        /// <summary>… und so viele entgegen der Richtung, für den Schritt zurück.</summary>
        private const int VorratZurück = 1;

        /// <summary>
        /// Grenze des Vorrats. Ein bildschirmfüllend dekodiertes Bild belegt rund 8 MB
        /// (1920 × 1080 × 4 Byte), sechs Einträge also etwa 50 MB. Höher zu gehen bringt
        /// wenig: Was weiter weg liegt als ein paar Schritte, wird meist nie angesehen,
        /// und der Vorauslauf käme mit dem Füllen ohnehin nicht hinterher.
        /// </summary>
        private const int VorratMax = 6;

        /// <summary>
        /// Ein fertig dekodiertes Bild samt allem, was der Ladeweg sonst von der Platte
        /// holt: den Originalmassen und beiden Stufen.
        /// </summary>
        private sealed record VorratsBild(
            int OriginalBreite,
            int OriginalHöhe,
            int DekodierBreite,
            int DekodierHöhe,
            BitmapSource Klein,
            BitmapSource Gross);

        private readonly object _vorratTor = new();

        private readonly Dictionary<string, VorratsBild> _vorrat =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Ältester zuerst — daran hängt das Verwerfen, wenn der Vorrat voll ist.</summary>
        private readonly List<string> _vorratAlter = new();

        private CancellationTokenSource? _vorratLauf;

        /// <summary>
        /// Zuletzt gegangene Richtung: +1 nach rechts, -1 nach links. Der Vorauslauf
        /// arbeitet vorwiegend in diese Richtung — vorwärts blättern ist der Normalfall,
        /// und beide Richtungen gleich weit zu laden hiesse, die halbe Plattenzeit für
        /// den selteneren Fall auszugeben.
        /// </summary>
        private int _blätterRichtung = 1;

        /// <summary>
        /// Dekodiergrösse für die Anzeige: das Bild auf Bildschirmgrösse einpassen, nie
        /// vergrössern. Steht hier, weil der Vorauslauf dieselbe Zahl treffen muss wie
        /// der Ladebefehl — sonst läge im Vorrat ein Bild, das nicht passt, und der
        /// Vorauslauf hätte umsonst gelesen.
        /// </summary>
        private static (int breite, int höhe) DekodierGrösseRechnen(
            int originalBreite, int originalHöhe, int monitorBreite, int monitorHöhe)
        {
            if (originalBreite <= 0 || originalHöhe <= 0)
            {
                return (monitorBreite, monitorHöhe);
            }

            double faktor = Math.Min(
                (double)monitorBreite / originalBreite,
                (double)monitorHöhe / originalHöhe);

            // Nie hochskalieren.
            faktor = Math.Min(faktor, 1.0);

            return ((int)Math.Round(originalBreite * faktor),
                    (int)Math.Round(originalHöhe * faktor));
        }

        private VorratsBild? VorratNachschlagen(string? pfad)
        {
            if (string.IsNullOrEmpty(pfad))
            {
                return null;
            }

            lock (_vorratTor)
            {
                return _vorrat.TryGetValue(pfad, out var eintrag) ? eintrag : null;
            }
        }

        private void VorratEinlegen(string pfad, VorratsBild bild)
        {
            lock (_vorratTor)
            {
                if (!_vorrat.ContainsKey(pfad))
                {
                    _vorratAlter.Add(pfad);
                }

                _vorrat[pfad] = bild;

                // Ältestes zuerst hinaus. Beim Vorwärtsblättern ist das der Eintrag, der
                // am weitesten hinter dem gezeigten Bild liegt.
                while (_vorratAlter.Count > VorratMax)
                {
                    _vorrat.Remove(_vorratAlter[0]);
                    _vorratAlter.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// Alles vergessen. Nötig, sobald sich die Bilderliste ändert: Bilder werden in
        /// andere Ordner verschoben, gelöscht oder es kommt ein ganz neuer Ordner. Der
        /// Vorrat ist nach Pfad geschlüsselt und würde sonst Speicher halten, den
        /// niemand mehr abholt.
        /// </summary>
        private void VorratLeeren()
        {
            VorratLaufAbbrechen();

            lock (_vorratTor)
            {
                _vorrat.Clear();
                _vorratAlter.Clear();
            }
        }

        /// <summary>
        /// Nimmt dem laufenden Vorauslauf den Auftrag. Die gerade begonnene Dekodierung
        /// läuft zu Ende — sie abzubrechen ginge nur mitten im Decoder —, aber es wird
        /// nichts Neues mehr angefangen.
        /// </summary>
        private void VorratLaufAbbrechen()
        {
            var alt = _vorratLauf;
            _vorratLauf = null;

            // Bewusst kein Dispose: Der Faden liest sein Token noch, und ein
            // CancellationTokenSource ohne Zeitgeber und Anmeldungen hält nichts fest.
            alt?.Cancel();
        }

        /// <summary>
        /// Legt das eben angezeigte Bild in den Vorrat und stösst den Vorauslauf an.
        /// Aufruf aus dem UI-Faden, direkt nachdem das grosse Bild steht: Früher nähme
        /// der Vorauslauf der Platte genau die Zeit weg, die das gefragte Bild braucht.
        /// </summary>
        private void VorratNachfüllen(
            string pfad,
            int originalBreite,
            int originalHöhe,
            int dekodierBreite,
            int dekodierHöhe,
            BitmapSource? klein,
            BitmapSource? gross)
        {
            // Das gezeigte Bild gehört selbst hinein — sonst wäre der Schritt zurück
            // wieder ein voller Ladevorgang.
            if (klein is not null && gross is not null)
            {
                VorratEinlegen(
                    pfad,
                    new VorratsBild(originalBreite, originalHöhe, dekodierBreite, dekodierHöhe, klein, gross));
            }

            var nachbarn = NachbarPfadeSammeln();
            if (nachbarn.Count == 0)
            {
                return;
            }

            // Im UI-Faden holen: GetMonitorDecodeSize geht über das Hauptfenster.
            var (monitorBreite, monitorHöhe) = MieneServices.GetMonitorDecodeSize();

            VorratLaufAbbrechen();

            var quelle = new CancellationTokenSource();
            _vorratLauf = quelle;
            var marke = quelle.Token;

            // Ein einziger Faden. Die Platte liest ohnehin nacheinander, und jeder
            // weitere hielte währenddessen ein ganzes ausgepacktes Bild im Speicher —
            // dieselbe Überlegung wie im MiniaturLader.
            _ = Task.Run(
                () =>
                {
                    foreach (var nachbar in nachbarn)
                    {
                        if (marke.IsCancellationRequested)
                        {
                            return;
                        }

                        if (VorratNachschlagen(nachbar) is not null)
                        {
                            continue;
                        }

                        VorratLaden(nachbar, monitorBreite, monitorHöhe, marke);
                    }
                },
                marke);
        }

        /// <summary>
        /// Die Pfade rundherum, in Blätterrichtung zuerst. Läuft im UI-Faden, weil
        /// <c>AufgabenView</c> dort zu Hause ist.
        /// </summary>
        private List<string> NachbarPfadeSammeln()
        {
            var pfade = new List<string>();

            if (AufgabenView is null || AufgabenView.Count == 0 || AufgabenView.CurrentPosition < 0)
            {
                return pfade;
            }

            Sammle(_blätterRichtung, VorratVoraus);
            Sammle(-_blätterRichtung, VorratZurück);

            return pfade;

            void Sammle(int richtung, int anzahl)
            {
                int gefunden = 0;

                for (int i = AufgabenView.CurrentPosition + richtung;
                     i >= 0 && i < AufgabenView.Count && gefunden < anzahl;
                     i += richtung)
                {
                    // Dieselbe Auswahl wie beim Blättern: Was für links markiert ist,
                    // überspringen die Pfeile — vorzuladen wäre es umsonst.
                    if (AufgabenView.GetItemAt(i) is not MeinBildchen bildchen
                        || bildchen.BildFürLinks
                        || string.IsNullOrEmpty(bildchen.BName))
                    {
                        continue;
                    }

                    pfade.Add(bildchen.BName);
                    gefunden++;
                }
            }
        }

        /// <summary>
        /// Ein Nachbarbild vollständig laden. Denselben Weg geht der Ladebefehl, nur
        /// eben schon jetzt und auf einem Hintergrundfaden.
        /// </summary>
        private void VorratLaden(string pfad, int monitorBreite, int monitorHöhe, CancellationToken marke)
        {
            try
            {
                if (!File.Exists(pfad))
                {
                    return;
                }

                var (breite, höhe) = MieneServices.ReadOriginalSize(pfad);

                if (marke.IsCancellationRequested)
                {
                    return;
                }

                var klein = MieneServices.CreateBitmap(pfad, 100);

                if (marke.IsCancellationRequested)
                {
                    return;
                }

                var (dekodierBreite, dekodierHöhe) =
                    DekodierGrösseRechnen(breite, höhe, monitorBreite, monitorHöhe);

                var gross = MieneServices.CreateBitmap(pfad, dekodierBreite, dekodierHöhe);

                if (marke.IsCancellationRequested || klein is null || gross is null)
                {
                    return;
                }

                VorratEinlegen(
                    pfad,
                    new VorratsBild(breite, höhe, dekodierBreite, dekodierHöhe, klein, gross));
            }
            catch
            {
                // Ein Bild, das sich nicht vorladen lässt, ist hier kein Fehler: Der
                // Ladebefehl geht denselben Weg noch einmal und meldet dort, was nicht
                // stimmt — mit Ampelfeldern und Sprungbremse.
            }
        }

        #endregion
    }
}
