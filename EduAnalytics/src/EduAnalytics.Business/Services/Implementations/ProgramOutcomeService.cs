using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Helpers;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.Core.Entities;
using EduAnalytics.DataAccess.Context;
using Microsoft.EntityFrameworkCore;

namespace EduAnalytics.Business.Services.Implementations;

public class ProgramOutcomeService : IProgramOutcomeService
{
    private readonly EduAnalyticsDbContext _context;

    public ProgramOutcomeService(EduAnalyticsDbContext context)
    {
        _context = context;
    }

    public async Task<List<Program>> GetProgramsAsync()
    {
        return await _context.Programs.OrderBy(p => p.Name).ToListAsync();
    }

    public async Task<List<ProgramOutcomeDto>> GetByProgramAsync(int programId)
    {
        return await _context.ProgramOutcomes
            .Include(po => po.Program)
            .Include(po => po.Mappings)
            .Where(po => po.ProgramId == programId)
            .Select(po => new ProgramOutcomeDto
            {
                Id = po.Id,
                ProgramId = po.ProgramId,
                ProgramName = po.Program.Name,
                Code = po.Code,
                Description = po.Description,
                LearningOutcomeCount = po.Mappings.Count
            })
            .OrderBy(po => po.Code)
            .ToListAsync();
    }

    public async Task<int> CreateAsync(ProgramOutcomeCreateModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Code))
            throw new ArgumentException("PÇ kodu zorunludur.", nameof(model));
        if (string.IsNullOrWhiteSpace(model.Description))
            throw new ArgumentException("PÇ açıklaması zorunludur.", nameof(model));

        var po = new ProgramOutcome
        {
            ProgramId = model.ProgramId,
            Code = model.Code.Trim(),
            Description = model.Description.Trim()
        };
        _context.ProgramOutcomes.Add(po);
        await _context.SaveChangesAsync();
        return po.Id;
    }

    public async Task UpdateAsync(int programOutcomeId, ProgramOutcomeCreateModel model)
    {
        var po = await _context.ProgramOutcomes.FindAsync(programOutcomeId)
            ?? throw new InvalidOperationException($"PÇ bulunamadı: {programOutcomeId}");

        po.Code = model.Code.Trim();
        po.Description = model.Description.Trim();
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int programOutcomeId)
    {
        var po = await _context.ProgramOutcomes
            .Include(p => p.Mappings)
            .FirstOrDefaultAsync(p => p.Id == programOutcomeId)
            ?? throw new InvalidOperationException($"PÇ bulunamadı: {programOutcomeId}");

        _context.ProgramOutcomeMappings.RemoveRange(po.Mappings);
        _context.ProgramOutcomes.Remove(po);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// PÇ başarı raporu.
    /// <list type="bullet">
    /// <item>ÖÇ başarısı = ÖÇ'yü ölçen sorulardan, iptal edilmemiş bütün sınavlarda kazanılan puan /
    /// alınabilecek puan. Test ve klasik sorular birlikte sayılır (sınav analizindeki ÖÇ hesabıyla aynı).</item>
    /// <item>PÇ başarısı = bağlı ÖÇ başarılarının katkı düzeyiyle (1–5) ağırlıklı ortalaması:
    /// Σ(ÖÇ başarısı × katkı) / Σ katkı. Henüz verisi olmayan ÖÇ'ler ortalamaya girmez.</item>
    /// </list>
    /// </summary>
    public async Task<List<ProgramOutcomeReportDto>> GenerateReportAsync(int programId)
    {
        var programOutcomes = await _context.ProgramOutcomes
            .AsNoTracking()
            .Include(po => po.Mappings)
                .ThenInclude(m => m.LearningOutcome)
                    .ThenInclude(lo => lo.Course)
            .Where(po => po.ProgramId == programId)
            .ToListAsync();

        var learningOutcomeIds = programOutcomes
            .SelectMany(po => po.Mappings)
            .Select(m => m.LearningOutcomeId)
            .Distinct()
            .ToList();
        var outcomeStats = await ComputeOutcomeStatsAsync(learningOutcomeIds);

        var reports = new List<ProgramOutcomeReportDto>();

        foreach (var po in programOutcomes)
        {
            var report = new ProgramOutcomeReportDto
            {
                ProgramOutcomeId = po.Id,
                ProgramOutcomeCode = po.Code,
                ProgramOutcomeDescription = po.Description
            };

            double weightedSum = 0;
            int weightTotal = 0;

            foreach (var mapping in po.Mappings
                         .OrderBy(m => m.LearningOutcome.Course.Name)
                         .ThenBy(m => m.LearningOutcome.Code))
            {
                var lo = mapping.LearningOutcome;
                var stats = outcomeStats.GetValueOrDefault(lo.Id) ?? new OutcomeStats();
                var weight = Math.Clamp(mapping.ContributionLevel, 1, 5);
                var rate = stats.Tally.Rate;

                report.LinkedLearningOutcomes.Add(new LinkedLearningOutcomeDto
                {
                    LearningOutcomeId = lo.Id,
                    Code = lo.Code,
                    Name = lo.Name,
                    CourseName = lo.Course.Name,
                    ContributionLevel = weight,
                    QuestionCount = stats.QuestionCount,
                    AnswerCount = stats.Tally.Answers,
                    HasData = rate.HasValue,
                    SuccessRate = Math.Round(rate ?? 0, 1)
                });

                report.TotalQuestionCount += stats.QuestionCount;
                report.TotalAnswerCount += stats.Tally.Answers;

                if (rate is double r)
                {
                    weightedSum += r * weight;
                    weightTotal += weight;
                }
            }

            report.HasData = weightTotal > 0;
            report.OverallSuccessRate = weightTotal > 0 ? Math.Round(weightedSum / weightTotal, 1) : 0;

            reports.Add(report);
        }

        return reports.OrderBy(r => r.ProgramOutcomeCode).ToList();
    }

    /// <summary>
    /// Her ÖÇ için: sınavlarda kullanılmış (iptal edilmemiş) soru sayısı ve bütün sınavlardaki puan toplamı.
    /// Pasif hâle getirilmiş sorular da sayılır; geçmiş sınavlarda gerçekten sorulmuşlardır.
    /// </summary>
    private async Task<Dictionary<int, OutcomeStats>> ComputeOutcomeStatsAsync(List<int> learningOutcomeIds)
    {
        var links = await _context.QuestionLearningOutcomes
            .AsNoTracking()
            .Where(ql => learningOutcomeIds.Contains(ql.LearningOutcomeId))
            .Select(ql => new { ql.LearningOutcomeId, ql.QuestionId })
            .ToListAsync();

        var questionIds = links.Select(l => l.QuestionId).Distinct().ToList();

        var examQuestions = await _context.ExamQuestions
            .AsNoTracking()
            .Include(eq => eq.Question)
            .Where(eq => questionIds.Contains(eq.QuestionId) && !eq.IsCancelled)
            .ToListAsync();

        var examQuestionByKey = new Dictionary<(int ExamId, int QuestionId), ExamQuestion>();
        foreach (var eq in examQuestions)
            examQuestionByKey.TryAdd((eq.ExamId, eq.QuestionId), eq);
        var usedQuestionIds = examQuestions.Select(eq => eq.QuestionId).ToHashSet();

        var answers = await _context.StudentAnswers
            .AsNoTracking()
            .Where(sa => questionIds.Contains(sa.QuestionId))
            .ToListAsync();
        var answersByQuestion = answers.ToLookup(a => a.QuestionId);

        var result = new Dictionary<int, OutcomeStats>();
        foreach (var group in links.GroupBy(l => l.LearningOutcomeId))
        {
            var stats = new OutcomeStats();
            foreach (var questionId in group.Select(l => l.QuestionId).Distinct())
            {
                if (usedQuestionIds.Contains(questionId))
                    stats.QuestionCount++;

                foreach (var a in answersByQuestion[questionId])
                {
                    // Sorunun iptal edildiği sınavdaki cevaplar sayılmaz.
                    if (examQuestionByKey.TryGetValue((a.ExamId, a.QuestionId), out var eq))
                        stats.Tally.Add(eq, a);
                }
            }
            result[group.Key] = stats;
        }

        return result;
    }

    private sealed class OutcomeStats
    {
        public int QuestionCount { get; set; }
        public ScoreTally Tally { get; } = new();
    }

    public async Task LinkToLearningOutcomeAsync(int programOutcomeId, int learningOutcomeId, int contributionLevel)
    {
        var existing = await _context.ProgramOutcomeMappings
            .FirstOrDefaultAsync(m => m.ProgramOutcomeId == programOutcomeId
                                   && m.LearningOutcomeId == learningOutcomeId);

        if (existing != null)
        {
            existing.ContributionLevel = Math.Clamp(contributionLevel, 1, 5);
        }
        else
        {
            _context.ProgramOutcomeMappings.Add(new ProgramOutcomeMapping
            {
                ProgramOutcomeId = programOutcomeId,
                LearningOutcomeId = learningOutcomeId,
                ContributionLevel = Math.Clamp(contributionLevel, 1, 5)
            });
        }
        await _context.SaveChangesAsync();
    }

    public async Task UnlinkFromLearningOutcomeAsync(int programOutcomeId, int learningOutcomeId)
    {
        var link = await _context.ProgramOutcomeMappings
            .FirstOrDefaultAsync(m => m.ProgramOutcomeId == programOutcomeId
                                   && m.LearningOutcomeId == learningOutcomeId);

        if (link != null)
        {
            _context.ProgramOutcomeMappings.Remove(link);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<LearningOutcomeDto>> GetAllLearningOutcomesInProgramAsync(int programId)
    {
        return await _context.LearningOutcomes
            .Include(lo => lo.Course)
            .Where(lo => lo.Course.ProgramId == programId)
            .Select(lo => new LearningOutcomeDto
            {
                Id = lo.Id,
                CourseId = lo.CourseId,
                Code = lo.Code,
                Name = lo.Course.Name + " — " + lo.Code + " " + lo.Name,
                Description = lo.Description,
                TopicCount = lo.TopicLearningOutcomes.Count,
                QuestionCount = lo.QuestionLearningOutcomes.Count,
                ProgramOutcomeCodes = lo.ProgramOutcomeMappings
                    .Select(m => m.ProgramOutcome.Code)
                    .ToList()
            })
            .OrderBy(lo => lo.Name)
            .ToListAsync();
    }

    public async Task<List<MappedLearningOutcomeDto>> GetMappedLearningOutcomesAsync(int programOutcomeId)
    {
        return await _context.ProgramOutcomeMappings
            .Include(m => m.LearningOutcome)
                .ThenInclude(lo => lo.Course)
            .Where(m => m.ProgramOutcomeId == programOutcomeId)
            .Select(m => new MappedLearningOutcomeDto
            {
                LearningOutcomeId = m.LearningOutcomeId,
                Code = m.LearningOutcome.Code,
                Name = m.LearningOutcome.Name,
                CourseName = m.LearningOutcome.Course.Name,
                ContributionLevel = m.ContributionLevel
            })
            .OrderBy(m => m.CourseName).ThenBy(m => m.Code)
            .ToListAsync();
    }
}
