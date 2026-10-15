# S1: ANCS + AMS + Battery (+ S8 Device Information, yakınlık için RSSI)

## Hedef

iPhone'da uygulama olmadan, .NET'ten WinRT ile:

- ANCS: bildirimleri okumak, aksiyon göndermek
- AMS: çalan şarkıyı okumak, medya komutu göndermek
- Battery, Current Time ve Device Information (S8) okumak
- Eşleşme akışını görmek: Windows Ayarlar'dan eşleşme yetiyor mu, `Uncached` keşif, Service Changed
- Yakınlık kilidi için RSSI'nin nereden okunacağını bulmak (DECISIONS #22)

## Yapılanlar

`AncsSpike/`: paketlenmemiş .NET 10 konsol uygulaması (`net10.0-windows10.0.22621.0`).

| Dosya | Görevi |
|---|---|
| `Program.cs` | Komutlar: `run` (varsayılan), `scan`, `pair <id>`; etkileşimli komut döngüsü |
| `IphoneLink.cs` | ANCS ve AMS oturumları; yeniden bağlanınca ve `GattServicesChanged` olayında keşfi ve abonelikleri yeniler |
| `Discovery.cs` | `Uncached` servis keşfi, DIS, CTS, BAS okumaları |
| `AncsSession.cs` | Notification Source, Control Point kuyruğu, Data Source birleştirme, uygulama adı önbelleği, aksiyonlar |
| `AmsSession.cs` | Entity Update kaydı, kesilmiş değerler için Entity Attribute okuma, Remote Command |
| `RssiMonitor.cs` | `Aep.SignalStrength` izleyicileri (LE ve klasik) ve pasif reklam izleyicisi |
| `Pairing.cs` | Uygulama içinden eşleşme denemesi (`scan`, `pair`); **cihazda denenmedi** |
| `Log.cs` | Konsol çıktısı; ham baytlar `captures/*.jsonl` dosyasına (git'e girmez) |

```bash
dotnet run --project spikes/S1-ancs-ams/AncsSpike
```

Komutlar: `p` çal/duraklat, `n` sonraki, `b` önceki, `+`/`-` ses, `x <uid>` olumsuz aksiyon, `y <uid>` olumlu aksiyon, `s` durum, `r` oturumları yeniden başlat, `q` çık.

## Sonuç (15–16 Ekim 2026, iPhone 13 `iPhone14,5`, MediaTek adaptör, Windows 11 26H2)

Aşağıdakiler Buğra'nın iPhone'uyla cihazda denendi; denenmeyenler ayrıca belirtildi.

### Eşleşme

- Phone Link'ten kalan eski LE bağı bozuktu. Bağlantı kuruluyor, **tam 10 saniyede bir kopuyor**, servis keşfi `Unreachable` dönüyordu. iPhone cihazı "Bağlı Değil" gösteriyordu.
- İki taraftan eşleşmeyi kaldırıp **Windows Ayarlar > Bluetooth ve cihazlar > Cihaz ekle** ile yeniden eşleştirince düzeldi. Tek eşleşme hem klasik hem LE kaydı oluşturuyor; ANCS ve AMS ilk keşifte görünüyor. Uygulama içinden eşleşme (`pair`) denenmedi.
- Yeniden eşleşmeden sonraki ilk çalıştırmada abonelik başarılı olduğu halde **hiç ANCS olayı gelmedi**; bir sonraki çalıştırmada geldi. Aradaki farkın iPhone'daki bildirim paylaşma izni olduğunu düşünüyoruz, ama kesinleşmedi.
- Phone Link açıkken bağlantıyla çakışıyor. "Ayarlar > Bluetooth ve cihazlar > Mobil cihazlar" kapatılınca sorun kalmadı. `CrossDeviceService` çalışmaya devam etti, bir çakışma görülmedi.

### Servisler

- `Uncached` keşifte görünenler: Generic Access, Generic Attribute, Device Information, Battery, Current Time, ANCS, AMS, Apple Continuity, Apple Nearby. Bütün karakteristiklerin `ProtectionLevel` değeri `Plain`; şifreleme bağın kendisinden geliyor.
- Service Changed (0x2A05) aboneliği `AccessDenied`: Windows bu karakteristiği kendine ayırıyor. Onun yerine `BluetoothLEDevice.GattServicesChanged` olayı geliyor; iPhone her bağlantıda iki kez gönderiyor.
- Max PDU 527. En uzun Data Source yanıtı 488 bayt, hiç parçalanmadı. Birleştirme kodu yine de gerekli; daha küçük MTU'da parçalanır.

### ANCS

- Notification Source aboneliğinin hemen ardından iPhone'daki tüm bildirimler `PreExisting|Silent` bayraklarıyla geliyor.
- Öznitelik yanıtı (uygulama, başlık, alt başlık, mesaj, boyut, tarih, aksiyon etiketleri) tekil istekte 120–400 ms sürüyor. Açılıştaki toplu istekte kuyruk yüzünden 950 ms'ye çıkıyor.
- Görülen bütün uygulamalarda (WhatsApp, Mail, Outlook, Kısayollar) **sadece olumsuz aksiyon** var (etiketi "Clear"); hiçbirinde olumlu aksiyon yok. Mesajlar (SMS/iMessage) bildirimi bu oturumlarda gelmedi.
- `x <uid>` bildirimi iPhone'dan siliyor (Buğra iPhone'da gördü). Mail ve Outlook için `Removed` olayı aynı milisaniyede geldi; iki WhatsApp bildirimi için konsola `Removed` düşmedi. Geçersiz UID `0xA2` (invalid parameter) döndürüyor.
- **UID'ler her bağlantıda sıfırlanıyor.** Aynı UID, yeniden bağlandıktan sonra başka bir bildirime ait olabiliyor.
- WhatsApp'ın `DisplayName` değeri başında U+200E (soldan sağa işareti) taşıyor. Bülten maillerinin önizlemesi uzun bir yumuşak tire (U+00AD) ve boşluk dizisiyle bitiyor.
- Bağlıyken gelen **canlı** bir bildirimin gecikmesi ölçülmedi; bütün gözlemler `PreExisting` bildirimlerle yapıldı.

### Yeniden bağlanma

- Telefonla başka odaya gidince bağlantı ~10 saniye içinde koptu. Geri gelince `MaintainConnection = true` sayesinde bağlantı **1 saniye içinde kendiliğinden** yeniden kuruldu.
- Yeniden bağlandıktan sonra eski `GattDeviceService` ve `GattCharacteristic` nesneleri geçersiz hale geldi: abonelikler düştü, iPhone'a gelen bildirimler PC'ye ulaşmadı, eski nesneye yazmak `ObjectDisposedException` attı.
- `IphoneLink`, `GattServicesChanged` olayından 4 saniye sonra keşfi ve abonelikleri yeniliyor. Kod yazıldı ve çalışıyor; ama yeniden bağlanma ve ardından gelen bildirim senaryosu **cihazda denenmedi**.

### AMS

- Çal/duraklat, sonraki şarkı ve ses komutları çalıştı. Durum güncellemesi 40–90 ms içinde geliyor.
- Desteklenen komut listesi oyuncunun durumuna göre değişiyor: çalan bir şey yokken sadece ses komutları, YouTube Music çalarken dokuz komut.
- Arka arkaya dört ses komutuna karşılık tek bir `Volume` güncellemesi geldi; iPhone ses güncellemelerini birleştiriyor.
- `PlaybackInfo` biçimi `durum,hız,geçen süre`. 103 baytlık şarkı başlığı MTU 527'de kesilmeden geldi.

### Battery, Current Time, Device Information (S8)

- Battery: okunuyor ve abone olunabiliyor; değişiklik bildirimi geliyor.
- Current Time: okunuyor. PC ile 1–2 saniye fark var.
- Device Information: sadece `Manufacturer Name` ("Apple Inc.") ve `Model Number` ("iPhone14,5") var. **iOS sürümü ve yazılım sürümü yok.**

### RSSI (yakınlık kilidi)

- Bağlı GATT bağlantısının kendi RSSI'si yok. Ama iki yol çalışıyor ve ikisi aynı değeri veriyor:
  - `System.Devices.Aep.SignalStrength` (LE adresi için association endpoint izleyicisi)
  - Pasif reklam izleyicisi: Windows, iPhone'un değişen özel adresini eşleşmiş cihazın adresine çözüyor (`adv-bonded`).
- Ölçülen değerler:

  | Durum | RSSI |
  |---|---|
  | Masada | −50…−55 dBm, arada tek tük −72…−77 |
  | Uzaklaşırken | −78…−92 dBm |
  | Kopmadan hemen önce | Reklamlar kesildi |

- Klasik adresin `SignalStrength` değeri çoğunlukla 0 geldi, kullanışsız.
- `present=False` ile gelen eski değerler önbellekten okunuyor, yok sayılmalı.

## Karar

- **ANCS, AMS, Battery ve Current Time .NET + WinRT ile, paketlenmemiş bir süreçte bile çalışıyor.** Aşama 1 planlandığı gibi yürür.
- **Aşama 1 eşleşmesi Windows Ayarlar üzerinden yapılır.** Fenpo sihirbazı Bluetooth ayarlarını açar, LE kaydının oluşmasını ve ANCS'nin görünmesini bekler. Uygulama içinden eşleşme Aşama 1'de ayrıca denenir; iOS uygulamasının tetiklediği eşleşme S4'ün konusu.
- **S8:** model kimliği uygulamasız okunuyor, iOS sürümü okunmuyor. Model adı için `iPhone14,5 → iPhone 13` eşleme tablosu gerekiyor. iOS sürümü ancak Fenpo kanalından gelebilir.
- **Yakınlık kilidi** reklam RSSI'si ve bağlantı durumuyla yapılır. Başlangıç kuralı: 5 saniyelik hareketli ortalama −80 dBm'in altına iner **veya** bağlantı koparsa, 30 saniye sonra kilitle (tasarımdaki "yaklaşık 3 metre, 30 saniye" ile uyumlu). Eşik ayarda değiştirilebilir olmalı.

## Ürün koduna etkisi

1. `Fenpo.Bluetooth` bağlantı sınıfı `GattServicesChanged` olayında ve her yeniden bağlanmada keşfi ve abonelikleri baştan kurar. Eski GATT nesnelerine hiç dokunmaz.
2. ANCS UID'si sadece o bağlantıda geçerli. Uygulama bildirimi (Windows toast) kimliği bağlantı oturumu + UID'den üretilir; yeniden bağlanınca eski toast'lar `PreExisting` listesiyle eşleştirilir.
3. `PreExisting` bildirimler Windows toast olarak gösterilmez, sadece listeye girer.
4. Metin temizleme: baştaki ve sondaki çift yönlü kontrol karakterleri (U+200E vb.) ve U+00AD dizileri atılır, boşluklar sadeleştirilir.
5. Control Point yazmaları tek kuyrukta sıralı yapılır. Data Source birleştirmesi, istenen öznitelik listesine göre uzunluk alanlarıyla yapılır; MTU'ya güvenilmez.
6. Bildirim aksiyonları ANCS bayraklarından okunur. Olumlu aksiyon yoksa düğme gösterilmez. Tasarımdaki WhatsApp bildiriminde yer alan **"iPhone'da aç" düğmesinin ANCS'de karşılığı yok**; "Kapat" = olumsuz aksiyon.
7. AMS'de desteklenen komut listesi her bildirimde güncellenir; kontroller ona göre etkinleşir. Ses düzeyi göstergesi son gelen değere değil, gönderilen komuta göre de güncellenir.
8. Bozuk bağ belirtisi: bağlantı kuruluyor, keşif `Unreachable` dönüyor ve bu üst üste tekrarlanıyor. Bunu görünce kullanıcıya "Eşleşmeyi kaldırıp yeniden eşleştir" yönlendirmesi gösterilir.
9. Phone Link çalışıyorsa (`PhoneExperienceHost`) kullanıcı uyarılır ve "Mobil cihazlar" ayarına yönlendirilir.
10. Cihaz satırındaki "iOS 27.0" bilgisi uygulamasız okunamaz. Ya kaldırılır ya da Fenpo kanalından gelir; bunun için `Hello`'ya işletim sistemi sürümü ve model alanı eklenmesi S5'teki protokol commit'ine alınır.

## Fixture'lar

`fixtures/` altındakiler gerçek kayıtlardan üretildi. Başlık, mesaj, kişi adı, cihaz adı ve şarkı metinleri uydurma metinlerle değiştirildi; uzunluk alanları buna göre yeniden hesaplandı. Bayt yapısı, öznitelik sırası, tarih biçimi, bayraklar ve uygulama kimlikleri gerçek. Ham kayıtlar `captures/` altında kalıyor ve git'e girmiyor (DECISIONS #24).

| Dosya | İçerik |
|---|---|
| `ancs-preexisting-and-actions.jsonl` | Abonelik sonrası 6 `PreExisting` bildirim, öznitelik ve uygulama adı istekleri/yanıtları, olumsuz aksiyonlar, `Removed` olayları, geçersiz UID yazmaları |
| `ams-commands.jsonl` | Entity Update kaydı, oyuncu/kuyruk/şarkı güncellemeleri, çal/duraklat, sonraki, ses komutları |
| `device-info-battery-time.jsonl` | DIS, CTS ve Battery okumaları |
| `rssi-walk-away.csv` | Masada → uzaklaşma → kopma → geri dönme sırasındaki RSSI değerleri (ms, kaynak, dBm) |

Satır biçimi: `{"ms": <oturum başından ms>, "kind": "notify|read|write", "source": "<karakteristik>", "hex": "<bayt>"}`.

## Açık kalanlar

- Yeniden bağlandıktan sonra bildirimlerin akmaya devam etmesi (`IphoneLink`): cihazda doğrulanmadı.
- Bağlıyken gelen canlı bildirimin uçtan uca gecikmesi (Aşama 1 hedefi: < 1 s): ölçülmedi.
- Yeniden eşleşmeden sonraki ilk çalıştırmada ANCS'nin neden sessiz kaldığı.
- Uygulama içinden eşleşme (`scan` / `pair`).
- Gelen arama (`IncomingCall`) bildiriminin olumlu ve olumsuz aksiyonları; S3 ile birlikte denenecek.
