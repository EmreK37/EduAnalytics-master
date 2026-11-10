using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.UI.Services;
using EduAnalytics.UI.Services.AIAssistant;

namespace EduAnalytics.UI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IExamAnalysisService _examAnalysis;
    private readonly IExamStatisticsService _statisticsService;
    private readonly IItemAnalysisService _itemAnalysisService;
    private readonly ILearningOutcomePerformanceService _learningOutcomeService;
    private readonly IStudentService _studentService;
    private readonly IUserProfileService _userProfile;
    private readonly IAISettingsService _aiSettings;

    public event Action<int>? OpenAnalysisRequested;
    public event Action<DateTime?>? CalendarRequested;
    public event Action? ExamManagementRequested;
    public event Action? StudentsRequested;
    public event Action<int>? LatestExamStatisticsRequested;

    private List<ExamSummaryDto> _allExams = new();

    [ObservableProperty]
    private ObservableCollection<ExamSummaryDto> _exams = new();

    [ObservableProperty]
    private ObservableCollection<ExamSummaryDto> _filteredExams = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _studentCount;

    [ObservableProperty]
    private double _overallAverageSuccessRate;

    [ObservableProperty]
    private string _lastActivityDate = "—";

    [ObservableProperty]
    private DateTime? _lastActivityExamDate;

    [ObservableProperty]
    private int? _lastActivityExamId;

    // ─── Sistem durumu (sabit metin değil, gerçek durum) ───

    /// <summary>Öğrencilerin dağıldığı sınıf sayısı (öğrenci kartındaki rozet).</summary>
    [ObservableProperty]
    private int _classCount;

    [ObservableProperty]
    private bool _isDatabaseConnected = true;

    [ObservableProperty]
    private string _databaseStatusText = "Veritabanı kontrol ediliyor…";

    [ObservableProperty]
    private bool _isAiConfigured;

    [ObservableProperty]
    private string _aiStatusText = string.Empty;

    /// <summary>Henüz hiç cevap girilmemiş sınav sayısı.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExamsAwaitingAnswers))]
    private int _examsAwaitingAnswers;

    public bool HasExamsAwaitingAnswers => ExamsAwaitingAnswers > 0;

    /// <summary>Cevabı girilmiş, yani analiz edilebilen sınav sayısı ("Analize hazır" kartı).</summary>
    [ObservableProperty]
    private int _examsReadyForAnalysis;

    [ObservableProperty]
    private string _readyCaption = string.Empty;

    [ObservableProperty]
    private string _pendingExamsText = string.Empty;

    public string DisplayUserName =>
        string.IsNullOrWhiteSpace(_userProfile.Current.Username)
            ? "Edu"
            : _userProfile.Current.Username;

    public string GreetingText
    {
        get
        {
            var hour = DateTime.Now.Hour;
            return hour switch
            {
                >= 5 and < 12 => "Günaydın, ",
                >= 12 and < 17 => "Tünaydın, ",
                >= 17 and < 22 => "İyi akşamlar, ",
                _ => "İyi geceler, "
            };
        }
    }

    public DashboardViewModel(
        IExamAnalysisService examAnalysis,
        IExamStatisticsService statisticsService,
        IItemAnalysisService itemAnalysisService,
        ILearningOutcomePerformanceService learningOutcomeService,
        IStudentService studentService,
        IUserProfileService userProfile,
        IAISettingsService aiSettings)
    {
        _examAnalysis = examAnalysis;
        _statisticsService = statisticsService;
        _itemAnalysisService = itemAnalysisService;
        _learningOutcomeService = learningOutcomeService;
        _studentService = studentService;
        _userProfile = userProfile;
        _aiSettings = aiSettings;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        OnPropertyChanged(nameof(DisplayUserName));
        OnPropertyChanged(nameof(GreetingText));
        UpdateAiStatus();
        try
        {
            var listTask    = _examAnalysis.GetAllExamSummariesAsync();
            var studentsTask = _studentService.GetAllAsync();

            var list = await listTask;
            foreach (var exam in list)
                await EnrichDashboardMetricsAsync(exam);

            _allExams = list.ToList();
            Exams = new ObservableCollection<ExamSummaryDto>(_allExams);
            ApplyExamFilter();

            var students = await studentsTask;
            StudentCount = students.Count;
            ClassCount = students
                .Select(s => s.ClassName?.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Count();

            // Genel başarı yalnızca cevabı girilmiş sınavlardan; cevapsız sınav %0 sayılıp ortalamayı düşürmez.
            var examsWithAnswers = Exams.Where(e => e.TotalStudents > 0).ToList();
            OverallAverageSuccessRate = examsWithAnswers.Count > 0
                ? Math.Round(examsWithAnswers.Average(e => e.AverageSuccessRate), 1)
                : 0;

            ExamsReadyForAnalysis = examsWithAnswers.Count;
            ExamsAwaitingAnswers = Exams.Count - examsWithAnswers.Count;
            ReadyCaption = Exams.Count == 0
                ? "Henüz sınav yok"
                : ExamsAwaitingAnswers > 0
                    ? $"{ExamsAwaitingAnswers} sınav cevap bekliyor"
                    : "Bütün sınavların cevapları girilmiş";
            PendingExamsText = ExamsAwaitingAnswers > 0
                ? $"Cevabı girilmemiş {ExamsAwaitingAnswers} sınav var"
                : Exams.Count > 0 ? "Bütün sınavların cevapları girilmiş" : "Henüz sınav yok";

            var latest = Exams.MaxBy(e => e.ExamDate);
            LastActivityExamDate = latest?.ExamDate.Date;
            LastActivityExamId = latest?.ExamId;
            LastActivityDate = latest is not null
                ? latest.ExamDate.ToString("dd MMM yyyy")
                : "—";

            IsDatabaseConnected = true;
            DatabaseStatusText = "Veritabanı bağlantısı aktif";
        }
        catch (Exception ex)
        {
            IsDatabaseConnected = false;
            DatabaseStatusText = "Veritabanına bağlanılamadı: " + ex.GetBaseException().Message;
            PendingExamsText = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Yapay zekâ asistanının seçili sağlayıcısı için API anahtarı girilmiş mi? (Sunucuya bağlanılmaz;
    /// yalnızca yapılandırma denetlenir.)
    /// </summary>
    private void UpdateAiStatus()
    {
        try
        {
            var provider = _aiSettings.Current.ActiveProvider;
            IsAiConfigured = !string.IsNullOrWhiteSpace(_aiSettings.GetApiKey(provider));
            AiStatusText = IsAiConfigured
                ? $"Yapay zekâ asistanı hazır ({provider})"
                : "Yapay zekâ asistanı için API anahtarı girilmemiş";
        }
        catch
        {
            IsAiConfigured = false;
            AiStatusText = "Yapay zekâ ayarları okunamadı";
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyExamFilter();

    private void ApplyExamFilter()
    {
        var query = (SearchText ?? string.Empty).Trim();
        IEnumerable<ExamSummaryDto> filtered = _allExams;

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = _allExams.Where(exam =>
                Contains(exam.ExamTitle, query) ||
                Contains(exam.CourseName, query) ||
                Contains(exam.ExamDate.ToString("dd MMM yyyy"), query) ||
                Contains(exam.ExamDate.ToString("dd.MM.yyyy"), query));
        }

        FilteredExams = new ObservableCollection<ExamSummaryDto>(filtered);
    }

    private static bool Contains(string? source, string query) =>
        !string.IsNullOrWhiteSpace(source) &&
        source.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private async Task EnrichDashboardMetricsAsync(ExamSummaryDto exam)
    {
        var statistics = await _statisticsService.ComputeAsync(exam.ExamId, 50);
        if (statistics != null)
        {
            exam.MedianScore = statistics.Median;
            exam.CronbachAlpha = statistics.CronbachAlpha;
            exam.CronbachAlphaInterpretation = statistics.CronbachAlphaInterpretation;
            exam.PassingScore = statistics.PassingScore;
            exam.PassRate = statistics.PassRate;
        }

        // Sınav analizi ekranıyla aynı kural: ortalamalar yalnızca hesaplanabilen maddelerden alınır.
        var itemAnalysis = await _itemAnalysisService.AnalyzeAsync(exam.ExamId);
        var withData = itemAnalysis.Where(i => i.HasData).ToList();
        if (withData.Count > 0)
            exam.AverageDifficultyIndex = Math.Round(withData.Average(i => i.DifficultyIndex), 3);
        var withDiscrimination = itemAnalysis.Where(i => i.HasDiscrimination).ToList();
        if (withDiscrimination.Count > 0)
            exam.AverageDiscriminationIndex = Math.Round(withDiscrimination.Average(i => i.DiscriminationIndex), 3);

        var outcomes = await _learningOutcomeService.AnalyzeExamAsync(exam.ExamId);
        var lowestOutcome = outcomes.OrderBy(o => o.SuccessRate).FirstOrDefault();
        if (lowestOutcome != null)
        {
            exam.LowestLearningOutcomeName = lowestOutcome.OutcomeName;
            exam.LowestLearningOutcomeSuccessRate = Math.Round(lowestOutcome.SuccessRate, 1);
        }
    }

    [RelayCommand]
    private void OpenAnalysis(ExamSummaryDto? exam)
    {
        if (exam != null)
            OpenAnalysisRequested?.Invoke(exam.ExamId);
    }

    [RelayCommand]
    private void OpenCalendar()
    {
        CalendarRequested?.Invoke(LastActivityExamDate);
    }

    [RelayCommand]
    private void OpenExamManagement()
    {
        ExamManagementRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenLatestExamStatistics()
    {
        if (LastActivityExamId.HasValue)
            LatestExamStatisticsRequested?.Invoke(LastActivityExamId.Value);
        else
            ExamManagementRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenStudents()
    {
        StudentsRequested?.Invoke();
    }
}
