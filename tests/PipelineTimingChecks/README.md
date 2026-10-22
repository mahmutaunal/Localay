# P0: işlem bazında ölçüm ve tekrar oynatma

Bu araçlar ölçümün doğruluğunu sınar. Windows OCR, ekran yakalama, anlam kalitesi veya fiziksel ekran sunumu için performans sonucu üretmiş sayılmaz.

## Canlı tanıları açma

Normal kullanımda ayrıntılı tanılar kapalıdır. Başlatılmadan önce Windows PowerShell'de:

```powershell
$env:LOCALAY_PIPELINE_DIAGNOSTICS = '1'
& 'C:\Localay\Localay.exe' # Gerçek uygulama yoluyla değiştirin.
```

Bir oturumu duraklatınca veya durdurunca `%LOCALAPPDATA%\AlpWare Studio\Localay\Logs\pipeline-*.json` raporu arka planda yazılır. Tanıları kapatmak için ortam değişkenini kaldırıp uygulamayı yeniden başlatın:

```powershell
Remove-Item Env:LOCALAY_PIPELINE_DIAGNOSTICS
```

Rapor yalnızca kimlik, sayı, geometri ve süreler içerir. Kaynak/çeviri metni, istem, API anahtarı, URL ve ekran görüntüsü içermez. Mevcut içerik günlüğü bu rapordan ayrıdır.

## Ölçüm sınırları

- Başlangıç, uygulamanın yakalama girişidir. Metnin ekranda ilk ortaya çıkması ve yakalamaya kadar bekleme henüz dışarıdan ölçülmez (`SourceOnset: not_measured`). Bu fark gerçek kullanıcı gecikmesinin parçasıdır; sonraki aşamalarda ayrıca doğrulanmalıdır.
- Bitiş, çevirili öğelerin görünür WPF yüzeyine mantıksal uygulanmasıdır. GPU/compositor/ekran sunumu ve anlamsal doğruluk ölçülmez. `UiApplied` bunların kanıtı değildir.
- Aynı sahnenin sabitlenmesi ve yeniden OCR kontrolü başlangıcı ileri taşımaz. Yakalama başlatma süresi OCR tamamlanma süresi olarak yazılmaz.
- Provider süreleri, iptali görmezden gelen iş gerçekten bitene kadar sürer. Rapor alınırken henüz bitmemiş aralıklar `InFlightWork` ile sayılır; tamamlanmış sıfır maliyetli iş gibi gösterilmez. İptal edilen iş başarılı sonuca dönüşemez.
- `CaptureToUi` yalnızca tam mantıksal UI uygulaması bulunan sahnelerin dağılımıdır. Başarısız, eksik, gösterilmeyen ve bekleyen işler ayrıca raporlanır. Kapsama bu işleri de içerir; yalnızca hız istatistiğine bakılarak başarı ilan edilmez.
- `VisibleBlockCoverage` mantıksal UI öğesi sayımıdır; gerçek ekran piksellerinde OCR doğruluğu/örtüşme değerlendirmesi değildir. ChatBox sayımı farklı blokların güncelleme bildirimlerini izler; ekran okuma/semantik doğrulama yapmaz.
- Rapor son 256 işlemin kayan penceresini içerir. Daha eski işlemler `EvictedSamples` ile açıkça sayılır. İstatistikler bu pencereye aittir. Bir işte en fazla 96 çalışma aralığı ve 1024 ChatBox kimliği tutulur; eksilen kayıtlar `DroppedEvents` içinde sayılır.
- `Work` alanındaki süreler örtüşebilir; toplanarak uçtan uca süre elde edilmez. OCR, UI dispatcher beklemesi, sahne işleme, sağlayıcı, ön çeviri bekleme, arka plan silme ve UI uygulaması ayrı izlenir.
- `ProcessResources`, oturum başlangıcından rapor örneğine kadar tüm uygulama sürecinin CPU zamanı ve tahsisleri ile anlık çalışma kümesidir. Bunlar yalnızca OCR'a atfedilemez; GPU kullanım sayacı veya CPU yüzdesi değildir.
- Hedef mümkünse **100 ms altı**, kaliteyi koruyarak düşük kaynak maliyetidir. `AtOrAbove100Ms` sınırı dahil sayar. Eski 500 ms sınırı yalnızca yavaş kuyruk olaylarını ayırt eden ek sayımdır.

## Kontroller

```powershell
dotnet run --project tests/PipelineTimingChecks/PipelineTimingChecks.csproj -c Release
$env:LOCALAY_PIPELINE_DIAGNOSTICS = '1'
dotnet run --project tests/PipelineTimingChecks/PipelineTimingChecks.csproj -c Release
Remove-Item Env:LOCALAY_PIPELINE_DIAGNOSTICS
```

Her iki mod yerelde sınanabilir. Etkin modda asenkron işlerin ölçüm kapsamı ve üretim sağlayıcı sarmalayıcısı da çalıştırılır. `Verify-Project.ps1` varsayılan moddaki kontrolleri içerir. Kullanıcı tercihi gereği installer workflow'una ek bir ölçüm testi adımı eklenmez.

12 sabit aşama senaryosu: kısa altyazı, önbellekten yerleşim, sabitlenme, yavaş sağlayıcı, eksik yanıt, hata, iptal, eski sahne, görünmeyen çıktı, boş sahne, yalnız OCR ve UI işaretleyicisi olmayan sahte başarı. Bunlar kayıtlı aşama olaylarının tekrar oynatımıdır; piksel görüntülerini gerçek OCR'a veren bir sürücü değildir.

Sentetik raporu üretmek ve aynı olaylardan istatistikleri yeniden hesaplamak için:

```powershell
dotnet run --project tests/PipelineTimingChecks/PipelineTimingChecks.csproj -c Release -- --report artifacts/validation/pipeline-timing/synthetic-replay-report.json
dotnet run --project tests/PipelineTimingChecks/PipelineTimingChecks.csproj -c Release -- --replay artifacts/validation/pipeline-timing/synthetic-replay-report.json
```

`--replay` canlı bir `pipeline-*.json` raporunu da okuyabilir. Sentetik kayıt, yeniden okunduğunda canlı performans ölçümü olarak etiketlenmez.

İsteğe bağlı `--benchmark`, yalnızca tanı kaydı oluşturma maliyetini ölçer. Sonuçları gerçek çeviri gecikmesi diye kullanmayın. Kapalı mod iz oluşturmaz ve rapor klasörüne erişmez. Ölçüm sonuçları sonraki yakalama/çeviri optimizasyonlarını yönlendirecek; bu adım bu algoritmaların çalışma sıklığını değiştirmez.
