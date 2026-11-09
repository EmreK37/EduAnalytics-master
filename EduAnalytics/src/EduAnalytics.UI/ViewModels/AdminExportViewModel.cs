using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduAnalytics.Business.Services.Interfaces;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace EduAnalytics.UI.ViewModels;

public partial class AdminExportViewModel : ObservableObject
{
    private readonly IDataExportService _exportService;

    [ObservableProperty]
    private bool _isExporting;

    /// <summary>
    /// Sınavlar, kitapçıklar, öğrenci cevapları ve rubrik puanları da pakete eklensin mi?
    /// Bunlar kişisel veridir; yalnızca soru bankası ve ders yapısı dağıtılacaksa kapatılmalı.
    /// </summary>
    [ObservableProperty]
    private bool _includeExamResults = true;

    public AdminExportViewModel(IDataExportService exportService)
    {
        _exportService = exportService;
    }

    [RelayCommand]
    private async Task ExportDataAsync()
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "EduAnalytics Data (*.edudata)|*.edudata",
                DefaultExt = ".edudata",
                FileName = $"okul_veritabani_{DateTime.Now:yyyyMMdd}.edudata"
            };

            if (dialog.ShowDialog() != true)
                return;

            IsExporting = true;
            var package = await _exportService.ExportDatabaseAsync(IncludeExamResults);
            var json = JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(dialog.FileName, json);

            var contents = $"{package.Courses.Count} ders, {package.Students.Count} öğrenci, " +
                           $"{package.LearningOutcomes.Count} ÖÇ, {package.ProgramOutcomes.Count} PÇ, " +
                           $"{package.Questions.Count} soru" +
                           (IncludeExamResults
                               ? $", {package.Exams.Count} sınav ve {package.Exams.Sum(e => e.Answers.Count)} cevap"
                               : "");
            var privacy = IncludeExamResults
                ? "\n\nPaket öğrenci cevaplarını içerir ve şifreli değildir; yalnızca yetkili kişilerle paylaşın."
                : string.Empty;

            EduAnalytics.UI.Services.AppMessageBox.Show(
                $"Veritabanı .edudata paketine aktarıldı: {contents}.\n\n" +
                "Paketi başka bir bilgisayarda Ayarlar penceresindeki \"Admin Paketi Yükle\" ile içe aktarabilirsiniz." + privacy,
                "Admin Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            EduAnalytics.UI.Services.AppMessageBox.Show($"Dışa aktarılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsExporting = false;
        }
    }
}
