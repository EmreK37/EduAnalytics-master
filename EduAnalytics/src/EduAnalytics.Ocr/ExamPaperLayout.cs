namespace EduAnalytics.Ocr;

/// <summary>
/// Sınav kâğıdının 1. sayfasının mm cinsinden geometrisi (sürüm 3). Üst kısım (antet, öğrenci bilgileri,
/// Toplam Not kutusu, ders tablosu, kurallar) sabittir; puan tablosu ve test cevap alanı yalnızca soru
/// sayılarına göre büyür. Geometri yalnızca (soru sayısı, test sorusu sayısı, kitapçık sayısı) ile
/// belirlenir; antet metinleri (kurum, kurallar vb.) konumları DEĞİŞTİRMEZ. Böylece kâğıt basılırken
/// girilen bilgiler okuyucuya gerekmez: okuyucu aynı düzeni sınavın veritabanındaki sorularından kurar.
/// </summary>
public sealed class ExamPaperLayout
{
    private const double L = OpticalFormTemplate.ContentLeftMm;
    private const double W = OpticalFormTemplate.ContentWidthMm;

    public int QuestionCount { get; }
    public int MultipleChoiceCount { get; }
    public int BookletCount { get; }

    private ExamPaperLayout(int questionCount, int multipleChoiceCount, int bookletCount)
    {
        QuestionCount = questionCount;
        MultipleChoiceCount = multipleChoiceCount;
        BookletCount = bookletCount;
    }

    public static ExamPaperLayout For(ExamPaperSpec spec)
        => Create(spec.QuestionCount, spec.MultipleChoiceCount, spec.BookletCodes.Count);

    public static ExamPaperLayout Create(int questionCount, int multipleChoiceCount, int bookletCount)
    {
        if (questionCount < 1)
            throw new ArgumentException("Kâğıtta basılacak soru yok.", nameof(questionCount));
        if (questionCount > OpticalFormTemplate.MaxQuestions)
            throw new ArgumentException(
                $"Sınav kâğıdı en fazla {OpticalFormTemplate.MaxQuestions} soru destekler (istenen: {questionCount}).",
                nameof(questionCount));
        if (multipleChoiceCount < 0 || multipleChoiceCount > questionCount)
            throw new ArgumentException("Test sorusu sayısı geçersiz.", nameof(multipleChoiceCount));
        if (bookletCount > OpticalFormTemplate.MaxBooklets)
            throw new ArgumentException($"Sınav kâğıdı en fazla {OpticalFormTemplate.MaxBooklets} kitapçık destekler.",
                nameof(bookletCount));

        var layout = new ExamPaperLayout(questionCount, multipleChoiceCount, Math.Max(1, bookletCount));
        if (layout.FixedRegionBottom > OpticalFormTemplate.PageHeightMm - OpticalFormTemplate.FooterHeightMm - 2)
            throw new ArgumentException(
                $"{questionCount} soru ({multipleChoiceCount} test) birinci sayfaya sığmıyor; test sorusu sayısını azaltın.",
                nameof(multipleChoiceCount));
        return layout;
    }

    // ─────────────── Antet (sabit) ───────────────

    /// <summary>Kurum logosu + adı satırı.</summary>
    public static readonly MmRect InstitutionRow = new(L, 13.5, W, 17);

    /// <summary>Başlık + öğrenci bilgileri çerçevesi.</summary>
    public static readonly MmRect HeaderFrame = new(L, 32, W, 64);

    public static readonly MmRect TitleLine1 = new(20, 33.5, 170, 6.5);   // DERS KODU DERS ADI SINAV
    public static readonly MmRect TitleLine2 = new(20, 40.5, 170, 5);     // EĞİTİM-ÖĞRETİM YILI
    public static readonly MmRect TitleLine3 = new(20, 45.8, 170, 5);     // DÖNEM

    public static readonly MmRect StudentCaption = new(20, 53, 70, 5.5);

    public static readonly MmRect NameLabel = new(20, 59.5, 45, 6);
    /// <summary>Ad-soyadın yazıldığı alan (alt kenarında yazı çizgisi). İsteğe bağlı olarak OCR ile okunur.</summary>
    public static readonly MmRect NameArea = new(66, 57.5, 77, 8.5);

    public static readonly MmRect NumberLabel = new(20, 68.5, 30, 6.5);
    public const double NumberBoxesX = 51;
    public const double NumberBoxesY = 67;
    public const double NumberBoxWidth = 6.5;
    public const double NumberBoxHeight = 8.5;

    /// <summary>Öğrenci numarasının i. hanesinin kutusu (el yazısı, okunur).</summary>
    public static MmRect NumberCell(int index)
        => new(NumberBoxesX + index * NumberBoxWidth, NumberBoxesY, NumberBoxWidth, NumberBoxHeight);

    public static MmRect NumberArea
        => new(NumberBoxesX, NumberBoxesY, NumberBoxWidth * OpticalFormTemplate.StudentNumberCells, NumberBoxHeight);

    public static readonly MmRect DepartmentLabel = new(20, 78.5, 66, 6);
    public static readonly MmRect DepartmentArea = new(87, 77, 56, 7.5);

    public static readonly MmRect SignatureLabel = new(20, 87.5, 30, 6);
    public static readonly MmRect SignatureArea = new(51, 86, 50, 7.5);

    /// <summary>Sağdaki "Toplam Not – Paraf / Total Credit - Initials" kutusu.</summary>
    public static readonly MmRect TotalBox = new(147, 53, 44, 40);
    public static readonly MmRect TotalCaption = new(148, 53.6, 42, 7.2);
    public const double TotalDividerX = 174;
    public static readonly MmRect TotalScoreCaption = new(148.5, 61.4, 25, 4);
    /// <summary>Toplam notun yazıldığı alan (okunur, puan tablosuyla tutarlılık denetimi için).</summary>
    public static readonly MmRect TotalScoreArea = new(148.4, 65.6, 25.2, 26.4);
    public static readonly MmRect InitialsCaption = new(174.5, 61.4, 16, 4);
    public static readonly MmRect InitialsArea = new(174.6, 65.6, 16.2, 26.4);

    // ─────────────── Ders tablosu + kurallar (sabit) ───────────────

    public const double CourseTableTop = 98;
    public const double CourseRowHeight = 7;
    public static readonly MmRect CourseTable = new(L, CourseTableTop, W, CourseRowHeight * 3);
    public const double CourseFieldX = 36;
    public const double CourseValueX = 71;
    public const double ExamTypeX = 122;
    public const double DateX = 150;

    public static readonly MmRect CourseCaption = new(L, CourseTableTop, CourseFieldX - L, CourseRowHeight * 3);

    public static MmRect CourseFieldLabel(int row)
        => new(CourseFieldX + 1.5, CourseTableTop + row * CourseRowHeight, CourseValueX - CourseFieldX - 2, CourseRowHeight);

    public static MmRect CourseFieldValue(int row)
        => new(CourseValueX, CourseTableTop + row * CourseRowHeight, ExamTypeX - CourseValueX - 1.5, CourseRowHeight);

    public static readonly MmRect ExamTypeCell = new(ExamTypeX, CourseTableTop, DateX - ExamTypeX, CourseRowHeight * 3);
    public static readonly MmRect DateLabelCell = new(DateX, CourseTableTop, L + W - DateX, CourseRowHeight);
    public static readonly MmRect DateValueCell = new(DateX, CourseTableTop + CourseRowHeight, L + W - DateX, CourseRowHeight);
    public static readonly MmRect DurationCell = new(DateX, CourseTableTop + 2 * CourseRowHeight, L + W - DateX, CourseRowHeight);

    public static readonly MmRect RulesBox = new(L, CourseTableTop + CourseRowHeight * 3, W, 16);
    public static readonly MmRect RulesCaption = new(19, RulesBox.Y + 0.8, 80, 4.6);
    public static readonly MmRect RulesText = new(21, RulesBox.Y + 5.4, W - 6, 10.2);

    // ─────────────── Puan tablosu (Soru Numarası / Alınan Not) ───────────────

    public const double ScoreTableTop = 135;
    public const double ScoreTitleColumnWidth = 48;
    public const double ScoreNumberRowHeight = 6.5;
    public const double ScoreRowHeight = 11;
    public const double ScorePairHeight = ScoreNumberRowHeight + ScoreRowHeight;
    public const int MaxScoreColumns = 20;
    public const int MinScoreColumns = 10;

    /// <summary>Satır çifti (numara + puan) sayısı: her çift en fazla 20 soru.</summary>
    public int ScorePairs => (QuestionCount + MaxScoreColumns - 1) / MaxScoreColumns;

    /// <summary>Bir satırdaki kutu sayısı: tek satırda soru sayısı kadar (en az 10), birden çok satırda 20.</summary>
    public int ScoreColumns => QuestionCount > MaxScoreColumns
        ? MaxScoreColumns
        : Math.Max(QuestionCount, MinScoreColumns);

    public double ScoreCellWidth => (W - ScoreTitleColumnWidth) / ScoreColumns;

    public MmRect ScoreTableFrame => new(L, ScoreTableTop, W, ScorePairs * ScorePairHeight);

    /// <summary>Kâğıttaki k. sorunun (0-bazlı) numara hücresi.</summary>
    public MmRect ScoreNumberCell(int position)
    {
        var (pair, column) = (position / ScoreColumns, position % ScoreColumns);
        return new MmRect(L + ScoreTitleColumnWidth + column * ScoreCellWidth,
            ScoreTableTop + pair * ScorePairHeight, ScoreCellWidth, ScoreNumberRowHeight);
    }

    /// <summary>Kâğıttaki k. sorunun (0-bazlı) "Alınan Not" kutusu: öğretmen puanı buraya yazar (okunur).</summary>
    public MmRect ScoreCell(int position)
    {
        var number = ScoreNumberCell(position);
        return new MmRect(number.X, number.Bottom, number.Width, ScoreRowHeight);
    }

    /// <summary>Satır çiftinin başlık hücreleri (sol sütun).</summary>
    public MmRect ScoreTitleCell(int pair, bool scoreRow)
        => new(L, ScoreTableTop + pair * ScorePairHeight + (scoreRow ? ScoreNumberRowHeight : 0),
            ScoreTitleColumnWidth, scoreRow ? ScoreRowHeight : ScoreNumberRowHeight);

    public double ScoreTableBottom => ScoreTableTop + ScorePairs * ScorePairHeight;

    public MmRect NoteBox => new(L, ScoreTableBottom + 0.8, W, 4);

    // ─────────────── SORULAR + kitapçık + test cevap alanı ───────────────

    public MmRect QuestionsHeading => new(L, ScoreTableBottom + 6.2, 90, 6.5);

    public bool HasBookletSection => BookletCount > 1;

    public MmRect BookletLabel => new(118, QuestionsHeading.Y, 38, QuestionsHeading.Height);

    /// <summary>Kitapçık baloncuğu (1. sayfada basılan kitapçığınki dolu basılır).</summary>
    public MmPoint BookletBubbleCenter(int bookletIndex)
        => new(160 + bookletIndex * 7.5, QuestionsHeading.Center.Y);

    public const double AnswerRowPitch = 5.6;
    public const double AnswerColumnWidth = 35.2;
    public const double AnswerOptionPitch = 5.2;
    public const double AnswerFirstBubbleOffsetX = 12;
    public const double AnswerNumberOffsetX = 8.4;

    public int AnswerColumns => MultipleChoiceCount == 0 ? 0 : Math.Min(5, MultipleChoiceCount);
    public int AnswerRows => MultipleChoiceCount == 0 ? 0 : (MultipleChoiceCount + AnswerColumns - 1) / AnswerColumns;

    public MmRect AnswerCaption => new(L, QuestionsHeading.Bottom + 0.6, W, 4.4);
    public double AnswerLettersY => AnswerCaption.Bottom + 3.2;
    public double AnswerFirstRowY => AnswerCaption.Bottom + 8.4;

    /// <summary>i. test sorusunun (kâğıttaki test soruları sırasıyla, 0-bazlı) sütun/satırı (sütun öncelikli).</summary>
    public (int Column, int Row) AnswerPosition(int mcIndex) => (mcIndex / AnswerRows, mcIndex % AnswerRows);

    public double AnswerColumnX(int column) => L + column * AnswerColumnWidth;

    public MmPoint AnswerBubbleCenter(int mcIndex, int optionIndex)
    {
        var (column, row) = AnswerPosition(mcIndex);
        return new MmPoint(AnswerColumnX(column) + AnswerFirstBubbleOffsetX + optionIndex * AnswerOptionPitch,
            AnswerFirstRowY + row * AnswerRowPitch);
    }

    /// <summary>Soru numarasının sağa hizalı yazıldığı nokta (baloncuk satırının solu).</summary>
    public MmPoint AnswerNumberAnchor(int mcIndex)
    {
        var (column, row) = AnswerPosition(mcIndex);
        return new MmPoint(AnswerColumnX(column) + AnswerNumberOffsetX, AnswerFirstRowY + row * AnswerRowPitch);
    }

    public double AnswerAreaBottom => AnswerFirstRowY + (AnswerRows - 1) * AnswerRowPitch + OpticalFormTemplate.BubbleRadiusMm + 2;

    /// <summary>1. sayfada sabit düzenin bittiği y; soru metinleri bunun altından akar.</summary>
    public double FixedRegionBottom => (MultipleChoiceCount > 0 ? AnswerAreaBottom : QuestionsHeading.Bottom) + 2;
}
