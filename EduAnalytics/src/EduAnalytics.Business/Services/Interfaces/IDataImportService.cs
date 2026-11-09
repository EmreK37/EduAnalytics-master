using EduAnalytics.Business.Dtos;

namespace EduAnalytics.Business.Services.Interfaces;

public interface IDataImportService
{
    /// <summary>
    /// .edudata paketini içe aktarır. Var olan kayıtlar (aynı kod / numara / başlık) yeniden eklenmez;
    /// paket birden çok kez yüklenebilir.
    /// </summary>
    Task<DataImportResult> ImportAdminPackageAsync(AdminExportPackageDto package);
}
