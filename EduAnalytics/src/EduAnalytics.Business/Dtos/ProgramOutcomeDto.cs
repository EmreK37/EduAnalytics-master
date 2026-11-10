namespace EduAnalytics.Business.Dtos;

/// <summary>
/// Program Çıktısı (PÇ) görünümü.
/// </summary>
public class ProgramOutcomeDto
{
    public int Id { get; set; }
    public int ProgramId { get; set; }
    public string ProgramName { get; set; } = null!;
    public string Code { get; set; } = null!;
    public string Description { get; set; } = null!;

    /// <summary>Bu PÇ'ye katkı sağlayan ÖÇ sayısı.</summary>
    public int LearningOutcomeCount { get; set; }
}

/// <summary>
/// Program → Ders → ÖÇ → Soru hiyerarşik raporu.
/// "Bu programın PÇ-3'üne hangi ÖÇ'ler ne kadar katkı sağlıyor, hangi sorular bu ÖÇ'leri ölçüyor?"
/// </summary>
public class ProgramOutcomeReportDto
{
    public int ProgramOutcomeId { get; set; }
    public string ProgramOutcomeCode { get; set; } = null!;
    public string ProgramOutcomeDescription { get; set; } = null!;

    public List<LinkedLearningOutcomeDto> LinkedLearningOutcomes { get; set; } = new();

    /// <summary>
    /// Bağlı ÖÇ başarılarının katkı düzeyiyle (1–5) ağırlıklı ortalaması. Verisi olmayan ÖÇ'ler girmez.
    /// </summary>
    public double OverallSuccessRate { get; set; }

    /// <summary>En az bir bağlı ÖÇ'nün puanlanmış cevabı var mı? Yoksa başarı oranı gösterilmemeli.</summary>
    public bool HasData { get; set; }

    public string OverallSuccessDisplay => HasData
        ? "%" + OverallSuccessRate.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture)
        : "veri yok";

    public int TotalQuestionCount { get; set; }
    public int TotalAnswerCount { get; set; }
}

public class LinkedLearningOutcomeDto
{
    public int LearningOutcomeId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string CourseName { get; set; } = null!;

    /// <summary>ÖÇ'nün PÇ'ye katkı düzeyi (1–5); PÇ ortalamasında ağırlık olarak kullanılır.</summary>
    public int ContributionLevel { get; set; }

    /// <summary>ÖÇ'yü ölçen ve en az bir sınavda (iptal edilmeden) sorulmuş soru sayısı.</summary>
    public int QuestionCount { get; set; }

    /// <summary>Puanlanmış cevap sayısı.</summary>
    public int AnswerCount { get; set; }

    /// <summary>Puanlanmış cevap var mı? Yoksa <see cref="SuccessRate"/> anlamsızdır.</summary>
    public bool HasData { get; set; }

    /// <summary>Kazanılan puan / alınabilecek puan × 100 (test ve klasik sorular birlikte).</summary>
    public double SuccessRate { get; set; }

    public string SuccessDisplay => HasData
        ? "%" + SuccessRate.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture)
        : "veri yok";
}

public class ProgramOutcomeCreateModel
{
    public int ProgramId { get; set; }
    public string Code { get; set; } = null!;
    public string Description { get; set; } = null!;
}

/// <summary>
/// PÇ-ÖÇ eşleştirme ekranı için, bir PÇ'ye bağlı ÖÇ'nün özet bilgileri + katkı seviyesi.
/// </summary>
public class MappedLearningOutcomeDto
{
    public int LearningOutcomeId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string CourseName { get; set; } = null!;
    public int ContributionLevel { get; set; }
}
