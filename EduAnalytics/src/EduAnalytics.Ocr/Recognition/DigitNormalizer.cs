using OpenCvSharp;

namespace EduAnalytics.Ocr.Recognition;

/// <summary>
/// Tek bir el yazısı rakamını rakam modelinin girdi biçimine (MNIST biçimi) getirir: mürekkep
/// koyu zemin üzerinde açık renktedir; rakamın sınırlayıcı kutusu en-boy oranı korunarak 20×20'ye
/// sığdırılır ve kütle merkezi 28×28 tuvalin ortasına kaydırılır.
/// Model bu biçimle eğitildiği için eğitim aracı (tools/EduAnalytics.DigitTrainer) ile okuyucu
/// AYNI kodu kullanır; burası değişirse model yeniden eğitilmelidir.
/// </summary>
public static class DigitNormalizer
{
    public const int Size = 28;
    public const int PixelCount = Size * Size;

    private const int FitSize = 20;
    private const double InkFloor = 24;
    private const double MaxCenteringShift = 4;

    /// <param name="ink">CV_8UC1 görüntü: mürekkep parlak (0–255), zemin 0. Yalnızca tek bir rakam içermelidir.</param>
    /// <returns>784 elemanlı, 0–1 aralığında dizi; görüntüde mürekkep yoksa null.</returns>
    public static float[]? Normalize(Mat ink)
    {
        using var mask = new Mat();
        Cv2.Threshold(ink, mask, InkFloor, 255, ThresholdTypes.Binary);
        var bounds = Cv2.BoundingRect(mask);
        if (bounds.Width == 0 || bounds.Height == 0)
            return null;

        var scale = FitSize / (double)Math.Max(bounds.Width, bounds.Height);
        var width = Math.Clamp((int)Math.Round(bounds.Width * scale), 1, FitSize);
        var height = Math.Clamp((int)Math.Round(bounds.Height * scale), 1, FitSize);

        using var roi = new Mat(ink, bounds);
        using var resized = new Mat();
        Cv2.Resize(roi, resized, new Size(width, height), 0, 0,
            scale < 1 ? InterpolationFlags.Area : InterpolationFlags.Linear);

        using var canvas = new Mat(Size, Size, MatType.CV_8UC1, Scalar.Black);
        using (var target = new Mat(canvas, new Rect((Size - width) / 2, (Size - height) / 2, width, height)))
            resized.CopyTo(target);

        var moments = Cv2.Moments(canvas);
        using var centered = new Mat();
        if (moments.M00 > 0)
        {
            var dx = Math.Clamp(Size / 2.0 - moments.M10 / moments.M00, -MaxCenteringShift, MaxCenteringShift);
            var dy = Math.Clamp(Size / 2.0 - moments.M01 / moments.M00, -MaxCenteringShift, MaxCenteringShift);
            using var shift = new Mat(2, 3, MatType.CV_64FC1);
            shift.Set(0, 0, 1.0); shift.Set(0, 1, 0.0); shift.Set(0, 2, dx);
            shift.Set(1, 0, 0.0); shift.Set(1, 1, 1.0); shift.Set(1, 2, dy);
            Cv2.WarpAffine(canvas, centered, shift, canvas.Size(), InterpolationFlags.Linear,
                BorderTypes.Constant, Scalar.Black);
        }
        else
        {
            canvas.CopyTo(centered);
        }

        var pixels = new byte[PixelCount];
        System.Runtime.InteropServices.Marshal.Copy(centered.Data, pixels, 0, PixelCount);
        var result = new float[PixelCount];
        for (var i = 0; i < PixelCount; i++)
            result[i] = pixels[i] / 255f;
        return result;
    }
}
