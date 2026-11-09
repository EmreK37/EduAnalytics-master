namespace EduAnalytics.Ocr.Recognition;

/// <summary>
/// Küçük bir evrişimli sinir ağının (LeNet benzeri) ağırlıkları. Mimari sabittir:
/// 28×28 → conv 5×5 (12) → ReLU → maxpool 2 → conv 5×5 (24) → ReLU → maxpool 2 → 384
/// → tam bağlı 96 → ReLU → tam bağlı 10 (0–9 rakamları).
/// Eğitim aracı bu sınıfı doldurup <see cref="Write"/> ile kaydeder; okuyucu gömülü dosyayı yükler.
/// </summary>
public sealed class DigitModelWeights
{
    public const int Conv1Channels = 12;
    public const int Conv2Channels = 24;
    public const int Kernel = 5;
    public const int Hidden = 96;
    public const int Classes = 10;

    public const int Conv1Out = DigitNormalizer.Size - Kernel + 1;   // 24
    public const int Pool1Out = Conv1Out / 2;                        // 12
    public const int Conv2Out = Pool1Out - Kernel + 1;               // 8
    public const int Pool2Out = Conv2Out / 2;                        // 4
    public const int Flat = Conv2Channels * Pool2Out * Pool2Out;     // 384

    private const string Magic = "EDDC";
    private const int FormatVersion = 1;

    public float[] W1 { get; } = new float[Conv1Channels * Kernel * Kernel];
    public float[] B1 { get; } = new float[Conv1Channels];
    public float[] W2 { get; } = new float[Conv2Channels * Conv1Channels * Kernel * Kernel];
    public float[] B2 { get; } = new float[Conv2Channels];
    public float[] W3 { get; } = new float[Hidden * Flat];
    public float[] B3 { get; } = new float[Hidden];
    public float[] W4 { get; } = new float[Classes * Hidden];
    public float[] B4 { get; } = new float[Classes];

    /// <summary>Eğitimde ve dosya okuma/yazmada kullanılan sabit sıra.</summary>
    public float[][] Tensors => new[] { W1, B1, W2, B2, W3, B3, W4, B4 };

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write(Magic.ToCharArray());
        writer.Write(FormatVersion);
        foreach (var tensor in Tensors)
        {
            writer.Write(tensor.Length);
            foreach (var value in tensor)
                writer.Write(value);
        }
    }

    public static DigitModelWeights Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        if (new string(reader.ReadChars(Magic.Length)) != Magic || reader.ReadInt32() != FormatVersion)
            throw new InvalidDataException("Rakam modeli dosyası tanınmadı.");

        var weights = new DigitModelWeights();
        foreach (var tensor in weights.Tensors)
        {
            if (reader.ReadInt32() != tensor.Length)
                throw new InvalidDataException("Rakam modeli dosyasının mimarisi bu sürümle uyuşmuyor.");
            for (var i = 0; i < tensor.Length; i++)
                tensor[i] = reader.ReadSingle();
        }
        return weights;
    }
}

/// <summary>
/// El yazısı tek rakam sınıflandırıcısı. Ağırlıklar derlemeye gömülüdür (Recognition/digits-cnn.bin);
/// internet ya da ek paket gerekmez. Model MNIST el yazısı rakamları ve yazı tipi örnekleriyle,
/// tarama koşullarına benzetilmiş bozulmalarla eğitilmiştir (bkz. tools/EduAnalytics.DigitTrainer).
/// Örnek başına ayrı tampon kullandığı için iş parçacığı açısından güvenlidir.
/// </summary>
public sealed class DigitClassifier
{
    private const string ResourceName = "EduAnalytics.Ocr.Recognition.digits-cnn.bin";

    private static readonly Lazy<DigitClassifier> _default = new(LoadEmbedded);

    private readonly DigitModelWeights _w;

    public DigitClassifier(DigitModelWeights weights) => _w = weights;

    /// <summary>Derlemeye gömülü eğitilmiş model.</summary>
    public static DigitClassifier Default => _default.Value;

    private static DigitClassifier LoadEmbedded()
    {
        using var stream = typeof(DigitClassifier).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Gömülü rakam modeli bulunamadı: {ResourceName}");
        return new DigitClassifier(DigitModelWeights.Read(stream));
    }

    /// <summary>
    /// <see cref="DigitNormalizer.Normalize"/> çıktısını (784 değer) sınıflandırır;
    /// 0–9 rakamlarının olasılıklarını (softmax) döner.
    /// </summary>
    public float[] Predict(ReadOnlySpan<float> input)
    {
        const int k = DigitModelWeights.Kernel;
        const int n0 = DigitNormalizer.Size;
        const int c1 = DigitModelWeights.Conv1Channels, o1 = DigitModelWeights.Conv1Out, p1 = DigitModelWeights.Pool1Out;
        const int c2 = DigitModelWeights.Conv2Channels, o2 = DigitModelWeights.Conv2Out, p2 = DigitModelWeights.Pool2Out;

        if (input.Length != DigitNormalizer.PixelCount)
            throw new ArgumentException("Girdi 28×28 olmalıdır.", nameof(input));

        // conv1 + ReLU + maxpool → pool1[c1][12][12]
        var pool1 = new float[c1 * p1 * p1];
        var conv1 = new float[o1 * o1];
        for (var oc = 0; oc < c1; oc++)
        {
            for (var y = 0; y < o1; y++)
            {
                for (var x = 0; x < o1; x++)
                {
                    var sum = _w.B1[oc];
                    for (var ky = 0; ky < k; ky++)
                    {
                        var rowIn = (y + ky) * n0 + x;
                        var rowW = (oc * k + ky) * k;
                        for (var kx = 0; kx < k; kx++)
                            sum += _w.W1[rowW + kx] * input[rowIn + kx];
                    }
                    conv1[y * o1 + x] = sum > 0 ? sum : 0;
                }
            }
            MaxPool(conv1, o1, pool1, oc * p1 * p1);
        }

        // conv2 + ReLU + maxpool → flat[384]
        var flat = new float[DigitModelWeights.Flat];
        var conv2 = new float[o2 * o2];
        for (var oc = 0; oc < c2; oc++)
        {
            for (var y = 0; y < o2; y++)
            {
                for (var x = 0; x < o2; x++)
                {
                    var sum = _w.B2[oc];
                    for (var ic = 0; ic < c1; ic++)
                    {
                        var inBase = ic * p1 * p1;
                        var wBase = (oc * c1 + ic) * k * k;
                        for (var ky = 0; ky < k; ky++)
                        {
                            var rowIn = inBase + (y + ky) * p1 + x;
                            var rowW = wBase + ky * k;
                            for (var kx = 0; kx < k; kx++)
                                sum += _w.W2[rowW + kx] * pool1[rowIn + kx];
                        }
                    }
                    conv2[y * o2 + x] = sum > 0 ? sum : 0;
                }
            }
            MaxPool(conv2, o2, flat, oc * p2 * p2);
        }

        // Tam bağlı katmanlar
        var hidden = new float[DigitModelWeights.Hidden];
        for (var h = 0; h < hidden.Length; h++)
        {
            var sum = _w.B3[h];
            var row = h * DigitModelWeights.Flat;
            for (var i = 0; i < flat.Length; i++)
                sum += _w.W3[row + i] * flat[i];
            hidden[h] = sum > 0 ? sum : 0;
        }

        var logits = new float[DigitModelWeights.Classes];
        for (var c = 0; c < logits.Length; c++)
        {
            var sum = _w.B4[c];
            var row = c * DigitModelWeights.Hidden;
            for (var h = 0; h < hidden.Length; h++)
                sum += _w.W4[row + h] * hidden[h];
            logits[c] = sum;
        }

        return Softmax(logits);
    }

    private static void MaxPool(float[] src, int size, float[] dst, int dstOffset)
    {
        var half = size / 2;
        for (var y = 0; y < half; y++)
        {
            for (var x = 0; x < half; x++)
            {
                var i = 2 * y * size + 2 * x;
                dst[dstOffset + y * half + x] = Math.Max(Math.Max(src[i], src[i + 1]),
                                                         Math.Max(src[i + size], src[i + size + 1]));
            }
        }
    }

    public static float[] Softmax(float[] logits)
    {
        var max = logits.Max();
        var result = new float[logits.Length];
        float sum = 0;
        for (var i = 0; i < logits.Length; i++)
        {
            result[i] = MathF.Exp(logits[i] - max);
            sum += result[i];
        }
        for (var i = 0; i < result.Length; i++)
            result[i] /= sum;
        return result;
    }
}
