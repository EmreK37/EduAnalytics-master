using System.Globalization;
using System.Text;

namespace EduAnalytics.Ocr;

/// <summary>
/// <see cref="ExamPaperFill"/>'i 1. sayfanın üstüne çizilecek tam sayfa SVG'ye çevirir: öğrencinin mavi
/// tükenmez kalemle yazdıkları (ad, numara, bölüm, imza), kurşun kalem cevap işaretleri ve öğretmenin
/// kırmızı kalemle yazdığı puanlar, toplam ve paraf. Rakamlar <see cref="SyntheticHandwriting"/> ile çizilir.
/// </summary>
internal static class ExamPaperFillRenderer
{
    private const string StudentInk = "#1f2a5c";
    private const string TeacherInk = "#c0392b";
    private const string PencilColor = "#262626";
    private const string HandwritingFont = "Segoe Print, Ink Free, Lato, Arial, sans-serif";

    public static string BuildSvg(ExamPaperLayout layout, ExamPaperFill fill)
    {
        var rng = new Random(fill.Seed);
        var student = SyntheticHandwriting.WriterStyle.Random(rng);
        var teacher = SyntheticHandwriting.WriterStyle.Random(new Random(fill.Seed * 31 + 7)) with { CrossedSeven = true };

        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" ")
          .Append($"width=\"{F(OpticalFormTemplate.PageWidthMm)}mm\" height=\"{F(OpticalFormTemplate.PageHeightMm)}mm\" ")
          .Append($"viewBox=\"0 0 {F(OpticalFormTemplate.PageWidthMm)} {F(OpticalFormTemplate.PageHeightMm)}\">");

        // ── Öğrenci ──
        if (!string.IsNullOrWhiteSpace(fill.StudentName))
            HandText(sb, ExamPaperLayout.NameArea, fill.StudentName, 4.6);

        var digits = (fill.StudentNumber ?? string.Empty).Where(char.IsDigit)
            .Take(OpticalFormTemplate.StudentNumberCells).ToArray();
        for (var i = 0; i < digits.Length; i++)
        {
            var cell = ExamPaperLayout.NumberCell(i);
            var height = 5.2 + rng.NextDouble() * 1.0;
            var ink = SyntheticHandwriting.Write(digits[i].ToString(), cell.Center.X + (rng.NextDouble() - 0.5) * 0.8,
                cell.Bottom - 1.3 - rng.NextDouble() * 0.5, height, student, rng);
            sb.Append(SyntheticHandwriting.ToSvg(ink, StudentInk));
        }

        if (!string.IsNullOrWhiteSpace(fill.Department))
            HandText(sb, ExamPaperLayout.DepartmentArea, fill.Department, 4.2);

        if (fill.Signed)
            sb.Append(SyntheticHandwriting.ToSvg(SyntheticHandwriting.Signature(ExamPaperLayout.SignatureArea.Deflate(1), student, rng), StudentInk));

        // ── Test cevapları (kurşun kalem) ──
        for (var row = 0; row < Math.Min(fill.Answers.Count, layout.MultipleChoiceCount); row++)
        {
            var weak = fill.WeakMarkRows.Contains(row);
            foreach (var option in fill.Answers[row].Where(o => o is >= 0 and < OpticalFormTemplate.OptionCount))
            {
                var c = layout.AnswerBubbleCenter(row, option);
                var r = weak ? OpticalFormTemplate.BubbleRadiusMm * 0.45 : OpticalFormTemplate.BubbleRadiusMm - 0.1;
                sb.Append($"<circle cx=\"{F(c.X)}\" cy=\"{F(c.Y)}\" r=\"{F(r)}\" fill=\"{PencilColor}\"/>");
            }
        }

        // ── Öğretmen: puan tablosu, toplam, paraf (kırmızı kalem) ──
        for (var position = 0; position < Math.Min(fill.Scores.Count, layout.QuestionCount); position++)
        {
            var text = fill.Scores[position];
            if (string.IsNullOrWhiteSpace(text))
                continue;
            var cell = layout.ScoreCell(position);
            WriteNumber(sb, text, cell, maxHeight: 6.2, teacher, rng);
        }

        if (!string.IsNullOrWhiteSpace(fill.Total))
            WriteNumber(sb, fill.Total, ExamPaperLayout.TotalScoreArea, maxHeight: 9, teacher, rng);

        if (fill.Initialed)
            sb.Append(SyntheticHandwriting.ToSvg(SyntheticHandwriting.Signature(ExamPaperLayout.InitialsArea.Deflate(3), teacher, rng), TeacherInk));

        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>Sayıyı kutunun ortasına, kutuya sığacak boyda el yazısıyla yazar.</summary>
    private static void WriteNumber(StringBuilder sb, string text, MmRect box, double maxHeight,
        SyntheticHandwriting.WriterStyle style, Random rng)
    {
        var height = Math.Min(maxHeight, box.Height - 4);
        var natural = SyntheticHandwriting.NaturalWidth(text, height, style);
        var available = box.Width - 1.4;
        if (natural > available)
            height = Math.Max(2.8, height * available / natural);

        var ink = SyntheticHandwriting.Write(text,
            box.Center.X + (rng.NextDouble() - 0.5) * 0.6,
            box.Center.Y + height / 2 + (rng.NextDouble() - 0.5) * 0.8,
            height, style, rng);
        sb.Append(SyntheticHandwriting.ToSvg(ink, TeacherInk));
    }

    private static void HandText(StringBuilder sb, MmRect area, string text, double sizeMm)
        => sb.Append($"<text x=\"{F(area.X + 2)}\" y=\"{F(area.Bottom - 1.4)}\" font-family=\"{HandwritingFont}\" " +
                     $"font-size=\"{F(sizeMm)}\" fill=\"{StudentInk}\">{ExamPaperPdfGenerator.Escape(text)}</text>");

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
