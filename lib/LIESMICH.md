# Optionales OCR-Plugin für schräge Schrift

Dieser Ordner ist im Repo leer — bis auf diese Datei. Er nimmt ein **optionales
Plugin** auf, das die Texterkennung ergänzt. Ohne das Plugin baut und läuft
TestImage unverändert; es fehlt dann nur das Mitlesen schräger Schrift.

## Was es tut

Die Windows-OCR, die TestImage benutzt, liest das ganze Bild in einem Stück und
findet dabei nur einen Textwinkel je Bild. Auf Karten und Luftbildern laufen die
Beschriftungen aber in jede Richtung, häufig hell auf unruhigem Grund — dort
findet sie fast nichts.

Das Plugin sucht stattdessen die **Textzeilen samt Neigung** und liest sie ab
Fassung 0.0.3 mit einem eigenen, eingebetteten Zeilen-Netz. Auf dem
Kartenausschnitt, an dem gemessen wurde (1026 × 607, Strassennamen bis 60°
geneigt), liest es 9 von 12 Namen fehlerfrei; die Windows-OCR allein fand im
ganzen Bild 5 Wörter. Ein älteres Plugin rückt jede Zeile gerade und gibt sie
einzeln der Windows-OCR.

**Zeichenvorrat:** lateinische Buchstaben mit Umlauten und ß, Ziffern, gängige
Satzzeichen. Andere Schriften liest das Netz nicht.

**Scheinzeilen (ab 0.0.4):** Das Plugin hält auch Holzmaserung, Symbole und
fremde Schrift für Zeilen und liest dort Unsinn. Jede Lesung trägt deshalb eine
Sicherheit; unter 0,85 oder mit weniger als 3 Buchstaben und Ziffern wird sie
verworfen. Ab 0.0.5 liest das Plugin jede Zeile zusätzlich in beiden Richtungen
und lässt die sicheren Zeilen über die Leserichtung des Bildes abstimmen — vorher
standen senkrechte Zeilen zufällig auf dem Kopf. Das Lesen dauert dafür rund das
1,5-fache.

In einer Stichprobe von 40 Anime-Standbildern blieben von rund 170
Scheinzeilen etwa 9 übrig, von den echten Zeilen fielen zwei weg — beide ohnehin
falsch gelesen. Echte Ein- und Zweizeichen-Zeilen entfallen dabei mit. In der
Rahmenansicht bleiben verworfene Zeilen gestrichelt stehen.

**Wo es nichts bringt:** gewöhnlicher dunkler Text auf hellem Grund —
Bildschirmfotos, Dokumente. Den liest die Windows-OCR im ganzen Bild schon gut.

**Was es kostet:** Gelesen wird in zwei Durchgängen, einem für helle Schrift auf
dunklem Grund und einem für dunkle auf hellem — ein Durchgang kann nur eine der
beiden Arten finden, und welche in einem Bild steckt, weiss man vorher nicht.

Am Kartenausschnitt gemessen, noch mit Lesen über die Windows-OCR: 1637 ms für
den hellen Durchgang mit 12 gefundenen Zeilen, 364 ms für den dunklen, der dort
keine findet. Der zweite Durchgang ist also billig, solange er leer ausgeht —
teuer ist nicht das Suchen, sondern die Erkennung je Zeile. Liest das Plugin
selbst, sinkt der helle Durchgang auf rund 0,8 s. Beides *zusätzlich* zur
Windows-OCR über das ganze Bild.

Deshalb ist das Mitlesen in der OCR-Karte ein Kreuzchen und ab Werk nicht
gesetzt: Ein Ordnerlauf über viele Bilder würde sonst ein Vielfaches der Zeit
brauchen.

## Warum es nicht im Repo liegt

Das Verfahren stammt aus einem eigenen, nicht veröffentlichten Projekt. Beide
DLLs stehen in der `.gitignore`.

| Datei | Was drin ist |
|---|---|
| `Ocr.Vertrag.dll` (+ `.xml`) | nur Schnittstellen, Datentypen und der Lader |
| `Plugins\Ocr.Core.dll` | das Verfahren selbst |

## Einlegen

1. Beide Dateien wie in der Tabelle hierher legen — `Ocr.Vertrag.dll` und
   `Ocr.Vertrag.xml` direkt in `lib\`, `Ocr.Core.dll` in `lib\Plugins\`.
2. Neu bauen. `TestImage.csproj` prüft mit `Exists()`, ob der Vertrag da ist,
   verweist ihn dann und setzt `OCR_PLUGIN`; das Verfahren wird nur nach
   `<Ausgabeordner>\Plugins` kopiert und zur Laufzeit geladen.
3. In der OCR-Karte steht neben dem Kreuzchen Name und Fassung des geladenen
   Plugins. Bleibt es bei „Plugin nicht eingelegt", hat der Lader nichts
   gefunden.

Das Verfahren darf **nicht** als Verweis eingetragen werden — dann lädt .NET es
beim Start in den normalen Laderaum, und die Anwendung wäre ohne die DLL nicht
mehr lauffähig.

Aufgerufen wird es aus `Bildersuche\PluginOcrDienst.cs`. Ohne `OCR_PLUGIN`
bleibt von dieser Klasse ein kurzer Zweig übrig, der „nicht verfügbar" meldet —
darum braucht keine Aufrufstelle im ViewModel ein `#if`.
