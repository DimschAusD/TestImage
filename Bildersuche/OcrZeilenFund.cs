using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Eine gefundene Textzeile, aufbereitet für die Anzeige: der Rahmen zum Zeichnen,
    /// der geradegerückte Streifen zum Ansehen und der daraus gelesene Text.
    ///
    /// <b>Absichtlich ohne Typen des Vertrags.</b> Das Plugin liegt nicht im Repo, und
    /// diese Klasse muss auch ohne <c>OCR_PLUGIN</c> übersetzbar bleiben — sonst müsste
    /// jede Ansicht, die sie anfasst, ebenfalls bedingt kompiliert werden. Alles hier
    /// sind WPF-Typen; die Umrechnung macht <see cref="PluginOcrDienst"/>.
    /// </summary>
    internal sealed partial class OcrZeilenFund : ObservableObject
    {
        /// <summary>Laufende Nummer, wie sie in der Liste steht — ab 1, je Ebene eigen.</summary>
        public required int Nummer { get; init; }

        /// <summary>
        /// Wer den Fund geliefert hat: „Windows-OCR" für die Standardsuche über das
        /// ganze Bild, „Plugin" für die Zeilensuche in beliebiger Neigung. Steht über
        /// dem Streifen, damit dort nie unklar ist, welche Ebene man ansieht.
        /// </summary>
        public required string Herkunft { get; init; }

        /// <summary>
        /// Was die Texterkennung aus dem Streifen gelesen hat. Leer heisst: Zeile
        /// gefunden, aber nichts darin gelesen — meist ein Wegrand, ein Gitter oder
        /// eine Signatur, die wie Schrift aussieht.
        /// </summary>
        public required string Text { get; init; }

        /// <summary>
        /// Die vier Ecken des gedrehten Rechtecks, in Bildpunkten des Bildes, das die
        /// Erkennung gesehen hat. Eingefroren, weil sie auf einem eigenen Faden
        /// entstehen und danach im Anzeige-Faden gezeichnet werden.
        /// </summary>
        public required PointCollection Ecken { get; init; }

        /// <summary>
        /// Der geradegerückte, vergrösserte Streifen — genau das Bild, das die
        /// Texterkennung zu lesen bekam. <b>Daran</b> sieht man, warum ein Wort falsch
        /// gelesen wurde; am Rahmen im Gesamtbild ist das nicht zu erkennen.
        /// </summary>
        public required BitmapSource Streifen { get; init; }

        /// <summary>Neigung in Grad.</summary>
        public required double Winkel { get; init; }

        /// <summary>Anzahl der Zeichen-Kandidaten, aus denen die Zeile gebildet wurde.</summary>
        public required int Zeichen { get; init; }

        /// <summary>Länge der Zeile entlang ihrer Richtung, in Bildpunkten.</summary>
        public required double Laenge { get; init; }

        /// <summary>Mittlere Zeichenhöhe — das Maß für die Schriftgrösse.</summary>
        public required double MittlereHoehe { get; init; }

        /// <summary>
        /// Wie sicher das Plugin beim Lesen war, 0..1 — nur wenn es selbst gelesen hat
        /// (ab Fassung 0.0.4). <c>null</c> bei der Windows-OCR, die keine Sicherheit angibt.
        /// </summary>
        public float? Sicherheit { get; init; }

        /// <summary>
        /// True, wenn das Plugin die Lesung verwirft: zu unsicher oder zu wenige
        /// Buchstaben und Ziffern — meist Holzmaserung, Symbole oder fremde Schrift.
        /// Die Zeile bleibt in der Liste stehen, damit die Nummern zu den Rahmen
        /// passen, zählt aber nicht als gelesener Text.
        /// </summary>
        public bool IstScheinzeile { get; init; }

        /// <summary>True, wenn wirklich etwas gelesen wurde.</summary>
        public bool HatText => Text.Length > 0;

        /// <summary>
        /// True, wenn die Zeile als gelesener Text zählt. Ordnerlauf und Rahmenansicht
        /// fragen beide hier, damit sie gleich zählen.
        /// </summary>
        public bool ZaehltAlsText => HatText && !IstScheinzeile;

        /// <summary>Die Kennzahlen der Zeile in einer Zeile, für die Liste.</summary>
        public string Kennzahlen => string.Create(
            CultureInfo.CurrentCulture,
            $"{Winkel:F1}°   {Zeichen} Zeichen   {Laenge:F0} × {MittlereHoehe:F0} px")
            + (Sicherheit is float s ? string.Create(CultureInfo.CurrentCulture, $"   Sicherheit {s:P0}") : string.Empty)
            + (IstScheinzeile ? "   verworfen" : string.Empty);

        /// <summary>
        /// Dasselbe mit der Nummer davor — für die Kopfzeile über dem Streifen, wo die
        /// Nummer nicht wie in der Liste schon daneben steht.
        /// </summary>
        public string KennzahlenMitNummer => $"{Herkunft}   Nr. {Nummer}   {Kennzahlen}";

        /// <summary>
        /// True, solange die Maus auf dieser Zeile steht — in der Liste oder auf ihrem
        /// Rahmen im Bild. Der Rahmen wird dann hervorgehoben.
        ///
        /// Das Merkmal sitzt am Fund und nicht an der Ansicht, weil zwei Stellen es
        /// zugleich brauchen: die Liste rechts und das Polygon über dem Bild. Ein
        /// WPF-Trigger kann nur sein eigenes Element umstellen, nicht das Gegenstück in
        /// einer anderen Liste.
        /// </summary>
        [ObservableProperty]
        public partial bool IstHervorgehoben { get; set; }
    }

    /// <summary>
    /// Das Ergebnis eines Suchlaufs über ein Bild: alle gefundenen Zeilen und das Bild,
    /// zu dem ihre Koordinaten gehören.
    ///
    /// <b>Das Bild wird mitgegeben, nicht neu geladen.</b> Die Ecken sind Bildpunkte
    /// genau dieser Bitmap. Wer für die Anzeige die Datei ein zweites Mal lädt, bekommt
    /// bei gedrehtem EXIF oder anderer Skalierung Rahmen, die neben dem Text sitzen.
    /// </summary>
    internal sealed class OcrZeilenBefund
    {
        /// <summary>Das Bild, das die Erkennung gesehen hat.</summary>
        public required BitmapSource Bild { get; init; }

        /// <summary>Die gefundenen Zeilen, längste zuerst — auch die ohne gelesenen Text.</summary>
        public required IReadOnlyList<OcrZeilenFund> Zeilen { get; init; }

        /// <summary>Wie lange der Lauf gedauert hat.</summary>
        public required TimeSpan Dauer { get; init; }

        /// <summary>
        /// Strichstärke für die Rahmen, in Bildpunkten.
        ///
        /// Gezeichnet wird in Bildkoordinaten und das Ganze anschliessend eingepasst —
        /// ein fester Wert wäre deshalb bei einem kleinen Bild ein Klotz und bei einem
        /// grossen unsichtbar. Ausgerichtet an der Bildbreite, mindestens 1,5.
        /// </summary>
        public double Strichstaerke => Math.Max(1.5, Bild.PixelWidth / 700.0);
    }
}
