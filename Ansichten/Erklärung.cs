using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TestImage.Ansichten
{
    /// <summary>
    /// Erklärungen zum Element unter der Maus, angezeigt in einem festen Feld statt als
    /// Tooltip.
    ///
    /// <b>Warum kein Tooltip.</b> In der Vollbildansicht liegt die Maus ständig auf
    /// irgendetwas — auf den breiten Blätterzonen, auf dem Bild. Jeder Tooltip sprang
    /// dort nach kurzer Zeit über das Bild. Wer die Bedienung kennt, braucht ihn nie,
    /// und wer sie nicht kennt, will ihn nicht erst durch Stillhalten hervorlocken.
    ///
    /// <b>Wie es zusammenhängt.</b> <see cref="TextProperty"/>, <see cref="TitelProperty"/>
    /// und <see cref="TasteProperty"/> vererben sich: Ein Element ohne eigene Erklärung
    /// übernimmt die des Elternelements, die Wurzel trägt den Grundtext. So gibt es keine
    /// Stelle ohne Erklärung, und der Pfeil in einer Blätterzone muss nicht eigens
    /// beschriftet werden. Wer eine eigene Erklärung setzt, setzt deshalb immer Titel
    /// und Text zusammen, sonst erbte er den Titel des Elternelements.
    ///
    /// An der Wurzel nennt <see cref="AnzeigeProperty"/> das Feld. Bei jeder
    /// Mausbewegung wird das Element unter der Maus als <see cref="QuelleProperty"/> an
    /// das Feld gehängt; dessen Texte binden an die Erklärung dieser Quelle. Weil es eine
    /// Bindung ist und keine Kopie, laufen sich ändernde Texte live mit — etwa der
    /// Bluetooth-Stand oder der Bildtext beim Vergrössern.
    ///
    /// Liegt die Maus im Feld selbst, bleibt die Quelle stehen: Wer darin scrollt, will
    /// lesen, was dort steht, und nicht die Erklärung des Feldes.
    ///
    /// <b>Der Weg zum Feld.</b> Vom Bluetooth-Zeichen oben rechts führt jeder Weg zum
    /// Feld über das Bild oder die Eigenschaften-Liste, und die hätten die Quelle
    /// unterwegs überschrieben — der lange Text liess sich nie erreichen. Deshalb hält
    /// die Quelle auch, solange die Maus auf das Feld zuläuft (wie bei Untermenüs, die
    /// man schräg ansteuert). Biegt sie ab oder bleibt sie unterwegs kurz stehen, gilt
    /// wieder das Element unter ihr.
    /// </summary>
    public static class Erklärung
    {
        #region Die Erklärung selbst (vererbend)

        public static readonly DependencyProperty TitelProperty = DependencyProperty.RegisterAttached(
            "Titel", typeof(string), typeof(Erklärung),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

        public static string? GetTitel(DependencyObject element) => (string?)element.GetValue(TitelProperty);

        public static void SetTitel(DependencyObject element, string? wert) => element.SetValue(TitelProperty, wert);

        public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(Erklärung),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

        public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

        public static void SetText(DependencyObject element, string? wert) => element.SetValue(TextProperty, wert);

        /// <summary>Taste, die dasselbe tut — erscheint als Tastenkappe neben dem Titel.</summary>
        public static readonly DependencyProperty TasteProperty = DependencyProperty.RegisterAttached(
            "Taste", typeof(string), typeof(Erklärung),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

        public static string? GetTaste(DependencyObject element) => (string?)element.GetValue(TasteProperty);

        public static void SetTaste(DependencyObject element, string? wert) => element.SetValue(TasteProperty, wert);

        /// <summary>
        /// Welche Maus-Bedienung hier gilt, als Wörter: <c>Links</c>, <c>Rad</c>,
        /// <c>Rechts</c>, auch mehrere durch Leerzeichen getrennt. Das Erklärfeld zeigt je
        /// Wort das passende Maussymbol. Ein leerer Text hebt die geerbte Angabe auf — für
        /// Stellen, an denen die Maus nichts tut, etwa die Indikatoren.
        /// </summary>
        public static readonly DependencyProperty MausProperty = DependencyProperty.RegisterAttached(
            "Maus", typeof(string), typeof(Erklärung),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

        public static string? GetMaus(DependencyObject element) => (string?)element.GetValue(MausProperty);

        public static void SetMaus(DependencyObject element, string? wert) => element.SetValue(MausProperty, wert);

        #endregion

        #region Anzeige (an der Wurzel) und Quelle (am Feld)

        /// <summary>
        /// Das Erklärfeld. Gesetzt an der Wurzel des Bereichs, dessen Elemente erklärt
        /// werden; hängt dort den Mausbeobachter an.
        /// </summary>
        public static readonly DependencyProperty AnzeigeProperty = DependencyProperty.RegisterAttached(
            "Anzeige", typeof(FrameworkElement), typeof(Erklärung),
            new PropertyMetadata(null, AnzeigeGeändert));

        public static FrameworkElement? GetAnzeige(DependencyObject element) =>
            (FrameworkElement?)element.GetValue(AnzeigeProperty);

        public static void SetAnzeige(DependencyObject element, FrameworkElement? wert) =>
            element.SetValue(AnzeigeProperty, wert);

        /// <summary>
        /// Das Element, dessen Erklärung das Feld gerade zeigt. Gesetzt am Feld; dessen
        /// Texte binden an <c>(Erklärung.Quelle).(Erklärung.Text)</c>.
        /// </summary>
        public static readonly DependencyProperty QuelleProperty = DependencyProperty.RegisterAttached(
            "Quelle", typeof(DependencyObject), typeof(Erklärung), new PropertyMetadata(null));

        public static DependencyObject? GetQuelle(DependencyObject element) =>
            (DependencyObject?)element.GetValue(QuelleProperty);

        public static void SetQuelle(DependencyObject element, DependencyObject? wert) =>
            element.SetValue(QuelleProperty, wert);

        /// <summary>
        /// Tooltips unterdrücken, solange das Feld sie ersetzt. Gesetzt an der Wurzel,
        /// gebunden an die Sichtbarkeit des Feldinhalts: Ist das Feld zu, erscheinen die
        /// Tooltips wie bisher.
        /// </summary>
        public static readonly DependencyProperty PopupsAusProperty = DependencyProperty.RegisterAttached(
            "PopupsAus", typeof(bool), typeof(Erklärung), new PropertyMetadata(false));

        public static bool GetPopupsAus(DependencyObject element) => (bool)element.GetValue(PopupsAusProperty);

        public static void SetPopupsAus(DependencyObject element, bool wert) => element.SetValue(PopupsAusProperty, wert);

        #endregion

        #region Verhalten

        private static bool _tooltipBremseAngemeldet;

        private static void AnzeigeGeändert(DependencyObject wurzel, DependencyPropertyChangedEventArgs e)
        {
            if (wurzel is not UIElement element)
                return;

            element.PreviewMouseMove -= WurzelMausBewegt;
            if (e.NewValue is not null)
                element.PreviewMouseMove += WurzelMausBewegt;

            // Einmal für alle Elemente: ToolTipOpening ist ein direktes Ereignis und
            // steigt nicht zur Wurzel auf, deshalb ein Klassenhandler statt eines
            // Handlers an der Wurzel.
            if (!_tooltipBremseAngemeldet)
            {
                EventManager.RegisterClassHandler(
                    typeof(FrameworkElement), FrameworkElement.ToolTipOpeningEvent,
                    new ToolTipEventHandler(TooltipÖffnet));
                _tooltipBremseAngemeldet = true;
            }
        }

        private static void WurzelMausBewegt(object sender, MouseEventArgs e)
        {
            if (sender is not UIElement wurzel || e.OriginalSource is not DependencyObject quelle)
                return;

            var anzeige = GetAnzeige(wurzel);
            if (anzeige is null)
                return;

            var anflug = AnflugVon(wurzel);
            anflug.Zielt = ZieltAufFeld(wurzel, anzeige, anflug, e.GetPosition(wurzel));

            if (IstInnerhalb(quelle, anzeige) || ReferenceEquals(GetQuelle(anzeige), quelle))
            {
                anflug.Verwerfen();
                return;
            }

            if (anflug.Zielt)
            {
                anflug.Warten(quelle);
                return;
            }

            anflug.Verwerfen();
            SetQuelle(anzeige, quelle);
        }

        /// <summary>Weg, ab dem die Richtung neu bestimmt wird; kürzere Schritte der Maus
        /// kommen nur als Pixeltreppe an und zeigten mal senkrecht, mal waagrecht.</summary>
        private const double AnflugSchritt = 8;

        /// <summary>Spielraum um das Feld: Von oben gesehen ist es schmal, und niemand
        /// zielt aus der Ecke gegenüber auf den Pixel genau.</summary>
        private const double AnflugSpielraum = 24;

        /// <summary>So lange darf die Maus unterwegs stehen, ohne dass die Quelle wechselt.</summary>
        private static readonly System.TimeSpan AnflugGeduld = System.TimeSpan.FromMilliseconds(300);

        private static readonly DependencyProperty AnflugProperty = DependencyProperty.RegisterAttached(
            "Anflug", typeof(Anflug), typeof(Erklärung), new PropertyMetadata(null));

        private static Anflug AnflugVon(UIElement wurzel)
        {
            if (wurzel.GetValue(AnflugProperty) is Anflug vorhanden)
                return vorhanden;

            var anflug = new Anflug(wurzel);
            wurzel.SetValue(AnflugProperty, anflug);
            return anflug;
        }

        /// <summary>
        /// Läuft die Maus auf das Feld zu? Gemessen wird die Richtung der letzten
        /// <see cref="AnflugSchritt"/> Pixel: Trifft ihre Verlängerung das Feld, zielt sie.
        /// Zugeklappt oder ausgeblendet gibt es im Feld nichts zu lesen — dann nie.
        /// </summary>
        private static bool ZieltAufFeld(UIElement wurzel, FrameworkElement anzeige, Anflug anflug, Point position)
        {
            // PopupsAus ist an die Sichtbarkeit des Feldinhalts gebunden und damit genau
            // „im Feld steht gerade etwas zu lesen".
            if (!GetPopupsAus(wurzel) || anflug.Bezug is not Point bezug)
            {
                anflug.Bezug = position;
                return false;
            }

            var weg = position - bezug;
            if (weg.Length < AnflugSchritt)
                return anflug.Zielt;

            anflug.Bezug = position;
            var feld = anzeige.TransformToAncestor(wurzel).TransformBounds(new Rect(anzeige.RenderSize));
            feld.Inflate(AnflugSpielraum, AnflugSpielraum);
            return StrahlTrifft(bezug, weg, feld);
        }

        /// <summary>Trifft der Strahl ab <paramref name="start"/> in <paramref name="richtung"/>
        /// das Rechteck? Nur vorwärts — wer sich entfernt, zielt nicht.</summary>
        private static bool StrahlTrifft(Point start, Vector richtung, Rect feld)
        {
            double von = 0, bis = double.PositiveInfinity;
            return Streifen(start.X, richtung.X, feld.Left, feld.Right, ref von, ref bis)
                && Streifen(start.Y, richtung.Y, feld.Top, feld.Bottom, ref von, ref bis);
        }

        /// <summary>Engt den Strahlabschnitt auf den Teil zwischen zwei parallelen Kanten ein.</summary>
        private static bool Streifen(double start, double richtung, double kanteA, double kanteB, ref double von, ref double bis)
        {
            if (System.Math.Abs(richtung) < 1e-9)
                return start >= kanteA && start <= kanteB;

            double tA = (kanteA - start) / richtung;
            double tB = (kanteB - start) / richtung;
            von = System.Math.Max(von, System.Math.Min(tA, tB));
            bis = System.Math.Min(bis, System.Math.Max(tA, tB));
            return von <= bis;
        }

        /// <summary>
        /// Zustand des Anflugs je Wurzel: letzter Bezugspunkt, ob die Maus zielt, und das
        /// Element, das die Quelle würde, wenn die Maus unterwegs stehen bleibt.
        /// </summary>
        private sealed class Anflug
        {
            private readonly UIElement _wurzel;
            private readonly System.Windows.Threading.DispatcherTimer _uhr;
            private DependencyObject? _wartend;

            public Point? Bezug;
            public bool Zielt;

            public Anflug(UIElement wurzel)
            {
                _wurzel = wurzel;
                _uhr = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Input, wurzel.Dispatcher)
                {
                    Interval = AnflugGeduld,
                };
                _uhr.Tick += UhrAbgelaufen;
            }

            /// <summary>Quelle vormerken; wechselt erst, wenn die Maus so lange steht.</summary>
            public void Warten(DependencyObject quelle)
            {
                _wartend = quelle;
                _uhr.Stop();
                _uhr.Start();
            }

            public void Verwerfen()
            {
                _wartend = null;
                _uhr.Stop();
            }

            private void UhrAbgelaufen(object? sender, System.EventArgs e)
            {
                var quelle = _wartend;
                Verwerfen();
                if (quelle is not null && GetAnzeige(_wurzel) is { } anzeige)
                    SetQuelle(anzeige, quelle);
            }
        }

        /// <summary>
        /// Unterdrückt den Tooltip eines Elements, das selbst eine Erklärung trägt, solange
        /// das Feld sie zeigt. Elemente ohne eigene Erklärung behalten ihren Tooltip —
        /// etwa der volle Ordnerpfad im Eigenschaften-Feld; er fiele sonst ersatzlos weg.
        /// </summary>
        private static void TooltipÖffnet(object sender, ToolTipEventArgs e)
        {
            if (sender is not FrameworkElement element
                || element.ReadLocalValue(TextProperty) == DependencyProperty.UnsetValue)
            {
                return;
            }

            for (DependencyObject? d = element; d is not null; d = Elternteil(d))
            {
                if (GetAnzeige(d) is not null)
                {
                    if (GetPopupsAus(d))
                        e.Handled = true;
                    return;
                }
            }
        }

        private static bool IstInnerhalb(DependencyObject element, DependencyObject bereich)
        {
            for (DependencyObject? d = element; d is not null; d = Elternteil(d))
            {
                if (ReferenceEquals(d, bereich))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Visueller Baum, wo es einen gibt; sonst der logische — ein Run in einem
        /// TextBlock ist kein Visual und hat nur einen logischen Elternteil.
        /// </summary>
        private static DependencyObject? Elternteil(DependencyObject d) =>
            d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);

        #endregion
    }

    /// <summary>
    /// Zerlegt <see cref="Erklärung.TasteProperty"/> in einzelne Tasten, etwa „← →" in
    /// zwei. Jede wird im Erklärfeld eine eigene Tastenkappe (Vorlage DT_Taste).
    /// </summary>
    public sealed class ErklärungTasten : System.Windows.Data.IValueConverter
    {
        public object Convert(object? wert, System.Type zielTyp, object? parameter, System.Globalization.CultureInfo kultur) =>
            wert is string angabe
                ? angabe.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
                : System.Array.Empty<string>();

        public object ConvertBack(object? wert, System.Type zielTyp, object? parameter, System.Globalization.CultureInfo kultur) =>
            throw new System.NotSupportedException();
    }

    /// <summary>
    /// Sichtbar, wenn die Maus-Angabe (<see cref="Erklärung.MausProperty"/>) das Wort aus
    /// dem ConverterParameter enthält. Eigener Umsetzer, weil ein DataTrigger nur auf
    /// Gleichheit prüfen kann und die Angabe mehrere Wörter tragen darf.
    /// </summary>
    public sealed class ErklärungMausZeigt : System.Windows.Data.IValueConverter
    {
        public object Convert(object? wert, System.Type zielTyp, object? parameter, System.Globalization.CultureInfo kultur) =>
            wert is string angabe
            && parameter is string wort
            && angabe.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Contains(wort)
                ? Visibility.Visible
                : Visibility.Collapsed;

        public object ConvertBack(object? wert, System.Type zielTyp, object? parameter, System.Globalization.CultureInfo kultur) =>
            throw new System.NotSupportedException();
    }
}
