# Oyun çeviri motoru için uygulama kararı

Tarih: 2026-10-23. Rol: teknik sorumlu.

## Karar

Ücretsiz, tek AI motoru yönünde entegrasyon tercihi **Tencent Hy-MT2 1.8B**.
Bu bir donanım uygunluğu kararı veya yeni aday karşılaştırma kampanyası değildir.
Önceki donanım temelli değerlendirme geri çekilmiş olarak kalır; aşağıdaki tercih
ürün gereksinimleri ve yayımlanmış model sözleşmesi üzerinden verilmiştir.

[Resmî model kartı](https://huggingface.co/tencent/Hy-MT2-1.8B), çeviriye odaklı
modeli, İngilizce/Türkçe dâhil destek listesini ve sade çeviri istemini yayımlıyor.
[Resmî GGUF lisansı](https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF/blob/main/LICENSE.txt)
Apache-2.0. Bu nedenle çokdilli çeviri görevi ve ücretli API bağımlılığını kaldırma
hedefi için bu modeli seçiyorum. İngilizce–Türkçe oyun çevirisinde Google'dan
kesin daha iyi olduğu veya her girdide doğru çalıştığı sonucunu çıkarmıyorum.

## Mevcut durum

Hy-MT2 adaptörü, oyun başlatma/dil kontrolleri, önbellek kimliği ve doğrudan yanıt
ayrıştırması tamamlandı. Model indirilmedi/çalıştırılmadı; yerel servis kurulmadı.
Genel sağlayıcı varsayılanı ve kayıtlı tercihler değiştirilmedi. Yeni, boş Ollama
yapılandırmasının model varsayılanı Hy-MT2 oldu. Bu teknik entegrasyon, yerel
motorun kurulu olduğunu veya gerçek çıktı kalitesinin onaylandığını iddia etmez.

Küçük sunum/hattı iyileştirmeleri mevcut: hazır paketler bekletilmeden aktarılıyor,
altyazı adayları önce sıralanıyor, sabit panel ölçüm/yerleşimi yeniden kullanılıyor.
Bunlar sağlayıcı gecikmesini ve anlam hatalarını çözmez. Google web yolundaki
1000 ms istek aralığını düşürmek çözüm olarak seçilmedi.

## Entegrasyon sınırı

- Mevcut `ITranslationService`, Ollama bağlantısı, önbellek, iptal ve blok
  kimlikleri kullanılacak. Yeni yakalama/kaplama mimarisi kurulmayacak.
- Hy-MT2 için özel sade metin profili eklendi. Genel Ollama yolunun JSON üretme
  talimatı modele gönderilmiyor. Blok kimliklerini uygulama yönetiyor.
- Ana motor tek olacak; başka modelle yarışma, otomatik Google/DeepL fallback
  veya yeni ücretli servis kurulumu eklenmeyecek.
- API kullanım ücreti gerekmemesi, kurulum gerekmemesi anlamına gelmez: yerel
  model ve onu çalıştıran servis gerekir. Bu adım bunları kurmaz veya indirmez.
- Diğer diller desteklenmeye devam edecek; ancak modelin yayımlanmış listesinde
  bulunmayan mevcut dil seçenekleri bu profile sessizce kabul edilmeyecek.
  Örneğin `da`, `fi`, `ro` için destek varsayılmıyor. Bu küçük dil uygunluğu
  kontrolü, tek motor tercihiyle birlikte entegrasyona eklendi.
- Mevcut yerel motor kabul politikası sahte ölçümlerle geçirilmiş gibi
  gösterilmeyecek; teknik entegrasyon ile gerçek kalite/performans kanıtı ayrı kalacak.

## Kalan gerçek sınırlar

Altyazı algılama alt-orta bölgeye yönelik sezgiseldir; bütün oyun düzenlerini
tanıdığı söylenemez. Google'ın mevcut satır eşleme yolu yalnızca satır sayısını
doğrulayabilir; aynı sayıda satırın anlam bakımından yer değiştirmesini kesin
saptayamaz. Yerel profilin blok kimliklerini model dışında tutması bu eşleme
varsayımını taşımamayı amaçlar. Modelin anlam hatalarını ortadan kaldırmaz.

Windows doğrulaması, GitHub Actions, workflow değişikliği, donanım karşılaştırması
ve yeni gerçek-model test kampanyası bu planın parçası değildir. Bu adım uygulama
kodu için yalnız mevcut adaptör/hattın dar entegrasyonunu kapsar.

Geliştirme kapsamı: dar entegrasyon ve yerel teknik kontroller. Sonraki kullanım
adımı [bir kerelik yerel kurulum](hymt2-setup.md); yeni bir geliştirme veya model
karşılaştırma turu değildir. Otomatik indirme/kurulum veya sağlayıcı değişimi eklenmedi.
