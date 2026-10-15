# Teknoloji Seçimleri

Sürümler Ekim 2026 itibarıyla. Her seçimin yanında neden seçildiği ve neyin elendiği var.

## Windows

| Konu | Seçim | Neden |
|---|---|---|
| Çalışma zamanı | **.NET 10 LTS** (10.0.13), C# 14 | LTS, Kasım 2028'e kadar destekli. .NET 11 Kasım 2026'da çıkacak ama STS; 10'da kalıp 12 LTS'ye geçmek daha sakin |
| UI | **WinUI 3, Windows App SDK 2.5.1** | Mica, Fluent, Segoe UI Variable, Windows bildirimleri yerel olarak geliyor. Fenpo'nun "Windows'un parçası" gibi durması için en doğru araç |
| Paketleme | **MSIX** (tek proje paketleme), tam güven | Paket kimliği şart: `phoneLineTransportManagement` kısıtlı yeteneği, uygulama bildirimleri, paylaşım hedefi, başlangıç görevi, Dosya Gezgini komutu |
| En düşük Windows | **Windows 11 22H2 (22621)** | Sanal kamera 22000+, `FocusSessionManager` 22H2. Windows 10 desteği Ekim 2025'te bitti |
| Bluetooth | WinRT `Windows.Devices.Bluetooth.*` (GATT istemci ve sunucu, RFCOMM), `Windows.ApplicationModel.Calls` (HFP) | Ek sürücü yok, yerleşik yığın. C# ile doğrudan, CsWinRT üzerinden |
| OBEX, MAP, PBAP | Kendi kodumuz, BestOwl/MyPhone (MIT) referans | Bakımı yapılan bir .NET kütüphanesi yok. MyPhone 2022'den beri durgun, gönderme kısmı yarım |
| MVVM | CommunityToolkit.Mvvm | Kaynak üreticili, hafif, WinUI ile standart |
| Tepsi ikonu | H.NotifyIcon.WinUI | WinUI 3'te yerleşik tepsi desteği yok |
| Barındırma, DI, log | Microsoft.Extensions.Hosting, DependencyInjection, Logging + Serilog (dosya) | Arka plan servisleri için doğal yapı; backend alışkanlıklarıyla aynı |
| Veri | SQLite, Microsoft.Data.Sqlite (EF Core 10 isteğe bağlı) | Mesaj önbelleği, bildirim geçmişi, ikon önbelleği, pano geçmişi |
| Protokol | Google.Protobuf + Grpc.Tools (sadece mesaj üretimi) | iOS ile tek şema; gRPC yükü yok |
| TLS | `SslStream`, TLS 1.3, karşılıklı sertifika sabitleme | Yerleşik, ek bağımlılık yok |
| Keşif | WinRT `DnssdServiceInstance` | Windows'ta yerleşik mDNS, Bonjour kurmaya gerek yok |
| Dosya aktarımı (uyumluluk) | LocalSend v2.2 protokolü | iOS, Android, macOS'taki LocalSend uygulamalarıyla Fenpo iOS uygulaması olmadan da çalışır |
| QR | QRCoder | Küçük, bağımlılıksız |
| Tema | Sistem / Açık / Koyu. Kök `FrameworkElement.RequestedTheme` + `AppWindowTitleBar` renkleri; "Sistem" seçiliyken `UISettings.ColorValuesChanged` dinlenir. Ana pencere, tepsi paneli ve Ada aynı servisten beslenir | WinUI 3'te `Application.RequestedTheme` çalışırken değiştirilemiyor, kök eleman üzerinden değiştirmek anında çalışıyor |
| Dil | Sistem dili / Türkçe / English. `ApplicationLanguages.PrimaryLanguageOverride` + `.resw` kaynakları; değişince "Şimdi yeniden başlat" bilgi çubuğu | Kaynaklar açılışta yükleniyor; yeniden başlatma en güvenilir yol. Windows bildirimleri de aynı dili kullanır |
| Sanal kamera | `MFCreateVirtualCamera` + .NET 10 Native AOT COM medya kaynağı (smourier/VCamNetSample, MIT, referans) | C#'ta kalıyoruz; Native AOT, Frame Server sürecine .NET çalışma zamanı yüklemiyor |
| Video çözme | Media Foundation (donanım H.264/HEVC) | GPU hızlandırmalı, ek kütüphane yok |
| Kilit açma | C++ Credential Provider (Aşama 5) | Companion Device Framework Windows 10 2004'ten beri kullanımdan kaldırılmış. LogonUI'ye yönetilen kod yüklenmemeli |
| Ekran yansıtma | iOS ReplayKit yayın uzantısı → Fenpo veri kanalı. Alternatif: UxPlay (GPLv3) ayrı süreç | ReplayKit yolu MIT kalıyor ve App Store uyumlu. UxPlay daha "yerel" (Denetim Merkezi > Ekran Yansıtma) ama lisansı ayrı tutulmalı |
| Test | xUnit v3, NSubstitute | ANCS/MAP ayrıştırıcıları kayıtlı bayt dizileriyle test edilir |
| Kod stili | Milvaion'un `.editorconfig`'i (repoda) | Milvasoft projeleriyle aynı stil |

**Elenenler:**
- **WPF:** Olgun ama Mica, modern bildirim ve Fluent kontrolleri için ekstra kütüphane gerekiyor, Windows 11 hissi zayıf.
- **Avalonia / .NET MAUI / Electron / Tauri:** Fenpo sadece Windows'ta çalışacak ve WinRT Bluetooth API'lerine derinlemesine bağlı. Çapraz platform katmanı fayda değil, yük.
- **Ayrı Windows servisi (LocalSystem):** Bluetooth ve bildirim API'leri kullanıcı oturumu ve paket kimliği istiyor.

## iPhone

| Konu | Seçim | Neden |
|---|---|---|
| Dil, UI | **Swift 6.4, SwiftUI**, Xcode 27 | Widget, Canlı Etkinlik ve Denetim Merkezi düğmeleri sadece SwiftUI ile yazılabiliyor. Sıkı eşzamanlılık denetimi açık |
| En düşük iOS | **iOS 18.0** | AccessorySetupKit (zorla kapatma sonrası yeniden başlatma için kritik) ve `ControlWidget` 18 ile geldi. iOS 26 tüm iPhone'ların %79'unda, iOS 18+ çok daha yüksek |
| Tasarım dili | iOS 26 Liquid Glass (iOS 27'de de geçerli) | Sistem bileşenleri cam görünümünü kendiliğinden alıyor |
| Bluetooth | CoreBluetooth (merkez, `bluetooth-central` arka plan modu, durum geri yükleme) + **AccessorySetupKit** | Arka planda uyanma ve güvenilir yeniden bağlanma |
| Ağ | Network.framework: `NWConnection` (TLS 1.3, sertifika sabitleme), `NWBrowser` (Bonjour) | Yerleşik, QUIC'e geçiş kolay |
| Kripto | CryptoKit + Keychain (App Group ile paylaşılan) | ECDSA P-256 sertifikalar, AES-GCM |
| Kısayollar ve sistem | App Intents, WidgetKit, ActivityKit, `ControlWidget` | Pano, gönder, çaldır, kilitle; tek dokunuşla her yerden |
| Dosyalar | Share Extension, File Provider Extension, PhotoKit | AirDrop ve "ağdaki bilgisayar" deneyimi |
| Kamera | AVFoundation + VideoToolbox (HEVC/H.264 donanım kodlama) | Düşük gecikme, düşük pil |
| Ekran yansıtma | ReplayKit Broadcast Upload Extension | 50 MB bellek sınırı var; kodlamayı VideoToolbox yapar |
| Protokol | swift-protobuf (SPM) | Tek dış bağımlılık |
| Proje dosyası | **XcodeGen** (`project.yml`) | `.xcodeproj` yerine okunabilir YAML; Claude Code ile düzenlemesi ve git farkları çok daha temiz |
| Test | Swift Testing | Xcode 16+ yerleşik |
| Derleme (Mac yok) | GitHub Actions `macos` runner: XcodeGen → `xcodebuild build` ve simülatörde testler. `FenpoKit` çekirdeği Windows'taki Swift araç zinciriyle de derlenebilir | Mac almadan ilerlemek; cihaz testi TestFlight ile |
| Tema | Sistem / Açık / Koyu: `preferredColorScheme`, seçim App Group'taki `UserDefaults`'ta | Uygulama ve paylaşım uzantısı seçimi izler. Widget'lar ve Denetim Merkezi düğmeleri iOS gereği sistemin görünümünü izler |
| Dil | iOS'un uygulama başına dil ayarı; uygulamadaki "Dil" satırı bu ayarı açar | Uygulama, uzantılar, widget'lar ve Kısayollar aynı dili kullanır. Uygulama içinde dili zorla değiştirmek uzantılarda tutarsızlık yaratıyor |

**Elenenler:**
- **.NET for iOS / MAUI:** Windows ile kod paylaşımı cazip, ama widget, Denetim Merkezi ve Canlı Etkinlik uzantıları SwiftUI zorunlu. Uygulamanın yarısı yine Swift olur, iki dil iki kat bakım demek.
- **Flutter / React Native:** Aynı uzantı sorunu, artı arka plan Bluetooth'ta ek köprü katmanı.

## Android

Kesinleşmiş olanlar: **Kotlin, Jetpack Compose, Material 3**, Gradle Kotlin DSL. Aşağıdakiler Android oturumunun ilk aşamasında araştırılıp `docs/ANDROID.md`'de kesinleşecek; şimdilik başlangıç önerisi.

| Konu | Başlangıç önerisi | Neden / neye bakılacak |
|---|---|---|
| Eşzamanlılık | Kotlin Coroutines + Flow | Standart |
| DI | Hilt | Standart, Compose ile uyumlu |
| Protokol | protobuf Kotlin lite veya Wire | Aynı `.proto` dosyaları |
| Cihaz ilişkilendirme | `CompanionDeviceManager` | iOS'taki AccessorySetupKit'in karşılığı; arka planda ayakta kalma ve izinler |
| Arka plan | Ön plan servisi (`connectedDevice` türü) | Android 14+ servis türü kuralları |
| Bildirimler | `NotificationListenerService` + `RemoteInput` | Yazılı yanıt dahil |
| Pano | Android 10+ arka planda okuma kısıtı | iOS'takine benzer: Hızlı Ayarlar kutucuğu, paylaşım menüsü |
| Ekran yansıtma | `MediaProjection` + erişilebilirlik servisiyle kontrol | Play Store politikaları kontrol edilmeli |
| Dağıtım | Önce GitHub Releases (APK), sonra Play Store ve F-Droid | SMS ve arama geçmişi izinleri Play'de kısıtlı |
| En düşük sürüm | Araştırmayla belirlenecek | Güncel dağılım verisi ve `CompanionDeviceManager` özellikleri |

## Ortak

| Konu | Seçim |
|---|---|
| Repo | Tek repo (monorepo): `windows/`, `ios/`, `protocol/`, `docs/`, `design/`, `spikes/` |
| Lisans | MIT (öneri) |
| Mobil platform | iPhone ve Android paralel iki iş kolu; ortak protokol `Hello.capabilities` ile yetenek pazarlığı yapar |
| CI | GitHub Actions: Windows runner'da derleme + test + MSIX; macOS runner'da iOS derleme + test; Linux runner'da Android derleme + test |
| Dağıtım | Windows: imzalı MSIX, GitHub Releases + winget. iOS: TestFlight, sonra App Store |
| Sürümleme | Protokolde `version` alanı; iki taraf farklı sürümdeyse ortak en yüksek sürümle konuşur |
| Dil | Arayüz Türkçe ve İngilizce (Windows `.resw`, iOS String Catalog) |
