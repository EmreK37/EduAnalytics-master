using EduAnalytics.Core.Enums;
using EduAnalytics.Ocr;
using EduAnalytics.Ocr.Models;
using EduAnalytics.Ocr.Reading;
using OpenCvSharp;

namespace EduAnalytics.Ocr.Tests;

/// <summary>
/// Uçtan uca sınav kâğıdı testleri: şablondan kâğıt üretilir, öğrenci ve öğretmen tarafından el yazısıyla
/// doldurulur (<see cref="SyntheticHandwriting"/>; rakam modeli bu yazıyla EĞİTİLMEMİŞTİR), gerçek taramaya
/// benzetilir (eğiklik, kayma, gürültü, bulanıklık, ters besleme) ve okuyucudan geçirilir.
/// Ad-soyad OCR'ı (PaddleOCR) kapalıdır; numara ve puanlar gömülü rakam modeliyle okunur.
/// </summary>
public class ExamPaperRoundTripTests : IDisposable
{
    private readonly OpticalFormReader _reader = new(enableTextOcr: false);

    public void Dispose() => _reader.Dispose();

    private static PaperQuestion Mc(int number, decimal points = 10)
        => new(number, true, points, $"Test sorusu {number}", new[] { "Bir", "İki", "Üç", "Dört", "Beş" });

    private static PaperQuestion Classic(int number, decimal points)
        => new(number, false, points, $"Klasik soru {number}: açıklayınız.");

    /// <summary>"Vize" benzeri sınav: 4 test (10'ar puan) + 2 klasik (30'ar puan).</summary>
    private static ExamPaperSpec VizeSpec() => new()
    {
        InstitutionName = "Doğuş Üniversitesi",
        CourseCode = "YM 344",
        CourseName = "Yazılım Kalite Güvencesi ve Testi",
        ExamTitle = "1. Vize Sınavı",
        ExamTypeTr = "Vize",
        ExamTypeEn = "Midterm",
        AcademicYear = "2025 – 2026",
        SemesterTr = "Güz",
        SemesterEn = "Fall",
        Lecturer = "Öğr. Gör. Deneme",
        ExamDate = new DateTime(2026, 4, 15),
        DurationMinutes = 75,
        Booklets = new[]
        {
            (IReadOnlyList<PaperQuestion>)new[] { Mc(1), Mc(2), Mc(3), Mc(4), Classic(5, 30), Classic(6, 30) }
        }
    };

    private OpticalSheetResult Read(ExamPaperSpec spec, ExamPaperFill fill, ScanSimulation simulation, int booklet = 0)
    {
        var png = ExamPaperSampleRenderer.RenderScanPng(spec, booklet, fill, simulation);
        using var image = Cv2.ImDecode(png, ImreadModes.Grayscale);
        return _reader.ReadSheet(image, spec);
    }

    private static IReadOnlyList<int> Mark(params int[] options) => options;

    private static string AnswerString(OpticalSheetResult result) => string.Concat(result.Answers.Select(a =>
        a.MultipleMarks ? '?' : a.Option == OptionLetter.Empty ? '-' : a.Option.ToString()[0]));

    private static string Digits(OpticalSheetResult result) => result.RawStudentNumber;

    [Fact]
    public void Number_answers_scores_and_total_are_read_from_a_skewed_noisy_scan()
    {
        var spec = VizeSpec();
        var fill = new ExamPaperFill
        {
            Seed = 5,
            StudentName = "Elif Arslan",
            StudentNumber = "202403011003",
            Department = "Yazılım Mühendisliği",
            Answers = new[] { Mark(2), Mark(1), Mark(), Mark(3) },
            Scores = new[] { "10", "10", "0", "0", "25", "17,5" },
            Total = "62,5"
        };

        var result = Read(spec, fill, ScanSimulation.Typical(seed: 7));

        Assert.True(result.MarkersFound);
        Assert.Equal(SheetPageKind.FirstPage, result.PageKind);
        Assert.Equal("202403011003", Digits(result));
        Assert.Equal("CB-D", AnswerString(result));
        Assert.Equal(new decimal?[] { 10, 10, 0, 0, 25, 17.5m }, result.Scores.Select(s => s.Value));
        Assert.Equal(62.5m, result.TotalScore!.Value);
        Assert.All(result.Scores, s => Assert.False(s.Unreadable));
        Assert.NotNull(result.ScoreTableImage);
        Assert.NotNull(result.StudentNumberImage);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Upside_down_scan_is_detected_and_corrected()
    {
        var spec = VizeSpec();
        var fill = new ExamPaperFill
        {
            Seed = 9,
            StudentNumber = "202503011060",
            Answers = new[] { Mark(0), Mark(4), Mark(2), Mark(1) },
            Scores = new[] { "10", "0", "10", "0", "30", "12" },
            Total = "62"
        };

        var result = Read(spec, fill, ScanSimulation.Typical(seed: 3) with { UpsideDown = true });

        Assert.Equal(SheetPageKind.FirstPage, result.PageKind);
        Assert.Equal("202503011060", Digits(result));
        Assert.Equal("AECB", AnswerString(result));
        Assert.Equal(new decimal?[] { 10, 0, 10, 0, 30, 12 }, result.Scores.Select(s => s.Value));
        Assert.Contains(result.Warnings, w => w.Contains("ters"));
    }

    [Fact]
    public void Continuation_pages_are_recognized_and_carry_no_reading()
    {
        var spec = VizeSpec();
        var fill = new ExamPaperFill { Seed = 2, StudentNumber = "202403011059" };
        var pages = ExamPaperSampleRenderer.RenderScanPages(spec, 0, fill, ScanSimulation.Typical(seed: 4));

        Assert.True(pages.Count >= 2);
        using var second = Cv2.ImDecode(pages[1], ImreadModes.Grayscale);
        var result = _reader.ReadSheet(second, spec);

        Assert.True(result.MarkersFound);
        Assert.Equal(SheetPageKind.Continuation, result.PageKind);
        Assert.Empty(result.Scores);
    }

    [Fact]
    public void Booklet_b_paper_is_identified_and_read_in_its_own_question_order()
    {
        // B kitapçığında sıra karışık: test ve klasik sorular iç içe.
        var a = VizeSpec().Booklets[0];
        var b = new[] { 4, 0, 5, 2, 1, 3 }.Select((index, k) => a[index] with { Number = k + 1 }).ToList();
        var spec = new ExamPaperSpec
        {
            CourseCode = "YM 344",
            CourseName = "Yazılım Kalite Güvencesi ve Testi",
            BookletCodes = new[] { "A", "B" },
            Booklets = new[] { a, (IReadOnlyList<PaperQuestion>)b }
        };
        var fill = new ExamPaperFill
        {
            Seed = 21,
            StudentNumber = "202303011014",
            Answers = new[] { Mark(3), Mark(0), Mark(2), Mark(4) },        // B'deki test soruları: 2, 4, 5, 6
            Scores = new[] { "27,5", "10", "8", "0", "10", "0" },
            Total = "55,5"
        };

        var result = Read(spec, fill, ScanSimulation.Typical(seed: 5), booklet: 1);

        Assert.Equal(1, result.BookletIndex);
        Assert.Equal("DACE", AnswerString(result));
        Assert.Equal(new decimal?[] { 27.5m, 10, 8, 0, 10, 0 }, result.Scores.Select(s => s.Value));
        Assert.Equal(55.5m, result.TotalScore!.Value);
    }

    [Fact]
    public void More_than_twenty_questions_use_a_second_score_row()
    {
        var questions = Enumerable.Range(1, 26).Select(n => n <= 24 ? Mc(n, 3) : Classic(n, 14)).ToList();
        var spec = new ExamPaperSpec { CourseCode = "MAT 101", Booklets = new[] { (IReadOnlyList<PaperQuestion>)questions } };
        var layout = ExamPaperLayout.For(spec);
        Assert.Equal(2, layout.ScorePairs);

        var rng = new Random(13);
        var scores = questions.Select(q => q.IsMultipleChoice
            ? (rng.Next(2) == 0 ? "0" : "3")
            : rng.Next(0, 15).ToString()).ToList();
        var fill = new ExamPaperFill
        {
            Seed = 44,
            StudentNumber = "202403011119",
            Answers = Enumerable.Range(0, 24).Select(i => Mark(i % 5)).ToList(),
            Scores = scores
        };

        var result = Read(spec, fill, ScanSimulation.Typical(seed: 9));

        Assert.Equal(26, result.Scores.Count);
        Assert.Equal(scores.Select(s => (decimal?)decimal.Parse(s)), result.Scores.Select(s => s.Value));
        Assert.True(result.TotalScore!.IsEmpty);
    }

    [Fact]
    public void Empty_boxes_double_and_weak_marks_are_reported()
    {
        var spec = VizeSpec();
        var fill = new ExamPaperFill
        {
            Seed = 31,
            Answers = new[] { Mark(1, 3), Mark(2), Mark(), Mark(4) },
            WeakMarkRows = new[] { 1 },
            Scores = new string?[] { "0", "10", null, "0", null, "20" }
        };

        var result = Read(spec, fill, ScanSimulation.Typical(seed: 1));

        Assert.Equal(string.Empty, Digits(result));
        Assert.Contains(result.Warnings, w => w.Contains("numarası kutuları boş"));
        Assert.True(result.Answers[0].MultipleMarks);
        Assert.True(result.Answers[1].WeakMark);
        Assert.Contains(result.Warnings, w => w.StartsWith("Soru 1:") && w.Contains("birden fazla"));
        Assert.True(result.Scores[2].IsEmpty);
        Assert.True(result.Scores[4].IsEmpty);
        Assert.Equal(20m, result.Scores[5].Value);
        Assert.True(result.TotalScore!.IsEmpty);
    }

    [Fact]
    public void Handwritten_scores_are_read_reliably_across_many_writers()
    {
        // Farklı yazarlar (eğim, kalınlık, 7/1/4 biçimleri) ve taramalar: okuma doğruluğu istatistiksel olarak sınanır.
        var spec = VizeSpec();
        var rng = new Random(2026);
        int cells = 0, correct = 0, numbers = 0, numbersCorrect = 0;

        for (var sheet = 0; sheet < 8; sheet++)
        {
            var number = "20" + string.Concat(Enumerable.Range(0, 10).Select(_ => rng.Next(10)));
            var scores = new List<string>
            {
                rng.Next(2) == 0 ? "10" : "0", rng.Next(2) == 0 ? "10" : "0",
                rng.Next(2) == 0 ? "10" : "0", rng.Next(2) == 0 ? "10" : "0",
                (rng.Next(0, 61) / 2m).ToString("0.#", new System.Globalization.CultureInfo("tr-TR")),
                rng.Next(0, 31).ToString()
            };
            var fill = new ExamPaperFill { Seed = 1000 + sheet, StudentNumber = number, Scores = scores };

            var result = Read(spec, fill, ScanSimulation.Typical(seed: 50 + sheet));

            numbers++;
            if (result.RawStudentNumber == number)
                numbersCorrect++;
            for (var i = 0; i < scores.Count; i++)
            {
                cells++;
                if (result.Scores[i].Value == decimal.Parse(scores[i], new System.Globalization.CultureInfo("tr-TR")))
                    correct++;
            }
        }

        Assert.True(correct >= cells * 0.95, $"Puan kutuları: {correct}/{cells} doğru okundu.");
        Assert.True(numbersCorrect >= numbers - 1, $"Öğrenci numaraları: {numbersCorrect}/{numbers} birebir okundu.");
    }

    [Fact]
    public void Page_without_alignment_markers_is_rejected()
    {
        using var blank = new Mat(2339, 1654, MatType.CV_8UC1, new Scalar(255));
        var result = _reader.ReadSheet(blank, VizeSpec());

        Assert.False(result.MarkersFound);
        Assert.Equal(SheetPageKind.Unknown, result.PageKind);
        Assert.Contains(result.Warnings, w => w.Contains("hizalama işaretleri bulunamadı"));
    }

    [Fact]
    public void Page_without_page_code_is_reported_as_foreign_form()
    {
        // Köşe işaretleri var ama sol kenardaki sayfa kodu yok (ör. eski sürüm optik form).
        var spec = VizeSpec();
        var png = ExamPaperSampleRenderer.RenderScanPng(spec, 0, new ExamPaperFill(), ScanSimulation.None);
        using var image = Cv2.ImDecode(png, ImreadModes.Grayscale);
        var pxPerMm = image.Width / OpticalFormTemplate.PageWidthMm;
        image.Rectangle(new Rect((int)(8 * pxPerMm), (int)(130 * pxPerMm), (int)(9 * pxPerMm), (int)(22 * pxPerMm)), Scalar.White, -1);

        var result = _reader.ReadSheet(image, spec);

        Assert.True(result.MarkersFound);
        Assert.Equal(SheetPageKind.Unknown, result.PageKind);
        Assert.Contains(result.Warnings, w => w.Contains("Sayfa kodu bulunamadı"));
    }
}
