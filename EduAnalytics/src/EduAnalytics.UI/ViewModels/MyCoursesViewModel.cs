using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.UI.Views;
using System.Collections.ObjectModel;
using System.Windows;

namespace EduAnalytics.UI.ViewModels;

public partial class MyCoursesViewModel : ObservableObject
{
    private readonly IMyCoursesService _service;

    [ObservableProperty]
    private ObservableCollection<MyCourseDetailDto> _courses = new();

    [ObservableProperty]
    private MyCourseDetailDto? _selectedCourse;

    public bool IsCourseSelected => SelectedCourse != null;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    // Arama: soldaki ders listesi ve sağdaki öğrenci tablosu için anlık filtre
    private static readonly System.Globalization.CompareInfo TurkishCompare =
        System.Globalization.CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

    [ObservableProperty] private string _courseSearchText = string.Empty;
    [ObservableProperty] private string _studentSearchText = string.Empty;
    [ObservableProperty] private ObservableCollection<MyCourseDetailDto> _filteredCourses = new();
    [ObservableProperty] private ObservableCollection<StudentDto> _filteredStudents = new();

    public MyCoursesViewModel(IMyCoursesService service)
    {
        _service = service;
    }

    partial void OnSelectedCourseChanged(MyCourseDetailDto? value)
    {
        OnPropertyChanged(nameof(IsCourseSelected));
        ApplyStudentFilter();
    }

    partial void OnCourseSearchTextChanged(string value) => ApplyCourseFilter();
    partial void OnStudentSearchTextChanged(string value) => ApplyStudentFilter();

    private static bool Matches(string? source, string query) =>
        !string.IsNullOrEmpty(source) &&
        TurkishCompare.IndexOf(source, query, System.Globalization.CompareOptions.IgnoreCase) >= 0;

    /// <summary>Dersleri adına veya koduna göre süzer; seçili ders gizlenirse ilk eşleşen seçilir.</summary>
    private void ApplyCourseFilter()
    {
        var query = CourseSearchText?.Trim() ?? string.Empty;
        var list = query.Length == 0
            ? Courses.ToList()
            : Courses.Where(c => Matches(c.CourseName, query) || Matches(c.CourseCode, query)).ToList();

        var keep = SelectedCourse;
        FilteredCourses = new ObservableCollection<MyCourseDetailDto>(list);
        SelectedCourse = keep != null && list.Contains(keep) ? keep : list.FirstOrDefault();
    }

    /// <summary>Seçili dersin öğrencilerini numara, ad soyad veya sınıf/şubeye göre süzer.</summary>
    private void ApplyStudentFilter()
    {
        var students = SelectedCourse?.EnrolledStudents ?? new List<StudentDto>();
        var query = StudentSearchText?.Trim() ?? string.Empty;
        var list = query.Length == 0
            ? students
            : students.Where(s => Matches(s.StudentNumber, query) || Matches(s.FullName, query) || Matches(s.ClassName, query)).ToList();

        FilteredStudents = new ObservableCollection<StudentDto>(list);
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var list = await _service.GetMyCoursesAsync();
            Courses = new ObservableCollection<MyCourseDetailDto>(list);
            ApplyCourseFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Dersler yüklenirken hata oluştu: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task InspectStudentAsync(StudentDto? student)
    {
        if (student == null || SelectedCourse == null) return;

        try
        {
            var profile = await _service.GetStudentProfileAsync(SelectedCourse.CourseId, student.Id);
            
            var dialog = new StudentProfileDialog(profile);
            
            // Set Owner to main window if possible
            if (Application.Current.MainWindow != null)
                dialog.Owner = Application.Current.MainWindow;

            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            EduAnalytics.UI.Services.AppMessageBox.Show($"Öğrenci profili yüklenemedi: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
