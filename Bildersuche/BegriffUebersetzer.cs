using System;
using System.Collections.Generic;
using System.Linq;

namespace TestImage.Bildersuche
{
    /// <summary>
    /// Übersetzt die von CLIP erkannten englischen Begriffe ins Deutsche.
    /// Deckt die Wortliste aus <see cref="BildAnalyseService"/> ab; unbekannte
    /// Wörter bleiben unverändert. Liefert außerdem Vorschläge für die
    /// Autovervollständigung im Suchfeld.
    /// </summary>
    public static class BegriffUebersetzer
    {
        /// <summary>Liefert die deutsche Entsprechung oder das Original, falls unbekannt.</summary>
        public static string ZuDeutsch(string englisch)
            => Map.TryGetValue(englisch, out string? de) ? de : englisch;

        /// <summary>Alle deutschen Begriffe, alphabetisch sortiert (lazy, da Map später steht).</summary>
        private static readonly Lazy<string[]> DeutschSortiert = new(() => Map.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
            .ToArray());

        /// <summary>
        /// Sucht den ersten deutschen Begriff, der mit <paramref name="praefix"/>
        /// beginnt (ohne exakte Übereinstimmung zu wiederholen). Liefert null,
        /// wenn keiner passt.
        /// </summary>
        public static string? Vervollstaendige(string praefix)
        {
            if (string.IsNullOrWhiteSpace(praefix)) return null;
            foreach (string w in DeutschSortiert.Value)
                if (w.Length > praefix.Length &&
                    w.StartsWith(praefix, StringComparison.CurrentCultureIgnoreCase))
                    return w;
            return null;
        }

        private static readonly Dictionary<string, string> Map = new(System.StringComparer.OrdinalIgnoreCase)
        {
            // Natur / Szenen
            ["flower"] = "Blume", ["tree"] = "Baum", ["forest"] = "Wald", ["mountain"] = "Berg",
            ["beach"] = "Strand", ["ocean"] = "Ozean", ["lake"] = "See", ["river"] = "Fluss",
            ["sky"] = "Himmel", ["clouds"] = "Wolken", ["sunset"] = "Sonnenuntergang",
            ["sunrise"] = "Sonnenaufgang", ["night"] = "Nacht", ["snow"] = "Schnee", ["rain"] = "Regen",
            ["water"] = "Wasser", ["fire"] = "Feuer", ["grass"] = "Gras", ["garden"] = "Garten",
            ["field"] = "Feld", ["waterfall"] = "Wasserfall", ["island"] = "Insel", ["desert"] = "Wüste",
            ["cave"] = "Höhle", ["rainbow"] = "Regenbogen", ["star"] = "Stern", ["moon"] = "Mond",
            ["city"] = "Stadt", ["street"] = "Straße", ["building"] = "Gebäude", ["house"] = "Haus",
            ["room"] = "Zimmer", ["bedroom"] = "Schlafzimmer", ["kitchen"] = "Küche", ["castle"] = "Schloss",
            ["temple"] = "Tempel", ["church"] = "Kirche", ["school"] = "Schule", ["library"] = "Bibliothek",
            ["office"] = "Büro", ["bridge"] = "Brücke", ["train"] = "Zug", ["spaceship"] = "Raumschiff",
            ["battlefield"] = "Schlachtfeld",

            // Personen
            ["person"] = "Person", ["girl"] = "Mädchen", ["boy"] = "Junge", ["man"] = "Mann",
            ["woman"] = "Frau", ["child"] = "Kind", ["baby"] = "Baby", ["group"] = "Gruppe",
            ["crowd"] = "Menschenmenge", ["couple"] = "Paar", ["face"] = "Gesicht", ["portrait"] = "Porträt",
            ["soldier"] = "Soldat", ["knight"] = "Ritter", ["warrior"] = "Krieger", ["king"] = "König",
            ["queen"] = "Königin", ["angel"] = "Engel", ["demon"] = "Dämon", ["robot"] = "Roboter",
            ["ninja"] = "Ninja", ["samurai"] = "Samurai", ["witch"] = "Hexe", ["monster"] = "Monster",

            // Tiere
            ["animal"] = "Tier", ["dog"] = "Hund", ["cat"] = "Katze", ["bird"] = "Vogel",
            ["horse"] = "Pferd", ["fish"] = "Fisch", ["dragon"] = "Drache", ["wolf"] = "Wolf",
            ["fox"] = "Fuchs", ["rabbit"] = "Kaninchen", ["tiger"] = "Tiger", ["lion"] = "Löwe",
            ["bear"] = "Bär", ["butterfly"] = "Schmetterling", ["snake"] = "Schlange", ["owl"] = "Eule",

            // Objekte
            ["car"] = "Auto", ["boat"] = "Boot", ["ship"] = "Schiff", ["airplane"] = "Flugzeug",
            ["sword"] = "Schwert", ["gun"] = "Waffe", ["knife"] = "Messer", ["shield"] = "Schild",
            ["book"] = "Buch", ["food"] = "Essen", ["cake"] = "Kuchen", ["coffee"] = "Kaffee",
            ["guitar"] = "Gitarre", ["piano"] = "Klavier", ["camera"] = "Kamera", ["umbrella"] = "Regenschirm",
            ["phone"] = "Telefon", ["clock"] = "Uhr", ["mirror"] = "Spiegel", ["candle"] = "Kerze",
            ["flag"] = "Flagge", ["key"] = "Schlüssel", ["treasure"] = "Schatz", ["magic"] = "Magie",
            ["throne"] = "Thron",

            // Kleidung
            ["dress"] = "Kleid", ["shirt"] = "Hemd", ["uniform"] = "Uniform", ["armor"] = "Rüstung",
            ["bikini"] = "Bikini", ["swimsuit"] = "Badeanzug", ["kimono"] = "Kimono", ["hat"] = "Hut",
            ["glasses"] = "Brille", ["mask"] = "Maske", ["crown"] = "Krone", ["cape"] = "Umhang",
            ["gloves"] = "Handschuhe", ["boots"] = "Stiefel",

            // Farben
            ["red"] = "Rot", ["blue"] = "Blau", ["green"] = "Grün", ["yellow"] = "Gelb",
            ["black"] = "Schwarz", ["white"] = "Weiß", ["brown"] = "Braun", ["blonde"] = "Blond",
            ["gray"] = "Grau", ["pink"] = "Rosa", ["purple"] = "Lila", ["orange"] = "Orange",

            // Komposition / Aktion
            ["close-up"] = "Nahaufnahme", ["full body"] = "Ganzkörper", ["silhouette"] = "Silhouette",
            ["reflection"] = "Spiegelung", ["landscape"] = "Landschaft", ["colorful"] = "Bunt",
            ["fighting"] = "Kämpfend", ["running"] = "Rennend", ["flying"] = "Fliegend",
            ["sitting"] = "Sitzend", ["standing"] = "Stehend", ["smiling"] = "Lächelnd",
            ["sleeping"] = "Schlafend", ["swimming"] = "Schwimmend", ["dancing"] = "Tanzend",

            // Natur (Erweiterung)
            ["fog"] = "Nebel", ["mist"] = "Dunst", ["storm"] = "Sturm", ["lightning"] = "Blitz",
            ["volcano"] = "Vulkan", ["jungle"] = "Dschungel", ["swamp"] = "Sumpf", ["meadow"] = "Wiese",
            ["cliff"] = "Klippe", ["valley"] = "Tal", ["hill"] = "Hügel", ["canyon"] = "Schlucht",
            ["glacier"] = "Gletscher", ["cherry blossom"] = "Kirschblüte", ["autumn leaves"] = "Herbstlaub",
            ["starry sky"] = "Sternenhimmel", ["moonlight"] = "Mondlicht", ["aurora"] = "Polarlicht",
            ["space"] = "Weltraum", ["planet"] = "Planet", ["galaxy"] = "Galaxie", ["underwater"] = "Unterwasser",
            ["coral reef"] = "Korallenriff", ["sand"] = "Sand", ["rock"] = "Felsen", ["path"] = "Pfad",
            ["road"] = "Straße", ["flower field"] = "Blumenfeld",

            // Orte / Gebäude
            ["bathroom"] = "Badezimmer", ["living room"] = "Wohnzimmer", ["classroom"] = "Klassenzimmer",
            ["hallway"] = "Flur", ["balcony"] = "Balkon", ["rooftop"] = "Dach", ["staircase"] = "Treppe",
            ["tower"] = "Turm", ["lighthouse"] = "Leuchtturm", ["windmill"] = "Windmühle", ["ruins"] = "Ruinen",
            ["shrine"] = "Schrein", ["cathedral"] = "Kathedrale", ["market"] = "Markt", ["shop"] = "Laden",
            ["cafe"] = "Café", ["restaurant"] = "Restaurant", ["bar"] = "Bar", ["hospital"] = "Krankenhaus",
            ["station"] = "Bahnhof", ["airport"] = "Flughafen", ["harbor"] = "Hafen", ["factory"] = "Fabrik",
            ["laboratory"] = "Labor", ["dungeon"] = "Verlies", ["arena"] = "Arena", ["stage"] = "Bühne",
            ["playground"] = "Spielplatz", ["tent"] = "Zelt", ["campfire"] = "Lagerfeuer",
            ["hot spring"] = "Heiße Quelle", ["pool"] = "Pool", ["aquarium"] = "Aquarium", ["museum"] = "Museum",

            // Personen / Rollen
            ["teenager"] = "Teenager", ["elderly"] = "Ältere Person", ["twins"] = "Zwillinge",
            ["family"] = "Familie", ["student"] = "Student", ["teacher"] = "Lehrer",
            ["nurse"] = "Krankenschwester", ["doctor"] = "Arzt", ["chef"] = "Koch",
            ["police officer"] = "Polizist", ["firefighter"] = "Feuerwehrmann", ["pilot"] = "Pilot",
            ["sailor"] = "Matrose", ["maid"] = "Dienstmädchen", ["butler"] = "Butler", ["priest"] = "Priester",
            ["nun"] = "Nonne", ["monk"] = "Mönch", ["wizard"] = "Zauberer", ["mage"] = "Magier",
            ["hunter"] = "Jäger", ["assassin"] = "Attentäter", ["thief"] = "Dieb", ["pirate"] = "Pirat",
            ["cowboy"] = "Cowboy", ["vampire"] = "Vampir", ["ghost"] = "Geist", ["zombie"] = "Zombie",
            ["skeleton"] = "Skelett", ["mermaid"] = "Meerjungfrau", ["fairy"] = "Fee", ["elf"] = "Elf",
            ["orc"] = "Ork", ["giant"] = "Riese", ["alien"] = "Außerirdischer", ["cyborg"] = "Cyborg",
            ["mecha"] = "Mecha", ["superhero"] = "Superheld", ["villain"] = "Schurke", ["goddess"] = "Göttin",
            ["prince"] = "Prinz", ["princess"] = "Prinzessin",

            // Körper / Merkmale
            ["eyes"] = "Augen", ["tears"] = "Tränen", ["blush"] = "Erröten", ["wings"] = "Flügel",
            ["horns"] = "Hörner", ["tail"] = "Schwanz", ["cat ears"] = "Katzenohren", ["fangs"] = "Reißzähne",
            ["halo"] = "Heiligenschein", ["tattoo"] = "Tätowierung", ["scar"] = "Narbe", ["muscles"] = "Muskeln",
            ["long hair"] = "Lange Haare", ["short hair"] = "Kurze Haare", ["ponytail"] = "Pferdeschwanz",
            ["twintails"] = "Zwei Zöpfe", ["braid"] = "Zopf", ["beard"] = "Bart", ["freckles"] = "Sommersprossen",

            // Kleidung / Accessoires
            ["jacket"] = "Jacke", ["coat"] = "Mantel", ["sweater"] = "Pullover", ["hoodie"] = "Kapuzenpulli",
            ["skirt"] = "Rock", ["shorts"] = "Shorts", ["pants"] = "Hose", ["jeans"] = "Jeans",
            ["stockings"] = "Strümpfe", ["scarf"] = "Schal", ["tie"] = "Krawatte", ["belt"] = "Gürtel",
            ["backpack"] = "Rucksack", ["necklace"] = "Halskette", ["earrings"] = "Ohrringe", ["ring"] = "Ring",
            ["sunglasses"] = "Sonnenbrille", ["headphones"] = "Kopfhörer", ["veil"] = "Schleier",
            ["apron"] = "Schürze", ["robe"] = "Robe", ["suit"] = "Anzug", ["wedding dress"] = "Hochzeitskleid",
            ["school uniform"] = "Schuluniform", ["military uniform"] = "Militäruniform", ["cloak"] = "Umhang",
            ["high heels"] = "High Heels", ["helmet"] = "Helm", ["tiara"] = "Diadem", ["cap"] = "Mütze",

            // Objekte
            ["laptop"] = "Laptop", ["computer"] = "Computer", ["television"] = "Fernseher",
            ["microphone"] = "Mikrofon", ["pen"] = "Stift", ["paintbrush"] = "Pinsel", ["notebook"] = "Notizbuch",
            ["newspaper"] = "Zeitung", ["map"] = "Landkarte", ["compass"] = "Kompass", ["telescope"] = "Teleskop",
            ["hourglass"] = "Sanduhr", ["lantern"] = "Laterne", ["torch"] = "Fackel", ["chandelier"] = "Kronleuchter",
            ["fireplace"] = "Kamin", ["ladder"] = "Leiter", ["rope"] = "Seil", ["chain"] = "Kette",
            ["cage"] = "Käfig", ["barrel"] = "Fass", ["box"] = "Kiste", ["basket"] = "Korb", ["vase"] = "Vase",
            ["bottle"] = "Flasche", ["cup"] = "Tasse", ["glass"] = "Glas", ["plate"] = "Teller", ["bowl"] = "Schüssel",
            ["teapot"] = "Teekanne", ["chair"] = "Stuhl", ["table"] = "Tisch", ["desk"] = "Schreibtisch",
            ["bed"] = "Bett", ["sofa"] = "Sofa", ["shelf"] = "Regal", ["painting"] = "Gemälde", ["poster"] = "Poster",
            ["banner"] = "Banner", ["sign"] = "Schild", ["statue"] = "Statue", ["fountain"] = "Brunnen",
            ["bench"] = "Bank", ["window"] = "Fenster", ["door"] = "Tür", ["wall"] = "Wand", ["fence"] = "Zaun",
            ["stairs"] = "Treppe", ["balloon"] = "Luftballon", ["kite"] = "Drachen", ["teddy bear"] = "Teddybär",
            ["doll"] = "Puppe",

            // Fahrzeuge
            ["bicycle"] = "Fahrrad", ["motorcycle"] = "Motorrad", ["bus"] = "Bus", ["truck"] = "Lastwagen",
            ["tank"] = "Panzer", ["helicopter"] = "Hubschrauber", ["rocket"] = "Rakete", ["submarine"] = "U-Boot",
            ["sailboat"] = "Segelboot", ["carriage"] = "Kutsche", ["tram"] = "Straßenbahn", ["ufo"] = "UFO",

            // Waffen / Fantasy
            ["bow"] = "Bogen", ["arrow"] = "Pfeil", ["spear"] = "Speer", ["axe"] = "Axt", ["dagger"] = "Dolch",
            ["hammer"] = "Hammer", ["staff"] = "Stab", ["wand"] = "Zauberstab", ["cannon"] = "Kanone",
            ["rifle"] = "Gewehr", ["pistol"] = "Pistole", ["katana"] = "Katana", ["scythe"] = "Sense",
            ["whip"] = "Peitsche", ["magic circle"] = "Magischer Kreis", ["portal"] = "Portal",
            ["crystal"] = "Kristall", ["orb"] = "Kugel", ["scroll"] = "Schriftrolle", ["potion"] = "Trank",
            ["treasure chest"] = "Schatztruhe", ["gold"] = "Gold", ["coins"] = "Münzen", ["gem"] = "Edelstein",

            // Tiere (Erweiterung)
            ["dolphin"] = "Delfin", ["whale"] = "Wal", ["shark"] = "Hai", ["octopus"] = "Krake",
            ["jellyfish"] = "Qualle", ["crab"] = "Krabbe", ["turtle"] = "Schildkröte", ["frog"] = "Frosch",
            ["lizard"] = "Echse", ["dinosaur"] = "Dinosaurier", ["elephant"] = "Elefant", ["giraffe"] = "Giraffe",
            ["zebra"] = "Zebra", ["monkey"] = "Affe", ["panda"] = "Panda", ["penguin"] = "Pinguin",
            ["eagle"] = "Adler", ["crow"] = "Krähe", ["dove"] = "Taube", ["parrot"] = "Papagei",
            ["peacock"] = "Pfau", ["swan"] = "Schwan", ["duck"] = "Ente", ["chicken"] = "Huhn", ["cow"] = "Kuh",
            ["pig"] = "Schwein", ["sheep"] = "Schaf", ["deer"] = "Reh", ["squirrel"] = "Eichhörnchen",
            ["bat"] = "Fledermaus", ["spider"] = "Spinne", ["bee"] = "Biene", ["dragonfly"] = "Libelle",
            ["unicorn"] = "Einhorn", ["phoenix"] = "Phönix",

            // Essen / Trinken
            ["fruit"] = "Obst", ["apple"] = "Apfel", ["strawberry"] = "Erdbeere", ["grapes"] = "Trauben",
            ["watermelon"] = "Wassermelone", ["cherry"] = "Kirsche", ["lemon"] = "Zitrone",
            ["vegetable"] = "Gemüse", ["tomato"] = "Tomate", ["pumpkin"] = "Kürbis", ["mushroom"] = "Pilz",
            ["corn"] = "Mais", ["bread"] = "Brot", ["sandwich"] = "Sandwich", ["burger"] = "Burger",
            ["pizza"] = "Pizza", ["pasta"] = "Pasta", ["noodles"] = "Nudeln", ["ramen"] = "Ramen",
            ["sushi"] = "Sushi", ["rice"] = "Reis", ["soup"] = "Suppe", ["salad"] = "Salat", ["cookie"] = "Keks",
            ["donut"] = "Donut", ["ice cream"] = "Eis", ["chocolate"] = "Schokolade", ["candy"] = "Süßigkeit",
            ["pancake"] = "Pfannkuchen", ["egg"] = "Ei", ["tea"] = "Tee", ["milk"] = "Milch", ["juice"] = "Saft",
            ["wine"] = "Wein", ["beer"] = "Bier", ["cocktail"] = "Cocktail",

            // Pflanzen
            ["rose"] = "Rose", ["tulip"] = "Tulpe", ["sunflower"] = "Sonnenblume", ["lotus"] = "Lotus",
            ["lily"] = "Lilie", ["daisy"] = "Gänseblümchen", ["cactus"] = "Kaktus", ["bamboo"] = "Bambus",
            ["palm tree"] = "Palme", ["pine tree"] = "Kiefer", ["maple leaf"] = "Ahornblatt",
            ["lavender"] = "Lavendel", ["ivy"] = "Efeu",

            // Stil / Stimmung
            ["cute"] = "Niedlich", ["cool"] = "Cool", ["elegant"] = "Elegant", ["scary"] = "Gruselig",
            ["dark"] = "Dunkel", ["bright"] = "Hell", ["pastel"] = "Pastell", ["neon"] = "Neon",
            ["glowing"] = "Leuchtend", ["sparkles"] = "Funkeln", ["magical"] = "Magisch",
            ["futuristic"] = "Futuristisch", ["cyberpunk"] = "Cyberpunk", ["steampunk"] = "Steampunk",
            ["medieval"] = "Mittelalterlich", ["ancient"] = "Uralt", ["modern"] = "Modern", ["vintage"] = "Vintage",
            ["gothic"] = "Gotisch", ["fantasy"] = "Fantasy", ["sci-fi"] = "Sci-Fi", ["horror"] = "Horror",
            ["dreamy"] = "Verträumt", ["cozy"] = "Gemütlich",

            // Komposition / Foto
            ["wide shot"] = "Weitwinkel", ["aerial view"] = "Luftaufnahme", ["from behind"] = "Von hinten",
            ["from below"] = "Von unten", ["side profile"] = "Seitenprofil", ["shadow"] = "Schatten",
            ["blurry background"] = "Unscharfer Hintergrund", ["bokeh"] = "Bokeh", ["symmetry"] = "Symmetrie",
            ["black and white"] = "Schwarz-Weiß", ["sepia"] = "Sepia", ["panorama"] = "Panorama",
            ["macro"] = "Makro", ["group photo"] = "Gruppenfoto", ["selfie"] = "Selfie",

            // Gefühle / Ausdruck
            ["happy"] = "Glücklich", ["sad"] = "Traurig", ["angry"] = "Wütend", ["crying"] = "Weinend",
            ["laughing"] = "Lachend", ["surprised"] = "Überrascht", ["scared"] = "Ängstlich", ["shy"] = "Schüchtern",
            ["serious"] = "Ernst", ["confused"] = "Verwirrt", ["sleepy"] = "Müde", ["excited"] = "Aufgeregt",
            ["embarrassed"] = "Verlegen", ["determined"] = "Entschlossen", ["calm"] = "Ruhig",
        };
    }
}
