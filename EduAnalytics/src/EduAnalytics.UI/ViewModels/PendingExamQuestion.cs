using CommunityToolkit.Mvvm.ComponentModel;
using EduAnalytics.Business.Dtos;

namespace EduAnalytics.UI.ViewModels;

/// <summary>
/// Mevcut bir sınava eklenmek üzere seçilmiş soru. Puan, sınava özgü (OverrideMaxPoints) olarak düzenlenebilir.
/// </summary>
public partial class PendingExamQuestion : ObservableObject
{
    public PendingExamQuestion(QuestionBankItemDto question)
    {
        Question = question;
        _points = question.MaxPoints;
    }

    public QuestionBankItemDto Question { get; }

    [ObservableProperty] private decimal _points;
}
