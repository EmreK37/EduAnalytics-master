using System.Globalization;
using System.Text;
using OpenCvSharp;

namespace EduAnalytics.Ocr;

/// <summary>
/// Örnek (simülasyon) sınav kâğıtlarında rakamları "elle yazılmış" gibi çizer: her rakam kalem
/// darbelerinden (stroke) oluşur; eğim, boyut, en-boy, titreme ve yazım biçimi (ör. çizgili 7,
/// bayraklı 1, açık/kapalı 4) her çizimde rastgele değişir.
///
/// Rakam modeli bu üreticiyle EĞİTİLMEZ (MNIST + yazı tipleriyle eğitilir); böylece örnek
/// kâğıtların okunması modelin daha önce görmediği bir el yazısıyla yapılan bağımsız bir sınamadır.
/// </summary>
public static class SyntheticHandwriting
{
    /// <summary>Bir yazarın (öğrenci/öğretmen) tüm yazısında sabit kalan özellikleri.</summary>
    public sealed record WriterStyle(double Slant, double StrokeWidthMm, double WidthScale, bool CrossedSeven,
        bool FlaggedOne, bool OpenFour, bool StraightNineTail)
    {
        public static WriterStyle Random(Random rng) => new(
            Slant: 0.04 + rng.NextDouble() * 0.2,
            StrokeWidthMm: 0.32 + rng.NextDouble() * 0.16,
            WidthScale: 0.85 + rng.NextDouble() * 0.25,
            CrossedSeven: rng.NextDouble() < 0.5,
            FlaggedOne: rng.NextDouble() < 0.7,
            OpenFour: rng.NextDouble() < 0.5,
            StraightNineTail: rng.NextDouble() < 0.6);
    }

    /// <summary>mm cinsinden kalem darbeleri (her biri çoklu doğru) ve kalem kalınlığı.</summary>
    public sealed record Ink(IReadOnlyList<IReadOnlyList<MmPoint>> Strokes, double StrokeWidthMm);

    /// <summary>
    /// Metni (rakamlar, ',', '.', '-') verilen konuma yazar. <paramref name="centerX"/> metnin yatay
    /// ortası, <paramref name="baselineY"/> rakamların alt çizgisidir; <paramref name="heightMm"/> rakam boyu.
    /// </summary>
    public static Ink Write(string text, double centerX, double baselineY, double heightMm, WriterStyle style, Random rng)
    {
        var glyphs = text.Where(c => char.IsDigit(c) || c is ',' or '.' or '-').ToList();
        var widths = glyphs.Select(c => GlyphWidth(c) * heightMm * style.WidthScale * (0.92 + rng.NextDouble() * 0.16)).ToList();
        var gap = heightMm * 0.16;
        var total = widths.Sum() + gap * Math.Max(0, glyphs.Count - 1);

        var strokes = new List<IReadOnlyList<MmPoint>>();
        var x = centerX - total / 2;
        for (var i = 0; i < glyphs.Count; i++)
        {
            var h = heightMm * (0.93 + rng.NextDouble() * 0.14);
            var top = baselineY - h + (rng.NextDouble() - 0.5) * heightMm * 0.08;
            foreach (var stroke in GlyphStrokes(glyphs[i], style, rng))
            {
                var points = stroke.Smooth ? CatmullRom(stroke.Points, 8) : stroke.Points;
                strokes.Add(points
                    .Select(p => Jitter(p, rng, stroke.Smooth ? 0.018 : 0.012))
                    .Select(p => new MmPoint(x + p.X * widths[i] + style.Slant * (1 - p.Y) * h, top + p.Y * h))
                    .ToList());
            }
            x += widths[i] + gap;
        }

        return new Ink(strokes, style.StrokeWidthMm * (0.9 + rng.NextDouble() * 0.2));
    }

    /// <summary>Metnin verilen yükseklikte yaklaşık genişliği (yerleşim/sığdırma için; titreme hariç).</summary>
    public static double NaturalWidth(string text, double heightMm, WriterStyle style)
    {
        var glyphs = text.Where(c => char.IsDigit(c) || c is ',' or '.' or '-').ToList();
        return glyphs.Sum(c => GlyphWidth(c) * heightMm * style.WidthScale) + heightMm * 0.16 * Math.Max(0, glyphs.Count - 1)
               + style.Slant * heightMm;
    }

    /// <summary>İmza/paraf karalaması: alanın içinde birkaç ilmekli tek darbe.</summary>
    public static Ink Signature(MmRect area, WriterStyle style, Random rng)
    {
        var points = new List<MmPoint>();
        var loops = 3 + rng.Next(3);
        for (var i = 0; i <= loops * 2; i++)
        {
            var t = i / (double)(loops * 2);
            var x = area.X + area.Width * (0.08 + 0.84 * t) + (rng.NextDouble() - 0.5) * area.Width * 0.08;
            var y = area.Y + area.Height * (i % 2 == 0 ? 0.75 : 0.2) + (rng.NextDouble() - 0.5) * area.Height * 0.2;
            points.Add(new MmPoint(x, y));
        }
        points.Add(new MmPoint(area.X + area.Width * 0.15, area.Y + area.Height * 0.55));
        return new Ink(new[] { CatmullRom(points, 6) }, style.StrokeWidthMm);
    }

    /// <summary>Darbeleri SVG yolları olarak döner (1 SVG birimi = 1 mm).</summary>
    public static string ToSvg(Ink ink, string color)
    {
        var sb = new StringBuilder();
        foreach (var stroke in ink.Strokes)
        {
            if (stroke.Count == 0)
                continue;
            sb.Append("<path d=\"M").Append(F(stroke[0].X)).Append(' ').Append(F(stroke[0].Y));
            for (var i = 1; i < stroke.Count; i++)
                sb.Append(" L").Append(F(stroke[i].X)).Append(' ').Append(F(stroke[i].Y));
            if (stroke.Count == 1)
                sb.Append(" l0.01 0");
            sb.Append($"\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{F(ink.StrokeWidthMm)}\" ")
              .Append("stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
        }
        return sb.ToString();
    }

    /// <summary>Darbeleri gri bir görüntüye (beyaz zemin) çizer; testler ve model sınaması için.</summary>
    public static void Draw(Mat canvas, Ink ink, double pxPerMm, double offsetXMm = 0, double offsetYMm = 0)
    {
        var thickness = Math.Max(1, (int)Math.Round(ink.StrokeWidthMm * pxPerMm));
        foreach (var stroke in ink.Strokes)
        {
            var pts = stroke.Select(p => new Point(
                (int)Math.Round((p.X - offsetXMm) * pxPerMm),
                (int)Math.Round((p.Y - offsetYMm) * pxPerMm))).ToArray();
            if (pts.Length == 1)
                Cv2.Circle(canvas, pts[0], Math.Max(1, thickness / 2), new Scalar(30), -1, LineTypes.AntiAlias);
            else
                Cv2.Polylines(canvas, new[] { pts }, false, new Scalar(30), thickness, LineTypes.AntiAlias);
        }
    }

    private static double GlyphWidth(char c) => c switch
    {
        '1' => 0.38,
        ',' or '.' => 0.22,
        '-' => 0.5,
        _ => 0.62
    };

    private sealed record Stroke(IReadOnlyList<MmPoint> Points, bool Smooth);

    private static Stroke S(params double[] xy) => new(Pairs(xy), true);
    private static Stroke L(params double[] xy) => new(Pairs(xy), false);

    private static List<MmPoint> Pairs(double[] xy)
        => Enumerable.Range(0, xy.Length / 2).Select(i => new MmPoint(xy[2 * i], xy[2 * i + 1])).ToList();

    /// <summary>Rakam iskeletleri: birim kutu (x 0–1 soldan sağa, y 0–1 yukarıdan aşağı).</summary>
    private static IEnumerable<Stroke> GlyphStrokes(char c, WriterStyle style, Random rng)
    {
        switch (c)
        {
            case '0':
                yield return S(0.52, 0.0, 0.18, 0.12, 0.03, 0.5, 0.18, 0.9, 0.5, 1.0, 0.85, 0.88, 0.97, 0.5, 0.82, 0.1, 0.45, 0.02);
                break;
            case '1':
                if (style.FlaggedOne)
                    yield return L(0.1, 0.3, 0.75, 0.0, 0.75, 1.0);
                else
                    yield return L(0.6, 0.0, 0.6, 1.0);
                if (rng.NextDouble() < 0.15)
                    yield return L(0.25, 1.0, 1.0, 1.0);
                break;
            case '2':
                yield return S(0.08, 0.25, 0.3, 0.02, 0.65, 0.0, 0.88, 0.18, 0.82, 0.42, 0.45, 0.72, 0.06, 0.99);
                yield return L(0.06, 0.99, 0.98, 0.97);
                break;
            case '3':
                yield return S(0.1, 0.12, 0.45, 0.0, 0.82, 0.12, 0.75, 0.38, 0.42, 0.48);
                yield return S(0.42, 0.48, 0.86, 0.6, 0.9, 0.86, 0.52, 1.0, 0.08, 0.88);
                break;
            case '4':
                if (style.OpenFour)
                {
                    yield return L(0.35, 0.0, 0.06, 0.66, 0.98, 0.66);
                    yield return L(0.74, 0.28, 0.74, 1.0);
                }
                else
                {
                    yield return L(0.72, 1.0, 0.72, 0.0, 0.04, 0.68, 0.98, 0.68);
                }
                break;
            case '5':
                yield return L(0.9, 0.02, 0.24, 0.02, 0.18, 0.44);
                yield return S(0.18, 0.44, 0.5, 0.36, 0.86, 0.52, 0.9, 0.8, 0.55, 1.0, 0.08, 0.9);
                break;
            case '6':
                yield return S(0.78, 0.02, 0.42, 0.18, 0.13, 0.58, 0.18, 0.92, 0.52, 1.0, 0.85, 0.84, 0.82, 0.58, 0.5, 0.48, 0.17, 0.64);
                break;
            case '7':
                yield return L(0.05, 0.03, 0.95, 0.03, 0.38, 1.0);
                if (style.CrossedSeven)
                    yield return L(0.42, 0.52, 0.9, 0.5);
                break;
            case '8':
                yield return S(0.5, 0.47, 0.16, 0.28, 0.26, 0.04, 0.52, 0.0, 0.8, 0.06, 0.84, 0.28, 0.5, 0.47,
                               0.12, 0.7, 0.2, 0.95, 0.5, 1.0, 0.82, 0.94, 0.88, 0.7, 0.5, 0.47);
                break;
            case '9':
                yield return S(0.84, 0.28, 0.62, 0.03, 0.28, 0.04, 0.12, 0.3, 0.3, 0.52, 0.62, 0.5, 0.84, 0.28);
                if (style.StraightNineTail)
                    yield return L(0.84, 0.22, 0.76, 1.0);
                else
                    yield return S(0.84, 0.22, 0.8, 0.7, 0.52, 1.0, 0.2, 0.92);
                break;
            case ',':
                yield return L(0.55, 0.86, 0.35, 1.12);
                break;
            case '.':
                yield return L(0.5, 0.94, 0.52, 0.97);
                break;
            case '-':
                yield return L(0.05, 0.55, 0.95, 0.52);
                break;
        }
    }

    private static MmPoint Jitter(MmPoint p, Random rng, double sigma)
        => new(p.X + Gaussian(rng) * sigma, p.Y + Gaussian(rng) * sigma);

    private static double Gaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    /// <summary>Kontrol noktalarından geçen yumuşak eğri (Catmull-Rom), çoklu doğruya açılmış.</summary>
    private static List<MmPoint> CatmullRom(IReadOnlyList<MmPoint> p, int segmentsPerSpan)
    {
        var result = new List<MmPoint> { p[0] };
        for (var i = 0; i < p.Count - 1; i++)
        {
            var p0 = p[Math.Max(0, i - 1)];
            var p1 = p[i];
            var p2 = p[i + 1];
            var p3 = p[Math.Min(p.Count - 1, i + 2)];
            for (var s = 1; s <= segmentsPerSpan; s++)
            {
                var t = s / (double)segmentsPerSpan;
                var t2 = t * t;
                var t3 = t2 * t;
                result.Add(new MmPoint(
                    0.5 * (2 * p1.X + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3),
                    0.5 * (2 * p1.Y + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3)));
            }
        }
        return result;
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
