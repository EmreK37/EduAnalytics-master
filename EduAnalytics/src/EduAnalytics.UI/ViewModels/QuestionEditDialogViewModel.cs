using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.Core.Enums;

namespace EduAnalytics.UI.ViewModels;

/// <summary>
/// Soru bankasındaki bir sorunun temel alanlarını düzenlemek için modal dialog VM'i.
/// Topic ve öğrenim çıktısı bağlantıları korunur.
/// </summary>
public partial class QuestionEditDialogViewModel : ObservableObject
{
    private readonly IQuestionBankService _bankService;
    private readonly IRubricService _rubricService;
    private QuestionBankCreateModel? _model;
    private bool _hadCriteria;

    /// <summary>Klasik soru için rubrik (puanlama) ölçütleri.</summary>
    public System.Collections.ObjectModel.ObservableCollection<RubricCriterionEditRow> RubricCriteria { get; } = new();

    public decimal RubricTotal => RubricCriteria.Sum(c => c.MaxPoints);

    /// <summary>Ölçüt varsa toplamı sorunun puanına eşit olmalı.</summary>
    public bool RubricTotalMismatch => RubricCriteria.Count > 0 && RubricTotal != MaxPoints;

    public string RubricTotalText => RubricCriteria.Count == 0
        ? "Ölçüt yok; puan cevap ekranında doğrudan girilir."
        : RubricTotalMismatch
            ? $"Ölçüt toplamı {RubricTotal:0.##}, soru puanı {MaxPoints:0.##}: eşit olmalı."
            : $"Ölçüt toplamı {RubricTotal:0.##} = soru puanı ✓";

    internal void NotifyRubricChanged()
    {
        OnPropertyChanged(nameof(RubricTotal));
        OnPropertyChanged(nameof(RubricTotalMismatch));
        OnPropertyChanged(nameof(RubricTotalText));
    }

    partial void OnMaxPointsChanged(decimal value) => NotifyRubricChanged();

    [RelayCommand]
    private void AddCriterion()
    {
        RubricCriteria.Add(new RubricCriterionEditRow(this) { Title = string.Empty, MaxPoints = 0 });
        NotifyRubricChanged();
    }

    [RelayCommand]
    private void RemoveCriterion(RubricCriterionEditRow? row)
    {
        if (row == null) return;
        RubricCriteria.Remove(row);
        NotifyRubricChanged();
    }

    [ObservableProperty] private int _questionId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMultipleChoice))]
    [NotifyPropertyChangedFor(nameof(IsOpenEnded))]
    private QuestionType _type = QuestionType.MultipleChoice;

    [ObservableProperty] private string _questionText = string.Empty;
    [ObservableProperty] private decimal _maxPoints = 1.0m;

    [ObservableProperty] private string _optionA = string.Empty;
    [ObservableProperty] private string _optionB = string.Empty;
    [ObservableProperty] private string _optionC = string.Empty;
    [ObservableProperty] private string _optionD = string.Empty;
    [ObservableProperty] private string _optionE = string.Empty;
    [ObservableProperty] private OptionLetter _correctOption = OptionLetter.A;
    [ObservableProperty] private string _answerKey = string.Empty;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private bool _isFavorite;

    [ObservableProperty] private string? _errorMessage;

    public bool IsMultipleChoice => Type == QuestionType.MultipleChoice;
    public bool IsOpenEnded => Type == QuestionType.OpenEnded;

    public OptionLetter[] AvailableOptions { get; } =
    {
        OptionLetter.A,
        OptionLetter.B,
        OptionLetter.C,
        OptionLetter.D,
        OptionLetter.E
    };

    public event Action<bool>? Closed;

    public QuestionEditDialogViewModel(IQuestionBankService bankService, IRubricService rubricService)
    {
        _bankService = bankService;
        _rubricService = rubricService;
    }

    public async Task LoadAsync(int questionId)
    {
        ErrorMessage = null;
        _model = await _bankService.GetEditModelAsync(questionId)
            ?? throw new InvalidOperationException($"Soru bulunamadı: {questionId}");

        QuestionId = questionId;
        Type = _model.Type;
        QuestionText = _model.QuestionText;
        MaxPoints = _model.MaxPoints;
        OptionA = _model.OptionA;
        OptionB = _model.OptionB;
        OptionC = _model.OptionC;
        OptionD = _model.OptionD;
        OptionE = _model.OptionE;
        CorrectOption = _model.CorrectOption == OptionLetter.Empty ? OptionLetter.A : _model.CorrectOption;
        AnswerKey = _model.AnswerKey ?? string.Empty;
        IsActive = _model.IsActive;
        IsFavorite = _model.IsFavorite;

        RubricCriteria.Clear();
        foreach (var c in await _rubricService.GetCriteriaAsync(questionId))
        {
            RubricCriteria.Add(new RubricCriterionEditRow(this)
            {
                Id = c.Id,
                Title = c.Title,
                MaxPoints = c.MaxPoints,
                Description = c.Description
            });
        }
        _hadCriteria = RubricCriteria.Count > 0;
        NotifyRubricChanged();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_model == null)
        {
            ErrorMessage = "Düzenleme modeli yüklenmedi.";
            return;
        }

        if (string.IsNullOrWhiteSpace(QuestionText))
        {
            ErrorMessage = "Soru metni boş olamaz.";
            return;
        }

        if (MaxPoints <= 0)
        {
            ErrorMessage = "Puan 0'dan büyük olmalı.";
            return;
        }

        if (IsMultipleChoice)
        {
            if (string.IsNullOrWhiteSpace(OptionA) ||
                string.IsNullOrWhiteSpace(OptionB) ||
                string.IsNullOrWhiteSpace(OptionC) ||
                string.IsNullOrWhiteSpace(OptionD))
            {
                ErrorMessage = "Test sorularında en az A/B/C/D şıkları doldurulmalı.";
                return;
            }

            if (CorrectOption == OptionLetter.Empty)
            {
                ErrorMessage = "Doğru şık seçilmeli.";
                return;
            }
        }

        // Rubrik ölçütleri soru kaydedilmeden önce denetlenir; böylece yarım kayıt oluşmaz.
        if (IsOpenEnded && RubricCriteria.Count > 0)
        {
            if (RubricCriteria.Any(c => string.IsNullOrWhiteSpace(c.Title)))
            {
                ErrorMessage = "Her rubrik ölçütünün bir başlığı olmalı.";
                return;
            }
            if (RubricCriteria.Any(c => c.MaxPoints <= 0))
            {
                ErrorMessage = "Rubrik ölçüt puanları 0'dan büyük olmalı.";
                return;
            }
            if (RubricTotalMismatch)
            {
                ErrorMessage = $"Rubrik ölçüt puanlarının toplamı ({RubricTotal:0.##}) sorunun puanına ({MaxPoints:0.##}) eşit olmalı.";
                return;
            }
        }

        var updated = new QuestionBankCreateModel
        {
            CourseId = _model.CourseId,
            QuestionGroupId = _model.QuestionGroupId,
            Type = Type,
            QuestionText = QuestionText,
            MaxPoints = MaxPoints,
            OptionA = IsMultipleChoice ? OptionA : string.Empty,
            OptionB = IsMultipleChoice ? OptionB : string.Empty,
            OptionC = IsMultipleChoice ? OptionC : string.Empty,
            OptionD = IsMultipleChoice ? OptionD : string.Empty,
            OptionE = IsMultipleChoice ? OptionE : string.Empty,
            CorrectOption = IsMultipleChoice ? CorrectOption : OptionLetter.Empty,
            AnswerKey = IsOpenEnded ? AnswerKey : null,
            IsActive = IsActive,
            IsFavorite = IsFavorite,
            CreatedByUserId = _model.CreatedByUserId,
            TopicIds = _model.TopicIds,
            LearningOutcomeIds = _model.LearningOutcomeIds
        };

        try
        {
            await _bankService.UpdateAsync(QuestionId, updated);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Kayıt hatası: {ex.Message}";
            return;
        }

        // Klasik soruda ölçütler eşitlenir (liste boşsa rubrik kaldırılır). Teste çevrilen sorunun
        // ölçütlerine dokunulmaz; test sorusunda kullanılmazlar.
        if (IsOpenEnded && (_hadCriteria || RubricCriteria.Count > 0))
        {
            try
            {
                await _rubricService.SetCriteriaAsync(QuestionId, RubricCriteria.Select((c, i) => new RubricCriterionCreateModel
                {
                    Id = c.Id,
                    Title = c.Title,
                    MaxPoints = c.MaxPoints,
                    Description = c.Description,
                    Order = i + 1
                }).ToList());
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Soru kaydedildi ama rubrik ölçütleri kaydedilemedi: {ex.Message}";
                return;
            }
        }

        Closed?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        Closed?.Invoke(false);
    }
}

/// <summary>Soru düzenleme penceresindeki tek bir rubrik ölçütü satırı.</summary>
public partial class RubricCriterionEditRow : ObservableObject
{
    private readonly QuestionEditDialogViewModel _parent;

    public RubricCriterionEditRow(QuestionEditDialogViewModel parent)
    {
        _parent = parent;
    }

    /// <summary>Var olan ölçütün Id'si (puanlanmış ölçütler korunur); yeni ölçütte null.</summary>
    public int? Id { get; init; }

    /// <summary>Notlandırma rehberi; bu pencerede düzenlenmez, olduğu gibi korunur.</summary>
    public string? Description { get; init; }

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private decimal _maxPoints;

    partial void OnMaxPointsChanged(decimal value) => _parent.NotifyRubricChanged();
}
