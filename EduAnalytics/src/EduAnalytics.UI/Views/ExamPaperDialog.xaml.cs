using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EduAnalytics.Ocr;
using EduAnalytics.UI.Services;
using Microsoft.Win32;

namespace EduAnalytics.UI.Views;

/// <summary>
/// Resmi sınav kâğıdı (antet + puan tablosu + sorular) üretme penceresi. Antet bilgileri sınavdan gelir ve
/// düzenlenebilir; kurum, logo, öğretim elemanı ve kurallar bir sonraki sınav için hatırlanır.
/// Önizleme, kaydedilecek PDF ile aynı çizimden (ExamPaperPdfGenerator) üretilir.
/// </summary>
public partial class ExamPaperDialog : Window
{
    private readonly ExamPaperSpec _baseSpec;
    private readonly DispatcherTimer _previewDebounce;
    private readonly bool _isLoaded;
    private byte[]? _logo;
    private string? _logoPath;
    private int _previewVersion;

    /// <param name="baseSpec">Sınavın soruları/kitapçıkları ve antetin varsayılan değerleri.</param>
    public ExamPaperDialog(ExamPaperSpec baseSpec)
    {
        InitializeComponent();
        _baseSpec = baseSpec;

        var settings = ExamPaperSettingsStore.Load();
        InstitutionTextBox.Text = string.IsNullOrWhiteSpace(settings.InstitutionName) ? baseSpec.InstitutionName : settings.InstitutionName;
        SetLogo(settings.LogoPath, showErrors: false);
        ExamTitleTextBox.Text = baseSpec.ExamTitle;
        AcademicYearTextBox.Text = baseSpec.AcademicYear;
        SelectByContent(SemesterCombo, baseSpec.SemesterTr);
        LecturerTextBox.Text = string.IsNullOrWhiteSpace(settings.Lecturer) ? baseSpec.Lecturer : settings.Lecturer;
        SelectByContent(ExamTypeCombo, baseSpec.ExamTypeTr);
        DurationTextBox.Text = baseSpec.DurationMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ExamDatePicker.SelectedDate = baseSpec.ExamDate;
        RulesTextBox.Text = string.Join(Environment.NewLine, settings.Rules ?? baseSpec.Rules.ToList());
        PrintTextsCheckBox.IsChecked = settings.PrintQuestionTexts;

        if (baseSpec.HasBookletSection)
        {
            BookletPanel.Visibility = Visibility.Visible;
            foreach (var code in baseSpec.BookletCodes)
                BookletCombo.Items.Add($"Kitapçık {code}");
            BookletCombo.SelectedIndex = 0;
        }

        var classic = baseSpec.QuestionCount - baseSpec.MultipleChoiceCount;
        SummaryText.Text = $"{baseSpec.CourseCode} {baseSpec.CourseName} • {baseSpec.QuestionCount} soru " +
                           $"({baseSpec.MultipleChoiceCount} test, {classic} klasik) • toplam {ExamPaperPdfGenerator.FormatPoints(baseSpec.TotalPoints)} puan" +
                           (baseSpec.HasBookletSection ? $" • {baseSpec.BookletCodes.Count} kitapçık" : string.Empty);

        // Yazarken her tuşta yeniden çizmemek için kısa gecikme.
        _previewDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            _ = DrawPreviewAsync();
        };

        _isLoaded = true;
        _ = DrawPreviewAsync();
    }

    private static void SelectByContent(ComboBox combo, string content)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content as string, content, StringComparison.CurrentCultureIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded)
            return;
        _previewDebounce.Stop();
        _previewDebounce.Start();
    }

    private int SelectedBooklet => _baseSpec.HasBookletSection ? Math.Max(0, BookletCombo.SelectedIndex) : 0;

    private ExamPaperSpec BuildCurrentSpec()
    {
        var semester = SemesterCombo.SelectedItem as ComboBoxItem;
        var examType = ExamTypeCombo.SelectedItem as ComboBoxItem;
        int? duration = int.TryParse(DurationTextBox.Text.Trim(), out var minutes) && minutes > 0 ? minutes : null;

        return new ExamPaperSpec
        {
            InstitutionName = InstitutionTextBox.Text.Trim(),
            InstitutionLogo = _logo,
            CourseCode = _baseSpec.CourseCode,
            CourseName = _baseSpec.CourseName,
            ExamTitle = ExamTitleTextBox.Text.Trim(),
            ExamTypeTr = examType?.Content as string ?? _baseSpec.ExamTypeTr,
            ExamTypeEn = examType?.Tag as string ?? _baseSpec.ExamTypeEn,
            AcademicYear = AcademicYearTextBox.Text.Trim(),
            SemesterTr = semester?.Content as string ?? string.Empty,
            SemesterEn = semester?.Tag as string ?? string.Empty,
            Lecturer = LecturerTextBox.Text.Trim(),
            ExamDate = ExamDatePicker.SelectedDate,
            DurationMinutes = duration,
            Rules = RulesTextBox.Text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            PrintQuestionTexts = PrintTextsCheckBox.IsChecked == true,
            BookletCodes = _baseSpec.BookletCodes,
            Booklets = _baseSpec.Booklets
        };
    }

    private async Task DrawPreviewAsync()
    {
        var version = ++_previewVersion;
        ExamPaperSpec spec;
        try
        {
            spec = BuildCurrentSpec();
        }
        catch (Exception ex)
        {
            ShowPreviewError(ex.Message);
            return;
        }

        var booklet = SelectedBooklet;
        PreviewProgress.Visibility = Visibility.Visible;
        try
        {
            var pages = await Task.Run(() => ExamPaperPdfGenerator.RenderPngPages(spec, booklet, fill: null, dpi: 90));
            if (version != _previewVersion)
                return; // daha yeni bir önizleme istendi
            PreviewPages.ItemsSource = pages.Select(ToBitmap).ToList();
            PreviewError.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            if (version == _previewVersion)
                ShowPreviewError($"Önizleme oluşturulamadı: {ex.Message}");
        }
        finally
        {
            if (version == _previewVersion)
                PreviewProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowPreviewError(string message)
    {
        PreviewErrorText.Text = message;
        PreviewError.Visibility = Visibility.Visible;
    }

    private static BitmapImage ToBitmap(byte[] png)
    {
        var image = new BitmapImage();
        using (var stream = new MemoryStream(png))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
        }
        image.Freeze();
        return image;
    }

    private void SelectLogo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Görüntü|*.png;*.jpg;*.jpeg",
            Title = "Kurum Logosu Seç"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        SetLogo(dialog.FileName, showErrors: true);
        Setting_Changed(sender, e);
    }

    private void ClearLogo_Click(object sender, RoutedEventArgs e)
    {
        SetLogo(null, showErrors: false);
        Setting_Changed(sender, e);
    }

    private void SetLogo(string? path, bool showErrors)
    {
        _logo = null;
        _logoPath = null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                _logo = File.ReadAllBytes(path);
                _logoPath = path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (showErrors)
                    AppMessageBox.Show($"Logo okunamadı: {ex.Message}", "Logo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        LogoPathText.Text = _logoPath != null ? Path.GetFileName(_logoPath) : "Logo seçilmedi";
        LogoPathText.ToolTip = _logoPath;
    }

    private void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        ExamPaperSpec spec;
        try
        {
            spec = BuildCurrentSpec();
            spec.Validate();
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(ex.Message, "Sınav Kâğıdı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var baseName = Sanitize($"SinavKagidi_{spec.CourseCode}_{spec.ExamTitle}");
        var dialog = new SaveFileDialog
        {
            Filter = "PDF Dosyası|*.pdf",
            Title = "Sınav Kâğıdını Kaydet",
            FileName = baseName + ".pdf"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var written = new List<string>();
            if (!spec.HasBookletSection)
            {
                File.WriteAllBytes(dialog.FileName, ExamPaperPdfGenerator.GeneratePdf(spec, 0));
                written.Add(dialog.FileName);
            }
            else
            {
                var directory = Path.GetDirectoryName(dialog.FileName)!;
                var stem = Path.GetFileNameWithoutExtension(dialog.FileName);
                for (var b = 0; b < spec.BookletCodes.Count; b++)
                {
                    var path = Path.Combine(directory, $"{stem}_Kitapcik_{spec.BookletCodes[b]}.pdf");
                    File.WriteAllBytes(path, ExamPaperPdfGenerator.GeneratePdf(spec, b));
                    written.Add(path);
                }
            }

            ExamPaperSettingsStore.Save(new ExamPaperSettings
            {
                InstitutionName = spec.InstitutionName,
                LogoPath = _logoPath,
                Lecturer = spec.Lecturer,
                Rules = spec.Rules.ToList(),
                PrintQuestionTexts = spec.PrintQuestionTexts
            });

            AppMessageBox.Show(
                "Sınav kâğıdı kaydedildi:\n" + string.Join("\n", written.Select(Path.GetFileName)) +
                "\n\nKâğıdı ölçeklendirmeden (gerçek boyut, %100) yazdırın; köşedeki kareler okuma için gereklidir.",
                "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"PDF üretilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(s.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
    }
}
