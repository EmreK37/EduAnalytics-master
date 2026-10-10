namespace EduAnalytics.Business.Helpers;

/// <summary>
/// Soru puanlarını ağırlık katsayılarına oranla verilen toplama ölçekler.
/// </summary>
public static class PointDistributor
{
    /// <summary>
    /// Her sorunun mevcut puanı o sorunun ağırlık katsayısı kabul edilir ve puanlar
    /// toplamı tam olarak <paramref name="total"/> olacak şekilde orantılı dağıtılır
    /// (örn. ağırlıklar 1:1:2 ve toplam 100 → 25, 25, 50).
    /// Yuvarlama farkı en büyük kesirli paya sahip sorulara 0.01'lik adımlarla dağıtılır;
    /// böylece toplam her zaman tam <paramref name="total"/> çıkar.
    /// Tüm ağırlıklar 0 veya negatifse puanlar eşit paylaştırılır.
    /// </summary>
    public static decimal[] ScaleToTotal(IReadOnlyList<decimal> weights, decimal total = 100m)
    {
        var count = weights.Count;
        if (count == 0)
            return Array.Empty<decimal>();

        var safeWeights = weights.Select(w => w > 0 ? w : 0m).ToArray();
        var weightSum = safeWeights.Sum();
        if (weightSum <= 0)
            safeWeights = Enumerable.Repeat(1m, count).ToArray();
        weightSum = safeWeights.Sum();

        // 0.01 hassasiyetinde çalış: toplam "kuruş" sayısı sabit, paylar tam sayı.
        var totalCents = (long)Math.Round(total * 100m, MidpointRounding.AwayFromZero);
        var exact = safeWeights.Select(w => w * totalCents / weightSum).ToArray();
        var cents = exact.Select(x => (long)Math.Floor(x)).ToArray();

        var remainder = (int)(totalCents - cents.Sum());
        var order = Enumerable.Range(0, count)
            .OrderByDescending(i => exact[i] - cents[i])
            .ThenBy(i => i)
            .ToArray();
        for (var k = 0; k < remainder; k++)
            cents[order[k % count]]++;

        return cents.Select(c => c / 100m).ToArray();
    }
}
