using System.Globalization;
using System.Windows.Data;
using EduAnalytics.Core.Enums;

namespace EduAnalytics.UI.Converters;

/// <summary>
/// ExamType değerini Türkçe etikete çevirir (Quiz / Vize / Final / Bütünleme).
/// </summary>
public class ExamTypeLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        ExamType? type = value switch
        {
            ExamType t => t,
            string s when Enum.TryParse<ExamType>(s, ignoreCase: true, out var parsed) => parsed,
            _ => null
        };

        return type switch
        {
            ExamType.Quiz => "Quiz",
            ExamType.Midterm => "Vize",
            ExamType.Final => "Final",
            ExamType.MakeUp => "Bütünleme",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// OptionLetter değerini gösterim metnine çevirir: A–E aynen, Empty → "Boş".
/// ConverterParameter "dash" verilirse Empty → "—" (ör. klasik sorunun doğru şıkkı yoktur).
/// </summary>
public class OptionLetterLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEmpty = value switch
        {
            OptionLetter.Empty => true,
            string s => string.IsNullOrWhiteSpace(s) || string.Equals(s, nameof(OptionLetter.Empty), StringComparison.OrdinalIgnoreCase),
            null => true,
            _ => false
        };

        if (!isEmpty)
            return value!.ToString() ?? string.Empty;

        return parameter is string p && string.Equals(p, "dash", StringComparison.OrdinalIgnoreCase) ? "—" : "Boş";
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
