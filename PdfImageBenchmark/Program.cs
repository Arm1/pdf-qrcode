using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using PDFiumCore;
using PDFtoImage;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using FeatherQR;

namespace PdfImageBenchmark;

public class Program
{
    public static void Main(string[] args)
    {

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        PrintHeader();

        // 1. Déterminer le fichier PDF à tester
        string pdfPath;
        if (args.Length > 0 && File.Exists(args[0]))
        {
            pdfPath = args[0];
            Console.WriteLine($"📄 Utilisation du fichier PDF fourni : {pdfPath}");
        }
        else if (File.Exists("offre.pdf"))
        {
            pdfPath = "offre.pdf";
            Console.WriteLine($"📄 Utilisation du fichier PDF par défaut : {pdfPath}");
        }
        else if (File.Exists("../offre.pdf"))
        {
            pdfPath = "../offre.pdf";
            Console.WriteLine($"📄 Utilisation du fichier PDF par défaut : {pdfPath}");
        }
        else
        {
            pdfPath = "benchmark_sample_20p.pdf";
            Console.WriteLine($"📄 Génération d'un PDF de test réaliste (20 pages avec graphismes et images)...");
            GenerateBenchmarkPdf(pdfPath, pageCount: 20);
            Console.WriteLine($"✅ PDF généré avec succès ({new FileInfo(pdfPath).Length / 1024} Ko)\n");
        }

        // 2. Initialisation de la bibliothèque PDFium
        fpdfview.FPDF_InitLibrary();

        try
        {
            // Vérification du nombre de pages
            int pageCount = GetPageCount(pdfPath);
            Console.WriteLine($"📊 Document chargé : {pageCount} pages à convertir en image.\n");

            Console.WriteLine("🚀 Démarrage du Benchmark (.NET 10)...\n");

            // --- TEST 1 : PDFiumCore (Rastérisation native P/Invoke directe) ---
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine("1️⃣  Moteur : PDFiumCore (Rastérisation native C++ directe - Mémoire brute)");
            Console.WriteLine("    -> Rendu de la page entière (vectoriel, texte, images) en buffer BGRA natif.");
            var perfPdfiumCore = BenchmarkEngine("PDFiumCore (P/Invoke)", pdfPath, pageCount, () => RunPdfiumCore(pdfPath, 150));
            PrintResult(perfPdfiumCore);

            // --- TEST 2 : PDFtoImage (PDFiumCore + SkiaSharp) ---
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine("2️⃣  Moteur : PDFtoImage (Wrapper moderne de PDFiumCore avec SkiaSharp)");
            Console.WriteLine("    -> Rendu de la page entière en objets SKBitmap gérés.");
            var perfPdfToImage = BenchmarkEngine("PDFtoImage (SkiaSharp)", pdfPath, pageCount, () => RunPdfToImage(pdfPath, 150));
            PrintResult(perfPdfToImage);

            // --- TEST 3 : PDFsharp (Extraction d'images embarquées) ---
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine("3️⃣  Moteur : PDFsharp (Extraction d'images XObject)");
            Console.WriteLine("    -> IMPORTANT : PDFsharp n'a PAS de rastériseur de page !");
            Console.WriteLine("    -> Mesure de l'extraction des flux d'images bruts déjà présents dans les pages.");
            var perfPdfSharp = BenchmarkEngine("PDFsharp (Extract Images)", pdfPath, pageCount, () => RunPdfSharpExtract(pdfPath));
            PrintResult(perfPdfSharp);

            // 3. Tableau comparatif final
            PrintSummaryTable(perfPdfiumCore, perfPdfToImage, perfPdfSharp);
            PrintArchitecturalExplanation();
        }
        finally
        {
            fpdfview.FPDF_DestroyLibrary();
        }
    }

    private static void PrintHeader()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine("       BENCHMARK DE PERFORMANCE PDF -> IMAGE (.NET 10 / Apple Silicon)          ");
        Console.WriteLine("       PDFiumCore (Google PDFium) vs PDFtoImage vs PDFsharp                     ");
        Console.WriteLine("================================================================================\n");
        Console.ResetColor();
    }

    #region Benchmark Logic

    public record BenchmarkResult(
        string EngineName,
        int PageCount,
        int ProcessedItems,
        double TotalMilliseconds,
        double AvgMsPerPage,
        double PagesPerSecond,
        long AllocatedBytes
    );

    private static BenchmarkResult BenchmarkEngine(string name, string pdfPath, int pageCount, Func<int> action)
    {
        // 1. Warm-up pour éliminer les biais JIT
        action();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long startMem = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();

        int processed = action();

        sw.Stop();
        long endMem = GC.GetAllocatedBytesForCurrentThread();

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double avgMs = totalMs / pageCount;
        double pps = (pageCount / totalMs) * 1000.0;
        long allocated = Math.Max(0, endMem - startMem);

        return new BenchmarkResult(name, pageCount, processed, totalMs, avgMs, pps, allocated);
    }

    private static void PrintResult(BenchmarkResult res)
    {
        Console.WriteLine($"   Éléments traités   : {res.ProcessedItems} {(res.EngineName.Contains("Extract") ? "image(s) XObject extraite(s)" : "page(s) rastérisée(s)")}");
        Console.WriteLine($"   Temps total        : {res.TotalMilliseconds,8:F2} ms");
        Console.WriteLine($"   Temps moyen / page : {res.AvgMsPerPage,8:F2} ms");
        Console.WriteLine($"   Débit              : {res.PagesPerSecond,8:F1} pages/sec");
        Console.WriteLine($"   Mémoire allouée    : {res.AllocatedBytes / 1024.0 / 1024.0,8:F2} Mo (GC)");
        Console.WriteLine();
    }

    private static void PrintSummaryTable(params BenchmarkResult[] results)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("================================================================================");
        Console.WriteLine("                             TABLEAU COMPARATIF FINAL                           ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        Console.WriteLine($"| {"Moteur / Méthode",-26} | {"Temps Total",-11} | {"Moyenne/Page",-12} | {"Débit (p/s)",-11} | {"Alloué (Mo)",-11} |");
        Console.WriteLine("|----------------------------|-------------|--------------|-------------|-------------|");

        foreach (var r in results)
        {
            Console.WriteLine($"| {r.EngineName,-26} | {r.TotalMilliseconds,8:F1} ms | {r.AvgMsPerPage,8:F2} ms  | {r.PagesPerSecond,8:F1} p/s | {r.AllocatedBytes / 1024.0 / 1024.0,8:F2} Mo  |");
        }
        Console.WriteLine("--------------------------------------------------------------------------------\n");
    }

    private static void PrintArchitecturalExplanation()
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("💡 ANALYSE ARCHITECTURALE & RECOMMANDATIONS :");
        Console.ResetColor();
        Console.WriteLine(" 1. Docnet.Core vs PDFiumCore :");
        Console.WriteLine("    - Votre remarque est 100% exacte : Docnet.Core n'est plus maintenu activement.");
        Console.WriteLine("    - PDFiumCore est le wrapper C# officiel et moderne pour Google PDFium.");
        Console.WriteLine("    - Il suit directement les versions amont de Chromium/PDFium via bblanchon.");
        Console.WriteLine();
        Console.WriteLine(" 2. PDFsharp vs PDFiumCore :");
        Console.WriteLine("    - PDFsharp n'est PAS un moteur de rendu : il ne sait PAS convertir une page");
        Console.WriteLine("      vectorielle (texte, lignes, formes) en image bitmap.");
        Console.WriteLine("    - PDFsharp peut uniquement EXTRAIRE les images XObject déjà encapsulées.");
        Console.WriteLine("    - Si votre PDF contient un QR code vectoriel ou si vous voulez l'image complète");
        Console.WriteLine("      de la page pour la donner à un décodeur de QR code, PDFiumCore ou PDFtoImage");
        Console.WriteLine("      est OBLIGATOIRE.");
        Console.WriteLine();
        Console.WriteLine(" 3. Recommandation pour votre projet de détection de QR Code :");
        Console.WriteLine("    - Pour la vitesse maximale brute : PDFiumCore (rendu BGRA direct sans copie)");
        Console.WriteLine("    - Pour la simplicité de code : PDFtoImage (haut niveau, basé sur PDFiumCore)");
        Console.WriteLine("    - Décodeur recommandé : zxingcpp.net (C++ natif) sur le buffer obtenu.");
        Console.WriteLine("================================================================================\n");
    }

    #endregion

    #region Engine Implementations

    /// <summary>
    /// Rastérisation via PDFiumCore directement dans un buffer BGRA natif.
    /// Vitesse maximale absolue, aucune allocation SkiaSharp intermédiaire.
    /// </summary>
    private static int RunPdfiumCore(string pdfPath, int targetDpi)
    {
        var doc = fpdfview.FPDF_LoadDocument(pdfPath, null);
        if (doc == null) throw new InvalidOperationException("Impossible de charger le document avec PDFium.");

        int count = fpdfview.FPDF_GetPageCount(doc);
        int rendered = 0;

        for (int i = 0; i < count; i++)
        {
            var page = fpdfview.FPDF_LoadPage(doc, i);
            double widthPt = fpdfview.FPDF_GetPageWidth(page);
            double heightPt = fpdfview.FPDF_GetPageHeight(page);

            int widthPx = (int)(widthPt * targetDpi / 72.0);
            int heightPx = (int)(heightPt * targetDpi / 72.0);

            // Création d'un bitmap natif 4 octets BGRA (alpha = 1)
            var bitmap = fpdfview.FPDFBitmapCreate(widthPx, heightPx, 1);
            fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, widthPx, heightPx, 0xFFFFFFFF); // Fond blanc
            fpdfview.FPDF_RenderPageBitmap(bitmap, page, 0, 0, widthPx, heightPx, 0, 0);

            IntPtr buffer = fpdfview.FPDFBitmapGetBuffer(bitmap);
            int stride = fpdfview.FPDFBitmapGetStride(bitmap);

            if (buffer != IntPtr.Zero && stride > 0)
            {
                rendered++;

                if (TryDecodeQr(buffer, widthPx, heightPx, stride, out var qrText))
                {
                    Console.WriteLine($"QR Code détecté sur la page {i + 1} : {qrText}");
                }
            }

            fpdfview.FPDFBitmapDestroy(bitmap);
            fpdfview.FPDF_ClosePage(page);
        }

        fpdfview.FPDF_CloseDocument(doc);
        return rendered;
    }

    // Convertit le buffer BGRA en luminance 8 bits puis décode avec FeatherQR.
    private static bool TryDecodeQr(IntPtr buffer, int width, int height, int stride, out string? text)
    {
        var bgra = new byte[stride * height];
        Marshal.Copy(buffer, bgra, 0, bgra.Length);

        var luminance = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            int src = y * stride;
            int dst = y * width;
            for (int x = 0; x < width; x++, src += 4)
            {
                luminance[dst + x] = (byte)((bgra[src] * 29 + bgra[src + 1] * 150 + bgra[src + 2] * 77) >> 8);
            }
        }

        return QRCodeDecoder.TryDecodeImage(luminance, width, height, out text, out _);
    }

    private static bool TryDecodeQr(SKBitmap bitmap, out string? text)
    {
        int width = bitmap.Width, height = bitmap.Height;
        var pixels = bitmap.Pixels;
        var luminance = new byte[width * height];
        for (int i = 0; i < luminance.Length; i++)
        {
            var p = pixels[i];
            // Composite sur fond blanc
            int a = p.Alpha;
            int r = (p.Red * a + 255 * (255 - a)) / 255;
            int g = (p.Green * a + 255 * (255 - a)) / 255;
            int b = (p.Blue * a + 255 * (255 - a)) / 255;
            luminance[i] = (byte)((b * 29 + g * 150 + r * 77) >> 8);
        }

        return QRCodeDecoder.TryDecodeImage(luminance, width, height, out text, out _);
    }

    // Décode une image XObject PDF : JPEG (DCTDecode) ou pixels bruts 8 bits Gray/RGB.
    private static bool TryDecodeQr(PdfDictionary image, out string? text)
    {
        text = null;
        try
        {
            string filter = image.Elements.GetValue("/Filter")?.ToString() ?? "";
            if (filter.Contains("DCTDecode") || filter.Contains("JPXDecode"))
            {
                using var bmp = SKBitmap.Decode(image.Stream.Value);
                return bmp != null && TryDecodeQr(bmp, out text);
            }

            int width = image.Elements.GetInteger("/Width");
            int height = image.Elements.GetInteger("/Height");
            if (image.Elements.GetInteger("/BitsPerComponent") != 8 || width <= 0 || height <= 0) return false;

            var raw = image.Stream.UnfilteredValue;
            if (raw == null) return false;
            int channels = raw.Length / (width * height);
            if (channels != 1 && channels != 3) return false;

            var luminance = new byte[width * height];
            for (int i = 0, s = 0; i < luminance.Length; i++, s += channels)
            {
                luminance[i] = channels == 1
                    ? raw[s]
                    : (byte)((raw[s] * 77 + raw[s + 1] * 150 + raw[s + 2] * 29) >> 8);
            }

            return QRCodeDecoder.TryDecodeImage(luminance, width, height, out text, out _);
        }
        catch (Exception)
        {
            Console.WriteLine("Erreur lors du décodage de l'image PDF.");
            return false;
        }
    }

    /// <summary>
    /// Rastérisation via PDFtoImage (qui encapsule PDFiumCore et convertit en SKBitmap).
    /// </summary>
    private static int RunPdfToImage(string pdfPath, int targetDpi)
    {
        using var stream = File.OpenRead(pdfPath);
        var options = new RenderOptions { Dpi = targetDpi };
        int rendered = 0;
        int pageIndex = 0;

#pragma warning disable CA1416
        foreach (var skBitmap in Conversion.ToImages(stream, leaveOpen: true, options: options))
        {
            pageIndex++;
            if (skBitmap != null)
            {
                rendered++;
                if (TryDecodeQr(skBitmap, out var qrText))
                {
                    Console.WriteLine($"QR Code détecté sur la page {pageIndex} : {qrText}");
                }
                skBitmap.Dispose();
            }
        }
#pragma warning restore CA1416

        return rendered;
    }

    /// <summary>
    /// Extraction d'images brutes XObject avec PDFsharp.
    /// N'effectue PAS de rendu vectoriel de la page.
    /// </summary>
    private static int RunPdfSharpExtract(string pdfPath)
    {
        using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        int extracted = 0;
        int pageIndex = 0;

        foreach (var page in doc.Pages)
        {
            pageIndex++;
            var resources = page.Resources;
            var xObjects = resources.Elements.GetDictionary("/XObject");
            if (xObjects == null) continue;

            foreach (var item in xObjects.Elements.Values)
            {
                PdfDictionary? dict = item as PdfDictionary;
                if (item is PdfReference reference)
                {
                    dict = reference.Value as PdfDictionary;
                }

                if (dict != null && dict.Elements.GetName("/Subtype") == "/Image")
                {
                    byte[] streamBytes = dict.Stream.Value;
                    if (streamBytes.Length > 0)
                    {
                        extracted++;
                        if (TryDecodeQr(dict, out var qrText))
                        {
                            Console.WriteLine($"QR Code détecté sur la page {pageIndex} : {qrText}");
                        }
                    }
                }
            }
        }

        return extracted;
    }

    private static int GetPageCount(string pdfPath)
    {
        var doc = fpdfview.FPDF_LoadDocument(pdfPath, null);
        int count = fpdfview.FPDF_GetPageCount(doc);
        fpdfview.FPDF_CloseDocument(doc);
        return count;
    }

    #endregion

    #region PDF Generator Helper

    private static void GenerateBenchmarkPdf(string outputPath, int pageCount)
    {
        // Création d'une image factice Simulant un QR code pour le test
        using var skBitmap = new SKBitmap(250, 250);
        using (var canvas = new SKCanvas(skBitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill };
            
            // Motifs carrés de coin typiques d'un QR code
            DrawQrFinderPattern(canvas, 10, 10, paint);
            DrawQrFinderPattern(canvas, 170, 10, paint);
            DrawQrFinderPattern(canvas, 10, 170, paint);

            // Quelques points aléatoires pseudo-QR
            var rng = new Random(42);
            for (int r = 0; r < 20; r++)
            {
                for (int c = 0; c < 20; c++)
                {
                    if (rng.Next(2) == 1)
                    {
                        canvas.DrawRect(10 + c * 11, 10 + r * 11, 10, 10, paint);
                    }
                }
            }
        }

        using var ms = new MemoryStream();
        skBitmap.Encode(ms, SKEncodedImageFormat.Png, 100);
        ms.Position = 0;

        using var doc = new PdfDocument();
        var xImage = XImage.FromStream(ms);

        for (int i = 1; i <= pageCount; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);

            // 1. Dessiner des graphiques vectoriels
            var borderPen = new XPen(XColors.Navy, 2);
            gfx.DrawRectangle(borderPen, 20, 20, (int)page.Width.Point - 40, (int)page.Height.Point - 40);

            var fillBrush = new XSolidBrush(XColors.AliceBlue);
            gfx.DrawRoundedRectangle(fillBrush, 50, 50, 400, 200, 20, 20);

            var linePen = new XPen(XColors.LightSlateGray, 1);
            for (int y = 300; y < 700; y += 40)
            {
                gfx.DrawLine(linePen, 50, y, (int)page.Width.Point - 50, y);
            }

            // 2. Dessiner l'image / QR code
            gfx.DrawImage(xImage, 70, 70, 150, 150);
        }

        doc.Save(outputPath);
    }

    private static void DrawQrFinderPattern(SKCanvas canvas, float x, float y, SKPaint blackPaint)
    {
        using var whitePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        canvas.DrawRect(x, y, 70, 70, blackPaint);
        canvas.DrawRect(x + 10, y + 10, 50, 50, whitePaint);
        canvas.DrawRect(x + 20, y + 20, 30, 30, blackPaint);
    }

    #endregion
}
