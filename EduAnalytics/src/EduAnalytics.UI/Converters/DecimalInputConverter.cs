using System.Globalization;
using System.Windows;
using System.Windows.Data;
using EduAnalytics.Business.Helpers;

namespace EduAnalytics.UI.Converters;

/// <summary>
/// Puan ve sayı giriş kutuları için çevirici. WPF'in varsayılan çevirisi bölge ayarına göre okur:
/// "7,5" bir ayarda 75, "7.5" başka bir ayarda 75 olur. Bu çevirici <see cref="DecimalText"/> kuralını
/// kullanır: virgül de nokta da ondalık ayırıcıdır, binlik ayırıcı kabul edilmez.
/// <list type="bullet">
/// <item>Boş kutu: alan boş bırakılabiliyorsa (decimal?) null, değilse doğrulama hatası.</item>
/// <item>Geçersiz metin: doğrulama hatası (kutu kırmızı çerçevelenir), kaynak değişmez.</item>
/// </list>
/// </summary>
public sealed class DecimalInputConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        null => string.Empty,
        decimal d => d.ToString("0.##", culture),
        double x => x.ToString("0.##", culture),
        float f => f.ToString("0.##", culture),
        IFormattable formattable => formattable.ToString(null, culture),
        _ => value.ToString() ?? string.Empty
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!DecimalText.TryParse(value as string, out var parsed))
            return DependencyProperty.UnsetValue;

        var underlying = Nullable.GetUnderlyingType(targetType);
        if (parsed is not decimal number)
            return underlying != null ? null : DependencyProperty.UnsetValue;

        var type = underlying ?? targetType;
        if (type == typeof(decimal)) return number;
        if (type == typeof(double)) return (double)number;
        if (type == typeof(float)) return (float)number;
        return System.Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
    }
}
