using EduAnalytics.Business.Helpers;

namespace EduAnalytics.Business.Dtos;

public class LearningOutcomePerformanceDto
{
    public int LearningOutcomeId { get; set; }
    public string OutcomeName { get; set; } = null!;
    public string? Description { get; set; }

    public int RelatedQuestionCount { get; set; }

    /// <summary>Puanlanmış cevap sayısı (test + puanı girilmiş klasik).</summary>
    public int TotalAnswers { get; set; }

    /// <summary>Doğru test cevabı ve tam puanın en az yarısını alan klasik cevap sayısı.</summary>
    public int CorrectAnswers { get; set; }

    public decimal EarnedPoints { get; set; }
    public decimal PossiblePoints { get; set; }

    /// <summary>Kazanılan puan / alınabilecek puan × 100.</summary>
    public double SuccessRate { get; set; }

    /// <summary>Başarı, ortak zayıflık eşiğinin (<see cref="ScoringRules.WeakSuccessThreshold"/>) altında mı?</summary>
    public bool IsWeak => SuccessRate < ScoringRules.WeakSuccessThreshold;

    public string PerformanceLevel => SuccessRate switch
    {
        < 40 => "KRİTİK",
        < ScoringRules.WeakSuccessThreshold => "ZAYIF",
        < 80 => "ORTA",
        _ => "İYİ"
    };
}
