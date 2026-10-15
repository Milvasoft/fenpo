# Fenpo Mimarisi

## Büyük resim

Fenpo iki cihaz arasında **dört ayrı yol** kullanıyor. Her özellik, çalışabildiği en "uygulamasız" yolu seçer; iPhone uygulaması sadece başka yolu olmayan şeyler için devreye girer.

```
                         ┌──────────────────────── iPhone ────────────────────────┐
                         │                                                         │
                         │  iOS'un kendi servisleri          Fenpo iOS uygulaması   │
                         │  (uygulama gerekmez)              + uzantıları           │
                         │  ANCS  AMS  BAS  CTS              BLE merkez (arka plan) │
                         │  MAP   PBAP HFP                   TLS istemcisi          │
                         └───┬──────────┬────────────────────────┬──────────┬───────┘
                             │          │                        │          │
                 ① BLE GATT  │  ② Klasik│ Bluetooth   ③ Fenpo BLE │  ④ Wi‑Fi │ / USB
                 (PC istemci)│  RFCOMM/ │ OBEX, HFP     kanalı    │  TLS 1.3 │
                             │          │             (PC sunucu) │          │
                         ┌───┴──────────┴────────────────────────┴──────────┴───────┐
                         │                    Fenpo for Windows                      │
                         │  Fenpo.Bluetooth   Fenpo.Transport   Fenpo.Core           │
                         │                 Fenpo.App (WinUI 3, tepsi, Ada)          │
                         └───────────────────────────────────────────────────────────┘
```

| Yol | Kim kurar | Ne taşır | Uygulama gerekir mi |
|---|---|---|---|
| ① BLE GATT, Apple servisleri | PC istemci, iPhone sunucu | Bildirim (ANCS), medya (AMS), pil (BAS), saat (CTS) | Hayır |
| ② Klasik Bluetooth | PC istemci | Mesaj (MAP), rehber ve geçmiş (PBAP), arama sesi (HFP) | Hayır |
| ③ Fenpo BLE kanalı | PC çevre birim (`GattServiceProvider`), iPhone merkez | Uyandırma, küçük komutlar, kısa pano metni, yakınlık, Wi‑Fi adresi | Evet |
| ④ Fenpo veri kanalı | iPhone istemci, PC sunucu | Dosya, fotoğraf, büyük pano, video, dosya sistemi | Evet |

**Neden iPhone merkez, PC çevre birim?** iOS arka planda çevre birim olduğunda servis UUID'lerini Windows'un tarayamadığı bir "taşma" alanına koyuyor. iPhone merkez olunca `bluetooth-central` arka plan modu ve durum geri yükleme ile iOS uygulamayı her bildirimde yaklaşık 10 saniyeliğine uyandırıyor. AccessorySetupKit ile kurulursa uygulama zorla kapatıldıktan sonra bile geri getiriliyor (iOS 26+).

## Windows tarafı

### Süreçler

```
Fenpo.exe (MSIX, tam güven, oturum açılınca başlar)
├── Generic Host (Microsoft.Extensions.Hosting)
│   ├── BluetoothHostedService     ① ② ③ bağlantılarını yönetir, yeniden bağlanır
│   ├── TransportHostedService     ④ TLS sunucusu, DNS-SD kaydı, LocalSend
│   ├── NotificationRouter         ANCS → Windows bildirimi / Ada
│   ├── ClipboardService           Windows panosu ↔ Fenpo
│   ├── ProximityService           RSSI → kilitle
│   └── LocalApiServer             named pipe, kabuk ve diğer istemciler için
├── UI (WinUI 3)                   pencere sadece açılınca oluşur, kapanınca tepside kalır
│   ├── MainWindow                 NavigationView, sayfalar
│   ├── TrayFlyout                 H.NotifyIcon.WinUI
│   └── IslandWindow               her zaman üstte, şeffaf, ekranın üst ortası
└── Veri: %LOCALAPPDATA%\Packages\…\LocalState\fenpo.db (SQLite)

Fenpo.VirtualCamera.dll            Native AOT COM medya kaynağı, Frame Server içinde çalışır
                                   Fenpo.exe ile paylaşımlı bellek / paylaşımlı doku üzerinden konuşur
Fenpo.CredentialProvider.dll       C++ COM, LogonUI içinde (Aşama 5, ayrı kurulum)
fenpo-mirror.exe                   isteğe bağlı UxPlay derlemesi, GPLv3, ayrı indirme
```

Tek süreç tercihinin sebebi: Bluetooth bağlantıları uygulama kimliğine (MSIX paketi) bağlı ve `PhoneLineTransportDevice`, uygulama bildirimleri, paylaşım hedefi gibi API'ler paket kimliği istiyor. Ayrı bir servis süreci bunları zorlaştırır. UI çökerse diye, UI kodu servislerden katı bir arayüzle ayrılmış durumda; ileride gerekirse ayrılabilir.

### Projeler

```
windows/
├── Fenpo.slnx
├── src/
│   ├── Fenpo.App/                 WinUI 3, tek proje MSIX paketleme, view'lar ve view model'ler
│   ├── Fenpo.Core/                alan modeli, servis arayüzleri, eşleşme, ayarlar, SQLite
│   ├── Fenpo.Bluetooth/           ANCS, AMS, BAS, CTS istemcileri; GATT sunucusu; OBEX, MAP, PBAP; HFP
│   ├── Fenpo.Transport/           TLS sunucusu, çerçeveleme, DNS-SD, LocalSend, usbmux
│   ├── Fenpo.Protocol/            .proto'dan üretilen C# tipleri
│   ├── Fenpo.Sdk/                 named pipe istemcisi (NuGet paketi olabilir)
│   └── Fenpo.VirtualCamera/       Native AOT COM sunucusu
├── native/
│   └── Fenpo.CredentialProvider/  C++ (Aşama 5)
└── tests/
    ├── Fenpo.Core.Tests/
    ├── Fenpo.Bluetooth.Tests/     ANCS/MAP ayrıştırıcıları için kayıtlı bayt dizileriyle
    └── Fenpo.Transport.Tests/
```

Bağımlılık yönü: `App → Core ← Bluetooth, Transport`. Core hiçbir WinRT tipine bağlı değil, böylece ayrıştırıcılar ve iş mantığı Windows'suz test edilebiliyor.

### Bildirim akışı (örnek)

```
iPhone ANCS Notification Source  ─►  AncsClient.NotificationAdded(uid, kategori, bayraklar)
   ─►  Control Point'e GetNotificationAttributes (sıralı kuyruk)
   ─►  Data Source parçalarını birleştir  ─►  AncsNotification
   ─►  AppIdentityResolver (DisplayName + iTunes Lookup ikon, SQLite önbellek)
   ─►  NotificationRouter
         ├─ kategori IncomingCall ─► IslandWindow (Yanıtla = ANCS Positive / HFP, Reddet = Negative)
         ├─ uygulama Mesajlar     ─► AppNotification + yanıt kutusu (MAP ile gönderilir)
         └─ diğer                 ─► AppNotification (Kapat = ANCS Negative)
```

## iOS tarafı

### Hedefler (targets)

| Hedef | Görevi |
|---|---|
| `Fenpo` (uygulama) | Eşleşme, ana ekran, ayarlar; BLE merkez; TLS istemcisi; App Intent'ler |
| `FenpoShare` | Paylaşım uzantısı: dosya, fotoğraf, bağlantı → PC |
| `FenpoWidgets` | Widget'lar, Canlı Etkinlikler, Denetim Merkezi düğmeleri (`ControlWidget`) |
| `FenpoFiles` | File Provider: PC klasörleri Dosyalar uygulamasında |
| `FenpoBroadcast` | ReplayKit yayın uzantısı: ekran yansıtma (Aşama 4) |
| `FenpoFocus` | Odak filtresi uzantısı (Aşama 4) |
| `FenpoKit` (Swift paketi) | Ortak kod: protokol, kanal istemcisi, anahtar zinciri, modeller. Tüm hedefler kullanır |

Uygulama ile uzantılar App Group (`group.com.fenpo`) ve paylaşılan anahtar zinciri üzerinden eşleşme bilgisini ve sertifikaları paylaşıyor.

### Pano gerçeği

- **iPhone → PC:** iOS arka planda panoyu okutmuyor (Apple DTS'nin açık açıklaması). Çözüm App Intent: "Panoyu PC'ye gönder". Fenpo bunu hazır bir Kısayol olarak sunuyor; kullanıcı arkaya iki dokunmaya, Eylem Düğmesi'ne veya Denetim Merkezi'ne bağlıyor. Uygulama ön plandayken `UIPasteControl` sormadan yapıştırıyor.
- **PC → iPhone:** PC panoya bir şey koyunca ③ kanalı uygulamayı uyandırıyor. Arka planda `UIPasteboard`'a yazmak mümkünse (Spike S6) doğrudan yazılır; değilse son içerik saklanır ve "PC'den yapıştır" intent'i, widget'ı ve uygulama açılışı onu panoya koyar.

## Android tarafı (taslak)

Android uygulaması aynı protokolü konuşan üçüncü istemci. Ayrıntılar Android oturumunun ilk aşamasında `docs/ANDROID.md` içinde netleşecek; bilinen çerçeve şu:

| Yol | Android'de |
|---|---|
| ① Apple BLE servisleri | Yok. Bildirimler ve medya, Android uygulamasından Fenpo kanalıyla gelir (`CAPABILITY_NOTIFICATION_MIRROR`) |
| ② Klasik Bluetooth | HFP ve PBAP standart; MAP üreticiye göre değişiyor. Windows'taki aynı kod yeniden kullanılır, yoksa SMS Fenpo kanalıyla gelir (`CAPABILITY_MESSAGES`) |
| ③ Fenpo BLE kanalı | Aynı roller: telefon merkez, PC çevre birim. Android'de arka plan için ön plan servisi ve `CompanionDeviceManager` |
| ④ Fenpo veri kanalı | Aynı |

Android'de iOS'tan farklı olarak bildirimlere yazılı yanıt (`RemoteInput`), uygulama arka plandayken dosya alma ve ekran yansıtmada telefonu PC'den tam kontrol (erişilebilirlik servisi) mümkün.

## Protokol

Tek kaynak: `protocol/fenpo/v1/*.proto`. C# (`Grpc.Tools`, sadece mesaj üretimi) ve Swift (`swift-protobuf`) tipleri buradan üretilir. gRPC kullanılmıyor; kendi çerçevelememiz var.

### Mesajlar

Taslak v1 dosyaları hazır: `protocol/fenpo/v1/` (ayrıntı `protocol/README.md`).

| Dosya | İçerik |
|---|---|
| `common.proto` | `DevicePlatform` (WINDOWS, IOS, ANDROID), `Capability` |
| `hello.proto` | `Hello`, `Ping`, `Pong`, `Error` |
| `pairing.proto` | QR biçimi, `PairingRequest`, `PairingResult` (BLE kanal anahtarı ve servis UUID'si dahil) |
| `content.proto` | `ClipboardItem`, `ClipboardRequest`, `OpenUrl` |
| `transfer.proto` | `TransferOffer`, `TransferAccept`, `TransferStreamHeader`, `TransferProgress`, `TransferCancel` |
| `device.proto` | `DeviceState`, `RingDevice`, `LockPc` |
| `envelope.proto` | `Envelope` ve özellik gruplarına ayrılmış alan numarası aralıkları |

**El sıkışma:** Her bağlantının ilk mesajı `Hello` (eşleşme bağlantısında `PairingRequest`). Cihaz türünü ve yetenek listesini taşır; iki taraf sadece ikisinin de desteklediği özellikleri açar. Böylece Android veya başka bir istemci, Windows tarafında özel durum açmadan eklenebilir.

Windows'ta bildirim, mesaj ve medya servisleri kaynaktan bağımsız arayüzler üzerinden beslenir (`INotificationSource`, `IMessageSource`, `IMediaSource`). iPhone için kaynak ANCS, MAP ve AMS; Android kendi kaynağını Fenpo kanalı üzerinden ekler.

**Alan aralıkları (`Envelope.body`):** 10–19 oturum ve eşleşme, 20–29 içerik, 30–39 dosya aktarımı, 40–49 cihaz kontrolü, 50–59 cihaz durumu. Sonraki aşamalar için ayrılmış: 60–69 kamera, 70–79 PC klasörleri, 80–89 ekran yansıtma, 90–99 bildirim aynalama (Android), 100–109 mesajlar (Android).

### Çerçeveleme

- **④ TLS kanalı:** `[uint32 uzunluk, big-endian][Envelope bayt]`. Kontrol mesajları tek uzun ömürlü bağlantıda. Dosya gövdeleri ve video ayrı TLS bağlantılarında akar; kontrol kanalı tıkanmaz.
- **③ BLE kanalı:** İki karakteristik: `rx` (iPhone yazar, yanıtsız yazma) ve `tx` (PC bildirir). MTU'ya göre parçalama: `[1 bayt bayrak: ilk/son][2 bayt sıra][parça]`. Sadece 4 KB'tan küçük mesajlar; büyükler için ③ "Wi‑Fi'ye geç" sinyali verir.

### Eşleşme ve güvenlik

1. PC ilk açılışta bir ECDSA P-256 anahtarı ve kendinden imzalı bir sertifika üretir (Windows'ta DPAPI ile korunur). P-256 seçildi çünkü hem Windows Schannel hem Apple Network.framework hem Android TLS yığını bunu sorunsuz destekliyor; Ed25519 sertifikaları TLS'te her yerde desteklenmiyor.
2. PC ekranında QR: `fenpo://pair?v=1&id=<pcId>&n=<ad>&a=<ip:port,…>&fp=<sha256 sertifika parmak izi>&t=<tek kullanımlık 128 bit token>`. Yanında 6 haneli yedek kod.
3. Telefon TLS ile bağlanır, sunucu sertifikasını `fp` ile doğrular, kendi sertifikasıyla `PairingRequest` gönderir (`t` token'ı içinde). PC token'ı tüketir, iPhone sertifikasını sabitler. Bundan sonra her bağlantı karşılıklı sertifika sabitlemeli TLS 1.3.
4. iPhone AccessorySetupKit ile PC'nin Fenpo BLE servisini seçer, bağlanır ve şifreleme isteyen bir karakteristiğe erişir. iOS eşleşme penceresi çıkar, bağ kurulur. PC aynı bağ üzerinden ANCS'yi bulur (önbelleksiz keşif) ve iOS "Bildirimleri paylaş" diye sorar.
5. Klasik Bluetooth (MAP, PBAP, HFP) için Windows eşleşmesi gerekir. Sihirbaz kullanıcıyı Windows Bluetooth eşleşmesine ve iPhone'daki "Bildirimleri göster" ve "Kişileri eşzamanla" anahtarlarına yönlendirir.
6. ③ kanalındaki mesajlar ayrıca, PC'nin eşleşmede ürettiği ve `PairingResult.ble_channel_key` ile TLS üzerinden ilettiği anahtarla AES-GCM ile şifrelenir. BLE bağı tek başına güven kaynağı sayılmaz.

### Keşif

- DNS-SD: PC `_fenpo._tcp` kaydeder (WinRT `DnssdServiceInstance`). TXT: `id`, `v`.
- ③ kanalı bağlıysa PC güncel IP'lerini zaten oradan yollar; iPhone arka planda Bonjour taraması yapamadığı için asıl yol budur.
- USB: Apple Devices uygulaması kuruluysa PC, usbmuxd üzerinden iPhone'daki Fenpo'nun dinlediği porta bağlanır (kamera için düşük gecikme). Spike gerekiyor.

## Yerel API (kabuk ve diğerleri için)

`\\.\pipe\fenpo` üzerinde satır sonu ile ayrılmış JSON-RPC 2.0. Sadece aynı kullanıcının süreçleri bağlanabilir (pipe ACL).

```
fenpo.status()                        → { connected, deviceName, battery, links[] }
fenpo.media.state() / play / pause / next
fenpo.notifications.recent(limit)
fenpo.clipboard.sendToPhone(text)
fenpo.ring()
olaylar: status.changed, notification.added, media.changed, call.incoming
```

`Fenpo.Sdk` bunu tipli bir C# istemcisi olarak sarmalar; kabuk bar'ı pil, şu an çalıyor ve bildirim sayısını buradan çeker.
