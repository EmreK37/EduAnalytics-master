namespace EduAnalytics.Ocr;

/// <summary>Puan tablosu değerlendirmesinde tek bir soru (kâğıttaki sırasıyla).</summary>
/// <param name="Number">Kâğıttaki soru numarası.</param>
/// <param name="IsMultipleChoice">Test sorusu mu?</param>
/// <param name="MaxPoints">Sorunun puanı.</param>
/// <param name="OpticalPoints">
/// Test sorusunda optik işaretten hesaplanan puan (doğruysa sorunun puanı, değilse 0).
/// Hesaplanamıyorsa (ör. kitapçık bilinmiyor) null; klasik soruda her zaman null.
/// </param>
/// <param name="TableScore">"Alınan Not" kutusundaki (okunan ya da öğretmenin düzelttiği) puan; boşsa null.</param>
public sealed record QuestionScore(int Number, bool IsMultipleChoice, decimal MaxPoints, decimal? OpticalPoints, decimal? TableScore);

/// <summary>Bir kâğıdın puan değerlendirmesi.</summary>
public sealed class ScoreSheetEvaluation
{
    /// <summary>Kaydedilecek/hesaba katılacak puanlar (kâğıttaki sırasıyla): test → optik, klasik → tablo.</summary>
    public IReadOnlyList<decimal?> EffectiveScores { get; init; } = Array.Empty<decimal?>();

    /// <summary>Etkin puanların toplamı (boş klasik sorular 0 sayılır).</summary>
    public decimal ComputedTotal { get; init; }

    public decimal MaxTotal { get; init; }

    /// <summary>Toplam Not kutusundaki değer ile hesaplanan toplam uyuşuyor mu? Kutu boşsa null.</summary>
    public bool? TotalMatches { get; init; }

    /// <summary>Puanı girilmemiş (kutusu boş) klasik soru sayısı.</summary>
    public int MissingClassicScores { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Puan tablosunu sınavın yapısıyla karşılaştırır:
///   • Klasik soruların puanı doğrudan "Alınan Not" kutusundan alınır.
///   • Test sorularının puanı optik işaretten hesaplanır; tabloda puan yazılmışsa bununla doğrulanır.
///   • Toplam Not kutusu, soru puanlarının toplamıyla karşılaştırılır.
/// Okuyucudan bağımsızdır: inceleme ekranında öğretmen bir puanı düzelttiğinde yeniden çalıştırılır.
/// </summary>
public static class ScoreSheetEvaluator
{
    public static ScoreSheetEvaluation Evaluate(IReadOnlyList<QuestionScore> questions, decimal? writtenTotal)
    {
        var warnings = new List<string>();
        var effective = new List<decimal?>(questions.Count);
        var missing = 0;

        foreach (var q in questions)
        {
            if (q.TableScore is decimal written && (written < 0 || written > q.MaxPoints))
                warnings.Add($"Soru {q.Number}: tablodaki puan ({Format(written)}) sorunun puanını ({Format(q.MaxPoints)}) aşıyor.");

            if (q.IsMultipleChoice)
            {
                effective.Add(q.OpticalPoints);
                if (q.TableScore is decimal table && q.OpticalPoints is decimal optical && table != optical)
                {
                    warnings.Add($"Soru {q.Number} (test): tabloda {Format(table)} yazıyor, optik okumaya göre {Format(optical)} puan — " +
                                 "test puanı optik işaretten hesaplanır; işareti veya tabloyu kontrol edin.");
                }
            }
            else
            {
                effective.Add(q.TableScore);
                if (q.TableScore == null)
                {
                    missing++;
                    warnings.Add($"Soru {q.Number} (klasik): \"Alınan Not\" kutusu boş — bu sorunun puanı kaydedilmeyecek.");
                }
            }
        }

        var computed = effective.Sum(s => s ?? 0);
        bool? matches = null;
        if (writtenTotal is decimal total)
        {
            matches = total == computed;
            if (matches == false)
                warnings.Add($"Toplam Not kutusunda {Format(total)} yazıyor; soru puanlarının toplamı {Format(computed)} — kontrol edin.");
        }
        else
        {
            warnings.Add("Toplam Not kutusu boş — puan toplamı karşılaştırılamadı.");
        }

        return new ScoreSheetEvaluation
        {
            EffectiveScores = effective,
            ComputedTotal = computed,
            MaxTotal = questions.Sum(q => q.MaxPoints),
            TotalMatches = matches,
            MissingClassicScores = missing,
            Warnings = warnings
        };
    }

    private static string Format(decimal value) => Recognition.HandwrittenNumberReader.FormatScore(value);
}
