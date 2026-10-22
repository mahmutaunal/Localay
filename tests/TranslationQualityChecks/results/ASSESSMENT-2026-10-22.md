# Ücretsiz Google web: tek blok İngilizce–Türkçe pilotu

Tarih: 2026-10-22. **Bu tek blok pilotu kabul ölçütlerini geçmedi.**

60 özgün kaynak için 60 gerçek çıktı, uygulamadaki `GoogleTranslateFreeClient` ile alındı. Cloud API, API anahtarı, faturalandırma, Windows veya GitHub Actions kullanılmadı. Başlangıçtaki sandbox DNS denemesi ayrı kayıttır ve aşağıdaki kalite sayımına katılmaz.

İnceleyen: Codex asistanı. Referansları hazırlayan ile inceleyen aynı asistandır; bağımsız insan değerlendirmesi değildir. Küçük, sentetik bir küme ve tek koşu bütün oyunları veya hizmetin başka zamanlardaki davranışını temsil etmez.

## Kabul sonuçları

| Ölçüt | Gözlem | Karar |
| --- | --- | --- |
| Çıktı kapsamı | 60/60 alındı | Tam |
| 50 bağımsız örnekte büyük/kritik hata olmaması | 6 büyük, 0 kritik hata | Geçmedi |
| Değişken/tuş/sayı yapısal kontrolü | 60 örnekte uyarı yok | Geçti; tek başına anlam kanıtı değil |
| En az 45/50 doğal Türkçe | 38 doğal, 12 doğal olmayan ifade | Geçmedi |
| Bekleme süresi teriminin iki kullanımında tutarlılık | `Bekleme süresinde azalma` / `Bekleme süresi: 10 saniye` | Tutarlı |

50 bağımsız örnekte 35 doğru, 8 küçük ifade sorunu, 6 büyük hata ve 1 bağlamla belirsiz değerlendirme var. Büyük/kritik hata ayrımı ve doğallık puanları asistan yargısıdır. Belirsiz örneği paydadan çıkararak başarı yüzdesi üretilmedi; tek bir anlam doğruluğu yüzdesi verilmedi. Ayrıca 10 bağlam sondası ana puandan ayrı tutuldu.

## Belirgin bulgular

- `That was a close call.` → `Bu yakın bir karardı.` Tehlikeyi kıl payı atlatma deyimi aktarılmamış.
- `I've got your back.` → `Arkanı kolladım.` Şimdiki destek/güvence geçmişte yapılmış eyleme dönmüş. Cümle akıcı olsa da anlam sorunu var.
- `Only the host can start this mission.` → `Bu göreve yalnızca toplantı sahibi başlayabilir.` Oyun oturumu rolü toplantı terimiyle aktarılmış.
- `So much for a quiet entrance.` → `Sessiz bir giriş için bu kadar.` Alay ve başarısız plan anlamı kaybolmuş.
- `Defeat the guards without raising the alarm.` → `Alarmı yükseltmeden gardiyanları yen.` Alarmı tetikleme eylemi yanlış adlandırılmış.
- `This item is consumed on use.` → `Bu madde kullanıldıkça tüketilir.` Anlık tüketim yerine kademeli kullanım çağrışımı var. Bu büyük hata sınıflandırması ayrıca insan incelemesine açık.

`We're not out of the woods yet.` çıktısı literal orman okumasını seçti. Küme bunu bağımsız örnek saymış olsa da kaynak literal/deyim okumalarına açık. Sonuç görüldükten sonra küme değiştirilmedi; örnek belirsiz olarak işaretlendi. `Charge!` gibi diğer çok anlamlı sondalarda da gizli oyun bağlamını model biliyormuş gibi hata puanı verilmedi.

## Sürelerin sınırı

60 istemci çağrısında medyan **976.03 ms**, en yakın sıra p95 **1659.62 ms**, aralık **158.27–3244.80 ms**. İstekler seri ve önbelleksizdi. Bu süreler istemcinin mevcut bir saniyelik istek aralığını, varsa yeniden denemesini ve ağı içerir. Salt model/sunucu işlem süresi veya oyun içi uçtan uca gecikme değildir.

Bu deney paketli çeviri, önbellek isabeti, OCR, ekran yakalama veya WPF yerleşimini ölçmedi. Tek blok sonucundan paketli üretim yolunun aynı sonuçları vereceği veya bütün uygulamanın bu hızda çalışacağı çıkarılamaz.

## Uygulama kararı

Bu bulgularla ücretsiz Google web yolunu kusursuz oyun çevirisi veya birkaç milisaniyelik hizmet olarak sunmak doğru olmaz. Motor, istek aralığı, önbellek veya gerçek çıktılar bu puanı artırmak için değiştirilmedi. Örneklere özel karşılıklar uygulamaya eklenmedi. Sonraki teknik ayrım, üretimdeki paketli çeviri yolunun aynı kaynaklarda anlamı ve blok eşleşmesini nasıl etkilediğidir.

## İzlenebilir kayıtlar

- [Başarılı ağ koşusunun ham çıktıları](google-web-single-2026-10-22-network.json)
- [Örnek bazında asistan incelemesi](google-web-single-2026-10-22-review.json)
- [Ayrı sandbox DNS başarısızlığı](google-web-single-2026-10-22.json)
- [Önceden belirlenen küme ve ölçütler](../corpus.json)

Küme SHA256: `a4016e7b930cc9b575fdf2a3990429f55571a280d31c73ca47b84de5794045e8`. Ham sonuçta derlenmiş araç SHA256 ve UTC başlangıç/bitiş zamanları bulunur. Web hizmetinin model sürümü açıklanmadığından sabit ağırlık sürümü iddiası yoktur.

## Bütün örnekler

Bağlam sondaları aşağıda gösterilir ama ana puana katılmaz. Ayrıntılı gerekçeler inceleme JSON dosyasındadır.

| Kimlik | Kaynak | Gerçek çıktı | Anlam incelemesi | Doğallık |
| --- | --- | --- | --- | --- |
| ui-01 | Continue game | Oyuna devam et | Doğru | Doğal |
| ui-02 | Load last checkpoint | Son kontrol noktasını yükle | Doğru | Doğal |
| ui-03 | Overwrite this save? | Bu kaydetmenin üzerine yazılsın mı? | Küçük ifade sorunu | Doğal değil |
| ui-04 | Unsaved progress will be lost. | Kaydedilmemiş ilerleme kaybolacaktır. | Doğru | Doğal |
| ui-05 | Hold to confirm | Onaylamak için basılı tutun | Doğru | Doğal |
| ui-06 | Invert vertical look | Dikey görünümü ters çevir | Küçük ifade sorunu | Doğal değil |
| ui-07 | Subtitles for dialogue only | Yalnızca diyalog için altyazılar | Doğru | Doğal |
| ui-08 | Restore default settings | Varsayılan ayarları geri yükle | Doğru | Doğal |
| ui-09 | Return to title screen | Başlık ekranına dön | Doğru | Doğal |
| ui-10 | Purchase unavailable while offline | Satın alma işlemi çevrimdışıyken yapılamıyor | Doğru | Doğal |
| instruction-01 | Do not release the prisoner. | Mahkûmu serbest bırakmayın. | Doğru | Doğal |
| instruction-02 | You cannot fast travel while enemies are nearby. | Düşmanlar yakındayken hızlı seyahat edemezsiniz. | Doğru | Doğal |
| instruction-03 | Defeat the guards without raising the alarm. | Alarmı yükseltmeden gardiyanları yen. | Büyük hata | Doğal değil |
| instruction-04 | Unless you lower the bridge, the caravan cannot cross. | Köprüyü indirmediğiniz sürece kervan geçemez. | Doğru | Doğal |
| instruction-05 | Only the host can start this mission. | Bu göreve yalnızca toplantı sahibi başlayabilir. | Büyük hata | Doğal değil |
| instruction-06 | Destroy the generator before opening the gate. | Kapıyı açmadan önce jeneratörü yok edin. | Doğru | Doğal |
| instruction-07 | Keep the target alive until extraction. | Çıkarılana kadar hedefi canlı tutun. | Küçük ifade sorunu | Doğal değil |
| instruction-08 | Taking damage interrupts healing. | Hasar almak iyileşmeyi kesintiye uğratır. | Doğru | Doğal |
| instruction-09 | This door can only be opened from the other side. | Bu kapı sadece diğer taraftan açılabilir. | Doğru | Doğal |
| instruction-10 | You may leave, but your reward will be forfeited. | Ayrılabilirsiniz ancak ödülünüz kaybedilecektir. | Küçük ifade sorunu | Doğal değil |
| dialogue-01 | I've got your back. | Arkanı kolladım. | Büyük hata | Doğal |
| dialogue-02 | We're not out of the woods yet. | Henüz ormandan çıkmadık. | Bağlamla belirsiz | Doğal |
| dialogue-03 | That was a close call. | Bu yakın bir karardı. | Büyük hata | Doğal değil |
| dialogue-04 | Don't push your luck. | Şansınızı zorlamayın. | Doğru | Doğal |
| dialogue-05 | You sold us out for a handful of coins. | Bir avuç kuruşa bizi sattın. | Doğru | Doğal |
| dialogue-06 | I should have listened to you. | Seni dinlemeliydim. | Doğru | Doğal |
| dialogue-07 | If I hadn't missed, he'd still be alive. | Kaçırmasaydım hâlâ hayatta olacaktı. | Doğru | Doğal |
| dialogue-08 | You didn't think I'd leave you here, did you? | Seni burada bırakacağımı düşünmedin değil mi? | Doğru | Doğal |
| dialogue-09 | So much for a quiet entrance. | Sessiz bir giriş için bu kadar. | Büyük hata | Doğal değil |
| dialogue-10 | I'd rather lose the crown than lose you. | Seni kaybetmektense tacı kaybetmeyi tercih ederim. | Doğru | Doğal |
| context-01 | Charge! | Şarj! | Bağlamla belirsiz | Doğal değil |
| context-02 | Watch the bat. | Sopayı izle. | Bağlamla belirsiz | Doğal |
| context-03 | The party is waiting. | Parti bekliyor. | Bağlamla belirsiz | Doğal |
| context-04 | She left the bank. | Bankadan ayrıldı. | Bağlamla belirsiz | Doğal |
| context-05 | Take the right path. | Doğru yolu seçin. | Bağlamla belirsiz | Doğal |
| context-06 | That's a fine blade. | Bu çok iyi bir bıçak. | Bağlamla belirsiz | Doğal |
| context-07 | The seal is broken. | Mühür kırılmış. | Bağlamla belirsiz | Doğal |
| context-08 | Drop it. | Bırak onu. | Bağlamla belirsiz | Doğal |
| context-09 | Nice shot. | Güzel atış. | Bağlamla belirsiz | Doğal |
| context-10 | He is down. | O aşağıda. | Bağlamla belirsiz | Doğal |
| item-01 | Armor penetration | Zırh nüfuzu | Küçük ifade sorunu | Doğal değil |
| item-02 | Damage over time | Zamanla hasar | Küçük ifade sorunu | Doğal değil |
| item-03 | Cooldown reduction | Bekleme süresinde azalma | Doğru | Doğal |
| item-04 | Bound to your account | Hesabınıza bağlı | Doğru | Doğal |
| item-05 | This item is consumed on use. | Bu madde kullanıldıkça tüketilir. | Büyük hata | Doğal |
| item-06 | Grants immunity to poison, not to bleeding. | Kanamaya değil zehire karşı bağışıklık kazandırır. | Küçük ifade sorunu | Doğal değil |
| item-07 | Revive a fallen ally | Düşmüş bir müttefiki dirilt | Doğru | Doğal |
| item-08 | Life steal applies only to weapon damage. | Can çalma yalnızca silah hasarı için geçerlidir. | Doğru | Doğal |
| item-09 | Equipping this ring disables your shield. | Bu yüzüğü takmak kalkanınızı devre dışı bırakır. | Doğru | Doğal |
| item-10 | The Ashen Crown cannot be sold or dismantled. | Ashen Crown satılamaz veya parçalara ayrılamaz. | Doğru | Doğal |
| format-01 | Welcome back, {player}! | Tekrar hoş geldiniz {player}! | Doğru | Doğal |
| format-02 | Press [E] to interact. | Etkileşim için [E] tuşuna basın. | Doğru | Doğal |
| format-03 | Defeat {count} enemies with {weapon}. | {weapon} ile {count} düşmanı mağlup edin. | Doğru | Doğal |
| format-04 | Cooldown: 10 seconds. Damage: 250. | Bekleme süresi: 10 saniye. Hasar: 250. | Doğru | Doğal |
| format-05 | Health below 25% increases damage by 10%. | Sağlığın %25'in altında olması hasarı %10 artırır. | Doğru | Doğal |
| format-06 | You need 3 keys, not 2. | 2 değil 3 anahtara ihtiyacınız var. | Doğru | Doğal |
| format-07 | Quest complete: {0} / Reward: {1} gold | Görev tamamlandı: {0} / Ödül: {1} altın | Doğru | Doğal |
| format-08 | Hold [Shift] and press [E] twice. | [Shift] tuşunu basılı tutun ve iki kez [E] tuşuna basın. | Doğru | Doğal |
| format-09 | Earn 100 gold; spend no more than 40. | 100 altın kazanın; 40'tan fazla harcamayın. | Doğru | Doğal |
| format-10 | Deliver {item} to {npc} before day 5. | {item}'ı 5. günden önce {npc}'ye teslim edin. | Küçük ifade sorunu | Doğal değil |
