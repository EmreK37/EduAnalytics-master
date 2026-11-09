using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.Core.Entities;
using EduAnalytics.Core.Enums;
using EduAnalytics.DataAccess.Context;
using Microsoft.EntityFrameworkCore;

namespace EduAnalytics.Business.Services.Implementations;

public class AnswerEntryService : IAnswerEntryService
{
    private readonly EduAnalyticsDbContext _context;
    private readonly IExamBookletService _bookletService;

    public AnswerEntryService(EduAnalyticsDbContext context, IExamBookletService bookletService)
    {
        _context = context;
        _bookletService = bookletService;
    }

    public async Task<AnswerEntryModel> LoadAsync(int examId)
    {
        var exam = await _context.Exams
            .Include(e => e.Course)
            .Include(e => e.ExamQuestions.OrderBy(eq => eq.OrderInExam))
                .ThenInclude(eq => eq.Question)
            .FirstOrDefaultAsync(e => e.Id == examId)
            ?? throw new InvalidOperationException($"Sınav bulunamadı: {examId}");

        var students = await _context.StudentCourses
            .Where(sc => sc.CourseId == exam.CourseId)
            .Select(sc => sc.Student)
            .OrderBy(s => s.StudentNumber)
            .ToListAsync();

        var booklets = await _context.ExamBooklets
            .Where(b => b.ExamId == examId)
            .OrderBy(b => b.BookletCode)
            .ToListAsync();

        var bookletMap = booklets.ToDictionary(b => b.Id);

        var model = new AnswerEntryModel
        {
            ExamId = exam.Id,
            ExamTitle = exam.Title,
            CourseName = exam.Course.Name,
            BookletCount = exam.BookletCount,
            AvailableBooklets = booklets.Select(b => new BookletInfo
            {
                BookletId = b.Id,
                BookletCode = b.BookletCode
            }).ToList(),
            Questions = exam.ExamQuestions
                .OrderBy(eq => eq.OrderInExam)
                .Select(eq => new AnswerEntryQuestion
                {
                    QuestionId = eq.QuestionId,
                    QuestionNumber = eq.OrderInExam,
                    Type = eq.Question.Type,
                    MaxPoints = eq.OverrideMaxPoints ?? eq.Question.MaxPoints,
                    CorrectOption = eq.Question.CorrectOption,
                    IsCancelled = eq.IsCancelled,
                    QuestionTextPreview = eq.Question.QuestionText.Length > 60
                        ? eq.Question.QuestionText[..60] + "…"
                        : eq.Question.QuestionText
                })
                .ToList()
        };

        var qIds = exam.ExamQuestions.Select(eq => eq.QuestionId).ToList();
        var answers = await _context.StudentAnswers
            .Include(sa => sa.CriterionScores)
            .Where(sa => sa.ExamId == examId && qIds.Contains(sa.QuestionId))
            .ToListAsync();

        var answerLookup = answers.ToLookup(a => a.StudentId);

        foreach (var s in students)
        {
            var firstAnswer = answerLookup[s.Id].FirstOrDefault();
            var row = new AnswerEntryStudent
            {
                StudentId = s.Id,
                StudentNumber = s.StudentNumber,
                FullName = s.FullName,
                BookletId = firstAnswer?.BookletId,
                BookletCode = firstAnswer?.BookletId.HasValue == true && bookletMap.TryGetValue(firstAnswer.BookletId.Value, out var b)
                    ? b.BookletCode
                    : null
            };

            foreach (var a in answerLookup[s.Id])
            {
                row.Answers[a.QuestionId] = new AnswerEntryCell
                {
                    SelectedOption = a.SelectedOption,
                    Score = a.Score,
                    HasRubricBreakdown = a.CriterionScores.Count > 0
                };
            }

            model.Students.Add(row);
        }

        return model;
    }

    public async Task<AnswerSaveResult> SaveAsync(
        int examId,
        List<StudentAnswerUpdate> updates,
        IReadOnlyCollection<int>? attendedStudentIds = null)
    {
        var result = new AnswerSaveResult();

        var examQuestions = await _context.ExamQuestions
            .Where(eq => eq.ExamId == examId)
            .Include(eq => eq.Question)
            .ToDictionaryAsync(eq => eq.QuestionId);

        var qIds = examQuestions.Keys.ToList();
        var studentIds = updates.Select(u => u.StudentId).Distinct().ToList();

        var existing = await _context.StudentAnswers
            .Include(sa => sa.CriterionScores)
            .Where(sa => sa.ExamId == examId
                      && qIds.Contains(sa.QuestionId)
                      && studentIds.Contains(sa.StudentId))
            .ToListAsync();

        var existingLookup = existing.ToDictionary(sa => (sa.QuestionId, sa.StudentId));

        // Kitapçık-bazlı şık permütasyonu için lookup
        var bookletQuestions = await _context.ExamBookletQuestions
            .Include(bq => bq.Booklet)
            .Where(bq => bq.Booklet.ExamId == examId)
            .ToListAsync();
        var shuffleMapLookup = bookletQuestions
            .ToDictionary(bq => (bq.BookletId, bq.QuestionId), bq => bq.OptionShuffleMap);

        var defaultBookletId = await _context.ExamBooklets
            .Where(b => b.ExamId == examId && b.BookletCode == "A")
            .Select(b => (int?)b.Id)
            .FirstOrDefaultAsync();

        var attended = attendedStudentIds != null ? new HashSet<int>(attendedStudentIds) : new HashSet<int>();

        foreach (var studentUpdates in updates
                     .Where(u => examQuestions.ContainsKey(u.QuestionId))
                     .GroupBy(u => u.StudentId))
        {
            var studentId = studentUpdates.Key;
            var updatedQuestionIds = studentUpdates.Select(u => u.QuestionId).ToHashSet();

            // Öğrenci sınava girdi mi? Açıkça bildirildiyse (optik okumada kâğıdı okunan öğrenci),
            // bu kayıtta dolu bir değeri varsa ya da kayıt dışında kalan sorularda daha önce girilmiş
            // bir değeri varsa evet. Satırı tamamen boş öğrenci sınava girmemiş sayılır.
            var tookExam = attended.Contains(studentId)
                || studentUpdates.Any(u => u.SelectedOption != OptionLetter.Empty || u.Score.HasValue)
                || existing.Any(sa => sa.StudentId == studentId
                                   && !updatedQuestionIds.Contains(sa.QuestionId)
                                   && HasValue(sa));

            if (!tookExam)
            {
                // Satırı boşaltılan öğrencinin eski (artık boş) kayıtları silinir; analize girmez.
                var stale = studentUpdates
                    .Select(u => existingLookup.GetValueOrDefault((u.QuestionId, studentId)))
                    .OfType<StudentAnswer>()
                    .ToList();
                if (stale.Count > 0)
                {
                    _context.StudentAnswers.RemoveRange(stale);
                    result.ClearedStudents++;
                }
                else
                {
                    result.AbsentStudents++;
                }
                continue;
            }

            result.StudentsSaved++;

            foreach (var u in studentUpdates)
            {
                var eq = examQuestions[u.QuestionId];
                var max = eq.OverrideMaxPoints ?? eq.Question.MaxPoints;
                var bookletId = u.BookletId ?? defaultBookletId;
                existingLookup.TryGetValue((u.QuestionId, studentId), out var existingAnswer);

                if (eq.Question.Type == QuestionType.OpenEnded)
                {
                    if (u.Score is not decimal score)
                    {
                        // Puan girilmemiş (ya da silinmiş): soru bu öğrenci için değerlendirilmemiş sayılır.
                        if (existingAnswer != null)
                            _context.StudentAnswers.Remove(existingAnswer);
                        continue;
                    }

                    var isPassed = score >= max / 2m;
                    if (existingAnswer != null)
                    {
                        // Puan elle ya da optik okumayla değiştiyse eski kriter dökümü artık bu puanı
                        // açıklamaz; tutarsız kalmaması için silinir.
                        if (existingAnswer.CriterionScores.Count > 0
                            && existingAnswer.CriterionScores.Sum(c => c.Score) != score)
                        {
                            _context.StudentAnswerCriteria.RemoveRange(existingAnswer.CriterionScores);
                            result.RubricBreakdownsCleared++;
                        }

                        existingAnswer.SelectedOption = OptionLetter.Empty;
                        existingAnswer.Score = score;
                        existingAnswer.IsCorrect = isPassed;
                        existingAnswer.BookletId = bookletId;
                    }
                    else
                    {
                        _context.StudentAnswers.Add(new StudentAnswer
                        {
                            ExamId = examId,
                            QuestionId = u.QuestionId,
                            StudentId = studentId,
                            BookletId = bookletId,
                            SelectedOption = OptionLetter.Empty,
                            Score = score,
                            IsCorrect = isPassed
                        });
                    }
                    continue;
                }

                // Test sorusu. Kitapçıkta şıklar karıştırıldıysa işaretlenen şık orijinal şıkka çevrilir.
                var decodedOption = u.SelectedOption;
                if (bookletId.HasValue
                    && shuffleMapLookup.TryGetValue((bookletId.Value, u.QuestionId), out var shuffleMap)
                    && !string.IsNullOrEmpty(shuffleMap))
                {
                    decodedOption = _bookletService.DecodeStudentChoice(shuffleMap, u.SelectedOption);
                }

                // Sınava giren öğrencinin boş bıraktığı soru da kaydedilir: madde analizinde "boş",
                // puanda 0 olarak sayılır.
                var isCorrect = decodedOption != OptionLetter.Empty && decodedOption == eq.Question.CorrectOption;
                if (decodedOption == OptionLetter.Empty)
                    result.BlankAnswers++;

                if (existingAnswer != null)
                {
                    existingAnswer.SelectedOption = decodedOption;
                    existingAnswer.Score = null;
                    existingAnswer.IsCorrect = isCorrect;
                    existingAnswer.BookletId = bookletId;
                }
                else
                {
                    _context.StudentAnswers.Add(new StudentAnswer
                    {
                        ExamId = examId,
                        QuestionId = u.QuestionId,
                        StudentId = studentId,
                        BookletId = bookletId,
                        SelectedOption = decodedOption,
                        Score = null,
                        IsCorrect = isCorrect
                    });
                }
            }
        }

        await _context.SaveChangesAsync();
        return result;
    }

    private static bool HasValue(StudentAnswer answer) =>
        answer.SelectedOption != OptionLetter.Empty || answer.Score.HasValue;
}
