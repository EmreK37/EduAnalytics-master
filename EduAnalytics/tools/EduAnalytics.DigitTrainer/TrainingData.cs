using System.IO.Compression;
using EduAnalytics.Ocr;
using EduAnalytics.Ocr.Recognition;
using OpenCvSharp;
using SkiaSharp;

namespace EduAnalytics.DigitTrainer;

/// <summary>Eğitim örneği: 28×28 temel görüntü (mürekkep parlak) ve etiketi.</summary>
internal readonly record struct Sample(byte[] Pixels, int Label);

internal static class TrainingData
{
    /// <summary>MNIST idx dosyalarını okur (.gz ya da açılmış hâli).</summary>
    public static List<Sample> LoadMnist(string directory, string prefix)
    {
        var images = ReadIdx(Path.Combine(directory, $"{prefix}-images-idx3-ubyte"));
        var labels = ReadIdx(Path.Combine(directory, $"{prefix}-labels-idx1-ubyte"));
        const int headerImages = 16, headerLabels = 8;
        var count = (labels.Length - headerLabels);
        var samples = new List<Sample>(count);
        for (var i = 0; i < count; i++)
        {
            var pixels = new byte[DigitNormalizer.PixelCount];
            Buffer.BlockCopy(images, headerImages + i * DigitNormalizer.PixelCount, pixels, 0, pixels.Length);
            samples.Add(new Sample(pixels, labels[headerLabels + i]));
        }
        return samples;
    }

    private static byte[] ReadIdx(string path)
    {
        if (File.Exists(path))
            return File.ReadAllBytes(path);
        using var gz = new GZipStream(File.OpenRead(path + ".gz"), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Windows yazı tiplerinden (el yazısına benzeyenler ağırlıklı) rakam örnekleri üretir.
    /// Bulunamayan yazı tipi atlanır.
    /// </summary>
    public static List<Sample> RenderFontDigits(IEnumerable<string> fontFiles, int variantsPerDigit, Random rng)
    {
        var samples = new List<Sample>();
        foreach (var file in fontFiles)
        {
            if (!File.Exists(file))
            {
                Console.WriteLine($"  (yazı tipi yok, atlandı: {Path.GetFileName(file)})");
                continue;
            }

            using var typeface = SKTypeface.FromFile(file);
            if (typeface == null)
                continue;

            for (var digit = 0; digit <= 9; digit++)
            {
                for (var v = 0; v < variantsPerDigit; v++)
                {
                    using var bitmap = new SKBitmap(new SKImageInfo(96, 96, SKColorType.Gray8, SKAlphaType.Opaque));
                    using (var canvas = new SKCanvas(bitmap))
                    {
                        canvas.Clear(SKColors.Black);
                        using var paint = new SKPaint
                        {
                            Typeface = typeface,
                            TextSize = 52 + rng.Next(14),
                            IsAntialias = true,
                            Color = SKColors.White,
                            FakeBoldText = rng.NextDouble() < 0.3,
                            TextSkewX = (float)((rng.NextDouble() - 0.5) * 0.3)
                        };
                        canvas.DrawText(digit.ToString(), 24, 72, paint);
                    }

                    using var mat = Mat.FromPixelData(96, 96, MatType.CV_8UC1, bitmap.GetPixels(), bitmap.RowBytes);
                    var normalized = DigitNormalizer.Normalize(mat);
                    if (normalized == null)
                        continue;
                    samples.Add(new Sample(normalized.Select(f => (byte)Math.Round(f * 255)).ToArray(), digit));
                }
            }
        }
        return samples;
    }

    /// <summary>
    /// Örnek kâğıtların "el yazısı" üreticisiyle (SyntheticHandwriting) rakamlar çizer ve okuyucunun
    /// izleyeceği yolu taklit ederek modele hazırlar. Yalnızca SINAMA için kullanılır, eğitime girmez.
    /// </summary>
    public static List<(float[] Input, int Label)> SyntheticHandwritingSet(int count, Random rng)
    {
        const double pxPerMm = 8;
        var result = new List<(float[], int)>(count);
        for (var i = 0; i < count; i++)
        {
            var digit = rng.Next(10);
            var style = SyntheticHandwriting.WriterStyle.Random(rng);
            var height = 4.5 + rng.NextDouble() * 3;
            var ink = SyntheticHandwriting.Write(digit.ToString(), 10, 12, height, style, rng);

            using var page = new Mat(160, 160, MatType.CV_8UC1, Scalar.White);
            SyntheticHandwriting.Draw(page, ink, pxPerMm);
            using var inverted = new Mat();
            Cv2.BitwiseNot(page, inverted);
            var input = DigitNormalizer.Normalize(inverted);
            if (input != null)
                result.Add((input, digit));
        }
        return result;
    }
}
