namespace EduAnalytics.Business.Dtos;

/// <summary>
/// .edudata paketi (JSON). Kayıtlar kodlarla eşlenir: program ve ders kodu, öğrenci numarası, ÖÇ/PÇ kodu
/// ve paket içi soru kodu. Böylece paket başka bir veritabanına aktarılabilir.
/// <list type="bullet">
/// <item>Sürüm 2.0: programlar, dersler, öğrenciler ve ders kayıtları, konular, ÖÇ'ler, PÇ'ler ve ÖÇ–PÇ
/// eşleşmeleri (katkı düzeyiyle), soru bankası (konu ve ÖÇ bağlarıyla, rubrik ölçütleriyle), sınavlar,
/// kitapçıklar, öğrenci cevapları ve rubrik puanları.</item>
/// <item>Sürüm 1.0 (eski): yalnızca ders, öğrenci, konu ve soru. Bu paketler de okunur.</item>
/// </list>
/// Sınav cevapları kişisel veridir; paket şifrelenmez, yalnızca yetkili kişilerle paylaşılmalıdır.
/// </summary>
public class AdminExportPackageDto
{
    public const string CurrentVersion = "2.0";

    public string Version { get; set; } = CurrentVersion;
    public string Institution { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }

    public List<AdminExportProgramDto> Programs { get; set; } = new();
    public List<AdminExportCourseDto> Courses { get; set; } = new();
    public List<AdminExportStudentDto> Students { get; set; } = new();

    /// <summary>Öğrenci–ders kayıtları. Sürüm 1.0 paketlerinde yoktur (null).</summary>
    public List<AdminExportEnrollmentDto>? Enrollments { get; set; }

    public List<AdminExportTopicDto> Topics { get; set; } = new();
    public List<AdminExportLearningOutcomeDto> LearningOutcomes { get; set; } = new();
    public List<AdminExportProgramOutcomeDto> ProgramOutcomes { get; set; } = new();
    public List<AdminExportQuestionDto> Questions { get; set; } = new();

    /// <summary>Sınavlar, kitapçıklar ve cevaplar (dışa aktarırken isteğe bağlı).</summary>
    public List<AdminExportExamDto> Exams { get; set; } = new();
}

public class AdminExportProgramDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class AdminExportCourseDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Dersin bağlı olduğu programın kodu (sürüm 2.0).</summary>
    public string? ProgramCode { get; set; }
}

public class AdminExportEnrollmentDto
{
    public string CourseCode { get; set; } = string.Empty;
    public string StudentNumber { get; set; } = string.Empty;
}

public class AdminExportTopicDto
{
    public string CourseCode { get; set; } = string.Empty;
    public int WeekNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Konunun bağlı olduğu ÖÇ kodları (aynı derste).</summary>
    public List<string> LearningOutcomeCodes { get; set; } = new();
}

public class AdminExportLearningOutcomeDto
{
    public string CourseCode { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class AdminExportProgramOutcomeDto
{
    public string ProgramCode { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<AdminExportOutcomeMappingDto> Mappings { get; set; } = new();
}

/// <summary>PÇ'ye katkı veren ÖÇ ve katkı düzeyi (1–5).</summary>
public class AdminExportOutcomeMappingDto
{
    public string CourseCode { get; set; } = string.Empty;
    public string LearningOutcomeCode { get; set; } = string.Empty;
    public int ContributionLevel { get; set; } = 3;
}

public class AdminExportQuestionDto
{
    /// <summary>Paket içi soru kodu; sınavlar ve cevaplar soruya bununla bağlanır.</summary>
    public string Code { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int Difficulty { get; set; }
    public string CourseCode { get; set; } = string.Empty;

    /// <summary>İlk konu (sürüm 1.0 uyumluluğu). Sürüm 2.0'da bütün konular <see cref="TopicTitles"/> içindedir.</summary>
    public string? TopicTitle { get; set; }
    public List<string> TopicTitles { get; set; } = new();

    public List<string> LearningOutcomeCodes { get; set; } = new();
    public string QuestionType { get; set; } = string.Empty; // MultipleChoice, OpenEnded
    public bool IsActive { get; set; } = true;
    public List<AdminExportQuestionChoiceDto> Choices { get; set; } = new();
    // For classic questions
    public string? ClassicAnswerKey { get; set; }

    /// <summary>Sorunun tam puanı (adı eski sürümle uyumluluk için korunur; test soruları için de kullanılır).</summary>
    public decimal? ClassicMaxScore { get; set; }
    public List<AdminExportRubricCriteriaDto> RubricCriteria { get; set; } = new();
}

public class AdminExportQuestionChoiceDto
{
    public string Key { get; set; } = string.Empty; // A, B, C...
    public string Content { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
}

public class AdminExportRubricCriteriaDto
{
    public string Name { get; set; } = string.Empty;
    public decimal MaxScore { get; set; }
    public int OrderIndex { get; set; }
    public string? Description { get; set; }
}

public class AdminExportStudentDto
{
    public string Number { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? ClassName { get; set; }
}

public class AdminExportExamDto
{
    public string CourseCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime ExamDate { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public string ExamType { get; set; } = string.Empty;
    public int BookletCount { get; set; } = 1;
    public bool ShuffleOptions { get; set; }
    public List<AdminExportExamQuestionDto> Questions { get; set; } = new();
    public List<AdminExportBookletDto> Booklets { get; set; } = new();
    public List<AdminExportAnswerDto> Answers { get; set; } = new();
}

public class AdminExportExamQuestionDto
{
    public string QuestionCode { get; set; } = string.Empty;
    public int Order { get; set; }
    public decimal? OverrideMaxPoints { get; set; }
    public bool IsCancelled { get; set; }
    public string? CancellationReason { get; set; }
}

public class AdminExportBookletDto
{
    public string Code { get; set; } = string.Empty;
    public List<AdminExportBookletQuestionDto> Questions { get; set; } = new();
}

public class AdminExportBookletQuestionDto
{
    public string QuestionCode { get; set; } = string.Empty;
    public int Order { get; set; }
    public string? ShuffleMap { get; set; }
}

public class AdminExportAnswerDto
{
    public string StudentNumber { get; set; } = string.Empty;
    public string QuestionCode { get; set; } = string.Empty;
    public string? BookletCode { get; set; }

    /// <summary>Orijinal şık harfi (kitapçık karışıklığı çözülmüş): A–E ya da Empty.</summary>
    public string SelectedOption { get; set; } = "Empty";
    public decimal? Score { get; set; }
    public bool IsCorrect { get; set; }
    public List<AdminExportCriterionScoreDto> CriterionScores { get; set; } = new();
}

public class AdminExportCriterionScoreDto
{
    public int CriterionOrder { get; set; }
    public string CriterionTitle { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public string? Comment { get; set; }
}

/// <summary>İçe aktarmanın özeti.</summary>
public class DataImportResult
{
    public int ProgramsAdded { get; set; }
    public int CoursesAdded { get; set; }
    public int StudentsAdded { get; set; }
    public int EnrollmentsAdded { get; set; }
    public int TopicsAdded { get; set; }
    public int LearningOutcomesAdded { get; set; }
    public int ProgramOutcomesAdded { get; set; }
    public int OutcomeMappingsAdded { get; set; }
    public int QuestionsAdded { get; set; }
    public int ExamsAdded { get; set; }

    /// <summary>Veritabanında zaten bulunduğu için atlanan sınavlar (aynı ders, başlık ve tarih).</summary>
    public int ExamsSkipped { get; set; }
    public int AnswersAdded { get; set; }

    /// <summary>Öğretmenin bilmesi gereken durumlar (eksik eşleşmeler, eski paket sürümü…).</summary>
    public List<string> Warnings { get; } = new();
}
