using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Helpers;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.Core.Entities;
using EduAnalytics.Core.Enums;
using EduAnalytics.DataAccess.Context;
using Microsoft.EntityFrameworkCore;

namespace EduAnalytics.Business.Services.Implementations;

/// <summary>
/// Klasik test geliştirme literatüründeki "madde analizi"ni uygular. Test ve klasik sorular birlikte
/// analiz edilir; üst/alt gruplar sınavın toplam puanına göre (Kelley'nin %27 kuralı) belirlenir.
///
/// Test sorusu:
///   • Güçlük        p = doğru / cevap sayısı                         (0–1)
///   • Ayırt edicilik D = (Üst grupta doğru − Alt grupta doğru) / Ng    (−1 .. +1)
///   • Madde güvenilirliği r_jx = D × √(p · (1 − p))
///   • Çeldirici etkinliği: her çeldiricinin seçilme oranı
///
/// Klasik soru (puanı girilmiş cevaplar üzerinden):
///   • Güçlük        p = ortalama puan / tam puan
///   • Ayırt edicilik D = (Üst grup puan toplamı − Alt grup puan toplamı) / (Ng × tam puan);
///     grupta puanı olmayan öğrenci 0 sayılır (testteki "doğru değil" gibi)
///   • Madde güvenilirliği r_jx = D × s, s = (puan / tam puan) değerlerinin standart sapması
///     (test sorusunda s = √(p · (1 − p)) olduğundan iki formül aynıdır)
/// </summary>
public class ItemAnalysisService : IItemAnalysisService
{
    private const double UpperLowerRatio = 0.27;

    /// <summary>Üst/alt grup karşılaştırması için gereken en az öğrenci sayısı.</summary>
    private const int MinStudentsForDiscrimination = 4;

    private const string InsufficientData = "Veri Yetersiz";

    private readonly EduAnalyticsDbContext _context;

    public ItemAnalysisService(EduAnalyticsDbContext context)
    {
        _context = context;
    }

    public async Task<List<ItemAnalysisDto>> AnalyzeAsync(int examId)
    {
        var examQuestions = await _context.ExamQuestions
            .Where(eq => eq.ExamId == examId && !eq.IsCancelled)
            .Include(eq => eq.Question)
            .OrderBy(eq => eq.OrderInExam)
            .ToListAsync();

        if (examQuestions.Count == 0) return new List<ItemAnalysisDto>();

        var qIds = examQuestions.Select(eq => eq.QuestionId).ToList();
        var answers = await _context.StudentAnswers
            .Where(sa => sa.ExamId == examId && qIds.Contains(sa.QuestionId))
            .ToListAsync();

        var examQuestionMap = examQuestions.ToDictionary(eq => eq.QuestionId);

        // Öğrenci toplam puanları (test + klasik), üst/alt grupları belirlemek için.
        var studentTotals = answers
            .GroupBy(a => a.StudentId)
            .Select(g => new
            {
                StudentId = g.Key,
                Total = g.Sum(a => examQuestionMap.TryGetValue(a.QuestionId, out var eq)
                    ? ScoringRules.Score(eq, a)
                    : 0m)
            })
            .OrderByDescending(s => s.Total)
            .ToList();

        // Kelley %27'sini sağlayacak kadar öğrenci yoksa ayırt edicilik hesaplanmaz.
        var groupSize = studentTotals.Count >= MinStudentsForDiscrimination
            ? Math.Max(1, (int)Math.Round(studentTotals.Count * UpperLowerRatio))
            : 0;
        var groups = new ScoreGroups(
            studentTotals.Take(groupSize).Select(s => s.StudentId).ToHashSet(),
            studentTotals.TakeLast(groupSize).Select(s => s.StudentId).ToHashSet(),
            groupSize);

        var answerLookup = answers.ToLookup(a => a.QuestionId);

        return examQuestions
            .Select(eq => eq.Question.Type == QuestionType.MultipleChoice
                ? AnalyzeMultipleChoice(eq, answerLookup[eq.QuestionId].ToList(), groups)
                : AnalyzeOpenEnded(eq, answerLookup[eq.QuestionId].ToList(), groups))
            .ToList();
    }

    private sealed record ScoreGroups(HashSet<int> Upper, HashSet<int> Lower, int Size);

    private static ItemAnalysisDto AnalyzeMultipleChoice(ExamQuestion eq, List<StudentAnswer> qAnswers, ScoreGroups groups)
    {
        var q = eq.Question;
        int total = qAnswers.Count;
        int correct = qAnswers.Count(a => a.IsCorrect);
        int empty = qAnswers.Count(a => a.SelectedOption == OptionLetter.Empty);
        int wrong = total - correct - empty;

        double p = total > 0 ? (double)correct / total : 0;

        // Çeldirici detayları — k şıklı bir soruda ideal: (1 − p) / (k − 1)
        var distractors = BuildDistractors(q, qAnswers, p);

        var dto = new ItemAnalysisDto
        {
            QuestionId = q.Id,
            QuestionNumber = eq.OrderInExam,
            QuestionText = q.QuestionText,
            Type = QuestionType.MultipleChoice,
            MaxPoints = ScoringRules.MaxPoints(eq),
            CorrectOption = q.CorrectOption.ToString(),
            TotalStudents = total,
            CorrectCount = correct,
            WrongCount = wrong,
            EmptyCount = empty,

            DifficultyIndex = Math.Round(p, 3),
            DifficultyCategory = total > 0 ? ClassifyDifficulty(p) : InsufficientData,

            DistractorEffectivenessIndex = Math.Round(ComputeDistractorEffectiveness(distractors), 3),
            Distractors = distractors
        };

        if (groups.Size == 0 || total == 0)
        {
            dto.DiscriminationCategory = InsufficientData;
            return dto;
        }

        // D = (Pü − Pa) / Ng
        int upperCorrect = qAnswers.Count(a => a.IsCorrect && groups.Upper.Contains(a.StudentId));
        int lowerCorrect = qAnswers.Count(a => a.IsCorrect && groups.Lower.Contains(a.StudentId));
        double d = (double)upperCorrect / groups.Size - (double)lowerCorrect / groups.Size;

        dto.HasDiscrimination = true;
        dto.DiscriminationIndex = Math.Round(d, 3);
        dto.DiscriminationCategory = ClassifyDiscrimination(d);
        dto.UpperGroupCorrect = upperCorrect;
        dto.LowerGroupCorrect = lowerCorrect;
        dto.GroupSize = groups.Size;
        dto.ItemReliabilityIndex = Math.Round(d * Math.Sqrt(p * (1 - p)), 3);
        return dto;
    }

    private static ItemAnalysisDto AnalyzeOpenEnded(ExamQuestion eq, List<StudentAnswer> qAnswers, ScoreGroups groups)
    {
        var q = eq.Question;
        var max = (double)ScoringRules.MaxPoints(eq);

        // Puanı girilmemiş cevap henüz değerlendirilmemiştir; analize girmez.
        var scoreByStudent = new Dictionary<int, double>();
        foreach (var a in qAnswers)
        {
            if (a.Score is decimal s)
                scoreByStudent.TryAdd(a.StudentId, (double)s);
        }

        int total = scoreByStudent.Count;
        int passed = scoreByStudent.Values.Count(s => s >= max / 2);
        double mean = total > 0 ? scoreByStudent.Values.Average() : 0;
        double p = total > 0 && max > 0 ? mean / max : 0;

        var dto = new ItemAnalysisDto
        {
            QuestionId = q.Id,
            QuestionNumber = eq.OrderInExam,
            QuestionText = q.QuestionText,
            Type = QuestionType.OpenEnded,
            MaxPoints = (decimal)max,
            CorrectOption = "—",
            TotalStudents = total,
            CorrectCount = passed,
            WrongCount = total - passed,
            EmptyCount = 0,
            AverageScore = total > 0 ? Math.Round(mean, 2) : null,

            DifficultyIndex = Math.Round(p, 3),
            DifficultyCategory = total > 0 ? ClassifyDifficulty(p) : InsufficientData
        };

        if (groups.Size == 0 || total == 0 || max <= 0)
        {
            dto.DiscriminationCategory = InsufficientData;
            return dto;
        }

        double upperSum = groups.Upper.Sum(id => scoreByStudent.GetValueOrDefault(id));
        double lowerSum = groups.Lower.Sum(id => scoreByStudent.GetValueOrDefault(id));
        double d = (upperSum - lowerSum) / (groups.Size * max);

        double sd = Math.Sqrt(scoreByStudent.Values.Average(s => Math.Pow(s / max - p, 2)));

        dto.HasDiscrimination = true;
        dto.DiscriminationIndex = Math.Round(d, 3);
        dto.DiscriminationCategory = ClassifyDiscrimination(d);
        dto.UpperGroupCorrect = groups.Upper.Count(id => scoreByStudent.GetValueOrDefault(id) >= max / 2);
        dto.LowerGroupCorrect = groups.Lower.Count(id => scoreByStudent.GetValueOrDefault(id) >= max / 2);
        dto.GroupSize = groups.Size;
        dto.ItemReliabilityIndex = Math.Round(d * sd, 3);
        return dto;
    }

    private static List<DistractorDetailDto> BuildDistractors(
        Question q,
        List<StudentAnswer> answers,
        double p)
    {
        var options = new[] { OptionLetter.A, OptionLetter.B, OptionLetter.C, OptionLetter.D, OptionLetter.E };
        var available = options.Where(opt => HasOption(q, opt)).ToList();
        int k = available.Count;
        int total = answers.Count;

        // İdeal çeldirici oranı = (1 − p) / (k − 1)
        double idealRate = k > 1 ? (1 - p) / (k - 1) * 100 : 0;

        var list = new List<DistractorDetailDto>();
        foreach (var opt in available)
        {
            int cnt = answers.Count(a => a.SelectedOption == opt);
            double rate = total > 0 ? (double)cnt / total * 100 : 0;
            bool isCorrect = opt == q.CorrectOption;

            list.Add(new DistractorDetailDto
            {
                Option = opt.ToString(),
                SelectedCount = cnt,
                SelectionRate = Math.Round(rate, 1),
                IsCorrectOption = isCorrect,
                IdealRate = Math.Round(idealRate, 1),
                // En az %5 öğrencinin seçtiği çeldirici "etkili" sayılır
                IsEffective = !isCorrect && rate >= 5.0
            });
        }
        return list;
    }

    private static bool HasOption(Question q, OptionLetter opt) => opt switch
    {
        OptionLetter.A => !string.IsNullOrWhiteSpace(q.OptionA),
        OptionLetter.B => !string.IsNullOrWhiteSpace(q.OptionB),
        OptionLetter.C => !string.IsNullOrWhiteSpace(q.OptionC),
        OptionLetter.D => !string.IsNullOrWhiteSpace(q.OptionD),
        OptionLetter.E => !string.IsNullOrWhiteSpace(q.OptionE),
        _ => false
    };

    /// <summary>
    /// Doğru şık dışındaki çeldiricilerin etkinlik oranı.
    /// Etkili çeldirici / toplam çeldirici sayısı.
    /// </summary>
    private static double ComputeDistractorEffectiveness(List<DistractorDetailDto> distractors)
    {
        var nonCorrect = distractors.Where(d => !d.IsCorrectOption).ToList();
        if (nonCorrect.Count == 0) return 0;
        return (double)nonCorrect.Count(d => d.IsEffective) / nonCorrect.Count;
    }

    /// <summary>
    /// Klasik (Crocker &amp; Algina, 1986) zorluk kategorileri:
    ///   p &lt; 0.20 → Çok Zor, &lt; 0.40 → Zor, &lt; 0.60 → Orta,
    ///   &lt; 0.80 → Kolay, ≥ 0.80 → Çok Kolay.
    /// </summary>
    private static string ClassifyDifficulty(double p) => p switch
    {
        < 0.20 => "Çok Zor",
        < 0.40 => "Zor",
        < 0.60 => "Orta",
        < 0.80 => "Kolay",
        _ => "Çok Kolay"
    };

    /// <summary>
    /// Ebel (1979) ayırt edicilik kategorileri:
    ///   D &lt; 0.20 → Zayıf (atılmalı),
    ///   0.20 ≤ D &lt; 0.30 → Düzeltilmeli,
    ///   0.30 ≤ D &lt; 0.40 → Kabul edilebilir,
    ///   D ≥ 0.40 → Çok iyi.
    /// </summary>
    private static string ClassifyDiscrimination(double d) => d switch
    {
        < 0.20 => "Zayıf (Atılmalı)",
        < 0.30 => "Düzeltilmeli",
        < 0.40 => "Kabul Edilebilir",
        _ => "Çok İyi"
    };
}
