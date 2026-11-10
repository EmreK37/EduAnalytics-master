using System.Globalization;

namespace EduAnalytics.Business.Helpers;

/// <summary>
/// Kullanıcının yazdığı ya da dosyadan gelen puan metinlerini okur ve biçimlendirir.
/// <para>
/// Kural: virgül de nokta da ondalık ayırıcıdır ("7,5" = "7.5" = 7,5). Binlik ayırıcı kabul edilmez;
/// böylece bir kültürde ondalık olan işaret başka bir kültürde binlik sanılıp puan 10 ya da 100 katına
/// çıkmaz ("7,5" → 75, "26,00" → 2600 gibi). Puanlar negatif olamaz.
/// </para>
/// </summary>
public static class DecimalText
{
    /// <summary>Puanların saklandığı ondalık basamak sayısı (veritabanında decimal(6,2)).</summary>
    public const int Decimals = 2;

    /// <summary>
    /// Metni puana çevirir. Boş metin geçerlidir ve <paramref name="value"/> null olur.
    /// İşaret, harf, birden fazla ayırıcı ("1.234,5") ya da rakamsız metin geçersizdir.
    /// </summary>
    public static bool TryParse(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var separators = 0;
        var digits = 0;
        var buffer = new char[text.Length];
        var length = 0;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
                continue;

            if (c is >= '0' and <= '9')
            {
                digits++;
                buffer[length++] = c;
            }
            else if (c is ',' or '.')
            {
                if (++separators > 1)
                    return false;
                buffer[length++] = '.';
            }
            else
            {
                return false;
            }
        }

        if (digits == 0)
            return false;

        if (!decimal.TryParse(new string(buffer, 0, length), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var parsed))
            return false;

        value = Math.Round(parsed, Decimals, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>
    /// Puanı kullanıcının bölge ayarına göre, gereksiz sıfırlar olmadan yazar ("26", "25,5"). null → boş metin.
    /// </summary>
    public static string Format(decimal? value) =>
        value is decimal v ? v.ToString("0.##", CultureInfo.CurrentCulture) : string.Empty;
}
