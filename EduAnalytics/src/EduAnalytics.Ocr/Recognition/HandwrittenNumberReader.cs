using System.Globalization;
using EduAnalytics.Ocr.Models;
using OpenCvSharp;

namespace EduAnalytics.Ocr.Recognition;

/// <summary>
/// Kutu içine elle yazılmış sayıları okur:
///   • <see cref="ReadDigitBox"/>: tek rakamlık kutu (öğrenci numarası hanesi) → 0–9 olasılıkları.
///   • <see cref="ReadScore"/>: serbest yazılmış puan (ör. "25", "7,5") → rakamlara ayırma, her rakamı CNN ile
///     tanıma ve sorunun puan sınırını aşmayan en olası okumayı seçme.
/// Girdi kanonik (hizalanmış, 8 px/mm) gri sayfadır; kutu, çevreleyen çizgilerin merkezleri arasındaki
/// dikdörtgen olarak mm cinsinden verilir. Kutunun biraz dışı da incelenir: ızgara çizgileri silinir ve ağırlık
/// merkezi kutunun içinde kalan mürekkep alınır; böylece çizgiye değen ya da taşan rakamlar kesilmez.
/// Durum tutmaz; iş parçacıkları arasında paylaşılabilir.
/// </summary>
public sealed class HandwrittenNumberReader
{
    private readonly DigitClassifier _classifier;

    private const double Px = OpticalFormTemplate.PxPerMm;

    /// <summary>Kutuda en koyu mürekkep ile zemin arasındaki fark bundan azsa kutu boş sayılır (0–255).</summary>
    private const double MinInkContrast = 45;

    /// <summary>Bu alandan (mm²) küçük mürekkep parçaları leke/toz kabul edilir.</summary>
    private const double SpeckAreaMm2 = 0.12;

    /// <summary>Toplam mürekkep alanı bundan küçükse (mm²) kutu boş sayılır.</summary>
    private const double EmptyInkAreaMm2 = 0.5;

    public HandwrittenNumberReader(DigitClassifier? classifier = null)
        => _classifier = classifier ?? DigitClassifier.Default;

    // ─────────────────────────── Tek rakam kutusu ───────────────────────────

    /// <summary>Tek rakamlık kutuyu okur: 0–9 olasılıkları; kutu boşsa null.</summary>
    /// <param name="cell">Kutu (çizgi merkezleri arası, mm).</param>
    public float[]? ReadDigitBox(Mat canonicalGray, MmRect cell)
    {
        using var ink = InkImage.Extract(canonicalGray, cell, bordersAreLines: true);
        if (ink.Components.Count == 0)
            return null;

        // Kutudaki tüm parçalar tek rakamdır (ör. ayrı çizilmiş 5 şapkası, 7'nin orta çizgisi).
        using var digit = ink.Render(ink.Components);
        var input = DigitNormalizer.Normalize(digit);
        return input == null ? null : _classifier.Predict(input);
    }

    // ─────────────────────────── Serbest puan ───────────────────────────

    /// <summary>
    /// Kutudaki puanı okur. <paramref name="maxValue"/>: okunabilecek en yüksek değer (sorunun puanı ya da
    /// sınavın toplamı); bu sınırı aşan okumalar elenir.
    /// </summary>
    /// <param name="cell">Puanın yazıldığı alan (mm).</param>
    /// <param name="bordersAreLines">
    /// Alanın kenarları basılı çizgi mi (tablo hücresi)? false: çerçevesiz alan (ör. Toplam Not kutusunun iç kısmı);
    /// o durumda yalnızca alanı boydan boya kesen çizgiler silinir.
    /// </param>
    public ScoreReading ReadScore(Mat canonicalGray, MmRect cell, decimal maxValue, int position, bool bordersAreLines = true)
    {
        var reading = new ScoreReading { Position = position };

        using var ink = InkImage.Extract(canonicalGray, cell, bordersAreLines);
        if (ink.Components.Count == 0)
        {
            reading.IsEmpty = true;
            reading.Confidence = 1;
            return reading;
        }

        var glyphs = GroupGlyphs(ink.Components);
        var options = glyphs.Select(g => GlyphOptions(ink, g, glyphs)).ToList();
        var candidates = Enumerate(options, maxCandidates: 400);
        if (candidates.Count == 0)
        {
            // Yalnızca kalem lekeleri vardı.
            reading.IsEmpty = true;
            reading.Confidence = 1;
            return reading;
        }

        var valid = candidates
            .Select(c => (c.Text, c.Probability, Value: ParseScore(c.Text)))
            .Where(c => c.Value.HasValue && c.Value.Value >= 0 && c.Value.Value <= maxValue)
            .OrderByDescending(c => c.Probability)
            .ToList();

        if (valid.Count == 0)
        {
            reading.Unreadable = true;
            var ranked = candidates.OrderByDescending(c => c.Probability).Select(c => c.Text).Distinct().ToList();
            reading.Text = ranked.FirstOrDefault() ?? string.Empty;
            reading.Alternatives = ranked.Take(3).ToList();
            return reading;
        }

        var best = valid[0];
        reading.Value = best.Value;
        reading.Text = FormatScore(best.Value!.Value);
        reading.Confidence = Math.Clamp(best.Probability, 0, 1);
        reading.Alternatives = valid.Select(v => FormatScore(v.Value!.Value)).Distinct().Take(3).ToList();
        return reading;
    }

    /// <summary>"25", "7,5", "0" ve tek başına "-" (sıfır) geçerlidir; "05", ",5", "1,234" geçersizdir.</summary>
    public static decimal? ParseScore(string text)
    {
        if (text == "-")
            return 0;
        if (text.Length == 0 || text.Contains('-') || text[0] == ',' || text[^1] == ',' || text.Count(c => c == ',') > 1)
            return null;
        var parts = text.Split(',');
        if (parts[0].Length > 1 && parts[0][0] == '0')
            return null; // baştaki sıfır: büyük olasılıkla yanlış ayrıştırma
        if (parts.Length == 2 && parts[1].Length > 2)
            return null;
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, Turkish, out var value) ? value : null;
    }

    public static string FormatScore(decimal value) => value.ToString("0.##", Turkish);

    private static readonly CultureInfo Turkish = new("tr-TR");

    // ─────────────────────────── Mürekkep çıkarma ───────────────────────────

    private sealed record Component(Rect Box, int Area, int Label);

    /// <summary>
    /// Kutudaki mürekkebin "koyuluk" görüntüsü (mürekkep parlak, zemin 0) ve anlamlı bağlı bileşenleri.
    /// Görüntü kutunun biraz dışını da kapsar (kutu çizgisini aşan rakam kesilmesin); ızgara çizgileri silinir,
    /// yalnızca ağırlık merkezi kutunun içinde kalan bileşenler alınır (komşu kutunun yazısı karışmaz).
    /// </summary>
    private sealed class InkImage : IDisposable
    {
        /// <summary>Kutunun çevresinde incelenen pay (mm).</summary>
        private const double MarginMm = 1.2;

        public Mat Darkness { get; }
        public Mat Labels { get; }
        public List<Component> Components { get; }

        private InkImage(Mat darkness, Mat labels, List<Component> components)
        {
            Darkness = darkness;
            Labels = labels;
            Components = components;
        }

        public static InkImage Extract(Mat canonical, MmRect cell, bool bordersAreLines)
        {
            var regionPx = ToPx(new MmRect(cell.X - MarginMm, cell.Y - MarginMm, cell.Width + 2 * MarginMm, cell.Height + 2 * MarginMm))
                           & new Rect(0, 0, canonical.Width, canonical.Height);
            var cellPx = ToPx(cell);
            cellPx = new Rect(cellPx.X - regionPx.X, cellPx.Y - regionPx.Y, cellPx.Width, cellPx.Height);

            using var roi = new Mat(canonical, regionPx).Clone();
            var darkness = new Mat(roi.Size(), MatType.CV_8UC1, Scalar.Black);
            var labels = new Mat(roi.Size(), MatType.CV_32SC1, Scalar.Black);
            var components = new List<Component>();

            // Kontrast kutunun içinden (çizgilerden uzak) ölçülür: eşik basılı çizgiye değil yazıya göre ayarlanır.
            // Zemin: parlak piksellerin seviyesi (yüzdelik 80); mürekkep: en koyu %1'lik kısım.
            var inset = (int)Math.Round(0.6 * Px);
            var inner = new Rect(cellPx.X + inset, cellPx.Y + inset, cellPx.Width - 2 * inset, cellPx.Height - 2 * inset)
                        & new Rect(0, 0, roi.Width, roi.Height);
            if (inner.Width < 4 || inner.Height < 4)
                return new InkImage(darkness, labels, components);

            double background, darkest;
            using (var innerRoi = new Mat(roi, inner))
            {
                background = Percentile(innerRoi, 0.80);
                darkest = Percentile(innerRoi, 0.01);
            }
            if (background - darkest < MinInkContrast)
                return new InkImage(darkness, labels, components);

            using (var bg = new Mat(roi.Size(), MatType.CV_8UC1, new Scalar(background)))
                Cv2.Subtract(bg, roi, darkness);

            using var binary = new Mat();
            var threshold = Math.Max(30, (background - darkest) * 0.38);
            Cv2.Threshold(darkness, binary, threshold, 255, ThresholdTypes.Binary);
            RemoveGridLines(binary, cellPx, bordersAreLines);

            using var stats = new Mat();
            using var centroids = new Mat();
            var count = Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids, PixelConnectivity.Connectivity8);

            var speck = SpeckAreaMm2 * Px * Px;
            for (var i = 1; i < count; i++)
            {
                var area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
                if (area < speck)
                    continue;
                var center = new Point2d(centroids.At<double>(i, 0), centroids.At<double>(i, 1));
                if (center.X < cellPx.X || center.X > cellPx.Right || center.Y < cellPx.Y || center.Y > cellPx.Bottom)
                    continue; // komşu kutunun yazısı ya da kutu dışındaki metin

                var box = new Rect(stats.At<int>(i, 0), stats.At<int>(i, 1), stats.At<int>(i, 2), stats.At<int>(i, 3));
                components.Add(new Component(box, area, i));
            }

            if (components.Sum(c => c.Area) < EmptyInkAreaMm2 * Px * Px)
                components.Clear();

            var ink = new InkImage(darkness, labels, components);
            ink.StretchContrast();
            return ink;
        }

        private static Rect ToPx(MmRect r)
            => new((int)Math.Round(r.X * Px), (int)Math.Round(r.Y * Px), (int)Math.Round(r.Width * Px), (int)Math.Round(r.Height * Px));

        /// <summary>Soluk kalem / kırmızı mürekkep: seçilen bileşenlerdeki en koyu mürekkep 255 olacak şekilde ölçekle.</summary>
        private void StretchContrast()
        {
            if (Components.Count == 0)
                return;
            using var all = Mask(Components);
            using var values = new Mat();
            Darkness.CopyTo(values, all);
            var peak = Percentile(values, 0.99, ignoreZero: true);
            if (peak > 0)
                Darkness.ConvertTo(Darkness, MatType.CV_8UC1, 255.0 / peak);
        }

        /// <summary>Bileşenlerin piksel maskesi (kenar yumuşaklığı için 1 px genişletilmiş).</summary>
        public Mat Mask(IEnumerable<Component> parts)
        {
            var mask = new Mat(Darkness.Size(), MatType.CV_8UC1, Scalar.Black);
            foreach (var part in parts)
            {
                using var piece = new Mat();
                Cv2.Compare(Labels, new Scalar(part.Label), piece, CmpType.EQ);
                Cv2.BitwiseOr(mask, piece, mask);
            }
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
            Cv2.Dilate(mask, mask, kernel);
            return mask;
        }

        /// <summary>Yalnızca verilen bileşenlerin mürekkebini içeren görüntü (isteğe bağlı olarak bir dikdörtgenle sınırlı).</summary>
        public Mat Render(IEnumerable<Component> parts, Rect? clip = null)
        {
            using var mask = Mask(parts);
            if (clip is Rect c)
            {
                using var window = new Mat(mask.Size(), MatType.CV_8UC1, Scalar.Black);
                window.Rectangle(c, Scalar.White, -1);
                Cv2.BitwiseAnd(mask, window, mask);
            }
            var result = new Mat(Darkness.Size(), MatType.CV_8UC1, Scalar.Black);
            Darkness.CopyTo(result, mask);
            return result;
        }

        public void Dispose()
        {
            Darkness.Dispose();
            Labels.Dispose();
        }
    }

    /// <summary>
    /// İkili maskeden ızgara çizgilerini siler: kutunun kenar çizgisi konumunda duran (bordersAreLines) ya da
    /// kutuyu boydan boya kesen düz parçalar. Rakamın kendi düz çizgileri (7'nin üst çizgisi, 1'in gövdesi)
    /// kutunun ortasında ve kutudan kısa olduğu için korunur.
    /// </summary>
    private static void RemoveGridLines(Mat binary, Rect cellPx, bool bordersAreLines)
    {
        var horizontalLength = Math.Max(8, (int)(cellPx.Width * 0.6));
        var verticalLength = Math.Max(8, (int)(cellPx.Height * 0.6));
        using var hKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(horizontalLength, 1));
        using var vKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(1, verticalLength));
        using var horizontal = new Mat();
        using var vertical = new Mat();
        Cv2.MorphologyEx(binary, horizontal, MorphTypes.Open, hKernel);
        Cv2.MorphologyEx(binary, vertical, MorphTypes.Open, vKernel);

        using var remove = new Mat(binary.Size(), MatType.CV_8UC1, Scalar.Black);
        var tolerance = 0.7 * Px;
        foreach (var (lines, isHorizontal) in new[] { (horizontal, true), (vertical, false) })
        {
            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var count = Cv2.ConnectedComponentsWithStats(lines, labels, stats, centroids);
            for (var i = 1; i < count; i++)
            {
                var x = stats.At<int>(i, 0);
                var y = stats.At<int>(i, 1);
                var w = stats.At<int>(i, 2);
                var h = stats.At<int>(i, 3);
                var length = isHorizontal ? w : h;
                var position = isHorizontal ? y + h / 2.0 : x + w / 2.0;
                var nearBorder = bordersAreLines && (isHorizontal
                    ? Math.Abs(position - cellPx.Y) <= tolerance || Math.Abs(position - cellPx.Bottom) <= tolerance
                    : Math.Abs(position - cellPx.X) <= tolerance || Math.Abs(position - cellPx.Right) <= tolerance);
                var spansCell = length >= (isHorizontal ? cellPx.Width : cellPx.Height) * 0.95;
                if (!nearBorder && !spansCell)
                    continue;

                using var piece = new Mat();
                Cv2.Compare(labels, new Scalar(i), piece, CmpType.EQ);
                Cv2.BitwiseOr(remove, piece, remove);
            }
        }

        using (var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)))
            Cv2.Dilate(remove, remove, k);
        binary.SetTo(Scalar.Black, remove);
    }

    // ─────────────────────────── Rakamlara ayırma ───────────────────────────

    private sealed class Glyph
    {
        public List<Component> Parts { get; } = new();
        public Rect Box => Parts.Select(p => p.Box).Aggregate((a, b) => a | b);
    }

    private static bool Overlaps(Rect a, Rect b)
    {
        var overlap = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        return overlap > 0 && overlap >= Math.Min(a.Width, b.Width) * 0.45;
    }

    /// <summary>Yatayda büyük ölçüde örtüşen parçaları (ör. 5'in şapkası, 4'ün iki çizgisi) tek glife birleştirir.</summary>
    private static List<Glyph> GroupGlyphs(List<Component> components)
    {
        var glyphs = components
            .OrderBy(c => c.Box.X + c.Box.Width / 2.0)
            .Select(c => { var g = new Glyph(); g.Parts.Add(c); return g; })
            .ToList();

        var merged = true;
        while (merged)
        {
            merged = false;
            for (var i = 0; i < glyphs.Count && !merged; i++)
            {
                for (var j = i + 1; j < glyphs.Count && !merged; j++)
                {
                    if (!Overlaps(glyphs[i].Box, glyphs[j].Box))
                        continue;
                    glyphs[i].Parts.AddRange(glyphs[j].Parts);
                    glyphs.RemoveAt(j);
                    merged = true;
                }
            }
        }

        return glyphs.OrderBy(g => g.Box.X).ToList();
    }

    private sealed record Option(string Text, double Probability);

    /// <summary>
    /// Bir glifin olası okunuşları: rakam(lar), ondalık ayırıcı, tire ya da yok sayılacak leke.
    /// Boş metinli seçenek ("") glifin okumaya katılmadığı anlamına gelir.
    /// </summary>
    private List<Option> GlyphOptions(InkImage ink, Glyph glyph, List<Glyph> all)
    {
        var box = glyph.Box;
        var index = all.IndexOf(glyph);
        var lineTop = all.Min(g => g.Box.Y);
        var lineBottom = all.Max(g => g.Box.Bottom);
        var lineHeight = Math.Max(1, lineBottom - lineTop);
        var tallest = all.Max(g => g.Box.Height);

        // Tek başına kısa yatay çizgi: "-" (sıfır puan).
        if (all.Count == 1 && box.Width >= box.Height * 2.2 && box.Height < Px * 2.5)
            return new List<Option> { new("-", 1.0) };

        if (all.Count > 1)
        {
            var small = box.Height < tallest * 0.36 && box.Width < tallest * 0.5;
            if (small)
            {
                // Ancak iki rakamın arasında ve satırın alt kısmındaysa virgül/nokta olabilir; değilse kalem lekesi.
                var between = index > 0 && index < all.Count - 1;
                var low = box.Y > lineTop + lineHeight * 0.45;
                if (!between || !low)
                    return new List<Option> { new(string.Empty, 1.0) };
                // Belirgin işaret (≥ 0,5 mm) büyük olasılıkla virgüldür; daha küçüğü leke de olabilir.
                var distinct = Math.Max(box.Width, box.Height) >= Px * 0.5;
                return distinct
                    ? new List<Option> { new(",", 0.97), new(string.Empty, 0.03) }
                    : new List<Option> { new(",", 0.6), new(string.Empty, 0.4) };
            }

            // Satırın ortasında kısa yatay çizgi: tire (geçersiz okuma olarak elenir).
            if (box.Height < tallest * 0.3 && box.Width > box.Height * 1.8)
                return new List<Option> { new("-", 0.9) };
        }

        return Interpret(ink, glyph.Parts, box, depth: 0);
    }

    /// <summary>
    /// Bir bölgeyi tek rakam ya da (geniş ise) bitişik birden çok rakam olarak yorumlar. El yazısında tek bir
    /// rakam nadiren yüksekliğinden belirgin biçimde geniştir; bu yüzden tek/bölünmüş yorumların ön olasılığı
    /// mürekkebin en-boy oranından gelir (ör. "10", "25" bitişik yazıldığında bölünmüş yorum kazanır).
    /// </summary>
    private List<Option> Interpret(InkImage ink, IReadOnlyList<Component> parts, Rect region, int depth)
    {
        var bounds = InkBounds(ink, parts, region);
        if (bounds.Width == 0 || bounds.Height == 0)
            return new List<Option> { new(string.Empty, 1.0) };

        var aspect = bounds.Width / (double)bounds.Height;
        var singlePrior = aspect < 0.95 ? 1.0 : aspect < 1.2 ? 0.75 : aspect < 1.45 ? 0.35 : aspect < 1.9 ? 0.08 : 0.02;

        var options = Classify(ink, parts, bounds)
            .Select(s => new Option(s.Digit.ToString(CultureInfo.InvariantCulture), s.Probability * singlePrior))
            .ToList();

        if (singlePrior < 1 && depth < 2 && bounds.Width > Px * 2.4)
        {
            var split = FindSplitColumn(ink.Darkness, bounds);
            var left = new Rect(bounds.X, bounds.Y, split - bounds.X, bounds.Height);
            var right = new Rect(split, bounds.Y, bounds.Right - split, bounds.Height);
            if (left.Width > 1 && right.Width > 1)
            {
                var leftOptions = Interpret(ink, parts, left, depth + 1).Take(3).ToList();
                var rightOptions = Interpret(ink, parts, right, depth + 1).Take(3).ToList();
                foreach (var a in leftOptions)
                    foreach (var b in rightOptions)
                        if (a.Text.Length > 0 && b.Text.Length > 0)
                            options.Add(new Option(a.Text + b.Text, a.Probability * b.Probability * (1 - singlePrior)));
            }
        }

        return options.OrderByDescending(o => o.Probability).Take(6).ToList();
    }

    /// <summary>Bileşenlerin, verilen bölge içinde kalan mürekkebinin sınırlayıcı dikdörtgeni.</summary>
    private static Rect InkBounds(InkImage ink, IReadOnlyList<Component> parts, Rect region)
    {
        using var rendered = ink.Render(parts, region);
        using var mask = new Mat();
        Cv2.Threshold(rendered, mask, 40, 255, ThresholdTypes.Binary);
        return Cv2.BoundingRect(mask);
    }

    /// <summary>Glifin orta bölgesinde en az mürekkep bulunan sütun (bitişik rakamların ayrılacağı yer).</summary>
    private static int FindSplitColumn(Mat darkness, Rect box)
    {
        var from = box.X + (int)(box.Width * 0.3);
        var to = box.X + (int)(box.Width * 0.7);
        var best = box.X + box.Width / 2;
        var bestSum = double.MaxValue;
        for (var x = from; x <= to; x++)
        {
            using var column = new Mat(darkness, new Rect(x, box.Y, 1, box.Height));
            var sum = Cv2.Sum(column).Val0;
            if (sum < bestSum)
            {
                bestSum = sum;
                best = x;
            }
        }
        return best;
    }

    private List<(int Digit, double Probability)> Classify(InkImage ink, IEnumerable<Component> parts, Rect? clip)
    {
        using var region = ink.Render(parts, clip);
        var input = DigitNormalizer.Normalize(region);
        if (input == null)
            return new List<(int, double)> { (0, 0.0) };

        return _classifier.Predict(input)
            .Select((p, digit) => (digit, (double)p))
            .OrderByDescending(x => x.Item2)
            .Take(3)
            .ToList();
    }

    /// <summary>Glif seçeneklerinin kartezyen çarpımı (olasılık sırasıyla, en fazla <paramref name="maxCandidates"/>).</summary>
    private static List<(string Text, double Probability)> Enumerate(List<List<Option>> options, int maxCandidates)
    {
        var beam = new List<(string Text, double Probability)> { (string.Empty, 1.0) };
        foreach (var glyphOptions in options)
        {
            beam = beam
                .SelectMany(b => glyphOptions.Select(o => (b.Text + o.Text, b.Probability * o.Probability)))
                .OrderByDescending(x => x.Item2)
                .Take(maxCandidates)
                .ToList();
        }
        return beam.Where(b => b.Text.Length > 0).ToList();
    }

    // ─────────────────────────── Yardımcılar ───────────────────────────

    public static Mat Crop(Mat canonical, MmRect rect)
    {
        var px = new Rect(
            (int)Math.Round(rect.X * Px),
            (int)Math.Round(rect.Y * Px),
            (int)Math.Round(rect.Width * Px),
            (int)Math.Round(rect.Height * Px));
        px &= new Rect(0, 0, canonical.Width, canonical.Height);
        return new Mat(canonical, px).Clone();
    }

    private static double Percentile(Mat gray, double fraction, bool ignoreZero = false)
    {
        using var hist = new Mat();
        Cv2.CalcHist(new[] { gray }, new[] { 0 }, null, hist, 1, new[] { 256 }, new[] { new Rangef(0, 256) });
        var counts = new double[256];
        for (var i = 0; i < 256; i++)
            counts[i] = hist.At<float>(i);
        if (ignoreZero)
            counts[0] = 0;
        var total = counts.Sum();
        if (total <= 0)
            return 0;
        var target = total * fraction;
        double cumulative = 0;
        for (var i = 0; i < 256; i++)
        {
            cumulative += counts[i];
            if (cumulative >= target)
                return i;
        }
        return 255;
    }
}
