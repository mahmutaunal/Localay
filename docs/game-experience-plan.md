# Dar kapsamlı oyun deneyimi kararı

Başlangıç: 2026-10-22. Güncelleme: 2026-10-23.
İlk araştırmanın ardından aşağıdaki küçük uygulama müdahaleleri tamamlandı.
Güncel [motor uygulama kararı](game-engine-direction.md) bu planı tamamlar.

## Referans üründen doğrulananlar

[TranslaCat resmî sayfası](https://translacat.info/en), altyazı bölgesini seçerek
okuma ve ayrı bir çeviri kutusunun boyut/konumunu ayarlama yaklaşımını anlatıyor.
Android cihaz içi yolunda Google ML Kit, iPhone'da Apple Translation kullanıldığını
belirtiyor. İsteğe bağlı AI çevirisi DeepL'e metin gönderiyor ve token satın almayı
gerektiriyor. Altyazı ve menüyü birlikte çevirme özelliği henüz gelecek özellik
olarak listeleniyor. Sayfada uçtan uca birkaç milisaniye ölçümü yayımlanmıyor.

Bu bilgilerden çıkarımımız: ürünün sabit kutu yaklaşımı örnek alınabilir;
ücretsiz modu ile ücretli AI modunun maliyet ve kalite beklentileri birleştirilemez.
Orijinal yazıyı kapatmak bizim sunum tercihimizdir; referans ürünün doğrulanmış
özelliği olarak sunulmayacak. Sayfa kamera kullanımında kaynak ve çeviri alanlarını
ayırmayı öneriyor.

## Mevcut kodun durumu

- `SubtitlePanelPolicy.cs` zaten kaynak fontunu kopyalamadan sabit altyazı paneli
  planlıyor; belirsiz metinler normal UI kutularında kalıyor.
- `MonitorWindow.xaml.cs` paneli gerçek çeviri boyutuyla ölçüp kaynak metni
  örten opak kutuyu gösteriyor. HUD çakışması denetleniyor.
- `Logic.cs` ortak sağlayıcı yolunu koruyor; hazır paketler ayrı aktarılıyor ve
  altyazı adayları önce sıralanıyor. Altyazıya özel kırpılmış yakalama eklenmedi.
- `GoogleTranslateFreeClient.cs` istekler arasında 1000 ms aralık uyguluyor.
  `Logic.cs` OCR alt aralığı 200 ms. Bunlar kaynak kodu değerleridir; gözlenen
  oyun gecikmesi değildir. Panel değişikliği ağ beklemesini ortadan kaldırmaz.

## Kilitlenen kapsam

1. Kullanıcı akışı oyun seçimi, kaynak/hedef dil ve başlat olarak kalacak.
   Elle alan seçimi veya yeni mod seçenekleri eklenmeyecek.
2. Mevcut altyazı algılaması ve opak, kararlı panel korunacak. Font eşleme,
   arka planı yapay olarak yeniden üretme veya yeni OCR motoru eklenmeyecek.
3. İlk uygulama dokunuşu: mevcut blok/önbellek/iptal yolunu kullanarak değişen
   altyazının sabit HUD ve menü metinleriyle aynı tamamlanma bariyerinde beklemesini
   kaldırmak. Bağımsız hazır blokları göstermek ve yalnızca güncel sahneye ait
   sonuçları kabul etmek hedeflenecek; ikinci motor veya paralel sağlayıcı yok.
4. Sonraki küçük dokunuş ancak bu inceleme gerek gösterirse: öğrenilmiş altyazı
   alanına daha sık bakmak; menü/UI takibini korumak. Tüm ekranı körlemesine
   kırpmak, sabit metinleri yeniden çevirmek ve yeni alan seçim ekranı kapsam dışı.

## Motor kararı ve sınır

Mevcut Google web yolu geçici olarak kalıyor; önceki gerçek çıktı incelemesinde
hedef kaliteyi karşılamadı. Bu plan ona kalite onayı vermez. 2026-10-23 tarihli
uygulama kararı Hy-MT2 1.8B entegrasyonunu seçiyor; doğruluk garantisi vermiyor.
TranslaCat'ın AI seçeneğini
kopyalamak ücretli hizmet bağımlılığı getirir; kullanıcının maliyet tercihi nedeniyle
bu plan DeepL hesabı/API anahtarı veya ödeme kurulumu içermiyor.

Donanıma dayalı Hy-MT2 seçimi geri çekildi. Model indirme, yeni çıkarım kampanyası,
Windows doğrulaması, GitHub Actions çalıştırma ve workflow değişikliği planlanmıyor.
Yeni gecikme veya doğruluk sayısı verilmedi.

## İlk küçük müdahale

Tamamlanmış bloklar koordinatörün `Task.WhenAll` çağrısında bütün sahneyi
bekliyordu. Oyun yolu artık tamamlanmış blokları UI iş parçacığına aktarabiliyor;
diğer paketlerin tamamlanmasını beklemiyor. Ön yükleme ile paylaşılan sonuçlar da
aynı yoldan geliyor. İşlem/sahne/ayar geçerliliği UI kuyruğundan çıktıktan sonra
yeniden denetleniyor; mevcut kaynak kimliği ve görünürlük korumaları korunuyor.

Paket büyüklüğü, sağlayıcı sayısı, istek aralığı ve tek gerçek sağlayıcı çağrısı
sınırı değişmedi. Sağlayıcı tek paketin içindeki sonuçları birlikte döndürür;
bu paket içindeki bekleme devam eder. Aşağıdaki sıralama değişikliği altyazı
adaylarını ilk pakete yaklaştırır. Bu değişiklik OCR veya ağ süresini kısaltmaz.

## Altyazı sıralaması

Ön yükleme ve ana çeviri, mevcut panel politikasının alt-orta bölge aday ölçütlerini
aynı sıralama yardımcısıyla kullanıyor. Aday metinler önce, diğer metinler kendi
aralarındaki sıraları korunarak gönderiliyor. Panel için gereken zaman içindeki
altyazı kanıtı hâlâ zorunlu; öncelik verilmesi metnin üzerinin kapatılmasına izin
vermez. Konum önceliği kesin bir diyalog sınıflandırması değildir.

Fiziksel OCR koordinatları aynı DPI ile hesaplanan görünüm ölçüleriyle
karşılaştırılıyor. Birleştirilen ön yükleme metinlerinde gösterim yolunun kullandığı
orijinal kaynak sınırları kullanılıyor. Önbellekte bulunan metinler gönderilmiyor;
tekilleştirme, kimlik eşleme, 15 blok sınırı ve tek sağlayıcı korunuyor. Altyazıya
ayrı bir sağlayıcı çağrısı açılmıyor. Google istemcisi kendi karakter/satır
sınırlarına göre HTTP alt grupları oluşturduğu için kesin HTTP istek sayısının her
sahnede değişmeden kalacağı ileri sürülmüyor.

## Panel yenileme

Sabit ve kabul edilmiş panel artık her yenilemede Collapsed/Hidden durumlarına
geçirilmiyor. İçerik, font, genişlik veya WPF ölçüm geçerliliği değiştiğinde yeniden
hazırlanıyor. DPI değişiminde ölçüm açıkça geçersiz kılınıyor; yeni oyun ölçüm
anahtarını sıfırlıyor. Yükseklik sınırsızken alınan metin ölçüsü saklanıyor;
üst kapsayıcının sonradan sınırlı yükseklikle yaptığı ölçüm, tam metnin sığdığı
kararının yerine geçmiyor. Değişmeyen panel boyutu/konumu tekrar atanmıyor.

Kaynak güncelliği, altyazı kanıtı, HUD çakışması ve metnin tamamının sığması her
yenilemede kontrol edilmeye devam ediyor. Kaynak kaybolduğunda veya bu kontroller
başarısız olduğunda panel gizleniyor. Gerçek WPF çizimi veya milisaniye kazancı
ölçülmedi; bu bir kaynak kodu optimizasyonudur.

Dar Hy-MT2 entegrasyonu tamamlandı; teknik sözleşme kontrolleri gerçek model
çalıştırmadan yürütülüyor. Sonraki kullanım adımı [yerel kurulum](hymt2-setup.md);
yeni bir geliştirme veya model karşılaştırma turu planlanmıyor.
