using System.Text.RegularExpressions;

namespace ImageMatching.Cnn;

/// <summary>
/// Leichter, offline arbeitender Wörterbuch-Übersetzer Deutsch→Englisch für
/// Suchbegriffe. Deckt gängige Bild-Suchwörter ab (Farben, Orte, Personen,
/// Objekte, Tätigkeiten). Mehrwort-Ausdrücke werden bevorzugt (längster Treffer);
/// unbekannte Wörter bleiben unverändert – CLIP verkraftet das gut, da es vor
/// allem auf Inhaltswörter reagiert. Für beliebige Sätze wäre später ein
/// neuronales Modell (OPUS-MT) der nächste Ausbauschritt.
/// </summary>
public sealed class GermanQueryTranslator
{
    private readonly Dictionary<string, string> _map;

    public GermanQueryTranslator() => _map = Build();

    /// <summary>
    /// Füllwörter, die nicht als „nicht übersetzt" gemeldet werden.
    ///
    /// Sie stehen absichtlich nicht im Wörterbuch — CLIP braucht sie nicht, und ihre
    /// Auslassung ist kein Mangel. Ohne diese Liste bestünde jede Meldung zur Hälfte
    /// aus „der", „am", „mit" und wäre nach dreimal Lesen nur noch Rauschen.
    /// </summary>
    private static readonly HashSet<string> Fuellwoerter = new(StringComparer.OrdinalIgnoreCase)
    {
        "der", "die", "das", "den", "dem", "des", "ein", "eine", "einen", "einem", "einer", "eines",
        "am", "an", "auf", "aus", "bei", "im", "in", "mit", "nach", "über", "unter", "von", "vom",
        "vor", "zu", "zum", "zur", "und", "oder", "ist", "sind", "sich", "als", "wie", "es"
    };

    /// <summary>Übersetzt eine deutsche Suchanfrage nach Englisch (best effort).</summary>
    public string Translate(string german) => Translate(german, out _);

    /// <summary>
    /// Wie <see cref="Translate(string)"/>, meldet aber zusätzlich die Wörter, für die
    /// es keine Übersetzung gab.
    ///
    /// <b>Warum das gebraucht wird:</b> Ein unbekanntes Wort bleibt deutsch stehen und
    /// geht so in den englischen Text-Encoder von CLIP. Solange noch ein bekanntes Wort
    /// in der Anfrage steckt, fällt das nicht auf — „steine am strand" fand Bilder, aber
    /// allein wegen „at the beach". Steht das unbekannte Wort für sich, findet die Suche
    /// nichts, und niemand kann unterscheiden, ob das Bild fehlt oder das Wort.
    ///
    /// Füllwörter werden nicht gemeldet, siehe <see cref="Fuellwoerter"/>.
    /// </summary>
    public string Translate(string german, out IReadOnlyList<string> nichtUebersetzt)
    {
        var unbekannt = new List<string>();
        nichtUebersetzt = unbekannt;

        if (string.IsNullOrWhiteSpace(german)) return german;

        string[] tokens = Regex.Split(german.ToLowerInvariant().Trim(), @"\s+")
            .Select(t => t.Trim('.', ',', ';', ':', '!', '?', '"', '\''))
            .Where(t => t.Length > 0)
            .ToArray();

        var output = new List<string>();
        int i = 0;
        while (i < tokens.Length)
        {
            string? replacement = null;
            int used = 1;

            // Längster Treffer zuerst: 3er-, 2er-, dann 1er-Ausdruck.
            for (int n = Math.Min(3, tokens.Length - i); n >= 1; n--)
            {
                string phrase = string.Join(' ', tokens, i, n);
                if (_map.TryGetValue(phrase, out string? r))
                {
                    replacement = r;
                    used = n;
                    break;
                }
            }

            // Kein direkter Treffer -> morphologischer Fallback für das Einzelwort.
            replacement ??= Resolve(tokens[i]);

            if (replacement is null && !Fuellwoerter.Contains(tokens[i]))
            {
                unbekannt.Add(tokens[i]);
            }

            output.Add(replacement ?? tokens[i]); // weiterhin unbekannt -> unverändert
            i += used;
        }

        return string.Join(' ', output);
    }

    private static readonly string[] PluralEndings = { "en", "er", "e", "n", "s" };

    /// <summary>
    /// Sucht ein Wort auch in gebeugter Form: probiert Umlaut-Auflösung
    /// (ä→a, ö→o, ü→u, ß→ss) und gängige Plural-Endungen, um die Grundform
    /// im Wörterbuch zu finden. Liefert die Übersetzung oder null.
    /// </summary>
    private string? Resolve(string word)
    {
        if (_map.TryGetValue(word, out string? r)) return r;

        string deumlaut = Deumlaut(word);
        if (deumlaut != word && _map.TryGetValue(deumlaut, out r)) return r;

        foreach (string end in PluralEndings)
        {
            if (word.Length <= end.Length + 2 || !word.EndsWith(end, StringComparison.Ordinal)) continue;

            string stem = word[..^end.Length];
            if (_map.TryGetValue(stem, out r)) return r;         // z. B. hunde -> hund

            string stemDe = Deumlaut(stem);
            if (stemDe != stem && _map.TryGetValue(stemDe, out r)) return r; // z. B. bäume -> baum
        }
        return null;
    }

    private static string Deumlaut(string w)
        => w.Replace("ä", "a").Replace("ö", "o").Replace("ü", "u").Replace("ß", "ss");

    private static Dictionary<string, string> Build()
    {
        var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string en, params string[] de) { foreach (var d in de) m[d] = en; }

        // Mehrwort-Ausdrücke (haben Vorrang durch längsten Treffer)
        Add("red hair", "rote haare", "rotes haar");
        Add("blonde hair", "blonde haare", "blondes haar");
        Add("black hair", "schwarze haare", "schwarzes haar");
        Add("brown hair", "braune haare", "braunes haar");
        Add("long hair", "lange haare");
        Add("short hair", "kurze haare");
        Add("two people", "zwei personen", "zwei menschen", "zwei leute");
        Add("one person", "eine person");
        Add("full body", "ganzer körper", "ganzer koerper");
        Add("close-up", "nahaufnahme");
        Add("at the beach", "am strand");
        Add("in the city", "in der stadt");
        Add("in the forest", "im wald");
        Add("in a room", "in einem raum", "in einem zimmer");
        Add("outdoors", "im freien", "in der natur");
        Add("in the sky", "am himmel");
        Add("on a table", "auf einem tisch");

        // Farben
        Add("red", "rot", "rote", "roter", "rotes");
        Add("blue", "blau", "blaue", "blauer", "blaues");
        Add("green", "grün", "grüne", "grüner", "grünes");
        Add("yellow", "gelb", "gelbe");
        Add("black", "schwarz", "schwarze", "schwarzer", "schwarzes");
        Add("white", "weiß", "weiss", "weiße", "weisse");
        Add("brown", "braun", "braune", "brauner", "braunes");
        Add("blonde", "blond", "blonde", "blonder", "blondes");
        Add("gray", "grau", "graue");
        Add("pink", "rosa", "pink");
        Add("purple", "lila", "violett");
        Add("orange", "orange");
        Add("colorful", "bunt", "bunte", "farbig");
        Add("dark", "dunkel", "dunkle", "dunkler");
        Add("bright", "hell", "helle", "heller");

        // Personen
        Add("girl", "mädchen", "maedchen");
        Add("boy", "junge");
        Add("woman", "frau");
        Add("man", "mann");
        Add("person", "person");
        Add("people", "menschen", "leute", "personen");
        Add("child", "kind");
        Add("children", "kinder");
        Add("couple", "paar");
        Add("group", "gruppe");
        Add("face", "gesicht");
        Add("hair", "haare", "haar");

        // Orte / Szenen
        Add("indoors", "drinnen", "innen", "innenraum");
        Add("outdoors", "draußen", "draussen", "außen", "aussen");
        Add("forest", "wald");
        Add("beach", "strand");
        Add("city", "stadt");
        Add("mountains", "berge", "berg");
        Add("sky", "himmel");
        Add("sea", "meer");
        Add("lake", "see");
        Add("river", "fluss");
        Add("street", "straße", "strasse");
        Add("room", "zimmer", "raum");
        Add("kitchen", "küche", "kueche");
        Add("garden", "garten");
        Add("park", "park");
        Add("school", "schule");
        Add("night", "nacht");
        Add("day", "tag");
        Add("sunset", "sonnenuntergang");
        Add("snow", "schnee");
        Add("rain", "regen");
        Add("water", "wasser");

        // Objekte / Tiere / Kleidung
        Add("car", "auto");
        Add("house", "haus");
        Add("tree", "baum");
        Add("trees", "bäume", "baeume");
        Add("flower", "blume");
        Add("flowers", "blumen");
        Add("book", "buch");
        Add("window", "fenster");
        Add("door", "tür", "tuer");
        Add("food", "essen");
        Add("photo", "foto", "bild", "bilder");
        Add("swords", "schwerter", "schwertern");
        Add("table", "tisch");
        Add("chair", "stuhl");
        Add("sword", "schwert");
        Add("weapon", "waffe");
        Add("dog", "hund");
        Add("cat", "katze");
        Add("bird", "vogel");
        Add("horse", "pferd");
        Add("dragon", "drache", "drachen");
        Add("animal", "tier");
        Add("dress", "kleid");
        Add("shirt", "hemd");
        Add("hat", "hut");
        Add("uniform", "uniform");
        Add("glasses", "brille");
        Add("suit", "anzug");

        // Tätigkeiten / Zustände
        Add("smiling", "lächeln", "lächelnd", "laechelnd", "lächelnde", "lächelndes", "lächelnder");
        Add("laughing", "lachen", "lachend");
        Add("crying", "weinen", "weinend");
        Add("sitting", "sitzend", "sitzt");
        Add("standing", "stehend", "steht");
        Add("running", "laufend", "rennend");
        Add("sleeping", "schlafend", "schläft");
        Add("fighting", "kämpfen", "kampf", "kämpfend");
        Add("dancing", "tanzen", "tanzend");
        Add("portrait", "porträt", "portrait");
        Add("close-up", "close up");

        // Zahlen / Mengen
        Add("one", "eins");
        Add("two", "zwei");
        Add("three", "drei");
        Add("many", "viele");
        Add("several", "mehrere");

        // Adjektive
        Add("big", "groß", "gross", "große", "grosse");
        Add("small", "klein", "kleine", "kleiner");
        Add("old", "alt", "alte", "alter");
        Add("young", "jung", "junge", "junger");
        Add("beautiful", "schön", "schoen", "schöne");
        Add("pretty", "hübsch", "huebsch");
        Add("happy", "glücklich", "gluecklich");
        Add("sad", "traurig");
        Add("angry", "wütend", "wuetend");
        Add("cute", "süß", "suess", "niedlich");

        // Verbindungswörter
        Add("with", "mit");
        Add("without", "ohne");
        Add("and", "und");
        Add("in", "im", "in");
        Add("on", "auf");
        Add("at", "am", "an");
        Add("in front of", "vor");
        Add("behind", "hinter");
        Add("next to", "neben");
        Add("of", "von", "vom");
        Add("under", "unter");
        Add("above", "über");
        Add("between", "zwischen");
        Add("near", "nahe");
        Add("inside", "innerhalb");
        Add("outside", "außerhalb");
        Add("wearing", "trägt", "tragend");
        Add("a", "der", "die", "das", "dem", "den", "des", "ein", "eine", "einen", "einem", "eines", "einer");

        // ---------- Erweiterter Wortschatz ----------

        // Mehrwort-Ausdrücke (Vorrang)
        Add("black and white", "schwarz weiß", "schwarz-weiß", "schwarzweiß");
        Add("from behind", "von hinten");
        Add("from above", "von oben");
        Add("from below", "von unten");
        Add("side view", "von der seite", "seitenansicht");
        Add("in the sky", "am himmel");
        Add("at the sea", "am meer");
        Add("in the snow", "im schnee");
        Add("at sunset", "bei sonnenuntergang");
        Add("cherry blossom", "kirschblüte", "kirschblüten");
        Add("ice cream", "eiscreme", "eis creme");

        // Weitere Farben
        Add("turquoise", "türkis");
        Add("beige", "beige");
        Add("gold", "gold", "golden", "goldene", "goldenes");
        Add("silver", "silber", "silbern", "silberne");
        Add("light blue", "hellblau");
        Add("dark blue", "dunkelblau");

        // Personen / Rollen
        Add("teenager", "teenager");
        Add("baby", "baby");
        Add("lady", "dame");
        Add("guy", "kerl", "typ");
        Add("soldier", "soldat");
        Add("warrior", "krieger");
        Add("knight", "ritter");
        Add("king", "könig");
        Add("queen", "königin");
        Add("princess", "prinzessin");
        Add("prince", "prinz");
        Add("hero", "held");
        Add("witch", "hexe");
        Add("angel", "engel");
        Add("demon", "dämon", "teufel");
        Add("robot", "roboter");
        Add("ninja", "ninja");
        Add("samurai", "samurai");
        Add("pirate", "pirat");
        Add("monster", "monster", "ungeheuer");
        Add("crowd", "menge", "menschenmenge");
        Add("twins", "zwillinge");

        // Körper / Aussehen
        Add("eyes", "augen");
        Add("eye", "auge");
        Add("wings", "flügel", "flügeln");
        Add("tail", "schwanz");
        Add("ears", "ohren");
        Add("beard", "bart");
        Add("tattoo", "tattoo", "tätowierung");
        Add("muscular", "muskulös");

        // Kleidung
        Add("bikini", "bikini");
        Add("swimsuit", "badeanzug");
        Add("kimono", "kimono");
        Add("armor", "rüstung");
        Add("cape", "umhang");
        Add("boots", "stiefel");
        Add("gloves", "handschuhe");
        Add("scarf", "schal");
        Add("mask", "maske");
        Add("crown", "krone");
        Add("tie", "krawatte");
        Add("skirt", "rock");
        Add("pants", "hose");
        Add("sweater", "pullover");
        Add("coat", "mantel");
        Add("jacket", "jacke");
        Add("shoes", "schuhe");
        Add("cap", "mütze");
        Add("helmet", "helm");

        // Gefühle / Ausdruck
        Add("surprised", "überrascht");
        Add("scared", "ängstlich", "verängstigt");
        Add("shy", "schüchtern");
        Add("serious", "ernst");
        Add("bored", "gelangweilt");
        Add("embarrassed", "verlegen");
        Add("confused", "verwirrt");

        // Szenen / Orte
        Add("bedroom", "schlafzimmer");
        Add("bathroom", "badezimmer", "bad");
        Add("office", "büro");
        Add("classroom", "klassenzimmer");
        Add("library", "bibliothek");
        Add("restaurant", "restaurant");
        Add("cafe", "café", "cafe");
        Add("temple", "tempel");
        Add("castle", "schloss", "burg");
        Add("church", "kirche");
        Add("hospital", "krankenhaus");
        Add("train", "zug");
        Add("spaceship", "raumschiff");
        Add("desert", "wüste");
        Add("jungle", "dschungel");
        Add("cave", "höhle");
        Add("waterfall", "wasserfall");
        Add("bridge", "brücke");
        Add("rooftop", "dach");
        Add("battlefield", "schlachtfeld");
        Add("sunrise", "sonnenaufgang");
        Add("dusk", "dämmerung");
        Add("sun", "sonne");
        Add("moon", "mond");
        Add("star", "stern");
        Add("stars", "sterne");
        Add("cloud", "wolke");
        Add("clouds", "wolken");
        Add("fire", "feuer");
        Add("explosion", "explosion");
        Add("blood", "blut");
        Add("smoke", "rauch");

        // Wetter / Zeit
        Add("winter", "winter");
        Add("summer", "sommer");
        Add("spring", "frühling");
        Add("autumn", "herbst");
        Add("fog", "nebel");
        Add("storm", "sturm");
        Add("wind", "wind");
        Add("sunny", "sonnig");
        Add("cloudy", "bewölkt");
        Add("ice", "eis");
        Add("lightning", "blitz");

        // Objekte
        Add("phone", "telefon", "handy", "smartphone");
        Add("computer", "computer");
        Add("guitar", "gitarre");
        Add("piano", "klavier");
        Add("camera", "kamera");
        Add("umbrella", "regenschirm");
        Add("bag", "tasche");
        Add("ball", "ball");
        Add("clock", "uhr");
        Add("mirror", "spiegel");
        Add("candle", "kerze");
        Add("lamp", "lampe");
        Add("gun", "gewehr", "pistole");
        Add("knife", "messer");
        Add("shield", "schild");
        Add("bow", "bogen");
        Add("staff", "stab");
        Add("magic", "magie", "zauber");
        Add("potion", "trank");
        Add("treasure", "schatz");
        Add("money", "geld");
        Add("key", "schlüssel");
        Add("letter", "brief");
        Add("map", "landkarte");
        Add("flag", "fahne", "flagge");
        Add("boat", "boot");
        Add("ship", "schiff");
        Add("airplane", "flugzeug");
        Add("bicycle", "fahrrad");
        Add("motorcycle", "motorrad");
        Add("throne", "thron");

        // Essen / Trinken
        Add("cake", "kuchen", "torte", "torten");
        Add("slice of cake", "tortenstück", "tortenstueck", "kuchenstück", "kuchenstueck");
        Add("birthday cake", "geburtstagstorte", "geburtstagskuchen");
        Add("cream", "sahne");
        Add("coffee", "kaffee");
        Add("tea", "tee");
        Add("wine", "wein");
        Add("beer", "bier");
        Add("fruit", "obst", "früchte");
        Add("apple", "apfel");
        Add("bread", "brot");
        Add("candy", "süßigkeiten");
        Add("ramen", "ramen");
        Add("sushi", "sushi");

        // Tiere
        Add("fox", "fuchs");
        Add("wolf", "wolf");
        Add("rabbit", "kaninchen", "hase");
        Add("fish", "fisch");
        Add("snake", "schlange");
        Add("tiger", "tiger");
        Add("lion", "löwe");
        Add("bear", "bär");
        Add("mouse", "maus");
        Add("deer", "reh", "hirsch");
        Add("butterfly", "schmetterling");
        Add("owl", "eule");
        Add("spider", "spinne");

        // Wörter, die im Deutschen und Englischen gleich lauten.
        //
        // Sie stehen hier nicht der Übersetzung wegen, sondern damit die Meldung
        // „nicht übersetzt" bei ihnen schweigt. Ohne Eintrag würden sie gemeldet,
        // obwohl CLIP sie versteht — ein Fehlalarm, der den Hinweis entwertet.
        Add("sofa", "sofa");
        Add("hotel", "hotel");
        Add("taxi", "taxi");
        Add("radio", "radio");
        Add("computer", "computer");
        Add("laptop", "laptop");
        Add("monitor", "monitor");
        Add("tablet", "tablet");
        Add("video", "video");
        Add("poster", "poster");
        Add("baby", "baby");
        Add("pizza", "pizza");
        Add("tunnel", "tunnel");
        Add("museum", "museum");
        Add("restaurant", "restaurant");
        Add("garage", "garage");
        Add("zoo", "zoo");
        Add("tiger", "tiger");
        Add("ball", "ball");
        Add("film", "film");
        Add("park", "park");
        Add("bus", "bus");
        Add("bar", "bar");
        Add("jeans", "jeans");
        Add("shorts", "shorts");
        Add("motor", "motor");

        // Natur
        Add("grass", "gras");
        Add("leaves", "blätter");
        Add("leaf", "blatt");
        Add("rocks", "felsen", "fels", "felsbrocken");
        Add("stone", "stein");
        Add("stones", "steine", "steinen");
        Add("pebbles", "kiesel", "kieselsteine", "kies");
        Add("boulder", "findling");
        Add("gravel", "schotter");
        Add("cliff", "klippe", "klippen");
        Add("sand", "sand");
        Add("ocean", "ozean");
        Add("wave", "welle");
        Add("waves", "wellen");
        Add("island", "insel");
        Add("field", "feld");
        Add("meadow", "wiese");
        Add("hill", "hügel");
        Add("valley", "tal");
        Add("sunflower", "sonnenblume");
        Add("rose", "rose");

        // Tätigkeiten / Posen
        Add("jumping", "springend", "springt");
        Add("flying", "fliegend", "fliegt");
        Add("kneeling", "kniend");
        Add("walking", "gehend", "geht");
        Add("reading", "lesend", "liest");
        Add("writing", "schreibend");
        Add("cooking", "kochend");
        Add("eating", "essend");
        Add("drinking", "trinkend", "trinkt");
        Add("swimming", "schwimmend");
        Add("kissing", "küssend", "kuss");
        Add("hugging", "umarmend", "umarmung");
        Add("waving", "winkend");
        Add("holding", "haltend", "hält");
        Add("praying", "betend");
        Add("singing", "singend", "singt");
        Add("playing", "spielend", "spielt");
        Add("riding", "reitend", "reitet");
        Add("shooting", "schießend");

        // Komposition / Foto
        Add("silhouette", "silhouette");
        Add("reflection", "spiegelung", "reflexion");
        Add("shadow", "schatten");
        Add("blurry", "unscharf", "verschwommen");
        Add("profile", "profil");
        Add("group photo", "gruppenfoto");
        Add("selfie", "selfie");
        Add("landscape", "landschaft");

        // Zahlen
        Add("four", "vier");
        Add("five", "fünf");
        Add("six", "sechs");

        // Adjektive
        Add("wet", "nass");
        Add("dirty", "schmutzig", "dreckig");
        Add("clean", "sauber");
        Add("new", "neu");
        Add("thin", "dünn");
        Add("strong", "stark");
        Add("weak", "schwach");
        Add("fast", "schnell");
        Add("slow", "langsam");
        Add("scary", "gruselig", "unheimlich");
        Add("funny", "lustig");
        Add("elegant", "elegant");
        Add("futuristic", "futuristisch");
        Add("ancient", "uralt", "antik");
        Add("magical", "magisch");
        Add("glowing", "leuchtend", "glühend");
        Add("shiny", "glänzend");
        Add("transparent", "durchsichtig", "transparent");
        Add("nude", "nackt");
        Add("barefoot", "barfuß");
        Add("long", "lang", "lange");
        Add("short", "kurz");
        Add("tall", "hoch");
        Add("wide", "breit");
        Add("round", "rund");
        Add("huge", "riesig", "riesige");
        Add("tiny", "winzig");
        Add("ugly", "hässlich");

        return m;
    }
}
