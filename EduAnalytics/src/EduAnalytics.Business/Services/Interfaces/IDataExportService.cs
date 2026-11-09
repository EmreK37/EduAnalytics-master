using EduAnalytics.Business.Dtos;

namespace EduAnalytics.Business.Services.Interfaces;

public interface IDataExportService
{
    /// <summary>
    /// Veritabanını .edudata paketine dönüştürür (sürüm 2.0).
    /// </summary>
    /// <param name="includeExamResults">
    /// Sınavlar, kitapçıklar, öğrenci cevapları ve rubrik puanları da eklensin mi? Bunlar kişisel veridir.
    /// </param>
    Task<AdminExportPackageDto> ExportDatabaseAsync(bool includeExamResults = true);
}
