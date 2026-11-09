using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Helpers;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.Core.Entities;
using EduAnalytics.Core.Enums;
using EduAnalytics.DataAccess.Context;
using Microsoft.EntityFrameworkCore;

namespace EduAnalytics.Business.Services.Implementations;

public class RubricService : IRubricService
{
    private readonly EduAnalyticsDbContext _context;

    public RubricService(EduAnalyticsDbContext context)
    {
        _context = context;
    }

    public async Task<List<RubricCriterionDto>> GetCriteriaAsync(int questionId)
    {
        return await _context.QuestionRubricCriteria
            .Where(c => c.QuestionId == questionId)
            .OrderBy(c => c.Order)
            .Select(c => new RubricCriterionDto
            {
                Id = c.Id,
                QuestionId = c.QuestionId,
                Title = c.Title,
                MaxPoints = c.MaxPoints,
                Order = c.Order,
                Description = c.Description
            })
            .ToListAsync();
    }

    /// <summary>
    /// Sorunun rubrik ölçütlerini verilen listeyle eşitler. Id'si olan ölçüt güncellenir, Id'si olmayan
    /// eklenir, listede olmayan silinir. Kurallar:
    /// <list type="bullet">
    /// <item>Yalnızca klasik sorular için; her ölçütün başlığı olmalı ve puanı 0'dan büyük olmalı.</item>
    /// <item>Ölçüt puanlarının toplamı sorunun puanına eşit olmalı (liste boşsa rubrik kaldırılır).</item>
    /// <item>Öğrencilerin puanlandığı bir ölçüt silinemez; puanı verilmiş en yüksek puanın altına düşürülemez.</item>
    /// </list>
    /// </summary>
    public async Task SetCriteriaAsync(int questionId, List<RubricCriterionCreateModel> criteria)
    {
        var question = await _context.Questions.FindAsync(questionId)
            ?? throw new InvalidOperationException($"Soru bulunamadı: {questionId}");
        if (question.Type != QuestionType.OpenEnded)
            throw new InvalidOperationException("Rubrik ölçütleri yalnızca klasik sorular için tanımlanabilir.");

        var cleaned = criteria
            .Select(c => new
            {
                c.Id,
                Title = c.Title?.Trim() ?? string.Empty,
                c.MaxPoints,
                Description = string.IsNullOrWhiteSpace(c.Description) ? null : c.Description.Trim()
            })
            .ToList();

        if (cleaned.Any(c => c.Title.Length == 0))
            throw new ArgumentException("Her ölçütün bir başlığı olmalı.");
        if (cleaned.Any(c => c.MaxPoints <= 0))
            throw new ArgumentException("Ölçüt puanları 0'dan büyük olmalı.");
        var total = cleaned.Sum(c => c.MaxPoints);
        if (cleaned.Count > 0 && total != question.MaxPoints)
            throw new ArgumentException(
                $"Ölçüt puanlarının toplamı ({total:0.##}) sorunun puanına ({question.MaxPoints:0.##}) eşit olmalı.");

        var existing = await _context.QuestionRubricCriteria
            .Where(c => c.QuestionId == questionId)
            .ToListAsync();
        var existingIds = existing.Select(e => e.Id).ToList();

        // Ölçüt başına öğrencilere verilmiş en yüksek puan (puanlanmış ölçütleri korumak için).
        var highestGiven = await _context.StudentAnswerCriteria
            .Where(s => existingIds.Contains(s.CriterionId))
            .GroupBy(s => s.CriterionId)
            .Select(g => new { CriterionId = g.Key, Highest = g.Max(s => s.Score) })
            .ToDictionaryAsync(x => x.CriterionId, x => x.Highest);

        var keptIds = cleaned.Where(c => c.Id.HasValue).Select(c => c.Id!.Value).ToHashSet();
        var removed = existing.Where(e => !keptIds.Contains(e.Id)).ToList();
        var removedScored = removed.FirstOrDefault(r => highestGiven.ContainsKey(r.Id));
        if (removedScored != null)
            throw new InvalidOperationException(
                $"\"{removedScored.Title}\" ölçütüyle puanlanmış öğrenciler var; bu ölçüt silinemez. " +
                "Başlığını, açıklamasını ya da puanını değiştirebilirsiniz.");

        _context.QuestionRubricCriteria.RemoveRange(removed);

        var order = 1;
        foreach (var c in cleaned)
        {
            var entity = c.Id.HasValue ? existing.FirstOrDefault(e => e.Id == c.Id.Value) : null;
            if (entity == null)
            {
                entity = new QuestionRubricCriterion { QuestionId = questionId };
                _context.QuestionRubricCriteria.Add(entity);
            }
            else if (highestGiven.TryGetValue(entity.Id, out var highest) && c.MaxPoints < highest)
            {
                throw new InvalidOperationException(
                    $"\"{c.Title}\" ölçütünde {highest:0.##} puan verilmiş öğrenciler var; ölçüt puanı bunun altına düşürülemez.");
            }

            entity.Title = c.Title;
            entity.MaxPoints = c.MaxPoints;
            entity.Description = c.Description;
            entity.Order = order++;
        }

        await _context.SaveChangesAsync();
    }

    public async Task<StudentRubricGradeDto> GetStudentGradeAsync(int examId, int questionId, int studentId)
    {
        var criteria = await _context.QuestionRubricCriteria
            .Where(c => c.QuestionId == questionId)
            .OrderBy(c => c.Order)
            .ToListAsync();

        var studentAnswer = await _context.StudentAnswers
            .Include(sa => sa.CriterionScores)
            .FirstOrDefaultAsync(sa => sa.ExamId == examId
                                    && sa.QuestionId == questionId
                                    && sa.StudentId == studentId);

        var examQuestion = await _context.ExamQuestions
            .Include(eq => eq.Question)
            .FirstOrDefaultAsync(eq => eq.ExamId == examId && eq.QuestionId == questionId);

        var dto = new StudentRubricGradeDto
        {
            StudentAnswerId = studentAnswer?.Id ?? 0,
            StudentId = studentId,
            QuestionId = questionId,
            ExamMaxPoints = examQuestion != null ? ScoringRules.MaxPoints(examQuestion) : criteria.Sum(c => c.MaxPoints),
            CurrentScore = studentAnswer?.Score,
            HasBreakdown = studentAnswer?.CriterionScores.Count > 0
        };

        foreach (var c in criteria)
        {
            var existingScore = studentAnswer?.CriterionScores.FirstOrDefault(s => s.CriterionId == c.Id);
            dto.CriterionScores.Add(new CriterionScoreDto
            {
                CriterionId = c.Id,
                CriterionTitle = c.Title,
                MaxPoints = c.MaxPoints,
                Score = existingScore?.Score ?? 0m,
                Comment = existingScore?.Comment
            });
        }

        return dto;
    }

    public async Task SaveStudentGradeAsync(int examId, int questionId, int studentId, List<CriterionScoreUpdate> updates)
    {
        var examQuestion = await _context.ExamQuestions
            .Include(eq => eq.Question)
            .FirstOrDefaultAsync(eq => eq.ExamId == examId && eq.QuestionId == questionId)
            ?? throw new InvalidOperationException("Soru bu sınavda bulunamadı.");

        var criteria = await _context.QuestionRubricCriteria
            .Where(c => c.QuestionId == questionId)
            .ToDictionaryAsync(c => c.Id);
        if (criteria.Count == 0)
            throw new InvalidOperationException("Bu soru için rubrik ölçütü tanımlanmamış.");

        // Toplam puan sınavdaki soru puanı ölçeğinde olmalı; değilse kayıt yanlış ölçekte olur.
        var examMax = ScoringRules.MaxPoints(examQuestion);
        var criteriaTotal = criteria.Values.Sum(c => c.MaxPoints);
        if (criteriaTotal != examMax)
            throw new InvalidOperationException(
                $"Bu sınavda sorunun puanı {examMax:0.##}, rubrik ölçütlerinin toplamı {criteriaTotal:0.##}. " +
                "Rubrikle puanlamak için soru puanını ya da ölçüt puanlarını eşitleyin.");

        var valid = updates.Where(u => criteria.ContainsKey(u.CriterionId)).ToList();
        foreach (var u in valid)
        {
            var c = criteria[u.CriterionId];
            if (u.Score < 0 || u.Score > c.MaxPoints)
                throw new ArgumentException($"\"{c.Title}\" puanı 0 ile {c.MaxPoints:0.##} arasında olmalı.");
        }

        var studentAnswer = await _context.StudentAnswers
            .Include(sa => sa.CriterionScores)
            .FirstOrDefaultAsync(sa => sa.ExamId == examId
                                    && sa.QuestionId == questionId
                                    && sa.StudentId == studentId);

        // Toplam puan ve doğruluk (sınava özgü tam puanın en az yarısı)
        var totalScore = valid.Sum(u => u.Score);

        if (studentAnswer == null)
        {
            studentAnswer = new StudentAnswer
            {
                ExamId = examId,
                QuestionId = questionId,
                StudentId = studentId,
                SelectedOption = OptionLetter.Empty,
                Score = totalScore,
                IsCorrect = totalScore >= examMax / 2m
            };
            _context.StudentAnswers.Add(studentAnswer);
            await _context.SaveChangesAsync();
        }
        else
        {
            studentAnswer.Score = totalScore;
            studentAnswer.IsCorrect = totalScore >= examMax / 2m;
        }

        // Eski kriter puanlarını sil
        _context.StudentAnswerCriteria.RemoveRange(studentAnswer.CriterionScores);
        await _context.SaveChangesAsync();

        // Yeni kriter puanlarını ekle
        foreach (var u in valid)
        {
            _context.StudentAnswerCriteria.Add(new StudentAnswerCriterion
            {
                StudentAnswerId = studentAnswer.Id,
                CriterionId = u.CriterionId,
                Score = u.Score,
                Comment = u.Comment
            });
        }

        await _context.SaveChangesAsync();
    }
}
