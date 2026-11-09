# EduAnalytics.DigitTrainer — el yazısı rakam modeli eğitimi

Sınav kâğıdındaki el yazısı rakamları (öğrenci numarası kutuları, "Alınan Not" puanları, Toplam Not) okuyan modeli
yeniden eğitir. Uygulamanın çalışması için **gerekmez**. Eğitilmiş model zaten
`src/EduAnalytics.Ocr/Recognition/digits-cnn.bin` olarak derlemeye gömülüdür.

## Ne zaman gerekir?

- `DigitNormalizer` (rakamın 28×28 biçime getirilmesi) değiştiğinde. Eğitim ve okuma aynı kodu kullanır; biri
  değişirse model yeniden eğitilmelidir.
- Gerçek kâğıtlardan toplanan örneklerle modeli iyileştirmek istendiğinde. Bunun için `TrainingData`'ya yeni bir kaynak
  eklenir.

## Çalıştırma

1. MNIST dosyalarını bir klasöre indirin. Gereken dosyalar `train-images-idx3-ubyte.gz`, `train-labels-idx1-ubyte.gz`,
   `t10k-images-idx3-ubyte.gz` ve `t10k-labels-idx1-ubyte.gz`; örnek kaynak
   `https://storage.googleapis.com/cvdf-datasets/mnist/`.
2. Depo kökünden çalıştırın:

   ```
   dotnet run -c Release --project EduAnalytics/tools/EduAnalytics.DigitTrainer -- --mnist <klasör> --epochs 12
   ```

   - `--out <dosya>` verilmezse model doğrudan `src/EduAnalytics.Ocr/Recognition/digits-cnn.bin` üzerine yazılır.
   - `--preview <png>` eğitim girdilerinden örnek bir pafta yazar: artırılmış MNIST, yazı tipleri ve sentetik el yazısı.
3. Uygulamayı ve testleri yeniden derleyin: `dotnet test EduAnalytics/EduAnalytics.sln`.

6 çekirdekli bir dizüstünde eğitim yaklaşık 10–15 dakika sürer. Python ya da ek paket gerekmez; ileri ve geri yayılım
`Trainer.cs` içinde yazılıdır.

## Veri ve bozulmalar

- **MNIST** (60 bin el yazısı rakam) ve **Windows yazı tipleri** (24 yazı tipi × 10 rakam × 60 varyant). Yazı
  tiplerinin yarısı el yazısına benzer (Ink Free, Segoe Print, MV Boli…). Bulunamayan yazı tipi atlanır.
- Her örneğe eğitim sırasında rastgele bozulma uygulanır:
  - döndürme, yatık yazı (shear), en-boy değişimi;
  - elastik bozulma, kalem kalınlığı, kopuk çizgi;
  - Türkiye'de yaygın yazımlar: ortası çizgili 7, bayraklı ya da tabanlı 1.
- **Sınama:** MNIST sınama kümesi (10 bin) ve `SyntheticHandwriting` ile çizilmiş 3000 rakam. Bu üretici örnek
  kâğıtlarda kullanılır ve eğitime **girmez**; bağımsız bir sınamadır.

Son eğitim (12 tur): MNIST sınama %99,29, sentetik el yazısıyla çizilmiş 3000 tek rakamda %100. Kâğıt üzerindeki
uçtan uca okumada (kutu bulma ve normalleştirme dahil) yeni tohumlarla 960 haneden 958'i doğru okundu; bkz.
`src/EduAnalytics.Ocr/README.md`.
