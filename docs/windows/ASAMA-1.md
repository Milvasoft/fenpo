# Windows Aşama 1 uygulama planı (taslak, Buğra'nın onayını bekliyor)

> Kaynaklar: [PLAN.md](../PLAN.md) Aşama 1, [ARCHITECTURE.md](../ARCHITECTURE.md), DECISIONS #28–35, S1–S7 spike README'leri. Onaydan önce ürün kodu yazılmaz.

## Kapsam

iPhone'da Fenpo uygulaması olmadan çalışan her şey:

| Özellik | Kaynak | Spike |
|---|---|---|
| Tepsi ikonu ve tepsi paneli | H.NotifyIcon.WinUI | — |
| Ana pencere: iPhone'um, Bildirimler, Ayarlar | WinUI 3 NavigationView | — |
| Görünüm (Sistem/Açık/Koyu) ve Dil (Sistem/Türkçe/English) | `RequestedTheme`, `PrimaryLanguageOverride` | — |
| Eşleşme sihirbazı (Windows Ayarlar yolu) | DECISIONS #28 | S1 |
| Bildirimler: Windows bildirimi, "Kapat" aksiyonu, uygulama adı ve ikonu | ANCS | S1 |
| Medya: şu an çalıyor kartı, klavye medya tuşları, Windows medya paneli | AMS + `SystemMediaTransportControls` | S1 |
| Pil, saat farkı, model adı, bağlantı durumu | BAS, CTS, DIS | S1, S8 |
| Yakınlık kilidi | Reklam RSSI + bağlantı, `LockWorkStation` (DECISIONS #29) | S1 |
| Yerel API (named pipe, JSON-RPC) | ARCHITECTURE "Yerel API" | — |

Aşama 1'de **olmayanlar**: mesajlar ve aramalar (Aşama 2), Ada, Fenpo BLE ve Wi‑Fi kanalları, pano, dosyalar, kamera. Tasarımdaki bu menü öğeleri ve ana ekrandaki "Hotspot'a bağlan", "Dosya gönder", "Kamera olarak kullan", "Ekranı yansıt", "Telefonu çaldır" düğmeleri gizlenir (bkz. sorular).

## Projeler

```
windows/
├── Fenpo.slnx
├── Directory.Build.props        net10.0-windows10.0.22621.0, Nullable, TreatWarningsAsErrors (Release), analizörler
├── Directory.Packages.props     merkezi paket sürümleri
├── src/
│   ├── Fenpo.App/               WinUI 3, tek proje MSIX; görünümler, view model'ler, Generic Host, tepsi
│   ├── Fenpo.Core/              alan modeli, kaynaktan bağımsız arayüzler, ayarlar, SQLite, metin temizleme, model adı tablosu
│   ├── Fenpo.Bluetooth/         IphoneLink, ANCS/AMS/BAS/CTS/DIS istemcileri, ayrıştırıcılar, yakınlık izleyici
│   └── Fenpo.Sdk/               named pipe istemcisi (kabuk bar'ı için)
└── tests/
    ├── Fenpo.Core.Tests/
    └── Fenpo.Bluetooth.Tests/   Fixtures/ (S1'den anonim baytlar)
```

`Fenpo.Transport`, `Fenpo.Protocol` ve `Fenpo.VirtualCamera` Aşama 3 ve 4'te eklenir.

Bağımlılık yönü `App → Core ← Bluetooth`. Core WinRT'ye bağlı değil. ANCS ve AMS ayrıştırıcıları `Fenpo.Bluetooth` içinde ama WinRT tipi kullanmayan saf sınıflar; testleri cihaz istemiyor.

### Kaynaktan bağımsız arayüzler (Core)

| Arayüz | Aşama 1 kaynağı | Sonra |
|---|---|---|
| `INotificationSource` (eklendi, değişti, kaldırıldı; olumlu/olumsuz aksiyon) | ANCS | Android: Fenpo kanalı (`CAPABILITY_NOTIFICATION_MIRROR`) |
| `IMediaSource` (şu an çalıyor, desteklenen komutlar, komut gönder) | AMS | Android: Fenpo kanalı |
| `IDeviceStatusSource` (pil, model, bağlantılar) | BAS, DIS | Fenpo kanalı `DeviceState` |
| `IProximitySource` (RSSI örnekleri, bağlantı) | Reklam izleyici + `BluetoothLEDevice` | — |
| `ICallSource` | — | Aşama 2: ANCS `IncomingCall`; ileride HFP (DECISIONS #34) |

### Paketler

Windows App SDK 2.5.1, CommunityToolkit.Mvvm, H.NotifyIcon.WinUI, Microsoft.Extensions.Hosting, Serilog (+ Extensions.Hosting, Sinks.File), Microsoft.Data.Sqlite, xUnit v3, NSubstitute. Sürümler eklenirken NuGet'teki en güncel kararlı sürüme sabitlenir.

## Uygulama içi yapı

- `App.xaml.cs` Generic Host'u kurar. Pencere sadece açılınca oluşur; kapatılınca tepside kalır.
- Hosted servisler: `BluetoothHostedService` (IphoneLink yaşam döngüsü, yeniden bağlanma, `GattServicesChanged`'de baştan abonelik: S1 bulgusu), `NotificationRouter`, `MediaBridge` (SMTC), `ProximityService`, `LocalApiServer`.
- `ThemeService`: Sistem/Açık/Koyu. Ana pencere ve tepsi paneli aynı servisi dinler. Sistem seçiliyken `UISettings.ColorValuesChanged` izlenir; başlık çubuğu renkleri de güncellenir.
- `LocalizationService`: `PrimaryLanguageOverride`. Değişince "Şimdi yeniden başlat" bilgi çubuğu çıkar (tasarımdaki gibi). Windows bildirimleri de aynı dili kullanır.
- Veri: `LocalState\fenpo.db` (SQLite). Tablolar: bildirim geçmişi (oturum + UID ile), uygulama adı/ikon önbelleği, ayarlar.
- Log: Serilog, dosyaya, günlük. Sabit yol, sanallaştırma kapalı (klangbruecke'nin MSIX notu).

## Sıra

Her adım kendi başına derlenir, testleri geçer ve iki temada, iki dilde ekran görüntüsüyle doğrulanır.

1. **İskelet:** çözüm, Directory dosyaları, `.github/workflows/windows.yml` (derleme + test), MSIX (geliştirmede `CN=Fenpo Development` sertifikası), Generic Host, tema ve dil servisleri, `Resources.resw` (tr-TR, en-US), boş ana pencere, Ayarlar > Görünüm ve dil.
2. **Bluetooth katmanı:** IphoneLink (keşif, yeniden abonelik, bağlantı olayları), ANCS ve AMS istemcileri ve ayrıştırıcıları, BAS/CTS/DIS. Testler S1 fixture'larıyla. Geliştirme için ekransız bir tanı modu (`Fenpo.exe --diag`).
3. **Bildirimler:** router, Windows bildirimi (`AppNotificationManager`), "Kapat" = ANCS olumsuz aksiyon, olumlu aksiyon sadece varsa. `PreExisting` bildirimler toast olmaz, listeye girer. Metin temizleme (U+200E, U+00AD). Uygulama adı ve ikonu: ANCS `DisplayName`, iTunes Lookup, SQLite önbelleği, yerleşik yedek ikonlar. Bildirimler sayfası (gruplu liste, filtreler, "Tümünü temizle").
4. **Medya:** ana ekrandaki şu an çalıyor kartı, SMTC ile klavye medya tuşları ve Windows medya paneli, desteklenen komutlara göre etkin düğmeler.
5. **iPhone'um ve tepsi:** pil, bağlantı çipleri, model adı (`iPhone14,5` → "iPhone 13" tablosu), tepsi paneli (pil, Bildirimler ve Yakınlık kilidi hızlı düğmeleri, şu an çalıyor).
6. **Eşleşme sihirbazı:** Windows Bluetooth ayarlarını açar, klasik ve LE kaydını ve ANCS'yi bekler, iPhone'da "Sistem Bildirimlerini Paylaş" adımını anlatır. Phone Link çalışıyorsa uyarır. Bozuk bağı ("bağlan, 10 sn'de kop, `Unreachable`") tanıyıp "eşleşmeyi kaldırıp yeniden eşleştir" yönlendirmesi gösterir.
7. **Yakınlık kilidi:** reklam izleyici, 5 saniyelik hareketli ortalama, eşik ve süre ayarı, `LockWorkStation`. Algoritma S1'deki `rssi-walk-away.csv` ile testli.
8. **Yerel API ve Sdk:** `\\.\pipe\fenpo`, aynı kullanıcıya ACL, `fenpo.status`, `fenpo.media.*`, `fenpo.notifications.recent`, olaylar.
9. **Sağlamlaştırma:** 8 saatlik kesintisiz çalışma, uzaklaşıp dönünce yeniden bağlanma ve bildirimlerin devamı (S1'de cihazda doğrulanmamış madde), bildirim gecikmesi < 1 s ölçümü, bellek. `docs/windows/TESTING.md` cihaz kontrol listesi.

## Testler

| Test | Ne |
|---|---|
| Birim (Core) | Metin temizleme, model adı tablosu, ayarların saklanması, yakınlık algoritması (CSV tekrar oynatma), bildirim kimliği (oturum + UID) |
| Birim (Bluetooth) | ANCS Notification Source ve Data Source ayrıştırma (parçalı ve tek parça), uygulama öznitelikleri, Control Point komut kodlama, AMS Entity Update, CTS zamanı, DIS. Fixture'lar `tests/Fenpo.Bluetooth.Tests/Fixtures/` altında, S1'den |
| Servis | Router ve medya köprüsü sahte kaynaklarla (NSubstitute) |
| Elle, cihazda | `docs/windows/TESTING.md`: eşleşme, canlı bildirim gecikmesi, kapatma, medya tuşları, uzaklaşma ve kilit, yeniden bağlanma, 8 saat |
| Görsel | Her ekran Açık/Koyu × Türkçe/English ekran görüntüsüyle |

## Buğra'ya sorular

1. Aşama 1'de olmayan menü öğeleri (Mesajlar, Aramalar, Dosyalar, Pano, Kamera) **gizlensin mi**, yoksa "Yakında" etiketiyle pasif mi dursun? Önerim gizlemek.
2. Paket kimliği ve yayıncı adı: `Milvasoft.Fenpo` / "Milvasoft" mı? Geliştirmede kendinden imzalı `CN=Fenpo Development` sertifikası kullanılıyor.
3. Bekleyen tasarım değişiklikleri (DECISIONS #31, #34): "iPhone'da aç" düğmesi, cihaz satırındaki iOS sürümü, "Odak modunu eşitle" metni, Ada'daki arama düğmeleri. Bir de Aşama 1'e özel uygulamasız eşleşme sihirbazının tasarımı yok: `design/desktop/desktop.html`'e eklenmesini ister misin, yoksa mevcut eşleşme ekranından türeteyim mi?
4. Defender (S5): geliştirme klasörü için dışlama mı, Microsoft'a yanlış alarm bildirimi mi? Aşama 3'teki TLS sunucusu aynı riski taşıyor.
