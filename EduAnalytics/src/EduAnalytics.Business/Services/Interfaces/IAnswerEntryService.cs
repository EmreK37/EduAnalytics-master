using EduAnalytics.Business.Dtos;

namespace EduAnalytics.Business.Services.Interfaces;

public interface IAnswerEntryService
{
    /// <summary>
    /// Sınavın bilgisini, sorularını, öğrencilerini ve mevcut cevaplarını yükler.
    /// </summary>
    Task<AnswerEntryModel> LoadAsync(int examId);

    /// <summary>
    /// Cevapları kaydeder (ekle ya da güncelle); eski cevapların üzerine yazılır.
    /// <list type="bullet">
    /// <item>Sınava giren öğrencinin boş bıraktığı test soruları "boş" olarak kaydedilir (0 puan).</item>
    /// <item>Satırı tamamen boş olan öğrenci sınava girmemiş sayılır; daha önce kaydedilmiş cevapları silinir.</item>
    /// <item>Klasik soruda puan girilmemişse soru o öğrenci için değerlendirilmemiş sayılır.</item>
    /// <item>Klasik puan değiştiyse ve artık rubrik kriter toplamıyla uyuşmuyorsa kriter dökümü silinir.</item>
    /// </list>
    /// </summary>
    /// <param name="attendedStudentIds">
    /// Sınava girdiği kesin bilinen öğrenciler (örn. kâğıdı okunan öğrenciler). Bu öğrencilerin her şeyi
    /// boş bıraktığı kâğıt da kaydedilir.
    /// </param>
    Task<AnswerSaveResult> SaveAsync(
        int examId,
        List<StudentAnswerUpdate> updates,
        IReadOnlyCollection<int>? attendedStudentIds = null);
}
