using EduAnalytics.Ocr;

namespace EduAnalytics.Ocr.Tests;

public class StudentMatcherTests
{
    private static readonly StudentCandidate[] Students =
    {
        new(11, "202503011060", "Eren Yılmaz"),
        new(12, "202503011038", "Kerem Aslan"),
        new(13, "202403011003", "Elif Arslan"),
        new(14, "202403011059", "Can Aydın")
    };

    [Fact]
    public void Exact_number_matches_the_student()
    {
        var (id, kind) = StudentMatcher.Match("202403011003", "", Students);
        Assert.Equal(13, id);
        Assert.Equal(MatchKind.ExactNumber, kind);
    }

    [Fact]
    public void Unreadable_digit_is_treated_as_wildcard_when_only_one_student_fits()
    {
        var (id, kind) = StudentMatcher.Match("2024030110?3", "", Students);
        Assert.Equal(13, id);
        Assert.Equal(MatchKind.PartialNumber, kind);
    }

    [Fact]
    public void Unknown_number_is_not_matched()
    {
        var (id, kind) = StudentMatcher.Match("209903099999", "", Students);
        Assert.Null(id);
        Assert.Equal(MatchKind.None, kind);
    }

    [Fact]
    public void Name_from_ocr_is_used_only_when_number_is_missing()
    {
        // OCR Türkçe "İ" harfini küçük "i" okuyabilir; eşleştirme bunu tolere eder.
        var (id, kind) = StudentMatcher.Match("", "ELiF ARSLAN", Students);
        Assert.Equal(13, id);
        Assert.Equal(MatchKind.NameOnly, kind);
    }
}
