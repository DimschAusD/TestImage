# CLIP-Modelle

In diesem Ordner erwartet die Anwendung vier Dateien:

| Datei | Grösse | Im Repo |
|---|---|---|
| `clip-vocab.json` | 0,8 MB | ja |
| `clip-merges.txt` | 0,5 MB | ja |
| `clip-vit-b32-vision.onnx` | 335 MB | **nein** |
| `clip-text.onnx` | 242 MB | **nein** |

## Warum die beiden grossen fehlen

GitHub lehnt einzelne Dateien über 100 MB ab. Git LFS wäre technisch möglich, das
Freikontingent liegt aber bei 1 GB Übertragung im Monat — ein einziger Klon fräse ein
Drittel davon.

## Was ohne sie passiert

Der Build läuft durch und meldet eine Warnung. Der Bildbetrachter, die Dublettensuche
über Byte-, SHA- und Grauwert-Vergleich und die Konturansicht arbeiten
vollständig. Aus bleibt allein, was CLIP braucht: die Begriffssuche, „Schema-ähnlich"
und das Sortieren nach gelernten Vorlieben.

## Woher man sie bekommt

Es sind die nach ONNX exportierten Gewichte von **CLIP ViT-B/32** (OpenAI, MIT-Lizenz) —
der Bildteil und der Textteil getrennt. Zwei Wege:

1. Aus dem Anhang von [Release v2x.0.72.254](https://github.com/DimschAusD/TestImage/releases/tag/v2x.0.72.254)
   herunterladen — dort hängen beide Dateien einzeln. Release-Anhänge erlauben bis 2 GB
   je Datei und zählen nicht auf die Repo-Grösse.

   Der Verweis geht bewusst auf dieses eine Release und nicht auf `latest`: Die Gewichte
   ändern sich nicht und liegen deshalb nur dort. Spätere Releases bringen allein das
   Programm mit — unter `latest` wären die beiden Dateien also gerade nicht zu finden.
2. Selbst exportieren, aus `openai/clip-vit-base-patch32` auf Hugging Face. Die beiden
   Ausgaben müssen genau so heissen wie oben.

Die Dateien danach in diesen Ordner legen und neu bauen. Der Bauvorgang kopiert sie
neben die Anwendung.
