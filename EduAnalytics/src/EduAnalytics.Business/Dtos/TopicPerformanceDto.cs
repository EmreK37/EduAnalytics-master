namespace EduAnalytics.Business.Dtos;

/// <summary>
/// Bir konunun (Bologna çıktısı) sınıf genelinde ne kadar öğrenildiğini gösterir.
/// </summary>
public class TopicPerformanceDto
{
    public int TopicId { get; set; }
    public int WeekNumber { get; set; }
    public string TopicTitle { get; set; } = null!;
    public string? LearningOutcome { get; set; }

    public int RelatedQuestionCount { get; set; }     // Bu konuyu ölçen soru sayısı
    public int TotalAnswers { get; set; }             // Puanlanmış cevap sayısı
    public int CorrectAnswers { get; set; }

    /// <summary>Puanlanmış cevap var mı? Yoksa başarı oranı gösterilmemeli.</summary>
    public bool HasData { get; set; }

    public double SuccessRate { get; set; }           // 0-100: kazanılan / alınabilecek puan

    /// <summary>Konu ve ÖÇ satırının alt metni: bağlı ÖÇ'ler ve soru sayısı.</summary>
    public string Details => (string.IsNullOrWhiteSpace(LearningOutcome) ? "" : $"{LearningOutcome} · ") +
                             $"{RelatedQuestionCount} soru" +
                             (HasData ? "" : " · henüz puanlanmış cevap yok");

    /// <summary>
    /// Zayıflık seviyesi: 0-40 arası KRİTİK, 40-60 ZAYIF, 60-80 ORTA, 80+ İYİ.
    /// </summary>
    public string PerformanceLevel => SuccessRate switch
    {
        < 40 => "KRİTİK",
        < 60 => "ZAYIF",
        < 80 => "ORTA",
        _ => "İYİ"
    };
}
