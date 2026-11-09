using EduAnalytics.Ocr.Recognition;

namespace EduAnalytics.DigitTrainer;

/// <summary>
/// <see cref="DigitModelWeights"/> mimarisinin eğitimi: ileri/geri yayılım (elle yazılmış, bağımlılıksız),
/// mini-batch paralel gradyan, Adam. Çıkarımdaki ileri geçişle birebir aynı hesaplar; eğitim sonunda
/// doğruluk ayrıca <see cref="DigitClassifier"/> ile ölçülerek bu eşitlik doğrulanır.
/// </summary>
internal sealed class Trainer
{
    private const int K = DigitModelWeights.Kernel;
    private const int N0 = 28;
    private const int C1 = DigitModelWeights.Conv1Channels, O1 = DigitModelWeights.Conv1Out, P1 = DigitModelWeights.Pool1Out;
    private const int C2 = DigitModelWeights.Conv2Channels, O2 = DigitModelWeights.Conv2Out, P2 = DigitModelWeights.Pool2Out;
    private const int Flat = DigitModelWeights.Flat, Hidden = DigitModelWeights.Hidden, Classes = DigitModelWeights.Classes;

    public DigitModelWeights Weights { get; }

    private readonly float[][] _m;
    private readonly float[][] _v;
    private int _step;

    public float DropoutRate { get; init; } = 0.25f;
    public float LabelSmoothing { get; init; } = 0.05f;
    public float WeightDecay { get; init; } = 1e-4f;

    public Trainer(int seed)
    {
        Weights = new DigitModelWeights();
        var rng = new Random(seed);
        Init(Weights.W1, fanIn: K * K, rng);
        Init(Weights.W2, fanIn: C1 * K * K, rng);
        Init(Weights.W3, fanIn: Flat, rng);
        Init(Weights.W4, fanIn: Hidden, rng, gain: 1.0);
        _m = Weights.Tensors.Select(t => new float[t.Length]).ToArray();
        _v = Weights.Tensors.Select(t => new float[t.Length]).ToArray();
    }

    private static void Init(float[] w, int fanIn, Random rng, double gain = 2.0)
    {
        var std = Math.Sqrt(gain / fanIn);
        for (var i = 0; i < w.Length; i++)
        {
            var u1 = 1.0 - rng.NextDouble();
            var u2 = rng.NextDouble();
            w[i] = (float)(std * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        }
    }

    /// <summary>İş parçacığına özel ara bellekler ve gradyan toplayıcıları.</summary>
    private sealed class Workspace
    {
        public readonly float[] Conv1 = new float[C1 * O1 * O1];
        public readonly float[] Pool1 = new float[C1 * P1 * P1];
        public readonly int[] Pool1Arg = new int[C1 * P1 * P1];
        public readonly float[] Conv2 = new float[C2 * O2 * O2];
        public readonly float[] Pool2 = new float[Flat];
        public readonly int[] Pool2Arg = new int[Flat];
        public readonly float[] Hidden = new float[Trainer.Hidden];
        public readonly float[] HiddenPre = new float[Trainer.Hidden];
        public readonly float[] Drop = new float[Trainer.Hidden];
        public readonly float[] Probs = new float[Classes];

        public readonly float[] DHidden = new float[Trainer.Hidden];
        public readonly float[] DFlat = new float[Flat];
        public readonly float[] DConv2 = new float[C2 * O2 * O2];
        public readonly float[] DPool1 = new float[C1 * P1 * P1];
        public readonly float[] DConv1 = new float[C1 * O1 * O1];

        public readonly float[][] Grads;
        public double Loss;
        public int Correct;
        public readonly Random Rng;

        public Workspace(DigitModelWeights w, int seed)
        {
            Grads = w.Tensors.Select(t => new float[t.Length]).ToArray();
            Rng = new Random(seed);
        }

        public void Reset()
        {
            foreach (var g in Grads)
                Array.Clear(g);
            Loss = 0;
            Correct = 0;
        }
    }

    /// <summary>Bir mini-batch üzerinde gradyan hesaplar ve Adam ile günceller. Ortalama kaybı döner.</summary>
    public (double Loss, int Correct) TrainBatch(IReadOnlyList<(float[] Input, int Label)> batch, float learningRate)
    {
        var total = Weights.Tensors.Select(t => new float[t.Length]).ToArray();
        double loss = 0;
        var correct = 0;
        var gate = new object();

        Parallel.ForEach(
            System.Collections.Concurrent.Partitioner.Create(0, batch.Count, Math.Max(1, batch.Count / Environment.ProcessorCount)),
            () => new Workspace(Weights, Random.Shared.Next()),
            (range, _, ws) =>
            {
                for (var i = range.Item1; i < range.Item2; i++)
                    ForwardBackward(batch[i].Input, batch[i].Label, ws);
                return ws;
            },
            ws =>
            {
                lock (gate)
                {
                    for (var t = 0; t < total.Length; t++)
                    {
                        var dst = total[t];
                        var src = ws.Grads[t];
                        for (var i = 0; i < dst.Length; i++)
                            dst[i] += src[i];
                    }
                    loss += ws.Loss;
                    correct += ws.Correct;
                }
            });

        Adam(total, batch.Count, learningRate);
        return (loss / batch.Count, correct);
    }

    private void Adam(float[][] grads, int batchSize, float lr)
    {
        const float beta1 = 0.9f, beta2 = 0.999f, eps = 1e-8f;
        _step++;
        var c1 = 1 - MathF.Pow(beta1, _step);
        var c2 = 1 - MathF.Pow(beta2, _step);
        var tensors = Weights.Tensors;
        for (var t = 0; t < tensors.Length; t++)
        {
            var w = tensors[t];
            var g = grads[t];
            var m = _m[t];
            var v = _v[t];
            var decay = t % 2 == 0 ? WeightDecay : 0f;   // yalnızca ağırlıklar (bias değil)
            for (var i = 0; i < w.Length; i++)
            {
                var grad = g[i] / batchSize + decay * w[i];
                m[i] = beta1 * m[i] + (1 - beta1) * grad;
                v[i] = beta2 * v[i] + (1 - beta2) * grad * grad;
                w[i] -= lr * (m[i] / c1) / (MathF.Sqrt(v[i] / c2) + eps);
            }
        }
    }

    private void ForwardBackward(float[] input, int label, Workspace ws)
    {
        var w = Weights;

        // ── conv1 + ReLU + pool ──
        for (var oc = 0; oc < C1; oc++)
        {
            var cBase = oc * O1 * O1;
            for (var y = 0; y < O1; y++)
            {
                for (var x = 0; x < O1; x++)
                {
                    var sum = w.B1[oc];
                    for (var ky = 0; ky < K; ky++)
                    {
                        var rowIn = (y + ky) * N0 + x;
                        var rowW = (oc * K + ky) * K;
                        for (var kx = 0; kx < K; kx++)
                            sum += w.W1[rowW + kx] * input[rowIn + kx];
                    }
                    ws.Conv1[cBase + y * O1 + x] = sum > 0 ? sum : 0;
                }
            }
            Pool(ws.Conv1, cBase, O1, ws.Pool1, ws.Pool1Arg, oc * P1 * P1);
        }

        // ── conv2 + ReLU + pool ──
        for (var oc = 0; oc < C2; oc++)
        {
            var cBase = oc * O2 * O2;
            for (var y = 0; y < O2; y++)
            {
                for (var x = 0; x < O2; x++)
                {
                    var sum = w.B2[oc];
                    for (var ic = 0; ic < C1; ic++)
                    {
                        var inBase = ic * P1 * P1;
                        var wBase = (oc * C1 + ic) * K * K;
                        for (var ky = 0; ky < K; ky++)
                        {
                            var rowIn = inBase + (y + ky) * P1 + x;
                            var rowW = wBase + ky * K;
                            for (var kx = 0; kx < K; kx++)
                                sum += w.W2[rowW + kx] * ws.Pool1[rowIn + kx];
                        }
                    }
                    ws.Conv2[cBase + y * O2 + x] = sum > 0 ? sum : 0;
                }
            }
            Pool(ws.Conv2, cBase, O2, ws.Pool2, ws.Pool2Arg, oc * P2 * P2);
        }

        // ── fc1 + ReLU + dropout ──
        var keep = 1 - DropoutRate;
        for (var h = 0; h < Hidden; h++)
        {
            var sum = w.B3[h];
            var row = h * Flat;
            for (var i = 0; i < Flat; i++)
                sum += w.W3[row + i] * ws.Pool2[i];
            ws.HiddenPre[h] = sum;
            var mask = ws.Rng.NextDouble() < keep ? 1f / keep : 0f;
            ws.Drop[h] = mask;
            ws.Hidden[h] = (sum > 0 ? sum : 0) * mask;
        }

        // ── fc2 + softmax ──
        var logits = new float[Classes];
        for (var c = 0; c < Classes; c++)
        {
            var sum = w.B4[c];
            var row = c * Hidden;
            for (var h = 0; h < Hidden; h++)
                sum += w.W4[row + h] * ws.Hidden[h];
            logits[c] = sum;
        }
        var probs = DigitClassifier.Softmax(logits);
        var predicted = Array.IndexOf(probs, probs.Max());
        if (predicted == label)
            ws.Correct++;

        // ── kayıp ve çıktı gradyanı (etiket yumuşatma ile) ──
        var off = LabelSmoothing / Classes;
        var on = 1 - LabelSmoothing + off;
        double loss = 0;
        var dLogits = new float[Classes];
        for (var c = 0; c < Classes; c++)
        {
            var target = c == label ? on : off;
            loss -= target * Math.Log(Math.Max(probs[c], 1e-7));
            dLogits[c] = probs[c] - target;
        }
        ws.Loss += loss;

        var g = ws.Grads; // W1,B1,W2,B2,W3,B3,W4,B4

        // ── fc2 geri ──
        Array.Clear(ws.DHidden);
        for (var c = 0; c < Classes; c++)
        {
            var d = dLogits[c];
            g[7][c] += d;
            var row = c * Hidden;
            for (var h = 0; h < Hidden; h++)
            {
                g[6][row + h] += d * ws.Hidden[h];
                ws.DHidden[h] += w.W4[row + h] * d;
            }
        }

        // ── fc1 geri ──
        Array.Clear(ws.DFlat);
        for (var h = 0; h < Hidden; h++)
        {
            var d = ws.HiddenPre[h] > 0 ? ws.DHidden[h] * ws.Drop[h] : 0;
            if (d == 0)
                continue;
            g[5][h] += d;
            var row = h * Flat;
            for (var i = 0; i < Flat; i++)
            {
                g[4][row + i] += d * ws.Pool2[i];
                ws.DFlat[i] += w.W3[row + i] * d;
            }
        }

        // ── pool2 → conv2 geri ──
        Array.Clear(ws.DConv2);
        for (var i = 0; i < Flat; i++)
        {
            var idx = ws.Pool2Arg[i];
            if (ws.Conv2[idx] > 0)
                ws.DConv2[idx] = ws.DFlat[i];
        }

        Array.Clear(ws.DPool1);
        for (var oc = 0; oc < C2; oc++)
        {
            var cBase = oc * O2 * O2;
            for (var y = 0; y < O2; y++)
            {
                for (var x = 0; x < O2; x++)
                {
                    var d = ws.DConv2[cBase + y * O2 + x];
                    if (d == 0)
                        continue;
                    g[3][oc] += d;
                    for (var ic = 0; ic < C1; ic++)
                    {
                        var inBase = ic * P1 * P1;
                        var wBase = (oc * C1 + ic) * K * K;
                        for (var ky = 0; ky < K; ky++)
                        {
                            var rowIn = inBase + (y + ky) * P1 + x;
                            var rowW = wBase + ky * K;
                            for (var kx = 0; kx < K; kx++)
                            {
                                g[2][rowW + kx] += d * ws.Pool1[rowIn + kx];
                                ws.DPool1[rowIn + kx] += w.W2[rowW + kx] * d;
                            }
                        }
                    }
                }
            }
        }

        // ── pool1 → conv1 geri ──
        Array.Clear(ws.DConv1);
        for (var i = 0; i < ws.DPool1.Length; i++)
        {
            var idx = ws.Pool1Arg[i];
            if (ws.Conv1[idx] > 0)
                ws.DConv1[idx] = ws.DPool1[i];
        }

        for (var oc = 0; oc < C1; oc++)
        {
            var cBase = oc * O1 * O1;
            for (var y = 0; y < O1; y++)
            {
                for (var x = 0; x < O1; x++)
                {
                    var d = ws.DConv1[cBase + y * O1 + x];
                    if (d == 0)
                        continue;
                    g[1][oc] += d;
                    for (var ky = 0; ky < K; ky++)
                    {
                        var rowIn = (y + ky) * N0 + x;
                        var rowW = (oc * K + ky) * K;
                        for (var kx = 0; kx < K; kx++)
                            g[0][rowW + kx] += d * input[rowIn + kx];
                    }
                }
            }
        }
    }

    /// <summary>2×2 maxpool; seçilen öğenin kaynak index'i geri yayılım için saklanır.</summary>
    private static void Pool(float[] src, int srcBase, int size, float[] dst, int[] arg, int dstBase)
    {
        var half = size / 2;
        for (var y = 0; y < half; y++)
        {
            for (var x = 0; x < half; x++)
            {
                var i = srcBase + 2 * y * size + 2 * x;
                var best = i;
                if (src[i + 1] > src[best]) best = i + 1;
                if (src[i + size] > src[best]) best = i + size;
                if (src[i + size + 1] > src[best]) best = i + size + 1;
                dst[dstBase + y * half + x] = src[best];
                arg[dstBase + y * half + x] = best;
            }
        }
    }
}
