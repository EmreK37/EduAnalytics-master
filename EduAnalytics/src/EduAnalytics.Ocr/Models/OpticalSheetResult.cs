using EduAnalytics.Core.Enums;

namespace EduAnalytics.Ocr.Models;

/// <summary>Taranan sayfanın türü.</summary>
public enum SheetPageKind
{
    /// <summary>Sayfa bu uygulamanın sınav kâğıdı olarak tanınamadı (köşe işaretleri / sayfa kodu yok).</summary>
    Unknown,

    /// <summary>Okunan 1. sayfa: öğrenci numarası, puan tablosu, toplam ve test cevapları buradadır.</summary>
    FirstPage,

    /// <summary>Soru metinlerinin devam sayfası: okunacak alan yoktur, atlanır.</summary>
    Continuation
}

/// <summary>Tek bir taranmış sınav kâğıdı sayfasının okuma sonucu.</summary>
public class OpticalSheetResult
{
    /// <summary>Dört köşe hizalama işareti bulunup sayfa düzeltilebildi mi?</summary>
    public bool MarkersFound { get; set; }

    public SheetPageKind PageKind { get; set; } = SheetPageKind.Unknown;

    /// <summary>
    /// Numara kutularından el yazısı tanıma ile okunan öğrenci numarası. Boş ya da okunamayan kutu '?'
    /// ile gösterilir; baştaki/sondaki boş kutular numaraya dahil edilmez.
    /// </summary>
    public string RawStudentNumber { get; set; } = string.Empty;

    /// <summary>En belirsiz hanenin olasılığı (0–1).</summary>
    public double StudentNumberConfidence { get; set; }

    /// <summary>
    /// Her numara kutusu için 0–9 rakamlarının olasılıkları (boş kutu = null). Öğrenci eşleştirmesi tek
    /// tek haneler yerine bu olasılıklarla kayıtlı numaraların tamamını karşılaştırır.
    /// </summary>
    public IReadOnlyList<float[]?> StudentNumberDigits { get; set; } = Array.Empty<float[]?>();

    /// <summary>PaddleOCR'ın ad-soyad alanından okuduğu ham metin (yalnızca yardımcı bilgi).</summary>
    public string RawStudentName { get; set; } = string.Empty;
    public double StudentNameConfidence { get; set; }

    /// <summary>Kâğıttaki kitapçığın spec.BookletCodes içindeki index'i. null = okunamadı / tek kitapçık.</summary>
    public int? BookletIndex { get; set; }
    public double BookletConfidence { get; set; }

    /// <summary>Test sorularının kâğıttaki sırasıyla cevapları.</summary>
    public List<BubbleReadResult> Answers { get; } = new();

    /// <summary>"Alınan Not" satırındaki her kutunun okunuşu (kâğıttaki soru sırasıyla).</summary>
    public List<ScoreReading> Scores { get; } = new();

    /// <summary>Toplam Not kutusunun okunuşu.</summary>
    public ScoreReading? TotalScore { get; set; }

    /// <summary>"Soru Numarası / Alınan Not" tablosunun kırpılmış görüntüsü (PNG) — inceleme ekranında gösterilir.</summary>
    public byte[]? ScoreTableImage { get; set; }

    /// <summary>Numara kutularının kırpılmış görüntüsü (PNG).</summary>
    public byte[]? StudentNumberImage { get; set; }

    /// <summary>Toplam Not kutusunun kırpılmış görüntüsü (PNG).</summary>
    public byte[]? TotalScoreImage { get; set; }

    /// <summary>İnsan tarafından incelenmesi gereken durumların Türkçe açıklamaları.</summary>
    public List<string> Warnings { get; } = new();
}

/// <summary>Tek bir test sorusunun baloncuk satırının okuma sonucu.</summary>
public class BubbleReadResult
{
    public int FormIndex { get; set; }
    public OptionLetter Option { get; set; } = OptionLetter.Empty;

    /// <summary>0–1 arası; işaret ile ikinci en dolu baloncuk arasındaki ayrıma dayanır.</summary>
    public double Confidence { get; set; }

    /// <summary>Birden fazla baloncuk dolu bulundu; Option=Empty bırakılır, öğretmen karar verir.</summary>
    public bool MultipleMarks { get; set; }

    /// <summary>Dolu eşiğinin altında ama boş sayılamayacak kadar karalanmış (silik/silinmiş işaret).</summary>
    public bool WeakMark { get; set; }
}

/// <summary>El yazısıyla yazılmış bir puanın (tablo kutusu ya da Toplam Not) okunuşu.</summary>
public class ScoreReading
{
    /// <summary>Kâğıttaki soru sırası (0-bazlı); Toplam Not için -1.</summary>
    public int Position { get; set; }

    /// <summary>Okunan metin (ör. "25", "7,5"); kutu boşsa ya da okunamadıysa boş.</summary>
    public string Text { get; set; } = string.Empty;

    public decimal? Value { get; set; }

    /// <summary>0–1: seçilen okumanın olasılığı (tanıma güveni).</summary>
    public double Confidence { get; set; }

    /// <summary>Kutuda mürekkep yok.</summary>
    public bool IsEmpty { get; set; }

    /// <summary>Kutuda yazı var ama geçerli bir puan olarak okunamadı (ör. karalama, sınırı aşan değer).</summary>
    public bool Unreadable { get; set; }

    /// <summary>En olası ilk birkaç okuma (ör. "10" / "16") — kontrol ipucu olarak gösterilir.</summary>
    public IReadOnlyList<string> Alternatives { get; set; } = Array.Empty<string>();

    /// <summary>Bu güvenin altındaki okumalar incelemeye düşer.</summary>
    public const double LowConfidence = 0.6;

    /// <summary>
    /// Okuma incelenmeli mi? İncelenmesi gerekiyorsa Türkçe uyarı metni, gerekmiyorsa null.
    /// Uyarı okumaya bağlıdır; öğretmen değeri düzelttiğinde gösterilmez (bu yüzden sonuç listesine
    /// sabit olarak eklenmez, inceleme ekranı o anki duruma göre üretir).
    /// </summary>
    public string? Describe(string name, decimal maxValue)
    {
        if (IsEmpty)
            return null;
        if (Unreadable)
            return $"{name}: yazılan puan okunamadı" +
                   (Text.Length > 0 ? $" (\"{Text}\" — en fazla {Recognition.HandwrittenNumberReader.FormatScore(maxValue)} olabilir)" : "") +
                   " — elle girin.";
        if (Confidence < LowConfidence)
        {
            var alternatives = Alternatives.Skip(1).ToList();
            return $"{name}: puan belirsiz okundu ({Text}" +
                   (alternatives.Count > 0 ? $"; olası: {string.Join(", ", alternatives)}" : "") +
                   ") — kontrol edin.";
        }
        return null;
    }
}
