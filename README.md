# TestImage

Ein Bildbetrachter zum **Aussortieren grosser Bildersammlungen** unter Windows.
Er ist für den Fall gebaut, dass mehrere tausend Bilder in einem Ordner liegen und
entschieden werden muss, was bleibt: durchblättern, Doubletten finden, wegsortieren,
zurückholen.

Quelltext und Kommentare sind auf Deutsch.

## Was er kann

**Blättern und Wegsortieren**
Pfeiltasten vor und zurück, Pfeil nach unten legt ein Bild in den Unterordner
`kein_Fav`, Pfeil nach oben holt es zurück. Weitere Ablagen: `KI_Fehler`,
`Doppelt`, `Besonders`, `Wasserzeichen`. Jedes Verschieben lässt sich rückgängig
machen.

**Doubletten finden**
Vier Wege, vom billigsten zum teuersten:

| Verfahren | findet |
|---|---|
| Byte-Vergleich | bitgleiche Dateien |
| SHA256 | dasselbe, über einen einmal berechneten Prüfwert |
| Perceptual Hash (dHash) | dasselbe Bild in anderer Grösse oder Qualität |
| Grauwert-Abgleich | ähnliche Bilder mit einstellbarer Schwelle |

**Bilder prüfen**
Beim Anzeigen wird optional geprüft, ob die Datei beschädigt ist, ob der Dateikopf
zur Endung passt, ob ein Rahmen im Bild steckt und ob die Datei leer ist. Das
Ergebnis steht als Ampel neben dem Bild.

**Konturansicht**
Zeigt statt des Bildes sein Kantenbild (Sobel) mit einstellbarer Schwelle. Macht
schwache Aufdrucke und Wasserzeichen sichtbar.

**Begriffssuche über CLIP** *(braucht die Modelldateien, siehe unten)*
Ordner werden indiziert und lassen sich danach nach Begriffen durchsuchen —
auf Deutsch, ein Übersetzer davor bildet die Anfrage auf das englische Vokabular
des Modells ab. Dazu: „schema-ähnliche" Bilder finden und Ordner nach gelernten
Vorlieben vorsortieren.

## Bauen

Vorausgesetzt sind **Windows 10 (Build 18362) oder neuer** und das
**.NET 10 SDK**.

```
git clone https://github.com/DimschAusD/TestImage.git
cd TestImage
dotnet build
```

Das war es — alle Abhängigkeiten kommen über NuGet, es gibt keine nativen
Bibliotheken und nichts nachzuinstallieren.

### Die CLIP-Modelle

Der Build läuft auch ohne sie durch und meldet nur eine Warnung. Ohne die Modelle
fehlen die Begriffssuche, „schema-ähnlich" und das Sortieren nach Vorlieben —
Betrachter, Doublettensuche, Prüfung und Konturansicht arbeiten vollständig.

Die beiden Gewichtsdateien sind zusammen rund 580 MB und liegen deshalb nicht im
Repo; GitHub lehnt einzelne Dateien über 100 MB ab. Woher man sie bekommt, steht
in [`models/LIESMICH.md`](models/LIESMICH.md).

## Aufbau

| Ordner | Inhalt |
|---|---|
| `Ansichten/` | Normalansicht, Vollbild, Dublettenansicht |
| `Bildersuche/` | Index, Wasserzeichen, Farbsignatur, WPF-Brücke |
| `Converters/` | Wertwandler für die Bindungen |
| `Geraete/` | liest aus der Registrierung, ob Kamera oder Mikrofon in Benutzung sind |
| `Images/` | `Symbole.xaml` — alle Symbole als Zeichnungen, kein PNG |
| `ImageMatching.Core/` | Bildvergleich ohne WPF: Sobel, dHash, Index |
| `ImageMatching.Cnn/` | CLIP über ONNX Runtime, Tagger, Übersetzer |

`ImageMatching.Core` und `ImageMatching.Cnn` kennen kein WPF und sind für sich
verwendbar.

## Fremde Bestandteile

| Paket | Lizenz |
|---|---|
| CommunityToolkit.Mvvm | MIT |
| Microsoft.Xaml.Behaviors.Wpf | MIT |
| Microsoft.ML.OnnxRuntime | MIT |
| System.Drawing.Common | MIT |
| CLIP ViT-B/32 (Modellgewichte) | MIT, OpenAI |

Alle Symbole sind selbst gezeichnet oder Zeichen der mit Windows gelieferten
Schriften Segoe UI Symbol und Segoe MDL2 Assets. Es liegt keine fremde Grafik im
Repo.

## Lizenz

MIT — siehe [LICENSE](LICENSE).
