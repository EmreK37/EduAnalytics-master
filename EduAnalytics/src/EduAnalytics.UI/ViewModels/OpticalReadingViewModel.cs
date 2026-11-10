using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduAnalytics.Business.Dtos;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.Core.Enums;
using EduAnalytics.Ocr;
using EduAnalytics.Ocr.Models;
using EduAnalytics.Ocr.Reading;
using EduAnalytics.Ocr.Recognition;
using EduAnalytics.UI.Services;
using Microsoft.Win32;

namespace EduAnalytics.UI.ViewModels;

/// <summary>
/// Optik Okuma ekranı (resmi sınav kâğıdı, şablon sürüm 3):
///   1) Sınav seç → antetli sınav kâğıdı PDF'i üret (puan tablosu + test cevap alanı + sorular),
///   2) Taranmış kâğıtları (PDF/JPEG/PNG) yükle → öğrenci numarası el yazısı kutulardan, test cevapları optik
///      alandan, her sorunun puanı "Alınan Not" satırından, toplam Toplam Not kutusundan okunur,
///   3) İnceleme tablosunda okunan puanlar kırpılmış tablo görüntüsünün altında düzenlenebilir kutularda
///      gösterilir; test puanı tabloyla, satır toplamı Toplam Not ile karşılaştırılır,
///   4) Onaylanınca test cevapları ve klasik soru puanları mevcut cevap girişi servisiyle kaydedilir.
/// "Örnek Kâğıt Üret ve Oku" seçili sınav için el yazısıyla doldurulmuş örnek kâğıtlar üretip aynı akıştan geçirir.
/// </summary>
public partial class OpticalReadingViewModel : ObservableObject
{
    private readonly IExamCrudService _examCrud;
    private readonly IAnswerEntryService _answerEntry;
    private readonly IExamBookletService _booklets;
    private readonly IOpticalFormReader _reader;
    private readonly IAppLogService _log;
    private readonly IUserProfileService? _userProfile;

    private AnswerEntryModel? _model;

    /// <summary>Yüklenen sınav (antet varsayılanları: tür, tarih, süre, başlık).</summary>
    private ExamListItemDto? _exam;

    /// <summary>Kâğıttaki bir sorunun karşılığı (kitapçığın kendi sırasıyla).</summary>
    private sealed record PaperSlot(
        int QuestionId,
        bool IsMultipleChoice,
        decimal MaxPoints,
        OptionLetter CorrectOption,
        string? ShuffleMap,
        string Text,
        IReadOnlyList<string> Options);

    private sealed record BookletLayout(int BookletId, string Code, IReadOnlyList<PaperSlot> Slots)
    {
        public List<PaperSlot> McSlots { get; } = Slots.Where(s => s.IsMultipleChoice).ToList();
    }

    /// <summary>
    /// Kitapçık → kâğıttaki soru sırası. B/C/D kitapçıklarında soru sırası ve şıklar karıştırıldığından kâğıttaki
    /// k. kutunun hangi soruya karşılık geldiği ve hangi harfin doğru olduğu öğrencinin kitapçığına bağlıdır.
    /// </summary>
    private List<BookletLayout> _layouts = new();

    private string _courseCode = string.Empty;
    private string _programName = string.Empty;

    /// <summary>Bu sınavda daha önce cevap/puan girilmiş öğrenciler (kayıt onayında "üzerine yazılacak" uyarısı için).</summary>
    private HashSet<int> _studentsWithAnswers = new();

    [ObservableProperty] private ObservableCollection<ExamListItemDto> _exams = new();
    [ObservableProperty] private ExamListItemDto? _selectedExam;

    [ObservableProperty] private ObservableCollection<ScannedSheetRowViewModel> _sheets = new();
    [ObservableProperty] private ObservableCollection<StudentPickItem> _matchableStudents = new();
    [ObservableProperty] private ObservableCollection<string> _availableBookletCodes = new();

    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _processingStatus = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _successMessage;

    [ObservableProperty] private string _examInfo = string.Empty;
    [ObservableProperty] private bool _hasExamLoaded;

    /// <summary>Sınavda test sorusu var mı? (yoksa cevap sütunu kullanılmaz)</summary>
    [ObservableProperty] private bool _hasMultipleChoice;

    /// <summary>Örnek kâğıtların yazılacağı kök klasör (Belgeler\EduAnalytics\Örnek Sınav Kâğıtları).</summary>
    public string SampleOutputRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EduAnalytics", "Örnek Sınav Kâğıtları");

    /// <summary>
    /// Kaydetmeden önce kullanıcı onayı (görünüm tarafından atanır). null ise onay sorulmadan kaydedilir.
    /// </summary>
    public Func<string, bool>? ConfirmSave { get; set; }

    /// <summary>Kaydı durduran "onay bekleyen okuma var" uyarısı; onaylar tamamlanınca kaldırılır.</summary>
    private string? _pendingApprovalError;

    public OpticalReadingViewModel(
        IExamCrudService examCrud,
        IAnswerEntryService answerEntry,
        IExamBookletService booklets,
        IOpticalFormReader reader,
        IAppLogService log,
        IUserProfileService? userProfile = null)
    {
        _examCrud = examCrud;
        _answerEntry = answerEntry;
        _booklets = booklets;
        _reader = reader;
        _log = log;
        _userProfile = userProfile;
    }

    public async Task LoadAsync()
    {
        try
        {
            var exams = await _examCrud.GetAllExamsAsync();
            Exams = new ObservableCollection<ExamListItemDto>(exams);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Sınavlar yüklenemedi: {ex.Message}";
            _log.Error("OptikOkuma", "Sınav listesi yüklenemedi", ex);
        }
    }

    partial void OnSelectedExamChanged(ExamListItemDto? value)
    {
        _ = LoadExamAsync(value);
    }

    /// <summary>Seçili sınavın öğrencilerini, sorularını ve kitapçık düzenlerini yükler.</summary>
    public async Task LoadExamAsync(ExamListItemDto? exam)
    {
        foreach (var row in Sheets)
            Detach(row);
        Sheets.Clear();
        MatchableStudents.Clear();
        AvailableBookletCodes.Clear();
        ErrorMessage = null;
        SuccessMessage = null;
        HasExamLoaded = false;
        HasMultipleChoice = false;
        ExamInfo = string.Empty;
        _model = null;
        _exam = null;
        _layouts = new List<BookletLayout>();
        _studentsWithAnswers = new HashSet<int>();
        _courseCode = string.Empty;
        _programName = string.Empty;

        if (exam == null)
            return;

        try
        {
            _model = await _answerEntry.LoadAsync(exam.Id);
            _exam = exam;
            var byId = _model.Questions.ToDictionary(q => q.QuestionId);

            foreach (var booklet in await _booklets.GetBookletsForExamAsync(exam.Id))
            {
                var slots = booklet.Questions
                    .OrderBy(q => q.OrderInBooklet)
                    .Where(q => byId.ContainsKey(q.QuestionId))
                    .Select(q => new PaperSlot(
                        q.QuestionId,
                        q.Type == QuestionType.MultipleChoice,
                        byId[q.QuestionId].MaxPoints,
                        byId[q.QuestionId].CorrectOption,
                        q.OptionShuffleMap,
                        q.QuestionText,
                        new[] { q.OptionA, q.OptionB, q.OptionC, q.OptionD, q.OptionE }))
                    .ToList();
                _layouts.Add(new BookletLayout(booklet.BookletId, booklet.BookletCode, slots));
            }

            var course = (await _examCrud.GetCoursesAsync()).FirstOrDefault(c => c.Id == exam.CourseId);
            _courseCode = course?.Code ?? string.Empty;
            _programName = course?.Program?.Name ?? string.Empty;

            _studentsWithAnswers = _model.Students
                .Where(s => s.Answers.Values.Any(a => a.SelectedOption != OptionLetter.Empty || a.Score.HasValue))
                .Select(s => s.StudentId)
                .ToHashSet();

            foreach (var s in _model.Students)
            {
                MatchableStudents.Add(new StudentPickItem
                {
                    StudentId = s.StudentId,
                    StudentNumber = s.StudentNumber,
                    FullName = s.FullName
                });
            }

            foreach (var layout in _layouts)
                AvailableBookletCodes.Add(layout.Code);

            var total = _model.Questions.Count;
            var mc = _model.Questions.Count(q => q.Type == QuestionType.MultipleChoice);
            HasMultipleChoice = mc > 0;
            ExamInfo = $"{total} soru ({mc} test, {total - mc} klasik)" +
                       $" • {MatchableStudents.Count} kayıtlı öğrenci" +
                       (_layouts.Count > 1 ? $" • {_layouts.Count} kitapçık" : "");

            if (total == 0)
                ErrorMessage = "Bu sınavda soru yok; sınav kâğıdı üretilemez.";
            else if (_layouts.Count == 0)
                ErrorMessage = "Sınavın kitapçık kaydı bulunamadı; Sınav Yönetimi'nden kitapçıkları yeniden üretin.";
            else if (_layouts.Any(l => l.Slots.Count != total))
                ErrorMessage = "Kitapçıkların soru listesi sınavın sorularıyla uyuşmuyor; kitapçıkları yeniden üretin.";
            else
            {
                BuildReadingSpec().Validate();   // kapasite: soru sayısı / 1. sayfaya sığma
                HasExamLoaded = true;
            }
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Sınav yüklenemedi: {ex.Message}";
            _log.Error("OptikOkuma", $"Sınav {exam.Id} yüklenemedi", ex);
        }
    }

    private BookletLayout? ResolveLayout(string? bookletCode)
        => _layouts.Count == 1
            ? _layouts[0]
            : _layouts.FirstOrDefault(l => l.Code == bookletCode);

    /// <summary>Okuma için kâğıt tanımı: yalnızca kitapçıkların soru düzeni gerekir.</summary>
    private ExamPaperSpec BuildReadingSpec() => new()
    {
        BookletCodes = _layouts.Select(l => l.Code).ToList(),
        Booklets = _layouts
            .Select(l => (IReadOnlyList<PaperQuestion>)l.Slots
                .Select((s, k) => new PaperQuestion(k + 1, s.IsMultipleChoice, s.MaxPoints, s.Text, s.Options))
                .ToList())
            .ToList()
    };

    /// <summary>Basılacak kâğıt: okuma tanımı + antetin sınavdan gelen varsayılanları (pencerede düzenlenebilir).</summary>
    private ExamPaperSpec BuildPrintSpec()
    {
        var reading = BuildReadingSpec();
        var exam = _exam!;
        var (typeTr, typeEn) = exam.ExamType switch
        {
            ExamType.Quiz => ("Kısa Sınav", "Quiz"),
            ExamType.Final => ("Final", "Final"),
            ExamType.MakeUp => ("Bütünleme", "Make-up"),
            _ => ("Vize", "Midterm")
        };
        var (year, semesterTr, semesterEn) = AcademicPeriod(exam.ExamDate);
        var title = exam.Title.Contains("sınav", StringComparison.CurrentCultureIgnoreCase) ? exam.Title : $"{exam.Title} Sınavı";

        return new ExamPaperSpec
        {
            InstitutionName = "Doğuş Üniversitesi",
            CourseCode = _courseCode,
            CourseName = _model!.CourseName,
            ExamTitle = title,
            ExamTypeTr = typeTr,
            ExamTypeEn = typeEn,
            AcademicYear = year,
            SemesterTr = semesterTr,
            SemesterEn = semesterEn,
            Lecturer = _userProfile?.Current.Username ?? string.Empty,
            ExamDate = exam.ExamDate,
            DurationMinutes = exam.DurationMinutes > 0 ? exam.DurationMinutes : null,
            BookletCodes = reading.BookletCodes,
            Booklets = reading.Booklets
        };
    }

    /// <summary>Sınav tarihinden eğitim-öğretim yılı ve dönem (Eylül–Ocak güz, Şubat–Haziran bahar, Temmuz–Ağustos yaz).</summary>
    private static (string Year, string SemesterTr, string SemesterEn) AcademicPeriod(DateTime date)
    {
        var startYear = date.Month >= 9 ? date.Year : date.Year - 1;
        var year = $"{startYear} – {startYear + 1}";
        return date.Month switch
        {
            >= 9 or 1 => (year, "Güz", "Fall"),
            >= 2 and <= 6 => (year, "Bahar", "Spring"),
            _ => (year, "Yaz", "Summer")
        };
    }

    // ─────────────────────────── Kâğıt üretimi ───────────────────────────

    [RelayCommand]
    private void GenerateForm()
    {
        if (!HasExamLoaded || _model == null)
        {
            ErrorMessage = "Önce bir sınav seçin.";
            return;
        }

        var dialog = new EduAnalytics.UI.Views.ExamPaperDialog(BuildPrintSpec());
        if (System.Windows.Application.Current.MainWindow != null)
            dialog.Owner = System.Windows.Application.Current.MainWindow;
        dialog.ShowDialog();
    }

    // ─────────────────────────── Tarama okuma ───────────────────────────

    [RelayCommand]
    private async Task ImportScansAsync()
    {
        if (!HasExamLoaded || _model == null)
        {
            ErrorMessage = "Önce bir sınav seçin.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Taranmış Kâğıtlar|*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
            return;

        await ImportFilesAsync(dialog.FileNames);
    }

    /// <summary>Verilen tarama dosyalarını okur ve inceleme tablosuna ekler.</summary>
    public async Task ImportFilesAsync(IReadOnlyList<string> files)
    {
        if (!HasExamLoaded || _model == null || files.Count == 0)
            return;

        ErrorMessage = null;
        SuccessMessage = null;
        IsProcessing = true;

        var spec = BuildReadingSpec();
        var candidates = MatchableStudents
            .Select(s => new StudentCandidate(s.StudentId, s.StudentNumber, s.FullName))
            .ToList();

        var failures = new List<string>();
        var added = 0;
        var continuation = 0;
        try
        {
            var fileIndex = 0;
            foreach (var file in files)
            {
                fileIndex++;
                ProcessingStatus = $"Okunuyor ({fileIndex}/{files.Count}): {Path.GetFileName(file)}";

                try
                {
                    var pages = await Task.Run(() => _reader.ReadFile(file, spec));
                    foreach (var (label, result) in pages)
                    {
                        // Soru metinlerinin devam sayfalarında okunacak alan yoktur.
                        if (result.PageKind == SheetPageKind.Continuation)
                        {
                            continuation++;
                            continue;
                        }

                        var row = BuildRow(label, result, candidates);
                        Attach(row);
                        UpdateResult(row);
                        Sheets.Add(row);
                        added++;
                    }
                }
                catch (Exception ex)
                {
                    // Bozuk tek dosya partiyi durdurmasın; kalan dosyalar okunmaya devam eder.
                    failures.Add($"{Path.GetFileName(file)} ({ex.Message})");
                    _log.Error("OptikOkuma", $"Tarama dosyası okunamadı: {file}", ex);
                }
            }

            var unreadable = Sheets.Count(s => !s.MarkersFound);
            var unmatched = Sheets.Count(s => s.MarkersFound && s.SelectedStudent == null);
            var review = Sheets.Count(s => s.Status == SheetStatus.Review);
            var needsApproval = Sheets.Count(s => s.Status == SheetStatus.NeedsApproval);
            SuccessMessage = $"✓ {added} kâğıt okundu." +
                             (continuation > 0 ? $" {continuation} devam sayfası atlandı (okunacak alan yok)." : "") +
                             (needsApproval > 0 ? $" {needsApproval} kâğıtta onay bekleyen okuma var (sarı kutular)." : "") +
                             (review > 0 ? $" {review} kâğıtta kontrol edilmesi gereken uyarı var." : "") +
                             (unmatched > 0 ? $" {unmatched} kâğıtta öğrenci elle seçilmeli." : "") +
                             (unreadable > 0 ? $" {unreadable} sayfa sınav kâğıdı olarak tanınamadı." : "");
            if (failures.Count > 0)
                ErrorMessage = $"Okunamayan dosyalar: {string.Join(", ", failures)}";
        }
        finally
        {
            IsProcessing = false;
            ProcessingStatus = string.Empty;
        }
    }

    private ScannedSheetRowViewModel BuildRow(string label, OpticalSheetResult result, List<StudentCandidate> candidates)
    {
        if (!result.MarkersFound || result.PageKind != SheetPageKind.FirstPage)
        {
            return new ScannedSheetRowViewModel
            {
                SourceLabel = label,
                MarkersFound = false,
                MatchInfo = "Okunamadı",
                BaseWarnings = result.Warnings.ToList()
            };
        }

        var (studentId, kind) = StudentMatcher.MatchHandwritten(result.StudentNumberDigits, result.RawStudentName, candidates);
        var matchInfo = kind switch
        {
            MatchKind.ExactNumber => "Numara birebir eşleşti",
            MatchKind.PartialNumber => "Numara kısmen okundu — kontrol edin",
            MatchKind.FuzzyNumber => "Numara yaklaşık eşleşti — kontrol edin",
            MatchKind.NameOnly => "Yalnızca ad benzerliği — kontrol edin",
            _ => "Eşleşmedi — elle seçin"
        };

        var warnings = new List<string>();
        if (studentId == null)
        {
            warnings.Add(result.RawStudentNumber.Length > 0
                ? $"Öğrenci bulunamadı (okunan no: {result.RawStudentNumber}) — bu derse kayıtlı değil veya numara yanlış okunmuş; listeden elle seçin."
                : "Öğrenci bulunamadı — numara kutuları boş; listeden elle seçin.");
        }
        else if (kind is MatchKind.FuzzyNumber or MatchKind.NameOnly or MatchKind.PartialNumber)
        {
            warnings.Add("Öğrenci eşleşmesi kesin değil — numara görüntüsünü ve seçili öğrenciyi kontrol edin.");
        }
        warnings.AddRange(result.Warnings);

        var bookletCode = result.BookletIndex is int b && b >= 0 && b < _layouts.Count
            ? _layouts[b].Code
            : _layouts.Count == 1 ? _layouts[0].Code : null;

        var answersText = FormatAnswers(string.Concat(result.Answers.Select(a =>
            a.MultipleMarks ? '?'
            : a.Option == OptionLetter.Empty ? '-'
            : a.Option.ToString()[0])));

        // Güven: numara, tek işaretli cevaplar ve dolu puan kutularının okuma netliklerinin en düşüğü.
        var confidences = new List<double>();
        if (result.RawStudentNumber.Length > 0)
            confidences.Add(result.StudentNumberConfidence);
        confidences.AddRange(result.Answers.Where(a => !a.MultipleMarks && a.Option != OptionLetter.Empty).Select(a => a.Confidence));
        confidences.AddRange(result.Scores.Where(s => !s.IsEmpty).Select(s => s.Unreadable ? 0 : s.Confidence));
        if (result.TotalScore is { IsEmpty: false } total)
            confidences.Add(total.Unreadable ? 0 : total.Confidence);

        var row = new ScannedSheetRowViewModel
        {
            SourceLabel = label,
            MarkersFound = true,
            OcrNumber = result.RawStudentNumber,
            OcrName = result.RawStudentName,
            MatchInfo = matchInfo,
            OverallConfidence = confidences.Count > 0 ? confidences.Min() : 0,
            BaseWarnings = warnings,
            ScoreTableImage = ToImage(result.ScoreTableImage),
            NumberImage = ToImage(result.StudentNumberImage),
            TotalImage = ToImage(result.TotalScoreImage),
            TotalReading = result.TotalScore,
            SelectedStudent = studentId.HasValue
                ? MatchableStudents.FirstOrDefault(s => s.StudentId == studentId.Value)
                : null,
            BookletCode = bookletCode,
            AnswersText = answersText,
            TotalText = result.TotalScore?.Text ?? string.Empty
        };

        // Kesin olmayan eşleşme öğretmen onayı ister (ya da listeden doğru öğrencinin seçilmesi).
        if (studentId != null && kind is MatchKind.FuzzyNumber or MatchKind.NameOnly or MatchKind.PartialNumber)
        {
            row.RequiresMatchApproval = true;
            row.IsMatchApproved = false;
        }

        var layout = ResolveLayout(bookletCode) ?? _layouts[0];
        foreach (var reading in result.Scores.OrderBy(s => s.Position))
        {
            var slot = layout.Slots[reading.Position];
            row.Scores.Add(new ScoreEntryViewModel
            {
                Position = reading.Position,
                Number = reading.Position + 1,
                IsMultipleChoice = slot.IsMultipleChoice,
                MaxPoints = slot.MaxPoints,
                ReadText = reading.Text,
                ReadConfidence = reading.Confidence,
                ReadUnreadable = reading.Unreadable,
                ReadAlternatives = reading.Alternatives,
                Text = reading.Text
            });
        }

        return row;
    }

    private void Attach(ScannedSheetRowViewModel row)
    {
        row.PropertyChanged += OnRowPropertyChanged;
        foreach (var entry in row.Scores)
            entry.PropertyChanged += OnScoreEntryChanged;
    }

    private void Detach(ScannedSheetRowViewModel row)
    {
        row.PropertyChanged -= OnRowPropertyChanged;
        foreach (var entry in row.Scores)
            entry.PropertyChanged -= OnScoreEntryChanged;
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Kayıt "onay bekleyen okuma var" diye durdurulduysa, son onay da verilince bu uyarı kaldırılır.
        if (e.PropertyName == nameof(ScannedSheetRowViewModel.HasPendingApprovals)
            && _pendingApprovalError != null
            && ErrorMessage == _pendingApprovalError
            && !Sheets.Any(s => s.HasPendingApprovals))
        {
            ErrorMessage = null;
            _pendingApprovalError = null;
        }

        if (sender is ScannedSheetRowViewModel row &&
            e.PropertyName is nameof(ScannedSheetRowViewModel.AnswersText)
                or nameof(ScannedSheetRowViewModel.BookletCode)
                or nameof(ScannedSheetRowViewModel.TotalText))
        {
            UpdateResult(row);
        }
    }

    private void OnScoreEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ScoreEntryViewModel entry || e.PropertyName != nameof(ScoreEntryViewModel.Text))
            return;
        var row = Sheets.FirstOrDefault(r => r.Scores.Contains(entry));
        if (row == null)
            return;
        row.IsSaved = false;
        UpdateResult(row);
    }

    /// <summary>
    /// Satırın test cevaplarını cevap anahtarıyla karşılaştırır, puan tablosunu değerlendirir (test → optik,
    /// klasik → tablo; tablo-optik ve toplam tutarlılığı) ve uyarıları günceller.
    /// </summary>
    private void UpdateResult(ScannedSheetRowViewModel row)
    {
        if (!row.MarkersFound)
        {
            row.ResultText = string.Empty;
            row.WarningsText = string.Join("\n", row.BaseWarnings);
            return;
        }

        var warnings = new List<string>(row.BaseWarnings);
        var layout = ResolveLayout(row.BookletCode);
        if (layout == null)
        {
            row.ResultText = "Kitapçık seçin";
            row.TotalCheckText = string.Empty;
            warnings.Add("Kitapçık seçilmedi — soru sırası kitapçığa göre değiştiğinden puanlar sorulara eşlenemiyor.");
            row.WarningsText = string.Join("\n", warnings.Distinct());
            return;
        }

        // ── Test: optik işaretten puan ──
        var opticalPoints = new decimal?[layout.Slots.Count];
        int correct = 0, wrong = 0, empty = 0;
        decimal mcPoints = 0, mcMax = 0;
        var answers = layout.McSlots.Count > 0 ? ParseAnswers(row.AnswersText) : new List<OptionLetter>();
        var answersValid = answers != null && answers.Count == layout.McSlots.Count;
        if (!answersValid)
            warnings.Add($"Test cevap dizisi geçersiz: {layout.McSlots.Count} karakter olmalı (A–E, '-' boş, '?' çift işaret).");

        var mcIndex = 0;
        for (var k = 0; k < layout.Slots.Count; k++)
        {
            var slot = layout.Slots[k];
            if (!slot.IsMultipleChoice)
                continue;
            mcMax += slot.MaxPoints;
            if (answersValid)
            {
                var answer = answers![mcIndex];
                if (answer == OptionLetter.Empty)
                {
                    empty++;
                    opticalPoints[k] = 0;
                }
                else if (_booklets.DecodeStudentChoice(slot.ShuffleMap, answer) == slot.CorrectOption)
                {
                    correct++;
                    opticalPoints[k] = slot.MaxPoints;
                    mcPoints += slot.MaxPoints;
                }
                else
                {
                    wrong++;
                    opticalPoints[k] = 0;
                }
            }
            mcIndex++;
        }

        // ── Puan tablosu ──
        var questions = new List<QuestionScore>();
        foreach (var entry in row.Scores)
        {
            var slot = layout.Slots[entry.Position];
            entry.IsMultipleChoice = slot.IsMultipleChoice;
            entry.MaxPoints = slot.MaxPoints;
            entry.OpticalPoints = slot.IsMultipleChoice ? opticalPoints[entry.Position] : null;
            questions.Add(new QuestionScore(entry.Number, slot.IsMultipleChoice, slot.MaxPoints, entry.OpticalPoints, entry.Value));
        }

        var writtenTotal = ScoreEntryViewModel.ParseInput(row.TotalText);
        var evaluation = ScoreSheetEvaluator.Evaluate(questions, writtenTotal);

        foreach (var entry in row.Scores)
        {
            var reasons = new List<string>();
            if (entry.HasInvalidText)
            {
                reasons.Add("geçersiz puan");
                warnings.Add($"Soru {entry.Number}: \"{entry.Text}\" geçerli bir puan değil.");
            }
            else if (entry.Value > entry.MaxPoints)
            {
                reasons.Add("sorunun puanını aşıyor");
            }

            // Okuma uyarısı yalnızca öğretmen değeri değiştirmediyse geçerlidir.
            if (entry.Text == entry.ReadText)
            {
                var reading = new ScoreReading
                {
                    Text = entry.ReadText,
                    Confidence = entry.ReadConfidence,
                    Unreadable = entry.ReadUnreadable,
                    IsEmpty = entry.ReadText.Length == 0 && !entry.ReadUnreadable,
                    Alternatives = entry.ReadAlternatives
                };
                if (reading.Describe($"Soru {entry.Number}", entry.MaxPoints) is { } readingWarning)
                {
                    reasons.Add(entry.ReadUnreadable ? "okunamadı" : "belirsiz okundu");
                    warnings.Add(readingWarning);
                }
            }

            if (entry.IsMultipleChoice && entry.Value is decimal table && entry.OpticalPoints is decimal optical && table != optical)
                reasons.Add("optik okumayla uyuşmuyor");
            if (!entry.IsMultipleChoice && entry.Value == null && !entry.HasInvalidText)
                reasons.Add("boş");

            var flagged = reasons.Count > 0;
            var reason = flagged ? $"Soru {entry.Number}: {string.Join(", ", reasons)}" : entry.Caption;
            // Onaylanmış kutuda farklı bir sorun ortaya çıktıysa (ör. kitapçık ya da cevap değişti)
            // önceki onay bu sorunu kapsamaz; yeniden onay gerekir.
            if (entry.IsApproved && flagged && entry.IsFlagged && entry.FlagReason != reason)
                entry.IsApproved = false;
            entry.IsFlagged = flagged;
            entry.FlagReason = reason;
        }

        if (row.TotalReading is { } totalReading && row.TotalText == totalReading.Text
            && totalReading.Describe("Toplam Not", evaluation.MaxTotal) is { } totalWarning)
            warnings.Add(totalWarning);
        if (!string.IsNullOrWhiteSpace(row.TotalText) && writtenTotal == null)
            warnings.Add($"Toplam Not: \"{row.TotalText}\" geçerli bir puan değil.");
        warnings.AddRange(evaluation.Warnings);

        var classicMax = layout.Slots.Where(s => !s.IsMultipleChoice).Sum(s => s.MaxPoints);
        var classicPoints = row.Scores.Where(s => !s.IsMultipleChoice).Sum(s => s.Value ?? 0);
        var lines = new List<string>();
        if (layout.McSlots.Count > 0)
            lines.Add(answersValid
                ? $"Test: {correct} D · {wrong} Y · {empty} B ({F(mcPoints)}/{F(mcMax)})"
                : "Test: cevap dizisi geçersiz");
        if (classicMax > 0)
            lines.Add($"Klasik: {F(classicPoints)}/{F(classicMax)}");
        lines.Add($"Toplam: {F(evaluation.ComputedTotal)}/{F(evaluation.MaxTotal)} puan");
        row.ResultText = string.Join("\n", lines);

        row.TotalMismatch = evaluation.TotalMatches == false;
        row.TotalCheckText = writtenTotal is decimal written
            ? evaluation.TotalMatches == true
                ? $"Hesaplanan {F(evaluation.ComputedTotal)} — kâğıttaki toplamla aynı ✓"
                : $"Hesaplanan {F(evaluation.ComputedTotal)} — kâğıtta {F(written)} yazıyor"
            : $"Hesaplanan {F(evaluation.ComputedTotal)} / {F(evaluation.MaxTotal)}";

        row.WarningsText = string.Join("\n", warnings.Distinct());
    }

    private static string F(decimal value) => HandwrittenNumberReader.FormatScore(value);

    [RelayCommand]
    private void RemoveSheet(ScannedSheetRowViewModel? row)
    {
        if (row == null)
            return;
        Detach(row);
        Sheets.Remove(row);
    }

    // ─────────────────────────── Örnek kâğıt (simülasyon) ───────────────────────────

    /// <summary>
    /// Seçili sınavın kayıtlı öğrencileri için el yazısıyla doldurulmuş örnek kâğıtlar üretir (gerçek taramaya
    /// benzetilmiş PNG/PDF), klasöre yazar ve hemen okuyarak inceleme tablosuna ekler. Kâğıt ve tarayıcı olmadan
    /// tüm akışı uçtan uca denemek içindir; kaydetmek yine "Cevapları ve Puanları Kaydet" ile kullanıcının onayına bağlıdır.
    /// </summary>
    [RelayCommand]
    private async Task GenerateSamplesAsync()
    {
        if (!HasExamLoaded || _model == null)
        {
            ErrorMessage = "Önce bir sınav seçin.";
            return;
        }

        ErrorMessage = null;
        SuccessMessage = null;

        var folder = Path.Combine(SampleOutputRoot, $"{Sanitize(_model.ExamTitle)}_{DateTime.Now:yyyyMMdd_HHmmss}");
        List<string> files;

        IsProcessing = true;
        ProcessingStatus = "Örnek doldurulmuş kâğıtlar üretiliyor...";
        try
        {
            var spec = BuildPrintSpec();
            var plans = BuildSamplePlans();
            files = await Task.Run(() => plans
                .Select(p => p.AsPdf
                    ? ExamPaperSampleRenderer.SaveScanPdf(Path.Combine(folder, p.FileName + ".pdf"), spec, p.BookletIndex, p.Fill, p.Simulation)
                    : ExamPaperSampleRenderer.SaveScanPng(Path.Combine(folder, p.FileName + ".png"), spec, p.BookletIndex, p.Fill, p.Simulation))
                .ToList());
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Örnek kâğıtlar üretilemedi: {ex.Message}";
            _log.Error("OptikOkuma", "Örnek sınav kâğıdı üretilemedi", ex);
            IsProcessing = false;
            ProcessingStatus = string.Empty;
            return;
        }

        IsProcessing = false;
        var firstNewRow = Sheets.Count;
        await ImportFilesAsync(files);
        foreach (var row in Sheets.Skip(firstNewRow))
            row.IsSample = true;

        SuccessMessage = $"✓ {files.Count} örnek doldurulmuş kâğıt üretildi ve okundu (klasör: {folder}). " +
                         "Tabloyu inceleyin; kaydetmek isterseniz \"Cevapları ve Puanları Kaydet\" düğmesini kullanın." +
                         (string.IsNullOrEmpty(SuccessMessage) ? "" : " " + SuccessMessage.TrimStart('✓', ' '));
    }

    private sealed record SamplePlan(string FileName, int BookletIndex, ExamPaperFill Fill, ScanSimulation Simulation, bool AsPdf);

    /// <summary>
    /// Örnek seti: kayıtlı öğrencilerden ilk dördü farklı başarı düzeylerinde (biri ters taranmış, biri iki sayfalı
    /// PDF, birinin Toplam Not'u yanlış toplanmış), bir "problemli" kâğıt (çift ve silik işaret, boş puan kutusu)
    /// ve kayıtlı olmayan bir öğrenci numarası.
    /// </summary>
    private List<SamplePlan> BuildSamplePlans()
    {
        var rng = new Random(_model!.ExamId * 7919);
        var plans = new List<SamplePlan>();
        var students = MatchableStudents.Take(4).ToList();
        var accuracies = new[] { 0.95, 0.8, 0.6, 0.45 };

        for (var i = 0; i < students.Count; i++)
        {
            var student = students[i];
            var bookletIndex = i % _layouts.Count;
            var layout = _layouts[bookletIndex];
            var answers = SampleAnswers(rng, accuracies[i], layout);
            var scores = SampleScores(rng, accuracies[i], layout, answers);
            var total = scores.Sum(s => s ?? 0);
            if (i == 3)
                total = Math.Min(total + 5, layout.Slots.Sum(s => s.MaxPoints)); // öğretmen toplamı yanlış yazmış

            plans.Add(new SamplePlan(
                $"{i + 1:00}_{student.StudentNumber}_{Sanitize(student.FullName)}" +
                (i == 1 ? "_iki_sayfa" : i == 2 ? "_ters" : i == 3 ? "_toplam_hatali" : ""),
                bookletIndex,
                new ExamPaperFill
                {
                    Seed = 101 + i,
                    StudentName = student.FullName,
                    StudentNumber = student.StudentNumber,
                    Department = _programName,
                    Answers = answers,
                    Scores = scores.Select(s => s.HasValue ? F(s.Value) : null).ToList(),
                    Total = F(total)
                },
                ScanSimulation.Typical(seed: i + 1) with { UpsideDown = i == 2 },
                AsPdf: i == 1));
        }

        // Problemli kâğıt: ilk test sorusunda çift işaret, ikincisinde silik işaret, ilk klasik puan kutusu boş.
        var problemStudent = MatchableStudents.Skip(4).FirstOrDefault() ?? MatchableStudents.FirstOrDefault();
        if (problemStudent != null)
        {
            var layout = _layouts[0];
            var answers = SampleAnswers(rng, 0.7, layout);
            var weakRows = new List<int>();
            if (answers.Count > 0)
                answers[0] = new[] { 0, 2 };
            if (answers.Count > 1)
            {
                if (answers[1].Count == 0)
                    answers[1] = new[] { 3 };
                weakRows.Add(1);
            }
            var scores = SampleScores(rng, 0.7, layout, answers);
            var firstClassic = layout.Slots.ToList().FindIndex(s => !s.IsMultipleChoice);
            if (firstClassic >= 0)
                scores[firstClassic] = null;

            plans.Add(new SamplePlan(
                $"{plans.Count + 1:00}_{problemStudent.StudentNumber}_cift_silik_isaret_bos_puan",
                0,
                new ExamPaperFill
                {
                    Seed = 211,
                    StudentName = problemStudent.FullName,
                    StudentNumber = problemStudent.StudentNumber,
                    Department = _programName,
                    Answers = answers,
                    WeakMarkRows = weakRows,
                    Scores = scores.Select(s => s.HasValue ? F(s.Value) : null).ToList(),
                    Total = F(scores.Sum(s => s ?? 0))
                },
                ScanSimulation.Typical(seed: 11),
                AsPdf: false));
        }

        // Kayıtlı olmayan öğrenci: "Öğrenci bulunamadı" uyarısını göstermek için.
        {
            var layout = _layouts[0];
            var answers = SampleAnswers(rng, 0.5, layout);
            var scores = SampleScores(rng, 0.5, layout, answers);
            plans.Add(new SamplePlan(
                $"{plans.Count + 1:00}_kayitli_olmayan_ogrenci",
                0,
                new ExamPaperFill
                {
                    Seed = 307,
                    StudentName = "Kayıtsız Öğrenci",
                    StudentNumber = "209903099999",
                    Department = _programName,
                    Answers = answers,
                    Scores = scores.Select(s => s.HasValue ? F(s.Value) : null).ToList(),
                    Total = F(scores.Sum(s => s ?? 0))
                },
                ScanSimulation.Typical(seed: 13),
                AsPdf: false));
        }

        return plans;
    }

    /// <summary>Verilen başarı oranında rastgele test cevapları üretir (kitapçığın kendi sırası ve şıklarıyla).</summary>
    private static List<IReadOnlyList<int>> SampleAnswers(Random rng, double accuracy, BookletLayout layout)
    {
        var answers = new List<IReadOnlyList<int>>();
        foreach (var slot in layout.McSlots)
        {
            var correctDisplayed = ToDisplayedOption(slot.ShuffleMap, slot.CorrectOption);
            var roll = rng.NextDouble();
            if (roll < accuracy && correctDisplayed != OptionLetter.Empty)
            {
                answers.Add(new[] { (int)correctDisplayed - 1 });
            }
            else if (roll < accuracy + (1 - accuracy) * 0.8)
            {
                var wrong = rng.Next(OpticalFormTemplate.OptionCount);
                if (wrong == (int)correctDisplayed - 1)
                    wrong = (wrong + 1) % OpticalFormTemplate.OptionCount;
                answers.Add(new[] { wrong });
            }
            else
            {
                answers.Add(Array.Empty<int>()); // boş bırakıldı
            }
        }
        return answers;
    }

    /// <summary>
    /// Öğretmenin "Alınan Not" satırına yazacağı puanlar: test sorusunda optik sonuca göre tam puan ya da 0,
    /// klasik soruda başarı oranına yakın, yarım puan hassasiyetinde rastgele bir puan.
    /// </summary>
    private List<decimal?> SampleScores(Random rng, double accuracy, BookletLayout layout, List<IReadOnlyList<int>> answers)
    {
        var scores = new List<decimal?>();
        var mcIndex = 0;
        foreach (var slot in layout.Slots)
        {
            if (slot.IsMultipleChoice)
            {
                var marked = mcIndex < answers.Count ? answers[mcIndex] : Array.Empty<int>();
                var isCorrect = marked.Count == 1 &&
                                _booklets.DecodeStudentChoice(slot.ShuffleMap, (OptionLetter)(marked[0] + 1)) == slot.CorrectOption;
                scores.Add(isCorrect ? slot.MaxPoints : 0);
                mcIndex++;
            }
            else
            {
                var ratio = Math.Clamp(accuracy + (rng.NextDouble() - 0.6) * 0.4, 0, 1);
                scores.Add(Math.Round((decimal)ratio * slot.MaxPoints * 2, MidpointRounding.AwayFromZero) / 2);
            }
        }
        return scores;
    }

    /// <summary>Orijinal doğru şıkkın, şıkları karıştırılmış kitapçıkta hangi harfle göründüğünü bulur.</summary>
    private static OptionLetter ToDisplayedOption(string? shuffleMap, OptionLetter original)
    {
        if (string.IsNullOrWhiteSpace(shuffleMap) || original == OptionLetter.Empty)
            return original;

        // Biçim: "A:C,B:A,..." — orijinal → kitapçıkta görünen
        foreach (var pair in shuffleMap.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split(':');
            if (parts.Length == 2
                && Enum.TryParse<OptionLetter>(parts[0], out var from)
                && Enum.TryParse<OptionLetter>(parts[1], out var to)
                && from == original)
            {
                return to;
            }
        }
        return original;
    }

    // ─────────────────────────── Kaydetme ───────────────────────────

    [RelayCommand]
    private async Task SaveAllAsync()
    {
        if (!HasExamLoaded || _model == null)
            return;

        ErrorMessage = null;
        SuccessMessage = null;

        var rows = Sheets.Where(s => s.MarkersFound && s.SelectedStudent != null).ToList();
        if (rows.Count == 0)
        {
            ErrorMessage = "Kaydedilecek eşleşmiş kâğıt yok. Önce tarama yükleyin ve öğrencileri eşleştirin.";
            return;
        }

        // Aynı öğrenciye iki kâğıt: hangisinin geçerli olduğuna öğretmen karar vermeli.
        var duplicates = rows.GroupBy(r => r.SelectedStudent!.StudentId)
            .Where(g => g.Count() > 1)
            .Select(g => g.First().SelectedStudent!.Display)
            .ToList();
        if (duplicates.Count > 0)
        {
            ErrorMessage = $"Aynı öğrenciye birden fazla kâğıt eşleşti: {string.Join(", ", duplicates)}. " +
                           "Fazla kâğıtları silin veya eşleştirmeyi düzeltin.";
            return;
        }

        // İnsan onayı: belirsiz okunan kutular ve kesin olmayan öğrenci eşleşmeleri onaylanmadan
        // (ya da düzeltilmeden) hiçbir kâğıt kaydedilmez.
        var pending = rows.Where(r => r.HasPendingApprovals).ToList();
        if (pending.Count > 0)
        {
            var pendingReadings = pending.Sum(r => r.PendingApprovalCount);
            ErrorMessage = _pendingApprovalError = $"{pending.Count} kâğıtta onay bekleyen {pendingReadings} okuma var: " +
                           string.Join(", ", pending.Take(3).Select(r => r.SourceLabel)) +
                           (pending.Count > 3 ? " …" : "") + ". " +
                           "Sarı çerçeveli kutuları taranan görüntüyle karşılaştırın: değer doğruysa \"Onayla\"yı " +
                           "işaretleyin, yanlışsa kutuyu düzeltin. Öğrenci eşleşmesi kesin olmayan kâğıtlarda " +
                           "\"Eşleşmeyi onayla\"yı işaretleyin ya da listeden doğru öğrenciyi seçin.";
            return;
        }

        var updates = new List<StudentAnswerUpdate>();
        var classicScores = 0;
        foreach (var row in rows)
        {
            // Çok kitapçıklı sınavda soru sırası kitapçığa göre değişir; kitapçık bilinmeden eşlenemez.
            var layout = ResolveLayout(row.BookletCode);
            if (layout == null)
            {
                ErrorMessage = $"\"{row.SourceLabel}\": kitapçık seçilmemiş. Bu sınavda soru sırası kitapçığa göre " +
                               "değiştiğinden kitapçık seçimi zorunludur.";
                return;
            }

            int? bookletId = layout.BookletId;

            if (layout.McSlots.Count > 0)
            {
                var answers = ParseAnswers(row.AnswersText);
                if (answers == null || answers.Count != layout.McSlots.Count)
                {
                    ErrorMessage = $"\"{row.SourceLabel}\": test cevap dizisi {layout.McSlots.Count} karakter olmalı " +
                                   "(A–E, '-' boş, '?' belirsiz).";
                    return;
                }

                for (var i = 0; i < layout.McSlots.Count; i++)
                {
                    updates.Add(new StudentAnswerUpdate
                    {
                        ExamId = _model.ExamId,
                        QuestionId = layout.McSlots[i].QuestionId,
                        StudentId = row.SelectedStudent!.StudentId,
                        BookletId = bookletId,
                        SelectedOption = answers[i],
                        Score = null
                    });
                }
            }

            foreach (var entry in row.Scores.Where(e => !e.IsMultipleChoice))
            {
                if (entry.HasInvalidText || entry.Value > entry.MaxPoints)
                {
                    ErrorMessage = $"\"{row.SourceLabel}\": soru {entry.Number} için \"{entry.Text}\" geçerli bir puan değil " +
                                   $"(0–{F(entry.MaxPoints)}).";
                    return;
                }
                if (entry.Value is not decimal score)
                    continue; // boş kutu: mevcut puan değiştirilmez

                updates.Add(new StudentAnswerUpdate
                {
                    ExamId = _model.ExamId,
                    QuestionId = layout.Slots[entry.Position].QuestionId,
                    StudentId = row.SelectedStudent!.StudentId,
                    BookletId = bookletId,
                    SelectedOption = OptionLetter.Empty,
                    Score = score
                });
                classicScores++;
            }
        }

        var skipped = Sheets.Count - rows.Count;
        var reviewCount = rows.Count(r => r.HasWarnings);
        var totalMismatch = rows.Count(r => r.TotalMismatch);
        var overwriting = rows.Count(r => _studentsWithAnswers.Contains(r.SelectedStudent!.StudentId));
        var samples = rows.Count(r => r.IsSample);
        var hasMc = _layouts.Count > 0 && _layouts[0].McSlots.Count > 0;
        var summary = $"{rows.Count} öğrencinin " +
                      (hasMc ? "test cevapları ve " : "") +
                      $"{classicScores} klasik soru puanı \"{_model.ExamTitle}\" sınavına kaydedilecek." +
                      (reviewCount > 0 ? $"\n\n{reviewCount} kâğıtta uyarı var (belirsiz puan, çift işaret, kısmi eşleşme vb.)." : "") +
                      (totalMismatch > 0 ? $"\n{totalMismatch} kâğıtta Toplam Not kutusu soru puanlarının toplamıyla uyuşmuyor." : "") +
                      (skipped > 0 ? $"\n{skipped} kâğıt öğrenci seçilmediği veya okunamadığı için atlanacak." : "") +
                      (overwriting > 0
                          ? $"\n\n{overwriting} öğrencinin bu sınavda daha önce girilmiş cevap/puanları var; okunan değerlerle değiştirilecek. " +
                            "Boş bırakılan puan kutuları mevcut puanı değiştirmez."
                          : "") +
                      (samples > 0
                          ? $"\n\nDİKKAT: {samples} kâğıt \"Örnek Kâğıt Üret ve Oku\" ile üretilmiş simülasyon kâğıdı. " +
                            "Kaydederseniz bu öğrencilerin gerçek cevap ve puanlarının yerine örnek değerler yazılır."
                          : "") +
                      "\n\nDevam edilsin mi?";
        if (ConfirmSave != null && !ConfirmSave(summary))
            return;

        IsSaving = true;
        try
        {
            // Kâğıdı okunan her öğrenci sınava girmiştir; her şeyi boş bıraktıysa da 0 puanla kaydedilir.
            var attended = rows.Select(r => r.SelectedStudent!.StudentId).ToList();
            var result = await _answerEntry.SaveAsync(_model.ExamId, updates, attended);
            foreach (var row in rows)
            {
                row.IsSaved = true;
                _studentsWithAnswers.Add(row.SelectedStudent!.StudentId);
            }

            var entries = rows.SelectMany(r => r.Scores).ToList();
            var approvedBoxes = entries.Count(e => e.IsFlagged && e.IsApproved);
            var correctedBoxes = entries.Count(e => e.WasCorrected);
            var approvedMatches = rows.Count(r => r.RequiresMatchApproval && r.IsMatchApproved);
            _log.Info("OptikOkuma",
                $"Kayıt: sınav {_model.ExamId}, {rows.Count} kâğıt, {entries.Count} puan kutusu; " +
                $"{approvedBoxes} kutu öğretmen onayıyla, {correctedBoxes} kutu düzeltilerek kaydedildi; " +
                $"{approvedMatches} kesin olmayan eşleşme onaylandı.");

            SuccessMessage = $"✓ {rows.Count} öğrencinin cevapları ve puanları kaydedildi; test soruları değerlendirildi. " +
                             (approvedBoxes + correctedBoxes > 0
                                 ? $"{approvedBoxes} okuma onaylanarak, {correctedBoxes} okuma düzeltilerek kaydedildi. "
                                 : "") +
                             (result.RubricBreakdownsCleared > 0
                                 ? $"{result.RubricBreakdownsCleared} klasik puanın eski rubrik dökümü yeni puanla uyuşmadığı için silindi. "
                                 : "") +
                             "Sınav Analizi ekranından sonuçları inceleyebilirsiniz.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Kaydetme başarısız: {ex.Message}";
            _log.Error("OptikOkuma", "Optik okuma sonuçları kaydedilemedi", ex);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>'ABD-? CA…' biçimindeki diziyi şık listesine çevirir (boşluklar yok sayılır); geçersiz karakterde null döner.</summary>
    private static List<OptionLetter>? ParseAnswers(string text)
    {
        var list = new List<OptionLetter>();
        foreach (var raw in text.Trim())
        {
            if (char.IsWhiteSpace(raw))
                continue;

            var ch = char.ToUpperInvariant(raw);
            if (ch is >= 'A' and <= 'E')
                list.Add((OptionLetter)(ch - 'A' + 1));
            else if (ch is '-' or '?' or '.')
                list.Add(OptionLetter.Empty);
            else
                return null;
        }
        return list;
    }

    /// <summary>Okunabilirlik için cevap dizisini 5'erli gruplar: "ABCDA BC-?D".</summary>
    private static string FormatAnswers(string compact)
        => string.Join(" ", Enumerable.Range(0, (compact.Length + 4) / 5)
            .Select(i => compact.Substring(i * 5, Math.Min(5, compact.Length - i * 5))));

    private static ImageSource? ToImage(byte[]? png)
    {
        if (png == null || png.Length == 0)
            return null;
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

    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(s.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
    }
}
