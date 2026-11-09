using System.IO;
using System.Text.Json;

namespace EduAnalytics.UI.Services;

/// <summary>Sınav kâğıdı antetinde her sınavda tekrar girilmesi gerekmeyen bilgiler.</summary>
public sealed class ExamPaperSettings
{
    public string InstitutionName { get; set; } = "Doğuş Üniversitesi";

    /// <summary>Kurum logosunun dosya yolu (PNG/JPEG). Boşsa logo basılmaz.</summary>
    public string? LogoPath { get; set; }

    public string? Lecturer { get; set; }

    /// <summary>KURALLAR bölümü (her satır bir madde). null ise varsayılan kurallar.</summary>
    public List<string>? Rules { get; set; }

    public bool PrintQuestionTexts { get; set; } = true;
}

/// <summary>Antet ayarlarını %AppData%\EduAnalytics\exam-paper-settings.json dosyasında saklar.</summary>
public static class ExamPaperSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EduAnalytics", "exam-paper-settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ExamPaperSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<ExamPaperSettings>(File.ReadAllText(SettingsPath)) ?? new ExamPaperSettings();
        }
        catch (Exception)
        {
            // Bozuk ayar dosyası kâğıt üretimini engellemesin; varsayılanlarla devam edilir.
        }
        return new ExamPaperSettings();
    }

    public static void Save(ExamPaperSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception)
        {
            // Ayar kaydedilemese de PDF üretimi tamamlanmıştır; bir sonraki açılışta varsayılanlar gelir.
        }
    }
}
