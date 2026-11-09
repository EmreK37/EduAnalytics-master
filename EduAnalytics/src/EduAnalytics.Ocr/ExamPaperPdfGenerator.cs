using System.Globalization;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EduAnalytics.Ocr;

/// <summary>
/// Resmi üniversite sınav kâğıdı düzeninde (sürüm 3) yazdırılabilir PDF üretir.
///
/// Çizim iki katmandır:
///   • Arka plan SVG'si (1 SVG birimi = 1 mm): köşe işaretleri, sayfa kodu, tüm çizgi/kutu/baloncuklar ve
///     puan tablosundaki soru numaraları. Okunan her alanın konumu <see cref="ExamPaperLayout"/>'tan gelir;
///     böylece basılan ile okunan birebir eşleşir.
///   • Metinler (antet, etiketler, ders bilgileri, kurallar, sorular) QuestPDF düzeniyle, sabit kutuların
///     içine sığacak biçimde (gerekirse küçültülerek) yazılır. Metin uzunluğu hiçbir okunan alanı kaydırmaz.
/// 1. sayfa sabit düzendir; soru metinleri test cevap alanının altından başlayıp devam sayfalarına akar.
/// İsteğe bağlı <see cref="ExamPaperFill"/> ile kâğıdın doldurulmuş hâli çizilir (örnek/test kâğıtları).
/// </summary>
public static class ExamPaperPdfGenerator
{
    private const string InkColor = "#1a1a1a";
    private const string LightColor = "#9aa0a6";
    private const string FaintFill = "#eef0f2";
    private const string SvgFont = "Lato, Arial, sans-serif";
    private static readonly string[] SerifFonts = { "Times New Roman", "Lato" };
    private static readonly string[] SansFonts = { "Calibri", "Lato" };
    private static readonly CultureInfo Turkish = new("tr-TR");

    /// <summary>Basılacak kâğıdın PDF'i (<paramref name="bookletIndex"/>: hangi kitapçığın basılacağı).</summary>
    public static byte[] GeneratePdf(ExamPaperSpec spec, int bookletIndex = 0, ExamPaperFill? fill = null)
        => CreateDocument(spec, bookletIndex, fill).GeneratePdf();

    /// <summary>Kâğıdın tüm sayfalarını PNG olarak üretir (önizleme ve örnek taramalar için).</summary>
    public static IReadOnlyList<byte[]> RenderPngPages(ExamPaperSpec spec, int bookletIndex = 0, ExamPaperFill? fill = null, int dpi = 200)
        => CreateDocument(spec, bookletIndex, fill)
            .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = dpi })
            .ToList();

    private static Document CreateDocument(ExamPaperSpec spec, int bookletIndex, ExamPaperFill? fill)
    {
        spec.Validate();
        if (bookletIndex < 0 || bookletIndex >= spec.Booklets.Count)
            throw new ArgumentOutOfRangeException(nameof(bookletIndex), "Kitapçık bulunamadı.");

        QuestPDF.Settings.License = LicenseType.Community;

        var layout = ExamPaperLayout.For(spec);
        var questions = spec.Booklets[bookletIndex];
        var firstPageSvg = BuildFirstPageSvg(spec, layout, questions, bookletIndex);
        var continuationSvg = BuildContinuationSvg();
        var fillSvg = fill != null ? ExamPaperFillRenderer.BuildSvg(layout, fill) : null;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(t => t.FontFamily(SansFonts).FontSize(10.5f).FontColor(InkColor));

                page.Background().Column(col =>
                {
                    col.Item().ShowIf(ctx => ctx.PageNumber == 1).Svg(firstPageSvg);
                    col.Item().ShowIf(ctx => ctx.PageNumber > 1).Svg(continuationSvg);
                });

                if (fillSvg != null)
                    page.Foreground().ShowIf(ctx => ctx.PageNumber == 1).Svg(fillSvg);

                page.Header().Column(col =>
                {
                    col.Item().ShowOnce()
                        .Height((float)layout.FixedRegionBottom, Unit.Millimetre)
                        .Layers(layers => ComposeFirstPageTexts(layers, spec, layout, bookletIndex));
                    col.Item().SkipOnce()
                        .Height((float)OpticalFormTemplate.ContinuationHeaderHeightMm, Unit.Millimetre)
                        .Element(c => ComposeContinuationHeader(c, spec, bookletIndex));
                });

                page.Content()
                    .PaddingHorizontal((float)OpticalFormTemplate.ContentLeftMm, Unit.Millimetre)
                    .Element(c => ComposeQuestions(c, spec, questions));

                page.Footer()
                    .Height((float)OpticalFormTemplate.FooterHeightMm, Unit.Millimetre)
                    .PaddingTop(9, Unit.Millimetre)
                    .AlignCenter()
                    .Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                        t.Span("Sayfa ");
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
            });
        });
    }

    // ─────────────────────────── 1. sayfa metinleri ───────────────────────────

    private static void ComposeFirstPageTexts(LayersDescriptor layers, ExamPaperSpec spec, ExamPaperLayout layout, int bookletIndex)
    {
        layers.PrimaryLayer().Extend();

        // Kurum logosu + adı
        Place(layers, ExamPaperLayout.InstitutionRow, c => c.AlignCenter().AlignMiddle().ScaleToFit().Row(row =>
        {
            var hasLogo = spec.InstitutionLogo is { Length: > 0 };
            if (hasLogo)
                row.AutoItem().Height(15, Unit.Millimetre).Image(spec.InstitutionLogo!).FitHeight();
            row.AutoItem().PaddingLeft(hasLogo ? 5 : 0, Unit.Millimetre).AlignMiddle()
                .Text(Upper(spec.InstitutionName)).FontFamily("Arial", "Lato").Bold().FontSize(21);
        }));

        // Başlık: DERS KODU + DERS ADI + SINAV; eğitim-öğretim yılı; dönem
        var title = string.Join(" ", new[] { spec.CourseCode.Replace(" ", ""), spec.CourseName, spec.ExamTitle }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        Place(layers, ExamPaperLayout.TitleLine1, c => c.AlignCenter().AlignMiddle().Element(OneLine)
            .Text(Upper(title)).FontFamily(SerifFonts).Bold().FontSize(13.5f));

        if (!string.IsNullOrWhiteSpace(spec.AcademicYear))
        {
            var yearEn = spec.AcademicYear.Replace(" ", "").Replace("–", "-");
            Place(layers, ExamPaperLayout.TitleLine2, c => c.AlignCenter().AlignMiddle().Element(OneLine).Text(t =>
            {
                t.DefaultTextStyle(s => s.FontFamily(SerifFonts));
                t.Span($"{spec.AcademicYear} EĞİTİM – ÖĞRETİM YILI").Bold().FontSize(12);
                t.Span($" / {yearEn} ACADEMIC YEAR").FontSize(9);
            }));
        }

        if (!string.IsNullOrWhiteSpace(spec.SemesterTr))
        {
            Place(layers, ExamPaperLayout.TitleLine3, c => c.AlignCenter().AlignMiddle().Element(OneLine).Text(t =>
            {
                t.DefaultTextStyle(s => s.FontFamily(SerifFonts));
                t.Span($"{Upper(spec.SemesterTr)} DÖNEMİ").Bold().FontSize(12);
                t.Span($" / {spec.SemesterEn.ToUpperInvariant()} SEMESTER".Replace("  ", " ")).FontSize(9);
            }));
        }

        // Öğrenci bilgileri
        Label(layers, ExamPaperLayout.StudentCaption, "Öğrencinin", "Student's", 12.5f);
        Label(layers, ExamPaperLayout.NameLabel, "Adı Soyadı", "Name, Surname :", 12);
        Label(layers, ExamPaperLayout.NumberLabel, "Numarası", "Number :", 12);
        Label(layers, ExamPaperLayout.DepartmentLabel, "Bölüm - Program", "Department-Programme :", 12);
        Label(layers, ExamPaperLayout.SignatureLabel, "İmzası", "Signature :", 12);

        // Toplam Not – Paraf
        Place(layers, ExamPaperLayout.TotalCaption, c => c.AlignCenter().AlignMiddle().ScaleToFit().Column(col =>
        {
            col.Item().AlignCenter().Text("Toplam Not – Paraf").FontFamily(SerifFonts).Bold().FontSize(10);
            col.Item().AlignCenter().Text("Total Credit - Initials").FontFamily(SerifFonts).FontSize(8);
        }));
        Caption(layers, ExamPaperLayout.TotalScoreCaption, "Not / Credit");
        Caption(layers, ExamPaperLayout.InitialsCaption, "Paraf / Initials");

        // Ders tablosu
        Place(layers, ExamPaperLayout.CourseCaption, c => c.AlignCenter().AlignMiddle().Column(col =>
        {
            col.Item().AlignCenter().Text("DERS").FontFamily(SerifFonts).Bold().FontSize(12);
            col.Item().AlignCenter().Text("Course").FontFamily(SerifFonts).Italic().FontSize(9.5f);
        }));
        string[] fieldTr = { "Adı", "Kodu", "Sorumlusu" };
        string[] fieldEn = { "Name", "Code", "Lecturer" };
        string[] values = { spec.CourseName, spec.CourseCode, spec.Lecturer };
        for (var row = 0; row < 3; row++)
        {
            Label(layers, ExamPaperLayout.CourseFieldLabel(row), fieldTr[row], fieldEn[row], 11);
            var value = values[row];
            Place(layers, ExamPaperLayout.CourseFieldValue(row), c => c.AlignLeft().AlignMiddle().Element(OneLine)
                .Text($": {value}").FontFamily(SerifFonts).Italic().FontSize(11.5f));
        }

        Place(layers, ExamPaperLayout.ExamTypeCell, c => c.AlignCenter().AlignMiddle().ScaleToFit().Column(col =>
        {
            col.Item().AlignCenter().Text(spec.ExamTypeTr).FontFamily(SerifFonts).Bold().FontSize(12);
            col.Item().AlignCenter().Text(spec.ExamTypeEn).FontFamily(SerifFonts).Italic().FontSize(9.5f);
        }));
        Label(layers, Inset(ExamPaperLayout.DateLabelCell, 1.5), "Sınav Tarihi", "Date :", 11);
        if (spec.ExamDate.HasValue)
        {
            Place(layers, Inset(ExamPaperLayout.DateValueCell, 1.5), c => c.AlignRight().AlignMiddle()
                .Text(spec.ExamDate.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)).FontFamily(SerifFonts).FontSize(11.5f));
        }
        if (spec.DurationMinutes is > 0)
        {
            Place(layers, Inset(ExamPaperLayout.DurationCell, 1.5), c => c.AlignLeft().AlignMiddle().Element(OneLine).Text(t =>
            {
                t.DefaultTextStyle(s => s.FontFamily(SerifFonts));
                t.Span("Süre").Bold().FontSize(11);
                t.Span(" / ").FontSize(9);
                t.Span("Duration :").Italic().FontSize(8.5f);
                t.Span($"  {spec.DurationMinutes} dk").FontSize(11);
            }));
        }

        // Kurallar
        Label(layers, ExamPaperLayout.RulesCaption, "KURALLAR", "Rules :", 11);
        var rules = spec.Rules.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (rules.Count > 0)
        {
            Place(layers, ExamPaperLayout.RulesText, c => c.AlignTop().ScaleToFit().Column(col =>
            {
                foreach (var rule in rules)
                {
                    col.Item().Row(row =>
                    {
                        row.ConstantItem(5, Unit.Millimetre).Text("•").FontFamily(SerifFonts).FontSize(10);
                        row.RelativeItem().Text(rule).FontFamily(SerifFonts).FontSize(10);
                    });
                }
            }));
        }

        // Puan tablosu başlıkları
        for (var pair = 0; pair < layout.ScorePairs; pair++)
        {
            Label(layers, Inset(layout.ScoreTitleCell(pair, scoreRow: false), 1.2), "Soru Numarası", "Numbers of the Questions", 10.5f);
            Label(layers, Inset(layout.ScoreTitleCell(pair, scoreRow: true), 1.2), "Alınan Not", "Scored Points", 11.5f);
        }
        Place(layers, layout.NoteBox, c => c.AlignLeft().AlignMiddle().Element(OneLine)
            .Text("*Her sorunun puan değeri rakamsal olarak yanına belirtilmelidir. / The points for each question must be stated next to the question.")
            .Italic().FontSize(7.5f));

        // SORULAR + kitapçık + test cevap alanı açıklaması
        Place(layers, layout.QuestionsHeading, c => c.AlignLeft().AlignMiddle().Text(t =>
        {
            t.Span("SORULAR").FontSize(12.5f);
            t.Span(" / Questions").FontSize(11);
        }));

        if (layout.HasBookletSection)
        {
            Place(layers, layout.BookletLabel, c => c.AlignRight().AlignMiddle().PaddingRight(2, Unit.Millimetre).Element(OneLine).Text(t =>
            {
                t.Span("Kitapçık").Bold().FontSize(10.5f);
                t.Span(" / Booklet :").Italic().FontSize(8.5f);
            }));
        }

        if (layout.MultipleChoiceCount > 0)
        {
            Place(layers, layout.AnswerCaption, c => c.AlignLeft().AlignMiddle().Element(OneLine)
                .Text("Test soruları: cevabınızı, soru numarasının yanındaki şıkkın yuvarlağını tamamen doldurarak işaretleyiniz.")
                .FontSize(8.5f).FontColor(Colors.Grey.Darken2));
        }
    }

    private static void ComposeContinuationHeader(IContainer container, ExamPaperSpec spec, int bookletIndex)
    {
        container.PaddingHorizontal((float)OpticalFormTemplate.ContentLeftMm, Unit.Millimetre)
            .PaddingTop(15.5f, Unit.Millimetre)
            .Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8.5f));
                    t.Span("Adı Soyadı: ").Bold();
                    t.Span("..........................................   ");
                    t.Span("Numarası: ").Bold();
                    t.Span("..................................");
                });
                var info = string.Join(" • ", new[]
                {
                    $"{spec.CourseCode} {spec.ExamTitle}".Trim(),
                    spec.HasBookletSection ? $"Kitapçık {spec.BookletCodes[bookletIndex]}" : null
                }.Where(s => !string.IsNullOrWhiteSpace(s)));
                row.AutoItem().AlignRight().Text(info).FontSize(8.5f).FontColor(Colors.Grey.Darken1);
            });
    }

    // ─────────────────────────── Sorular (akan içerik) ───────────────────────────

    private static void ComposeQuestions(IContainer container, ExamPaperSpec spec, IReadOnlyList<PaperQuestion> questions)
    {
        container.PaddingTop(2, Unit.Millimetre).Column(col =>
        {
            col.Spacing(4.5f, Unit.Millimetre);
            foreach (var q in questions)
            {
                if (spec.PrintQuestionTexts)
                    col.Item().Element(c => ComposeQuestion(c, q));
                else if (!q.IsMultipleChoice)
                    col.Item().Element(c => ComposeAnswerSpace(c, q, withHeading: true));
            }
        });
    }

    private static void ComposeQuestion(IContainer container, PaperQuestion q)
    {
        container.EnsureSpace(MmToPoints(q.IsMultipleChoice ? 35 : 25)).Column(col =>
        {
            col.Item().Text(t =>
            {
                t.Span($"{q.Number}) ").Bold();
                t.Span(q.Text);
                t.Span($"  ({FormatPoints(q.MaxPoints)} puan)").Bold().FontColor(Colors.Grey.Darken2);
            });

            if (q.IsMultipleChoice)
            {
                var options = q.Options ?? Array.Empty<string>();
                for (var i = 0; i < Math.Min(options.Count, OpticalFormTemplate.OptionCount); i++)
                {
                    if (string.IsNullOrWhiteSpace(options[i]))
                        continue;
                    var letter = (char)('A' + i);
                    var text = options[i];
                    col.Item().PaddingLeft(5, Unit.Millimetre).PaddingTop(1, Unit.Millimetre).Text($"{letter}) {text}");
                }
            }
            else
            {
                col.Item().PaddingTop(2, Unit.Millimetre).Element(c => ComposeAnswerSpace(c, q, withHeading: false));
            }
        });
    }

    /// <summary>Klasik soru için cevap alanı; yüksekliği sorunun puanıyla orantılıdır.</summary>
    private static void ComposeAnswerSpace(IContainer container, PaperQuestion q, bool withHeading)
    {
        var height = (float)Math.Clamp(25 + (double)q.MaxPoints * 1.6, 35, 110);
        container.Column(col =>
        {
            if (withHeading)
            {
                col.Item().PaddingBottom(1.5f, Unit.Millimetre).Text(t =>
                {
                    t.Span($"{q.Number}) ").Bold();
                    t.Span("Cevap alanı").FontColor(Colors.Grey.Darken2);
                    t.Span($"  ({FormatPoints(q.MaxPoints)} puan)").Bold().FontColor(Colors.Grey.Darken2);
                });
            }
            col.Item().Height(height, Unit.Millimetre)
                .Border(0.7f).BorderColor(Colors.Grey.Lighten1)
                .Padding(1.5f, Unit.Millimetre)
                .Text("Cevap / Answer").FontSize(7).FontColor(Colors.Grey.Lighten1);
        });
    }

    // ─────────────────────────── Arka plan SVG'leri ───────────────────────────

    private static string BuildFirstPageSvg(ExamPaperSpec spec, ExamPaperLayout layout, IReadOnlyList<PaperQuestion> questions, int bookletIndex)
    {
        var sb = SvgStart();
        DrawMarkers(sb);
        DrawPageCode(sb, firstPage: true);

        // Antet çerçevesi + yazı çizgileri + numara kutuları
        Rect(sb, ExamPaperLayout.HeaderFrame, 0.4);
        WritingLine(sb, ExamPaperLayout.NameArea);
        for (var i = 0; i < OpticalFormTemplate.StudentNumberCells; i++)
            Rect(sb, ExamPaperLayout.NumberCell(i), 0.3);
        WritingLine(sb, ExamPaperLayout.DepartmentArea);
        WritingLine(sb, ExamPaperLayout.SignatureArea);

        // Toplam Not – Paraf kutusu
        var total = ExamPaperLayout.TotalBox;
        Rect(sb, total, 0.4);
        Line(sb, total.X, ExamPaperLayout.TotalScoreCaption.Y - 0.3, total.Right, ExamPaperLayout.TotalScoreCaption.Y - 0.3, 0.2, LightColor);
        Line(sb, ExamPaperLayout.TotalDividerX, ExamPaperLayout.TotalScoreCaption.Y - 0.3, ExamPaperLayout.TotalDividerX, total.Bottom, 0.2, LightColor);

        // Ders tablosu + kurallar
        var course = ExamPaperLayout.CourseTable;
        Rect(sb, course, 0.4);
        Line(sb, ExamPaperLayout.CourseFieldX, course.Y, ExamPaperLayout.CourseFieldX, course.Bottom, 0.3);
        Line(sb, ExamPaperLayout.ExamTypeX, course.Y, ExamPaperLayout.ExamTypeX, course.Bottom, 0.3);
        Line(sb, ExamPaperLayout.DateX, course.Y, ExamPaperLayout.DateX, course.Bottom, 0.3);
        for (var row = 1; row < 3; row++)
        {
            var y = course.Y + row * ExamPaperLayout.CourseRowHeight;
            Line(sb, ExamPaperLayout.CourseFieldX, y, ExamPaperLayout.ExamTypeX, y, 0.3);
            Line(sb, ExamPaperLayout.DateX, y, course.Right, y, 0.3);
        }
        Rect(sb, ExamPaperLayout.RulesBox, 0.4);

        DrawScoreTable(sb, layout);

        if (layout.HasBookletSection)
        {
            for (var b = 0; b < spec.BookletCodes.Count; b++)
            {
                var center = layout.BookletBubbleCenter(b);
                if (b == bookletIndex)
                {
                    // Basılan kitapçığın baloncuğu dolu basılır; öğrencinin işaretlemesi gerekmez.
                    sb.Append($"<circle cx=\"{F(center.X)}\" cy=\"{F(center.Y)}\" r=\"{F(OpticalFormTemplate.BubbleRadiusMm)}\" fill=\"{InkColor}\"/>");
                    Text(sb, center.X, center.Y + 1.0, Escape(spec.BookletCodes[b]), 2.6, bold: true, anchor: "middle", color: "#ffffff");
                }
                else
                {
                    Bubble(sb, center, spec.BookletCodes[b]);
                }
            }
        }

        DrawAnswerArea(sb, layout, questions);

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void DrawScoreTable(StringBuilder sb, ExamPaperLayout layout)
    {
        var frame = layout.ScoreTableFrame;
        Rect(sb, frame, 0.45);
        var firstCellX = frame.X + ExamPaperLayout.ScoreTitleColumnWidth;
        Line(sb, firstCellX, frame.Y, firstCellX, frame.Bottom, 0.35);

        for (var pair = 0; pair < layout.ScorePairs; pair++)
        {
            var top = ExamPaperLayout.ScoreTableTop + pair * ExamPaperLayout.ScorePairHeight;
            if (pair > 0)
                Line(sb, frame.X, top, frame.Right, top, 0.45);
            Line(sb, frame.X, top + ExamPaperLayout.ScoreNumberRowHeight, frame.Right, top + ExamPaperLayout.ScoreNumberRowHeight, 0.3);

            for (var column = 1; column < layout.ScoreColumns; column++)
            {
                var x = firstCellX + column * layout.ScoreCellWidth;
                Line(sb, x, top, x, top + ExamPaperLayout.ScorePairHeight, 0.3);
            }
        }

        var capacity = layout.ScorePairs * layout.ScoreColumns;
        for (var position = 0; position < capacity; position++)
        {
            var numberCell = layout.ScoreNumberCell(position);
            var scoreCell = layout.ScoreCell(position);
            if (position < layout.QuestionCount)
            {
                Text(sb, numberCell.Center.X, numberCell.Y + numberCell.Height * 0.72,
                    (position + 1).ToString(CultureInfo.InvariantCulture), 3.2, anchor: "middle");
            }
            else
            {
                // Sınavda olmayan sütunlar gri: öğretmen buraya yazmaz, okuyucu bakmaz.
                sb.Append($"<rect x=\"{F(numberCell.X + 0.2)}\" y=\"{F(numberCell.Y + 0.2)}\" width=\"{F(numberCell.Width - 0.4)}\" " +
                          $"height=\"{F(numberCell.Height + scoreCell.Height - 0.4)}\" fill=\"{FaintFill}\"/>");
            }
        }
    }

    private static void DrawAnswerArea(StringBuilder sb, ExamPaperLayout layout, IReadOnlyList<PaperQuestion> questions)
    {
        if (layout.MultipleChoiceCount == 0)
            return;

        for (var column = 0; column < layout.AnswerColumns; column++)
        {
            for (var opt = 0; opt < OpticalFormTemplate.OptionCount; opt++)
            {
                var x = layout.AnswerColumnX(column) + ExamPaperLayout.AnswerFirstBubbleOffsetX + opt * ExamPaperLayout.AnswerOptionPitch;
                Text(sb, x, layout.AnswerLettersY, ((char)('A' + opt)).ToString(), 2.6, bold: true, anchor: "middle", color: LightColor);
            }
        }

        var mcNumbers = questions.Where(q => q.IsMultipleChoice).Select(q => q.Number).ToList();
        for (var i = 0; i < mcNumbers.Count; i++)
        {
            var anchor = layout.AnswerNumberAnchor(i);
            Text(sb, anchor.X, anchor.Y + 1.05, mcNumbers[i].ToString(CultureInfo.InvariantCulture), 3, bold: true, anchor: "end");
            for (var opt = 0; opt < OpticalFormTemplate.OptionCount; opt++)
                Bubble(sb, layout.AnswerBubbleCenter(i, opt), ((char)('A' + opt)).ToString());
        }
    }

    private static string BuildContinuationSvg()
    {
        var sb = SvgStart();
        DrawMarkers(sb);
        DrawPageCode(sb, firstPage: false);
        Line(sb, OpticalFormTemplate.ContentLeftMm, 21.5, OpticalFormTemplate.ContentRightMm, 21.5, 0.25, LightColor);
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static StringBuilder SvgStart()
    {
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" ")
          .Append($"width=\"{F(OpticalFormTemplate.PageWidthMm)}mm\" height=\"{F(OpticalFormTemplate.PageHeightMm)}mm\" ")
          .Append($"viewBox=\"0 0 {F(OpticalFormTemplate.PageWidthMm)} {F(OpticalFormTemplate.PageHeightMm)}\">");
        return sb;
    }

    private static void DrawMarkers(StringBuilder sb)
    {
        var half = OpticalFormTemplate.MarkerSizeMm / 2;
        foreach (var c in OpticalFormTemplate.MarkerCenters)
        {
            sb.Append($"<rect x=\"{F(c.X - half)}\" y=\"{F(c.Y - half)}\" width=\"{F(OpticalFormTemplate.MarkerSizeMm)}\" " +
                      $"height=\"{F(OpticalFormTemplate.MarkerSizeMm)}\" fill=\"{InkColor}\"/>");
        }
    }

    /// <summary>Yön karesi her sayfada dolu; "ilk sayfa" karesi yalnızca 1. sayfada dolu, diğerlerinde boş çerçeve.</summary>
    private static void DrawPageCode(StringBuilder sb, bool firstPage)
    {
        var half = OpticalFormTemplate.PageCodeSizeMm / 2;
        var size = F(OpticalFormTemplate.PageCodeSizeMm);
        var o = OpticalFormTemplate.OrientationSlot;
        var p = OpticalFormTemplate.FirstPageSlot;
        sb.Append($"<rect x=\"{F(o.X - half)}\" y=\"{F(o.Y - half)}\" width=\"{size}\" height=\"{size}\" fill=\"{InkColor}\"/>");
        sb.Append(firstPage
            ? $"<rect x=\"{F(p.X - half)}\" y=\"{F(p.Y - half)}\" width=\"{size}\" height=\"{size}\" fill=\"{InkColor}\"/>"
            : $"<rect x=\"{F(p.X - half)}\" y=\"{F(p.Y - half)}\" width=\"{size}\" height=\"{size}\" fill=\"none\" stroke=\"{InkColor}\" stroke-width=\"0.25\"/>");
    }

    // ─────────────────────────── Yardımcılar ───────────────────────────

    /// <summary>
    /// Metni tek satırda tutar: satır kutuya sığmazsa kaydırılmaz, bütün olarak küçültülür
    /// (resmi kâğıttaki tek satırlık etiket ve başlıklar gibi).
    /// </summary>
    private static IContainer OneLine(IContainer container)
    {
        IContainer content = null!;
        container.ScaleToFit().Row(row => content = row.AutoItem());
        return content;
    }

    private static void Place(LayersDescriptor layers, MmRect r, Action<IContainer> content)
        => content(layers.Layer()
            .PaddingLeft((float)r.X, Unit.Millimetre)
            .PaddingTop((float)r.Y, Unit.Millimetre)
            .Width((float)r.Width, Unit.Millimetre)
            .Height((float)r.Height, Unit.Millimetre));

    /// <summary>"Türkçe / English" biçimli iki dilli etiket (Türkçe kalın, İngilizce italik).</summary>
    private static void Label(LayersDescriptor layers, MmRect r, string tr, string en, float size)
        => Place(layers, r, c => c.AlignLeft().AlignMiddle().Element(OneLine).Text(t =>
        {
            t.DefaultTextStyle(s => s.FontFamily(SerifFonts));
            t.Span(tr).Bold().FontSize(size);
            t.Span("  / ").FontSize(size - 2);
            t.Span(en).Italic().FontSize(size - 2.5f);
        }));

    private static void Caption(LayersDescriptor layers, MmRect r, string text)
        => Place(layers, r, c => c.AlignCenter().AlignMiddle().Element(OneLine)
            .Text(text).FontSize(6.5f).FontColor(Colors.Grey.Darken1));

    private static MmRect Inset(MmRect r, double dx) => new(r.X + dx, r.Y, r.Width - 2 * dx, r.Height);

    private static void WritingLine(StringBuilder sb, MmRect area)
        => sb.Append($"<line x1=\"{F(area.X)}\" y1=\"{F(area.Bottom)}\" x2=\"{F(area.Right)}\" y2=\"{F(area.Bottom)}\" " +
                     $"stroke=\"{LightColor}\" stroke-width=\"0.25\" stroke-dasharray=\"0.6 0.6\"/>");

    private static void Bubble(StringBuilder sb, MmPoint center, string label)
    {
        sb.Append($"<circle cx=\"{F(center.X)}\" cy=\"{F(center.Y)}\" r=\"{F(OpticalFormTemplate.BubbleRadiusMm)}\" " +
                  $"fill=\"none\" stroke=\"{InkColor}\" stroke-width=\"0.25\"/>");
        Text(sb, center.X, center.Y + OpticalFormTemplate.BubbleRadiusMm * 0.45, Escape(label),
            OpticalFormTemplate.BubbleRadiusMm * 1.2, anchor: "middle", color: LightColor);
    }

    private static void Rect(StringBuilder sb, MmRect r, double strokeWidth, string color = InkColor)
        => sb.Append($"<rect x=\"{F(r.X)}\" y=\"{F(r.Y)}\" width=\"{F(r.Width)}\" height=\"{F(r.Height)}\" " +
                     $"fill=\"none\" stroke=\"{color}\" stroke-width=\"{F(strokeWidth)}\"/>");

    private static void Line(StringBuilder sb, double x1, double y1, double x2, double y2, double width, string color = InkColor)
        => sb.Append($"<line x1=\"{F(x1)}\" y1=\"{F(y1)}\" x2=\"{F(x2)}\" y2=\"{F(y2)}\" stroke=\"{color}\" stroke-width=\"{F(width)}\"/>");

    private static void Text(StringBuilder sb, double x, double y, string content, double sizeMm,
        bool bold = false, string anchor = "start", string color = InkColor)
        => sb.Append($"<text x=\"{F(x)}\" y=\"{F(y)}\" font-family=\"{SvgFont}\" font-size=\"{F(sizeMm)}\" " +
                     $"fill=\"{color}\" text-anchor=\"{anchor}\"{(bold ? " font-weight=\"bold\"" : "")}>{content}</text>");

    private static float MmToPoints(double mm) => (float)(mm / 25.4 * 72);

    public static string FormatPoints(decimal points) => points.ToString("0.##", Turkish);

    private static string Upper(string s) => s.ToUpper(Turkish);

    internal static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    internal static string Escape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
