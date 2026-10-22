# Ücretsiz Google web: paketli çeviri karşılaştırması

Tarih: 2026-10-22. **Paketleme bu kümede anlam kalitesini değiştirmedi; pilot kabul ölçütleri yine geçilmedi.**

Üretimdeki `GoogleTranslateService`, `GoogleTranslateFreeClient`, `TranslationBlockProtocol` ve `TranslationParts` kaynak dosyaları taşınabilir araçta doğrudan derlendi. Paketleme algoritması test için kopyalanmadı veya değiştirilmedi. WPF ayar deposu yerine yalnızca en/tr, Cloud kapalı ve anahtarsız test ayarları kullanıldı. Çeviri koordinatörü ve önbellek çalıştırılmadı; aynı 15 blokluk üst sınırla dört grup servise gönderildi. İkinci sağlayıcı fabrikası çağrılırsa hata verir; bu koşuda çağrı sayısı sıfırdı. Gerçek HTTP kaydı yalnızca mevcut ücretsiz web uç noktalarına GET isteklerini kabul etti.

## Gerçek sonuçlar

| Kontrol | Gözlem |
| --- | --- |
| Kaynak kümesi | Aynı 60 kaynak; aynı SHA256 ve aynı sıra |
| Alınan çıktı | 60/60 |
| Tekli koşuyla aynı çıktı | 60/60, kimlik bazında birebir eşit |
| Değişken ve sayı uyarısı | 0 |
| Mantıksal paket | 4 × 15 blok |
| Gerçek web isteği | 6; hepsi HTTP 200 |
| Ağ isteklerinin blok sayıları | 15, 15, 15, 11, 1, 3 |
| İkinci sağlayıcı denemesi | 0 |

Çok satırlı `format-07` kaynak bloğu üretim kodu tarafından tek başına gönderildi. Son mantıksal paketin 11+1+3 ayrımı bu davranıştan kaynaklanıyor. Gerçek koşuda satır sayısı uyumsuzluğu nedeniyle tek tek yeniden gönderim gözlenmedi. Bu kümede başka kaynağın çevirisinin bir bloğa kaydığı da gözlenmedi.

## Anlam ve doğallık

Tüm kaynak/çıktı çiftleri aynı olduğundan önceki asistan incelemesindeki gerekçeler aynen ilişkilendirildi. Bu bağımsız ikinci değerlendirme değildir. 50 bağımsız örnekte 35 doğru, 8 küçük ifade sorunu, 6 büyük hata ve 1 bağlamla belirsiz örnek; doğallıkta 38 doğal ve 12 doğal olmayan ifade sonucu değişmedi. 10 bağlam sondası ana puana katılmadı. Hataları gizlemek için küme, ölçüt veya çeviriler değiştirilmedi.

Örnekler hâlâ aynı: “That was a close call.” → “Bu yakın bir karardı.”; “I've got your back.” → “Arkanı kolladım.”; oturum sahibi → “toplantı sahibi”. Paketlemenin bu koşuda ek bağlamdan yararlanarak bu hataları düzelttiğine dair bulgu yok.

## Süre gözlemi

Dört mantıksal paket: 391.86 ms, 728.64 ms, 1010.22 ms, 2994.75 ms. Bu paketli koşunun toplamı **5.14 saniye**; önceki tekli koşunun toplamı **64.24 saniye**. Bunlar farklı zamanda yapılan birer gözlemdir, tekrarlı ve kontrollü hız sıralaması değildir.

Paketleme zaten mevcut; bu değerlendirmede uygulamaya yeni bir performans iyileştirmesi eklenmedi. Toplamı 60'a bölerek metin başına gecikme ilan etmek yanlış olur. Grup tamamlanmadan sonucu bekleyen bloklar vardır; son 15'li grubun süresi yaklaşık 3 saniyedir. Yakalama, OCR, önbellek isabeti, hareketli metin veya ekrana sunum bu ölçüme dahil değildir.

## Eşleşme kontrolünün sınırı

Çevrimdışı sahte HTTP testlerinde 15 blokluk eşleştirme, çok satırlı bloğun ayrılması ve satır sayısı bozulunca tekli gönderime dönüş doğrulandı. Ek olarak iki sahte çeviri satırı ters sırada ama aynı satır sayısıyla döndürüldü. Üretim kodu bunları konumsal olarak kabul etti. Bu **sentetik bir karşı örnektir**, gerçek Google koşusunda gözlenen kayma değildir.

Blok kimlikleri ve kaynak metinleri uygulama tarafından yanıta ekleniyor; web servisi bu kimlikleri döndürmüyor. Dolayısıyla eşit satır sayısı ve sonradan eklenen kimlikler tek başına doğru anlamın doğru bloğa ait olduğunu kanıtlamaz. Bu deneydeki 60/60 birebir eşitlik yalnızca bu kaynak ve koşu için olumlu gözlemdir.

## Sonuç ve kayıtlar

Paketleme ağ işini azaltıyor; bu kümede çeviri doğruluğunu artırmadı. Mevcut ücretsiz Google web yolu bu kanıtla hedef kaliteyi karşılayan motor olarak onaylanamaz. Ücretli API'ye geçilmedi, üretim koduna örneğe özel çeviriler eklenmedi. Ana motor kararı ücretsiz kullanım, doğruluk ve oyunla paylaşılacak donanım sınırları birlikte ele alınarak yeniden değerlendirilmelidir.

- [Paketli ham çıktı, ağ grupları ve süreler](google-web-batch15-2026-10-22.json)
- [Paketli örnek incelemesi ve karşılaştırma](google-web-batch15-2026-10-22-review.json)
- [Önceki tekli değerlendirme](ASSESSMENT-2026-10-22.md)
- [Küme ve önceden belirlenen ölçütler](../corpus.json)

Windows, GitHub Actions, ücretli API ve model indirme işlemi yapılmadı. Web hizmeti sabit model sürümü açıklamıyor. İncelemeyi referansları hazırlayan aynı asistan yaptı; bağımsız insan doğrulaması yok.
