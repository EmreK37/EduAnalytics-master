using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Helpers;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.Core.Enums;

namespace EduAnalytics.UI.ViewModels;

/// <summary>
/// Tüm sınavları listeler; temel sınav bilgisini düzenler, mevcut sınava soru ekler ve sınavı silebilir.
/// </summary>
public partial class ExamManagementViewModel : ObservableObject
{
    private readonly IExamCrudService _examService;
    private readonly IQuestionBankService _bankService;
    private readonly SemaphoreSlim _poolLock = new(1, 1);

    [ObservableProperty] private ObservableCollection<ExamListItemDto> _exams = new();

    [ObservableProperty] private bool _isEditDialogOpen;
    [ObservableProperty] private int _editingExamId;
    [ObservableProperty] private string _editTitle = string.Empty;
    [ObservableProperty] private DateTime _editExamDate = DateTime.Today;
    [ObservableProperty] private int _editDurationMinutes = 60;
    [ObservableProperty] private ExamType _editExamType = ExamType.Midterm;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _successMessage;

    // ── Mevcut sınava soru ekleme diyaloğu ──
    [ObservableProperty] private bool _isAddQuestionsDialogOpen;
    [ObservableProperty] private ExamListItemDto? _addQuestionsExam;
    [ObservableProperty] private string _poolSearchText = string.Empty;
    [ObservableProperty] private ObservableCollection<QuestionTypeFilterOption> _poolTypeFilters = new()
    {
        new QuestionTypeFilterOption("Tümü", null),
        new QuestionTypeFilterOption("Test", QuestionType.MultipleChoice),
        new QuestionTypeFilterOption("Klasik", QuestionType.OpenEnded)
    };
    [ObservableProperty] private QuestionTypeFilterOption? _selectedPoolTypeFilter;
    [ObservableProperty] private ObservableCollection<QuestionBankItemDto> _poolQuestions = new();
    [ObservableProperty] private ObservableCollection<PendingExamQuestion> _pendingQuestions = new();
    [ObservableProperty] private string? _addQuestionsError;
    [ObservableProperty] private bool _isAddingQuestions;

    public event Action<int>? OpenAnalysisRequested;

    public ExamManagementViewModel(IExamCrudService examService, IQuestionBankService bankService)
    {
        _examService = examService;
        _bankService = bankService;
        SelectedPoolTypeFilter = PoolTypeFilters.FirstOrDefault();
    }

    /// <summary>Sınavda şu an bulunan (iptal edilmemiş) soruların toplam puanı.</summary>
    public decimal ExistingTotalPoints =>
        AddQuestionsExam?.Questions.Where(q => !q.IsCancelled).Sum(q => q.MaxPoints) ?? 0m;

    public decimal PendingTotalPoints => PendingQuestions.Sum(p => p.Points);

    public string AddQuestionsPointSummary =>
        $"Mevcut: {ExistingTotalPoints:0.##} p  +  Eklenecek: {PendingTotalPoints:0.##} p  =  {ExistingTotalPoints + PendingTotalPoints:0.##} / 100 p";

    private void RefreshAddQuestionsSummary()
    {
        OnPropertyChanged(nameof(ExistingTotalPoints));
        OnPropertyChanged(nameof(PendingTotalPoints));
        OnPropertyChanged(nameof(AddQuestionsPointSummary));
    }

    partial void OnSelectedPoolTypeFilterChanged(QuestionTypeFilterOption? value)
    {
        if (IsAddQuestionsDialogOpen)
            _ = SearchPoolAsync();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        SuccessMessage = null;

        try
        {
            var list = await _examService.GetAllExamsAsync();
            Exams = new ObservableCollection<ExamListItemDto>(list);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Sınav listesi yüklenemedi: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenAnalysis(ExamListItemDto? item)
    {
        if (item != null)
            OpenAnalysisRequested?.Invoke(item.Id);
    }

    [RelayCommand]
    private async Task EditAsync(ExamListItemDto? item)
    {
        if (item == null) return;

        ErrorMessage = null;
        SuccessMessage = null;

        try
        {
            var model = await _examService.GetExamForEditAsync(item.Id)
                ?? throw new InvalidOperationException("Sınav bulunamadı.");

            EditingExamId = model.Id;
            EditTitle = model.Title;
            EditExamDate = model.ExamDate;
            EditDurationMinutes = model.DurationMinutes;
            EditExamType = model.ExamType;
            IsEditDialogOpen = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Düzenleme yüklenemedi: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        if (string.IsNullOrWhiteSpace(EditTitle))
        {
            ErrorMessage = "Sınav başlığı boş olamaz.";
            return;
        }

        try
        {
            await _examService.UpdateExamAsync(new ExamUpdateModel
            {
                Id = EditingExamId,
                Title = EditTitle,
                ExamDate = EditExamDate,
                DurationMinutes = EditDurationMinutes,
                ExamType = EditExamType
            });

            IsEditDialogOpen = false;
            await LoadAsync();
            SuccessMessage = "Sınav güncellendi.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Güncelleme hatası: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditDialogOpen = false;
    }

    // ───────────────────────────────────────────────
    //  Mevcut sınava soru ekleme
    // ───────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenAddQuestionsAsync(ExamListItemDto? item)
    {
        if (item == null) return;

        ErrorMessage = null;
        SuccessMessage = null;
        AddQuestionsError = null;

        foreach (var p in PendingQuestions)
            p.PropertyChanged -= OnPendingPointsChanged;
        PendingQuestions = new ObservableCollection<PendingExamQuestion>();
        PoolSearchText = string.Empty;
        SelectedPoolTypeFilter = PoolTypeFilters.FirstOrDefault();
        AddQuestionsExam = item;
        IsAddQuestionsDialogOpen = true;
        RefreshAddQuestionsSummary();

        await SearchPoolAsync();
    }

    [RelayCommand]
    private async Task SearchPoolAsync()
    {
        var exam = AddQuestionsExam;
        if (exam == null) return;

        await _poolLock.WaitAsync();
        try
        {
            var result = await _bankService.SearchAsync(new QuestionBankFilter
            {
                CourseId = exam.CourseId,
                IsActive = true,
                Type = SelectedPoolTypeFilter?.Type,
                SearchText = string.IsNullOrWhiteSpace(PoolSearchText) ? null : PoolSearchText
            });

            // Sınavda zaten olan ve eklenmek üzere seçilmiş sorular havuzda görünmez.
            var excluded = exam.Questions.Select(q => q.QuestionId)
                .Concat(PendingQuestions.Select(p => p.Question.Id))
                .ToHashSet();
            PoolQuestions = new ObservableCollection<QuestionBankItemDto>(
                result.Where(q => !excluded.Contains(q.Id)));
        }
        catch (Exception ex)
        {
            AddQuestionsError = $"Soru havuzu yüklenemedi: {ex.Message}";
        }
        finally
        {
            _poolLock.Release();
        }
    }

    [RelayCommand]
    private void AddToPending(QuestionBankItemDto? question)
    {
        if (question == null) return;

        var pending = new PendingExamQuestion(question);
        pending.PropertyChanged += OnPendingPointsChanged;
        PendingQuestions.Add(pending);
        PoolQuestions.Remove(question);
        AddQuestionsError = null;
        RefreshAddQuestionsSummary();
    }

    [RelayCommand]
    private void RemoveFromPending(PendingExamQuestion? pending)
    {
        if (pending == null) return;

        pending.PropertyChanged -= OnPendingPointsChanged;
        PendingQuestions.Remove(pending);
        AddQuestionsError = null;
        RefreshAddQuestionsSummary();
        _ = SearchPoolAsync();
    }

    private void OnPendingPointsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PendingExamQuestion.Points))
            RefreshAddQuestionsSummary();
    }

    /// <summary>
    /// Sınavın 100'e kalan puanını, eklenecek soruların mevcut puanlarını ağırlık katsayısı
    /// kabul ederek orantılı dağıtır.
    /// </summary>
    [RelayCommand]
    private void FillRemainingPoints()
    {
        if (PendingQuestions.Count == 0)
        {
            AddQuestionsError = "Puan dağıtımı için önce havuzdan soru seçmelisiniz.";
            return;
        }

        var remaining = 100m - ExistingTotalPoints;
        if (remaining <= 0)
        {
            AddQuestionsError = $"Sınavın mevcut soruları zaten {ExistingTotalPoints:0.##} puan; dağıtılacak puan kalmadı.";
            return;
        }

        var snapshot = PendingQuestions.ToList();
        var scaled = PointDistributor.ScaleToTotal(snapshot.Select(p => p.Points).ToList(), remaining);
        for (var i = 0; i < snapshot.Count; i++)
            snapshot[i].Points = scaled[i];

        AddQuestionsError = null;
        RefreshAddQuestionsSummary();
    }

    [RelayCommand]
    private async Task ConfirmAddQuestionsAsync()
    {
        var exam = AddQuestionsExam;
        if (exam == null) return;

        if (PendingQuestions.Count == 0)
        {
            AddQuestionsError = "Eklemek için en az bir soru seçmelisiniz.";
            return;
        }
        if (PendingQuestions.Any(p => p.Points <= 0))
        {
            AddQuestionsError = "Soru puanları 0'dan büyük olmalı.";
            return;
        }

        IsAddingQuestions = true;
        try
        {
            var refs = PendingQuestions
                .Select((p, i) => new ExamBankQuestionRef
                {
                    QuestionId = p.Question.Id,
                    OrderInExam = i + 1,
                    OverrideMaxPoints = Math.Round(p.Points, 2)
                })
                .ToList();

            var added = await _examService.AddQuestionsToExamAsync(exam.Id, refs);

            var total = ExistingTotalPoints + PendingTotalPoints;
            var hadAnswers = exam.HasAnswers;
            CloseAddQuestionsDialog();
            await LoadAsync();

            SuccessMessage = $"{added} soru \"{exam.Title}\" sınavına eklendi (sınav toplamı {total:0.##} puan)." +
                (hadAnswers ? " Bu sınava daha önce cevap girilmişti; yeni sorular için Cevap Girişi ekranından cevapları tamamlayın." : string.Empty);
            if (total != 100m)
                SuccessMessage += $" Dikkat: toplam puan 100 değil ({total:0.##}).";
        }
        catch (Exception ex)
        {
            AddQuestionsError = $"Sorular eklenemedi: {ex.Message}";
        }
        finally
        {
            IsAddingQuestions = false;
        }
    }

    [RelayCommand]
    private void CancelAddQuestions() => CloseAddQuestionsDialog();

    private void CloseAddQuestionsDialog()
    {
        IsAddQuestionsDialogOpen = false;
        foreach (var p in PendingQuestions)
            p.PropertyChanged -= OnPendingPointsChanged;
        PendingQuestions = new ObservableCollection<PendingExamQuestion>();
        PoolQuestions = new ObservableCollection<QuestionBankItemDto>();
        AddQuestionsError = null;
    }

    [RelayCommand]
    private async Task RemoveQuestionAsync(ExamListQuestionDto? question)
    {
        if (question == null) return;

        var exam = Exams.FirstOrDefault(e => e.Id == question.ExamId);
        var answerNote = exam is { HasAnswers: true }
            ? "\n\nBu sınava cevap girilmiş; bu soruya ait öğrenci cevapları da silinir."
            : string.Empty;

        var confirm = EduAnalytics.UI.Services.AppMessageBox.Show(
            $"S{question.QuestionNumber} sorusunu sınavdan çıkarmak istediğinize emin misiniz?\nSoru, soru bankasında kalır.{answerNote}",
            "Soruyu Çıkar",
            System.Windows.MessageBoxButton.YesNo,
            exam is { HasAnswers: true } ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Question);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        ErrorMessage = null;
        SuccessMessage = null;
        try
        {
            var deleted = await _examService.RemoveQuestionFromExamAsync(question.ExamId, question.QuestionId);
            await LoadAsync();
            SuccessMessage = $"S{question.QuestionNumber} sınavdan çıkarıldı." +
                (deleted > 0 ? $" {deleted} öğrenci cevabı silindi." : string.Empty) +
                " Kalan sorular yeniden numaralandı.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Soru çıkarılamadı: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(ExamListItemDto? item)
    {
        if (item == null) return;

        var warning = item.HasAnswers
            ? $"\n\nBu sınava {item.TotalAnswers} cevap girilmiş. Silinince bu cevaplar da kaybolur."
            : string.Empty;

        var confirm = EduAnalytics.UI.Services.AppMessageBox.Show(
            $"\"{item.Title}\" sınavını silmek istediğinize emin misiniz?{warning}",
            "Sınav Sil",
            System.Windows.MessageBoxButton.YesNo,
            item.HasAnswers ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Question);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            await _examService.DeleteExamAsync(item.Id);
            Exams.Remove(item);
            SuccessMessage = "Sınav silindi.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Silme hatası: {ex.Message}";
        }
    }
}
