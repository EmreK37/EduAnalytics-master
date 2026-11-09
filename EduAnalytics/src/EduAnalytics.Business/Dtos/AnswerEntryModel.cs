using EduAnalytics.Core.Enums;

namespace EduAnalytics.Business.Dtos;

/// <summary>
/// Cevap girişi ekranı için tüm veriyi bir arada taşır.
/// </summary>
public class AnswerEntryModel
{
    public int ExamId { get; set; }
    public string ExamTitle { get; set; } = null!;
    public string CourseName { get; set; } = null!;
    public int BookletCount { get; set; } = 1;
    public List<AnswerEntryQuestion> Questions { get; set; } = new();
    public List<AnswerEntryStudent> Students { get; set; } = new();

    /// <summary>Sınavın kitapçıkları (BookletCode → BookletId) — UI'da öğrenci-kitapçık eşleştirme için.</summary>
    public List<BookletInfo> AvailableBooklets { get; set; } = new();
}

public class BookletInfo
{
    public int BookletId { get; set; }
    public string BookletCode { get; set; } = null!;
}

public class AnswerEntryQuestion
{
    public int QuestionId { get; set; }

    /// <summary>Sınavdaki sıra (ExamQuestion.OrderInExam'den gelir).</summary>
    public int QuestionNumber { get; set; }
    public QuestionType Type { get; set; }
    public decimal MaxPoints { get; set; }
    public OptionLetter CorrectOption { get; set; }
    public string QuestionTextPreview { get; set; } = string.Empty;

    /// <summary>Sınav sırasında iptal edildi mi?</summary>
    public bool IsCancelled { get; set; }
}

public class AnswerEntryStudent
{
    public int StudentId { get; set; }
    public string StudentNumber { get; set; } = null!;
    public string FullName { get; set; } = null!;

    /// <summary>Öğrencinin sınavda kullandığı kitapçığın Id'si. null = tek kitapçık veya seçilmedi.</summary>
    public int? BookletId { get; set; }
    public string? BookletCode { get; set; }

    public Dictionary<int, AnswerEntryCell> Answers { get; set; } = new();
}

public class AnswerEntryCell
{
    public OptionLetter SelectedOption { get; set; } = OptionLetter.Empty;
    public decimal? Score { get; set; }

    /// <summary>Bu klasik puanın kayıtlı bir rubrik kriter dökümü var mı?</summary>
    public bool HasRubricBreakdown { get; set; }
}

public class StudentAnswerUpdate
{
    public int ExamId { get; set; }
    public int QuestionId { get; set; }
    public int StudentId { get; set; }
    public int? BookletId { get; set; }
    public OptionLetter SelectedOption { get; set; }
    public decimal? Score { get; set; }
}

/// <summary>Cevap kaydının özeti; ekranda öğretmene ne olduğunu anlatmak için.</summary>
public class AnswerSaveResult
{
    /// <summary>Sınava girmiş sayılıp cevapları kaydedilen öğrenci sayısı.</summary>
    public int StudentsSaved { get; set; }

    /// <summary>Boş olarak kaydedilen test cevabı sayısı (0 puan; madde analizinde "boş").</summary>
    public int BlankAnswers { get; set; }

    /// <summary>Satırı tamamen boş olduğu için sınava girmemiş sayılan öğrenci sayısı.</summary>
    public int AbsentStudents { get; set; }

    /// <summary>Önceden cevabı olan ama satırı boşaltıldığı için cevapları silinen öğrenci sayısı.</summary>
    public int ClearedStudents { get; set; }

    /// <summary>Puanı değiştiği için rubrik kriter dökümü silinen klasik cevap sayısı.</summary>
    public int RubricBreakdownsCleared { get; set; }
}
