using System.Globalization;
using EduAnalytics.Core.Enums;

namespace EduAnalytics.Business.Dtos;

/// <summary>
/// Klasik test geliştirme literatüründeki "madde analizi" çıktısı.
/// Tek bir soru için zorluk indeksi (p), ayırt edicilik indeksi (D),
/// madde güvenilirlik indeksi, çeldirici etkinlik indeksi ve
/// üst grup / alt grup doğru sayılarını içerir.
/// </summary>
public class ItemAnalysisDto
{
    public int QuestionId { get; set; }
    public int QuestionNumber { get; set; }
    public string QuestionText { get; set; } = null!;

    /// <summary>Test sorusunda doğru şık; klasik soruda "—".</summary>
    public string CorrectOption { get; set; } = null!;

    public QuestionType Type { get; set; } = QuestionType.MultipleChoice;
    public bool IsOpenEnded => Type == QuestionType.OpenEnded;

    /// <summary>Sorunun bu sınavdaki tam puanı.</summary>
    public decimal MaxPoints { get; set; }

    /// <summary>Klasik soruda ortalama puan (puanı girilmiş cevaplar); test sorusunda null.</summary>
    public double? AverageScore { get; set; }

    // Temel sayılar
    public int TotalStudents { get; set; }      // Maddeye puanlanmış cevabı olan öğrenci
    public int CorrectCount { get; set; }       // Test: doğru. Klasik: tam puanın en az yarısını alan
    public int WrongCount { get; set; }         // Test: yanlış. Klasik: yarısının altında kalan
    public int EmptyCount { get; set; }         // Test: boş bırakan. Klasik: 0

    /// <summary>Maddeye puanlanmış en az bir cevap var mı?</summary>
    public bool HasData => TotalStudents > 0;

    /// <summary>Ayırt edicilik hesaplanabildi mi? (en az 4 öğrenci ve maddeye cevap gerekir)</summary>
    public bool HasDiscrimination { get; set; }

    // ─── Tablo gösterimi (klasik soruda test'e özgü alanlar "—") ───
    public string TypeLabel => IsOpenEnded ? "Klasik" : "Test";

    public string AverageScoreDisplay => AverageScore is double avg
        ? $"{avg.ToString("0.##", CultureInfo.CurrentCulture)} / {MaxPoints.ToString("0.##", CultureInfo.CurrentCulture)}"
        : "—";

    public string EmptyCountDisplay => IsOpenEnded ? "—" : EmptyCount.ToString(CultureInfo.CurrentCulture);

    public string DistractorEffectivenessDisplay => IsOpenEnded
        ? "—"
        : DistractorEffectivenessIndex.ToString("N2", CultureInfo.CurrentCulture);

    // ─── Zorluk indeksi (p) ───
    /// <summary>p = doğru / toplam.  0..1 arası, 0=çok zor, 1=çok kolay.</summary>
    public double DifficultyIndex { get; set; }
    /// <summary>"Çok Zor / Zor / Orta / Kolay / Çok Kolay" sözel kategorisi.</summary>
    public string DifficultyCategory { get; set; } = string.Empty;

    // ─── Ayırt edicilik indeksi (D) ───
    /// <summary>D = (Üst%27 doğru − Alt%27 doğru) / N_grup.  −1..+1 arası.</summary>
    public double DiscriminationIndex { get; set; }
    public string DiscriminationCategory { get; set; } = string.Empty;
    public int UpperGroupCorrect { get; set; }      // Üst %27 grupta doğru sayan
    public int LowerGroupCorrect { get; set; }      // Alt %27 grupta doğru sayan
    public int GroupSize { get; set; }              // Her grupta kaç öğrenci var

    // ─── Madde güvenilirlik indeksi ───
    /// <summary>r_jx = D × √(p × (1 − p)).  Maddenin teste katkısının ölçüsü.</summary>
    public double ItemReliabilityIndex { get; set; }

    // ─── Çeldirici etkinlik indeksi ───
    /// <summary>Çeldiricilerin toplam etkinliği (0..1). Yüksek = iyi çeldiriciler.</summary>
    public double DistractorEffectivenessIndex { get; set; }
    public List<DistractorDetailDto> Distractors { get; set; } = new();
}

public class DistractorDetailDto
{
    public string Option { get; set; } = null!;       // "A", "B", "C", "D", "E"
    public int SelectedCount { get; set; }
    public double SelectionRate { get; set; }         // 0..100
    public bool IsCorrectOption { get; set; }
    /// <summary>İdeal çeldirici oranı = (1 − p) / (k − 1). Bunun altındaysa zayıf.</summary>
    public double IdealRate { get; set; }
    public bool IsEffective { get; set; }             // En az %5 öğrenci tarafından seçildi mi?
}
