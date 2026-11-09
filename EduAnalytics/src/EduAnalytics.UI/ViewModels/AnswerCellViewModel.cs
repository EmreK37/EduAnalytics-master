using CommunityToolkit.Mvvm.ComponentModel;
using EduAnalytics.Core.Enums;

namespace EduAnalytics.UI.ViewModels;

/// <summary>
/// Tek bir öğrenci × soru hücresini temsil eder.
/// DataGrid'in kolon binding'leri bu sınıfa yapılır.
/// </summary>
public partial class AnswerCellViewModel : ObservableObject
{
    public int QuestionId { get; set; }
    public int StudentId { get; set; }
    public QuestionType Type { get; set; }
    public decimal MaxPoints { get; set; }

    /// <summary>Test sorusu için seçilen şık.</summary>
    [ObservableProperty] private OptionLetter _selectedOption = OptionLetter.Empty;

    /// <summary>Klasik soru için girilen puan.</summary>
    [ObservableProperty] private decimal? _score;

    /// <summary>Ekran açıldığındaki kayıtlı puan (klasik); değişikliği fark etmek için.</summary>
    public decimal? OriginalScore { get; set; }

    /// <summary>Kayıtlı puanın rubrik kriter dökümü var mı? Puan elle değişirse döküm silinir.</summary>
    public bool HasRubricBreakdown { get; set; }
}
