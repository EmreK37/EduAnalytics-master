# EduAnalytics — Değişiklik Özeti

**Tarih:** 10 Ekim 2026
**Durum:** Proje derleniyor ve uygulama açılıyor. Aşağıdaki "Açık konular" bölümündeki üç karar bekliyor; bunlardan biri (veritabanı şeması) sınav oluşturmayı ve sınava soru eklemeyi engelliyor.

---

## 1. Yeni özellikler

### Mevcut sınava soru ekleme
Sınav Yönetimi'ndeki her sınav kartında **Soru Ekle** butonu var.

- Açılan pencerede dersin aktif soru havuzu, Tümü / Test / Klasik filtresi ve metin aramasıyla listelenir. Sınavda zaten olan sorular listede çıkmaz.
- Seçilen sorular sınavın sonuna eklenir; her sorunun o sınavdaki puanı pencerede düzenlenebilir.
- "Mevcut + Eklenecek = Toplam / 100" özeti gösterilir. Toplam 100'ü aşarsa engellenmez, uyarı verilir.
- "Kalan Puanı Ağırlığa Göre Dağıt" butonu, 100'e kalan puanı eklenecek sorulara orantılı paylaştırır.
- Sorular sınavın mevcut kitapçıklarına da sona eklenir (basılmış kitapçıkların sırası bozulmaz).
- Sınava daha önce cevap girilmişse bunu hatırlatan bir not çıkar.

### Sınavdan soru çıkarma
Sınav kartındaki her sorunun yanında **Çıkar** butonu var.

- Soru yalnızca sınavdan çıkar, soru bankasında kalır.
- O soruya girilmiş öğrenci cevapları da silinir; onay penceresi bunu önceden söyler.
- Kalan sorular ve kitapçık sıraları boşluksuz yeniden numaralanır.
- Sınavdaki son soru çıkarılamaz.

### "100 Puana Eşitle" artık ağırlığa göre
Eskiden puanı soru sayısına eşit bölüyordu. Artık her sorunun mevcut puanını ağırlık katsayısı sayar ve toplamı tam 100 olacak şekilde orantılı dağıtır.

| Mevcut puanlar | Sonuç |
|---|---|
| 1 / 1 / 2 | 25 / 25 / 50 |
| 1 / 1 / 1 | 33,34 / 33,33 / 33,33 |
| 10 ×4, 30 ×2 | değişmez (zaten 100) |

Kodda ayrı bir "ağırlık katsayısı" alanı yoktur; sorunun puanı ağırlık olarak kullanıldı.

### Öğrenci numarası: 12 hane zorunlu
Format: giriş yılı (4) + mühendislik kodu `03` + bölüm kodu `011` + giriş sırası (3). Örnek: `202403011069`.

- Yeni öğrenci eklerken ve düzenlerken numara alanı yalnızca rakam kabul eder, en fazla 12 karakter alır; 12 hane değilse kayıt reddedilir.
- Excel'den toplu yüklemede geçersiz numaralı satırlar atlanır ve uyarı olarak listelenir.
- Veri paketi içe aktarmada geçersiz numaralı öğrenciler alınmaz.

### Tema anahtarı
Üst çubuktaki tek tema butonu yerine üç seçenekli (Sistem / Açık / Koyu) hap biçiminde bir anahtar geldi. Aktif seçeneği gösteren halka seçenekler arasında yaylanarak kayar. Kullanıcı menüsündeki "Temayı Değiştir" ile senkron çalışır.

### EduAI sohbeti: ortada açılan pencere
Sohbet artık sağda sabit bir sütun değil; "EduAI'ya sor"a tıklayınca sayfanın ortasında açılır.

- Başlığından sürüklenerek taşınabilir; her açılışta yeniden ortadan başlar.
- Kapat düğmesi ve Ctrl+I eskisi gibi çalışır; ek olarak Esc ve pencerenin dışına tıklamak da kapatır.
- Sohbetin işleyişine (mesaj gönderme, öneriler, ayarlar, temizleme, kopyalama) dokunulmadı. Pencere kapanınca konuşma silinmez.

---

## 2. Tasarım yenilemesi

Tasarım [Cruip Tailwind Dashboard Template (Mosaic)](https://github.com/cruip/tailwind-dashboard-template) şablonuna göre düzenlendi. Şablon React/Tailwind olduğu için kod alınmadı; renk, ölçü ve düzen değerleri WPF/XAML'de yeniden uygulandı.

- **Yazı tipi:** Inter, uygulamaya gömüldü (bilgisayarda yüklü olması gerekmez).
- **Renkler:** Mor vurgu; açık temada gri zemin üstünde beyaz kartlar, koyu temada koyu lacivert-gri zemin üstünde bir ton açık kartlar.
- **Kartlar:** 12 px yuvarlak köşe, hafif gölge.
- **Yan menü:** Sağ köşeleri yuvarlak, kenarlıksız; aktif öğe mor gradyanlı yuvarlak kutu.
- **Butonlar:** İkincil ve kırmızı (Sil, Çıkar, İptal) butonlara ince kenarlık eklendi; beyaz kart üstünde düz yazı gibi görünüyorlardı.
- **Açık temadaki mor:** Şablonun açık mor tonunda beyaz yazı okunaksız kaldığı için bir ton koyusu (`#755FF8`) kullanıldı.

---

## 3. Hata düzeltmeleri

### Cevap Girişi
- Sütun başlıkları sıkışıp kesiliyordu; başlık yüksekliği ve sütun genişlikleri artırıldı.
- Hücreye tıklayınca yazı beyaza dönüp okunmuyordu; seçili hücre artık tema rengini koruyor.
- Koyu temada hücre düzenlerken açılan kutu okunmuyordu; temaya uyduruldu.
- Boş cevap "Empty" yerine "Boş" olarak görünüyor.
- "Not" butonu metnine sığıyor.
- Numara, Öğrenci ve Kitapçık sütunları yatay kaydırmada sabit kalıyor.

### Tema uyumsuzluğu
- Öğrenci Profili açık temada siyah zeminle açılıyordu. Sebep: tema dosyalarında tanımlı olmayan renk adları (`BackgroundBrush`, `SurfaceBrush`, `TextMutedBrush`, `SecondaryBrush`, `DropShadowEffect`). Aynı sorun Derslerim, Admin Dışa Aktar, Optik Form Üretici ve Ana Sayfa'da da vardı; hepsi düzeltildi.
- Ana Sayfa ikon kutuları ve birkaç ikon her temada sabit açık renkteydi; temaya bağlandı.
- Standart diyalogların Windows başlık çubuğu artık uygulama temasına uyacak şekilde ayarlanıyor.

### Kırpılan metinler
- Derslerim'de "İncele" butonu ("İncel" görünüyordu).
- Derslerim ve Öğrenci Sıralaması'nda sınıf adı ve 12 haneli öğrenci numarası sütunları.
- Sınav Analizi tablolarının sütun başlıkları.
- Optik Okuma tablosu ("Cevaplar" sütunu neredeyse hiç görünmüyordu).
- Ana Sayfa'daki ders adı etiketi, Sınav Oluştur'daki ders kutusu, PÇ–ÖÇ Eşleştirme'deki ders sütunu.
- Soruyu İptal Et diyaloğunun alt kısmı ve Öğrenci Sıralaması'ndaki zayıf öğrenim çıktısı etiketleri.

### İngilizce kalan metinler
| Eski | Yeni |
|---|---|
| Midterm / MakeUp | Vize / Bütünleme |
| MultipleChoice / OpenEnded | Test / Klasik |
| Empty | Boş |
| 15 April 2026 | 15 Nisan 2026 |

### Bozuk Türkçe karakterler
Beş kaynak dosyada metinler bozuk kodlamayla kaydedilmişti ("BaÅŸlangÄ±Ã§ HatasÄ±" gibi) ve kullanıcıya da öyle görünüyordu. Giriş ekranı uyarıları, Optik Okuma mesajları, Kullanıcı Ayarları ve başlangıç hata pencereleri düzeltildi.

### Karışık yazı tipi
Yan menü, tablolar ve etiketler eski fontta, başlıklar Inter'de kalıyordu. Yazı tipi artık her pencerenin köküne uygulanıyor; tüm uygulama Inter kullanıyor.

---

## 4. Veritabanında yapılan değişiklik

Mevcut 11 öğrencinin numarası 12 haneli formata çevrildi. Giriş yılı sınıftan türetildi (2026-27 akademik yılına göre):

| Sınıf | Giriş yılı | Örnek |
|---|---|---|
| 2. sınıf | 2025 | 202503011060 |
| 3. sınıf | 2024 | 202403011003 |
| 4. sınıf | 2023 | 202303011014 |

- Öğrenciler `Id` ile bağlı olduğu için cevaplar ve ders kayıtları etkilenmedi.
- Sınıfı yalnızca "ym" olan test kaydına (`dsdasd`) yıl çıkarılamadığı için 2025 verildi.
- Sıfırdan oluşturulan veritabanında demo öğrenciler de aynı formatta üretilecek şekilde seed kodu güncellendi.
- **Yedek:** Değişiklikten önceki yedek geçici bir klasörde duruyor ve silinebilir. Saklamak istiyorsanız kalıcı bir yere taşıyın:
  `C:\Users\Emre\AppData\Local\Temp\claude\c--Users-Emre-Desktop-EduAnalytics-master\fed6138d-b796-4176-b32e-9fd75b69ac4e\scratchpad\EduAnalyticsDb_before_studentno.bak`

---

## 5. Nasıl doğrulandı

**Doğrulananlar**
- Soru ekleme ve çıkarma: veritabanının bir kopyası üzerinde çalıştırıldı. Sıra, puan ve kitapçık kayıtları doğru oluştu; aynı soruyu ikinci kez ekleme reddedildi; çıkarmada cevaplar silindi ve sıra düzeldi.
- Puan dağıtımı ve 12 hane kuralı: örnek değerlerle kontrol edildi.
- Görünüm: 24 ekran ve 8 diyalog, gerçek veriyle, açık ve koyu temada arka planda çizdirilip metin kırpılması açısından otomatik tarandı. Son taramada görünür kırpılma kalmadı.

**Doğrulanmayanlar** (elle denenmesi gerekiyor)
- Fareyle yapılan etkileşimler: sohbet penceresini sürükleme, tema anahtarına tıklama ve animasyonu, açılır listeler.
- Koyu temada diyalog başlık çubuğunun rengi.
- Gerçek veritabanında "Sınava Ekle": aşağıdaki 1. açık konu yüzünden hata verir.

---

## 6. Açık konular

1. **Veritabanı şeması kodla uyuşmuyor (engelleyici).** Bu makinedeki veritabanında `ExamBookletQuestions` tablosunda kodda olmayan dört zorunlu sütun var: `PageNumber`, `PositionX`, `PositionY`, `Width`. Başka bir sürümden kalmış görünüyor. Bu yüzden sınav oluşturma ve sınava soru ekleme "Cannot insert NULL into PositionX" hatasıyla başarısız olur. İki çözüm var: veritabanını silip yeniden oluşturmak ya da şu iki satırı çalıştırmak:
   ```sql
   ALTER TABLE ExamBookletQuestions ADD DEFAULT 0 FOR PositionX;
   ALTER TABLE ExamBookletQuestions ADD DEFAULT 0 FOR PositionY;
   ```
2. **Ana Sayfa'da sabit yazılmış veriler.** "+2 Yeni", "Bekleyen 2 analiz raporu", "Yapay Zeka servisi çevrimiçi" ve "Veritabanı bağlantısı aktif" gerçek veriden gelmiyor. Gerçek veriye bağlanacak mı, kaldırılacak mı?
3. **Ondalık ayırıcı tutarsız.** Aynı ekranda hem "0.383" hem "0,383" görünüyor. Hepsini virgüle çevirmek, puan kutularına "7.5" yazıldığında nasıl okunacağını da değiştirir; bu yüzden dokunulmadı.
4. **Klasik soru filtresi hatası yeniden üretilemedi.** Veritabanındaki klasik sorular servis, ViewModel ve Sınav Oluştur havuzunda Klasik filtresiyle doğru geliyor. Hangi ekranda ve hangi adımlarla olduğu bilinirse devam edilebilir.
5. **Şablon lisansı.** Cruip şablonu GPL-3.0 lisanslı. Koddan değil yalnızca görsel değerlerden yararlanıldı; proje dağıtılacaksa yine de gözden geçirilmeli.
6. **Eski "Sınav Oluştur" ekranı.** `ExamCreateView` menüden erişilebilir değil; yalnızca puan dağıtımı güncellendi, diğer düzeltmeler uygulanmadı.

---

## 7. Dosyalar

**Yeni dosyalar**
- `EduAnalytics.Business/Helpers/PointDistributor.cs` — ağırlığa göre puan dağıtımı
- `EduAnalytics.Business/Helpers/StudentNumberRules.cs` — 12 hane kuralı
- `EduAnalytics.UI/ViewModels/PendingExamQuestion.cs` — sınava eklenecek soru
- `EduAnalytics.UI/Controls/ThemeSwitcher.xaml` ve `.xaml.cs` — tema anahtarı
- `EduAnalytics.UI/Converters/QuestionTypeLabelConverter.cs`, `EnumLabelConverters.cs` — Türkçe etiketler
- `EduAnalytics.UI/Assets/Fonts/` — Inter yazı tipi ve lisansı

**Başlıca değişen dosyalar**
- İş mantığı: `ExamCrudService.cs`, `IExamCrudService.cs`, `StudentService.cs`, `DataImportService.cs`, `ExamListItemDto.cs`, `DbInitializer.cs`
- Tema ve stiller: `Themes/Light.xaml`, `Themes/Dark.xaml`, `App.xaml`, `App.xaml.cs`, `Services/ThemeService.cs`
- Ana pencere: `MainWindow.xaml`, `MainWindow.xaml.cs`
- Ekranlar: `ExamManagementView`, `AnswerEntryView`, `ExamAnalysisView`, `ExamFromBankView`, `DashboardView`, `MyCoursesView`, `OpticalReadingView`, `StudentManagementView`, `QuestionBankView`, `ProgramOutcomeMappingView`, `AIAssistantPanel`
- Diyaloglar: `StudentProfileDialog`, `QuestionCancelDialog`, `QuestionEditDialog`, `OmrGeneratorDialog`
- ViewModel'ler: `ExamManagementViewModel`, `ExamFromBankViewModel`, `ExamCreateViewModel`, `StudentManagementViewModel`
- Kodlama onarımı: `OpticalReadingViewModel.cs`, `LoginWindow.xaml.cs`, `UserSettingsDialog.xaml.cs`
