namespace EduAnalytics.Business.Dtos;

public class RubricCriterionDto
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public string Title { get; set; } = null!;
    public decimal MaxPoints { get; set; }
    public int Order { get; set; }
    public string? Description { get; set; }
}

public class RubricCriterionCreateModel
{
    /// <summary>Var olan ölçütü güncellemek için Id; yeni ölçüt için null.</summary>
    public int? Id { get; set; }
    public string Title { get; set; } = null!;
    public decimal MaxPoints { get; set; }
    public int Order { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Bir öğrencinin bir klasik soruya kriter-bazlı puanlamasını yansıtır.
/// </summary>
public class StudentRubricGradeDto
{
    public int StudentAnswerId { get; set; }
    public int StudentId { get; set; }
    public int QuestionId { get; set; }
    public List<CriterionScoreDto> CriterionScores { get; set; } = new();
    public decimal TotalScore => CriterionScores.Sum(c => c.Score);

    /// <summary>Ölçüt puanlarının toplamı (rubriğin tam puanı).</summary>
    public decimal CriteriaTotal => CriterionScores.Sum(c => c.MaxPoints);

    /// <summary>Sorunun bu sınavdaki tam puanı. Rubrikle puanlamak için ölçüt toplamına eşit olmalı.</summary>
    public decimal ExamMaxPoints { get; set; }

    /// <summary>Öğrencinin şu anki puanı (cevap girişinden ya da optik okumadan gelmiş olabilir).</summary>
    public decimal? CurrentScore { get; set; }

    /// <summary>Kayıtlı bir kriter dökümü var mı?</summary>
    public bool HasBreakdown { get; set; }
}

public class CriterionScoreDto
{
    public int CriterionId { get; set; }
    public string CriterionTitle { get; set; } = null!;
    public decimal MaxPoints { get; set; }
    public decimal Score { get; set; }
    public string? Comment { get; set; }
}

public class CriterionScoreUpdate
{
    public int CriterionId { get; set; }
    public decimal Score { get; set; }
    public string? Comment { get; set; }
}
