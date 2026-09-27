using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestImage.Ansichten;
using TestImage.Bildersuche;

namespace TestImage
{
    /// <summary>
    /// Texterkennung über die Windows-eigene OCR: einen Ordner einmal durchlesen, den
    /// Text neben die Bilder legen, danach darin suchen.
    ///
    /// <b>Der Text liegt je Bildordner</b> in <c>.bildocr.json</c> — genau wie der
    /// CLIP-Index daneben, und aus demselben Grund: Er wandert mit, wenn der Ordner
    /// verschoben wird, und muss dann nicht neu erkannt werden.
    ///
    /// <b>Derzeit im Klartext.</b> Wer Bildschirmfotos mit Kennwörtern in der Sammlung
    /// hat, hat sie danach ein zweites Mal lesbar auf der Platte. Eine Verschlüsselung
    /// ist vorgesehen; die Cache-Datei trägt dafür eine Version.
    ///
    /// Die Treffer landen in derselben Ergebnisliste wie die Begriffssuche. Damit
    /// funktionieren „Treffer öffnen" und „In Liste übernehmen" ohne Zutun mit.
    ///
    /// <b>Schräge Schrift kommt aus einem Plugin</b>, wenn eines eingelegt ist: Die
    /// Windows-OCR kennt je Bild nur einen Textwinkel und scheitert deshalb an
    /// Kartenbeschriftung. Das Kreuzchen dafür ist ab Werk aus, weil jedes Bild damit
    /// rund eine Sekunde mehr kostet — siehe <see cref="OcrSchraegMitlesen"/> und
    /// <c>lib\LIESMICH.md</c>.
    /// </summary>
    public partial class AufgabeViewModel
    {
        private readonly OcrCache _ocrCache = new();

        /// <summary>Ordner, dessen Cache gerade geladen ist — verhindert unnötiges Neuladen.</summary>
        private string _ocrCacheOrdner = string.Empty;

        /// <summary>True, wenn Windows auf diesem Rechner überhaupt Text erkennen kann.</summary>
        public bool OcrVerfuegbar => OcrDienst.IstVerfuegbar;

        /// <summary>Sprache der Erkennung, für die Anzeige in der Karte.</summary>
        public string OcrSprache => OcrDienst.Sprache;

        /// <summary>
        /// True, wenn das optionale Plugin für schräge Schrift eingelegt und geladen ist.
        /// Sperrt das Kreuzchen in der Karte, solange es fehlt.
        /// </summary>
        public bool OcrPluginVerfuegbar => PluginOcrDienst.IstVerfuegbar;

        /// <summary>Name und Fassung des Plugins — oder der Hinweis, dass keines daliegt.</summary>
        public string OcrPluginVermerk =>
            OcrPluginVerfuegbar ? PluginOcrDienst.Beschreibung : "Plugin nicht eingelegt";

        /// <summary>
        /// True, wenn zusätzlich die schrägen Zeilen des Plugins mitgelesen werden — in
        /// beiden Schriftarten, siehe <see cref="LiesTextMitPluginAsync"/>.
        ///
        /// <b>Ab Werk aus, und das mit Absicht:</b> Ein Bild kostet damit rund zwei
        /// Sekunden mehr — bei einem Ordnerlauf über viele Bilder ein Vielfaches der
        /// bisherigen Zeit. Der Gewinn liegt bei Karten und Luftbildern: schräge
        /// Beschriftung, an der die Windows-OCR im ganzen Bild scheitert, weil sie je
        /// Bild nur einen Textwinkel kennt.
        ///
        /// Wird das Kreuzchen gesetzt, gelten schon gelesene Bilder als veraltet und
        /// werden beim nächsten Lauf noch einmal vorgenommen — siehe
        /// <see cref="OcrEintrag.SchraegStufe"/>.
        /// </summary>
        [ObservableProperty]
        public partial bool OcrSchraegMitlesen { get; set; } = false;

        /// <summary>Das gewählte Bild neu lesen, sobald das Kreuzchen umgelegt wird.</summary>
        partial void OnOcrSchraegMitlesenChanged(bool value) => OcrVolltextAnfordern();

        /// <summary>
        /// Die Ansicht braucht nur die Windows-OCR: Ohne Plugin zeigt sie eben die
        /// Standardsuche allein — wo sie ihre Wörter gefunden hat und welchen
        /// Textwinkel sie für das Bild angenommen hat.
        /// </summary>
        private bool CanExecuteOcrRahmenAnsicht() => OcrVerfuegbar;

        /// <summary>
        /// Öffnet die Rahmenansicht für das angezeigte Bild: beide Suchen als Rahmen
        /// übereinander, daneben je Ebene das Gelesene, beim Überfahren der Ausschnitt.
        ///
        /// <b>Die Ansicht rechnet selbst.</b> Rahmen und Ausschnitte stehen in keinem
        /// Cache, sie entstehen bei jedem Öffnen neu — das kostet die beiden Läufe und
        /// zeigt dafür, was die Verfahren heute finden.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteOcrRahmenAnsicht))]
        private void CommandExecuteOcrRahmenAnsicht()
        {
            string? pfad = SelectedBildchen?.BName;
            if (string.IsNullOrEmpty(pfad))
            {
                OcrStatus = "Kein Bild gewählt — die Rahmenansicht gilt immer für das angezeigte Bild.";
                return;
            }

            OcrTextAnsicht.Zeige(pfad);
        }

        /// <summary>
        /// True, wenn der Inhalt der OCR-Karte ausgeklappt ist.
        ///
        /// Eingeklappt bleibt die Titelzeile stehen — die ganze Karte auszublenden hiesse,
        /// dass man sie ohne einen zweiten Knopf anderswo nicht wiederfände.
        /// </summary>
        [ObservableProperty]
        public partial bool IsOcrOffen { get; set; } = false;

        [RelayCommand]
        private void CommandExecuteOcrToggle() => IsOcrOffen = !IsOcrOffen;

        /// <summary>
        /// Beim Aufklappen den Text des gewählten Bildes nachholen: Zugeklappt wird
        /// nichts gelesen, sonst stünde die Karte beim Öffnen leer da.
        /// </summary>
        partial void OnIsOcrOffenChanged(bool value) => OcrVolltextAnfordern();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteOcrSuchenCommand))]
        public partial string OcrSuchText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string OcrStatus { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteOcrOrdnerLesenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CommandExecuteOcrSuchenCommand))]
        public partial bool OcrLaeuft { get; set; }

        [ObservableProperty]
        public partial double OcrFortschritt { get; set; }

        [ObservableProperty]
        public partial double OcrFortschrittMax { get; set; } = 1;

        /// <summary>Ordner des angezeigten Bildes, oder <c>null</c>.</summary>
        private string? OcrOrdner
        {
            get
            {
                string? pfad = SelectedBildchen?.BName;
                return string.IsNullOrEmpty(pfad) ? null : Path.GetDirectoryName(pfad);
            }
        }

        /// <summary>
        /// Lädt den Cache des aktuellen Ordners, falls noch nicht geschehen.
        ///
        /// Der Vergleich auf den Ordnernamen spart das Neulesen bei jedem Bildwechsel —
        /// beim Blättern innerhalb eines Ordners passiert hier nichts.
        /// </summary>
        private void StelleOcrCacheBereit()
        {
            string? ordner = OcrOrdner;
            if (string.IsNullOrEmpty(ordner))
            {
                return;
            }

            if (string.Equals(ordner, _ocrCacheOrdner, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ocrCache.Laden(ordner);
            _ocrCacheOrdner = ordner;
            MeldeOcrBestand();
        }

        /// <summary>Schreibt in OcrStatus, wie viele Bilder des Ordners gelesen sind.</summary>
        private void MeldeOcrBestand()
        {
            if (!OcrVerfuegbar)
            {
                OcrStatus = "Auf diesem Rechner ist kein Sprachpaket für die Texterkennung installiert.";
                return;
            }

            OcrStatus = _ocrCache.Anzahl == 0
                ? "Für diesen Ordner ist noch nichts gelesen."
                : $"{_ocrCache.Anzahl} Bilder gelesen.";
        }

        /// <summary>
        /// Liest ein Bild: Windows-OCR auf das ganze Bild, dazu auf Wunsch die schrägen
        /// Zeilen des Plugins. <c>null</c>, wenn das Bild gar nicht lesbar war.
        ///
        /// <b>Die Zeilen werden angehängt, nicht ersetzt</b> — damit wirken sie ohne
        /// weiteres Zutun in „Ordner lesen", in der Suche und im Volltextfeld.
        ///
        /// <b>Beide Schriftarten, zwei Läufe.</b> Ein Lauf des Plugins sucht entweder
        /// helle Schrift auf dunklem Grund (Karte, Luftbild) oder dunkle auf hellem
        /// (Dokument, Bildschirmfoto), nie beides. Welche Art in einem Bild steckt,
        /// weiss vorher niemand — und wer beim Lesen eines Ordners die falsche Hälfte
        /// auslässt, merkt es nie, weil ein fehlender Fund nichts hinterlässt.
        ///
        /// <b>Ab Plugin 0.0.5 ist es ein Lauf statt zweier</b>: Das Plugin sucht beide
        /// Schriftfarben selbst und führt sie zusammen, zum gleichen Preis wie zwei
        /// getrennte Läufe. Der Rest dieses Absatzes gilt für ältere Plugins.
        ///
        /// <b>Der zweite Lauf kostet weniger als der erste</b>, solange er nichts
        /// findet: Er sucht zwar durch das ganze Bild, hat danach aber keine Zeile
        /// vorzulegen, und die Erkennung je Zeile ist der teure Teil. Am
        /// Kartenausschnitt gemessen — 1637 ms für den hellen Durchgang mit 12 Zeilen,
        /// 364 ms für den dunklen mit keiner. Bei einem Bild, in dem beide Arten
        /// vorkommen, summiert es sich entsprechend auf.
        ///
        /// <b>Doppeltes fliegt raus</b> — sowohl was die Windows-OCR im ganzen Bild schon
        /// gefunden hat als auch Wiederholungen unter den Zeilen selbst, quer über beide
        /// Läufe. Das Plugin findet dieselbe Beschriftung regelmässig mehrfach; am
        /// Kartenausschnitt kam derselbe Strassenname dreimal. Für die Suche wäre das
        /// gleichgültig, im Volltextfeld stünde er dreimal da. Verglichen wird nur der
        /// ganze Text einer Zeile: Zwei Fassungen mit unterschiedlichen Lesefehlern (ein
        /// vertauschter Buchstabe genügt) bleiben beide stehen — welche die richtige ist,
        /// weiss an dieser Stelle niemand.
        /// </summary>
        private static async Task<string?> LiesTextMitPluginAsync(
            string pfad, bool mitSchraeg, CancellationToken token)
        {
            string? text = await OcrDienst.LiesTextAsync(pfad).ConfigureAwait(true);

            if (!mitSchraeg || text is null)
            {
                return text;
            }

            var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var neu = new List<string>();

            // Ab Plugin 0.0.5 genügt ein Lauf für beide Schriftfarben: Das Plugin sucht
            // hell und dunkel selbst, wirft Doppelte weg und verbindet Zeilen, die über
            // einen Farbwechsel laufen — das kann ein Zusammenführen hinterher nicht.
            // Ältere Plugins übergehen die Einstellung, deshalb dort weiter zwei Läufe.
            bool beideAufEinmal = PluginOcrDienst.KannBeideSchriftfarben;

            // Helle Schrift zuerst: Bei ihr liegt der belegte Gewinn, und bricht der
            // Lauf zwischen den beiden ab, ist der wichtigere Teil schon getan.
            bool[] schriftarten = beideAufEinmal ? [false] : [false, true];

            foreach (bool dunklerTextAufHell in schriftarten)
            {
                IReadOnlyList<string>? zeilen = await PluginOcrDienst
                    .LiesZeilenAsync(pfad, dunklerTextAufHell, token, beideSchriftfarben: beideAufEinmal)
                    .ConfigureAwait(true);

                if (zeilen is null)
                {
                    continue;
                }

                foreach (string zeile in zeilen)
                {
                    if (!text.Contains(zeile, StringComparison.OrdinalIgnoreCase) && gesehen.Add(zeile))
                    {
                        neu.Add(zeile);
                    }
                }
            }

            return neu.Count == 0
                ? text
                : text + Environment.NewLine + string.Join(Environment.NewLine, neu);
        }

        private bool CanExecuteOcrOrdnerLesen() =>
            OcrVerfuegbar && !OcrLaeuft && !IndexLaeuft && !PrüfungLäuft && OcAufgabens.Count > 0;

        /// <summary>
        /// Liest den Text aller Bilder des Ordners und legt ihn neben sie.
        ///
        /// Bereits gelesene Bilder werden übersprungen — erkannt am Vergleich von Grösse
        /// und Änderungszeit. Ein zweiter Lauf über denselben Ordner kostet deshalb fast
        /// nichts, und ein nachträglich hinzugekommenes Bild wird nachgeholt.
        ///
        /// Zwischenspeichern alle 25 Bilder: Bricht der Lauf ab oder stürzt etwas, ist
        /// die Arbeit bis dahin nicht verloren.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteOcrOrdnerLesen), IncludeCancelCommand = true)]
        private async Task CommandExecuteOcrOrdnerLesen(CancellationToken token)
        {
            string? ordner = OcrOrdner;
            if (string.IsNullOrEmpty(ordner))
            {
                return;
            }

            StelleOcrCacheBereit();

            // Momentaufnahme: Die Liste kann sich während des Laufs ändern.
            List<string> bilder = OcAufgabens
                .Select(b => b.BName)
                .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                .ToList();

            // Einmal festgehalten und nicht je Bild neu gefragt: Ein Kreuzchen, das
            // mitten im Lauf umgelegt wird, hinterliesse sonst einen Ordner, in dem die
            // eine Hälfte der Bilder mit und die andere ohne schräge Zeilen gelesen ist.
            bool schraeg = OcrSchraegMitlesen && PluginOcrDienst.IstVerfuegbar;

            OcrLaeuft = true;
            OcrFortschritt = 0;
            OcrFortschrittMax = Math.Max(1, bilder.Count);

            int gelesen = 0;
            int uebersprungen = 0;
            int ohneText = 0;
            int seitSpeichern = 0;

            // Für Restzeit und Tempo. Der Startzeitpunkt gehört vor die Schleife.
            var begonnen = DateTime.Now;
            var letzteMeldung = DateTime.MinValue;

            try
            {
                foreach (string bild in bilder)
                {
                    token.ThrowIfCancellationRequested();
                    OcrFortschritt++;

                    if (_ocrCache.IstAktuell(bild, schraeg))
                    {
                        uebersprungen++;
                        continue;
                    }

                    string? text = await LiesTextMitPluginAsync(bild, schraeg, token).ConfigureAwait(true);
                    if (text is null)
                    {
                        continue;   // unlesbar oder unbekanntes Format
                    }

                    _ocrCache.Setze(bild, text, OcrSprache, schraeg ? OcrCache.SchraegStufeVoll : 0);
                    gelesen++;

                    if (text.Length == 0)
                    {
                        ohneText++;
                    }

                    if (++seitSpeichern >= 25)
                    {
                        _ocrCache.Speichern(ordner);
                        seitSpeichern = 0;
                    }

                    // Höchstens viermal je Sekunde melden.
                    //
                    // Der Text hängt an einer Bindung; ihn je Bild zu setzen erzeugt bei
                    // tausenden Bildern tausende Oberflächen-Aktualisierungen für eine
                    // Zahl, die niemand so schnell lesen kann.
                    if ((DateTime.Now - letzteMeldung).TotalMilliseconds >= 250)
                    {
                        letzteMeldung = DateTime.Now;

                        var stand = new CLProgressStückzahl(
                            begonnen,
                            bilder.Count,
                            (long)OcrFortschritt,
                            done: false);

                        OcrStatus = $"Lese … {OcrFortschritt:F0} von {bilder.Count}  "
                                  + $"({stand.Percent:F0} %)   Rest {stand.Restzeit}   "
                                  + $"{stand.StückPerSecond:F1} Bilder/Sek";
                    }
                }

                var dauer = DateTime.Now - begonnen;
                OcrStatus = $"Fertig in {dauer.TotalSeconds:F0} Sek: {gelesen} gelesen, "
                          + $"davon {ohneText} ohne Text. {uebersprungen} waren schon gelesen."
                          + (schraeg ? "  Schräge Zeilen mitgelesen, hell und dunkel." : string.Empty);
            }
            catch (OperationCanceledException)
            {
                OcrStatus = $"Abgebrochen — {gelesen} Bilder gelesen, das bleibt gespeichert. "
                          + $"Ein neuer Lauf macht dort weiter.";
            }
            finally
            {
                if (!_ocrCache.Speichern(ordner))
                {
                    OcrStatus = "Der Text konnte nicht abgelegt werden — ist der Ordner schreibgeschützt?";
                }

                OcrLaeuft = false;
                OcrFortschritt = 0;
            }
        }

        private bool CanExecuteOcrSuchen() =>
            OcrVerfuegbar && !OcrLaeuft && !string.IsNullOrWhiteSpace(OcrSuchText);

        /// <summary>
        /// Sucht die Zeichenfolge im gelesenen Text und füllt die gemeinsame
        /// Ergebnisliste. Der Prozenttext nimmt dabei die Fundstelle auf — bei OCR sagt
        /// ein Prozentwert nichts, der Textausschnitt dagegen viel.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteOcrSuchen))]
        private async Task CommandExecuteOcrSuchen()
        {
            StelleOcrCacheBereit();

            // Leeren wie jeder andere Suchlauf — NICHT über VerwerfeSuchtreffer().
            //
            // Die Methode ist für den Ordnerwechsel da: Sie setzt SuchErgebnisseVeraltet
            // und färbt damit IC_SuchErgebnisse rot, samt der Meldung „Neuer Ordner
            // geladen". Beim zweiten Suchen im selben Ordner behauptet das etwas Falsches.
            SuchErgebnisse.Clear();
            LeereTrefferCache();
            ErgebnisseSindSchemaAehnlich = false;

            IReadOnlyList<string> treffer = _ocrCache.Suche(OcrSuchText);
            if (treffer.Count == 0)
            {
                // „ und “ sind die deutschen Anführungszeichen. Ausgeschrieben,
                // weil ein gewöhnliches " die Zeichenkette beenden würde.
                OcrStatus = _ocrCache.Anzahl == 0
                    ? "Für diesen Ordner ist noch nichts gelesen — erst „Ordner lesen“."
                    : $"Kein Treffer für „{OcrSuchText}“ in {_ocrCache.Anzahl} gelesenen Bildern.";
                return;
            }

            var liste = new List<(SuchErgebnis Erg, float Score)>(treffer.Count);
            foreach (string pfad in treffer)
            {
                liste.Add((new SuchErgebnis
                {
                    Path = pfad,
                    DateiName = Path.GetFileName(pfad),
                    ProzentText = Fundstelle(_ocrCache.Hole(pfad), OcrSuchText),
                    Thumb = LadeThumb(pfad)
                }, 1f));
            }

            await FuegeErgebnisseEinAsync(liste);

            OcrStatus = $"{treffer.Count} Treffer für „{OcrSuchText}“.";
            SucheStatus = OcrStatus;

            CommandExecuteTrefferUebernehmenCommand?.NotifyCanExecuteChanged();
        }

        #region Erkannter Text des gewählten Bildes

        /// <summary>
        /// Der erkannte Text des Bildes, das gerade angezeigt wird. Leer, wenn keiner
        /// vorliegt — was danebensteht, sagt <see cref="OcrVolltextKopf"/>.
        /// </summary>
        [ObservableProperty]
        public partial string OcrVolltext { get; set; } = string.Empty;

        /// <summary>
        /// Zeile über dem Textfeld: Dateiname und Umfang — oder der Grund, warum dort
        /// nichts steht. Ein leeres Feld ohne Erklärung sähe aus wie ein Fehler,
        /// obwohl das Bild schlicht keinen Text enthält.
        /// </summary>
        [ObservableProperty]
        public partial string OcrVolltextKopf { get; set; } = "Kein Bild gewählt.";

        /// <summary>Bricht eine noch laufende Einzelerkennung ab, sobald das Bild wechselt.</summary>
        private CancellationTokenSource? _ocrVolltextAbbruch;

        /// <summary>
        /// Zeigt den Text des gewählten Bildes an. Wird bei jedem Bildwechsel gerufen —
        /// also auch beim Durchklicken der Trefferliste, beim Blättern mit den Pfeilen
        /// und aus der Miniaturleiste.
        ///
        /// <b>Zugeklappte Karte kostet nichts:</b> Ohne <c>IsOcrOffen</c> wird weder
        /// gelesen noch etwas gesetzt. Beim Aufklappen holt
        /// <see cref="OnIsOcrOffenChanged"/> es nach.
        ///
        /// Liegt der Text schon im Cache, steht er sofort da. Fehlt er, wird das eine
        /// Bild nachgelesen — verzögert, siehe <see cref="LiesOcrVolltextNachAsync"/>.
        /// </summary>
        internal void OcrVolltextAnfordern()
        {
            _ocrVolltextAbbruch?.Cancel();
            _ocrVolltextAbbruch = null;

            if (!IsOcrOffen)
            {
                return;
            }

            // Kein File.Exists mehr: ein Plattenzugriff im UI-Faden, und zwar bei jedem
            // Bildwechsel — also bei jedem Druck auf eine Pfeiltaste, solange die Karte
            // aufgeklappt ist. Fehlt die Datei, meldet das der Nachlauf mit „nicht
            // lesbar", was ohnehin genauer ist als das frühere „Kein Bild gewählt.".
            string? pfad = SelectedBildchen?.BName;
            if (string.IsNullOrEmpty(pfad))
            {
                OcrVolltext = string.Empty;
                OcrVolltextKopf = "Kein Bild gewählt.";
                return;
            }

            if (!OcrVerfuegbar)
            {
                OcrVolltext = string.Empty;
                OcrVolltextKopf = "Auf diesem Rechner ist kein Sprachpaket für die Texterkennung installiert.";
                return;
            }

            // Bleibt im UI-Faden, obwohl es beim Ordnerwechsel die Cache-Datei liest:
            // Der Vergleich mit _ocrCacheOrdner fängt jeden weiteren Aufruf ab, es
            // passiert also einmal je Ordner und nicht je Bild — und ohne den geladenen
            // Cache gäbe es unten nichts sofort zu zeigen.
            StelleOcrCacheBereit();

            string name = Path.GetFileName(pfad);

            // Was im Speicher liegt, sofort zeigen — ohne die Datei zu befragen.
            //
            // Vorher stand hier IstAktuell, und das macht ein FileInfo auf das Bild:
            // Grösse und Änderungszeit, im UI-Faden, bei jedem Bildwechsel. Geprüft wird
            // weiterhin, nur eben im Nachlauf. Der Preis ist, dass ein Text zu einem
            // inzwischen bearbeiteten Bild kurz sichtbar bleibt, bis der Nachlauf ihn
            // ersetzt; ihn dafür jedes Mal hinter einem „wird gelesen …" zu verstecken
            // wäre der schlechtere Tausch.
            string? bekannt = _ocrCache.Hole(pfad);

            if (bekannt is not null)
            {
                ZeigeOcrVolltext(name, bekannt);
            }
            else
            {
                OcrVolltext = string.Empty;
                OcrVolltextKopf = $"{name} — wird gelesen …";
            }

            var abbruch = new CancellationTokenSource();
            _ocrVolltextAbbruch = abbruch;

            // Absichtlich nicht abgewartet: Der Bildwechsel darf nicht auf die
            // Erkennung warten. Fehler landen in der Kopfzeile, nicht in einer Ausnahme.
            _ = LiesOcrVolltextNachAsync(pfad, name, abbruch.Token);
        }

        /// <summary>
        /// Liest ein einzelnes Bild nach, das der Ordnerlauf noch nicht erfasst hat, und
        /// legt das Ergebnis in denselben Cache — beim nächsten Betrachten steht es sofort da.
        ///
        /// <b>Die Wartezeit vorweg ist der Kern:</b> Wer sich durch dreissig Treffer
        /// klickt, streift neunundzwanzig Bilder nur. Ohne die Verzögerung liefe für
        /// jedes davon eine Erkennung an, die niemand sehen will. Der Abbruch beim
        /// nächsten Bildwechsel erledigt den Rest.
        /// </summary>
        private async Task LiesOcrVolltextNachAsync(string pfad, string name, CancellationToken token)
        {
            try
            {
                await Task.Delay(350, token).ConfigureAwait(true);

                // Passt der gespeicherte Text noch zum Stand der Datei? Diese Frage stand
                // früher im UI-Faden vor dem Anzeigen; sie kostet ein FileInfo, also einen
                // Plattenzugriff, und wird deshalb hier gestellt — nach der Wartezeit, auf
                // einem Hintergrundfaden und nur für das Bild, bei dem man stehen bleibt.
                //
                // Stimmt er, ist nichts zu tun: Angezeigt wird er schon.
                bool schraeg = OcrSchraegMitlesen && PluginOcrDienst.IstVerfuegbar;

                if (await Task.Run(() => _ocrCache.IstAktuell(pfad, schraeg), token).ConfigureAwait(true))
                {
                    return;
                }

                token.ThrowIfCancellationRequested();

                // Ab hier wird wirklich gelesen. Steht noch ein Text aus einem früheren
                // Stand der Datei in der Karte, gehört er nicht mehr dorthin.
                OcrVolltext = string.Empty;
                OcrVolltextKopf = schraeg
                    ? $"{name} — wird gelesen, mit schrägen Zeilen …"
                    : $"{name} — wird gelesen …";

                // Während der Ordnerlauf arbeitet, nicht dazwischenfunken: Er kommt
                // ohnehin an diesem Bild vorbei.
                if (OcrLaeuft)
                {
                    OcrVolltextKopf = $"{name} — noch nicht gelesen, der Ordnerlauf ist gerade dabei.";
                    return;
                }

                string? text = await LiesTextMitPluginAsync(pfad, schraeg, token).ConfigureAwait(true);
                token.ThrowIfCancellationRequested();

                if (text is null)
                {
                    OcrVolltext = string.Empty;
                    OcrVolltextKopf = $"{name} — nicht lesbar.";
                    return;
                }

                _ocrCache.Setze(pfad, text, OcrSprache, schraeg ? OcrCache.SchraegStufeVoll : 0);

                string? ordner = Path.GetDirectoryName(pfad);
                if (!string.IsNullOrEmpty(ordner))
                {
                    // Ohne Speichern wäre die Arbeit beim nächsten Start wieder weg.
                    // Fehlschlag ist hier kein Grund für eine Meldung — der Text steht
                    // trotzdem in der Karte.
                    _ocrCache.Speichern(ordner);
                }

                ZeigeOcrVolltext(name, text);
            }
            catch (OperationCanceledException)
            {
                // Das nächste Bild ist dran und hat die Anzeige längst übernommen.
            }
        }

        /// <summary>Text und Kopfzeile setzen — an einer Stelle, damit beide zusammenpassen.</summary>
        private void ZeigeOcrVolltext(string name, string text)
        {
            OcrVolltext = text;

            OcrVolltextKopf = text.Length == 0
                ? $"{name} — gelesen, aber kein Text im Bild."
                : $"{name} — {text.Length} Zeichen";
        }

        #endregion

        /// <summary>
        /// Schneidet den Fund samt Umgebung heraus, damit man in der Trefferliste sieht,
        /// in welchem Zusammenhang das Wort steht.
        /// </summary>
        private static string Fundstelle(string? text, string suche)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            int stelle = text.IndexOf(suche, StringComparison.OrdinalIgnoreCase);
            if (stelle < 0)
            {
                return string.Empty;
            }

            const int umfeld = 25;
            int von = Math.Max(0, stelle - umfeld);
            int bis = Math.Min(text.Length, stelle + suche.Length + umfeld);

            string ausschnitt = text.Substring(von, bis - von).Replace('\n', ' ').Replace('\r', ' ');

            return (von > 0 ? "… " : string.Empty)
                 + ausschnitt.Trim()
                 + (bis < text.Length ? " …" : string.Empty);
        }
    }
}
