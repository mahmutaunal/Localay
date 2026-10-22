# Geri çekilen yerel model seçimi — arşiv araştırması

Durum güncellemesi: 2026-10-22. **Donanıma dayalı Hy-MT2 aday seçimi geri çekildi.**
Aşağıdaki karşılaştırma ve geçiş koşulları eski araştırmanın kaydıdır; yürürlükteki
plan veya yeni test/indirme talimatı değildir. Model indirilmedi, çalıştırılmadı ve
varsayılan motor değiştirilmedi. Yeni yön: [dar kapsamlı oyun planı](game-experience-plan.md).

## Donanıma göre karar

Kullanıcının hedefi Ryzen 5 7500F, RTX 4060 ve 16 GB DDR5 RAM. NVIDIA'nın
[özellik sayfasında](https://www.nvidia.com/en-us/geforce/graphics-cards/40-series/rtx-4060-4060ti/)
RTX 4060 için 8 GB VRAM belirtiliyor. Oyun ve model bu belleği paylaşacak.
Küçük model seçmek bu nedenle önemli; karta sığmak, oyunla birlikte akıcı çalışmak
anlamına gelmez. Boş VRAM, oyun ayarları ve işlem yükü burada ölçülmedi.

Hy-MT2'nin [resmî listesinde](https://huggingface.co/tencent/Hy-MT2-1.8B) Türkçe,
İngilizce ve başka diller var. [Q4 dosyası](https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF/blob/main/Hy-MT2-1.8B-Q4_K_M.gguf)
yaklaşık 1,13 GB; [lisansı Apache-2.0](https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF/blob/main/LICENSE.txt).
İlk indirmeden sonra yerel çıkarım için çeviri API hesabı/faturalandırması gerekmiyor.
Dosya boyutu çalışma RAM/VRAM tüketimi değildir; elektrik ve bilgisayar kaynakları
kullanılır. 16 GB sistem belleğinin tamamı modele ayrılmış sayılmaz.

## Neden bu aday?

| Aday | Bu aşamadaki değerlendirme |
| --- | --- |
| Google web | Bizim tekli ve paketli pilotlarımız kalite ölçütlerini geçmedi. |
| OPUS/Marian en→tr | Mevcut özel paket tek dil çiftine bağlı; tek çokdilli ana motor hedefini karşılamıyor. Native paket de hazır değil. [Model kartı](https://huggingface.co/Helsinki-NLP/opus-mt-tc-big-en-tr). |
| TranslateGemma 4B | Özel adaptör var ama mevcut profil en→tr ile sınırlı ve blokları sırayla çeviriyor. [Ollama paketi](https://ollama.com/library/translategemma:4b) yaklaşık 3,3 GB. Daha küçük aday denenmeden oyun belleğine daha fazla yük vermek için yeterli gerekçe yok. |
| Gemma 4 E2B | Ciddi bir genel amaçlı alternatif. [Kartındaki](https://huggingface.co/google/gemma-4-E2B-it) çokdilli ön eğitim, bütün dillerde oyun çevirisi onayı değildir. Güncel [Ollama etiketi](https://ollama.com/library/gemma4:e2b) yaklaşık 7,5 GB gösteriyor; farklı/özel nicemlemelerin ölçülerini bu paketle karıştırmamak gerekir. Bu turda ikinci bir model indirilip çalıştırılmayacak. |
| **Hy-MT2 1.8B Q4_K_M** | Çeviriye odaklı, küçük, Türkçe listelenmiş; ilk gerçek değerlendirme için seçildi. Daha iyi Türkçe verdiği henüz bizim verimizle kanıtlanmadı. |

## Yayımlanmış kanıtın sınırları

Üreticinin [teknik raporu](https://arxiv.org/html/2605.22064v1) çokdilli çeviri
ve nicemleme karşılaştırmaları sunuyor. Genel puanlar bu projenin en→tr oyun
kümesindeki başarı oranı değildir. Q4 ile tam hassasiyet sonuçları da aynı sayılmaz.
Küçük 440 MB sürüm için belirtilen özel nicemleme/hız bulguları Q4 dosyasına
aktarılmayacak. STQ için [açık runtime geliştirmesi](https://github.com/ggml-org/llama.cpp/pull/22836)
bulunuyor; deneysel düşük bit yolu bu dar kapsamın dışında.

Bir oyun kaplaması geliştiricisi [kendi deneyinde](https://playto.dev/blog/hy-mt2-in-practice/)
RTX 4070 Ti ile kısa satırda 85 ms medyan ve 1,77 GB VRAM bildirmiş. Aynı yazıda
hedef dil başarısı 69/72; İngilizce–Japonca referans ölçümünde Gemma 4 E2B daha
yüksek chrF alıyor. Bu, Hy-MT2'nin her dilde en iyi olduğuna kanıt değildir.
Yazı Türkçeyi küçük dil kontrolleri arasında anıyor ama bizim 60 örneğimizin
çıktılarını sunmuyor. Ürün geliştiricisinin kendi ölçümü, bağımsız laboratuvar
sonucu değildir; 4070 Ti süresi 4060'a, uzun metne, oyuna veya uçtan uca gecikmeye
taşınamaz. Windows doğrulaması bu araştırmada yapılmadı.

## Dil kapsamı engeli

Yerel modelin listesi mevcut Google dil seçenekleriyle birebir örtüşmüyor.
Örneğin uygulamada `da`, `fi`, `ro` seçenekleri var; bunlar incelenen Hy-MT2
destek tablosunda yok. Türkçe yanında Japonca, Korece, Almanca, Fransızca gibi
listelenmiş diller değerlendirilebilir. Fakat bütün mevcut seçeneklerin bu modelle
aynı kaliteyle çalıştığı söylenemez. Desteklenmeyen çiftler sessizce çalıştırılmamalı
veya başka modele gönderilmemeli. Mevcut dil menüsü bu araştırmada değiştirilmedi.
Tüm mevcut dillerin korunması zorunluysa bu adayın tek motor olarak devreye alınması
bu engel çözülmeden onaylanamaz.

## Mevcut yapıya bağlanma kapsamı

Projede `ITranslationService`, Ollama bağlantısı, iptal, önbellek ve blok kimlik
protokolü zaten var. [Ollama donanım listesi](https://docs.ollama.com/gpu) RTX 4060'ı
kapsıyor; bu, her model/runtime birleşiminin hazır olduğunu kanıtlamıyor.
[GGUF içe aktarma](https://docs.ollama.com/import) mevcut yerel servis yolunu
kullanmaya imkân verir. Kesin runtime ve sohbet şablonu uyumu ayrıca sınanmalı.

Genel Ollama adaptörü JSON üretimini modelden bekliyor. Bunu bu çeviri modeline
aynen göndermek uygun bir hazır entegrasyon sayılmaz. Gerekli küçük özel profil,
üreticinin sade çeviri istemiyle gerçek metni gönderip uygulamanın kimliklerini
model dışında tutmalı. Böylece web yolundaki satır sayısından kimlik çıkarma
varsayımı da taşınmaz. Bu profil henüz yazılmadı. İkinci model, otomatik çevrimiçi
fallback veya yeni kaplama mimarisi planlanmıyor.

Bir başka sınır: 15 bloğu sırayla çevirip hepsini beklemek, tek satır hızlı olsa
bile UI'yı geciktirebilir. Model seçimi bu beklemeyi kendiliğinden çözmez. Gerçek
profil ölçümünde tek altyazı ile çok bloklu menü ayrı tutulmalı; ortalama toplam
süreyi blok sayısına bölmek görünür gecikme kanıtı sayılmamalı.

## Sonraki geçiş koşulları

1. Resmî dosyanın yayımlanmış SHA256 değerini indirme sonrasında yerelde doğrula.
   Runtime sürümü, model digest'i, şablon ve çıkarım ayarlarıyla birlikte kaydet.
2. Windows kullanmadan, ayrı test aracında aynı 60 kaynağın gerçek çıktısını al;
   referansları isteme koyma, mevcut pilot ölçütlerini değiştirme.
3. Anlam, yer tutucu ve hedef dil kontrollerini yap. Çokdilli destek ve eksik dil
   seçeneklerini açıkça kaydet. Donanım/oyun kaynak paylaşımı bu adımla ölçülmüş olmaz.
4. Kalite kanıtı olmadan varsayılanı değiştirme. `LocalModelDecision` hâlen eski iki
   profili tanıyor; araştırma notunu sahte ölçümlerle bu politikadan geçirilmiş gibi
   göstermemek için uygulama/politika değiştirilmedi.

Bu aşamada sonuç: **değerlendirme için tek aday seçildi; üretime geçiş onaylanmadı.**
