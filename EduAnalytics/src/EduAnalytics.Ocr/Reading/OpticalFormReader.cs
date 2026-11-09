using System.Globalization;
using EduAnalytics.Ocr.Models;
using EduAnalytics.Ocr.Recognition;
using OpenCvSharp;

namespace EduAnalytics.Ocr.Reading;

/// <summary>
/// Sınav kâğıdı okuma hattının orkestratörü:
///   sayfa → hizalama (PageNormalizer) → yön ve sayfa türü (sayfa kodu)
///        → kitapçık + test cevapları (OMR, BubbleReader)
///        → öğrenci numarası kutuları (el yazısı rakam modeli)
///        → "Alınan Not" satırı: ızgara tespiti (GridDetector) + kutu kırpma + el yazısı puan okuma
///        → Toplam Not kutusu
///        → (isteğe bağlı) ad-soyad için PaddleOCR.
/// PaddleOCR modelleri süreç boyunca yüklü kaldığı için singleton olarak kaydedilmelidir.
/// </summary>
public sealed class OpticalFormReader : IOpticalFormReader, IDisposable
{
    private readonly PaddleTextReader? _textReader;
    private readonly HandwrittenNumberReader _numbers;

    /// <summary>PaddleOCR yüklenemezse (ör. eksik yerel kütüphane) bir kez işaretlenir; okuma onsuz sürer.</summary>
    private bool _ocrUnavailable;

    /// <param name="enableTextOcr">
    /// false: ad-soyad OCR'ı hiç çalışmaz. Testlerde ve PaddleOCR'ın kurulu olmadığı ortamlarda kullanılır.
    /// Numara ve puanlar her durumda gömülü rakam modeliyle okunur.
    /// </param>
    public OpticalFormReader(bool enableTextOcr = true)
    {
        _textReader = enableTextOcr ? new PaddleTextReader() : null;
        _numbers = new HandwrittenNumberReader();
    }

    public bool IsSupportedFile(string path) => PdfPageRasterizer.IsSupported(path);

    public IReadOnlyList<(string SourceLabel, OpticalSheetResult Result)> ReadFile(string path, ExamPaperSpec spec)
    {
        var results = new List<(string, OpticalSheetResult)>();
        var fileName = Path.GetFileName(path);
        var page = 0;

        foreach (var mat in PdfPageRasterizer.LoadPages(path))
        {
            page++;
            using (mat)
            {
                var label = page == 1 && Path.GetExtension(path).ToLowerInvariant() != ".pdf"
                    ? fileName
                    : $"{fileName} (sayfa {page})";
                results.Add((label, ReadSheet(mat, spec)));
            }
        }

        return results;
    }

    public OpticalSheetResult ReadSheet(Mat pageImage, ExamPaperSpec spec)
    {
        var layout = ExamPaperLayout.For(spec);
        var result = new OpticalSheetResult();

        using var normalized = PageNormalizer.Normalize(pageImage);
        if (normalized == null)
        {
            result.Warnings.Add("Köşe hizalama işaretleri bulunamadı — sayfa bu uygulamanın sınav kâğıdı olmayabilir veya tarama kalitesi düşük.");
            return result;
        }
        result.MarkersFound = true;

        // Köşe işaretleri 180° dönüşe göre simetriktir; yön ve sayfa türü sol kenardaki sayfa kodundan okunur.
        using var flipped = new Mat();
        Cv2.Rotate(normalized, flipped, RotateFlags.Rotate180);
        var upright = BubbleReader.ReadPageCode(normalized);
        var reversed = BubbleReader.ReadPageCode(flipped);

        Mat canonical;
        bool firstPage;
        if (upright.Oriented)
        {
            canonical = normalized;
            firstPage = upright.FirstPage;
        }
        else if (reversed.Oriented)
        {
            canonical = flipped;
            firstPage = reversed.FirstPage;
            result.Warnings.Add("Sayfa ters (180°) taranmış — otomatik düzeltildi.");
        }
        else
        {
            result.Warnings.Add("Sayfa kodu bulunamadı — bu sayfa bu sürümün sınav kâğıdı değil (eski sürüm optik form olabilir). " +
                                "Sınav kâğıdını \"Sınav Kâğıdı Üret\" ile yeniden alın.");
            return result;
        }

        if (!firstPage)
        {
            result.PageKind = SheetPageKind.Continuation;
            return result;
        }
        result.PageKind = SheetPageKind.FirstPage;

        using var inkMask = BubbleReader.PrepareInkMask(canonical);
        ReadBooklet(inkMask, layout, spec, result);
        ReadAnswers(inkMask, layout, spec, result);
        ReadStudentNumber(canonical, result);
        ReadScoreTable(canonical, layout, spec, result);
        ReadTotal(canonical, spec, result);
        ReadStudentName(canonical, result);

        return result;
    }

    // ─────────────────────────── OMR ───────────────────────────

    private static void ReadBooklet(Mat inkMask, ExamPaperLayout layout, ExamPaperSpec spec, OpticalSheetResult result)
    {
        if (!spec.HasBookletSection)
        {
            result.BookletIndex = 0;
            result.BookletConfidence = 1;
            return;
        }

        var (index, confidence, ambiguous) = BubbleReader.ReadBookletRow(inkMask, layout, spec.BookletCodes.Count);
        result.BookletIndex = index;
        result.BookletConfidence = confidence;

        if (index == null)
            result.Warnings.Add(ambiguous
                ? "Kitapçık alanı: birden fazla veya belirsiz işaret — kitapçığı seçin."
                : "Kitapçık okunamadı — kitapçığı seçin.");
        else if (ambiguous)
            result.Warnings.Add($"Kitapçık belirsiz okundu ({spec.BookletCodes[index.Value]}) — kontrol edin.");
    }

    private static void ReadAnswers(Mat inkMask, ExamPaperLayout layout, ExamPaperSpec spec, OpticalSheetResult result)
    {
        var mcNumbers = spec.QuestionsFor(result.BookletIndex).Where(q => q.IsMultipleChoice).Select(q => q.Number).ToList();
        for (var i = 0; i < layout.MultipleChoiceCount; i++)
        {
            var answer = BubbleReader.ReadAnswerRow(inkMask, layout, i);
            result.Answers.Add(answer);

            var number = i < mcNumbers.Count ? mcNumbers[i] : i + 1;
            if (answer.MultipleMarks)
                result.Warnings.Add($"Soru {number}: birden fazla şık işaretlenmiş (çift işaret).");
            else if (answer.WeakMark)
                result.Warnings.Add($"Soru {number}: silik/kararsız işaret ({answer.Option}) — kontrol edin.");
        }
    }

    // ─────────────────────────── El yazısı: öğrenci numarası ───────────────────────────

    private void ReadStudentNumber(Mat canonical, OpticalSheetResult result)
    {
        var cells = OpticalFormTemplate.StudentNumberCells;
        var expectedXs = Enumerable.Range(0, cells + 1)
            .Select(i => ExamPaperLayout.NumberBoxesX + i * ExamPaperLayout.NumberBoxWidth).ToList();
        var expectedYs = new[] { ExamPaperLayout.NumberBoxesY, ExamPaperLayout.NumberBoxesY + ExamPaperLayout.NumberBoxHeight };
        var (xs, ys, _) = GridDetector.Refine(canonical, expectedXs, expectedYs, minLineLengthMm: ExamPaperLayout.NumberBoxHeight * 0.6);

        var digits = new float[]?[cells];
        for (var i = 0; i < cells; i++)
            digits[i] = _numbers.ReadDigitBox(canonical, Cell(xs[i], ys[0], xs[i + 1], ys[1]));

        result.StudentNumberDigits = digits;
        result.StudentNumberImage = EncodeCrop(canonical, new MmRect(xs[0] - 1, ys[0] - 1, xs[^1] - xs[0] + 2, ys[1] - ys[0] + 2));

        var chars = digits.Select(p => p == null ? '?' : (char)('0' + ArgMax(p))).ToArray();
        var first = Array.FindIndex(digits, p => p != null);
        var last = Array.FindLastIndex(digits, p => p != null);
        if (first < 0)
        {
            result.RawStudentNumber = string.Empty;
            result.Warnings.Add("Öğrenci numarası kutuları boş — öğrenciyi listeden elle seçin.");
            return;
        }

        result.RawStudentNumber = new string(chars[first..(last + 1)]);
        var known = digits.Where(p => p != null).Select(p => (double)p!.Max()).ToList();
        result.StudentNumberConfidence = known.Min();

        var unsure = digits
            .Select((p, i) => (p, i))
            .Where(x => x.p != null && x.p.Max() < ScoreReading.LowConfidence)
            .Select(x => (x.i + 1).ToString(CultureInfo.InvariantCulture))
            .ToList();
        if (unsure.Count > 0)
            result.Warnings.Add($"Öğrenci numarasının {string.Join(", ", unsure)}. hanesi belirsiz okundu — kontrol edin.");
        if (result.RawStudentNumber.Contains('?'))
            result.Warnings.Add($"Öğrenci numarasında boş kutu var: \"{result.RawStudentNumber}\".");
        else if (result.RawStudentNumber.Length != cells)
            result.Warnings.Add($"Öğrenci numarası {result.RawStudentNumber.Length} haneli okundu (beklenen {cells}).");
    }

    // ─────────────────────────── El yazısı: puan tablosu ve toplam ───────────────────────────

    /// <summary>
    /// "Alınan Not" satırını bulur ve sınavdaki her soru için kutuyu sırayla kırpıp okur. Izgara çizgileri
    /// görüntüde aranır (<see cref="GridDetector"/>); bulunan çizgiler şablon konumlarının yerini alır.
    /// </summary>
    private void ReadScoreTable(Mat canonical, ExamPaperLayout layout, ExamPaperSpec spec, OpticalSheetResult result)
    {
        var columns = layout.ScoreColumns;
        var firstCellX = OpticalFormTemplate.ContentLeftMm + ExamPaperLayout.ScoreTitleColumnWidth;
        var expectedXs = Enumerable.Range(0, columns + 1).Select(c => firstCellX + c * layout.ScoreCellWidth).ToList();

        var tableTop = double.MaxValue;
        var tableBottom = 0.0;
        var tableRight = 0.0;

        for (var pair = 0; pair < layout.ScorePairs; pair++)
        {
            var top = ExamPaperLayout.ScoreTableTop + pair * ExamPaperLayout.ScorePairHeight;
            var expectedYs = new[] { top, top + ExamPaperLayout.ScoreNumberRowHeight, top + ExamPaperLayout.ScorePairHeight };
            var (xs, ys, found) = GridDetector.Refine(canonical, expectedXs, expectedYs,
                minLineLengthMm: ExamPaperLayout.ScoreRowHeight * 0.6);
            if (found < 0.5)
                result.Warnings.Add("Puan tablosunun çizgileri net bulunamadı; kutular şablon konumundan kırpıldı — tabloyu kontrol edin.");

            tableTop = Math.Min(tableTop, ys[0]);
            tableBottom = Math.Max(tableBottom, ys[2]);
            tableRight = Math.Max(tableRight, xs[^1]);

            for (var column = 0; column < columns; column++)
            {
                var position = pair * columns + column;
                if (position >= layout.QuestionCount)
                    break;

                var cell = Cell(xs[column], ys[1], xs[column + 1], ys[2]);
                var max = spec.MaxPointsAt(position, result.BookletIndex);
                var reading = _numbers.ReadScore(canonical, cell, max, position);
                result.Scores.Add(reading);
            }
        }

        // İnceleme ekranı için tablonun tamamı (başlık sütunu dahil) kırpılır.
        var frameLeft = OpticalFormTemplate.ContentLeftMm;
        result.ScoreTableImage = EncodeCrop(canonical,
            new MmRect(frameLeft - 1, tableTop - 1, Math.Max(tableRight, OpticalFormTemplate.ContentRightMm) - frameLeft + 2, tableBottom - tableTop + 2));
    }

    private void ReadTotal(Mat canonical, ExamPaperSpec spec, OpticalSheetResult result)
    {
        // Kutunun sol kenarı, bölücü çizgi ve alt kenarı bulunarak kaymaya göre alan düzeltilir.
        var box = ExamPaperLayout.TotalBox;
        var (xs, ys, _) = GridDetector.Refine(canonical,
            new[] { box.X, ExamPaperLayout.TotalDividerX }, new[] { box.Bottom }, minLineLengthMm: 15);
        var dx = ((xs[0] - box.X) + (xs[1] - ExamPaperLayout.TotalDividerX)) / 2;
        var dy = ys[0] - box.Bottom;
        var area = ExamPaperLayout.TotalScoreArea;
        var shifted = new MmRect(area.X + dx, area.Y + dy, area.Width, area.Height);

        var total = _numbers.ReadScore(canonical, shifted, spec.TotalPoints, position: -1, bordersAreLines: false);
        result.TotalScore = total;
        result.TotalScoreImage = EncodeCrop(canonical, new MmRect(box.X + dx - 0.5, box.Y + dy - 0.5, box.Width + 1, box.Height + 1));
    }

    // ─────────────────────────── Ad-soyad (isteğe bağlı) ───────────────────────────

    private void ReadStudentName(Mat canonical, OpticalSheetResult result)
    {
        if (_textReader == null || _ocrUnavailable)
            return;

        try
        {
            using var nameRegion = HandwrittenNumberReader.Crop(canonical, ExamPaperLayout.NameArea);
            var (text, confidence) = _textReader.ReadFreeText(nameRegion);
            result.RawStudentName = text;
            result.StudentNameConfidence = confidence;
        }
        catch (Exception ex)
        {
            _ocrUnavailable = true;
            result.Warnings.Add($"Metin tanıma (PaddleOCR) kullanılamadı; ad-soyad okunmadı. ({ex.GetBaseException().Message})");
        }
    }

    // ─────────────────────────── Yardımcılar ───────────────────────────

    /// <summary>Bulunan çizgi merkezleri arasındaki hücre.</summary>
    private static MmRect Cell(double left, double top, double right, double bottom)
        => new(left, top, right - left, bottom - top);

    private static int ArgMax(float[] p)
    {
        var best = 0;
        for (var i = 1; i < p.Length; i++)
            if (p[i] > p[best]) best = i;
        return best;
    }

    private static byte[] EncodeCrop(Mat canonical, MmRect rect)
    {
        using var crop = HandwrittenNumberReader.Crop(canonical, rect);
        Cv2.ImEncode(".png", crop, out var png);
        return png;
    }

    public void Dispose() => _textReader?.Dispose();
}
