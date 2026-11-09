using EduAnalytics.Core.Enums;
using EduAnalytics.Ocr.Models;
using OpenCvSharp;

namespace EduAnalytics.Ocr.Reading;

/// <summary>
/// OMR: kanonik sayfadaki baloncukların ve sayfa kodu karelerinin doluluk oranını ölçer.
/// Bir baloncuğun karalanıp karalanmadığı piksel sayma işidir; metin tanıma kullanılmaz.
/// </summary>
public static class BubbleReader
{
    /// <summary>Bu oranın üstü kesin dolu kabul edilir.</summary>
    private const double FilledThreshold = 0.40;

    /// <summary>Bu oranın üstü (ama dolu eşiğinin altı) silik işaret sayılır ve incelemeye düşer.</summary>
    private const double WeakThreshold = 0.26;

    /// <summary>Sayfa kodu karesi bu orandan doluysa "dolu" sayılır (boş çerçevede yalnızca kenar çizgisi vardır).</summary>
    private const double PageCodeFilledThreshold = 0.6;

    /// <summary>
    /// Kanonik gri sayfadan mürekkep maskesi üretir (mürekkep = beyaz piksel).
    /// Erode adımı, baloncukların içine basılı açık gri şık harflerinin ince çizgilerini
    /// siler; kurşun kalem dolgusu bloktur, erode'dan etkilenmez.
    /// </summary>
    public static Mat PrepareInkMask(Mat canonicalGray)
    {
        var ink = new Mat();
        Cv2.AdaptiveThreshold(canonicalGray, ink, 255,
            AdaptiveThresholdTypes.GaussianC, ThresholdTypes.BinaryInv, blockSize: 51, c: 15);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.Erode(ink, ink, kernel, iterations: 1);
        return ink;
    }

    /// <summary>
    /// Sayfa kodunu okur: yön karesi dolu mu (sayfa bu yönde mi duruyor) ve ilk sayfa karesi dolu mu.
    /// Kareler büyük ve tam dolu olduğu için Otsu eşiği yeterlidir.
    /// </summary>
    public static (bool Oriented, bool FirstPage) ReadPageCode(Mat canonicalGray)
    {
        using var binary = new Mat();
        Cv2.Threshold(canonicalGray, binary, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        var oriented = SquareFill(binary, OpticalFormTemplate.OrientationSlot) >= PageCodeFilledThreshold;
        var firstPage = SquareFill(binary, OpticalFormTemplate.FirstPageSlot) >= PageCodeFilledThreshold;
        return (oriented, firstPage);
    }

    /// <summary>Kare içinin (kenar çizgisi hariç) mürekkep oranı.</summary>
    private static double SquareFill(Mat binary, MmPoint centerMm)
    {
        var px = OpticalFormTemplate.PxPerMm;
        var half = (OpticalFormTemplate.PageCodeSizeMm / 2 - 0.6) * px;
        var rect = new Rect((int)Math.Round(centerMm.X * px - half), (int)Math.Round(centerMm.Y * px - half),
            (int)Math.Round(half * 2), (int)Math.Round(half * 2));
        rect &= new Rect(0, 0, binary.Width, binary.Height);
        if (rect.Width <= 0 || rect.Height <= 0)
            return 0;
        using var roi = new Mat(binary, rect);
        return Cv2.CountNonZero(roi) / (double)(rect.Width * rect.Height);
    }

    /// <summary>Tek test sorusunun baloncuk satırını okur (mcIndex = kâğıttaki test soruları sırasıyla, 0-bazlı).</summary>
    public static BubbleReadResult ReadAnswerRow(Mat inkMask, ExamPaperLayout layout, int mcIndex)
    {
        var ratios = new double[OpticalFormTemplate.OptionCount];
        for (var opt = 0; opt < OpticalFormTemplate.OptionCount; opt++)
            ratios[opt] = FillRatio(inkMask, layout.AnswerBubbleCenter(mcIndex, opt), OpticalFormTemplate.BubbleRadiusMm);

        var result = Decide(ratios, out var optionIndex);
        result.FormIndex = mcIndex;
        result.Option = optionIndex >= 0 ? (OptionLetter)(optionIndex + 1) : OptionLetter.Empty;
        return result;
    }

    /// <summary>Kitapçık baloncuklarını okur; işaretli (basılı kâğıtta önceden dolu) kitapçığın index'ini ve güveni döner.</summary>
    public static (int? Index, double Confidence, bool Ambiguous) ReadBookletRow(Mat inkMask, ExamPaperLayout layout, int bookletCount)
    {
        var ratios = new double[bookletCount];
        for (var i = 0; i < bookletCount; i++)
            ratios[i] = FillRatio(inkMask, layout.BookletBubbleCenter(i), OpticalFormTemplate.BubbleRadiusMm);

        var decision = Decide(ratios, out var index);
        if (decision.MultipleMarks || index < 0)
            return (null, 0, decision.MultipleMarks || decision.WeakMark);
        return (index, decision.Confidence, decision.WeakMark);
    }

    /// <summary>Verilen yarıçaptaki baloncuğun iç diskindeki mürekkep oranı (0–1). Çemberin kendi çizgisi dışarıda bırakılır.</summary>
    public static double FillRatio(Mat inkMask, MmPoint centerMm, double bubbleRadiusMm)
    {
        var cx = (int)Math.Round(centerMm.X * OpticalFormTemplate.PxPerMm);
        var cy = (int)Math.Round(centerMm.Y * OpticalFormTemplate.PxPerMm);
        var radius = (int)Math.Round((bubbleRadiusMm - 0.4) * OpticalFormTemplate.PxPerMm);

        var roiRect = new Rect(cx - radius, cy - radius, radius * 2 + 1, radius * 2 + 1);
        roiRect &= new Rect(0, 0, inkMask.Width, inkMask.Height);
        if (roiRect.Width <= 0 || roiRect.Height <= 0)
            return 0;

        using var roi = new Mat(inkMask, roiRect);
        using var diskMask = new Mat(roiRect.Height, roiRect.Width, MatType.CV_8UC1, Scalar.Black);
        Cv2.Circle(diskMask, new Point(cx - roiRect.X, cy - roiRect.Y), radius, Scalar.White, thickness: -1);

        using var masked = new Mat();
        Cv2.BitwiseAnd(roi, diskMask, masked);

        var diskArea = Cv2.CountNonZero(diskMask);
        return diskArea == 0 ? 0 : Cv2.CountNonZero(masked) / (double)diskArea;
    }

    private static BubbleReadResult Decide(double[] ratios, out int optionIndex)
    {
        var best = -1;
        var second = -1;
        for (var i = 0; i < ratios.Length; i++)
        {
            if (best < 0 || ratios[i] > ratios[best]) { second = best; best = i; }
            else if (second < 0 || ratios[i] > ratios[second]) { second = i; }
        }

        var bestRatio = best >= 0 ? ratios[best] : 0;
        var secondRatio = second >= 0 ? ratios[second] : 0;
        var filledCount = ratios.Count(r => r >= FilledThreshold);

        var result = new BubbleReadResult();

        if (filledCount >= 2)
        {
            // Çift işaret: karar verme, incelemeye bırak.
            result.MultipleMarks = true;
            optionIndex = -1;
            return result;
        }

        if (filledCount == 1)
        {
            optionIndex = best;
            result.Confidence = Math.Clamp((bestRatio - secondRatio) * 2, 0.05, 1);
            return result;
        }

        if (bestRatio >= WeakThreshold)
        {
            // Silik işaret: en olası şıkkı öner ama düşük güvenle işaretle.
            optionIndex = best;
            result.WeakMark = true;
            result.Confidence = Math.Clamp((bestRatio - WeakThreshold) / (FilledThreshold - WeakThreshold) * 0.5, 0.05, 0.5);
            return result;
        }

        // Boş satır.
        optionIndex = -1;
        result.Confidence = Math.Clamp(1 - bestRatio / WeakThreshold, 0, 1);
        return result;
    }
}
