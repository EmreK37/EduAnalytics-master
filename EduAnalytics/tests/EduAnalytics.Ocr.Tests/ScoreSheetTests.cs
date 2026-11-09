using EduAnalytics.Ocr;
using EduAnalytics.Ocr.Recognition;
using OpenCvSharp;

namespace EduAnalytics.Ocr.Tests;

/// <summary>Puan değerlendirmesi, puan metni ayrıştırma, rakam modeli ve olasılıkla öğrenci eşleştirme.</summary>
public class ScoreSheetTests
{
    [Theory]
    [InlineData("25", 25)]
    [InlineData("7,5", 7.5)]
    [InlineData("0", 0)]
    [InlineData("-", 0)]
    [InlineData("100", 100)]
    public void Valid_score_texts_are_parsed(string text, double expected)
        => Assert.Equal((decimal)expected, HandwrittenNumberReader.ParseScore(text));

    [Theory]
    [InlineData("05")]
    [InlineData(",5")]
    [InlineData("5,")]
    [InlineData("1,234")]
    [InlineData("1-2")]
    [InlineData("")]
    public void Invalid_score_texts_are_rejected(string text)
        => Assert.Null(HandwrittenNumberReader.ParseScore(text));

    [Fact]
    public void Multiple_choice_points_come_from_optical_marks_and_classic_from_the_table()
    {
        var evaluation = ScoreSheetEvaluator.Evaluate(new[]
        {
            new QuestionScore(1, true, 10, OpticalPoints: 10, TableScore: 10),
            new QuestionScore(2, true, 10, OpticalPoints: 0, TableScore: null),
            new QuestionScore(3, false, 30, OpticalPoints: null, TableScore: 22.5m)
        }, writtenTotal: 32.5m);

        Assert.Equal(new decimal?[] { 10, 0, 22.5m }, evaluation.EffectiveScores);
        Assert.Equal(32.5m, evaluation.ComputedTotal);
        Assert.True(evaluation.TotalMatches);
        Assert.Empty(evaluation.Warnings);
    }

    [Fact]
    public void Table_score_disagreeing_with_optical_reading_and_wrong_total_are_flagged()
    {
        var evaluation = ScoreSheetEvaluator.Evaluate(new[]
        {
            new QuestionScore(1, true, 10, OpticalPoints: 10, TableScore: 0),
            new QuestionScore(2, false, 30, OpticalPoints: null, TableScore: null),
            new QuestionScore(3, false, 30, OpticalPoints: null, TableScore: 35)
        }, writtenTotal: 50);

        Assert.False(evaluation.TotalMatches);
        Assert.Equal(1, evaluation.MissingClassicScores);
        Assert.Contains(evaluation.Warnings, w => w.StartsWith("Soru 1 (test)") && w.Contains("optik"));
        Assert.Contains(evaluation.Warnings, w => w.StartsWith("Soru 2 (klasik)") && w.Contains("boş"));
        Assert.Contains(evaluation.Warnings, w => w.StartsWith("Soru 3:") && w.Contains("aşıyor"));
        Assert.Contains(evaluation.Warnings, w => w.StartsWith("Toplam Not kutusunda 50"));
    }

    [Fact]
    public void Digit_model_recognizes_unseen_synthetic_handwriting()
    {
        // Rakam modeli MNIST + yazı tipleriyle eğitildi; bu el yazısı üreticisini hiç görmedi.
        var rng = new Random(77);
        var reader = new HandwrittenNumberReader();
        var correct = 0;
        const int samples = 300;
        for (var i = 0; i < samples; i++)
        {
            var digit = rng.Next(10);
            var style = SyntheticHandwriting.WriterStyle.Random(rng);
            var ink = SyntheticHandwriting.Write(digit.ToString(), 4, 7.4, 5.6, style, rng);
            using var box = new Mat((int)(8.5 * OpticalFormTemplate.PxPerMm), (int)(8 * OpticalFormTemplate.PxPerMm), MatType.CV_8UC1, Scalar.White);
            SyntheticHandwriting.Draw(box, ink, OpticalFormTemplate.PxPerMm);
            var p = reader.ReadDigitBox(box, new MmRect(0, 0, 8, 8.5));
            if (p != null && Array.IndexOf(p, p.Max()) == digit)
                correct++;
        }
        Assert.True(correct >= samples * 0.98, $"{correct}/{samples}");
    }

    private static readonly StudentCandidate[] Students =
    {
        new(11, "202503011060", "Eren Yılmaz"),
        new(12, "202503011038", "Kerem Aslan"),
        new(13, "202403011003", "Elif Arslan"),
        new(14, "202403011059", "Can Aydın"),
        new(15, "202403011070", "İrem Yıldız")
    };

    /// <summary>Okunan rakamdan olasılık dizisi: verilen olasılıkla o rakam, kalanı diğerlerine eşit.</summary>
    private static float[] Digit(int digit, float p = 0.98f, int? second = null, float secondP = 0)
    {
        var rest = (1 - p - secondP) / (second.HasValue ? 8 : 9);
        var probs = Enumerable.Repeat(rest, 10).ToArray();
        probs[digit] = p;
        if (second.HasValue)
            probs[second.Value] = secondP;
        return probs;
    }

    private static float[]?[] Number(string digits) => digits.Select(c => (float[]?)Digit(c - '0')).ToArray();

    [Fact]
    public void Handwritten_number_matches_exactly()
    {
        var (id, kind) = StudentMatcher.MatchHandwritten(Number("202403011003"), "", Students);
        Assert.Equal(13, id);
        Assert.Equal(MatchKind.ExactNumber, kind);
    }

    [Fact]
    public void Uncertain_digits_are_resolved_by_the_whole_number_likelihood()
    {
        // 11. hane "7" mi "1" mi belirsiz, 12. hane yanlışlıkla "6" okunmuş (gerçek: 0) — yalnızca İrem uyar.
        var digits = Number("202403011076");
        digits[10] = Digit(1, 0.55f, second: 7, secondP: 0.4f);
        digits[11] = Digit(6, 0.7f, second: 0, secondP: 0.25f);

        var (id, kind) = StudentMatcher.MatchHandwritten(digits, "", Students);

        Assert.Equal(15, id);
        Assert.Equal(MatchKind.FuzzyNumber, kind);
    }

    [Fact]
    public void Ambiguous_handwritten_number_is_not_matched()
    {
        // Son iki hane tamamen belirsiz: 202403011003 ve 202403011059 eşit olası → eşleştirme yapılmaz.
        var digits = Number("2024030110??".Replace('?', '0'));
        digits[10] = Enumerable.Repeat(0.1f, 10).ToArray();
        digits[11] = Enumerable.Repeat(0.1f, 10).ToArray();

        var (id, _) = StudentMatcher.MatchHandwritten(digits, "", Students);

        Assert.Null(id);
    }
}
