using System.Globalization;
using System.Windows.Data;
using EduAnalytics.Core.Enums;

namespace EduAnalytics.UI.Converters;

/// <summary>
/// QuestionType değerini kullanıcıya gösterilecek Türkçe etikete çevirir (Test / Klasik).
/// Enum olarak ya da metin olarak ("MultipleChoice" / "OpenEnded") gelen değerleri destekler.
/// </summary>
public class QuestionTypeLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        QuestionType.MultipleChoice => "Test",
        QuestionType.OpenEnded => "Klasik",
        string s when Enum.TryParse<QuestionType>(s, ignoreCase: true, out var parsed)
            => parsed == QuestionType.MultipleChoice ? "Test" : "Klasik",
        null => string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
