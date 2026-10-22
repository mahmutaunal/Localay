# Hy-MT2 ile yerel oyun çevirisi

Entegrasyon tamamlandı; yerel kullanım için Ollama ve modelin bir kere kurulması
gerekir. Localay bunları bu çalışma sırasında indirmedi veya kurmadı.

1. [Ollama](https://ollama.com/download) kurulu ve çalışıyor olmalı. İndirme
   bağlantısı Localay'in çeviri ayarlarında da bulunur.

2. Ana ekrandan **Çeviri ayarları → Yerel yapay zekâyı kullan → Kaydet** yolunu
   izleyin. Model otomatik belirlenir. Ardından **Modeli hazırla** ile yalnızca
   gerekiyorsa indirin. Bu işlem çeviri çalıştırmaz. Bağlantı ayrıntıları isteğe
   bağlı açılır; yerel varsayılan adres `http://localhost`, bağlantı noktası `11434`.

3. Oyunu ve dilleri seçip başlatın. Sonraki oturumlarda model indirme işlemi
   tekrarlanmaz. Model ilk kez belleğe alınırken başlangıç beklemesi olabilir.

Terminal kullanmayı tercih edenler için aynı modelin indirme komutu:

```text
ollama pull hf.co/tencent/Hy-MT2-1.8B-GGUF:Q4_K_M
```

Bu kurulum çeviri API anahtarı veya ücretli API hesabı gerektirmez. İlk model
indirmesi internet gerektirir; çeviri yerelde çalışır. Ollama'nın çalışıyor olması
ve modelin kurulmuş olması gerekir. Localay'deki model adı bir yapılandırma
kontrolüdür; tek başına modelin kurulduğunu veya servis bağlantısının hazır
olduğunu kanıtlamaz. Hazır değilse Google'a otomatik geçilmez.

İngilizce–Türkçe önceliklidir. Modelin yayımlanmış listesinde yer alan diğer dil
çiftleri de kullanılabilir; listede olmayan dil çiftleri başlatma ve istek
aşamasında engellenir. Kaynak dili otomatik tahmin eden yeni bir yol eklenmedi.

## Teknik kapsamın son durumu

- Sade çeviri istemi; modelin üretmediği, uygulamanın yönettiği blok kimlikleri.
- Kaynak başına tek yerel üretim; hazır altyazı kalan menü üretimlerini beklemez.
- Mevcut kaynak önbelleği, iptal, sahne güncelliği ve altyazı önceliği korunur.
- Yer tutucu/işaretleme değişimi, kesilmiş veya bozuk yanıt gösterime alınmaz.
- Yeni motor karşılaştırması veya gerçek model test kampanyası planlanmıyor.

Teknik kontroller gerçek model çıktısı veya gerçek oyun gecikmesi ölçümü değildir.
Model dosyası bu çalışma sırasında yerel olarak doğrulanmadı. Mevcut model etiketi
ve istem sürümü önbelleğe dahildir; etiketin işaret ettiği ağırlıkların değişmezliği
ve anlamsal doğruluk garanti edilmiyor. Windows/Actions doğrulaması yapılmadı ve
workflow dosyaları değiştirilmedi.
