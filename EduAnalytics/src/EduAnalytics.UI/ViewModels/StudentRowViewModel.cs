using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EduAnalytics.Core.Enums;

namespace EduAnalytics.UI.ViewModels;

public partial class StudentRowViewModel : ObservableObject
{
    public int StudentId { get; set; }
    public string StudentNumber { get; set; } = null!;
    public string FullName { get; set; } = null!;

    /// <summary>Öğrencinin kullandığı kitapçık kodu (A/B/C/D). Tek kitapçıklı sınavlarda otomatik "A".</summary>
    [ObservableProperty]
    private string? _bookletCode;

    /// <summary>BookletCode'a karşılık gelen veritabanı Id'si (lookup ile set edilir).</summary>
    public int? BookletId { get; set; }

    /// <summary>
    /// Öğrenci sınava girdi ama hiçbir soruyu cevaplamadı. İşaretliyse satır boş olsa da kaydedilir
    /// ve öğrenci 0 puanla analize girer; işaretli değilse boş satır "sınava girmedi" sayılır.
    /// </summary>
    [ObservableProperty]
    private bool _isBlankPaper;

    /// <summary>Ekran açıldığında bu öğrencinin bu sınavda kayıtlı cevabı var mıydı?</summary>
    public bool HadSavedAnswers { get; set; }

    public ObservableCollection<AnswerCellViewModel> Cells { get; set; } = new();

    /// <summary>Satırda hiç şık ya da puan girilmemiş mi?</summary>
    public bool IsEmptyRow => Cells.All(c => c.SelectedOption == OptionLetter.Empty && c.Score == null);
}
