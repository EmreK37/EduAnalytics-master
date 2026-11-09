using System.Text.Json;
using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Services.Implementations;
using EduAnalytics.Core.Entities;
using EduAnalytics.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace EduAnalytics.Business.Tests;

/// <summary>.edudata paketinin dışa aktarma → JSON → içe aktarma gidiş-dönüşü.</summary>
public class DataTransferTests
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static async Task<TestExamBuilder> SourceAsync()
    {
        var b = new TestExamBuilder();
        var mc = b.AddMultipleChoice(10, overrideMax: 15);
        b.AddMultipleChoice(10, cancelled: true);
        var classic = b.AddClassic(20);
        b.AddOutcome(1, "ÖÇ-1", mc, classic);

        b.Db.Topics.Add(new Topic { Id = 1, CourseId = TestExamBuilder.CourseId, WeekNumber = 3, Title = "Birim testi" });
        b.Db.TopicLearningOutcomes.Add(new TopicLearningOutcome { TopicId = 1, LearningOutcomeId = 1 });
        b.Db.QuestionTopics.Add(new QuestionTopic { QuestionId = mc.Id, TopicId = 1 });

        // İkinci ders ve yalnızca ona kayıtlı bir öğrenci: içe aktarmada ilk derse kaydedilmemeli.
        b.Db.Courses.Add(new Course { Id = 2, ProgramId = TestExamBuilder.ProgramId, Code = "YM201", Name = "Veri Yapıları" });
        b.AddStudent(1);
        b.AddStudent(2);
        b.Db.Students.Add(new Student { Id = 3, StudentNumber = "202403011903", FullName = "Üçüncü Öğrenci", ClassName = "2A" });
        b.Db.StudentCourses.Add(new StudentCourse { StudentId = 3, CourseId = 2 });

        b.Db.ProgramOutcomes.Add(new ProgramOutcome { Id = 1, ProgramId = TestExamBuilder.ProgramId, Code = "PÇ-1", Description = "Test tasarlar" });
        b.Db.ProgramOutcomeMappings.Add(new ProgramOutcomeMapping { ProgramOutcomeId = 1, LearningOutcomeId = 1, ContributionLevel = 4 });

        var kavram = new QuestionRubricCriterion { QuestionId = classic.Id, Title = "Kavram", MaxPoints = 12, Order = 1 };
        var ornek = new QuestionRubricCriterion { QuestionId = classic.Id, Title = "Örnek", MaxPoints = 8, Order = 2 };
        b.Db.QuestionRubricCriteria.AddRange(kavram, ornek);

        var booklet = new ExamBooklet { ExamId = TestExamBuilder.ExamId, BookletCode = "A" };
        booklet.BookletQuestions.Add(new ExamBookletQuestion { QuestionId = mc.Id, OrderInBooklet = 1, OptionShuffleMap = "A:B,B:A,C:C,D:D,E:E" });
        b.Db.ExamBooklets.Add(booklet);

        b.Mark(1, mc, OptionLetter.A).Booklet = booklet;
        b.Mark(2, mc, OptionLetter.Empty);
        var graded = b.Grade(1, classic, 15);
        graded.CriterionScores.Add(new StudentAnswerCriterion { Criterion = kavram, Score = 10, Comment = "iyi" });
        graded.CriterionScores.Add(new StudentAnswerCriterion { Criterion = ornek, Score = 5 });
        b.Grade(2, classic, 9);

        await b.SaveAsync();
        return b;
    }

    private static async Task<AdminExportPackageDto> ExportThroughJsonAsync(TestExamBuilder source)
    {
        var package = await new DataExportService(source.Db).ExportDatabaseAsync();
        var json = JsonSerializer.Serialize(package);
        return JsonSerializer.Deserialize<AdminExportPackageDto>(json, ReadOptions)!;
    }

    [Fact]
    public async Task Package_carries_exams_answers_outcomes_and_only_the_real_enrollments()
    {
        var source = await SourceAsync();
        var package = await ExportThroughJsonAsync(source);
        Assert.Equal("2.0", package.Version);

        using var target = TestExamBuilder.CreateDb();
        var result = await new DataImportService(target).ImportAdminPackageAsync(package);

        Assert.Equal(2, result.CoursesAdded);
        Assert.Equal(3, result.StudentsAdded);
        Assert.Equal(3, result.EnrollmentsAdded);
        Assert.Equal(1, result.LearningOutcomesAdded);
        Assert.Equal(1, result.ProgramOutcomesAdded);
        Assert.Equal(3, result.QuestionsAdded);
        Assert.Equal(1, result.ExamsAdded);
        Assert.Equal(4, result.AnswersAdded);
        Assert.Empty(result.Warnings);

        // Üçüncü öğrenci yalnızca kendi dersine kayıtlı (eskiden her derse kaydediliyordu).
        var third = await target.Students.SingleAsync(s => s.StudentNumber == "202403011903");
        var thirdCourses = await target.StudentCourses.Where(sc => sc.StudentId == third.Id)
            .Select(sc => sc.Course.Code).ToListAsync();
        Assert.Equal(new[] { "YM201" }, thirdCourses);

        Assert.Equal(4, (await target.ProgramOutcomeMappings.SingleAsync()).ContributionLevel);
        Assert.Equal(1, await target.TopicLearningOutcomes.CountAsync());
        Assert.Equal(2, await target.QuestionLearningOutcomes.CountAsync());

        var exam = await target.Exams.SingleAsync();
        var examQuestions = await target.ExamQuestions.Where(eq => eq.ExamId == exam.Id).ToListAsync();
        Assert.Equal(3, examQuestions.Count);
        Assert.Single(examQuestions, eq => eq.IsCancelled);
        Assert.Single(examQuestions, eq => eq.OverrideMaxPoints == 15);

        var bookletQuestion = await target.ExamBookletQuestions.SingleAsync();
        Assert.Equal("A:B,B:A,C:C,D:D,E:E", bookletQuestion.OptionShuffleMap);
        Assert.Equal(1, await target.StudentAnswers.CountAsync(a => a.BookletId != null));
        Assert.Equal(2, await target.StudentAnswerCriteria.CountAsync());

        // Aktarılan veriyle analiz aynı sonucu verir.
        var before = await new ExamAnalysisService(source.Db).GetSummaryAsync(TestExamBuilder.ExamId);
        var after = await new ExamAnalysisService(target).GetSummaryAsync(exam.Id);
        Assert.Equal(before!.AverageScore, after!.AverageScore);
        Assert.Equal(before.TotalStudents, after.TotalStudents);
    }

    [Fact]
    public async Task Importing_the_same_package_twice_adds_nothing_new()
    {
        var package = await ExportThroughJsonAsync(await SourceAsync());
        using var target = TestExamBuilder.CreateDb();
        await new DataImportService(target).ImportAdminPackageAsync(package);

        var again = await new DataImportService(target).ImportAdminPackageAsync(package);

        Assert.Equal(0, again.CoursesAdded + again.StudentsAdded + again.EnrollmentsAdded + again.LearningOutcomesAdded
                        + again.QuestionsAdded + again.ExamsAdded + again.AnswersAdded + again.OutcomeMappingsAdded);
        Assert.Equal(1, again.ExamsSkipped);
        Assert.Equal(4, await target.StudentAnswers.CountAsync());
    }

    [Fact]
    public async Task Legacy_package_with_several_courses_does_not_enroll_everyone_everywhere()
    {
        var package = new AdminExportPackageDto
        {
            Version = "1.0",
            Courses = { new() { Code = "A101", Name = "A" }, new() { Code = "B101", Name = "B" } },
            Students = { new() { Number = "202403011901", FirstName = "Ayşe", LastName = "Demir" } },
            Enrollments = null
        };
        using var target = TestExamBuilder.CreateDb();

        var result = await new DataImportService(target).ImportAdminPackageAsync(package);

        Assert.Equal(0, result.EnrollmentsAdded);
        Assert.Empty(await target.StudentCourses.ToListAsync());
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task Legacy_package_with_a_single_course_enrolls_its_students_in_that_course()
    {
        var package = new AdminExportPackageDto
        {
            Version = "1.0",
            Courses = { new() { Code = "A101", Name = "A" } },
            Students = { new() { Number = "202403011901", FirstName = "Ayşe", LastName = "Demir" } },
            Enrollments = null
        };
        using var target = TestExamBuilder.CreateDb();

        var result = await new DataImportService(target).ImportAdminPackageAsync(package);

        Assert.Equal(1, result.EnrollmentsAdded);
        Assert.Empty(result.Warnings);
    }
}
