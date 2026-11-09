using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using EduAnalytics.Ocr.Models;
using EduAnalytics.Ocr.Recognition;

namespace EduAnalytics.UI.ViewModels;

/// <summary>Optik okuma inceleme ızgarasında öğrenci seçimi için combo öğesi.</summary>
public class StudentPickItem
{
    public int StudentId { get; init; }
    public string StudentNumber { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Display => $"{StudentNumber} — {FullName}";
}

/// <summary>İnceleme satırının durumu (renkli rozet olarak gösterilir).</summary>
public enum SheetStatus
{
    /// <summary>Öğrenci eşleşti, uyarı yok — kaydedilmeye hazır.</summary>
    Ready,
    /// <summary>Kaydedilebilir ama uyarılar var (belirsiz puan, toplam uyuşmazlığı, çift işaret…).</summary>
    Review,
    /// <summary>Öğrenci seçilmeden kaydedilemez.</summary>
    NeedsStudent,
    /// <summary>Belirsiz okunan kutular ya da kesin olmayan öğrenci eşleşmesi onaylanmadan kaydedilemez.</summary>
    NeedsApproval,
    /// <summary>Sayfa sınav kâğıdı olarak tanınamadı; kaydedilemez.</summary>
    Unreadable,
    /// <summary>Cevaplar ve puanlar veritabanına kaydedildi.</summary>
    Saved
}

/// <summary>
/// "Alınan Not" satırındaki tek bir kutu: okunan puan düzenlenebilir. Test sorusunda kaydedilen puan optik
/// işaretten hesaplanır (kutudaki değer yalnızca doğrulama içindir); klasik soruda kutudaki değer kaydedilir.
/// </summary>
public partial class ScoreEntryViewModel : ObservableObject
{
    /// <summary>Kâğıttaki sıra (0-bazlı).</summary>
    public int Position { get; init; }

    /// <summary>Kâğıtta basılan soru numarası.</summary>
    public int Number { get; init; }

    /// <summary>Kitapçığa bağlıdır (karışık kitapçıkta k. kutu farklı soru olabilir); kitapçık değişince güncellenir.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Caption))]
    private bool _isMultipleChoice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Caption))]
    private decimal _maxPoints;

    /// <summary>Okuyucunun bulduğu değer ve güveni (düzenlemeden önce).</summary>
    public string ReadText { get; init; } = string.Empty;
    public double ReadConfidence { get; init; }
    public bool ReadUnreadable { get; init; }
    public IReadOnlyList<string> ReadAlternatives { get; init; } = Array.Empty<string>();

    /// <summary>Kutudaki puan (öğretmen düzeltebilir). Boş = kutu boş.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Value))]
    [NotifyPropertyChangedFor(nameof(HasInvalidText))]
    private string _text = string.Empty;

    /// <summary>Test sorusunda optik işaretten hesaplanan puan (null: hesaplanamadı / klasik).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Caption))]
    private decimal? _opticalPoints;

    /// <summary>İncelenmesi gereken kutu (belirsiz/okunamayan, tabloyla optik uyuşmazlığı, boş klasik).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsApproval))]
    private bool _isFlagged;

    [ObservableProperty] private string _flagReason = string.Empty;

    /// <summary>
    /// Öğretmen kutuyu taranan görüntüyle karşılaştırıp okunan değeri onayladı mı? İşaretli (sarı) kutular
    /// onaylanmadan ya da düzeltilmeden kâğıt kaydedilemez. Kutudaki değer değişince onay düşer.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsApproval))]
    private bool _isApproved;

    /// <summary>Kutu işaretli ve henüz onaylanmamış.</summary>
    public bool NeedsApproval => IsFlagged && !IsApproved;

    /// <summary>Öğretmen okunan değeri değiştirdi mi (düzeltme)?</summary>
    public bool WasCorrected => Text != ReadText;

    partial void OnTextChanged(string value) => IsApproved = false;

    public string Header => Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string Caption => IsMultipleChoice
        ? OpticalPoints is decimal points
            ? $"test · optik {HandwrittenNumberReader.FormatScore(points)}"
            : "test"
        : $"klasik · /{HandwrittenNumberReader.FormatScore(MaxPoints)}";

    /// <summary>Kutudaki metnin puan değeri (virgül ya da nokta kabul edilir); boş ya da geçersizse null.</summary>
    public decimal? Value => ParseInput(Text);

    public bool HasInvalidText => !string.IsNullOrWhiteSpace(Text) && Value == null;

    public static decimal? ParseInput(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim().Replace('.', ',');
        return trimmed.Length == 0 ? null : HandwrittenNumberReader.ParseScore(trimmed);
    }
}

/// <summary>
/// Taranmış tek bir sınav kâğıdının inceleme satırı. Okuma sonuçları düzenlenebilir: öğretmen eşleşen
/// öğrenciyi, kitapçığı, test cevaplarını ve "Alınan Not" puanlarını düzeltebilir.
/// </summary>
public partial class ScannedSheetRowViewModel : ObservableObject
{
    /// <summary>Dosya adı (+ sayfa numarası).</summary>
    public string SourceLabel { get; init; } = string.Empty;

    /// <summary>Tanınamayan sayfalar kaydedilemez.</summary>
    public bool MarkersFound { get; init; }

    /// <summary>Kâğıttan okunan ham numara (el yazısı kutular) ve ad (OCR).</summary>
    public string OcrNumber { get; init; } = string.Empty;
    public string OcrName { get; init; } = string.Empty;

    /// <summary>Eşleştirmenin nasıl yapıldığının açıklaması (birebir / yaklaşık / elle).</summary>
    public string MatchInfo { get; init; } = string.Empty;

    /// <summary>0–1: numara, işaretli cevaplar ve okunan puanların en düşük güveni.</summary>
    public double OverallConfidence { get; init; }
    public string ConfidenceDisplay => MarkersFound ? $"%{OverallConfidence * 100:0}" : "—";

    /// <summary>Okuma ve eşleştirmeden gelen uyarılar (puan değerlendirmesi uyarıları ayrıca eklenir).</summary>
    public IReadOnlyList<string> BaseWarnings { get; init; } = Array.Empty<string>();

    /// <summary>Kırpılmış görüntüler: puan tablosu, numara kutuları, Toplam Not kutusu.</summary>
    public ImageSource? ScoreTableImage { get; init; }
    public ImageSource? NumberImage { get; init; }
    public ImageSource? TotalImage { get; init; }

    /// <summary>"Alınan Not" kutuları (kâğıttaki soru sırasıyla).</summary>
    public ObservableCollection<ScoreEntryViewModel> Scores { get; } = new();
    public bool HasScores => Scores.Count > 0;

    /// <summary>"Örnek Kâğıt Üret ve Oku" ile üretilmiş simülasyon sayfası (kaydetmeden önce ayrıca uyarılır).</summary>
    public bool IsSample { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private StudentPickItem? _selectedStudent;

    [ObservableProperty] private string? _bookletCode;

    /// <summary>Test soruları için: soru başına tek karakter A–E, '-' boş, '?' çift işaret. Elle düzeltilebilir.</summary>
    [ObservableProperty] private string _answersText = string.Empty;

    /// <summary>Toplam Not kutusunun okunuşu (düzenlemeden önce; uyarı ve ipucu için).</summary>
    public ScoreReading? TotalReading { get; init; }

    /// <summary>Toplam Not kutusundan okunan (düzenlenebilir) değer.</summary>
    [ObservableProperty] private string _totalText = string.Empty;

    /// <summary>Test özeti, klasik ve genel toplam.</summary>
    [ObservableProperty] private string _resultText = string.Empty;

    /// <summary>Hesaplanan toplamın Toplam Not kutusuyla karşılaştırması (ör. "Hesaplanan 62,5 / 100 ✓").</summary>
    [ObservableProperty] private string _totalCheckText = string.Empty;
    [ObservableProperty] private bool _totalMismatch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarnings))]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _warningsText = string.Empty;

    public bool HasWarnings => !string.IsNullOrWhiteSpace(WarningsText);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isSaved;

    /// <summary>
    /// Öğrenci numarası yaklaşık, kısmen ya da yalnızca ad benzerliğiyle eşleştiyse öğretmen eşleşmeyi
    /// onaylamalı (ya da listeden doğru öğrenciyi seçmeli).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsMatchApproval))]
    private bool _requiresMatchApproval;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsMatchApproval))]
    private bool _isMatchApproved;

    public bool NeedsMatchApproval => RequiresMatchApproval && !IsMatchApproved && SelectedStudent != null;

    /// <summary>Onay bekleyen okuma sayısı: onaylanmamış işaretli kutular + onaylanmamış kesin olmayan eşleşme.</summary>
    public int PendingApprovalCount => Scores.Count(s => s.NeedsApproval) + (NeedsMatchApproval ? 1 : 0);
    public bool HasPendingApprovals => PendingApprovalCount > 0;

    public string ApprovalText => PendingApprovalCount switch
    {
        0 => string.Empty,
        1 => "Onay bekleyen 1 okuma var.",
        var n => $"Onay bekleyen {n} okuma var."
    };

    public SheetStatus Status =>
        !MarkersFound ? SheetStatus.Unreadable
        : IsSaved ? SheetStatus.Saved
        : SelectedStudent == null ? SheetStatus.NeedsStudent
        : HasPendingApprovals ? SheetStatus.NeedsApproval
        : HasWarnings ? SheetStatus.Review
        : SheetStatus.Ready;

    public string StatusText => Status switch
    {
        SheetStatus.Ready => "Hazır",
        SheetStatus.Review => "Kontrol et",
        SheetStatus.NeedsStudent => "Öğrenci seçin",
        SheetStatus.NeedsApproval => "Onay bekliyor",
        SheetStatus.Unreadable => "Okunamadı",
        SheetStatus.Saved => "Kaydedildi",
        _ => string.Empty
    };

    public ScannedSheetRowViewModel()
    {
        Scores.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (ScoreEntryViewModel entry in e.NewItems)
                    entry.PropertyChanged += OnScoreEntryPropertyChanged;
            }
            if (e.OldItems != null)
            {
                foreach (ScoreEntryViewModel entry in e.OldItems)
                    entry.PropertyChanged -= OnScoreEntryPropertyChanged;
            }
            RefreshApprovalState();
        };
    }

    private void OnScoreEntryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScoreEntryViewModel.NeedsApproval))
            RefreshApprovalState();
    }

    private void RefreshApprovalState()
    {
        OnPropertyChanged(nameof(PendingApprovalCount));
        OnPropertyChanged(nameof(HasPendingApprovals));
        OnPropertyChanged(nameof(ApprovalText));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
    }

    // Öğretmen satırı düzenlerse önceki "kaydedildi" durumu artık geçerli değildir.
    partial void OnSelectedStudentChanged(StudentPickItem? value)
    {
        IsSaved = false;
        // Öğretmenin listeden yaptığı seçim, eşleşmenin onayı sayılır.
        IsMatchApproved = true;
        OnPropertyChanged(nameof(NeedsMatchApproval));
        RefreshApprovalState();
    }

    partial void OnRequiresMatchApprovalChanged(bool value) => RefreshApprovalState();

    partial void OnIsMatchApprovedChanged(bool value)
    {
        IsSaved = false;
        RefreshApprovalState();
    }

    partial void OnBookletCodeChanged(string? value) => IsSaved = false;
    partial void OnAnswersTextChanged(string value) => IsSaved = false;
    partial void OnTotalTextChanged(string value) => IsSaved = false;
}
