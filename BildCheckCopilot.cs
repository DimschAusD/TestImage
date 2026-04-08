using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using System.Linq;


namespace TestImage
{
    internal static class BildCheckCopilot
    {



        // ------------------------------------------------------------
        // Öffentliche API
        // ------------------------------------------------------------

        /// <summary>
        /// Ein Durchgang für alle Checks:
        /// (HeaderPasst, HatFrame, DownloadKorrupt, IstNullDatei, DetektiertesFormat)
        /// DownloadKorrupt = strukturelle Heuristik (EOI/IEND/RIFF) ODER Decoder-Formatfehler ODER Grauleisten-Heuristik.
        /// </summary>
        public static (bool HeaderPasst, bool HatFrame, bool DownloadKorrupt, bool IstNullDatei, string DetektiertesFormat, bool IstBeschädigt)
            PruefeBildDatei(string filePath)
        {



            // 0) Null-Datei zuerst prüfen
            bool istNull = IsBildNullDatei(filePath);
            if (istNull)
                return (false, false, false, true, "unknown", true);
            // Header/HatFrame/DownloadKorrupt sind hier irrelevant; 
            // "IstBeschädigt" = true, weil 0-Byte-Dateien faktisch unbrauchbar sind.

            // 1) Header ↔ Extension
            bool headerPasst = IsHeaderPassendZurErweiterung(filePath, out string detectedFormat);

            // 2) Strukturelle Heuristik
            bool heuristikKorrupt = IstWahrscheinlichKorrupt(filePath, detectedFormat);

            // 3) Frame-Check + 4) Grauleiste + 5) Explizite Beschädigung
            bool hatFrame = false;
            bool decoderKorrupt = false;
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                hatFrame = decoder.Frames != null && decoder.Frames.Count > 0;
            }
            catch (NotSupportedException) { hatFrame = false; }
            catch (FileFormatException) { decoderKorrupt = true; hatFrame = false; }
            catch { hatFrame = false; }

            bool grauleiste = false;
            try { grauleiste = HatGraueAbschlusszeile(filePath); } catch { grauleiste = false; }

            bool downloadKorrupt = heuristikKorrupt || decoderKorrupt || grauleiste;
            bool istBeschaedigt = IsBildDateiBeschädigt(filePath);

            return (headerPasst, hatFrame, downloadKorrupt, false, detectedFormat, istBeschaedigt);


        }

        // ---- Wrapper im alten Stil (falls du bestehende Aufrufe behalten willst)

        public static bool IsHeaderPassendZurErweiterung(string filePath)
            => IsHeaderPassendZurErweiterung(filePath, out _);

        public static bool IsFrameImBildDrin(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.None);
                return decoder.Frames != null && decoder.Frames.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsBildDownloadCorrupted(string filePath)
        {
            // Kombination aus struktureller Heuristik + Grauleisten-Heuristik
            _ = IsHeaderPassendZurErweiterung(filePath, out var fmt);
            bool strukturell = IstWahrscheinlichKorrupt(filePath, fmt);
            bool grau = false;
            try { grau = HatGraueAbschlusszeile(filePath); } catch { grau = false; }
            return strukturell || grau;
        }

        public static bool IsBildNullDatei(string pfad)
        {


            // Null/leer → wie Null-Datei behandeln
            if (string.IsNullOrWhiteSpace(pfad))
                return true;

            try
            {
                var fi = new FileInfo(pfad);

                // Nicht vorhanden → wie Null-Datei behandeln
                if (!fi.Exists)
                    return true;

                // 0 Bytes → Null-Datei
                if (fi.Length == 0)
                    return true;

                // > 0 Bytes → nicht Null-Datei
                return false;
            }
            catch
            {
                // Zugriffs-/IO-Fehler → vorsichtig als Null/ungültig werten
                return true;
            }

        }


        /// <summary>
        /// Prüft, ob eine Bilddatei beschädigt ist.
        /// Für WebP ohne Codec: wertet nicht automatisch als beschädigt, nimmt RIFF/WEBP-Header als Plausibilitätscheck.
        /// </summary>
        public static bool IsBildDateiBeschädigt(string dateiPfad)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dateiPfad) || !File.Exists(dateiPfad))
                    return true;

                var fi = new FileInfo(dateiPfad);
                if (fi.Length == 0)
                    return true;

                var ext = Path.GetExtension(dateiPfad).TrimStart('.').ToLowerInvariant();

                if (ext == "webp")
                {
                    try
                    {
                        return !TryStrictDecode(dateiPfad); // Codec vorhanden → echtes Dekodieren
                    }
                    catch (NotSupportedException)
                    {
                        // Kein WebP-Codec → Header-Check als Fallback
                        return !IsValidWebpRiffHeader(dateiPfad);
                    }
                    catch (FileFormatException)
                    {
                        return true;
                    }
                    catch
                    {
                        return true;
                    }
                }
                else
                {
                    // Andere Formate → striktes OnLoad-Dekodieren
                    return !TryStrictDecode(dateiPfad);
                }
            }
            catch
            {
                return true;
            }
        }

        // ------------------------------------------------------------
        // Dein wiedererkennbarer Header-Check (mit minBytes & WEBP-Switch)
        // ------------------------------------------------------------

        internal static bool IsHeaderPassendZurErweiterung(string filePath, out string detectedFormat)
        {
            detectedFormat = "unknown";

            if (!File.Exists(filePath)) return false;

            var ext = Path.GetExtension(filePath).ToLowerInvariant().TrimStart('.'); // "webp", "png", ...

            int minBytes = ext switch
            {
                "webp" => 12,
                "ico" => 6,
                "gif" => 6,
                "jpg" or "jpeg" => 3,
                "tif" or "tiff" => 4,
                "bmp" => 2,
                "png" => 8,
                _ => 8
            };

            byte[] buffer = new byte[minBytes];

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
#if NET8_0_OR_GREATER
                    fs.ReadExactly(buffer);
#else
                int read = fs.Read(buffer, 0, buffer.Length);
                if (read < buffer.Length) return false;
#endif
                }

                switch (ext)
                {
                    case "webp":
                        bool isWebP = buffer.Length >= 12
                            && buffer[0] == (byte)'R' && buffer[1] == (byte)'I'
                            && buffer[2] == (byte)'F' && buffer[3] == (byte)'F'
                            && buffer[8] == (byte)'W' && buffer[9] == (byte)'E'
                            && buffer[10] == (byte)'B' && buffer[11] == (byte)'P';
                        detectedFormat = isWebP ? "webp" : "unknown";
                        return isWebP;

                    case "tif":
                    case "tiff":
                        bool isII = buffer[0] == (byte)'I' && buffer[1] == (byte)'I' && buffer[2] == 42 && buffer[3] == 0;
                        bool isMM = buffer[0] == (byte)'M' && buffer[1] == (byte)'M' && buffer[2] == 0 && buffer[3] == 42;
                        detectedFormat = (isII || isMM) ? "tiff" : "unknown";
                        return isII || isMM;

                    default:
                        var key = ext.ToUpperInvariant();
                        if (!ImageHeaders.TryGetValue(key, out var magic) || magic == null)
                            return false;

                        if (buffer.Length < magic.Length) return false;

                        bool ok = buffer.AsSpan(0, magic.Length).SequenceEqual(magic);
                        if (ok) detectedFormat = NormalisiereExt(ext);
                        return ok;
                }
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------
        // Hilfstabellen & Helper
        // ------------------------------------------------------------

        // Schlüssel UPPERCASE (wir nutzen oben ext.ToUpperInvariant()).
        private static readonly Dictionary<string, byte[]> ImageHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PNG"] = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            ["JPG"] = new byte[] { 0xFF, 0xD8, 0xFF },
            ["JPEG"] = new byte[] { 0xFF, 0xD8, 0xFF },
            ["GIF"] = new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8' }, // 87a + 89a
            ["BMP"] = new byte[] { (byte)'B', (byte)'M' },
            ["ICO"] = new byte[] { 0x00, 0x00, 0x01, 0x00 }
            // TIFF/WEBP werden separat geprüft
        };

        private static string NormalisiereExt(string ext) => ext switch
        {
            "jpg" or "jpeg" or "jpe" => "jpg",
            "tif" or "tiff" => "tiff",
            _ => ext
        };

        /// <summary>
        /// Strukturelle Heuristik auf Dateiebene (ohne volle Dekodierung).
        /// </summary>
        private static bool IstWahrscheinlichKorrupt(string filePath, string detectedFormat)
        {
            try
            {
                var len = new FileInfo(filePath).Length;
                if (len < 2) return true;

                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

                switch (detectedFormat)
                {
                    case "jpg":
                        if (len < 4) return true;
                        fs.Position = len - 2;
                        Span<byte> end2 = stackalloc byte[2];
                        if (fs.Read(end2) < 2) return true;
                        return !(end2[0] == 0xFF && end2[1] == 0xD9);

                    case "png":
                        if (len < 45) return true;
                        fs.Position = len - 8;
                        Span<byte> last8 = stackalloc byte[8];
                        if (fs.Read(last8) < 8) return true;
                        return !(last8[0] == (byte)'I' && last8[1] == (byte)'E' &&
                                 last8[2] == (byte)'N' && last8[3] == (byte)'D');

                    case "gif":
                        if (len < 14) return true;
                        fs.Position = len - 1;
                        int b = fs.ReadByte();
                        return b != 0x3B;

                    case "bmp":
                        if (len < 26) return true;
                        fs.Position = 2;
                        Span<byte> sizeLE = stackalloc byte[4];
                        if (fs.Read(sizeLE) < 4) return true;
                        uint declared = BitConverter.ToUInt32(sizeLE);
                        return declared > len;

                    case "webp":
                        if (len < 12) return true;
                        Span<byte> riff = stackalloc byte[12];
                        fs.Position = 0;
#if NET8_0_OR_GREATER
                        fs.ReadExactly(riff);
#else
                    if (fs.Read(riff) < 12) return true;
#endif
                        bool riffWebp = riff[0] == 'R' && riff[1] == 'I' && riff[2] == 'F' && riff[3] == 'F' &&
                                        riff[8] == 'W' && riff[9] == 'E' && riff[10] == 'B' && riff[11] == 'P';
                        if (!riffWebp) return true;

                        uint riffSize = BitConverter.ToUInt32(riff.Slice(4, 4)); // LE
                        if ((ulong)riffSize + 8UL > (ulong)len) return true;

                        if (len >= 16)
                        {
                            Span<byte> fourcc = stackalloc byte[4];
                            fs.Position = 12;
#if NET8_0_OR_GREATER
                            fs.ReadExactly(fourcc);
#else
                        if (fs.Read(fourcc) < 4) return true;
#endif
                            if (!(fourcc[0] == 'V' && fourcc[1] == 'P' && fourcc[2] == '8' &&
                                  (fourcc[3] == (byte)' ' || fourcc[3] == (byte)'L' || fourcc[3] == (byte)'X')))
                                return true;
                        }
                        return false;

                    case "tiff":
                        return len < 8;

                    case "ico":
                        if (len < 6) return true;
                        fs.Position = 0;
                        Span<byte> hdr = stackalloc byte[6];
                        if (fs.Read(hdr) < 6) return true;
                        if (!(hdr[0] == 0x00 && hdr[1] == 0x00 && hdr[2] == 0x01 && hdr[3] == 0x00))
                            return true;
                        ushort count = BitConverter.ToUInt16(hdr.Slice(4, 2));
                        long minSize = 6L + 16L * count;
                        return len < minSize;

                    default:
                        return true;
                }
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Versucht ein Bild vollständig zu initialisieren (OnLoad). Gelingt es → ok.
        /// </summary>
        private static bool TryStrictDecode(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            if (decoder.Frames == null || decoder.Frames.Count == 0) return false;

            var frame = decoder.Frames[0];
            _ = frame.DpiX; // leichte Zugriffe triggern Validierung
            int w = frame.PixelWidth;
            int h = frame.PixelHeight;

            return w > 0 && h > 0;
        }

        /// <summary>
        /// Minimal robuster RIFF/WEBP-Header-Check (codec-frei).
        /// </summary>
        private static bool IsValidWebpRiffHeader(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 12) return false;

            Span<byte> head = stackalloc byte[12];
#if NET8_0_OR_GREATER
            fs.ReadExactly(head);
#else
        if (fs.Read(head) < 12) return false;
#endif

            bool riffWebp = head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F'
                          && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P';
            if (!riffWebp) return false;

            uint riffSize = BitConverter.ToUInt32(head.Slice(4, 4)); // LE
            if ((ulong)riffSize + 8UL > (ulong)fs.Length) return false;

            if (fs.Length >= 16)
            {
                fs.Position = 12;
                Span<byte> fourcc = stackalloc byte[4];
#if NET8_0_OR_GREATER
                fs.ReadExactly(fourcc);
#else
            if (fs.Read(fourcc) < 4) return false;
#endif
                if (!(fourcc[0] == 'V' && fourcc[1] == 'P' && fourcc[2] == '8' &&
                      (fourcc[3] == (byte)' ' || fourcc[3] == (byte)'L' || fourcc[3] == (byte)'X')))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// *** Deine inhaltsbasierte Heuristik: erkennt eine graue Abschlusszeile (typisch bei abgebrochenen Downloads). ***
        /// Lädt dekodiert (OnLoad), konvertiert auf BGRA32 und prüft die letzte Zeile auf >= schwelleAnteil exakt (128,128,128).
        /// </summary>
        private static bool HatGraueAbschlusszeile(string pfad, double schwelleAnteil = 0.9, byte grau = 128)
        {
            // Laden & Dekodieren (OnLoad), damit die Pixel verfügbar sind.
            var src = new BitmapImage();
            src.BeginInit();
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.UriSource = new Uri(pfad);
            src.EndInit();
            src.Freeze();

            // Auf definiertes Pixelformat konvertieren (BGRA32 = 4 Bytes).
            BitmapSource bmp = src.Format == PixelFormats.Bgra32
                ? src
                : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            bmp.Freeze();

            int width = bmp.PixelWidth;
            int height = bmp.PixelHeight;
            if (width <= 0 || height <= 0) return false;

            int stride = width * 4;
            byte[] lastPixels = new byte[stride];

            var rect = new Int32Rect(0, height - 1, width, 1);
            bmp.CopyPixels(rect, lastPixels, stride, 0);

            int grayCount = 0;
            for (int x = 0; x < width; x++)
            {
                int i = x * 4;
                byte b = lastPixels[i + 0];
                byte g = lastPixels[i + 1];
                byte r = lastPixels[i + 2];

                if (r == grau && g == grau && b == grau)
                    grayCount++;
            }

            return grayCount > (int)(width * schwelleAnteil);
        }
    }


}

