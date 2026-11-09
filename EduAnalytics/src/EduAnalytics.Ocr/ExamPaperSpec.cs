namespace EduAnalytics.Ocr;

/// <summary>Kâğıtta basılan tek soru (kâğıttaki sırasıyla).</summary>
/// <param name="Number">Kâğıtta basılan soru numarası (1, 2, 3…); puan tablosundaki sütunla aynıdır.</param>
/// <param name="IsMultipleChoice">Test sorusu mu? Test soruları optik cevap alanından okunur.</param>
/// <param name="MaxPoints">Sorunun puan değeri (kâğıtta sorunun yanında basılır; okunan puan bunu aşamaz).</param>
/// <param name="Text">Soru metni (yalnızca soru metinleri basılırken kullanılır).</param>
/// <param name="Options">Test sorusunun kitapçıkta görünen sırasıyla A–E şıkları.</param>
public sealed record PaperQuestion(
    int Number,
    bool IsMultipleChoice,
    decimal MaxPoints,
    string Text = "",
    IReadOnlyList<string>? Options = null);

/// <summary>
/// Üretilecek/okunacak sınav kâğıdının içeriği. Antet alanları yalnızca basılır; okuyucu için soru listesi
/// (ve kitapçıklar) yeterlidir. Çok kitapçıklı sınavda her kitapçık kendi soru sırasıyla ayrı basılır;
/// tüm kitapçıklarda soru sayısı ve test sorusu sayısı aynı olduğundan 1. sayfa geometrisi ortaktır.
/// </summary>
public sealed class ExamPaperSpec
{
    public static IReadOnlyList<string> DefaultRules { get; } = new[]
    {
        "Cevaplarınızı yalnızca ilgili alanlara yazınız; test sorularında şıkkın yuvarlağını tamamen doldurunuz.",
        "Öğrenci numaranızı her kutuya bir rakam gelecek şekilde, rakamları kutunun içinde kalacak biçimde yazınız.",
        "Puan tablosu ve Toplam Not kutusu öğretim elemanı tarafından doldurulacaktır."
    };

    // ── Antet ──
    public string InstitutionName { get; init; } = string.Empty;

    /// <summary>Kurum logosu (PNG/JPEG). null ise logo basılmaz.</summary>
    public byte[]? InstitutionLogo { get; init; }

    public string CourseCode { get; init; } = string.Empty;
    public string CourseName { get; init; } = string.Empty;

    /// <summary>Başlık satırındaki sınav adı, ör. "1. VİZE SINAVI".</summary>
    public string ExamTitle { get; init; } = string.Empty;

    /// <summary>Ders tablosundaki sınav türü (Türkçe / İngilizce), ör. "Vize" / "Midterm".</summary>
    public string ExamTypeTr { get; init; } = string.Empty;
    public string ExamTypeEn { get; init; } = string.Empty;

    /// <summary>Ör. "2025 – 2026".</summary>
    public string AcademicYear { get; init; } = string.Empty;

    /// <summary>Ör. "GÜZ" / "FALL".</summary>
    public string SemesterTr { get; init; } = string.Empty;
    public string SemesterEn { get; init; } = string.Empty;

    public string Lecturer { get; init; } = string.Empty;
    public DateTime? ExamDate { get; init; }
    public int? DurationMinutes { get; init; }

    /// <summary>KURALLAR bölümündeki maddeler (en fazla 3 satır sığar; uzun metin küçültülür).</summary>
    public IReadOnlyList<string> Rules { get; init; } = DefaultRules;

    /// <summary>Soru metinleri kâğıda basılsın mı? false: yalnızca cevap alanları (cevap kâğıdı).</summary>
    public bool PrintQuestionTexts { get; init; } = true;

    // ── Sorular ──

    /// <summary>Kitapçık kodları (A, B, …). 1 ise kitapçık alanı basılmaz/okunmaz.</summary>
    public IReadOnlyList<string> BookletCodes { get; init; } = new[] { "A" };

    /// <summary>Her kitapçık için kâğıttaki sırasıyla sorular (BookletCodes ile aynı sırada).</summary>
    public IReadOnlyList<IReadOnlyList<PaperQuestion>> Booklets { get; init; } = Array.Empty<IReadOnlyList<PaperQuestion>>();

    public int QuestionCount => Booklets.Count == 0 ? 0 : Booklets[0].Count;
    public int MultipleChoiceCount => Booklets.Count == 0 ? 0 : Booklets[0].Count(q => q.IsMultipleChoice);
    public bool HasBookletSection => BookletCodes.Count > 1;
    public decimal TotalPoints => Booklets.Count == 0 ? 0 : Booklets[0].Sum(q => q.MaxPoints);

    /// <summary>Kitapçığın kâğıttaki soruları; kitapçık bilinmiyorsa (null) ilk kitapçık.</summary>
    public IReadOnlyList<PaperQuestion> QuestionsFor(int? bookletIndex)
        => Booklets[bookletIndex is int b && b >= 0 && b < Booklets.Count ? b : 0];

    /// <summary>Kâğıttaki k. sorunun okunabilecek en yüksek puanı; kitapçık bilinmiyorsa tüm kitapçıkların en büyüğü.</summary>
    public decimal MaxPointsAt(int position, int? bookletIndex)
        => bookletIndex is int b && b >= 0 && b < Booklets.Count
            ? Booklets[b][position].MaxPoints
            : Booklets.Max(q => q[position].MaxPoints);

    public void Validate()
    {
        if (Booklets.Count == 0 || Booklets[0].Count == 0)
            throw new ArgumentException("Kâğıtta basılacak soru yok.");
        if (Booklets.Count != Math.Max(1, BookletCodes.Count))
            throw new ArgumentException("Kitapçık kodları ile kitapçık soru listeleri uyuşmuyor.");
        if (Booklets.Any(b => b.Count != QuestionCount || b.Count(q => q.IsMultipleChoice) != MultipleChoiceCount))
            throw new ArgumentException("Kitapçıkların soru sayıları uyuşmuyor; kitapçıkları yeniden üretin.");
        _ = ExamPaperLayout.For(this); // kapasite denetimi
    }
}
