using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Services.Interfaces;

namespace EduAnalytics.UI.ViewModels;

public partial class ExamCalendarViewModel : ObservableObject
{
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");
    private readonly IExamCrudService _examService;

    [ObservableProperty] private ObservableCollection<ExamListItemDto> _exams = new();
    [ObservableProperty] private ObservableCollection<CalendarDayViewModel> _calendarDays = new();
    [ObservableProperty] private ObservableCollection<ExamListItemDto> _selectedDayExams = new();
    [ObservableProperty] private DateTime _displayMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _selectedDate;
    [ObservableProperty] private bool _hasSelectedDayExams;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    // Gezinme: ay/yıl seçimi, görünüm modu (Gün/Hafta/Ay) ve doğrudan tarihe atlama
    [ObservableProperty] private ObservableCollection<int> _years = new();
    [ObservableProperty] private int _selectedYear = DateTime.Today.Year;
    [ObservableProperty] private int _selectedMonthIndex = DateTime.Today.Month - 1;
    [ObservableProperty] private CalendarViewModeOption _selectedViewMode;
    [ObservableProperty] private DateTime? _jumpDate;
    private bool _syncingPickers;

    public event Action<int>? OpenExamRequested;

    public ExamCalendarViewModel(IExamCrudService examService)
    {
        _examService = examService;
        _selectedViewMode = ViewModes[2]; // Ay
        RebuildYears();
    }

    public IReadOnlyList<string> WeekdayNames { get; } = ["Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz"];

    public string MonthTitle => DisplayMonth.ToString("MMMM yyyy", TurkishCulture);

    public IReadOnlyList<CalendarViewModeOption> ViewModes { get; } =
    [
        new("Gün", CalendarViewMode.Day),
        new("Hafta", CalendarViewMode.Week),
        new("Ay", CalendarViewMode.Month)
    ];

    public IReadOnlyList<string> MonthNames { get; } = TurkishCulture.DateTimeFormat.MonthNames.Take(12).ToList();

    private CalendarViewMode Mode => SelectedViewMode?.Mode ?? CalendarViewMode.Month;

    // Görünüme göre ızgara düzeni
    public int GridColumns => Mode == CalendarViewMode.Day ? 1 : 7;
    public int GridRows => Mode == CalendarViewMode.Month ? 6 : 1;
    public bool ShowWeekdayHeader => Mode != CalendarViewMode.Day;
    public double DayCellMinHeight => Mode switch
    {
        CalendarViewMode.Month => 104,
        CalendarViewMode.Week => 260,
        _ => 320
    };

    /// <summary>Gösterilen aralığın başlığı: "Ekim 2026", "12 – 18 Ekim 2026" ya da "15 Ekim 2026, Perşembe".</summary>
    public string RangeTitle => Mode switch
    {
        CalendarViewMode.Week => FormatWeek(WeekStart(Anchor)),
        CalendarViewMode.Day => Anchor.ToString("d MMMM yyyy, dddd", TurkishCulture),
        _ => MonthTitle
    };

    private DateTime Anchor => (SelectedDate ?? DisplayMonth).Date;

    private static DateTime WeekStart(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static string FormatWeek(DateTime start)
    {
        var end = start.AddDays(6);
        return start.Month == end.Month
            ? $"{start.Day} – {end.ToString("d MMMM yyyy", TurkishCulture)}"
            : $"{start.ToString("d MMMM", TurkishCulture)} – {end.ToString("d MMMM yyyy", TurkishCulture)}";
    }

    public string SelectedDateTitle => SelectedDate.HasValue
        ? SelectedDate.Value.ToString("d MMMM yyyy", TurkishCulture)
        : "Gün seçin";

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var list = await _examService.GetAllExamsAsync();
            Exams = new ObservableCollection<ExamListItemDto>(list);
            RebuildYears();
            BuildCalendar(keepSelection: true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Takvim yüklenemedi: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadAsync(DateTime? focusDate)
    {
        if (focusDate.HasValue)
        {
            DisplayMonth = new DateTime(focusDate.Value.Year, focusDate.Value.Month, 1);
            SelectedDate = focusDate.Value.Date;
        }

        await LoadAsync();
    }

    /// <summary>Görünüme göre bir ay, bir hafta ya da bir gün geri gider.</summary>
    [RelayCommand]
    private void PreviousMonth() => Shift(-1);

    /// <summary>Görünüme göre bir ay, bir hafta ya da bir gün ileri gider.</summary>
    [RelayCommand]
    private void NextMonth() => Shift(1);

    private void Shift(int direction)
    {
        switch (Mode)
        {
            case CalendarViewMode.Week:
                MoveAnchor(Anchor.AddDays(7 * direction));
                break;
            case CalendarViewMode.Day:
                MoveAnchor(Anchor.AddDays(direction));
                break;
            default:
                DisplayMonth = DisplayMonth.AddMonths(direction);
                BuildCalendar(keepSelection: false);
                break;
        }
    }

    /// <summary>Seçili günü değiştirir; gösterilen ay da o güne kayar.</summary>
    private void MoveAnchor(DateTime date)
    {
        SelectedDate = date.Date;
        DisplayMonth = new DateTime(date.Year, date.Month, 1);
        BuildCalendar(keepSelection: true);
    }

    [RelayCommand]
    private void GoToday()
    {
        DisplayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        SelectedDate = DateTime.Today;
        BuildCalendar(keepSelection: true);
    }

    [RelayCommand]
    private void SelectDay(CalendarDayViewModel? day)
    {
        if (day == null)
            return;

        MoveAnchor(day.Date);
    }

    [RelayCommand]
    private void OpenExam(ExamListItemDto? exam)
    {
        if (exam != null)
            OpenExamRequested?.Invoke(exam.Id);
    }

    partial void OnDisplayMonthChanged(DateTime value)
    {
        OnPropertyChanged(nameof(MonthTitle));
    }

    partial void OnSelectedDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(SelectedDateTitle));
    }

    partial void OnSelectedViewModeChanged(CalendarViewModeOption value)
    {
        OnPropertyChanged(nameof(GridColumns));
        OnPropertyChanged(nameof(GridRows));
        OnPropertyChanged(nameof(ShowWeekdayHeader));
        OnPropertyChanged(nameof(DayCellMinHeight));
        BuildCalendar(keepSelection: true);
    }

    partial void OnSelectedYearChanged(int value) => JumpToSelectedMonth();
    partial void OnSelectedMonthIndexChanged(int value) => JumpToSelectedMonth();

    /// <summary>Ay/yıl açılır listelerinden seçim yapılınca o aya atlar.</summary>
    private void JumpToSelectedMonth()
    {
        if (_syncingPickers || SelectedMonthIndex < 0 || SelectedYear <= 0) return;

        var month = SelectedMonthIndex + 1;
        if (SelectedYear == DisplayMonth.Year && month == DisplayMonth.Month) return;

        if (Mode == CalendarViewMode.Month)
        {
            DisplayMonth = new DateTime(SelectedYear, month, 1);
            BuildCalendar(keepSelection: false);
        }
        else
        {
            var day = Math.Min(Anchor.Day, DateTime.DaysInMonth(SelectedYear, month));
            MoveAnchor(new DateTime(SelectedYear, month, day));
        }
    }

    /// <summary>Tarih seçiciden seçilen güne doğrudan gider.</summary>
    partial void OnJumpDateChanged(DateTime? value)
    {
        if (_syncingPickers || !value.HasValue) return;
        MoveAnchor(value.Value);
    }

    private void RebuildYears()
    {
        var years = Exams.Select(e => e.ExamDate.Year)
            .Append(DateTime.Today.Year - 5)
            .Append(DateTime.Today.Year + 5)
            .Append(DisplayMonth.Year)
            .ToList();

        _syncingPickers = true;
        try
        {
            Years = new ObservableCollection<int>(Enumerable.Range(years.Min(), years.Max() - years.Min() + 1));
            SelectedYear = DisplayMonth.Year;
        }
        finally
        {
            _syncingPickers = false;
        }
    }

    /// <summary>Ay/yıl listelerini ve tarih seçiciyi gösterilen tarihle eşitler (geri besleme döngüsü olmadan).</summary>
    private void SyncPickers()
    {
        if (!Years.Contains(DisplayMonth.Year))
            RebuildYears();

        _syncingPickers = true;
        try
        {
            SelectedYear = DisplayMonth.Year;
            SelectedMonthIndex = DisplayMonth.Month - 1;
            JumpDate = SelectedDate;
        }
        finally
        {
            _syncingPickers = false;
        }
        OnPropertyChanged(nameof(RangeTitle));
    }

    private void BuildCalendar(bool keepSelection)
    {
        var selected = ResolveSelectedDate(keepSelection);
        if (Mode != CalendarViewMode.Month)
            selected ??= DisplayMonth; // Gün/Hafta görünümünde her zaman bir gün seçili olsun

        DateTime gridStart;
        int dayCount;
        switch (Mode)
        {
            case CalendarViewMode.Week:
                gridStart = WeekStart(selected!.Value);
                dayCount = 7;
                break;
            case CalendarViewMode.Day:
                gridStart = selected!.Value.Date;
                dayCount = 1;
                break;
            default:
                var monthStart = new DateTime(DisplayMonth.Year, DisplayMonth.Month, 1);
                gridStart = monthStart.AddDays(-(((int)monthStart.DayOfWeek + 6) % 7));
                dayCount = 42;
                break;
        }

        var previewLimit = Mode switch
        {
            CalendarViewMode.Month => 2,
            CalendarViewMode.Week => 6,
            _ => int.MaxValue
        };
        var days = new ObservableCollection<CalendarDayViewModel>();

        for (var i = 0; i < dayCount; i++)
        {
            var date = gridStart.AddDays(i);
            var dayExams = Exams
                .Where(e => e.ExamDate.Date == date.Date)
                .OrderBy(e => e.ExamDate)
                .ToList();

            days.Add(new CalendarDayViewModel(
                date,
                Mode != CalendarViewMode.Month || (date.Month == DisplayMonth.Month && date.Year == DisplayMonth.Year),
                date.Date == DateTime.Today,
                selected.HasValue && date.Date == selected.Value.Date,
                dayExams,
                previewLimit));
        }

        CalendarDays = days;
        SelectedDate = selected;
        UpdateSelectedDayExams();
        SyncPickers();
    }

    private DateTime? ResolveSelectedDate(bool keepSelection)
    {
        if (keepSelection &&
            SelectedDate.HasValue &&
            SelectedDate.Value.Year == DisplayMonth.Year &&
            SelectedDate.Value.Month == DisplayMonth.Month)
        {
            return SelectedDate.Value.Date;
        }

        if (DateTime.Today.Year == DisplayMonth.Year && DateTime.Today.Month == DisplayMonth.Month)
            return DateTime.Today;

        return Exams
            .Where(e => e.ExamDate.Year == DisplayMonth.Year && e.ExamDate.Month == DisplayMonth.Month)
            .OrderBy(e => e.ExamDate)
            .Select(e => (DateTime?)e.ExamDate.Date)
            .FirstOrDefault();
    }

    private void UpdateSelectedDayExams()
    {
        var list = SelectedDate.HasValue
            ? Exams
                .Where(e => e.ExamDate.Date == SelectedDate.Value.Date)
                .OrderBy(e => e.ExamDate)
                .ToList()
            : [];

        SelectedDayExams = new ObservableCollection<ExamListItemDto>(list);
        HasSelectedDayExams = SelectedDayExams.Count > 0;
    }
}

public partial class CalendarDayViewModel : ObservableObject
{
    public CalendarDayViewModel(
        DateTime date,
        bool isCurrentMonth,
        bool isToday,
        bool isSelected,
        IReadOnlyList<ExamListItemDto> exams,
        int previewLimit = 2)
    {
        _previewLimit = previewLimit;
        Date = date;
        DayNumber = date.Day.ToString(CultureInfo.InvariantCulture);
        IsCurrentMonth = isCurrentMonth;
        IsToday = isToday;
        IsSelected = isSelected;
        Exams = new ObservableCollection<ExamListItemDto>(exams);
    }

    public DateTime Date { get; }
    public string DayNumber { get; }
    public bool IsCurrentMonth { get; }
    public bool IsToday { get; }
    public bool IsSelected { get; }
    public ObservableCollection<ExamListItemDto> Exams { get; }
    public bool HasExams => Exams.Count > 0;
    private readonly int _previewLimit;
    public bool HasMoreExams => Exams.Count > _previewLimit;
    public string OverflowText => $"+{Math.Max(0, Exams.Count - _previewLimit)}";
    public IEnumerable<ExamListItemDto> PreviewExams => Exams.Take(_previewLimit);
}

public enum CalendarViewMode { Day, Week, Month }

/// <summary>Takvim görünüm seçeneği (açılır listede gösterilen etiket + mod).</summary>
public sealed record CalendarViewModeOption(string Label, CalendarViewMode Mode);
