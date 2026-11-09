using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Helpers;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.Core.Enums;
using EduAnalytics.DataAccess.Context;
using Microsoft.EntityFrameworkCore;

namespace EduAnalytics.Business.Services.Implementations;

public class StudentPerformanceService : IStudentPerformanceService
{
    private readonly EduAnalyticsDbContext _context;

    public StudentPerformanceService(EduAnalyticsDbContext context)
    {
        _context = context;
    }

    public async Task<List<StudentPerformanceDto>> GetExamRankingAsync(int examId)
    {
        var examQuestions = await _context.ExamQuestions
            .Where(eq => eq.ExamId == examId && !eq.IsCancelled)
            .Include(eq => eq.Question)
                .ThenInclude(q => q.QuestionLearningOutcomes)
                    .ThenInclude(qlo => qlo.LearningOutcome)
            .ToListAsync();

        if (examQuestions.Count == 0) return new List<StudentPerformanceDto>();

        var qIds = examQuestions.Select(eq => eq.QuestionId).ToList();
        var allAnswers = await _context.StudentAnswers
            .Where(sa => sa.ExamId == examId && qIds.Contains(sa.QuestionId))
            .Include(sa => sa.Student)
            .ToListAsync();

        var totalQ = examQuestions.Count;
        var maxPossible = examQuestions.Sum(eq => eq.OverrideMaxPoints ?? eq.Question.MaxPoints);

        var byStudent = allAnswers.GroupBy(a => a.StudentId).ToList();
        var results = new List<StudentPerformanceDto>();

        var examQuestionMap = examQuestions.ToDictionary(eq => eq.QuestionId);

        foreach (var sg in byStudent)
        {
            var studentId = sg.Key;
            var student = sg.First().Student;
            var studentAnswers = sg.ToList();

            var correct = studentAnswers.Count(a => a.IsCorrect);
            var empty = studentAnswers.Count(a => a.SelectedOption == OptionLetter.Empty && a.Score == null);
            var wrong = studentAnswers.Count - correct - empty;

            decimal totalScore = studentAnswers.Sum(a =>
            {
                if (!examQuestionMap.TryGetValue(a.QuestionId, out var eq))
                    return 0m;
                return ExamAnalysisService.ComputeScore(eq.Question, a, eq.OverrideMaxPoints);
            });

            var weakLearningOutcomes = new List<string>();

            var outcomeGroups = examQuestions
                .SelectMany(eq => eq.Question.QuestionLearningOutcomes.Select(qlo => new { qlo.LearningOutcome, ExamQuestion = eq }))
                .GroupBy(x => x.LearningOutcome.Id);

            // Sınav analizindeki sınıf ÖÇ hesabıyla aynı kural ve aynı eşik (ScoringRules).
            foreach (var outcomeGroup in outcomeGroups)
            {
                var outcome = outcomeGroup.First().LearningOutcome;
                var outcomeQuestionIds = outcomeGroup.Select(x => x.ExamQuestion.QuestionId).ToHashSet();

                var tally = new ScoreTally();
                foreach (var a in studentAnswers.Where(a => outcomeQuestionIds.Contains(a.QuestionId)))
                {
                    if (examQuestionMap.TryGetValue(a.QuestionId, out var eq))
                        tally.Add(eq, a);
                }

                if (tally.Rate is double rate && rate < ScoringRules.WeakSuccessThreshold)
                    weakLearningOutcomes.Add($"{outcome.Code} - {outcome.Name}");
            }

            results.Add(new StudentPerformanceDto
            {
                StudentId = studentId,
                StudentNumber = student.StudentNumber,
                FullName = student.FullName,
                ClassName = student.ClassName,
                TotalQuestions = totalQ,
                CorrectAnswers = correct,
                WrongAnswers = wrong,
                EmptyAnswers = empty,
                TotalScore = Math.Round(totalScore, 2),
                MaxPossibleScore = maxPossible,
                SuccessRate = maxPossible > 0 ? Math.Round((double)(totalScore / maxPossible) * 100, 1) : 0,
                WeakLearningOutcomes = weakLearningOutcomes
            });
        }

        var ranked = results.OrderByDescending(r => r.TotalScore).ToList();
        for (int i = 0; i < ranked.Count; i++)
            ranked[i].ClassRank = i + 1;

        return ranked;
    }

    public async Task<StudentPerformanceDto?> GetStudentReportAsync(int examId, int studentId)
    {
        var all = await GetExamRankingAsync(examId);
        return all.FirstOrDefault(s => s.StudentId == studentId);
    }
}
