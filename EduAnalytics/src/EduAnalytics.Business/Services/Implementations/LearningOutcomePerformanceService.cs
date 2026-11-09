using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Helpers;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.DataAccess.Context;
using Microsoft.EntityFrameworkCore;

namespace EduAnalytics.Business.Services.Implementations;

public class LearningOutcomePerformanceService : ILearningOutcomePerformanceService
{
    private readonly EduAnalyticsDbContext _context;

    public LearningOutcomePerformanceService(EduAnalyticsDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Sınavdaki her ÖÇ'nün sınıf başarısı: ÖÇ'yü ölçen sorulardan kazanılan puan / alınabilecek puan.
    /// Test ve klasik sorular birlikte sayılır; iptal edilen sorular ve puanı girilmemiş klasik cevaplar
    /// hesaba girmez. Öğrenci raporundaki ÖÇ hesabı da aynı kuralı kullanır (<see cref="ScoreTally"/>).
    /// </summary>
    public async Task<List<LearningOutcomePerformanceDto>> AnalyzeExamAsync(int examId)
    {
        var examQuestions = await _context.ExamQuestions
            .Where(eq => eq.ExamId == examId && !eq.IsCancelled)
            .Include(eq => eq.Question)
                .ThenInclude(q => q.QuestionLearningOutcomes)
                    .ThenInclude(qlo => qlo.LearningOutcome)
            .ToListAsync();

        var qIds = examQuestions.Select(eq => eq.QuestionId).ToList();
        var answers = await _context.StudentAnswers
            .Where(sa => sa.ExamId == examId && qIds.Contains(sa.QuestionId))
            .ToListAsync();

        var answerLookup = answers.ToLookup(a => a.QuestionId);

        var allOutcomes = examQuestions
            .SelectMany(eq => eq.Question.QuestionLearningOutcomes.Select(qlo => qlo.LearningOutcome))
            .DistinctBy(lo => lo.Id)
            .ToList();

        var result = new List<LearningOutcomePerformanceDto>();

        foreach (var outcome in allOutcomes)
        {
            var relatedEqs = examQuestions
                .Where(eq => eq.Question.QuestionLearningOutcomes.Any(qlo => qlo.LearningOutcomeId == outcome.Id))
                .ToList();

            var tally = new ScoreTally();
            foreach (var eq in relatedEqs)
            {
                foreach (var a in answerLookup[eq.QuestionId])
                    tally.Add(eq, a);
            }

            if (tally.Rate is not double successRate)
                continue;

            result.Add(new LearningOutcomePerformanceDto
            {
                LearningOutcomeId = outcome.Id,
                OutcomeName = $"{outcome.Code} — {outcome.Name}",
                Description = outcome.Description,
                RelatedQuestionCount = relatedEqs.Count,
                TotalAnswers = tally.Answers,
                CorrectAnswers = tally.Correct,
                EarnedPoints = tally.Earned,
                PossiblePoints = tally.Possible,
                SuccessRate = successRate
            });
        }

        return result.OrderByDescending(r => r.SuccessRate).ToList();
    }
}
