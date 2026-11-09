using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Services.Implementations;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.Core.Entities;
using EduAnalytics.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EduAnalytics.Business.Tests;

public class AnswerEntryServiceTests
{
    private static AnswerEntryService Service(TestExamBuilder b) => new(b.Db, Mock.Of<IExamBookletService>());

    private static StudentAnswerUpdate Mc(int studentId, Question q, OptionLetter option) => new()
    {
        ExamId = TestExamBuilder.ExamId, QuestionId = q.Id, StudentId = studentId, SelectedOption = option
    };

    private static StudentAnswerUpdate Classic(int studentId, Question q, decimal? score) => new()
    {
        ExamId = TestExamBuilder.ExamId, QuestionId = q.Id, StudentId = studentId,
        SelectedOption = OptionLetter.Empty, Score = score
    };

    [Fact]
    public async Task Blank_answers_and_blank_papers_are_counted_in_the_analysis()
    {
        // 4 test sorusu × 25 puan. Öğrenci 1: hepsi doğru (100). Öğrenci 2: 1. soru boş (75).
        // Öğrenci 3: sınava girdi, her şeyi boş bıraktı (0). Doğru ortalama 58,33; boş cevaplar
        // kaybolsaydı 87,5 çıkardı ve 1. sorunun güçlüğü 1/3 yerine 1 olurdu.
        var b = new TestExamBuilder();
        var questions = Enumerable.Range(0, 4).Select(_ => b.AddMultipleChoice(25)).ToList();
        b.AddStudent(1); b.AddStudent(2); b.AddStudent(3);
        await b.SaveAsync();

        var updates = new List<StudentAnswerUpdate>();
        foreach (var q in questions)
        {
            updates.Add(Mc(1, q, OptionLetter.A));
            updates.Add(Mc(2, q, q == questions[0] ? OptionLetter.Empty : OptionLetter.A));
            updates.Add(Mc(3, q, OptionLetter.Empty));
        }

        var result = await Service(b).SaveAsync(TestExamBuilder.ExamId, updates, attendedStudentIds: new[] { 3 });

        Assert.Equal(3, result.StudentsSaved);
        Assert.Equal(5, result.BlankAnswers);
        Assert.Equal(0, result.AbsentStudents);

        var summary = await new ExamAnalysisService(b.Db).GetSummaryAsync(TestExamBuilder.ExamId);
        Assert.Equal(3, summary!.TotalStudents);
        Assert.Equal(58.33m, summary.AverageScore);

        var first = (await new ItemAnalysisService(b.Db).AnalyzeAsync(TestExamBuilder.ExamId)).Single(i => i.QuestionNumber == 1);
        Assert.Equal(3, first.TotalStudents);
        Assert.Equal(1, first.CorrectCount);
        Assert.Equal(2, first.EmptyCount);
        Assert.Equal(0.333, first.DifficultyIndex);
    }

    [Fact]
    public async Task Completely_empty_row_means_the_student_did_not_take_the_exam()
    {
        var b = new TestExamBuilder();
        var q1 = b.AddMultipleChoice(50);
        var q2 = b.AddMultipleChoice(50);
        b.AddStudent(1); b.AddStudent(2);
        await b.SaveAsync();

        var result = await Service(b).SaveAsync(TestExamBuilder.ExamId, new List<StudentAnswerUpdate>
        {
            Mc(1, q1, OptionLetter.A), Mc(1, q2, OptionLetter.B),
            Mc(2, q1, OptionLetter.Empty), Mc(2, q2, OptionLetter.Empty)
        });

        Assert.Equal(1, result.StudentsSaved);
        Assert.Equal(1, result.AbsentStudents);
        Assert.False(await b.Db.StudentAnswers.AnyAsync(a => a.StudentId == 2));
        // Öğrenci 1'in boş olmayan ama yanlış cevabı da kayıtlı: 2 satır.
        Assert.Equal(2, await b.Db.StudentAnswers.CountAsync(a => a.StudentId == 1));
    }

    [Fact]
    public async Task Clearing_a_saved_row_removes_the_students_answers()
    {
        var b = new TestExamBuilder();
        var q1 = b.AddMultipleChoice(50);
        var q2 = b.AddClassic(50);
        b.AddStudent(1);
        await b.SaveAsync();
        var service = Service(b);

        await service.SaveAsync(TestExamBuilder.ExamId, new List<StudentAnswerUpdate> { Mc(1, q1, OptionLetter.A), Classic(1, q2, 30) });
        Assert.Equal(2, await b.Db.StudentAnswers.CountAsync());

        var result = await service.SaveAsync(TestExamBuilder.ExamId, new List<StudentAnswerUpdate> { Mc(1, q1, OptionLetter.Empty), Classic(1, q2, null) });

        Assert.Equal(1, result.ClearedStudents);
        Assert.Equal(0, await b.Db.StudentAnswers.CountAsync());
    }

    [Fact]
    public async Task Cleared_classic_score_is_no_longer_graded()
    {
        var b = new TestExamBuilder();
        var q1 = b.AddMultipleChoice(50);
        var q2 = b.AddClassic(50);
        b.AddStudent(1);
        b.Mark(1, q1, OptionLetter.A);
        b.Grade(1, q2, 40);
        await b.SaveAsync();

        await Service(b).SaveAsync(TestExamBuilder.ExamId, new List<StudentAnswerUpdate> { Mc(1, q1, OptionLetter.A), Classic(1, q2, null) });

        Assert.False(await b.Db.StudentAnswers.AnyAsync(a => a.QuestionId == q2.Id));
        Assert.True(await b.Db.StudentAnswers.AnyAsync(a => a.QuestionId == q1.Id));
    }

    [Fact]
    public async Task Changing_a_rubric_graded_score_removes_the_stale_breakdown()
    {
        var b = new TestExamBuilder();
        var q = b.AddClassic(30);
        b.AddStudent(1);
        var c1 = new QuestionRubricCriterion { QuestionId = q.Id, Title = "Kavram", MaxPoints = 15, Order = 1 };
        var c2 = new QuestionRubricCriterion { QuestionId = q.Id, Title = "Uygulama", MaxPoints = 15, Order = 2 };
        b.Db.QuestionRubricCriteria.AddRange(c1, c2);
        var answer = b.Grade(1, q, 26);
        answer.CriterionScores.Add(new StudentAnswerCriterion { Criterion = c1, Score = 13 });
        answer.CriterionScores.Add(new StudentAnswerCriterion { Criterion = c2, Score = 13 });
        await b.SaveAsync();
        var service = Service(b);

        // Aynı puan: döküm korunur.
        var unchanged = await service.SaveAsync(TestExamBuilder.ExamId, new List<StudentAnswerUpdate> { Classic(1, q, 26) });
        Assert.Equal(0, unchanged.RubricBreakdownsCleared);
        Assert.Equal(2, await b.Db.StudentAnswerCriteria.CountAsync());

        // Farklı puan (elle ya da optik okumayla): eski döküm artık bu puanı açıklamaz.
        var changed = await service.SaveAsync(TestExamBuilder.ExamId, new List<StudentAnswerUpdate> { Classic(1, q, 21) });
        Assert.Equal(1, changed.RubricBreakdownsCleared);
        Assert.Equal(0, await b.Db.StudentAnswerCriteria.CountAsync());
        Assert.Equal(21m, (await b.Db.StudentAnswers.SingleAsync()).Score);
    }
}
