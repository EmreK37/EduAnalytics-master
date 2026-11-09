namespace EduAnalytics.Ocr;

/// <summary>
/// Bir sınav kâğıdının öğrenci ve öğretmen tarafından "doldurulmuş" hâli. Örnek/test kâğıdı üretmek için
/// kullanılır (<see cref="ExamPaperSampleRenderer"/>): ad, numara, bölüm ve puanlar el yazısıyla yazılır,
/// test cevapları kurşun kalemle işaretlenir.
/// </summary>
public sealed class ExamPaperFill
{
    /// <summary>El yazısının rastgeleliği (aynı tohum aynı kâğıdı üretir).</summary>
    public int Seed { get; init; } = 1;

    public string? StudentName { get; init; }

    /// <summary>Numara kutularına yazılacak rakamlar (en fazla 12). null = boş bırakıldı.</summary>
    public string? StudentNumber { get; init; }

    public string? Department { get; init; }

    /// <summary>İmza alanına karalama çizilsin mi?</summary>
    public bool Signed { get; init; } = true;

    /// <summary>
    /// Test sorularının kâğıttaki sırasıyla işaretlenen şıkları (0 = A … 4 = E).
    /// Boş liste = boş bırakıldı; iki veya daha fazla öğe = çift işaret.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>> Answers { get; init; } = Array.Empty<IReadOnlyList<int>>();

    /// <summary>Silik (yarım basılmış) işaret olarak çizilecek test satırları.</summary>
    public IReadOnlyCollection<int> WeakMarkRows { get; init; } = Array.Empty<int>();

    /// <summary>
    /// Öğretmenin "Alınan Not" satırına yazdığı puanlar (kâğıttaki soru sırasıyla; ör. "25", "7,5").
    /// null veya boş = kutu boş bırakıldı.
    /// </summary>
    public IReadOnlyList<string?> Scores { get; init; } = Array.Empty<string?>();

    /// <summary>Toplam Not kutusuna yazılan değer (null = boş).</summary>
    public string? Total { get; init; }

    /// <summary>Paraf alanına karalama çizilsin mi?</summary>
    public bool Initialed { get; init; } = true;
}
