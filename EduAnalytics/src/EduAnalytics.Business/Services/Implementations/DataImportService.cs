using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Helpers;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.Core.Entities;
using EduAnalytics.Core.Enums;
using EduAnalytics.DataAccess.Context;
using Microsoft.EntityFrameworkCore;

namespace EduAnalytics.Business.Services.Implementations;

/// <summary>
/// .edudata paketini içe aktarır. Kayıtlar kodlarla eşlenir; var olan kayıtlar değiştirilmez, yalnızca
/// eksik olanlar ve eksik bağlar (konu–ÖÇ, soru–konu, soru–ÖÇ, ÖÇ–PÇ, ders kaydı) eklenir. Aynı ders,
/// başlık ve tarihe sahip bir sınav zaten varsa paketteki sınav (ve cevapları) atlanır; böylece paket
/// birden çok kez yüklense de kopya oluşmaz.
/// </summary>
public class DataImportService : IDataImportService
{
    private readonly EduAnalyticsDbContext _context;

    public DataImportService(EduAnalyticsDbContext context)
    {
        _context = context;
    }

    public async Task<DataImportResult> ImportAdminPackageAsync(AdminExportPackageDto package)
    {
        if (package == null) throw new ArgumentNullException(nameof(package));

        var result = new DataImportResult();
        var defaultUserId = await EnsureDefaultUserAsync();

        var programIds = await ImportProgramsAsync(package, result);
        var courseIds = await ImportCoursesAsync(package, programIds, defaultUserId, result);
        var studentIds = await ImportStudentsAsync(package, result);
        await ImportEnrollmentsAsync(package, courseIds, studentIds, result);
        var outcomeIds = await ImportLearningOutcomesAsync(package, courseIds, result);
        await ImportTopicsAsync(package, courseIds, outcomeIds, result);
        await ImportProgramOutcomesAsync(package, programIds, courseIds, outcomeIds, result);
        var questionIds = await ImportQuestionsAsync(package, courseIds, outcomeIds, defaultUserId, result);
        await ImportExamsAsync(package, courseIds, studentIds, questionIds, defaultUserId, result);

        return result;
    }

    // ─────────────────────────── Temel kayıtlar ───────────────────────────

    private async Task<int> EnsureDefaultUserAsync()
    {
        var existingUser = await _context.Users.OrderBy(u => u.Id).FirstOrDefaultAsync();
        if (existingUser != null)
            return existingUser.Id;

        var newUser = new User { FullName = "Admin", Email = "admin@edu.local", PasswordHash = "123", Role = UserRole.Admin, CreatedAt = DateTime.UtcNow };
        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();
        return newUser.Id;
    }

    private async Task<Dictionary<string, int>> ImportProgramsAsync(AdminExportPackageDto package, DataImportResult result)
    {
        var programIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in await _context.Programs.OrderBy(p => p.Id).ToListAsync())
            programIds.TryAdd(p.Code.Trim(), p.Id);

        var added = new List<Program>();
        foreach (var p in package.Programs ?? new List<AdminExportProgramDto>())
        {
            var code = p.Code?.Trim();
            if (string.IsNullOrEmpty(code) || programIds.ContainsKey(code) || added.Any(a => a.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                continue;
            added.Add(new Program
            {
                Code = code,
                Name = string.IsNullOrWhiteSpace(p.Name) ? code : p.Name.Trim(),
                Description = p.Description
            });
        }

        if (added.Count > 0)
        {
            _context.Programs.AddRange(added);
            await _context.SaveChangesAsync();
            foreach (var p in added)
                programIds[p.Code] = p.Id;
            result.ProgramsAdded += added.Count;
        }

        return programIds;
    }

    /// <summary>Program kodu olmayan (eski paket) dersler için: ilk program, yoksa "GENEL".</summary>
    private async Task<int> DefaultProgramIdAsync(Dictionary<string, int> programIds, DataImportResult result)
    {
        var first = await _context.Programs.OrderBy(p => p.Id).FirstOrDefaultAsync();
        if (first != null)
            return first.Id;

        var program = new Program { Code = "GENEL", Name = "Genel Program" };
        _context.Programs.Add(program);
        await _context.SaveChangesAsync();
        programIds[program.Code] = program.Id;
        result.ProgramsAdded++;
        return program.Id;
    }

    private async Task<Dictionary<string, int>> ImportCoursesAsync(
        AdminExportPackageDto package, Dictionary<string, int> programIds, int defaultUserId, DataImportResult result)
    {
        var courseIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in await _context.Courses.OrderBy(c => c.Id).ToListAsync())
            courseIds.TryAdd(c.Code.Trim(), c.Id);

        var added = new List<Course>();
        foreach (var c in package.Courses ?? new List<AdminExportCourseDto>())
        {
            var code = c.Code?.Trim();
            if (string.IsNullOrEmpty(code) || courseIds.ContainsKey(code) || added.Any(a => a.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                continue;

            var programId = !string.IsNullOrWhiteSpace(c.ProgramCode) && programIds.TryGetValue(c.ProgramCode.Trim(), out var pid)
                ? pid
                : await DefaultProgramIdAsync(programIds, result);

            added.Add(new Course
            {
                Code = code,
                Name = string.IsNullOrWhiteSpace(c.Name) ? "İsimsiz Ders" : c.Name.Trim(),
                Description = c.Description,
                ProgramId = programId,
                CreatedByUserId = defaultUserId
            });
        }

        if (added.Count > 0)
        {
            _context.Courses.AddRange(added);
            await _context.SaveChangesAsync();
            foreach (var c in added)
                courseIds[c.Code] = c.Id;
            result.CoursesAdded += added.Count;
        }

        return courseIds;
    }

    private async Task<Dictionary<string, int>> ImportStudentsAsync(AdminExportPackageDto package, DataImportResult result)
    {
        var studentIds = new Dictionary<string, int>();
        foreach (var s in await _context.Students.OrderBy(s => s.Id).ToListAsync())
            studentIds.TryAdd(s.StudentNumber.Trim(), s.Id);

        var added = new List<Student>();
        var invalid = 0;
        foreach (var s in package.Students ?? new List<AdminExportStudentDto>())
        {
            if (!StudentNumberRules.IsValid(s.Number))
            {
                invalid++;
                continue;
            }
            var number = s.Number!.Trim();
            if (studentIds.ContainsKey(number) || added.Any(a => a.StudentNumber == number))
                continue;

            added.Add(new Student
            {
                StudentNumber = number,
                FullName = string.IsNullOrWhiteSpace(s.FirstName) && string.IsNullOrWhiteSpace(s.LastName)
                    ? "İsimsiz Öğrenci"
                    : $"{s.FirstName} {s.LastName}".Trim(),
                ClassName = string.IsNullOrWhiteSpace(s.ClassName) ? "Belirtilmemiş" : s.ClassName
            });
        }

        if (added.Count > 0)
        {
            _context.Students.AddRange(added);
            await _context.SaveChangesAsync();
            foreach (var s in added)
                studentIds[s.StudentNumber] = s.Id;
            result.StudentsAdded += added.Count;
        }

        if (invalid > 0)
            result.Warnings.Add($"{invalid} öğrencinin numarası 12 haneli değil; bu öğrenciler aktarılmadı.");

        return studentIds;
    }

    /// <summary>
    /// Öğrenci–ders kayıtları. Sürüm 2.0 paketleri kayıtları taşır; yalnızca bunlar eklenir. Kayıt bilgisi
    /// olmayan eski (1.0) pakette tek ders varsa öğrenciler o derse kaydedilir; birden çok ders varsa hangi
    /// öğrencinin hangi derste olduğu bilinmediği için kayıt yapılmaz.
    /// </summary>
    private async Task ImportEnrollmentsAsync(
        AdminExportPackageDto package,
        Dictionary<string, int> courseIds,
        Dictionary<string, int> studentIds,
        DataImportResult result)
    {
        var existing = (await _context.StudentCourses.Select(sc => new { sc.StudentId, sc.CourseId }).ToListAsync())
            .Select(x => (x.StudentId, x.CourseId))
            .ToHashSet();

        var pairs = new List<(int StudentId, int CourseId)>();
        if (package.Enrollments != null)
        {
            foreach (var e in package.Enrollments)
            {
                if (courseIds.TryGetValue(e.CourseCode?.Trim() ?? string.Empty, out var courseId)
                    && studentIds.TryGetValue(e.StudentNumber?.Trim() ?? string.Empty, out var studentId))
                {
                    pairs.Add((studentId, courseId));
                }
            }
        }
        else
        {
            var packageCourses = (package.Courses ?? new List<AdminExportCourseDto>())
                .Select(c => c.Code?.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var packageStudents = (package.Students ?? new List<AdminExportStudentDto>())
                .Select(s => s.Number?.Trim() ?? string.Empty)
                .Where(studentIds.ContainsKey)
                .Select(n => studentIds[n])
                .ToList();

            if (packageCourses.Count == 1 && courseIds.TryGetValue(packageCourses[0]!, out var onlyCourseId))
            {
                pairs.AddRange(packageStudents.Select(id => (id, onlyCourseId)));
            }
            else if (packageCourses.Count > 1 && packageStudents.Count > 0)
            {
                result.Warnings.Add("Paket eski sürüm (1.0) ve öğrenci–ders kayıtlarını içermiyor. Öğrenciler derslere " +
                                    "otomatik kaydedilmedi; derslere elle ekleyin ya da paketi yeni sürümle yeniden alın.");
            }
        }

        foreach (var pair in pairs)
        {
            if (existing.Add(pair))
            {
                _context.StudentCourses.Add(new StudentCourse { StudentId = pair.StudentId, CourseId = pair.CourseId });
                result.EnrollmentsAdded++;
            }
        }

        await _context.SaveChangesAsync();
    }

    // ─────────────────────────── Öğrenme çıktıları ───────────────────────────

    private async Task<Dictionary<(int CourseId, string Code), int>> ImportLearningOutcomesAsync(
        AdminExportPackageDto package, Dictionary<string, int> courseIds, DataImportResult result)
    {
        var outcomeIds = new Dictionary<(int, string), int>();
        foreach (var lo in await _context.LearningOutcomes.OrderBy(l => l.Id).ToListAsync())
            outcomeIds.TryAdd((lo.CourseId, lo.Code.Trim()), lo.Id);

        var added = new List<LearningOutcome>();
        foreach (var lo in package.LearningOutcomes ?? new List<AdminExportLearningOutcomeDto>())
        {
            var code = lo.Code?.Trim();
            if (string.IsNullOrEmpty(code) || !courseIds.TryGetValue(lo.CourseCode?.Trim() ?? string.Empty, out var courseId))
                continue;
            if (outcomeIds.ContainsKey((courseId, code)) || added.Any(a => a.CourseId == courseId && a.Code == code))
                continue;

            added.Add(new LearningOutcome
            {
                CourseId = courseId,
                Code = code,
                Name = string.IsNullOrWhiteSpace(lo.Name) ? code : lo.Name.Trim(),
                Description = lo.Description
            });
        }

        if (added.Count > 0)
        {
            _context.LearningOutcomes.AddRange(added);
            await _context.SaveChangesAsync();
            foreach (var lo in added)
                outcomeIds[(lo.CourseId, lo.Code)] = lo.Id;
            result.LearningOutcomesAdded += added.Count;
        }

        return outcomeIds;
    }

    private async Task ImportTopicsAsync(
        AdminExportPackageDto package,
        Dictionary<string, int> courseIds,
        Dictionary<(int CourseId, string Code), int> outcomeIds,
        DataImportResult result)
    {
        var topics = await _context.Topics.ToListAsync();
        var topicLinks = (await _context.TopicLearningOutcomes.Select(tl => new { tl.TopicId, tl.LearningOutcomeId }).ToListAsync())
            .Select(x => (x.TopicId, x.LearningOutcomeId))
            .ToHashSet();

        foreach (var t in package.Topics ?? new List<AdminExportTopicDto>())
        {
            if (string.IsNullOrWhiteSpace(t.Title) || !courseIds.TryGetValue(t.CourseCode?.Trim() ?? string.Empty, out var courseId))
                continue;

            var topic = topics.FirstOrDefault(et => et.CourseId == courseId && et.Title == t.Title);
            if (topic == null)
            {
                topic = new Topic
                {
                    CourseId = courseId,
                    WeekNumber = t.WeekNumber,
                    Title = t.Title,
                    Description = t.Description
                };
                _context.Topics.Add(topic);
                topics.Add(topic);
                result.TopicsAdded++;
                await _context.SaveChangesAsync();
            }

            foreach (var code in t.LearningOutcomeCodes ?? new List<string>())
            {
                if (outcomeIds.TryGetValue((courseId, code.Trim()), out var outcomeId) && topicLinks.Add((topic.Id, outcomeId)))
                    _context.TopicLearningOutcomes.Add(new TopicLearningOutcome { TopicId = topic.Id, LearningOutcomeId = outcomeId });
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task ImportProgramOutcomesAsync(
        AdminExportPackageDto package,
        Dictionary<string, int> programIds,
        Dictionary<string, int> courseIds,
        Dictionary<(int CourseId, string Code), int> outcomeIds,
        DataImportResult result)
    {
        var programOutcomes = await _context.ProgramOutcomes.ToListAsync();
        var mappings = (await _context.ProgramOutcomeMappings.Select(m => new { m.ProgramOutcomeId, m.LearningOutcomeId }).ToListAsync())
            .Select(x => (x.ProgramOutcomeId, x.LearningOutcomeId))
            .ToHashSet();

        foreach (var po in package.ProgramOutcomes ?? new List<AdminExportProgramOutcomeDto>())
        {
            var code = po.Code?.Trim();
            if (string.IsNullOrEmpty(code) || !programIds.TryGetValue(po.ProgramCode?.Trim() ?? string.Empty, out var programId))
                continue;

            var entity = programOutcomes.FirstOrDefault(p => p.ProgramId == programId && p.Code.Trim() == code);
            if (entity == null)
            {
                entity = new ProgramOutcome
                {
                    ProgramId = programId,
                    Code = code,
                    Description = string.IsNullOrWhiteSpace(po.Description) ? code : po.Description.Trim()
                };
                _context.ProgramOutcomes.Add(entity);
                programOutcomes.Add(entity);
                result.ProgramOutcomesAdded++;
                await _context.SaveChangesAsync();
            }

            foreach (var m in po.Mappings ?? new List<AdminExportOutcomeMappingDto>())
            {
                if (!courseIds.TryGetValue(m.CourseCode?.Trim() ?? string.Empty, out var courseId)
                    || !outcomeIds.TryGetValue((courseId, m.LearningOutcomeCode?.Trim() ?? string.Empty), out var outcomeId))
                    continue;

                // Var olan eşleşmenin katkı düzeyi değiştirilmez.
                if (mappings.Add((entity.Id, outcomeId)))
                {
                    _context.ProgramOutcomeMappings.Add(new ProgramOutcomeMapping
                    {
                        ProgramOutcomeId = entity.Id,
                        LearningOutcomeId = outcomeId,
                        ContributionLevel = Math.Clamp(m.ContributionLevel, 1, 5)
                    });
                    result.OutcomeMappingsAdded++;
                }
            }
        }

        await _context.SaveChangesAsync();
    }

    // ─────────────────────────── Soru bankası ───────────────────────────

    /// <returns>Paket içi soru kodu → veritabanındaki soru Id'si (yeni ya da var olan).</returns>
    private async Task<Dictionary<string, int>> ImportQuestionsAsync(
        AdminExportPackageDto package,
        Dictionary<string, int> courseIds,
        Dictionary<(int CourseId, string Code), int> outcomeIds,
        int defaultUserId,
        DataImportResult result)
    {
        var questionIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (package.Questions == null || package.Questions.Count == 0)
            return questionIds;

        var existing = await _context.Questions
            .Select(q => new { q.Id, q.CourseId, q.QuestionText })
            .ToListAsync();
        var byCourseAndText = new Dictionary<(int, string), int>();
        foreach (var q in existing)
            byCourseAndText.TryAdd((q.CourseId, q.QuestionText), q.Id);

        var allTopics = await _context.Topics.ToListAsync();
        var questionTopicLinks = (await _context.QuestionTopics.Select(x => new { x.QuestionId, x.TopicId }).ToListAsync())
            .Select(x => (x.QuestionId, x.TopicId)).ToHashSet();
        var questionOutcomeLinks = (await _context.QuestionLearningOutcomes.Select(x => new { x.QuestionId, x.LearningOutcomeId }).ToListAsync())
            .Select(x => (x.QuestionId, x.LearningOutcomeId)).ToHashSet();

        foreach (var q in package.Questions)
        {
            if (string.IsNullOrWhiteSpace(q.Content) || !courseIds.TryGetValue(q.CourseCode?.Trim() ?? string.Empty, out var courseId))
                continue;

            var qType = q.QuestionType == nameof(QuestionType.OpenEnded) ? QuestionType.OpenEnded : QuestionType.MultipleChoice;

            int questionId;
            if (byCourseAndText.TryGetValue((courseId, q.Content), out var existingId))
            {
                questionId = existingId;
            }
            else
            {
                var newQuestion = new Question
                {
                    CourseId = courseId,
                    QuestionText = q.Content,
                    Type = qType,
                    MaxPoints = q.ClassicMaxScore is decimal max && max > 0 ? max : 1.0m,
                    AnswerKey = q.ClassicAnswerKey,
                    IsActive = q.IsActive,
                    CreatedByUserId = defaultUserId,
                    CreatedAt = DateTime.UtcNow
                };

                if (q.Choices != null && q.Choices.Any() && qType == QuestionType.MultipleChoice)
                {
                    var dict = q.Choices.GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First());
                    if (dict.TryGetValue("A", out var a)) { newQuestion.OptionA = a.Content; if (a.IsCorrect) newQuestion.CorrectOption = OptionLetter.A; }
                    if (dict.TryGetValue("B", out var b)) { newQuestion.OptionB = b.Content; if (b.IsCorrect) newQuestion.CorrectOption = OptionLetter.B; }
                    if (dict.TryGetValue("C", out var c)) { newQuestion.OptionC = c.Content; if (c.IsCorrect) newQuestion.CorrectOption = OptionLetter.C; }
                    if (dict.TryGetValue("D", out var d)) { newQuestion.OptionD = d.Content; if (d.IsCorrect) newQuestion.CorrectOption = OptionLetter.D; }
                    if (dict.TryGetValue("E", out var e)) { newQuestion.OptionE = e.Content; if (e.IsCorrect) newQuestion.CorrectOption = OptionLetter.E; }
                }

                if (q.RubricCriteria != null && q.RubricCriteria.Any() && qType == QuestionType.OpenEnded)
                {
                    foreach (var r in q.RubricCriteria.OrderBy(r => r.OrderIndex))
                    {
                        newQuestion.RubricCriteria.Add(new QuestionRubricCriterion
                        {
                            Title = r.Name,
                            MaxPoints = r.MaxScore,
                            Order = r.OrderIndex,
                            Description = r.Description
                        });
                    }
                }

                _context.Questions.Add(newQuestion);
                await _context.SaveChangesAsync();
                questionId = newQuestion.Id;
                byCourseAndText[(courseId, q.Content)] = questionId;
                result.QuestionsAdded++;
            }

            if (!string.IsNullOrWhiteSpace(q.Code))
                questionIds.TryAdd(q.Code.Trim(), questionId);

            // Eksik konu ve ÖÇ bağları eklenir.
            var topicTitles = (q.TopicTitles ?? new List<string>()).ToList();
            if (topicTitles.Count == 0 && !string.IsNullOrWhiteSpace(q.TopicTitle))
                topicTitles.Add(q.TopicTitle);
            foreach (var title in topicTitles.Distinct())
            {
                var topic = allTopics.FirstOrDefault(t => t.CourseId == courseId && t.Title == title);
                if (topic != null && questionTopicLinks.Add((questionId, topic.Id)))
                    _context.QuestionTopics.Add(new QuestionTopic { QuestionId = questionId, TopicId = topic.Id });
            }

            foreach (var code in q.LearningOutcomeCodes ?? new List<string>())
            {
                if (outcomeIds.TryGetValue((courseId, code.Trim()), out var outcomeId) && questionOutcomeLinks.Add((questionId, outcomeId)))
                    _context.QuestionLearningOutcomes.Add(new QuestionLearningOutcome { QuestionId = questionId, LearningOutcomeId = outcomeId });
            }
        }

        await _context.SaveChangesAsync();
        return questionIds;
    }

    // ─────────────────────────── Sınavlar ve cevaplar ───────────────────────────

    private async Task ImportExamsAsync(
        AdminExportPackageDto package,
        Dictionary<string, int> courseIds,
        Dictionary<string, int> studentIds,
        Dictionary<string, int> questionIds,
        int defaultUserId,
        DataImportResult result)
    {
        if (package.Exams == null || package.Exams.Count == 0)
            return;

        var existingExams = await _context.Exams
            .Select(e => new { e.CourseId, e.Title, e.ExamDate })
            .ToListAsync();
        var criteriaByQuestion = (await _context.QuestionRubricCriteria.ToListAsync())
            .ToLookup(c => c.QuestionId);

        var unmatchedQuestions = 0;
        var unmatchedAnswers = 0;

        foreach (var e in package.Exams)
        {
            if (string.IsNullOrWhiteSpace(e.Title) || !courseIds.TryGetValue(e.CourseCode?.Trim() ?? string.Empty, out var courseId))
                continue;

            if (existingExams.Any(x => x.CourseId == courseId && x.Title == e.Title && x.ExamDate == e.ExamDate))
            {
                result.ExamsSkipped++;
                continue;
            }

            var exam = new Exam
            {
                CourseId = courseId,
                Title = e.Title,
                ExamDate = e.ExamDate,
                DurationMinutes = e.DurationMinutes,
                ExamType = Enum.TryParse<ExamType>(e.ExamType, true, out var examType) ? examType : ExamType.Midterm,
                BookletCount = Math.Max(1, e.BookletCount),
                ShuffleOptions = e.ShuffleOptions,
                CreatedByUserId = defaultUserId
            };
            _context.Exams.Add(exam);

            var examQuestionIds = new HashSet<int>();
            foreach (var eq in e.Questions ?? new List<AdminExportExamQuestionDto>())
            {
                if (!questionIds.TryGetValue(eq.QuestionCode?.Trim() ?? string.Empty, out var questionId))
                {
                    unmatchedQuestions++;
                    continue;
                }
                if (!examQuestionIds.Add(questionId))
                    continue;

                exam.ExamQuestions.Add(new ExamQuestion
                {
                    QuestionId = questionId,
                    OrderInExam = eq.Order,
                    OverrideMaxPoints = eq.OverrideMaxPoints,
                    IsCancelled = eq.IsCancelled,
                    CancellationReason = eq.CancellationReason
                });
            }

            var booklets = new Dictionary<string, ExamBooklet>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in e.Booklets ?? new List<AdminExportBookletDto>())
            {
                if (string.IsNullOrWhiteSpace(b.Code) || booklets.ContainsKey(b.Code.Trim()))
                    continue;

                var booklet = new ExamBooklet { BookletCode = b.Code.Trim() };
                foreach (var bq in b.Questions ?? new List<AdminExportBookletQuestionDto>())
                {
                    if (questionIds.TryGetValue(bq.QuestionCode?.Trim() ?? string.Empty, out var questionId)
                        && examQuestionIds.Contains(questionId))
                    {
                        booklet.BookletQuestions.Add(new ExamBookletQuestion
                        {
                            QuestionId = questionId,
                            OrderInBooklet = bq.Order,
                            OptionShuffleMap = bq.ShuffleMap
                        });
                    }
                }
                booklets[booklet.BookletCode] = booklet;
                exam.Booklets.Add(booklet);
            }

            var answerKeys = new HashSet<(int QuestionId, int StudentId)>();
            foreach (var a in e.Answers ?? new List<AdminExportAnswerDto>())
            {
                if (!studentIds.TryGetValue(a.StudentNumber?.Trim() ?? string.Empty, out var studentId)
                    || !questionIds.TryGetValue(a.QuestionCode?.Trim() ?? string.Empty, out var questionId)
                    || !examQuestionIds.Contains(questionId)
                    || !answerKeys.Add((questionId, studentId)))
                {
                    unmatchedAnswers++;
                    continue;
                }

                var answer = new StudentAnswer
                {
                    QuestionId = questionId,
                    StudentId = studentId,
                    Booklet = a.BookletCode != null && booklets.TryGetValue(a.BookletCode.Trim(), out var booklet) ? booklet : null,
                    SelectedOption = Enum.TryParse<OptionLetter>(a.SelectedOption, true, out var option) ? option : OptionLetter.Empty,
                    Score = a.Score,
                    IsCorrect = a.IsCorrect
                };

                var criteria = criteriaByQuestion[questionId].ToList();
                foreach (var cs in a.CriterionScores ?? new List<AdminExportCriterionScoreDto>())
                {
                    var criterion = criteria.FirstOrDefault(c => c.Order == cs.CriterionOrder && c.Title == cs.CriterionTitle)
                                    ?? criteria.FirstOrDefault(c => c.Title == cs.CriterionTitle)
                                    ?? criteria.FirstOrDefault(c => c.Order == cs.CriterionOrder);
                    if (criterion != null)
                    {
                        answer.CriterionScores.Add(new StudentAnswerCriterion
                        {
                            CriterionId = criterion.Id,
                            Score = cs.Score,
                            Comment = cs.Comment
                        });
                    }
                }

                exam.StudentAnswers.Add(answer);
                result.AnswersAdded++;
            }

            await _context.SaveChangesAsync();
            result.ExamsAdded++;
        }

        if (unmatchedQuestions > 0)
            result.Warnings.Add($"Sınavlardaki {unmatchedQuestions} soru pakette bulunamadığı için atlandı.");
        if (unmatchedAnswers > 0)
            result.Warnings.Add($"{unmatchedAnswers} cevap, öğrencisi ya da sorusu eşleşmediği için atlandı.");
    }
}
