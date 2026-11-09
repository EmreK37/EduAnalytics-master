using EduAnalytics.Business.Services.Implementations;
using EduAnalytics.Core.Entities;
using EduAnalytics.Core.Enums;

namespace EduAnalytics.Business.Tests;

/// <summary>
/// Sınıf, öğrenci, PÇ ve profil hesaplarının aynı kuralla (kazanılan / alınabilecek puan; sınava özgü
/// tam puan; iptal edilen sorular hariç) çalıştığını doğrular.
/// </summary>
public class AnalysisConsistencyTests
{
    [Fact]
    public async Task Learning_outcomes_use_points_from_both_question_types_at_class_and_student_level()
    {
        // ÖÇ-1: test (10) + klasik (20). ÖÇ-2: yalnızca klasik (10) — eskiden sınıf raporunda hiç görünmüyordu.
        var b = new TestExamBuilder();
        var q1 = b.AddMultipleChoice(10);
        var q2 = b.AddClassic(20);
        var q3 = b.AddClassic(10);
        b.AddOutcome(1, "ÖÇ-1", q1, q2);
        b.AddOutcome(2, "ÖÇ-2", q3);
        b.AddStudent(1); b.AddStudent(2);
        // Öğrenci 1: ÖÇ-1 = 24/30 (%80), ÖÇ-2 = 5/10 (%50)
        b.Mark(1, q1, OptionLetter.A); b.Grade(1, q2, 14); b.Grade(1, q3, 5);
        // Öğrenci 2: ÖÇ-1 = 10/30 (%33,3), ÖÇ-2 = 6/10 (%60 — eşikte, zayıf değil)
        b.Mark(2, q1, OptionLetter.B); b.Grade(2, q2, 10); b.Grade(2, q3, 6);
        await b.SaveAsync();

        var classLevel = await new LearningOutcomePerformanceService(b.Db).AnalyzeExamAsync(TestExamBuilder.ExamId);
        var lo1 = classLevel.Single(o => o.LearningOutcomeId == 1);
        var lo2 = classLevel.Single(o => o.LearningOutcomeId == 2);
        Assert.Equal(34.0 / 60 * 100, lo1.SuccessRate, 6);
        Assert.Equal(55.0, lo2.SuccessRate, 6);
        Assert.True(lo1.IsWeak);
        Assert.True(lo2.IsWeak);

        var ranking = await new StudentPerformanceService(b.Db).GetExamRankingAsync(TestExamBuilder.ExamId);
        var s1 = ranking.Single(s => s.StudentId == 1);
        var s2 = ranking.Single(s => s.StudentId == 2);
        Assert.Equal(new[] { "ÖÇ-2 - Çıktı ÖÇ-2" }, s1.WeakLearningOutcomes);
        Assert.Equal(new[] { "ÖÇ-1 - Çıktı ÖÇ-1" }, s2.WeakLearningOutcomes);
    }

    [Fact]
    public async Task Program_outcome_is_the_contribution_weighted_mean_and_ignores_cancelled_questions()
    {
        var b = new TestExamBuilder();
        var q1 = b.AddMultipleChoice(10);                    // ÖÇ-1
        var q2 = b.AddMultipleChoice(10);                    // ÖÇ-2
        var q3 = b.AddMultipleChoice(10, cancelled: true);   // ÖÇ-2, sınavda iptal edildi
        var q4 = b.AddClassic(10, isActive: false);          // ÖÇ-1, sonradan pasif yapıldı ama sınavda soruldu
        b.AddOutcome(1, "ÖÇ-1", q1, q4);
        b.AddOutcome(2, "ÖÇ-2", q2, q3);
        b.AddOutcome(3, "ÖÇ-3");                             // hiç ölçülmemiş

        for (var s = 1; s <= 5; s++)
        {
            b.AddStudent(s);
            b.Mark(s, q1, s <= 4 ? OptionLetter.A : OptionLetter.B);   // 4/5 doğru
            b.Mark(s, q2, s == 1 ? OptionLetter.A : OptionLetter.B);   // 1/5 doğru
            b.Mark(s, q3, OptionLetter.A);                             // iptal: sayılmamalı
            b.Grade(s, q4, 8);                                         // 8/10
        }

        b.Db.ProgramOutcomes.AddRange(
            new ProgramOutcome { Id = 1, ProgramId = TestExamBuilder.ProgramId, Code = "PÇ-1", Description = "Birinci" },
            new ProgramOutcome { Id = 2, ProgramId = TestExamBuilder.ProgramId, Code = "PÇ-2", Description = "İkinci" });
        b.Db.ProgramOutcomeMappings.AddRange(
            new ProgramOutcomeMapping { ProgramOutcomeId = 1, LearningOutcomeId = 1, ContributionLevel = 5 },
            new ProgramOutcomeMapping { ProgramOutcomeId = 1, LearningOutcomeId = 2, ContributionLevel = 1 },
            new ProgramOutcomeMapping { ProgramOutcomeId = 2, LearningOutcomeId = 3, ContributionLevel = 3 });
        await b.SaveAsync();

        var report = await new ProgramOutcomeService(b.Db).GenerateReportAsync(TestExamBuilder.ProgramId);

        var po1 = report.Single(r => r.ProgramOutcomeCode == "PÇ-1");
        var lo1 = po1.LinkedLearningOutcomes.Single(l => l.Code == "ÖÇ-1");
        var lo2 = po1.LinkedLearningOutcomes.Single(l => l.Code == "ÖÇ-2");
        Assert.Equal(80.0, lo1.SuccessRate);   // (40 + 40) / 100
        Assert.Equal(20.0, lo2.SuccessRate);   // iptal edilen soru sayılsaydı %60 olurdu
        Assert.Equal(2, lo1.QuestionCount);
        Assert.Equal(1, lo2.QuestionCount);
        Assert.Equal(70.0, po1.OverallSuccessRate); // (80 × 5 + 20 × 1) / 6
        Assert.True(po1.HasData);

        var po2 = report.Single(r => r.ProgramOutcomeCode == "PÇ-2");
        Assert.False(po2.HasData);
        Assert.Equal("veri yok", po2.OverallSuccessDisplay);
    }

    [Fact]
    public async Task Student_profile_uses_exam_specific_points_and_skips_cancelled_questions()
    {
        var b = new TestExamBuilder();
        var q1 = b.AddMultipleChoice(1, overrideMax: 10);   // bankada 1 puan, bu sınavda 10
        var q2 = b.AddMultipleChoice(5, cancelled: true);
        var q3 = b.AddClassic(20);
        b.AddStudent(1);
        b.Mark(1, q1, OptionLetter.A);
        b.Mark(1, q2, OptionLetter.A);
        b.Grade(1, q3, 7);
        await b.SaveAsync();

        var profile = await new MyCoursesService(b.Db).GetStudentProfileAsync(TestExamBuilder.CourseId, 1);
        var exam = Assert.Single(profile.Exams);
        Assert.Equal(17m, exam.Score);
        Assert.Equal(30m, exam.MaxScore);

        // Sıralamadaki puanla aynı.
        var ranking = await new StudentPerformanceService(b.Db).GetExamRankingAsync(TestExamBuilder.ExamId);
        Assert.Equal(exam.Score, ranking.Single().TotalScore);
    }

    [Fact]
    public async Task Classic_questions_get_difficulty_and_discrimination_from_total_score_groups()
    {
        var b = new TestExamBuilder();
        var mc = b.AddMultipleChoice(10);
        var classic = b.AddClassic(10);
        var scores = new decimal[] { 10, 9, 8, 7, 3, 2, 1, 0 };
        for (var s = 1; s <= 8; s++)
        {
            b.AddStudent(s);
            b.Mark(s, mc, s <= 4 ? OptionLetter.A : OptionLetter.C);
            b.Grade(s, classic, scores[s - 1]);
        }
        await b.SaveAsync();

        var items = await new ItemAnalysisService(b.Db).AnalyzeAsync(TestExamBuilder.ExamId);

        // 8 öğrenci → üst ve alt grup 2'şer kişi (round(8 × 0,27)).
        var open = items.Single(i => i.QuestionId == classic.Id);
        Assert.True(open.IsOpenEnded);
        Assert.Equal(5.0, open.AverageScore);
        Assert.Equal(0.5, open.DifficultyIndex);            // ortalama 5 / tam puan 10
        Assert.True(open.HasDiscrimination);
        Assert.Equal(0.9, open.DiscriminationIndex);        // ((10 + 9) − (1 + 0)) / (2 × 10)
        Assert.Equal(2, open.GroupSize);
        Assert.Empty(open.Distractors);

        var test = items.Single(i => i.QuestionId == mc.Id);
        Assert.False(test.IsOpenEnded);
        Assert.Equal(0.5, test.DifficultyIndex);
        Assert.Equal(1.0, test.DiscriminationIndex);
    }

    [Fact]
    public async Task Items_without_answers_are_reported_as_insufficient_data()
    {
        var b = new TestExamBuilder();
        b.AddMultipleChoice(10);
        b.AddClassic(10);
        await b.SaveAsync();

        var items = await new ItemAnalysisService(b.Db).AnalyzeAsync(TestExamBuilder.ExamId);

        Assert.Equal(2, items.Count);
        Assert.All(items, i =>
        {
            Assert.False(i.HasData);
            Assert.False(i.HasDiscrimination);
            Assert.Equal("Veri Yetersiz", i.DiscriminationCategory);
        });
    }

    [Fact]
    public async Task Cronbach_alpha_treats_a_missing_answer_like_a_zero_score()
    {
        async Task<double> AlphaAsync(bool zeroRowForLastStudent)
        {
            var b = new TestExamBuilder();
            var q1 = b.AddMultipleChoice(10);
            var q2 = b.AddMultipleChoice(10);
            var q3 = b.AddClassic(10);
            for (var s = 1; s <= 4; s++) b.AddStudent(s);
            b.Mark(1, q1, OptionLetter.A); b.Mark(1, q2, OptionLetter.A); b.Grade(1, q3, 9);
            b.Mark(2, q1, OptionLetter.A); b.Mark(2, q2, OptionLetter.A); b.Grade(2, q3, 6);
            b.Mark(3, q1, OptionLetter.A); b.Mark(3, q2, OptionLetter.B); b.Grade(3, q3, 4);
            b.Mark(4, q1, OptionLetter.B); b.Mark(4, q2, OptionLetter.B);
            if (zeroRowForLastStudent) b.Grade(4, q3, 0);
            await b.SaveAsync();
            var stats = await new ExamStatisticsService(b.Db).ComputeAsync(TestExamBuilder.ExamId);
            return stats!.CronbachAlpha;
        }

        var withZero = await AlphaAsync(zeroRowForLastStudent: true);
        var missing = await AlphaAsync(zeroRowForLastStudent: false);

        Assert.True(withZero > 0);
        Assert.Equal(withZero, missing);
    }
}
