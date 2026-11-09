using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.Core.Enums;
using EduAnalytics.DataAccess.Context;
using Microsoft.EntityFrameworkCore;

namespace EduAnalytics.Business.Services.Implementations;

public class DataExportService : IDataExportService
{
    private readonly EduAnalyticsDbContext _context;

    public DataExportService(EduAnalyticsDbContext context)
    {
        _context = context;
    }

    public async Task<AdminExportPackageDto> ExportDatabaseAsync(bool includeExamResults = true)
    {
        var package = new AdminExportPackageDto
        {
            Version = AdminExportPackageDto.CurrentVersion,
            Institution = "EduAnalytics Demo Üniversitesi",
            CreatedAt = DateTime.Now
        };

        // Programs
        var programs = await _context.Programs.AsNoTracking().ToListAsync();
        package.Programs = programs.Select(p => new AdminExportProgramDto
        {
            Code = p.Code,
            Name = p.Name,
            Description = p.Description
        }).ToList();

        // Students
        var students = await _context.Students.AsNoTracking().ToListAsync();
        package.Students = students.Select(s => new AdminExportStudentDto
        {
            Number = s.StudentNumber,
            FirstName = s.FullName.Split(' ').First(),
            LastName = string.Join(' ', s.FullName.Split(' ').Skip(1)),
            ClassName = s.ClassName
        }).ToList();

        // Courses
        var courses = await _context.Courses.AsNoTracking().Include(c => c.Program).ToListAsync();
        package.Courses = courses.Select(c => new AdminExportCourseDto
        {
            Code = c.Code,
            Name = c.Name,
            Description = c.Description,
            ProgramCode = c.Program?.Code
        }).ToList();

        // Enrollments (öğrenci–ders kayıtları)
        var enrollments = await _context.StudentCourses
            .AsNoTracking()
            .Select(sc => new AdminExportEnrollmentDto
            {
                CourseCode = sc.Course.Code,
                StudentNumber = sc.Student.StudentNumber
            })
            .ToListAsync();
        package.Enrollments = enrollments;

        // Learning outcomes
        var outcomes = await _context.LearningOutcomes.AsNoTracking().Include(lo => lo.Course).ToListAsync();
        package.LearningOutcomes = outcomes.Select(lo => new AdminExportLearningOutcomeDto
        {
            CourseCode = lo.Course.Code,
            Code = lo.Code,
            Name = lo.Name,
            Description = lo.Description
        }).ToList();

        // Topics (+ ÖÇ bağları)
        var topics = await _context.Topics
            .AsNoTracking()
            .Include(t => t.Course)
            .Include(t => t.TopicLearningOutcomes).ThenInclude(tl => tl.LearningOutcome)
            .ToListAsync();
        package.Topics = topics.Select(t => new AdminExportTopicDto
        {
            CourseCode = t.Course.Code,
            WeekNumber = t.WeekNumber,
            Title = t.Title,
            Description = t.Description,
            LearningOutcomeCodes = t.TopicLearningOutcomes.Select(tl => tl.LearningOutcome.Code).ToList()
        }).ToList();

        // Program outcomes (+ ÖÇ eşleşmeleri ve katkı düzeyleri)
        var programOutcomes = await _context.ProgramOutcomes
            .AsNoTracking()
            .Include(po => po.Program)
            .Include(po => po.Mappings).ThenInclude(m => m.LearningOutcome).ThenInclude(lo => lo.Course)
            .ToListAsync();
        package.ProgramOutcomes = programOutcomes.Select(po => new AdminExportProgramOutcomeDto
        {
            ProgramCode = po.Program.Code,
            Code = po.Code,
            Description = po.Description,
            Mappings = po.Mappings.Select(m => new AdminExportOutcomeMappingDto
            {
                CourseCode = m.LearningOutcome.Course.Code,
                LearningOutcomeCode = m.LearningOutcome.Code,
                ContributionLevel = m.ContributionLevel
            }).ToList()
        }).ToList();

        // Questions
        var questions = await _context.Questions
            .AsNoTracking()
            .Include(q => q.Course)
            .Include(q => q.QuestionTopics).ThenInclude(qt => qt.Topic)
            .Include(q => q.QuestionLearningOutcomes).ThenInclude(ql => ql.LearningOutcome)
            .Include(q => q.RubricCriteria)
            .ToListAsync();

        package.Questions = questions.Select(q =>
        {
            var topicTitles = q.QuestionTopics.Select(qt => qt.Topic.Title).ToList();
            var dto = new AdminExportQuestionDto
            {
                Code = QuestionCode(q.Id),
                Content = q.QuestionText,
                Difficulty = 1,
                CourseCode = q.Course.Code,
                TopicTitle = topicTitles.FirstOrDefault(),
                TopicTitles = topicTitles,
                LearningOutcomeCodes = q.QuestionLearningOutcomes.Select(ql => ql.LearningOutcome.Code).ToList(),
                QuestionType = q.Type.ToString(),
                IsActive = q.IsActive,
                ClassicAnswerKey = q.AnswerKey,
                ClassicMaxScore = q.MaxPoints,
                RubricCriteria = q.RubricCriteria.OrderBy(r => r.Order).Select(r => new AdminExportRubricCriteriaDto
                {
                    Name = r.Title,
                    MaxScore = r.MaxPoints,
                    OrderIndex = r.Order,
                    Description = r.Description
                }).ToList()
            };

            if (q.Type != QuestionType.OpenEnded)
            {
                dto.Choices = new List<AdminExportQuestionChoiceDto>
                {
                    new() { Key = "A", Content = q.OptionA, IsCorrect = q.CorrectOption == OptionLetter.A },
                    new() { Key = "B", Content = q.OptionB, IsCorrect = q.CorrectOption == OptionLetter.B },
                    new() { Key = "C", Content = q.OptionC, IsCorrect = q.CorrectOption == OptionLetter.C },
                    new() { Key = "D", Content = q.OptionD, IsCorrect = q.CorrectOption == OptionLetter.D },
                    new() { Key = "E", Content = q.OptionE, IsCorrect = q.CorrectOption == OptionLetter.E }
                }.Where(c => !string.IsNullOrWhiteSpace(c.Content)).ToList();
            }

            return dto;
        }).ToList();

        if (includeExamResults)
            package.Exams = await ExportExamsAsync();

        return package;
    }

    private async Task<List<AdminExportExamDto>> ExportExamsAsync()
    {
        var exams = await _context.Exams
            .AsNoTracking()
            .Include(e => e.Course)
            .Include(e => e.ExamQuestions)
            .Include(e => e.Booklets).ThenInclude(b => b.BookletQuestions)
            .ToListAsync();

        var answers = await _context.StudentAnswers
            .AsNoTracking()
            .Include(sa => sa.Student)
            .Include(sa => sa.Booklet)
            .Include(sa => sa.CriterionScores).ThenInclude(cs => cs.Criterion)
            .ToListAsync();
        var answersByExam = answers.ToLookup(a => a.ExamId);

        return exams.Select(e => new AdminExportExamDto
        {
            CourseCode = e.Course.Code,
            Title = e.Title,
            ExamDate = e.ExamDate,
            DurationMinutes = e.DurationMinutes,
            ExamType = e.ExamType.ToString(),
            BookletCount = e.BookletCount,
            ShuffleOptions = e.ShuffleOptions,
            Questions = e.ExamQuestions.OrderBy(eq => eq.OrderInExam).Select(eq => new AdminExportExamQuestionDto
            {
                QuestionCode = QuestionCode(eq.QuestionId),
                Order = eq.OrderInExam,
                OverrideMaxPoints = eq.OverrideMaxPoints,
                IsCancelled = eq.IsCancelled,
                CancellationReason = eq.CancellationReason
            }).ToList(),
            Booklets = e.Booklets.OrderBy(b => b.BookletCode).Select(b => new AdminExportBookletDto
            {
                Code = b.BookletCode,
                Questions = b.BookletQuestions.OrderBy(bq => bq.OrderInBooklet).Select(bq => new AdminExportBookletQuestionDto
                {
                    QuestionCode = QuestionCode(bq.QuestionId),
                    Order = bq.OrderInBooklet,
                    ShuffleMap = bq.OptionShuffleMap
                }).ToList()
            }).ToList(),
            Answers = answersByExam[e.Id].Select(a => new AdminExportAnswerDto
            {
                StudentNumber = a.Student.StudentNumber,
                QuestionCode = QuestionCode(a.QuestionId),
                BookletCode = a.Booklet?.BookletCode,
                SelectedOption = a.SelectedOption.ToString(),
                Score = a.Score,
                IsCorrect = a.IsCorrect,
                CriterionScores = a.CriterionScores.Select(cs => new AdminExportCriterionScoreDto
                {
                    CriterionOrder = cs.Criterion.Order,
                    CriterionTitle = cs.Criterion.Title,
                    Score = cs.Score,
                    Comment = cs.Comment
                }).ToList()
            }).ToList()
        }).ToList();
    }

    private static string QuestionCode(int questionId) => $"Q{questionId}";
}
