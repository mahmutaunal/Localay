# İngilizce–Türkçe oyun çevirisi kabul kümesi

Durum: **Çevrimdışı küme hazır; gerçek tek blok pilotu kabul ölçütlerini geçmedi.**
[Tekli sonuçlar](results/ASSESSMENT-2026-10-22.md) ve
[paketli karşılaştırma](results/BATCH-ASSESSMENT-2026-10-22.md) ayrı kayıtlıdır.
Çevrimdışı kontrol aracı API anahtarı, faturalandırma, ağ bağlantısı, Windows,
GitHub Actions veya ek paket gerektirmez. Açık `--run` ile kullanılan WebCapture
gerçek Google web çağrıları yapar. Uygulama kodu bu değerlendirmede değiştirilmez.

## Küme

`corpus.json`, Localay için özgün yazılmış 60 sentetik oyun metni içerir. Yayımlanmış
ve bağımsız değerlendirilmiş bir benchmark değildir. Kaynak metinler ve Türkçe
kabul açıklamaları aynı asistan tarafından hazırlanmıştır; bağımsız insan incelemesi
yapılmış gibi sunulamaz. Küçük bir pilot kümedeki başarı bütün oyunlara genellenemez.

| Grup | Örnek sayısı | İncelenen noktalar |
| --- | ---: | --- |
| ui | 10 | Menü, kayıt yükleme, onay, ayarlar, uyarılar |
| instruction | 10 | Olumsuzluk, koşul, yalnızca, önce/sonra, görev başarısı |
| dialogue | 10 | Deyim, pişmanlık, geçmişe aykırı koşul, alay, tercih |
| context | 10 | Çok anlamlı sözcükler ve tek bloktan çözülemeyen bağlam |
| item | 10 | Zırh, süreli hasar, bekleme süresi, bağışıklık, can çalma |
| format | 10 | Değişken, tuş etiketi, sayı, yüzde, satır sonu, rol eşleşmesi |

Her örnek `meaning`, `acceptableExamples`, `criticalRisk` ve `reviewContext`
alanlarına sahiptir. `acceptableExamples` motor çıktısı değildir ve birebir metin
eşleşmesi beklenmez. Farklı ve doğru Türkçe karşılıklar kabul edilir. `cooldown`
teriminin iki kullanımında tutarlılık ayrıca incelenir.

`context` grubundaki hedef okumalar ancak dış oyun bağlamıyla kesinleşebilir.
Bu 10 örnek, 50 bağımsız örneğin başarı oranına katılmaz. Olası alternatif anlam,
eklenen bilgi ve eksik bağlam ayrı not edilir; modelin görmediği bağlamı tahmin
edememesi otomatik hata sayılmaz. Diğer örneklerde de geçerli alternatif okumalar
incelemeyle ele alınır; referanslar kapalı bir doğru-cevap listesi değildir.

## Sonuç görülmeden belirlenen pilot kabul ölçütleri

1. 60 örneğin tamamı için gerçek sonuç veya açık bir başarısızlık kaydı bulunmalı.
   Eksik/boş sonuç tamamlanmış değerlendirme sayılmaz.
2. 50 bağımsız örnekte **kritik veya büyük anlam hatası olmamalı**. Eylemin tersine
   çevrilmesi, yanlış sayı, koşul veya alıcı/eşya değişimi kritik olabilir. Yanlış
   terim veya önemli anlam kaybı büyük hata sayılır. Anlamı koruyan ufak ifade
   pürüzleri ayrı, küçük dil sorunu olarak kaydedilir.
3. Korunması gereken değişken ve tuş etiketleri yazımları ve adetleriyle korunmalı.
   Bunların cümledeki rolü ayrıca anlam incelemesi gerektirir.
4. Bağımsız örneklerin en az 45'i doğal Türkçe olarak değerlendirilmeli. Doğal
   görünmek anlam doğruluğunun yerine geçmez.
5. Bağlama bağlı 10 örnek ve terim tutarlılığı ayrıca raporlanmalı. Sonuçlar görülüp
   başarısızlıkları gizlemek için ölçütler değiştirilmemeli. Küme/ölçüt değişirse yeni
   sürüm ve yeni değerlendirme gerekir.

Anlam incelemesi: `correct`, `minor_wording`, `major`, `critical` veya `unavailable`.
Doğallık incelemesi: `natural`, `awkward`, `unreadable` veya `unavailable`.
Bağlama bağlı okumalar için açıklama zorunludur. İlk incelemeyi asistan yapabilir;
inceleyen kişi ve bağımsız inceleme durumu sonuç raporuna açıkça yazılmalıdır.

## Çevrimdışı kullanım

```sh
python3 tests/TranslationQualityChecks/checks.py
python3 tests/TranslationQualityChecks/checks.py --template /private/tmp/localay-pending-outputs.json
```

İlk komut veri bütünlüğünü ve değerlendirme aracının hata senaryolarını sınar.
İkinci komut çevirileri boş, `kind=pending_engine_output` olan bir şablon üretir;
var olan dosyanın üzerine yazmaz. **İkisi de çeviri yapmaz.** Çıktıdaki SHA256,
değerlendirilecek kümenin ve JSON içindeki `acceptanceCriteria` ölçütlerinin tam
bayt içeriğini bağlar.

İleride gerçek Google web çıktıları ayrı toplanınca:

```sh
python3 tests/TranslationQualityChecks/checks.py \
  --prepare-review /private/tmp/localay-real-web-outputs.json \
  --out /private/tmp/localay-review.json
```

Gerçek toplama aracı kaynak `id`, `source` ve `corpusSha256` değerlerini korumalı;
`provenance.kind=engine_output`, `provider=Google Translate`, `mode=web` ve dil
kodlarını kaydetmelidir. Bunlar beyan edilen kaynaktır, bağımsız doğrulanmış veya
kriptografik olarak motorun imzaladığı kanıt değildir. Referans Türkçe metinler
sonuç alanlarına doldurulmamalıdır.

Tek blok ve 15'li paket koşulları `requestLayout=single` / `batch15` ile **ayrı
dosyalarda** tutulur; başka blokların metinsel bağlam etkisi aynı sonuç havuzunda
gizlenmez. Tek blok koşulunda yalnızca `source` gönderilir. Kabul açıklamaları ve
oyun bağlamı modele gönderilmez. Bu çevrimdışı hazırlama komutu gerçek sonuç toplamaz.

Araç bilinmeyen/tekrarlanan kimlikleri, yanlış kaynak metni ve farklı küme karmasını
reddeder. Eksik sonuçları, değişen değişkenleri ve sayı biçimi farklarını işaretler.
Sayıların yazıyla yazılması veya yerel binlik ayırıcı kullanımı doğru olabileceği
için sayı uyarısı insan incelemesi ister. Sayılar aynen kalsa da roller ters olabilir:
“3 değil 2” ile “2 değil 3” ayrımını bu kontroller çözmez.

Üretilen inceleme raporu her zaman `pending_semantic_review` durumundadır;
`qualityScore`, `semanticReview` ve `naturalnessReview` boş başlar. Yapısal
kontrollerin geçmesi kalite onayı değildir. Sahte yanıtlar yalnızca aracın
regresyon testlerinde kullanılır; motor başarı istatistiğine katılmaz.

Bu araç gecikme ölçmez. İleride alınan servis süreleri de OCR, yakalama, yerleşim ve
fiziksel ekran sunumunu kapsayan uçtan uca gecikme olarak sunulmamalıdır.

## Gerçek web çıktısı toplama aracı

`WebCapture`, uygulamadaki `GoogleTranslateFreeClient` kaynak dosyasını doğrudan
derler; paketli modda `GoogleTranslateService` ve blok ayrıştırıcısını da çalıştırır.
Cloud kapalı, dil kodları en/tr olan izole ayarlar kullanılır; kullanıcı ayarları ve
anahtarları okunmaz. İkinci sağlayıcı fabrikası çağrılırsa hata verir. Gerçek ağ
gönderiminde yalnızca mevcut ücretsiz web uç noktaları kabul edilir. Windows/WPF
çalıştırılmaz. Şu komut yerel kümeyi ve sahte HTTP ile üretim paketleme kodunu
kontrol eder, gerçek ağ çağrısı yapmaz:

```sh
dotnet run --project tests/TranslationQualityChecks/WebCapture/WebCapture.csproj -c Release -- \
  --check tests/TranslationQualityChecks/corpus.json
```

Gerçek toplama açıkça `--run` gerektirir. Aşağıdaki komut 60 özgün İngilizce metni
Google'ın ücretsiz web çeviri hizmetine **gönderir**; referansları göndermez:

```sh
dotnet run --project tests/TranslationQualityChecks/WebCapture/WebCapture.csproj -c Release -- \
  --run tests/TranslationQualityChecks/corpus.json /private/tmp/localay-web-outputs-NEW.json
```

Her örnek ayrı gönderilir. Aynı istemcinin istek aralığı, geri çekilme ve iptal
davranışı korunur. İlk başarısız örnekte toplama durur; eksikler `not_attempted`
olarak kaydedilir. Kota engelini aşmak için başka sağlayıcıya geçilmez. Yeni çıktı
dosyası önceden ayrılır; mevcut dosya üzerine yazılmaz. Referanslar sonuçları
doldurmak için kullanılmaz. Toplama, önbellek veya 15'li paket yolunu sınamaz.

Üretimdeki paketli yolu aynı kaynak sırasıyla çalıştırmak için:

```sh
dotnet run --project tests/TranslationQualityChecks/WebCapture/WebCapture.csproj -c Release -- \
  --run-batch15 tests/TranslationQualityChecks/corpus.json /private/tmp/localay-batch15-outputs-NEW.json
```

Bu mod gerçek web çağrısı yapar. Dört 15'li grup üretim servisine verilir; mevcut
1000 karakter sınırı ve çok satırlı blokların ayrılması nedeniyle gerçek HTTP
sayısı dört olmayabilir. Önceki tekli ham çıktılar yeniden kullanılmaz, gerçek
çeviriler tekrar alınır. Çeviri koordinatörü, önbellek ve WPF ayar deposu bu araçta
çalışmaz. Paketlerde hata oluşursa kalan paketler denenmez; eksik bloklar kaydedilir.
Her blok için paylaşılan paket kimliği, paket başına süre ve gerçek ağ grupları
kaydedilir. Paket süresi her bloğa gecikme olarak kopyalanmaz veya blok sayısına
bölünerek kişisel gecikme üretilmez.

Sahte HTTP testleri satır sayısı tutmadığında tekli isteğe dönüşü doğrular. Aynı
satır sayısıyla ters sırada dönen sahte yanıt ise konumsal kabulün sınırını gösterir:
satır sayısı tek başına doğru anlamın doğru bloğa ait olduğunu kanıtlamaz. Bu
sentetik bulgu gerçek Google koşusunda gözlenen kayma olarak sunulmaz.

Kayıtta küme SHA256 değeri, derlenmiş aracın SHA256 değeri, UTC zamanları, örnek
başına durum ve süre bulunur. Süre istemcinin aralık beklemesini ve kendi yeniden
denemelerini içerir; yalnızca sunucu işlem süresi veya oyun gecikmesi değildir.
Çıktının başarılı alınması, Türkçesinin doğru olduğuna dair puan değildir.
