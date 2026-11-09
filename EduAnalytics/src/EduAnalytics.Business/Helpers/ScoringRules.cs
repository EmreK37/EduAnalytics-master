using EduAnalytics.Business.Services.Implementations;
using EduAnalytics.Core.Entities;
using EduAnalytics.Core.Enums;

namespace EduAnalytics.Business.Helpers;

/// <summary>
/// Puanlama ve başarı oranı kuralları. Sınav analizi, öğrenci raporu, konu, ÖÇ ve PÇ raporları
/// hepsi bu tanımları kullanır; böylece aynı veri her ekranda aynı sonucu verir.
/// </summary>
public static class ScoringRules
{
    /// <summary>
    /// ÖÇ, konu ve PÇ başarısında "zayıf" eşiği (yüzde). Bu değerin altındaki başarı zayıf sayılır.
    /// Sınıf raporu, öğrenci raporu ve sınav analizi özeti aynı eşiği kullanır.
    /// </summary>
    public const double WeakSuccessThreshold = 60.0;

    /// <summary>Sorunun bu sınavdaki tam puanı: sınava özgü puan varsa o, yoksa soru bankasındaki puan.</summary>
    public static decimal MaxPoints(ExamQuestion eq) => eq.OverrideMaxPoints ?? eq.Question.MaxPoints;

    /// <summary>
    /// Cevap puanlanmış mı? Test sorusunda her kayıt puanlanmıştır; boş bırakılan cevap 0 puandır.
    /// Klasik soruda puan girilmemişse soru o öğrenci için henüz değerlendirilmemiştir ve oranlara girmez.
    /// </summary>
    public static bool IsScored(Question question, StudentAnswer answer) =>
        question.Type == QuestionType.MultipleChoice || answer.Score.HasValue;

    /// <summary>Öğrencinin sorudan aldığı puan. Test: doğruysa tam puan. Klasik: girilen puan.</summary>
    public static decimal Score(ExamQuestion eq, StudentAnswer answer) =>
        ExamAnalysisService.ComputeScore(eq.Question, answer, eq.OverrideMaxPoints);
}

/// <summary>
/// Bir grup cevabın kazanılan ve alınabilecek puan toplamı. Başarı oranı = kazanılan / alınabilecek.
/// Test ve klasik sorular puanlarıyla birlikte sayılır; puan girilmemiş klasik cevaplar atlanır.
/// </summary>
public sealed class ScoreTally
{
    public decimal Earned { get; private set; }
    public decimal Possible { get; private set; }
    public int Answers { get; private set; }
    public int Correct { get; private set; }

    public void Add(ExamQuestion eq, StudentAnswer answer)
    {
        if (!ScoringRules.IsScored(eq.Question, answer))
            return;

        Earned += ScoringRules.Score(eq, answer);
        Possible += ScoringRules.MaxPoints(eq);
        Answers++;
        if (answer.IsCorrect)
            Correct++;
    }

    /// <summary>Başarı oranı (0–100). Puanlanmış cevap yoksa null.</summary>
    public double? Rate => Possible > 0 ? (double)(Earned / Possible) * 100 : null;
}
