using EduAnalytics.Ocr.Models;

namespace EduAnalytics.Ocr.Reading;

/// <summary>Taranmış sınav kâğıdı dosyalarını okuyan servis.</summary>
public interface IOpticalFormReader
{
    /// <summary>Bu uzantı işlenebilir mi (pdf/png/jpg/...)?</summary>
    bool IsSupportedFile(string path);

    /// <summary>
    /// Dosyadaki her sayfayı okur. SourceLabel "dosyaadı (sayfa N)" biçimindedir. Devam sayfaları
    /// <see cref="SheetPageKind.Continuation"/> olarak döner (okunacak alanları yoktur).
    /// Ağır iştir; UI thread'inden çağrılmamalıdır.
    /// </summary>
    IReadOnlyList<(string SourceLabel, OpticalSheetResult Result)> ReadFile(string path, ExamPaperSpec spec);
}
