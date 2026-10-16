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

Tek kaynak: `protocol/fenpo/v1/*.proto`. C# (`Grpc.Tools`, sadece mesaj üretimi) ve Swift (`swift-protobuf`, `ios/scripts/generate-proto.sh` ile) tipleri buradan üretilir. gRPC kullanılmıyor; kendi çerçevelememiz var.

### Mesajlar

Taslak v1 dosyaları hazır: `protocol/fenpo/v1/` (ayrıntı `protocol/README.md`).

| Dosya | İçerik |
|---|---|
| `common.proto` | `DevicePlatform` (WINDOWS, IOS, ANDROID), `Capability` |
| `hello.proto` | `Hello` (işletim sistemi sürümü ve model dahil), `Ping`, `Pong`, `Error` |
| `pairing.proto` | QR biçimi, `PairingRequest`, `PairingResult` (BLE kanal anahtarı dahil) |
| `content.proto` | `ClipboardItem` (hassas işaretiyle), `ClipboardRequest`, `OpenUrl` |
| `transfer.proto` | `TransferOffer`, `TransferAccept`, `TransferStreamHeader`, `TransferProgress`, `TransferCancel` |
| `device.proto` | `DeviceState` (Android'in ölçtüğü BLE RSSI dahil), `PcStatus`, `RingDevice`, `LockPc` |
| `media.proto` | `MediaState`, `MediaCommand`: Android'in ve PC'nin şu an çalanı ve kontrolü |
| `messages.proto` | MAP'i olmayan Android telefonlarda SMS: konuşmalar, mesajlar, gönderme |
| `pc_input.proto` | Telefonun PC'ye dokunmatik yüzey, klavye ve sunum kumandası olması: `PcInput` |
| `video.proto` | Görüntü ve ses bağlantıları: biçimler, PC'nin çözebildikleri (`AcceptedFormats`), `MediaStreamOpen`, `StreamPacket`, `RequestKeyFrame` |
| `camera.proto` | Telefonun web kamerası olması: `CameraStart`, `CameraStop`, `CameraStatus`, `CameraSettingsChange` |
| `screen.proto` | Android ekranının PC'de görünmesi ve PC'den kontrolü: `ScreenStart`, `ScreenStop`, `ScreenStatus`, `ScreenInput` |
| `notification.proto` | Android bildirim aynalama: `PhoneNotification` (kategori ve sıra numarasıyla), `NotificationRemoved`, `NotificationSnapshotDone`, `DismissNotification`, `RunNotificationAction`, `AppIconRequest`, `AppIcon` |
| `pc_notification.proto` | PC'nin seçilen uygulamalarının bildirimleri telefonda: `PcNotification`, `PcNotificationRemoved` |
| `files.proto` | PC'nin paylaşılan klasörleri telefonda (iPhone Dosyalar, Android dosya seçici): `FolderListRequest`, `FolderListing`, `FileFetch`, `FileThumbnailRequest`, `FileThumbnail`, `FileChange`, `FileChangeResult`, `FolderChanged`. İçerik normal dosya aktarımıyla; yükleme `TransferOffer.destination_path` ile |
| `photos.proto` | Telefonun fotoğraf kitaplığına PC'den göz atma: `PhotoListRequest`, `PhotoList`, `PhotoThumbnailRequest`, `PhotoThumbnail`, `PhotoFetch` |
| `hotspot.proto` | Telefonun hotspot'una PC'den tek tıkla bağlanma: `HotspotInfo` (kullanıcının telefona bir kez yazdığı ad ve şifre), `HotspotRequest`, `HotspotStatus` |
| `envelope.proto` | `Envelope` ve özellik gruplarına ayrılmış alan numarası aralıkları |

**El sıkışma:** Her bağlantının ilk mesajı `Hello` (eşleşme bağlantısında `PairingRequest`). Cihaz türünü, işletim sistemi sürümünü, modeli ve yetenek listesini taşır; iki taraf sadece ikisinin de desteklediği özellikleri açar. Böylece Android veya başka bir istemci, Windows tarafında özel durum açmadan eklenebilir. iPhone'daki cihaz satırının iOS sürümü `Hello.os_version`'dan gelir (DECISIONS #30).

**PC durumu:** Telefon, PC'nin ANCS'ye abone olup olmadığını ve MAP'e bağlanıp bağlanmadığını kendisi göremez. PC bunları `PcStatus` ile `Hello`'dan hemen sonra ve her değişiklikte bildirir; iPhone'un eşleşme ekranındaki "Bildirim paylaşımı" ve "Mesajlar" adımları buna bakar.

Windows'ta bildirim, mesaj ve medya servisleri kaynaktan bağımsız arayüzler üzerinden beslenir (`INotificationSource`, `IMessageSource`, `IMediaSource`). iPhone için kaynak ANCS, MAP ve AMS; Android kendi kaynağını Fenpo kanalı üzerinden ekler.

**Alan aralıkları (`Envelope.body`):** 10–19 oturum ve eşleşme, 20–29 içerik, 30–39 dosya aktarımı, 40–49 cihaz kontrolü (medya komutu ve PC girdisi dahil), 50–59 cihaz, PC ve medya durumu, 90–96 bildirim aynalama (Android), 100–106 mesajlar (Android, MAP'i olmayan telefonlar). 60–65 kamera, görüntü bağlantısının ilk çerçevesi ve anahtar kare isteği, 80–83 ekran yansıtma (Android). 70–77 PC'nin paylaşılan klasörleri, 97–98 PC bildirimleri telefonda, 110–114 fotoğraf kitaplığı, 120–122 hotspot. 130–139 telefonla kilit açmaya ayrılmıştı, kaldırıldı (DECISIONS #40); yeniden kullanılmaz. Sonraki aşamalar için ayrılmış: 66–69 kamera eklemeleri, 78–79 klasör eklemeleri, 84–89 yansıtma eklemeleri, 99 bildirim eklemeleri, 107–109 mesaj eklemeleri, 115–119 fotoğraf eklemeleri, 123–129 hotspot eklemeleri.

**iPhone ekran yansıtma:** Yayını (ReplayKit) sadece iPhone'un kullanıcısı başlatabilir. iPhone `ScreenStart`'a `WAITING_FOR_CONSENT` ile cevap verip nasıl başlatılacağını gösterir; yayın uzantısı görüntü bağlantısını açar ve `ScreenStart` olmadan `STREAMING` bildirir. PC'den kontrol yok.

**Android bildirimleri:** Android telefon bildirimlerini `NotificationListenerService` ile okur ve Fenpo kanalından gönderir (`CAPABILITY_NOTIFICATION_MIRROR`): `Hello`'dan sonra o kanalda mevcut bildirimlerin hepsi (`pre_existing`, sonunda `NotificationSnapshotDone`), sonra her yeni, güncellenen ve kalkan bildirim. İki kanal arasında sırayı `seq` korur; gelen arama `INCOMING_CALL` kategorisiyle Ada'ya gider. PC kapatma ve eylem (yazılı yanıt dahil) isteğini geri yollar; uygulama simgesini sadece önbelleğinde yoksa ister. Windows'ta bu kaynak `INotificationSource` arayüzüne ANCS'nin yanına bağlanır. Ayrıntı `protocol/README.md`.

### Çerçeveleme

- **④ TLS kanalı:** `[uint32 uzunluk, big-endian][Envelope bayt]`. Kontrol mesajları tek uzun ömürlü bağlantıda. Dosya gövdeleri ve video ayrı TLS bağlantılarında akar; kontrol kanalı tıkanmaz. Dosya bağlantısı `[uint32 uzunluk][TransferStreamHeader]` ile başlar, ardından dosyaların ham baytları teklif sırasıyla gelir. Veri bağlantısını iki yönde de telefon açar, PC'nin kontrol sunucusu portuna; ilk çerçeve `Hello`/`PairingRequest` ise kontrol, `TransferStreamHeader` ise veri bağlantısıdır. PC'den telefona aktarımda başlığı telefon yazar, baytları PC. Alıcı, teklifteki boyutlara göre okur ve akışın bitmesini beklemeden sonucu bildirir; telefon bağlantıyı sonucu aldıktan sonra kapatır, çünkü okunmamış bayt varken kapanan TLS soketi TCP reset gönderir ve yoldaki son baytlar kaybolabilir. Telefonda dinleyen soket yok (Android yerel ağ izni, NAT, iOS arka plan sınırları).
- **③ BLE kanalı:** Sabit bir Fenpo servisi ve iki karakteristik: `rx` (iPhone yazar, yanıtsız yazma) ve `tx` (PC bildirir), ikisi de şifreli bağlantı ister. UUID'ler her PC için aynı (`protocol/README.md`), çünkü AccessorySetupKit servis UUID'sinin iPhone uygulamasının Info.plist'inde önceden bildirilmesini istiyor. Yakında birden fazla Fenpo PC'si varsa ASK seçicisinde hangisinin hangisi olduğu S4'te netleşecek. Her Envelope önce AES-GCM ile mühürlenir (`nonce (12) || şifreli metin || etiket (16)`), sonra MTU'ya göre parçalanır: `[1 bayt bayrak: bit0 ilk, bit1 son][2 bayt sıra, big-endian, her mesajda 0'dan][parça]`. Mühürlü mesaj en fazla 4096 bayt; büyükler için ③ "Wi‑Fi'ye geç" sinyali verir. Telefon `tx` bildirimlerine abone olur olmaz `Hello` gönderir, PC `Hello` ve `PcStatus` ile cevap verir. Ayrıntı `protocol/README.md`.

### Eşleşme ve güvenlik

1. PC ilk açılışta bir ECDSA P-256 anahtarı ve kendinden imzalı bir sertifika üretir (Windows'ta DPAPI ile korunur). P-256 seçildi çünkü hem Windows Schannel hem Apple Network.framework hem Android TLS yığını bunu sorunsuz destekliyor; Ed25519 sertifikaları TLS'te her yerde desteklenmiyor.
2. PC ekranında QR: `fenpo://pair?v=1&id=<pcId>&n=<ad>&a=<ip:port,…>&fp=<sha256 sertifika parmak izi>&t=<tek kullanımlık 128 bit token>`. Sayısal yedek kod yok: QR olmadan telefonun elinde doğrulayacağı parmak izi olmaz ve kısa bir kod araya giren saldırgana karşı koruma sağlamaz. Kamera QR'ı okuyamazsa PC QR'ı yeniden gösterir (S5).
3. Telefon TLS ile bağlanır, sunucu sertifikasını `fp` ile doğrular, kendi sertifikasıyla `PairingRequest` gönderir (`t` token'ı içinde). PC token'ı tüketir, iPhone'un parmak izini TLS el sıkışmasından hesaplayıp sabitler. Kabulde aynı bağlantı normal oturuma döner (telefon `Hello`, PC `Hello`); retde PC bağlantıyı kapatır. Bundan sonra her bağlantı karşılıklı sertifika sabitlemeli TLS 1.3.
4. iPhone AccessorySetupKit ile PC'nin Fenpo BLE servisini seçer, bağlanır ve şifreleme isteyen bir karakteristiğe erişir. iOS eşleşme penceresi çıkar, bağ kurulur. PC aynı bağ üzerinden ANCS'yi bulur (önbelleksiz keşif) ve iOS "Bildirimleri paylaş" diye sorar.
5. Klasik Bluetooth (MAP, PBAP, HFP) için Windows eşleşmesi gerekir. Sihirbaz kullanıcıyı Windows Bluetooth eşleşmesine ve iPhone'daki "Bildirimleri göster" ve "Kişileri eşzamanla" anahtarlarına yönlendirir.
6. ③ kanalındaki mesajlar ayrıca, PC'nin eşleşmede ürettiği ve `PairingResult.ble_channel_key` ile TLS üzerinden ilettiği anahtarla AES-256-GCM ile şifrelenir. Nonce `[1 bayt gönderen: 0x01 telefon, 0x02 PC][3 bayt sıfır][8 bayt sayaç]`, AAD `fenpo-ble-v1`. Her gönderen sayacını kalıcı ve artan tutar; alıcı son gördüğünden küçük veya eşit sayacı reddeder. BLE bağı tek başına güven kaynağı sayılmaz.

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
