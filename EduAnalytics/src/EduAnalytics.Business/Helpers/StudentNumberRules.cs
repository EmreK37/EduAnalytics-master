namespace EduAnalytics.Business.Helpers;

/// <summary>
/// Öğrenci numarası kuralı: tam 12 rakam.
/// Örn. 202403011069 → 2024 (giriş yılı) + 03 (mühendislik) + 011 (yazılım müh.) + 069 (giriş sırası).
/// </summary>
public static class StudentNumberRules
{
    public const int Length = 12;

    public const string ErrorMessage = "Öğrenci numarası tam 12 haneli rakamlardan oluşmalıdır (örn. 202403011069).";

    public static bool IsValid(string? number)
    {
        var n = number?.Trim();
        return n is { Length: Length } && n.All(c => c is >= '0' and <= '9');
    }
}
