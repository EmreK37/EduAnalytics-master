using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Helpers;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.Core.Entities;
using EduAnalytics.DataAccess.Context;
using Microsoft.EntityFrameworkCore;

namespace EduAnalytics.Business.Services.Implementations;

public class MyCoursesService : IMyCoursesService
{
    private readonly EduAnalyticsDbContext _context;

    public MyCoursesService(EduAnalyticsDbContext context)
    {
        _context = context;
    }

    public async Task<List<MyCourseDetailDto>> GetMyCoursesAsync()
    {
        var courses = await _context.Courses
            .Include(c => c.StudentCourses)
            .ThenInclude(sc => sc.Student)
            .Include(c => c.Topics)
            .Include(c => c.Questions)
            .ToListAsync();

        var result = new List<MyCourseDetailDto>();
        foreach(var c in courses)
        {
            var dto = new MyCourseDetailDto
            {
                CourseId = c.Id,
                CourseCode = c.Code,
                CourseName = c.Name,
                EnrolledStudentCount = c.StudentCourses.Count,
                TopicCount = c.Topics?.Count ?? 0,
                QuestionCount = c.Questions?.Count ?? 0,
                AverageScore = null, // Can be calculated if exams exist
                EnrolledStudents = c.StudentCourses.Select(sc => new StudentDto
                {
                    Id = sc.Student.Id,
                    StudentNumber = sc.Student.StudentNumber,
                    FullName = sc.Student.FullName,
                    ClassName = sc.Student.ClassName
                }).ToList()
            };
            result.Add(dto);
        }
        return result;
    }

    public async Task<StudentCourseProfileDto> GetStudentProfileAsync(int courseId, int studentId)
    {
        var student = await _context.Students.FindAsync(studentId);
        var course = await _context.Courses.FindAsync(courseId);
        if (student == null || course == null) throw new Exception("Öğrenci veya Ders bulunamadı.");

        var answers = await _context.StudentAnswers
            .Include(a => a.Exam)
            .Where(a => a.StudentId == studentId && a.Exam.CourseId == courseId)
            .ToListAsync();

        // Puan, sınav analizi ve sıralamayla aynı kuralla hesaplanır: sınava özgü tam puan
        // (OverrideMaxPoints) kullanılır, iptal edilen sorular sayılmaz.
        var examIds = answers.Select(a => a.ExamId).Distinct().ToList();
        var examQuestions = await _context.ExamQuestions
            .Include(eq => eq.Question)
            .Where(eq => examIds.Contains(eq.ExamId) && !eq.IsCancelled)
            .ToListAsync();

        var examQuestionByKey = new Dictionary<(int ExamId, int QuestionId), ExamQuestion>();
        foreach (var eq in examQuestions)
            examQuestionByKey.TryAdd((eq.ExamId, eq.QuestionId), eq);
        var maxScoreByExam = examQuestions
            .GroupBy(eq => eq.ExamId)
            .ToDictionary(g => g.Key, g => g.Sum(ScoringRules.MaxPoints));

        var examsDto = answers.GroupBy(a => a.Exam)
            .Select(g => new StudentExamResultDto
            {
                ExamName = g.Key.Title,
                Date = g.Key.ExamDate,
                Score = g.Sum(a => examQuestionByKey.TryGetValue((a.ExamId, a.QuestionId), out var eq)
                    ? ScoringRules.Score(eq, a)
                    : 0m),
                MaxScore = maxScoreByExam.GetValueOrDefault(g.Key.Id)
            })
            .OrderBy(e => e.Date)
            .ToList();

        return new StudentCourseProfileDto
        {
            StudentId = student.Id,
            FullName = student.FullName,
            StudentNumber = student.StudentNumber,
            ClassName = student.ClassName ?? "Belirtilmemiş",
            CourseName = course.Name,
            Exams = examsDto
        };
    }
}
