# EduAnalytics — Değişiklik Özeti

**Tarih:** 10 Ekim 2026 (son güncelleme: 9 Kasım 2026)
**Durum:** Proje derleniyor ve uygulama açılıyor. Sınav oluşturmayı engelleyen veritabanı şeması sorunu 15 Ekim'de çözüldü. Optik Okuma, resmi sınav kâğıdı ve el yazısı puan okumayla yenilendi (16 Ekim). 9 Kasım'da analiz hesaplarındaki hatalar düzeltildi, optik okumaya öğretmen onayı eklendi ve şema migration'lara geçti (bkz. ilk bölüm). "Açık konular" bölümünde kararlar ve gerçek kâğıtla pilot bekliyor.

---

## 9 Kasım 2026 güncellemesi

4 Kasım'da tespit edilen eksikler düzeltildi. Güvenlik konuları bilerek bu güncellemeye alınmadı (aşağıda). Ayrıntılar
commit mesajlarında.

### Hesaplama düzeltmeleri
- **Boş bırakılan cevaplar artık kaybolmuyor.**
  - Sınava giren öğrencinin boş bıraktığı test sorusu "boş" ve 0 puan olarak kaydedilir.
  - Satırı tamamen boş öğrenci sınava girmemiş sayılır. Hiçbir soruyu cevaplamadan kâğıt veren öğrenci için Cevap
    Girişi'ndeki **Boş kâğıt** kutusu işaretlenir. Optik okumada kâğıdı okunan her öğrenci sınava girmiş sayılır.
  - Satırı boşaltılan öğrencinin eski cevapları silinir; kayıttan önce sorulur.
  - Klasik soruda puan girilmemişse o soru öğrenci için "değerlendirilmemiş" sayılır.
- **ÖÇ başarısı tek formülle hesaplanıyor.** Sınıf ve öğrenci raporu test ve klasik soruları birlikte, kazanılan puan /
  alınabilecek puan olarak hesaplar. Zayıflık eşiği her yerde %60'tır ve tek yerden değiştirilir
  (`ScoringRules.WeakSuccessThreshold`). Önceden sınıf raporu yalnız test sorularını ve %60'ı, öğrenci raporu bütün
  soruları ve %50'yi kullanıyordu.
- **PÇ raporu.** PÇ başarısı, bağlı ÖÇ başarılarının katkı düzeyiyle (1–5) ağırlıklı ortalamasıdır. İptal edilen sorular
  düşülür, pasif hâle getirilmiş soruların geçmiş sınav verisi sayılır, verisi olmayan ÖÇ ve PÇ "veri yok" gösterilir.
- **Öğrenci profili** sınava özgü soru puanını kullanır, iptal edilen soruları saymaz ve puanı "x / tam puan" gösterir.
- **Rubrik.**
  - Puan elle ya da optik okumayla değişirse eski kriter dökümü silinir; elle girişte kayıttan önce sorulur.
  - Rubrikle puanlamak için ölçüt toplamı sorunun o sınavdaki puanına eşit olmalıdır.
- **Sınav analizi.**
  - "Madde Riski" kartı doğru renkleniyor, aynı madde iki kez sayılmıyor ve cevapsız sınavda risk gösterilmiyor.
  - Cronbach α bütün maddeleri aynı öğrenci kümesiyle hesaplıyor.
  - Oran çubukları boş görünüyordu (şablonda `PART_Track` eksikti); p ve D çubukları ise hep kırmızıydı.

### Yeni özellikler
- **Optik okumada kutu kutu öğretmen onayı.**
  - Belirsiz ya da okunamayan, boş klasik ve optikle uyuşmayan puan kutusu sarı işaretlenir. Öğretmen değeri düzeltir
    ya da "Onayla" ile onaylar.
  - Numarası kesin eşleşmeyen kâğıtta "Eşleşmeyi onayla" gerekir.
  - Onay bekleyen kâğıt kaydedilmez. Kaç kutunun onaylanıp kaçının düzeltildiği günlüğe yazılır.
- **Klasik sorularda madde analizi.** p = ortalama puan / tam puan; D = üst ve alt %27 grubun puan farkı / tam puan.
  Gruplar artık sınavın toplam puanına (test + klasik) göre ayrılır.
- **Rubrik ölçütü tanımlama:** Soru Bankası → soruyu düzenle → "Rubrik Ölçütleri". Puanlanmış bir ölçüt silinemez ama
  yeniden adlandırılabilir.
- **Konu başarısı:** Sınav Analizi → Öğrenim Çıktıları sekmesinin altında.
- **.edudata 2.0.**
  - Paket programları, ders kayıtlarını, ÖÇ/PÇ ve eşleşmelerini, sınavları, kitapçıkları, cevapları ve rubrik
    puanlarını taşır. Sınav verisi isteğe bağlıdır.
  - İçe aktarma artık her öğrenciyi her derse kaydetmiyor; yalnızca paketteki kayıtlar eklenir.
  - Eski (1.0) paketler okunur. Birden çok ders içeriyorlarsa öğrenciler derslere kaydedilmez ve uyarı verilir.
  - Paket şifreli değildir; cevap içeren paketi yalnızca yetkili kişilerle paylaşın.

### Veritabanı
- Şema artık EF Core migration'larıyla yönetiliyor (`EduAnalytics.DataAccess/Migrations`); açılışta `Migrate()` çalışır.
- EnsureCreated ile kurulmuş eski veritabanlarında migration geçmişi oluşturulur ve ilk migration uygulanmış sayılır.
  Şema ve veri değişmez.
- Yeni bir şema değişikliği için:

  ```
  dotnet ef migrations add <Ad> --project src/EduAnalytics.DataAccess --startup-project src/EduAnalytics.DataAccess
  ```

### Nasıl doğrulandı
- 27 Business testi (18'i yeni) ve 30 Ocr testi geçiyor.
- Gerçek veritabanının anonimleştirilmiş kopyasında:
  - Boş cevap senaryosunda N=3, p=0,333, ortalama %58,3 çıktı. Önceden N=1, p=1 ve %87,5 çıkıyordu.
  - Profil ve sıralama puanı aynı.
  - İptal edilen soru artık PÇ sonucunu da değiştiriyor.
  - PÇ oranları katkı düzeyine göre ayrışıyor. Önceden 15 PÇ'nin hepsi %62,5 görünüyordu.
  - Onay bekleyen optik kâğıt kaydedilmedi; onaylandıktan sonra kaydedildi.
  - Kopya .edudata paketine alınıp boş bir veritabanına aktarıldı. Cevap, kriter puanı, ders kaydı ve eşleşme sayıları ile
    sınav ortalaması aynı çıktı; ikinci yükleme kopya oluşturmadı.
- Migration ile sıfırdan kurulan şema, EnsureCreated şemasıyla birebir aynı. Gerçek veritabanının kopyasında şema ve
  satır sayıları değişmedi.
- Değişen ekranlar anonim veriyle ekran dışında çizilip kontrol edildi.

### Bilerek dokunulmayanlar
- **Güvenlik:** giriş ekranındaki sabit kodlu yönetici girişi ve yerel parolanın saklanma biçimi. Ayrı bir çalışmada
  ele alınmalı.
- **Gerçek kâğıtla pilot** yapılmadı (bkz. 5. bölüm).
- **`SampleOptics/`** klasörü depoya alınmıyor (`.gitignore`). İçindeki kâğıtlar veritabanındaki öğrencilerin ad ve
  numaralarını taşıyor.

---

## 1. Yeni özellikler

### Resmi sınav kâğıdı ve el yazısı puan okuma (Optik Okuma)
Optik Okuma ekranı artık yalnızca test cevap formu değil, Doğuş Üniversitesi sınav kâğıdının birebir düzeninde tam
bir sınav kâğıdı üretiyor ve okuyor.

**Kâğıt (Sınav Kâğıdı Üret)**
- **Antet:** kurum logosu ve adı; kalın başlıkta ders kodu, ders adı ve sınav (ör. "YM344 YAZILIM KALİTE GÜVENCESİ VE
  TESTİ VİZE SINAVI"); eğitim-öğretim yılı ve dönem.
- **Öğrenci bilgileri:** "Öğrencinin / Student's" altında Adı Soyadı, Numarası, Bölüm - Program ve İmzası alanları.
  Numara OCR için 12 kutuya, her kutuya bir rakam yazılır. Sağda **Toplam Not – Paraf** kutusu var.
- **Ders tablosu:** DERS / Course; Adı, Kodu ve Sorumlusu satırları; sınav türü (Vize / Midterm), Sınav Tarihi ve Süre.
  Altında **KURALLAR / Rules** maddeleri.
- **Puan tablosu:**
  - Soru Numarası / Alınan Not satırları, sınavdaki soru sayısına göre (en az 10, en çok 20 kutu).
  - 20'den fazla soruda ek satır basılır; kullanılmayan kutular gri.
  - Altında "*Her sorunun puan değeri…" notu.
- **SORULAR:** test soruları için optik cevap alanı, ardından soru metinleri. Her sorunun yanında puanı yazar; klasik
  sorulara cevap alanı ayrılır. İstenirse soru metinleri basılmaz, yalnızca cevap alanları kalır.
- **Pencerede düzenlenebilenler:** kurum adı, logo, başlık, yıl, dönem, öğretim elemanı, sınav türü, tarih, süre ve
  kurallar. Canlı önizleme vardır. Kurum, logo, öğretim elemanı ve kurallar sonraki sınav için hatırlanır.
- **Çok kitapçıklı sınav:** her kitapçık kendi soru sırası ve şıklarıyla ayrı PDF olarak kaydedilir. Kitapçık işareti
  kâğıda önceden basılır.
- **Logo:** Kurumun resmi logosu uygulamayla gelmez. "Logo Seç…" ile kendi dosyanızı seçin.

**Okuma (Tarama Yükle)**
1. **Öğrenci numarası:** kutulardaki el yazısı rakamlar okunur. Öğrenci, numaranın tamamının olasılığıyla derse kayıtlı
   öğrenciler arasından seçilir; bir iki belirsiz hane çoğunlukla doğru öğrenciye çözülür.
2. **"Alınan Not" satırı:**
   - Tablo çizgileri görüntüde bulunur ve her soru için kutu sırayla kırpılır.
   - Kutudaki el yazısı puan okunur. Virgüllü puanlar (`17,5`) ve bitişik yazılmış rakamlar da okunur.
   - Sorunun puanını aşan okuma elenir.
3. **Puanların kullanımı:**
   - Klasik soruların puanı bu kutulardan alınır.
   - Test sorularının puanı optik işaretten hesaplanır; tabloda farklı bir değer yazıyorsa uyarı verilir.
4. **Toplam Not kutusu:** okunur ve soru puanlarının toplamıyla karşılaştırılır.
5. **Devam sayfaları:** soru metinlerinin devam sayfaları tanınır ve atlanır. Ters taranan kâğıt otomatik çevrilir.

**İnceleme ekranı**
- **Satırda:** okunan numara, numara kutularının görüntüsü ve sonuç (test / klasik / toplam).
- **Satırın altında:**
  - kırpılmış **Soru Numarası / Alınan Not** tablosu ve hemen altında her soru için düzenlenebilir puan kutusu;
  - Toplam Not kutusunun görüntüsü ve "Hesaplanan 62,5 — kâğıttaki toplamla aynı ✓" ya da uyuşmazlık uyarısı.
- **İşaretleme:** belirsiz okunan, boş klasik ya da optikle uyuşmayan kutular sarı çerçeveyle işaretlenir.
- **Düzeltme:** bir puan düzeltilince değerlendirme ve uyarılar anında yenilenir.
- **Kayıt:** **Cevapları ve Puanları Kaydet** düğmesiyle yapılır.
  - Test cevapları: kitapçık şık çözme ve doğru/yanlış değerlendirmesiyle kaydedilir.
  - Klasik puanlar: `StudentAnswer.Score` alanına yazılır.
  - Boş bırakılan puan kutusu mevcut puanı değiştirmez.

**El yazısı rakam modeli**
- Uygulamaya gömülü küçük bir sinir ağıdır (180 KB). İnternet ya da Python gerekmez.
- MNIST el yazısı rakamları ve yazı tipleriyle eğitildi. Bozulmalar arasında Türkiye'de yaygın yazımlar da var:
  ortası çizgili 7, bayraklı 1.
- `tools/EduAnalytics.DigitTrainer` ile yeniden eğitilebilir.

**Örnek kâğıtlar:** **Örnek Kâğıt Üret ve Oku** düğmesi ve depo kökündeki `SampleOptics/` klasörü el yazısıyla
doldurulmuş, taramaya benzetilmiş kâğıtlar içerir. Bunlarla kâğıt ve tarayıcı olmadan deneme yapılabilir.

> **Uyumsuzluk:** Önceki sürümün optik formları (kodlama baloncuklu numara) okunamaz; "sayfa kodu bulunamadı"
> uyarısıyla bildirilir. Kâğıtları yeniden üretin.

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
- **Sınav kâğıdı ve el yazısı okuma:**
  - **Birim testleri:** 30 test geçti. Kapsam: eğik/gürültülü tarama, ters tarama, devam sayfası, B kitapçığı,
    26 soruluk iki satırlı tablo, boş kutular, çift ve silik işaret, puan değerlendirmesi, olasılıkla öğrenci
    eşleştirme.
  - **İstatistiksel ölçüm:** Modelin eğitimde görmediği sentetik el yazısıyla 80 kâğıt üretildi; bu sabit tohumlu
    sette 960 numara hanesi, 480 puan kutusu ve 80 Toplam Not'un hepsi doğru okundu. 4 Kasım'da farklı tohumlarla
    üretilen 80 yeni kâğıtta ise 960 numara hanesinin 958'i, 80 numaranın 78'i, 480 puan kutusunun ve 80 Toplam
    Not'un hepsi doğru okundu.
  - **Uçtan uca:** Gerçek uygulamanın ekran mantığıyla, veritabanının kopyasında 65 kontrol yapıldı, hepsi geçti.
    - Bilinen değerlerle tek öğrenci; kayıttan sonra test cevapları, doğru/yanlış ve klasik puanlar veritabanında
      doğrulandı.
    - "Örnek Kâğıt Üret ve Oku" ile 6 kâğıt: iki sayfalı PDF, ters tarama, yanlış toplam, çift/silik işaret, boş puan
      ve kayıtsız öğrenci.
    - A/B kitapçıklı, soru sırası ve şıkları karışık sınav.
  - Ekran ve "Sınav Kâğıdı Üret" penceresi açık ve koyu temada çizdirildi; kırpılma yok.

**Doğrulanmayanlar** (elle denenmesi gerekiyor)
- Fareyle yapılan etkileşimler: sohbet penceresini sürükleme, tema anahtarına tıklama ve animasyonu, açılır listeler.
- Koyu temada diyalog başlık çubuğunun rengi.
- **Gerçek el yazısı ve gerçek tarayıcı.** Sentetik kâğıtlarda okuma neredeyse tam isabetli (yeni tohumlarla 960
  haneden 958). Gönderilen fotoğraftaki kutusuz,
  küçük ve bitişik yazılmış numarada ise 12 hanenin 8–9'u doğru okundu. Yazdırılmış kâğıt ve gerçek taramayla pilot
  gerekiyor. Pilot adımları için bkz. `EduAnalytics/src/EduAnalytics.Ocr/README.md`.

---

## 6. Açık konular

1. **Veritabanı şeması kodla uyuşmuyordu — çözüldü (15 Ekim 2026).**
   - **Sorun:** Bu makinedeki veritabanı, kitapçık sayfa düzeni tutan başka bir uygulama sürümüyle oluşturulmuş. `ExamBookletQuestions` tablosunda kodda olmayan `PositionX` ve `PositionY` sütunları zorunlu ve varsayılansızdı. Bu yüzden sınav oluşturma ve sınava soru ekleme "Cannot insert the value NULL into column 'PositionX'" hatasıyla başarısız oluyordu.
   - **Veritabanı:** Bu iki sütuna `0` varsayılanı eklendi. Mevcut kayıtlar ve düzen verisi korundu.
   - **Açılış adımı (`DbInitializer`):** Uygulama açılışta aynı onarımı kendisi yapıyor. Sütunlar varsa ve varsayılanları yoksa ekleniyor, yoksa hiçbir şey yapılmıyor.
   - **DbContext:** Kayıt başarısız olunca izlenen kayıtlar temizleniyor. Önceden aynı ekrandan yapılan ikinci deneme, geri alınmış kayıtlar yüzünden yabancı anahtar hatası veriyordu.
2. **Ana Sayfa'da sabit yazılmış veriler.** "+2 Yeni", "Bekleyen 2 analiz raporu", "Yapay Zeka servisi çevrimiçi" ve "Veritabanı bağlantısı aktif" gerçek veriden gelmiyor. Gerçek veriye bağlanacak mı, kaldırılacak mı?
3. **Ondalık ayırıcı tutarsız.** Aynı ekranda hem "0.383" hem "0,383" görünüyor. Hepsini virgüle çevirmek, puan kutularına "7.5" yazıldığında nasıl okunacağını da değiştirir; bu yüzden dokunulmadı.
4. **Klasik soru filtresi hatası yeniden üretilemedi.** Veritabanındaki klasik sorular servis, ViewModel ve Sınav Oluştur havuzunda Klasik filtresiyle doğru geliyor. Hangi ekranda ve hangi adımlarla olduğu bilinirse devam edilebilir.
5. **Şablon lisansı.** Cruip şablonu GPL-3.0 lisanslı. Koddan değil yalnızca görsel değerlerden yararlanıldı; proje dağıtılacaksa yine de gözden geçirilmeli.
6. **Eski "Sınav Oluştur" ekranı.** `ExamCreateView` menüden erişilebilir değil; yalnızca puan dağıtımı güncellendi, diğer düzeltmeler uygulanmadı.
7. **Kurum logosu.** Sınav kâğıdında üniversitenin resmi logosu uygulamayla gelmiyor. "Sınav Kâğıdı Üret" penceresinde
   "Logo Seç…" ile resmi logo dosyası bir kez seçilmeli; seçim hatırlanır.
8. **Optik okumada gerçek kâğıt pilotu.** Pilot yapılmadı; ayrıntı için bkz. 5. bölüm, "Doğrulanmayanlar". Belirsiz
   okunan kutular 9 Kasım'dan beri öğretmen onaylamadan ya da düzeltmeden kaydedilmiyor. Pilotta onay isteyen kutuların
   oranına göre güven eşiği (`ScoreReading.LowConfidence = 0,6`) ayarlanabilir. Yazıya özgü hatalar görülürse gerçek
   örneklerle model yeniden eğitilebilir.

---

## 7. Dosyalar

**Yeni dosyalar**
- `EduAnalytics.Business/Helpers/PointDistributor.cs` — ağırlığa göre puan dağıtımı
- `EduAnalytics.Business/Helpers/StudentNumberRules.cs` — 12 hane kuralı
- `EduAnalytics.UI/ViewModels/PendingExamQuestion.cs` — sınava eklenecek soru
- `EduAnalytics.UI/Controls/ThemeSwitcher.xaml` ve `.xaml.cs` — tema anahtarı
- `EduAnalytics.UI/Converters/QuestionTypeLabelConverter.cs`, `EnumLabelConverters.cs` — Türkçe etiketler
- `EduAnalytics.UI/Assets/Fonts/` — Inter yazı tipi ve lisansı
- Sınav kâğıdı ve el yazısı okuma:
  - `EduAnalytics.Ocr/ExamPaperSpec.cs`, `ExamPaperLayout.cs`, `ExamPaperPdfGenerator.cs`: kâğıt tanımı, 1. sayfa
    geometrisi ve PDF üretimi.
  - `EduAnalytics.Ocr/ExamPaperFill.cs`, `ExamPaperFillRenderer.cs`, `ExamPaperSampleRenderer.cs`,
    `SyntheticHandwriting.cs`: örnek kâğıtlar.
  - `EduAnalytics.Ocr/Recognition/`: rakam modeli (`DigitClassifier.cs`, `DigitNormalizer.cs`, `digits-cnn.bin`) ve
    `HandwrittenNumberReader.cs`.
  - `EduAnalytics.Ocr/Reading/GridDetector.cs`: tablo ızgarası tespiti.
  - `EduAnalytics.Ocr/ScoreSheetEvaluator.cs`: puan ve toplam denetimi.
  - `EduAnalytics.UI/Views/ExamPaperDialog.xaml` ve `.xaml.cs`: "Sınav Kâğıdı Üret" penceresi;
    `OmrGeneratorDialog`'un yerini aldı.
  - `EduAnalytics.UI/Services/ExamPaperSettingsStore.cs`: hatırlanan antet ayarları.
  - `EduAnalytics/tools/EduAnalytics.DigitTrainer/`: rakam modelinin eğitim aracı.
  - `tests/EduAnalytics.Ocr.Tests/ExamPaperRoundTripTests.cs`, `ScoreSheetTests.cs`.
- Kaldırılan dosyalar (önceki optik form sürümü): `OpticalFormSpec.cs`, `OpticalFormPdfGenerator.cs`,
  `OpticalFormFill.cs`, `OpticalFormSampleRenderer.cs`, `OmrGeneratorDialog.xaml`/`.cs`, `OpticalFormRoundTripTests.cs`.

**Başlıca değişen dosyalar**
- İş mantığı: `ExamCrudService.cs`, `IExamCrudService.cs`, `StudentService.cs`, `DataImportService.cs`, `ExamListItemDto.cs`, `DbInitializer.cs`
- Tema ve stiller: `Themes/Light.xaml`, `Themes/Dark.xaml`, `App.xaml`, `App.xaml.cs`, `Services/ThemeService.cs`
- Ana pencere: `MainWindow.xaml`, `MainWindow.xaml.cs`
- Ekranlar: `ExamManagementView`, `AnswerEntryView`, `ExamAnalysisView`, `ExamFromBankView`, `DashboardView`, `MyCoursesView`, `OpticalReadingView`, `StudentManagementView`, `QuestionBankView`, `ProgramOutcomeMappingView`, `AIAssistantPanel`
- Diyaloglar: `StudentProfileDialog`, `QuestionCancelDialog`, `QuestionEditDialog`, `OmrGeneratorDialog`
- ViewModel'ler: `ExamManagementViewModel`, `ExamFromBankViewModel`, `ExamCreateViewModel`, `StudentManagementViewModel`
- Kodlama onarımı: `OpticalReadingViewModel.cs`, `LoginWindow.xaml.cs`, `UserSettingsDialog.xaml.cs`
- Optik Okuma (sınav kâğıdı):
  - OCR modülü: `OpticalFormTemplate.cs`, `Models/OpticalSheetResult.cs`, `StudentMatcher.cs`, `Reading/BubbleReader.cs`,
    `Reading/OpticalFormReader.cs`, `Reading/IOpticalFormReader.cs`, `Reading/PaddleTextReader.cs`, `README.md`.
  - Arayüz: `OpticalReadingViewModel.cs`, `ScannedSheetRowViewModel.cs`, `OpticalReadingView.xaml`.
  - Örnekler: `SampleOptics/` (README dahil).
