using EduAnalytics.Core.Entities;
using EduAnalytics.Core.Enums;
using EduAnalytics.DataAccess.Context;
using Microsoft.EntityFrameworkCore;
using AcademicProgram = EduAnalytics.Core.Entities.Program;

namespace EduAnalytics.Business.Tests;

/// <summary>
/// Testler için bellekte bir ders + sınav kurar: program, ders, sınav, sorular, öğrenciler, cevaplar, ÖÇ'ler.
/// Kimlikler sabittir (program 1, ders 1, sınav 1); sorular ve öğrenciler verilen sırayla numaralanır.
/// </summary>
internal sealed class TestExamBuilder
{
    public const int ProgramId = 1;
    public const int CourseId = 1;
    public const int ExamId = 1;

    private int _nextQuestionId = 1;
    private int _nextOrder = 1;

    public EduAnalyticsDbContext Db { get; }

    public TestExamBuilder(EduAnalyticsDbContext? db = null, string courseCode = "YM344")
    {
        Db = db ?? CreateDb();
        Db.Programs.Add(new AcademicProgram { Id = ProgramId, Code = "YM", Name = "Yazılım Mühendisliği" });
        Db.Courses.Add(new Course { Id = CourseId, ProgramId = ProgramId, Code = courseCode, Name = "Yazılım Kalite Güvencesi" });
        Db.Exams.Add(new Exam { Id = ExamId, CourseId = CourseId, Title = "Vize", ExamDate = new DateTime(2026, 11, 2, 10, 0, 0) });
    }

    public static EduAnalyticsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<EduAnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    public Question AddMultipleChoice(decimal points = 25, OptionLetter correct = OptionLetter.A,
        decimal? overrideMax = null, bool cancelled = false, bool isActive = true)
    {
        var id = _nextQuestionId++;
        var q = new Question
        {
            Id = id,
            CourseId = CourseId,
            QuestionText = $"Test sorusu {id}",
            Type = QuestionType.MultipleChoice,
            MaxPoints = points,
            CorrectOption = correct,
            OptionA = "a", OptionB = "b", OptionC = "c", OptionD = "d",
            IsActive = isActive
        };
        AddToExam(q, overrideMax, cancelled);
        return q;
    }

    public Question AddClassic(decimal points, decimal? overrideMax = null, bool cancelled = false, bool isActive = true)
    {
        var id = _nextQuestionId++;
        var q = new Question
        {
            Id = id,
            CourseId = CourseId,
            QuestionText = $"Klasik soru {id}",
            Type = QuestionType.OpenEnded,
            MaxPoints = points,
            CorrectOption = OptionLetter.Empty,
            IsActive = isActive
        };
        AddToExam(q, overrideMax, cancelled);
        return q;
    }

    private void AddToExam(Question q, decimal? overrideMax, bool cancelled)
    {
        Db.Questions.Add(q);
        Db.ExamQuestions.Add(new ExamQuestion
        {
            ExamId = ExamId,
            QuestionId = q.Id,
            OrderInExam = _nextOrder++,
            OverrideMaxPoints = overrideMax,
            IsCancelled = cancelled
        });
    }

    public Student AddStudent(int id, bool enroll = true)
    {
        var s = new Student
        {
            Id = id,
            StudentNumber = $"2024030119{id:00}",
            FullName = $"Öğrenci {id}",
            ClassName = "Yazılım Mühendisliği 3A"
        };
        Db.Students.Add(s);
        if (enroll)
            Db.StudentCourses.Add(new StudentCourse { StudentId = id, CourseId = CourseId });
        return s;
    }

    public StudentAnswer Mark(int studentId, Question q, OptionLetter option)
    {
        var a = new StudentAnswer
        {
            ExamId = ExamId,
            QuestionId = q.Id,
            StudentId = studentId,
            SelectedOption = option,
            IsCorrect = option != OptionLetter.Empty && option == q.CorrectOption
        };
        Db.StudentAnswers.Add(a);
        return a;
    }

    public StudentAnswer Grade(int studentId, Question q, decimal score, decimal? max = null)
    {
        var a = new StudentAnswer
        {
            ExamId = ExamId,
            QuestionId = q.Id,
            StudentId = studentId,
            SelectedOption = OptionLetter.Empty,
            Score = score,
            IsCorrect = score >= (max ?? q.MaxPoints) / 2m
        };
        Db.StudentAnswers.Add(a);
        return a;
    }

    public LearningOutcome AddOutcome(int id, string code, params Question[] questions)
    {
        var lo = new LearningOutcome { Id = id, CourseId = CourseId, Code = code, Name = $"Çıktı {code}" };
        Db.LearningOutcomes.Add(lo);
        foreach (var q in questions)
            Db.QuestionLearningOutcomes.Add(new QuestionLearningOutcome { QuestionId = q.Id, LearningOutcomeId = id });
        return lo;
    }

    public Task SaveAsync() => Db.SaveChangesAsync();
}
