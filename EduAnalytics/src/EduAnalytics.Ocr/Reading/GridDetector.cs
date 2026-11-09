using OpenCvSharp;

namespace EduAnalytics.Ocr.Reading;

/// <summary>
/// Tablo ızgarasını (puan tablosu, numara kutuları) görüntüde bulur: şablondaki beklenen çizgi konumlarının
/// çevresinde gerçek düz çizgileri arar ve konumları onlara oturtur. Homografi sonrası kalan küçük kaymalar
/// ve kâğıdın bükülmesi böylece hücre kırpmasını bozmaz. Çizgi bulunamazsa şablon konumu kullanılır.
/// </summary>
public static class GridDetector
{
    private const double Px = OpticalFormTemplate.PxPerMm;

    /// <param name="expectedXs">Beklenen dikey çizgilerin x'leri (mm).</param>
    /// <param name="expectedYs">Beklenen yatay çizgilerin y'leri (mm).</param>
    /// <param name="minLineLengthMm">Bir çizginin "çizgi" sayılması için en az uzunluğu (el yazısı bundan kısa kalır).</param>
    /// <param name="toleranceMm">Beklenen konumun çevresinde aranacak mesafe.</param>
    /// <returns>Düzeltilmiş konumlar (mm) ve bulunan çizgi oranı (0–1).</returns>
    public static (double[] Xs, double[] Ys, double FoundRatio) Refine(Mat canonicalGray,
        IReadOnlyList<double> expectedXs, IReadOnlyList<double> expectedYs,
        double minLineLengthMm, double toleranceMm = 1.4)
    {
        var margin = toleranceMm + 1;
        var region = new MmRect(
            expectedXs.Min() - margin, expectedYs.Min() - margin,
            expectedXs.Max() - expectedXs.Min() + 2 * margin, expectedYs.Max() - expectedYs.Min() + 2 * margin);

        var roiRect = new Rect((int)(region.X * Px), (int)(region.Y * Px), (int)(region.Width * Px), (int)(region.Height * Px))
                      & new Rect(0, 0, canonicalGray.Width, canonicalGray.Height);
        if (roiRect.Width < 10 || roiRect.Height < 10)
            return (expectedXs.ToArray(), expectedYs.ToArray(), 0);

        using var roi = new Mat(canonicalGray, roiRect);
        using var binary = new Mat();
        Cv2.AdaptiveThreshold(roi, binary, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.BinaryInv, 31, 12);

        var lengthPx = Math.Max(10, (int)(minLineLengthMm * Px));
        using var vertical = new Mat();
        using var horizontal = new Mat();
        using (var vk = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(1, Math.Min(lengthPx, roiRect.Height - 2))))
            Cv2.MorphologyEx(binary, vertical, MorphTypes.Open, vk);
        using (var hk = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(Math.Min(lengthPx, roiRect.Width - 2), 1)))
            Cv2.MorphologyEx(binary, horizontal, MorphTypes.Open, hk);

        var columnProfile = Profile(vertical, byColumn: true);
        var rowProfile = Profile(horizontal, byColumn: false);

        var found = 0;
        var xs = expectedXs.Select(x =>
        {
            var local = x * Px - roiRect.X;
            var hit = Peak(columnProfile, local, toleranceMm * Px, minStrength: lengthPx * 0.6);
            if (hit.HasValue) found++;
            return hit.HasValue ? (hit.Value + roiRect.X) / Px : x;
        }).ToArray();

        var ys = expectedYs.Select(y =>
        {
            var local = y * Px - roiRect.Y;
            var hit = Peak(rowProfile, local, toleranceMm * Px, minStrength: lengthPx * 0.6);
            if (hit.HasValue) found++;
            return hit.HasValue ? (hit.Value + roiRect.Y) / Px : y;
        }).ToArray();

        return (xs, ys, found / (double)(expectedXs.Count + expectedYs.Count));
    }

    private static double[] Profile(Mat lines, bool byColumn)
    {
        using var reduced = new Mat();
        Cv2.Reduce(lines, reduced, byColumn ? ReduceDimension.Row : ReduceDimension.Column, ReduceTypes.Sum, MatType.CV_32S);
        var length = byColumn ? reduced.Cols : reduced.Rows;
        var profile = new double[length];
        for (var i = 0; i < length; i++)
            profile[i] = (byColumn ? reduced.At<int>(0, i) : reduced.At<int>(i, 0)) / 255.0;
        return profile;
    }

    /// <summary>Beklenen konum çevresindeki en güçlü çizginin (ağırlıklı) merkezi; yeterince güçlü çizgi yoksa null.</summary>
    private static double? Peak(double[] profile, double expected, double tolerance, double minStrength)
    {
        var from = Math.Max(0, (int)Math.Floor(expected - tolerance));
        var to = Math.Min(profile.Length - 1, (int)Math.Ceiling(expected + tolerance));
        var best = -1;
        for (var i = from; i <= to; i++)
        {
            if (best < 0 || profile[i] > profile[best] ||
                (profile[i] == profile[best] && Math.Abs(i - expected) < Math.Abs(best - expected)))
                best = i;
        }
        if (best < 0 || profile[best] < minStrength)
            return null;

        // Kalın çizgide tepe birkaç piksele yayılır: komşularla ağırlıklı merkez.
        double sum = 0, weighted = 0;
        for (var i = Math.Max(0, best - 2); i <= Math.Min(profile.Length - 1, best + 2); i++)
        {
            if (profile[i] < profile[best] * 0.5)
                continue;
            sum += profile[i];
            weighted += profile[i] * i;
        }
        return sum > 0 ? weighted / sum : best;
    }
}
