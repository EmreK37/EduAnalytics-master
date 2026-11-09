using OpenCvSharp;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EduAnalytics.Ocr;

/// <summary>Örnek kâğıdı gerçek bir taramaya benzetmek için uygulanan bozulmalar.</summary>
public sealed record ScanSimulation
{
    /// <summary>Kâğıdın tarayıcıda eğik durması (derece).</summary>
    public double RotationDegrees { get; init; }

    /// <summary>Kâğıdın tarayıcı camında kayması (mm).</summary>
    public double ShiftXMm { get; init; }
    public double ShiftYMm { get; init; }

    /// <summary>Gri tonlamalı Gauss gürültüsü standart sapması (0–255 ölçeğinde).</summary>
    public double NoiseSigma { get; init; }

    /// <summary>Hafif odak kaybı (Gauss bulanıklığı) uygulansın mı?</summary>
    public bool Blur { get; init; }

    /// <summary>Sayfa ters (180°) beslenmiş gibi döndürülsün mü?</summary>
    public bool UpsideDown { get; init; }

    /// <summary>Bozulmasız (dijital) çıktı.</summary>
    public static ScanSimulation None { get; } = new();

    /// <summary>Tipik bir masaüstü tarayıcı çıktısı.</summary>
    public static ScanSimulation Typical(int seed = 0)
    {
        var rng = new Random(seed);
        return new ScanSimulation
        {
            RotationDegrees = (rng.NextDouble() - 0.5) * 3.0,   // ±1.5°
            ShiftXMm = (rng.NextDouble() - 0.5) * 6,           // ±3 mm
            ShiftYMm = (rng.NextDouble() - 0.5) * 6,
            NoiseSigma = 6,
            Blur = true
        };
    }
}

/// <summary>
/// Doldurulmuş örnek sınav kâğıdı görüntüleri üretir. Uygulamanın "Örnek Kâğıt Üret ve Oku" özelliği,
/// otomatik testler ve SampleOptics klasöründeki hazır örnekler bunu kullanır. Görüntü
/// <see cref="ExamPaperPdfGenerator"/> ile aynı şablondan çizildiği için okuyucuyla birebir uyumludur;
/// ardından isteğe bağlı olarak tarama bozulmaları eklenir.
/// </summary>
public static class ExamPaperSampleRenderer
{
    /// <summary>Doldurulmuş 1. sayfayı taranmış gibi gri tonlamalı PNG olarak döner.</summary>
    public static byte[] RenderScanPng(ExamPaperSpec spec, int bookletIndex, ExamPaperFill fill,
        ScanSimulation? simulation = null, int dpi = 200)
        => RenderScanPages(spec, bookletIndex, fill, simulation, dpi)[0];

    /// <summary>Kâğıdın tüm sayfalarını (yalnızca 1. sayfa doldurulmuş) taranmış gibi döner.</summary>
    public static IReadOnlyList<byte[]> RenderScanPages(ExamPaperSpec spec, int bookletIndex, ExamPaperFill fill,
        ScanSimulation? simulation = null, int dpi = 200)
    {
        var pages = ExamPaperPdfGenerator.RenderPngPages(spec, bookletIndex, fill, dpi);
        return pages.Select(png => Simulate(png, simulation ?? ScanSimulation.None, dpi)).ToList();
    }

    /// <summary>Örnek 1. sayfayı diske yazar (klasör yoksa oluşturur) ve dosya yolunu döner.</summary>
    public static string SaveScanPng(string path, ExamPaperSpec spec, int bookletIndex, ExamPaperFill fill,
        ScanSimulation? simulation = null, int dpi = 200)
    {
        EnsureDirectory(path);
        File.WriteAllBytes(path, RenderScanPng(spec, bookletIndex, fill, simulation, dpi));
        return path;
    }

    /// <summary>
    /// Tüm sayfaları tek bir PDF'e (tarayıcının çok sayfalı PDF çıktısı gibi) yazar. Okuyucu 1. sayfayı okur,
    /// devam sayfalarını tanıyıp atlar.
    /// </summary>
    public static string SaveScanPdf(string path, ExamPaperSpec spec, int bookletIndex, ExamPaperFill fill,
        ScanSimulation? simulation = null, int dpi = 200)
    {
        EnsureDirectory(path);
        var pages = RenderScanPages(spec, bookletIndex, fill, simulation, dpi);
        File.WriteAllBytes(path, ImagesToPdf(pages));
        return path;
    }

    /// <summary>Sayfa görüntülerinden (PNG/JPEG) A4 PDF üretir.</summary>
    public static byte[] ImagesToPdf(IEnumerable<byte[]> pageImages)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var images = pageImages.ToList();
        return Document.Create(container =>
        {
            foreach (var image in images)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0);
                    page.Content().Image(image).FitArea();
                });
            }
        }).GeneratePdf();
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }

    private static byte[] Simulate(byte[] png, ScanSimulation simulation, int dpi)
    {
        using var page = Cv2.ImDecode(png, ImreadModes.Grayscale);
        using var scanned = ApplySimulation(page, simulation, dpi);
        Cv2.ImEncode(".png", scanned, out var encoded);
        return encoded;
    }

    private static Mat ApplySimulation(Mat page, ScanSimulation sim, int dpi)
    {
        var result = page.Clone();
        var pxPerMm = dpi / 25.4;

        if (Math.Abs(sim.RotationDegrees) > 0.001 || Math.Abs(sim.ShiftXMm) > 0.001 || Math.Abs(sim.ShiftYMm) > 0.001)
        {
            var center = new Point2f(result.Width / 2f, result.Height / 2f);
            using var transform = Cv2.GetRotationMatrix2D(center, sim.RotationDegrees, 1.0);
            transform.Set(0, 2, transform.Get<double>(0, 2) + sim.ShiftXMm * pxPerMm);
            transform.Set(1, 2, transform.Get<double>(1, 2) + sim.ShiftYMm * pxPerMm);

            var warped = new Mat();
            Cv2.WarpAffine(result, warped, transform, result.Size(), InterpolationFlags.Linear,
                BorderTypes.Constant, new Scalar(255));
            result.Dispose();
            result = warped;
        }

        if (sim.Blur)
            Cv2.GaussianBlur(result, result, new OpenCvSharp.Size(3, 3), 0.8);

        if (sim.NoiseSigma > 0)
        {
            using var noise = new Mat(result.Size(), MatType.CV_16SC1);
            Cv2.Randn(noise, new Scalar(0), new Scalar(sim.NoiseSigma));
            using var wide = new Mat();
            result.ConvertTo(wide, MatType.CV_16SC1);
            Cv2.Add(wide, noise, wide);
            wide.ConvertTo(result, MatType.CV_8UC1); // doygunluk: 0–255 aralığına kırpılır
        }

        if (sim.UpsideDown)
            Cv2.Rotate(result, result, RotateFlags.Rotate180);

        return result;
    }
}
