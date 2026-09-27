using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#if OCR_PLUGIN
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ocr.Vertrag;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
#endif

namespace TestImage.Bildersuche
{
    /// <summary>Womit die gefundenen Zeilen gelesen werden.</summary>
    internal enum LeseVerfahren
    {
        /// <summary>
        /// Mit dem eigenen Zeilen-Netz des Plugins (ab Fassung 0.0.3). Liest nur
        /// lateinische Schrift mit Umlauten und ß, Ziffern und gängige Satzzeichen.
        /// Bei einem älteren Plugin wird von selbst <see cref="WindowsOcr"/> genommen.
        /// </summary>
        Plugin,

        /// <summary>
        /// Jede Zeile in WPF geradegerückt und einzeln der Windows-OCR gegeben.
        /// Kennt die Sprachen des Benutzerprofils.
        /// </summary>
        WindowsOcr
    }

    /// <summary>
    /// Liest Text, den die Windows-OCR im ganzen Bild nicht findet: schräge
    /// Beschriftung, helle Schrift auf unruhigem Grund (Karten, Luftbilder).
    ///
    /// Das Plugin (<c>Ocr.Core.dll</c>) sucht die Textzeilen in beliebiger
    /// Neigung und liest sie ab Fassung 0.0.3 selbst. Auf dem Kartenausschnitt,
    /// an dem gemessen wurde, liest es 9 von 12 Strassennamen fehlerfrei in
    /// 0,8 s; über die Windows-OCR je Zeile waren es 5 in 2,1 s, die Windows-OCR
    /// allein fand im ganzen Bild 5 Wörter.
    ///
    /// <b>Zwei Ausgänge:</b> <see cref="LiesZeilenAsync"/> gibt nur die gelesenen
    /// Texte — das braucht der Ordnerlauf, und liest das Plugin selbst, entfällt
    /// dort der WPF-Zuschnitt ganz. <see cref="SucheBefundAsync"/> gibt zusätzlich
    /// Rahmen, Streifen und das Bild, zu dem die Koordinaten gehören — das braucht
    /// die Rahmenansicht, und dort wird immer zugeschnitten.
    ///
    /// <b>Scheinzeilen:</b> Das Plugin liest jede gefundene Zeile, auch Maserung
    /// und Symbole. Lesungen unter <see cref="MindestSicherheit"/> oder mit weniger
    /// als <see cref="MindestZeichen"/> Buchstaben und Ziffern zählen nicht.
    ///
    /// <b>Das Plugin liegt nicht im Repo.</b> Es kommt aus einem eigenen Projekt
    /// und wird als DLL eingelegt: <c>lib\Ocr.Vertrag.dll</c> (Schnittstellen)
    /// und <c>lib\Plugins\Ocr.Core.dll</c> (Verfahren), beide in der .gitignore.
    /// Fehlen sie, setzt TestImage.csproj <c>OCR_PLUGIN</c> nicht, und von dieser
    /// Klasse bleibt der untere Zweig übrig: <see cref="IstVerfuegbar"/> meldet
    /// <c>false</c>, die beiden Suchen geben <c>null</c>. Die Aufrufstellen
    /// brauchen deshalb kein einziges <c>#if</c> — Näheres in <c>lib\LIESMICH.md</c>.
    ///
    /// <b>Die Anwendung kennt nur den Vertrag.</b> Das Plugin wird zur Laufzeit
    /// aus <c>PluginOrdner</c> geladen; fehlt es, läuft alles andere weiter.
    ///
    /// <b>Warum für die Windows-OCR hier und nicht im Plugin geradegerückt wird:</b>
    /// Derselbe Ausschnitt, von WPF gezeichnet, liefert 8 fehlerfreie Namen, vom
    /// Plugin selbst gezeichnet nur 5 — warum, ist ungeklärt. Das eigene Netz des
    /// Plugins schneidet dagegen selbst zu; der WPF-Streifen dient dann nur der Anzeige.
    /// </summary>
    internal static class PluginOcrDienst
    {
#if OCR_PLUGIN

        /// <summary>
        /// Wo das Plugin liegt. Muss vor dem ersten Aufruf gesetzt sein, danach
        /// wirkt eine Änderung nicht mehr. Eigener Unterordner, damit der Lader
        /// nicht jede DLL der Anwendung ansehen muss.
        /// </summary>
        internal static string PluginOrdner { get; set; } =
            Path.Combine(AppContext.BaseDirectory, "Plugins");

        /// <summary>Einmal geladen und behalten — das Laden kostet, der Zustand bleibt.</summary>
        private static readonly Lazy<IOcrErkennung?> Erkennung =
            new(() => PluginLader.Lade(PluginOrdner));

        private static readonly Lazy<OcrEngine?> Engine = new(() =>
        {
            try
            {
                return OcrEngine.TryCreateFromUserProfileLanguages();
            }
            catch (Exception)
            {
                return null;
            }
        });

        /// <summary>
        /// True, wenn das Plugin da ist und lesen kann — selbst (ab 0.0.3) oder über
        /// die Windows-OCR.
        /// </summary>
        internal static bool IstVerfuegbar =>
            Erkennung.Value is { } e && (e.KannLesen || Engine.Value is not null);

        /// <summary>True, wenn das Plugin selbst liest, <see cref="LeseVerfahren.Plugin"/> also wirkt.</summary>
        internal static bool LiestSelbst => Erkennung.Value?.KannLesen == true;

        /// <summary>
        /// Mindestsicherheit einer Lesung des Plugins (0..1). Darunter gilt die Zeile
        /// als Scheinzeile. Gemessen in OcrTestClaude an 44 Standbildern: echte Schrift
        /// fast immer ≥ 0,85, die meisten Scheinzeilen 0,3–0,8.
        /// </summary>
        internal const float MindestSicherheit = 0.85f;

        /// <summary>
        /// Mindestzahl an Buchstaben und Ziffern einer Lesung des Plugins. Nötig, weil
        /// kurze Scheinzeilen oft hohe Sicherheit erreichen („t“ 0,99, „K“ 1,00).
        /// Kostet echte Ein- und Zweizeichen-Zeilen.
        /// </summary>
        internal const int MindestZeichen = 3;

        /// <summary>
        /// True, wenn eine Lesung des Plugins nicht zählt. Ohne Sicherheit (Plugin
        /// 0.0.3) wird nichts verworfen.
        /// </summary>
        private static bool IstScheinzeile(OcrLesung lesung) =>
            lesung.Sicherheit is float sicherheit
            && (sicherheit < MindestSicherheit || lesung.Text.Count(char.IsLetterOrDigit) < MindestZeichen);

        /// <summary>
        /// True, wenn das Plugin helle und dunkle Schrift in <b>einem</b> Lauf sucht und
        /// zusammenführt (ab Fassung 0.0.5).
        ///
        /// Gefragt wird die Fassung, nicht einfach die Einstellung gesetzt: Ein älteres
        /// Plugin übergeht sie stillschweigend und sucht nur eine der beiden Arten — der
        /// Aufrufer bekäme also weniger Zeilen, ohne dass es jemand merkt. Er soll
        /// stattdessen weiter zwei Durchgänge fahren.
        /// </summary>
        internal static bool KannBeideSchriftfarben =>
            Erkennung.Value is { } e
            && Version.TryParse(e.Fassung, out Version? fassung)
            && fassung >= new Version(0, 0, 5);

        /// <summary>Name und Fassung des Plugins, für die Anzeige. Leer ohne Plugin.</summary>
        internal static string Beschreibung =>
            Erkennung.Value is { } e ? $"{e.Name} ({e.Fassung})" : string.Empty;

        /// <summary>
        /// Liest die Textzeilen eines Bildes, jede für sich geradegerückt.
        /// <c>null</c>, wenn nicht gelesen werden konnte (kein Plugin, keine
        /// Windows-OCR, unlesbare Datei). Eine leere Liste heisst: gesucht, aber
        /// keine Zeile gefunden.
        ///
        /// Zeilen ohne gelesenen Text fallen heraus — für den Ordnerlauf ist eine
        /// leere Zeile nichts wert. Wer sie sehen will (weil sie verrät, wo das
        /// Verfahren Gitter oder Wegränder für Schrift hält), nimmt
        /// <see cref="SucheBefundAsync"/>.
        /// </summary>
        /// <param name="pfad">Bilddatei.</param>
        /// <param name="dunklerTextAufHell">
        /// <c>false</c> für helle Schrift auf dunklem Grund (Karte, Luftbild),
        /// <c>true</c> für dunkle Schrift auf hellem Grund (Dokument, Foto einer
        /// Seite). Ein Lauf sucht nur eine der beiden Arten.
        /// </param>
        /// <param name="token">Bricht in der Suche und zwischen zwei Zeilen ab.</param>
        /// <param name="verfahren">
        /// Womit gelesen wird. Kann das Plugin nicht selbst lesen (vor 0.0.3), wird
        /// die Windows-OCR genommen.
        /// </param>
        /// <param name="beideSchriftfarben">
        /// Helle <b>und</b> dunkle Schrift in einem Lauf suchen und zusammenführen;
        /// <paramref name="dunklerTextAufHell"/> zählt dann nicht. Doppelte fallen weg,
        /// Zeilen über einen Farbwechsel hinweg werden verbunden. Rund doppelte Suchzeit,
        /// also etwa so viel wie zwei getrennte Durchgänge. Nur setzen, wenn
        /// <see cref="KannBeideSchriftfarben"/> gilt.
        /// </param>
        internal static async Task<IReadOnlyList<string>?> LiesZeilenAsync(
            string pfad, bool dunklerTextAufHell = false, CancellationToken token = default,
            LeseVerfahren verfahren = LeseVerfahren.Plugin, bool beideSchriftfarben = false)
        {
            if (verfahren == LeseVerfahren.Plugin && Erkennung.Value is { KannLesen: true } erkennung)
            {
                return await LiesSelbstAsync(erkennung, pfad, dunklerTextAufHell, beideSchriftfarben, token)
                    .ConfigureAwait(false);
            }

            OcrZeilenBefund? befund = await SucheBefundAsync(
                pfad, dunklerTextAufHell, token, verfahren: LeseVerfahren.WindowsOcr,
                beideSchriftfarben: beideSchriftfarben).ConfigureAwait(false);

            if (befund is null)
            {
                return null;
            }

            var zeilen = new List<string>(befund.Zeilen.Count);
            foreach (OcrZeilenFund zeile in befund.Zeilen)
            {
                if (zeile.ZaehltAlsText)
                {
                    zeilen.Add(zeile.Text);
                }
            }

            return zeilen;
        }

        /// <summary>
        /// Der schnelle Weg für den Ordnerlauf: suchen und das Plugin lesen lassen, ohne
        /// WPF-Zuschnitt — den braucht nur die Anzeige. Laden und Suchen brauchen keinen
        /// STA-Faden; das Bild wird eingefroren geladen.
        /// </summary>
        private static async Task<IReadOnlyList<string>?> LiesSelbstAsync(
            IOcrErkennung erkennung, string pfad, bool dunklerTextAufHell, bool beideSchriftfarben,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(pfad) || !File.Exists(pfad))
            {
                return null;
            }

            try
            {
                return await Task.Run<IReadOnlyList<string>?>(() =>
                {
                    (_, IOcrFund fund) = Suche(
                        erkennung, pfad, dunklerTextAufHell, beideSchriftfarben, null, token);

                    var zeilen = new List<string>(fund.Zeilen.Count);
                    for (int i = 0; i < fund.Zeilen.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();

                        OcrLesung lesung = fund.LiesMitSicherheit(i);
                        if (lesung.Text.Length > 0 && !IstScheinzeile(lesung))
                        {
                            zeilen.Add(lesung.Text);
                        }
                    }

                    return zeilen;
                }, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Sucht die Textzeilen und gibt alles zurück, was zum Ansehen nötig ist:
        /// Rahmen, geradegerückter Streifen, gelesener Text — und das Bild, zu dem
        /// die Koordinaten gehören. <c>null</c> unter denselben Bedingungen wie
        /// bei <see cref="LiesZeilenAsync"/>.
        ///
        /// Hier bleiben auch die Zeilen stehen, aus denen nichts zu lesen war:
        /// Sie sind der interessante Teil der Fehlersuche, denn jede kostet einen
        /// OCR-Lauf und bringt nichts ein.
        /// </summary>
        /// <param name="abNummer">
        /// Nummer, ab der gezählt wird. Wer beide Schriftarten hintereinander sucht und
        /// die Ergebnisse in eine Liste legt, gibt hier die Anzahl des ersten Laufs mit
        /// — sonst käme jede Nummer zweimal vor und die Liste passte nicht mehr zu den
        /// Rahmen im Bild.
        /// </param>
        /// <param name="fortschritt">
        /// Stückzahl für einen Balken, darf <c>null</c> sein. Gesamt ist immer
        /// <see cref="Promille"/>; Aufteilung siehe <see cref="AnteilSuche"/>. Die Stufe
        /// ist nur Anzeige, nicht darauf verzweigen. Im Oberflächenfaden als
        /// <c>Progress</c> anlegen — die Meldungen kommen aus Hintergrundfäden, und
        /// manche erst nach dem <c>await</c>.
        ///
        /// Ein Tupel statt <c>OcrFortschritt</c> aus dem Vertrag: Ohne eingelegtes
        /// Plugin gibt es diesen Typ nicht, und die Aufrufstellen sollen ohne
        /// <c>#if</c> auskommen. Die Restzeit rechnet der Aufrufer — bei zwei
        /// Durchgängen gälte die des Plugins ohnehin nur für den einen.
        /// </param>
        /// <param name="verfahren">
        /// Womit gelesen wird. Zugeschnitten wird in beiden Fällen — die Streifen sind
        /// das, was die Ansicht zeigt. Liest das Plugin selbst, ist der Streifen nur
        /// Anzeige, der Text kommt aus dem Netz.
        /// </param>
        internal static async Task<OcrZeilenBefund?> SucheBefundAsync(
            string pfad,
            bool dunklerTextAufHell = false,
            CancellationToken token = default,
            int abNummer = 0,
            IProgress<(int Erledigt, int Gesamt, string Stufe)>? fortschritt = null,
            LeseVerfahren verfahren = LeseVerfahren.Plugin,
            bool beideSchriftfarben = false)
        {
            IOcrErkennung? erkennung = Erkennung.Value;

            if (erkennung is null || string.IsNullOrWhiteSpace(pfad) || !File.Exists(pfad))
            {
                return null;
            }

            bool eigen = verfahren == LeseVerfahren.Plugin && erkennung.KannLesen;

            // Die Windows-OCR nur erzeugen, wenn sie gebraucht wird.
            OcrEngine? engine = eigen ? null : Engine.Value;
            if (!eigen && engine is null)
            {
                return null;
            }

            try
            {
                var uhr = Stopwatch.StartNew();
                fortschritt?.Report((0, Promille, "Laden"));

                // Liest das Plugin selbst, geschieht das Lesen gleich beim Zuschneiden,
                // und der Zuschnitt bekommt den ganzen Rest des Balkens.
                string stufeJeZeile = eigen ? "Zuschnitt und Lesen" : "Zuschnitt";
                int anteilJeZeile = eigen ? Promille - AnteilSuche : AnteilZuschnitt;

                // Die Suche meldet selbst in Promille; hier auf 0 … AnteilSuche umgerechnet.
                // Ihr „Fertig" ist hier erst der Beginn des Zuschnitts.
                IProgress<OcrFortschritt>? suchMeldung = fortschritt is null
                    ? null
                    : new Sofort(p => fortschritt.Report((
                        AnteilSuche * p.Erledigt / Math.Max(1, p.Gesamt),
                        Promille,
                        p.Stufe == "Fertig" ? stufeJeZeile : p.Stufe)));

                Action<int, int>? zuschnittMeldung = fortschritt is null
                    ? null
                    : (erledigt, anzahl) => fortschritt.Report((
                        AnteilSuche + anteilJeZeile * erledigt / Math.Max(1, anzahl),
                        Promille,
                        stufeJeZeile));

                // Suchen und Zuschneiden brauchen WPF und damit einen STA-Faden.
                // Ein eigener, damit der Aufrufer von jedem Faden aus rufen kann
                // und die Oberfläche währenddessen nicht steht.
                Rohbefund roh = await AufStaFadenAsync(
                    () => SucheUndSchneide(
                        erkennung, pfad, dunklerTextAufHell, beideSchriftfarben, eigen,
                        suchMeldung, zuschnittMeldung, token))
                    .ConfigureAwait(false);

                var zeilen = new List<OcrZeilenFund>(roh.Schnitte.Count);
                int nummer = abNummer;

                string schriftart = beideSchriftfarben
                    ? ", hell + dunkel"
                    : dunklerTextAufHell ? ", dunkle Schrift" : ", helle Schrift";

                string herkunft = (eigen ? "Plugin" : "Plugin + Windows-OCR") + schriftart;

                foreach (Schnitt schnitt in roh.Schnitte)
                {
                    token.ThrowIfCancellationRequested();

                    // engine ist oben geprüft: Ohne eigene Lesung kommt nur her, wer die Windows-OCR hat.
                    string text = schnitt.Lesung is { } lesung
                        ? lesung.Text
                        : await LiesAsync(engine!, schnitt.Png).ConfigureAwait(false);

                    zeilen.Add(new OcrZeilenFund
                    {
                        Nummer = ++nummer,
                        Herkunft = herkunft,
                        Text = text,
                        Ecken = schnitt.Ecken,
                        Streifen = schnitt.Streifen,
                        Winkel = schnitt.Winkel,
                        Zeichen = schnitt.Zeichen,
                        Laenge = schnitt.Laenge,
                        MittlereHoehe = schnitt.MittlereHoehe,
                        Sicherheit = schnitt.Lesung?.Sicherheit,
                        IstScheinzeile = schnitt.Lesung is { } l && IstScheinzeile(l)
                    });

                    if (!eigen)
                    {
                        fortschritt?.Report((
                            AnteilSuche + AnteilZuschnitt + AnteilLesen * (nummer - abNummer) / roh.Schnitte.Count,
                            Promille,
                            "Lesen"));
                    }
                }

                fortschritt?.Report((Promille, Promille, "Fertig"));

                return new OcrZeilenBefund
                {
                    Bild = roh.Bild,
                    Zeilen = zeilen,
                    Dauer = uhr.Elapsed
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Ein zugeschnittener Streifen, zweimal derselbe Inhalt: als PNG für die
        /// Windows-OCR, als Bitmap für die Anzeige. Beide entstehen aus einem
        /// Zeichenvorgang, die Anzeige kostet also nichts extra. Liest das Plugin
        /// selbst, bleibt das PNG leer und <see cref="Lesung"/> trägt den Text.
        /// </summary>
        private sealed record Schnitt(
            byte[] Png,
            BitmapSource Streifen,
            PointCollection Ecken,
            double Winkel,
            int Zeichen,
            double Laenge,
            double MittlereHoehe)
        {
            /// <summary>Die Lesung des Plugins; <c>null</c>, wenn die Windows-OCR liest.</summary>
            public OcrLesung? Lesung { get; init; }
        }

        /// <summary>Was der STA-Faden zurückbringt: das geladene Bild und seine Streifen.</summary>
        private sealed record Rohbefund(BitmapSource Bild, List<Schnitt> Schnitte);

        /// <summary>Gesamtzahl jeder Fortschrittsmeldung — dieselbe Einheit wie im Plugin.</summary>
        private const int Promille = 1000;

        /// <summary>
        /// Anteile am Fortschritt in Promille, gemessen mit Werkzeuge\Einbauprobe im
        /// Projekt OcrTestClaude auf einer A3-Scanseite (3400 px, 265 Zeilen, 9,2 s):
        /// Laden und Suche 1,5 s, Zuschnitt in WPF 4,1 s, Lesen durch die Windows-OCR
        /// 3,6 s. Zuschnitt und Lesen zählen je Zeile ein Stück.
        /// </summary>
        private const int AnteilSuche = 150;
        private const int AnteilZuschnitt = 450;
        private const int AnteilLesen = Promille - AnteilSuche - AnteilZuschnitt;

        /// <summary>
        /// Leitet die Meldungen der Suche sofort weiter. Ein zweites <c>Progress</c>
        /// wäre falsch: Auf dem STA-Faden gibt es keinen Kontext, in den es
        /// zurückstellen könnte — das tut erst das des Aufrufers.
        /// </summary>
        private sealed class Sofort(Action<OcrFortschritt> ziel) : IProgress<OcrFortschritt>
        {
            public void Report(OcrFortschritt value) => ziel(value);
        }

        private static Rohbefund SucheUndSchneide(
            IOcrErkennung erkennung,
            string pfad,
            bool dunklerTextAufHell,
            bool beideSchriftfarben,
            bool eigen,
            IProgress<OcrFortschritt>? suchMeldung,
            Action<int, int>? zuschnittMeldung,
            CancellationToken token)
        {
            (BitmapSource bild, IOcrFund fund) = Suche(
                erkennung, pfad, dunklerTextAufHell, beideSchriftfarben, suchMeldung, token);

            var schnitte = new List<Schnitt>(fund.Zeilen.Count);
            for (int i = 0; i < fund.Zeilen.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                // Liest das Plugin selbst, braucht niemand das PNG — nur den Streifen zum Ansehen.
                Schnitt schnitt = Geraderuecken(bild, fund.Zeilen[i], mitPng: !eigen);
                schnitte.Add(eigen ? schnitt with { Lesung = fund.LiesMitSicherheit(i) } : schnitt);

                zuschnittMeldung?.Invoke(i + 1, fund.Zeilen.Count);
            }

            return new Rohbefund(bild, schnitte);
        }

        /// <summary>Lädt das Bild und sucht die Zeilen; das Bild bleibt für den Zuschnitt.</summary>
        private static (BitmapSource Bild, IOcrFund Fund) Suche(
            IOcrErkennung erkennung,
            string pfad,
            bool dunklerTextAufHell,
            bool beideSchriftfarben,
            IProgress<OcrFortschritt>? suchMeldung,
            CancellationToken token)
        {
            // Derselbe Lader wie für die Anzeige und die Wortkästen der Standardsuche:
            // Alle Koordinaten müssen sich auf dieselbe Bitmap beziehen.
            BitmapSource bild = BildLader.Lade(pfad);

            // BGRA, weil der Vertrag dafür einen Umwandler mitbringt — er kümmert
            // sich um Kanalreihenfolge und aufgefüllte Zeilen.
            var bgra = new FormatConvertedBitmap(bild, PixelFormats.Bgra32, null, 0);
            int schrittweite = bgra.PixelWidth * 4;
            var puffer = new byte[schrittweite * bgra.PixelHeight];
            bgra.CopyPixels(puffer, schrittweite, 0);

            OcrBild eingabe = OcrBild.AusBgra32(bgra.PixelWidth, bgra.PixelHeight, puffer, schrittweite);

            // Nur den Modus setzen; alle Schwellen nehmen dessen Vorgaben. Das Plugin
            // prüft den Abbruch selbst zwischen den Stufen und je Bildzeile.
            var optionen = new OcrOptionen
            {
                DunklerTextAufHell = dunklerTextAufHell,
                BeideSchriftfarben = beideSchriftfarben
            };

            IOcrFund fund = erkennung.Finde(eingabe, optionen, suchMeldung, token);

            return (bild, fund);
        }

        // Zuschnitt wie im Plugin (Ocr.Core, AusschnittOptionen): gleiche Luft
        // und Zielgrösse, damit nur die Abtastung von WPF stammt.
        private const double LuftLaengs = 2.0;       // × Schrifthöhe, links und rechts
        private const double LuftQuer = 0.8;         // × Schrifthöhe, oben und unten
        private const double ZielSchrifthoehe = 48;  // Pixel — die Windows-OCR mag grosse Schrift
        private const double MinFaktor = 1.0;
        private const double MaxFaktor = 6.0;

        /// <summary>
        /// Dreht die Zeile waagerecht, vergrössert sie auf eine gut lesbare
        /// Schrifthöhe und schneidet sie mit etwas Luft aus.
        ///
        /// Alles, was die Anzeige später braucht, wird hier eingefroren: Der
        /// Aufrufer sitzt auf einem anderen Faden, und ein unfreezed Freezable
        /// gehört dem Faden, der es erzeugt hat.
        /// </summary>
        private static Schnitt Geraderuecken(BitmapSource bild, OcrZeile zeile, bool mitPng)
        {
            OcrPunkt[] e = zeile.Ecken;

            double mitteX = (e[0].X + e[1].X + e[2].X + e[3].X) / 4.0;
            double mitteY = (e[0].Y + e[1].Y + e[2].Y + e[3].Y) / 4.0;

            // Ecke 0 → 3 ist die Querkante: die Höhe des gedrehten Rechtecks.
            double dx = e[0].X - e[3].X;
            double dy = e[0].Y - e[3].Y;
            double quer = Math.Sqrt(dx * dx + dy * dy);

            double breite = zeile.Laenge + 2 * LuftLaengs * zeile.MittlereHoehe;
            double hoehe = quer + 2 * LuftQuer * zeile.MittlereHoehe;

            double faktor = Math.Clamp(ZielSchrifthoehe / Math.Max(1.0, zeile.MittlereHoehe), MinFaktor, MaxFaktor);

            int ausBreite = (int)Math.Ceiling(breite * faktor);
            int ausHoehe = (int)Math.Ceiling(hoehe * faktor);

            var wandlung = new TransformGroup();
            wandlung.Children.Add(new TranslateTransform(-mitteX, -mitteY));
            wandlung.Children.Add(new RotateTransform(-zeile.Winkel));
            wandlung.Children.Add(new ScaleTransform(faktor, faktor));
            wandlung.Children.Add(new TranslateTransform(ausBreite / 2.0, ausHoehe / 2.0));

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.PushTransform(wandlung);
                dc.DrawImage(bild, new Rect(0, 0, bild.PixelWidth, bild.PixelHeight));
                dc.Pop();
            }

            var ziel = new RenderTargetBitmap(ausBreite, ausHoehe, 96, 96, PixelFormats.Pbgra32);
            ziel.Render(visual);
            ziel.Freeze();

            byte[] png = [];
            if (mitPng)
            {
                var kodierer = new PngBitmapEncoder();

                // Ausgeschrieben, weil BitmapFrame in WPF und in WinRT existiert.
                kodierer.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(ziel));

                using var speicher = new MemoryStream();
                kodierer.Save(speicher);
                png = speicher.ToArray();
            }

            var ecken = new PointCollection(e.Length);
            foreach (OcrPunkt p in e)
            {
                ecken.Add(new Point(p.X, p.Y));
            }

            ecken.Freeze();

            return new Schnitt(
                png,
                ziel,
                ecken,
                zeile.Winkel,
                zeile.Zeichen,
                zeile.Laenge,
                zeile.MittlereHoehe);
        }

        private static async Task<string> LiesAsync(OcrEngine engine, byte[] png)
        {
            using var strom = new InMemoryRandomAccessStream();

            var schreiber = new DataWriter(strom);
            schreiber.WriteBytes(png);
            await schreiber.StoreAsync();
            await schreiber.FlushAsync();
            schreiber.DetachStream();
            strom.Seek(0);

            // Ausgeschrieben, weil BitmapDecoder in WinRT und in WPF existiert.
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(strom);

            using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            OcrResult ergebnis = await engine.RecognizeAsync(bitmap);
            return (ergebnis.Text ?? string.Empty).Trim();
        }

        /// <summary>
        /// Führt WPF-Arbeit auf einem eigenen STA-Faden aus. RenderTargetBitmap
        /// und DrawingVisual verlangen STA; ein Hintergrundfaden des Pools ist MTA.
        /// </summary>
        private static Task<T> AufStaFadenAsync<T>(Func<T> arbeit)
        {
            var ergebnis = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            var faden = new Thread(() =>
            {
                try
                {
                    ergebnis.SetResult(arbeit());
                }
                catch (Exception ex)
                {
                    ergebnis.SetException(ex);
                }
            });

            faden.SetApartmentState(ApartmentState.STA);
            faden.IsBackground = true;
            faden.Start();

            return ergebnis.Task;
        }

#else

        /// <summary>
        /// Ohne eingelegtes Plugin. Die Aufrufstellen fragen zuerst
        /// <see cref="IstVerfuegbar"/> und lassen die schrägen Zeilen dann aus —
        /// gelesen wird allein mit <see cref="OcrDienst"/>.
        /// </summary>
        internal static bool IstVerfuegbar => false;

        /// <summary>Immer <c>false</c> ohne Plugin.</summary>
        internal static bool LiestSelbst => false;

        /// <summary>Immer <c>false</c> ohne Plugin.</summary>
        internal static bool KannBeideSchriftfarben => false;

        /// <summary>Leer, solange kein Plugin eingelegt ist.</summary>
        internal static string Beschreibung => string.Empty;

        /// <summary>Immer <c>null</c> — es gibt niemanden, der die Zeilen finden könnte.</summary>
        internal static Task<IReadOnlyList<string>?> LiesZeilenAsync(
            string pfad, bool dunklerTextAufHell = false, CancellationToken token = default,
            LeseVerfahren verfahren = LeseVerfahren.Plugin, bool beideSchriftfarben = false)
            => Task.FromResult<IReadOnlyList<string>?>(null);

        /// <summary>Immer <c>null</c>; die Rahmenansicht bleibt dann leer und sagt es.</summary>
        internal static Task<OcrZeilenBefund?> SucheBefundAsync(
            string pfad,
            bool dunklerTextAufHell = false,
            CancellationToken token = default,
            int abNummer = 0,
            IProgress<(int Erledigt, int Gesamt, string Stufe)>? fortschritt = null,
            LeseVerfahren verfahren = LeseVerfahren.Plugin,
            bool beideSchriftfarben = false)
            => Task.FromResult<OcrZeilenBefund?>(null);

#endif
    }
}
