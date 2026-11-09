using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Services.Implementations;
using Microsoft.EntityFrameworkCore;

namespace EduAnalytics.Business.Tests;

public class RubricCriteriaRulesTests
{
    private static RubricCriterionCreateModel Criterion(string title, decimal points, int? id = null) =>
        new() { Id = id, Title = title, MaxPoints = points };

    [Fact]
    public async Task Criteria_total_must_equal_the_question_points()
    {
        var b = new TestExamBuilder();
        var q = b.AddClassic(20);
        await b.SaveAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new RubricService(b.Db).SetCriteriaAsync(q.Id, new List<RubricCriterionCreateModel> { Criterion("A", 10), Criterion("B", 5) }));
    }

    [Fact]
    public async Task Scored_criteria_can_be_renamed_but_not_deleted_or_lowered_below_given_scores()
    {
        var b = new TestExamBuilder();
        var q = b.AddClassic(20);
        b.AddStudent(1);
        await b.SaveAsync();
        var service = new RubricService(b.Db);

        await service.SetCriteriaAsync(q.Id, new List<RubricCriterionCreateModel> { Criterion("Kavram", 10), Criterion("Örnek", 10) });
        var saved = await b.Db.QuestionRubricCriteria.OrderBy(c => c.Order).ToListAsync();
        await service.SaveStudentGradeAsync(TestExamBuilder.ExamId, q.Id, 1, new List<CriterionScoreUpdate>
        {
            new() { CriterionId = saved[0].Id, Score = 8 },
            new() { CriterionId = saved[1].Id, Score = 5 }
        });

        // Başlık değişikliği: Id korunur, öğrencinin puanı kaybolmaz.
        await service.SetCriteriaAsync(q.Id, new List<RubricCriterionCreateModel>
        {
            Criterion("Kavram doğruluğu", 10, saved[0].Id), Criterion("Örnek", 10, saved[1].Id)
        });
        var renamed = await b.Db.QuestionRubricCriteria.SingleAsync(c => c.Id == saved[0].Id);
        Assert.Equal("Kavram doğruluğu", renamed.Title);
        Assert.Equal(2, await b.Db.StudentAnswerCriteria.CountAsync());

        // Puanlanmış ölçüt silinemez.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetCriteriaAsync(q.Id,
            new List<RubricCriterionCreateModel> { Criterion("Kavram doğruluğu", 20, saved[0].Id) }));

        // Verilmiş en yüksek puanın (8) altına düşürülemez.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetCriteriaAsync(q.Id,
            new List<RubricCriterionCreateModel> { Criterion("Kavram doğruluğu", 7, saved[0].Id), Criterion("Örnek", 13, saved[1].Id) }));
    }

    [Fact]
    public async Task Rubric_grading_requires_criteria_total_to_match_the_exam_points()
    {
        var b = new TestExamBuilder();
        var q = b.AddClassic(20, overrideMax: 40);   // bu sınavda 40 puan, rubrik 20 puan üzerinden
        b.AddStudent(1);
        await b.SaveAsync();
        var service = new RubricService(b.Db);
        await service.SetCriteriaAsync(q.Id, new List<RubricCriterionCreateModel> { Criterion("A", 10), Criterion("B", 10) });
        var criteria = await b.Db.QuestionRubricCriteria.ToListAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveStudentGradeAsync(TestExamBuilder.ExamId, q.Id, 1,
            criteria.Select(c => new CriterionScoreUpdate { CriterionId = c.Id, Score = 5 }).ToList()));

        var grade = await service.GetStudentGradeAsync(TestExamBuilder.ExamId, q.Id, 1);
        Assert.Equal(40m, grade.ExamMaxPoints);
        Assert.Equal(20m, grade.CriteriaTotal);
    }
}
