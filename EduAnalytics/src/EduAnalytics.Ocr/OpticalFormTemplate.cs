namespace EduAnalytics.Ocr;

public readonly record struct MmPoint(double X, double Y);

public readonly record struct MmRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public MmPoint Center => new(X + Width / 2, Y + Height / 2);
    public MmRect Deflate(double d) => new(X + d, Y + d, Width - 2 * d, Height - 2 * d);
}

/// <summary>
/// Sınav kâğıdının sayfa düzeyindeki sabitleri. Sayfa A4 (210×297 mm), koordinatlar sol üst köşeden
/// mm cinsinden ölçülür. Birinci sayfanın ayrıntılı geometrisi <see cref="ExamPaperLayout"/>'tadır;
/// PDF üretici ve okuyucu AYNI sınıfları kullanır, form düzeni yalnızca bu iki dosyada değişir.
///
/// Sürüm 3: resmi üniversite sınav kâğıdı düzeni (antet, öğrenci bilgileri, Toplam Not – Paraf kutusu,
/// ders tablosu, kurallar, "Soru Numarası / Alınan Not" puan tablosu, SORULAR). Öğrenci numarası
/// 12 kutuya el yazısıyla yazılır; test soruları için optik cevap alanı bulunur. Sürüm 2 optik
/// formları bu okuyucuyla okunamaz.
/// </summary>
public static class OpticalFormTemplate
{
    public const int Version = 3;

    public const double PageWidthMm = 210;
    public const double PageHeightMm = 297;

    /// <summary>Okuyucunun homografi sonrası çalıştığı kanonik çözünürlük (~203 DPI).</summary>
    public const double PxPerMm = 8;

    public static int PageWidthPx => (int)(PageWidthMm * PxPerMm);
    public static int PageHeightPx => (int)(PageHeightMm * PxPerMm);

    // ── Köşe hizalama işaretleri (içi dolu kare) — her sayfada ──
    public const double MarkerSizeMm = 6;

    /// <summary>Sıra: Sol-üst, Sağ-üst, Sol-alt, Sağ-alt (kare merkezleri).</summary>
    public static readonly MmPoint[] MarkerCenters =
    {
        new(12, 12),
        new(198, 12),
        new(12, 285),
        new(198, 285)
    };

    // ── Sayfa kodu (sol kenar boşluğu) ──
    // Köşe işaretleri 180° dönüşe göre simetrik olduğundan kâğıdın yönü ve sayfa türü bu iki küçük
    // kareden okunur: yön karesi her sayfada doludur (ters taranan sayfada sağ kenara düşer),
    // ilk sayfa karesi yalnızca okunacak 1. sayfada doludur (devam sayfalarında boş çerçeve).
    public const double PageCodeSizeMm = 3.6;
    public static readonly MmPoint OrientationSlot = new(12, 138);
    public static readonly MmPoint FirstPageSlot = new(12, 145);

    // ── İçerik alanı ──
    public const double ContentLeftMm = 17;
    public const double ContentRightMm = 193;
    public const double ContentWidthMm = ContentRightMm - ContentLeftMm;

    /// <summary>Sayfa altında sayfa numarası ve alt köşe işaretleri için ayrılan yükseklik.</summary>
    public const double FooterHeightMm = 16;

    /// <summary>Devam sayfalarının üst bilgisi (köşe işaretleri + "devam" satırı) için ayrılan yükseklik.</summary>
    public const double ContinuationHeaderHeightMm = 24;

    // ── Kapasite ──
    public const int MaxQuestions = 60;
    public const int OptionCount = 5;          // test sorularında her zaman A–E basılır
    public const int StudentNumberCells = 12;  // öğrenci no hane sayısı
    public const int MaxBooklets = 5;

    // ── Baloncuklar ──
    public const double BubbleRadiusMm = 1.8;
}
