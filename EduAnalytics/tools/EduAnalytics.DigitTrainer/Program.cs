using System.Diagnostics;
using EduAnalytics.DigitTrainer;
using EduAnalytics.Ocr.Recognition;

// Kullanım:
//   dotnet run -c Release --project tools/EduAnalytics.DigitTrainer -- --mnist <klasör> [--out <dosya>] [--epochs 12] [--seed 7]
// <klasör>: MNIST idx dosyaları (train-images-idx3-ubyte[.gz] vb.). Varsayılan çıktı:
// src/EduAnalytics.Ocr/Recognition/digits-cnn.bin

Console.OutputEncoding = System.Text.Encoding.UTF8;

string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();

var mnistDir = Arg("--mnist") ?? throw new ArgumentException("--mnist <klasör> gerekli");
var output = Arg("--out") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "../../../../../src/EduAnalytics.Ocr/Recognition/digits-cnn.bin"));
var epochs = int.Parse(Arg("--epochs") ?? "12");
var seed = int.Parse(Arg("--seed") ?? "7");

var rng = new Random(seed);
var clock = Stopwatch.StartNew();

Console.WriteLine("MNIST yükleniyor...");
var train = TrainingData.LoadMnist(mnistDir, "train");
var test = TrainingData.LoadMnist(mnistDir, "t10k");
Console.WriteLine($"  eğitim {train.Count}, sınama {test.Count}");

var fontDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
string[] fonts =
{
    // El yazısına benzeyenler
    "Inkfree.ttf", "segoepr.ttf", "segoesc.ttf", "mvboli.ttf", "comic.ttf", "LHANDW.TTF", "BRADHITC.TTF",
    "ITCKRIST.TTF", "TEMPSITC.TTF", "VINERITC.TTF", "PRISTINA.TTF", "JUICE___.TTF",
    // Düz yazı tipleri (temiz yazan öğretmenler / basılı rakam biçimleri)
    "arial.ttf", "calibri.ttf", "candara.ttf", "corbel.ttf", "tahoma.ttf", "verdana.ttf", "trebuc.ttf",
    "segoeui.ttf", "bahnschrift.ttf", "consola.ttf", "times.ttf", "georgia.ttf"
};
Console.WriteLine("Yazı tipi rakamları üretiliyor...");
var fontSamples = TrainingData.RenderFontDigits(fonts.Select(f => Path.Combine(fontDir, f)), variantsPerDigit: 60, rng);
Console.WriteLine($"  {fontSamples.Count} yazı tipi örneği");

var pool = train.Concat(fontSamples).ToList();
var testInputs = test.Select(s => (Input: Augmenter.Plain(s), s.Label)).ToList();
var synthetic = TrainingData.SyntheticHandwritingSet(3000, new Random(seed + 1));

// --preview <png>: eğitim girdilerinden örnek bir pafta (artırılmış MNIST, yazı tipleri, sentetik el yazısı) yazar ve çıkar.
if (Arg("--preview") is { } previewPath)
{
    var rows = new List<float[]>();
    var local = new Random(seed);
    for (var digit = 0; digit <= 9; digit++)
    {
        var mnistOfDigit = train.Where(s => s.Label == digit).Take(400).ToList();
        var fontOfDigit = fontSamples.Where(s => s.Label == digit).ToList();
        var synthOfDigit = synthetic.Where(s => s.Label == digit).Take(6).ToList();
        for (var i = 0; i < 8; i++)
            rows.Add(Augmenter.Augment(mnistOfDigit[local.Next(mnistOfDigit.Count)], local) ?? new float[784]);
        for (var i = 0; i < 6; i++)
            rows.Add(Augmenter.Augment(fontOfDigit[local.Next(fontOfDigit.Count)], local) ?? new float[784]);
        rows.AddRange(synthOfDigit.Select(s => s.Input));
    }
    const int perRow = 20;
    using var sheet = new OpenCvSharp.Mat(10 * 30, perRow * 30, OpenCvSharp.MatType.CV_8UC1, OpenCvSharp.Scalar.Black);
    for (var i = 0; i < rows.Count; i++)
    {
        var (r, c) = (i / perRow, i % perRow);
        for (var p = 0; p < 784; p++)
            sheet.Set(r * 30 + 1 + p / 28, c * 30 + 1 + p % 28, (byte)(rows[i][p] * 255));
    }
    using var big = new OpenCvSharp.Mat();
    OpenCvSharp.Cv2.Resize(sheet, big, new OpenCvSharp.Size(sheet.Width * 2, sheet.Height * 2), 0, 0, OpenCvSharp.InterpolationFlags.Nearest);
    OpenCvSharp.Cv2.ImWrite(previewPath, big);
    Console.WriteLine($"Önizleme yazıldı: {previewPath} (her satır bir rakam: 8 MNIST + 6 yazı tipi + 6 sentetik)");
    return;
}

var trainer = new Trainer(seed);
const int batchSize = 64;
const float baseLr = 1.5e-3f;

for (var epoch = 1; epoch <= epochs; epoch++)
{
    var order = Enumerable.Range(0, pool.Count).OrderBy(_ => rng.Next()).ToArray();
    // Kosinüs öğrenme hızı.
    var lr = (float)(baseLr * 0.5 * (1 + Math.Cos(Math.PI * (epoch - 1) / epochs)));
    double lossSum = 0;
    var correct = 0;
    var seen = 0;

    for (var start = 0; start < order.Length; start += batchSize)
    {
        var count = Math.Min(batchSize, order.Length - start);
        var batch = new (float[] Input, int Label)[count];
        Parallel.For(0, count, i =>
        {
            var sample = pool[order[start + i]];
            var local = new Random(HashCode.Combine(seed, epoch, start + i));
            var input = local.NextDouble() < 0.9 ? Augmenter.Augment(sample, local) : null;
            batch[i] = (input ?? Augmenter.Plain(sample), sample.Label);
        });

        var (loss, ok) = trainer.TrainBatch(batch, lr);
        lossSum += loss * count;
        correct += ok;
        seen += count;
    }

    var classifier = new DigitClassifier(trainer.Weights);
    var mnistAcc = Accuracy(classifier, testInputs);
    var synthAcc = Accuracy(classifier, synthetic);
    Console.WriteLine($"Epoch {epoch,2}/{epochs}  lr={lr:0.00000}  kayıp={lossSum / seen:0.0000}  eğitim=%{100.0 * correct / seen:0.00}  " +
                      $"MNIST-sınama=%{mnistAcc * 100:0.00}  sentetik-el-yazısı=%{synthAcc * 100:0.00}  ({clock.Elapsed:mm\\:ss})");
}

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using (var file = File.Create(output))
    trainer.Weights.Write(file);
Console.WriteLine($"Model yazıldı: {output} ({new FileInfo(output).Length / 1024} KB)");

// Kaydedilen dosya okuyucunun yükleyeceği biçimde geri okunup sınanır.
using (var file = File.OpenRead(output))
{
    var reloaded = new DigitClassifier(DigitModelWeights.Read(file));
    Console.WriteLine($"Yeniden yüklenen model: MNIST-sınama=%{Accuracy(reloaded, testInputs) * 100:0.00}  " +
                      $"sentetik-el-yazısı=%{Accuracy(reloaded, synthetic) * 100:0.00}");
    PrintConfusion(reloaded, synthetic, "Sentetik el yazısı karışıklık matrisi (satır: gerçek, sütun: tahmin)");
}

static double Accuracy(DigitClassifier classifier, IReadOnlyList<(float[] Input, int Label)> set)
{
    var ok = 0;
    Parallel.For(0, set.Count, () => 0, (i, _, local) =>
    {
        var p = classifier.Predict(set[i].Input);
        return Array.IndexOf(p, p.Max()) == set[i].Label ? local + 1 : local;
    }, local => Interlocked.Add(ref ok, local));
    return ok / (double)set.Count;
}

static void PrintConfusion(DigitClassifier classifier, IReadOnlyList<(float[] Input, int Label)> set, string title)
{
    var m = new int[10, 10];
    foreach (var (input, label) in set)
    {
        var p = classifier.Predict(input);
        m[label, Array.IndexOf(p, p.Max())]++;
    }
    Console.WriteLine(title);
    Console.WriteLine("     " + string.Concat(Enumerable.Range(0, 10).Select(c => $"{c,5}")));
    for (var r = 0; r < 10; r++)
        Console.WriteLine($"{r,5}" + string.Concat(Enumerable.Range(0, 10).Select(c => $"{m[r, c],5}")));
}
