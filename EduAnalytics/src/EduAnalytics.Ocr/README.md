# EduAnalytics.Ocr — Sınav Kâğıdı ve Optik Okuma Modülü

Resmi üniversite sınav kâğıdını üretir ve taranan kâğıtları okur. Kâğıt düzeni Doğuş Üniversitesi'nin sınav
kâğıdı şablonuyla birebir aynıdır:

- kurum logosu ve adı;
- ders kodu, adı ve sınav türüyle başlık, eğitim-öğretim yılı ve dönem;
- öğrenci bilgileri ile **Toplam Not – Paraf** kutusu;
- ders tablosu ve kurallar;
- **Soru Numarası / Alınan Not** tablosu;
- **SORULAR**.

Okuma üç teknolojiyle yapılır:

- **OMR** (OpenCvSharp ile optik işaret tanıma): test sorularının cevap baloncukları, kitapçık baloncuğu ve kâğıdın
  yönü/sayfa türü. Piksel doluluk oranıyla çalışır.
- **El yazısı rakam tanıma** (gömülü küçük bir CNN, `Recognition/digits-cnn.bin`):
  - öğrenci numarası kutuları;
  - "Alınan Not" satırındaki puanlar (ör. `25`, `7,5`);
  - Toplam Not kutusu.
- **PaddleOCR** (Sdcb.PaddleOCR, in-process): yalnızca ad-soyad alanı. Okunan ad bilgi amaçlıdır; eşleştirmede yalnızca
  numara işe yaramazsa kullanılır. PaddleOCR yüklenemezse okuma onsuz sürer.

Çalışma anında internet ya da Python gerekmez.

## Akış

```
Optik Okuma ekranı (UI)
  1. Sınav seç ──► "Sınav Kâğıdı Üret" penceresi ──► ExamPaperPdfGenerator ──► PDF (kitapçık başına bir dosya)
                   (antet bilgileri düzenlenebilir; kurum, logo, öğretim elemanı, kurallar hatırlanır;
                    önizleme PDF ile aynı çizimden)
  2. Tarama yükle (PDF/JPG/PNG/TIFF)
       └─► OpticalFormReader
             ├─ PdfPageRasterizer : PDF sayfası → görüntü (300 DPI)
             ├─ PageNormalizer    : 4 köşe işareti → homografi → kanonik sayfa (8 px/mm)
             ├─ BubbleReader      : sayfa kodu (yön + 1. sayfa / devam sayfası), kitapçık, test cevapları
             ├─ GridDetector      : numara kutularının ve "Alınan Not" satırının çizgilerini görüntüde bulur
             ├─ HandwrittenNumberReader : kutu kırpma, rakamlara ayırma, CNN ile tanıma, puan sınırı denetimi
             └─ PaddleTextReader  : ad-soyad (isteğe bağlı)
     StudentMatcher.MatchHandwritten : numaranın tamamının olasılığı → derse kayıtlı öğrenci
     ScoreSheetEvaluator             : test → optik puan, klasik → tablo; tablo-optik ve Toplam Not denetimi
  3. İnceleme tablosu: durum, eşleşen öğrenci (+ numara kutularının görüntüsü), test cevapları, sonuç, güven;
     satırın altında kırpılmış "Soru Numarası / Alınan Not" tablosu ve her soru için düzenlenebilir puan kutusu,
     Toplam Not kutusunun görüntüsü ve hesaplanan toplamla karşılaştırması, uyarılar.
  4. Onay penceresi → IAnswerEntryService.SaveAsync
     (test: kitapçık şık çözme + doğru/yanlış; klasik: StudentAnswer.Score)

  "Örnek Kâğıt Üret ve Oku": seçili sınavın kayıtlı öğrencileri için ExamPaperFill + ExamPaperSampleRenderer ile
  el yazısıyla doldurulmuş, taramaya benzetilmiş kâğıtlar üretir ve 2–4. adımları aynen çalıştırır.
```

## Kritik tasarım kuralı

`OpticalFormTemplate` (sayfa sabitleri) ve `ExamPaperLayout` (1. sayfanın geometrisi) **tek doğruluk kaynağıdır**.
Kâğıdı basan kod ile okuyan kod aynı mm koordinatlarını kullanır. Düzen değişecekse yalnızca bu iki dosyayı değiştirin
ve `OpticalFormTemplate.Version` değerini artırın.

1. sayfanın geometrisi yalnızca soru sayısı, test sorusu sayısı ve kitapçık sayısıyla belirlenir. Antet metinleri
(kurum, kurallar, öğretim elemanı) hiçbir okunan alanı kaydırmaz: metinler sabit kutulara gerekirse küçültülerek
yazılır. Bu yüzden okuyucu, kâğıt basılırken girilen bilgilere ihtiyaç duymaz; düzeni sınavın veritabanındaki
sorularından yeniden kurar.

Taranan kâğıt mutlaka bu uygulamanın ürettiği kâğıt olmalıdır. Fotokopiyle çoğaltmak sorun değildir; köşe
işaretleri ve sol kenardaki sayfa kodu korunduğu sürece okunur. Kâğıt ölçeklendirmeden, gerçek boyutta yazdırılmalıdır.

## Kâğıt şablonu (sürüm 3)

- **Köşe işaretleri:** her sayfanın dört köşesinde 6 mm'lik dolu kare var.
- **Sayfa kodu:** sol kenarda iki kare var.
  - Yön karesi her sayfada doludur. Ters taranan sayfa bu sayede tanınır ve otomatik çevrilir.
  - "İlk sayfa" karesi yalnızca 1. sayfada doludur. Soru metinlerinin devam sayfaları tanınır ve okunmadan atlanır.
- **Öğrenci numarası:** 12 kutuya, her kutuya bir rakam yazılır.
- **Toplam Not – Paraf:** kutu "Not" ve "Paraf" olarak ince bir çizgiyle bölünmüştür. Not kısmı okunur, paraf okunmaz.
- **Soru Numarası / Alınan Not tablosu:**
  - Tek satır çiftinde soru sayısı kadar kutu bulunur; en az 10, en çok 20.
  - 20'den fazla soruda 20'şerlik ek satır çiftleri basılır; en çok 60 soru.
  - Sınavda olmayan kutular gri basılır.
  - Altında "*Her sorunun puan değeri…" notu yer alır.
- **SORULAR:**
  - Test soruları varsa optik cevap alanı basılır. Sütun öncelikli, en çok 5 sütun; satırlar kâğıttaki soru
    numaralarıyla etiketlenir.
  - Ardından soru metinleri gelir. Her sorunun yanında puanı yazılır; klasik sorularda puanla orantılı bir cevap alanı
    bulunur.
  - Soru metinleri basılmazsa ("Soru metinlerini kâğıda yazdır" kapalı) yalnızca klasik sorular için cevap alanları
    basılır.
- **Kitapçık:** çok kitapçıklı sınavda her kitapçık kendi soru sırası ve karışık şıklarıyla ayrı basılır. Kitapçığın
  baloncuğu kâğıda dolu basılır; öğrenci işaretlemez.

> **Önceki sürümlerle uyumsuzluk:** Sürüm 1–2 optik formları (yalnızca cevap formu, kodlamalı numara) bu okuyucuyla
> okunamaz. Okuyucu bunları "Sayfa kodu bulunamadı" uyarısıyla bildirir. Kâğıdı **Sınav Kâğıdı Üret** ile yeniden alın.

## El yazısı okuma

### Kutu kırpma (hücre bölütleme)

- `GridDetector`, numara kutularının ve puan tablosunun dikey/yatay çizgilerini şablon konumlarının çevresinde arar.
  Bulunan çizgiler şablon koordinatlarının yerini alır. Homografiden sonra kalan küçük kaymalar ve kâğıdın bükülmesi
  böylece kırpmayı bozmaz.
- Her kutu çizgi merkezleri arasındaki dikdörtgendir. `HandwrittenNumberReader` kutunun 1,2 mm dışını da inceler.
  Izgara çizgilerini siler: kutu kenarında duran ya da kutuyu boydan boya kesen düz parçalar silinir. Ardından ağırlık
  merkezi kutunun içinde kalan mürekkep parçalarını alır.
- Böylece çizgiye değen ya da kutudan taşan rakamlar kesilmez, komşu kutunun yazısı karışmaz.
- Eşik, basılı çizgiye değil kutunun içindeki yazıya göre ayarlanır. Kurşun kalem, mavi ve kırmızı tükenmez okunur.

### Puanın okunması

1. Mürekkep parçaları glif hâline getirilir. Yatayda örtüşen parçalar (5'in şapkası, 4'ün iki çizgisi, 7'nin orta
   çizgisi) birleştirilir.
2. Rakamların arasındaki ve satır tabanındaki küçük işaret virgül sayılır (`7,5`). Diğer küçük lekeler yok sayılır.
   Tek başına yazılmış yatay çizgi (`-`) 0 puan sayılır.
3. Yüksekliğinden belirgin biçimde geniş glif bitişik yazılmış birden çok rakam olabilir (ör. `10`). Tek ve bölünmüş
   yorumların ön olasılığı en-boy oranından gelir.
4. Her rakam CNN ile tanınır; olası okumaların bileşimi çıkarılır. **Sorunun puanını aşmayan** en olası okuma seçilir.
   Örneğin 10 puanlık soruda "70" okunamaz; Toplam Not için sınır sınavın toplam puanıdır.
5. Güven, seçilen okumanın olasılığıdır. 0,6'nın altındaki okumalar ve geçerli bir puana çevrilemeyen yazılar
   ekranda sarı işaretlenir ve uyarı olarak gösterilir. Uyarıda olası diğer okumalar da yer alır.

### Puan değerlendirmesi (`ScoreSheetEvaluator`)

- **Test sorusu:** kaydedilen puan optik işaretten hesaplanır: doğruysa sorunun puanı, değilse 0. Tabloda başka bir
  değer yazılıysa "tabloda 0 yazıyor, optik okumaya göre 10 puan" uyarısı verilir.
- **Klasik soru:** puan doğrudan "Alınan Not" kutusundan alınır. Kutu boşsa uyarı verilir ve o soruya puan yazılmaz;
  varsa mevcut puan korunur.
- **Toplam Not:** soru puanlarının toplamıyla karşılaştırılır. Uyuşmazlık uyarı olur; kaydedilen değerler her zaman
  soru kutularından gelir.

Öğretmen inceleme ekranında bir puanı düzelttiğinde değerlendirme hemen yeniden yapılır. Değeri değiştirilen
kutunun okuma uyarısı da kalkar.

### Öğrenci eşleştirme

`StudentMatcher.MatchHandwritten`, her kutunun 0–9 olasılıklarıyla derse kayıtlı her numaranın tamamının olasılığını
hesaplar (Σ log p). Belirsiz bir hane (ör. 1/7) diğer hanelerin desteğiyle doğru öğrenciye çözülür.

- **Birebir:** okunan bütün haneler kayıtlı bir numarayla aynıdır.
- **Yaklaşık:** en olası öğrenci, okunan dizi ile en fazla 2 hanede ayrışır ve bir sonrakinden en az 40 kat daha
  olasıdır. Bu durumda uyarı verilir.
- **Belirsiz:** eşleştirme yapılmaz; okunan dizi ve ad ile klasik eşleştirmeye (`Match`) düşülür.

Yanlış öğrenciye not yazmaktansa öğretmenin seçmesi tercih edilir. Numara kutularının görüntüsü satırda gösterilir.

### Rakam modeli

- Mimari: 28×28 → conv 5×5 (12) → ReLU → maxpool → conv 5×5 (24) → ReLU → maxpool → 96 → 10. Yaklaşık 45 bin
  parametre, 180 KB; derlemeye gömülüdür.
- Eğitim verisi: MNIST el yazısı rakamları (60 bin) ve Windows yazı tiplerinden üretilmiş rakamlar (14 bin).
- Tarama/el yazısı çeşitliliği için bozulmalar:
  - eğim, yatık yazı, en-boy, kalem kalınlığı;
  - elastik bozulma, kopuk çizgi;
  - Türkiye'de yaygın yazımlar: ortası çizgili 7, bayraklı ya da tabanlı 1.
- Ölçüm sonuçları:
  - MNIST sınama kümesinde %99,3.
  - Modelin eğitimde hiç görmediği sentetik el yazısı üreticisinde (`SyntheticHandwriting`) %100. 80 doldurulmuş
    kâğıtta 960 numara hanesi, 480 puan kutusu ve 80 Toplam Not'un hepsi doğru okundu.
- Yeniden eğitmek için: `tools/EduAnalytics.DigitTrainer`. MNIST dosyaları ayrıca indirilir.

  ```
  dotnet run -c Release --project tools/EduAnalytics.DigitTrainer -- --mnist <klasör> --epochs 12
  ```

  Çıktı doğrudan `Recognition/digits-cnn.bin` dosyasına yazılır. `DigitNormalizer` eğitim ve okuma için ortaktır;
  değişirse model yeniden eğitilmelidir.

> **Gerçek el yazısı hakkında:** Sentetik kâğıtlardaki sonuçlar kodun doğru çalıştığını gösterir. Gerçek el yazısında
> başarı yazana ve tarama kalitesine bağlıdır. Bir deneme yapıldı: kutusuz, satıra küçük ve bitişik yazılmış gerçek bir
> numara fotoğrafında 12 hanenin 8–9'u doğru okundu. Kutulu düzen tam bu yüzden seçildi: her kutuya bir rakam ve daha
> büyük yazı. Belirsiz okumalar zaten işaretlenir. Pilot için aşağıdaki adımları izleyin.

## NuGet paketleri

| Paket | Amaç |
|---|---|
| `OpenCvSharp4` + `OpenCvSharp4.runtime.win` | Hizalama, OMR, kutu bölütleme, örnek taramaların üretimi |
| `Sdcb.PaddleOCR` + `Sdcb.PaddleOCR.Models.Local` + `Sdcb.PaddleInference.runtime.win64.mkl` | Ad-soyad OCR'ı (çevrimdışı, Windows x64) |
| `PDFtoImage` | Taranmış PDF → görüntü |
| `QuestPDF` | Sınav kâğıdı PDF'i ve PNG önizlemesi |

Rakam modeli için ek paket yoktur; çıkarım `DigitClassifier` içinde yazılıdır. PaddleOCR modelleri ilk okumada
yüklenir. `OpticalFormReader` bu yüzden DI'da **singleton** kayıtlıdır. Testlerde `new OpticalFormReader(enableTextOcr: false)`
ile ad OCR'ı kapatılabilir.

## Testler ve örnek kâğıtlar

- **Birim testleri** (`tests/EduAnalytics.Ocr.Tests`): kâğıt üretilir, el yazısıyla doldurulur, taramaya benzetilir
  ve okunur. Benzetim eğiklik, kayma, gürültü, bulanıklık ve ters beslemeyi içerir. Testler şunları kapsar:
  - numara, test cevapları, ondalıklı puanlar ve toplam;
  - ters tarama ve devam sayfası;
  - B kitapçığı (karışık sıra);
  - 26 soruluk iki satırlı tablo;
  - boş kutular, çift ve silik işaret;
  - 8 farklı yazarla istatistiksel okuma;
  - köşe işareti ya da sayfa kodu olmayan sayfa;
  - puan değerlendirmesi, puan metni ayrıştırma, rakam modeli ve olasılıkla öğrenci eşleştirme.

  Çalıştırmak için: `dotnet test EduAnalytics/EduAnalytics.sln`.
- **`ExamPaperFill` + `ExamPaperSampleRenderer` + `SyntheticHandwriting`:** doldurulmuş kâğıt çizer ve taramaya
  benzetir. Öğrenci mavi, öğretmen kırmızı kalemle yazar. Hem testler hem ekrandaki **Örnek Kâğıt Üret ve Oku**
  düğmesi bunları kullanır.
- **`SampleOptics/`** (depo kökünde): mevcut veritabanındaki "Vize" sınavı için hazır doldurulmuş kâğıtlar ve beklenen
  okuma sonuçları.

Gerçek kâğıtla pilot için:

1. Bir sınavın kâğıdını üretin ve **gerçek boyutta** yazdırın.
2. 5–10 kâğıdı farklı yazılarla doldurun:
   - numarayı kutulara yazın, test şıklarını işaretleyin;
   - puanları "Alınan Not" satırına, toplamı Toplam Not kutusuna yazın;
   - birkaçında ondalıklı puan, silik işaret ve yanlış toplanmış toplam olsun.
3. 200–300 DPI gri tonlamalı tarayın ve yükleyin.
4. Okunan puanları kırpılmış tablo görüntüsüyle karşılaştırın. Belirsiz okunan kutuların gerçekten okunması zor kutular
   olduğunu doğrulayın.
5. Kaydettikten sonra Sınav Analizi ve Cevap Girişi ekranlarındaki puanları kontrol edin.
