using EduAnalytics.Ocr.Recognition;
using OpenCvSharp;

namespace EduAnalytics.DigitTrainer;

/// <summary>
/// Eğitim örneklerini tarama/el yazısı çeşitliliğine benzetir: eğim, yatık yazı (shear), en-boy,
/// kalem kalınlığı, elastik bozulma ve Türkiye'de yaygın yazım biçimleri (ortası çizgili 7,
/// bayraklı/tabanlı 1). Sonuç okuyucuyla aynı <see cref="DigitNormalizer"/> adımından geçer.
/// </summary>
internal static class Augmenter
{
    private const int Canvas = 96;
    private const int Scale = 2;

    public static float[]? Augment(Sample sample, Random rng)
    {
        using var canvas = new Mat(Canvas, Canvas, MatType.CV_8UC1, Scalar.Black);
        using (var src = Mat.FromPixelData(DigitNormalizer.Size, DigitNormalizer.Size, MatType.CV_8UC1, sample.Pixels))
        using (var up = new Mat())
        {
            Cv2.Resize(src, up, new Size(DigitNormalizer.Size * Scale, DigitNormalizer.Size * Scale), 0, 0, InterpolationFlags.Linear);
            var offset = (Canvas - DigitNormalizer.Size * Scale) / 2;
            using var target = new Mat(canvas, new Rect(offset, offset, up.Width, up.Height));
            up.CopyTo(target);
        }

        if (sample.Label == 7 && rng.NextDouble() < 0.45)
            AddCrossbar(canvas, rng);
        if (sample.Label == 1)
        {
            if (rng.NextDouble() < 0.45)
                AddFlag(canvas, rng);
            if (rng.NextDouble() < 0.12)
                AddBase(canvas, rng);
        }

        using var warped = RandomAffine(canvas, rng);

        if (rng.NextDouble() < 0.45)
            Elastic(warped, rng);

        // Kalemin kâğıttan kalktığı yerler: çizginin bir bölümü kopuk (ör. tam kapanmamış 0, açık 6/9).
        if (rng.NextDouble() < 0.2)
            EraseGap(warped, rng);

        var r = rng.NextDouble();
        if (r < 0.3)
        {
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
            Cv2.Dilate(warped, warped, kernel, iterations: rng.NextDouble() < 0.3 ? 2 : 1);
        }
        else if (r < 0.42)
        {
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
            using var eroded = new Mat();
            Cv2.Erode(warped, eroded, kernel);
            // İnce çizgiler tamamen kaybolmasın: ancak mürekkebin çoğu kalıyorsa incelt.
            if (Cv2.CountNonZero(eroded) > Cv2.CountNonZero(warped) * 0.45)
                eroded.CopyTo(warped);
        }

        return DigitNormalizer.Normalize(warped);
    }

    /// <summary>Artırmasız giriş (sınama kümeleri için).</summary>
    public static float[] Plain(Sample sample)
    {
        using var src = Mat.FromPixelData(DigitNormalizer.Size, DigitNormalizer.Size, MatType.CV_8UC1, sample.Pixels);
        return DigitNormalizer.Normalize(src) ?? new float[DigitNormalizer.PixelCount];
    }

    private static Mat RandomAffine(Mat src, Random rng)
    {
        var angle = (rng.NextDouble() - 0.5) * 2 * 14 * Math.PI / 180;
        var shear = (rng.NextDouble() - 0.5) * 2 * 0.32;
        var sx = 0.78 + rng.NextDouble() * 0.44;
        var sy = 0.9 + rng.NextDouble() * 0.2;

        // M = R · Shear · S  (merkez etrafında)
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var a = cos * sx + (-sin) * 0;
        var b = cos * shear * sy + (-sin) * sy;
        var c = sin * sx;
        var d = sin * shear * sy + cos * sy;
        var center = Canvas / 2.0;
        var tx = center - (a * center + b * center);
        var ty = center - (c * center + d * center);

        using var m = new Mat(2, 3, MatType.CV_64FC1);
        m.Set(0, 0, a); m.Set(0, 1, b); m.Set(0, 2, tx);
        m.Set(1, 0, c); m.Set(1, 1, d); m.Set(1, 2, ty);

        var dst = new Mat();
        Cv2.WarpAffine(src, dst, m, src.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
        return dst;
    }

    private static readonly Mat GridX = Grid(horizontal: true);
    private static readonly Mat GridY = Grid(horizontal: false);

    private static Mat Grid(bool horizontal)
    {
        var data = new float[Canvas * Canvas];
        for (var y = 0; y < Canvas; y++)
            for (var x = 0; x < Canvas; x++)
                data[y * Canvas + x] = horizontal ? x : y;
        var grid = new Mat(Canvas, Canvas, MatType.CV_32FC1);
        grid.SetArray(data);
        return grid;
    }

    /// <summary>Simard tarzı elastik bozulma: düzgünleştirilmiş rastgele yer değiştirme alanı.</summary>
    private static void Elastic(Mat img, Random rng)
    {
        var alpha = 60 + rng.NextDouble() * 60;   // σ=7 ile ≈1.5–3 px (2× ölçekte) yer değiştirme
        const double sigma = 7;
        using var dx = RandomField(rng, sigma, alpha);
        using var dy = RandomField(rng, sigma, alpha);
        using var mapX = new Mat();
        using var mapY = new Mat();
        Cv2.Add(GridX, dx, mapX);
        Cv2.Add(GridY, dy, mapY);
        using var result = new Mat();
        Cv2.Remap(img, result, mapX, mapY, InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
        result.CopyTo(img);
    }

    private static Mat RandomField(Random rng, double sigma, double alpha)
    {
        var data = new float[Canvas * Canvas];
        for (var i = 0; i < data.Length; i++)
            data[i] = (float)(rng.NextDouble() * 2 - 1);
        var field = new Mat(Canvas, Canvas, MatType.CV_32FC1);
        field.SetArray(data);
        Cv2.GaussianBlur(field, field, new Size(0, 0), sigma);
        Cv2.Multiply(field, new Scalar(alpha), field);
        return field;
    }

    private static Rect InkBounds(Mat img)
    {
        using var mask = new Mat();
        Cv2.Threshold(img, mask, 60, 255, ThresholdTypes.Binary);
        return Cv2.BoundingRect(mask);
    }

    private static int StrokeThickness(Mat img, Rect bounds)
    {
        // Mürekkep alanı / yükseklik ≈ kalem kalınlığı (dikey çizgili rakamlar için kaba bir tahmin).
        using var mask = new Mat();
        Cv2.Threshold(img, mask, 60, 255, ThresholdTypes.Binary);
        var area = Cv2.CountNonZero(mask);
        var estimate = area / (double)Math.Max(1, bounds.Height * 2.2);
        return Math.Clamp((int)Math.Round(estimate), 3, 9);
    }

    /// <summary>Mürekkep üzerindeki rastgele bir noktada çizgiyi kısa bir boşlukla keser.</summary>
    private static void EraseGap(Mat img, Random rng)
    {
        var b = InkBounds(img);
        if (b.Height < 10)
            return;
        using var mask = new Mat();
        Cv2.Threshold(img, mask, 100, 255, ThresholdTypes.Binary);
        using var inkPoints = new Mat();
        Cv2.FindNonZero(mask, inkPoints);
        if (inkPoints.Empty())
            return;
        var center = inkPoints.At<Point>(rng.Next(inkPoints.Rows), 0);
        var w = (int)(b.Width * (0.18 + rng.NextDouble() * 0.15)) + 2;
        var h = (int)(b.Height * (0.12 + rng.NextDouble() * 0.12)) + 2;
        img.Rectangle(new Rect(center.X - w / 2, center.Y - h / 2, w, h), Scalar.Black, -1);
    }

    /// <summary>7'nin gövdesine yatay çizgi (Avrupa yazımı).</summary>
    private static void AddCrossbar(Mat img, Random rng)
    {
        var b = InkBounds(img);
        if (b.Height < 10)
            return;
        var y = b.Y + (int)(b.Height * (0.45 + rng.NextDouble() * 0.12));
        // O satırdaki mürekkep koşusunun ortası = gövde.
        int first = -1, last = -1;
        for (var x = b.X; x < b.Right; x++)
        {
            if (img.At<byte>(y, x) > 100)
            {
                if (first < 0) first = x;
                last = x;
            }
        }
        if (first < 0)
            return;
        var stem = (first + last) / 2;
        var half = (int)(b.Width * (0.22 + rng.NextDouble() * 0.12));
        var tilt = (int)((rng.NextDouble() - 0.5) * 6);
        Cv2.Line(img, new Point(stem - half, y + tilt), new Point(stem + half, y - tilt),
            Scalar.White, StrokeThickness(img, b), LineTypes.AntiAlias);
    }

    /// <summary>1'in tepesine sola-aşağı inen bayrak çizgisi.</summary>
    private static void AddFlag(Mat img, Random rng)
    {
        var b = InkBounds(img);
        if (b.Height < 10)
            return;
        var topY = b.Y + 1;
        int first = -1, last = -1;
        for (var x = b.X; x < b.Right; x++)
        {
            if (img.At<byte>(topY, x) > 60)
            {
                if (first < 0) first = x;
                last = x;
            }
        }
        if (first < 0)
            return;
        var top = new Point((first + last) / 2, topY);
        var length = b.Height * (0.18 + rng.NextDouble() * 0.2);
        var angle = (35 + rng.NextDouble() * 20) * Math.PI / 180; // dikeyden sapma
        var end = new Point(top.X - (int)(Math.Sin(angle) * length), top.Y + (int)(Math.Cos(angle) * length));
        Cv2.Line(img, top, end, Scalar.White, StrokeThickness(img, b), LineTypes.AntiAlias);
    }

    /// <summary>1'in altına taban çizgisi.</summary>
    private static void AddBase(Mat img, Random rng)
    {
        var b = InkBounds(img);
        if (b.Height < 10)
            return;
        var y = b.Bottom - 2;
        var cx = b.X + b.Width / 2;
        var half = (int)(b.Height * (0.18 + rng.NextDouble() * 0.1));
        Cv2.Line(img, new Point(cx - half, y), new Point(cx + half, y), Scalar.White, StrokeThickness(img, b), LineTypes.AntiAlias);
    }
}
