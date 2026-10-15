# Oturumları Başlatma

Üç Claude Code oturumu, üç iş kolu (DECISIONS #18–20). Windows oturumu repoyu kurar, diğer ikisi onun ilk commit'inden sonra açılır.

## Kurulum sırası

**1. Windows oturumu.** `C:\Users\bugra\source\repos\fenpo` klasöründe bir terminal aç, `claude` çalıştır, aşağıdaki **Windows promptunu** yapıştır. Oturum özetini onayladıktan sonra repoyu kurup ilk commit'i atacak ve sana haber verecek.

**2. Worktree'ler.** Windows oturumu "worktree'leri açabilirsin" dedikten sonra, ayrı bir PowerShell penceresinde:

```powershell
cd C:\Users\bugra\source\repos\fenpo
git worktree add ..\fenpo-ios -b ios
git worktree add ..\fenpo-android -b android
```

**3. iPhone oturumu.** `C:\Users\bugra\source\repos\fenpo-ios` klasöründe terminal, `claude`, **iPhone promptu**.

**4. Android oturumu.** `C:\Users\bugra\source\repos\fenpo-android` klasöründe terminal, `claude`, **Android promptu**.

**5. GitHub (iOS CI için gerekli).** Repoyu açtığında:

```powershell
cd C:\Users\bugra\source\repos\fenpo
git remote add origin https://github.com/<hesap>/fenpo.git
git push -u origin main ios android
```

## Günlük işleyiş

- Mobil bir oturum protokol değişikliği yaptığını söylerse: `fenpo` klasöründe `git cherry-pick <commit>` (veya dalı birleştir) ile `main`'e al, sonra diğer mobil oturuma "main'i birleştir" de.
- Kilometre taşlarında mobil dalları `main`'e birleştir: `git merge ios`, `git merge android`.
- Mobil oturumlar her iş bloğunun başında kendileri `git merge main` yapar.

---

## Windows promptu

```text
Fenpo projesinde Windows iş kolunu yürüteceksin. Bu ana oturum: Windows uygulamasından, Aşama 0 spike'larından, ortak protokolden (protocol/) ve dokümanlardan (docs/) sen sorumlusun. iPhone ve Android taraflarını iki ayrı Claude Code oturumu paralel yürütüyor; ios/ ve android/ klasörlerine dokunma.

1. OKU VE ÖZETLE
CLAUDE.md'yi ve orada sayılan her şeyi baştan sona oku: docs/DECISIONS.md, docs/PLAN.md, docs/ARCHITECTURE.md, protocol/ (README ve .proto dosyaları), docs/TECH-STACK.md, docs/RESEARCH.md. design/desktop/desktop.html dosyasını incele. Sonra bana en fazla 10 maddelik bir özet ver: ne yapıyoruz, Windows tarafında sırayla ne yapacaksın, dokümanlarda gördüğün çelişki veya eksik varsa ne. Onayımı bekle.

2. REPO
git init, ana dal main. .gitattributes ekle: *.cs, *.csproj, *.slnx, *.xaml, *.resw, *.props ve *.targets CRLF; *.swift, *.kt, *.kts, *.proto, *.yml, *.yaml, *.sh ve *.md LF; görseller ve diğer ikili dosyalar binary. Mevcut dosyaları "chore: planning, designs and protocol v1 draft" mesajıyla commit'le. Uzak repo ekleme ve push yapma, o kısmı ben yapacağım. Bitince bana iPhone ve Android worktree'lerini açabileceğimi söyle (komutlar docs/KICKOFF.md'de).

3. ORTAM
Kontrol et: dotnet --list-sdks (.NET 10), Windows App SDK 2.5.x, Visual Studio 2026 bileşenleri (WinUI, MSIX), Windows sürümü. Küçük bir konsol programıyla Bluetooth adaptörünün BluetoothAdapter.IsCentralRoleSupported ve IsPeripheralRoleSupported değerlerini oku. Eksik veya uyumsuz bir şey varsa neyi nasıl kuracağımı söyle.

4. AŞAMA 0 SPIKE'LARI
Bu sırayla: S1 (ANCS + AMS + Battery; S8 Device Information da bunun içinde), S2 (MAP), S3 (HFP, PhoneLineTransportDevice), S5'in Windows tarafı (TLS 1.3 sunucusu, ECDSA P-256 sertifika, QR, DNS-SD _fenpo._tcp, protocol/'deki PairingRequest/PairingResult ve Hello), S7 (sanal kamera, smourier/VCamNetSample referansıyla).
- Her spike spikes/S<n>-<ad>/ altında bağımsız küçük bir proje ve README.md: hedef, yapılanlar, sonuç, karar, ürün koduna etkisi.
- Cihaz gerektiren her adımda ne yapmam gerektiğini (Windows'ta hangi ekran, iPhone'da hangi ayar) numaralı adımlarla yaz ve sonucu bekle. Ben sonucu söylemeden "çalıştı" yazma.
- Cihazdan gelen ham baytları (ANCS, AMS, MAP, PBAP) ileride test fixture'ı olarak kullanmak üzere kaydet.
- Phone Link açıksa Bluetooth bağlantısını tutup çakışıyor; gerekiyorsa kapatmamı söyle.
- Her spike bitince sonucu docs/RESEARCH.md'ye, gerekiyorsa docs/PLAN.md ve docs/DECISIONS.md'ye işle; ayrı commit.

5. AŞAMA 1
Spike'lar bitince Aşama 1 için uygulama planını (windows/ altındaki projeler, ilk ekranlar, sıra, testler) bana sun. Onaydan sonra ürün koduna başla. Tema (Sistem/Açık/Koyu) ve dil (Türkçe/English) ilk günden; her ekran iki temada ve iki dilde doğrulanmadan bitmiş sayılmaz. Bildirim, mesaj ve medya servislerini ARCHITECTURE.md'deki gibi kaynaktan bağımsız arayüzlerle kur; ileride Android kaynağı da aynı arayüzlere bağlanacak.

Protokolün sahibi sensin: protocol/ değişikliği her zaman ayrı bir "protocol: ..." commit'i ve aynı commit'te docs/ARCHITECTURE.md güncellemesi. Diğer oturumların protokol değişikliklerini ben main'e alacağım; sen her iş bloğunun başında git log ile protocol/ değişikliklerini kontrol et.
```

## iPhone promptu

```text
Fenpo projesinde iPhone iş kolunu yürüteceksin. Bu klasör (fenpo-ios) ana reponun bir git worktree'si, dalın ios. Windows ve Android taraflarını ayrı Claude Code oturumları yürütüyor. Senin alanın: ios/, docs/ios/ ve .github/workflows/ios.yml. Başka bir yere dokunman gerekirse önce bana söyle.

ÖNEMLİ KISIT: Mac yok. Bu bilgisayarda iOS için derleme yapamazsın. Doğrulama yolları:
- GitHub Actions macOS runner'ı (.github/workflows/ios.yml): XcodeGen ile proje üret, xcodebuild ile simülatör için derle, simülatörde testleri koş. Repo GitHub'a bağlanana kadar CI çalışmaz; o zamana kadar yazdığın her şey "derlenmedi" sayılır ve öyle raporlanır.
- FenpoKit'in Apple çatılarına bağlı olmayan çekirdeği (protokol, çerçeveleme, QR ayrıştırma, durum makineleri) Windows'taki Swift araç zinciriyle derlenip test edilebilir. Kurulu değilse nasıl kuracağımı söyle.
- Cihaz testleri ortağım tarafından Mac + iPhone ile, kilometre taşlarında yapılacak (DECISIONS #14, #15).

1. OKU VE ÖZETLE
CLAUDE.md, docs/DECISIONS.md, docs/PLAN.md, docs/ARCHITECTURE.md, protocol/, docs/TECH-STACK.md ve docs/RESEARCH.md'yi oku; design/mobile/mobile.html dosyasını incele. En fazla 10 maddelik özet, ilk adımların ve gördüğün çelişkiler. Onayımı bekle.

2. İSKELET
Önce git merge main. Sonra:
- ios/project.yml (XcodeGen): Fenpo uygulaması, FenpoShare ve FenpoWidgets hedefleri. FenpoFiles, FenpoBroadcast ve FenpoFocus sonraki aşamalarda.
- FenpoKit Swift paketi: platformdan bağımsız çekirdek + Apple'a özel modül. swift-protobuf ile protocol/ altındaki dosyalardan kod üretimi.
- App Group, paylaşılan anahtar zinciri, Info.plist anahtarları: Bluetooth kullanımı, yerel ağ, NSBonjourServices (_fenpo._tcp), bluetooth-central arka plan modu, AccessorySetupKit.
- Asset Catalog'da marka renkleri Any/Dark, Localizable.xcstrings tr/en, uygulama ikonu design/logo/svg/1-cekirdek.svg'den.
- .github/workflows/ios.yml.

3. ARAYÜZ
design/mobile/mobile.html'e birebir: ana ekran ve sekme çubuğu, eşleşme (QR tarayıcı ve elle kod), Bluetooth onayı adımı, ayarlar. Ayarlar'da Görünüm (Sistem/Açık/Koyu) uygulamanın içinde seçilir; Dil satırı iOS'un uygulama başına dil ayarını açar. Veriler şimdilik protokol arayüzlerinin arkasında örnek verilerle. SwiftUI önizlemeleri iki tema ve iki dil için.

4. KANALLAR
QR ayrıştırma, TLS 1.3 istemcisi (Network.framework, sunucu sertifikasını QR'daki parmak izine sabitleme, kendi ECDSA P-256 sertifikası), PairingRequest/PairingResult, Hello ve yetenek pazarlığı, [uint32 uzunluk][Envelope] çerçeveleme. Fenpo BLE kanalı istemcisi: CoreBluetooth merkez rolü, durum geri yükleme, AccessorySetupKit ile kurulum, AES-GCM, ARCHITECTURE.md'deki parçalama. Çekirdek mantık birim testleriyle.

5. ORTAK İÇİN TEST PAKETİ
docs/ios/TESTING.md: kilometre taşı 1 için ortağımın adım adım izleyeceği liste. S4: arka planda BLE ile uyanma, zorla kapatma ve Bluetooth kapatıp açma sonrası geri gelme. S6: arka planda UIPasteboard'a yazma. Hangi commit'ten nasıl derlenir, iPhone'a nasıl kurulur, ne gözlenir, bana ne geri yazılır. Bu testler için uygulamada gizli bir "Geliştirici" menüsü olsun (log görüntüleme dahil).

Her iş bloğunun sonunda üç ayrı başlıkla raporla: CI'da derlenen ve testi geçen, derlenmeyen veya CI'ı olmayan, cihazda denenmesi gereken. Protokolde değişiklik gerekiyorsa kendi dalında ayrı bir "protocol: ..." commit'i yap, docs/ARCHITECTURE.md'yi aynı commit'te güncelle ve bana söyle.
```

## Android promptu

```text
Fenpo projesinde Android iş kolunu yürüteceksin. Bu klasör (fenpo-android) ana reponun bir git worktree'si, dalın android. Windows ve iPhone taraflarını ayrı Claude Code oturumları yürütüyor. Senin alanın: android/, docs/ANDROID.md, docs/android/, design/android/ ve .github/workflows/android.yml. Başka bir yere dokunman gerekirse önce bana söyle.

Android projeye sonradan eklendi: tasarımı ve ayrıntılı teknoloji kararları henüz yok. Kesin olanlar: Kotlin, Jetpack Compose, Material 3, Gradle Kotlin DSL, ortak protokol (protocol/). Bu yüzden önce araştırma ve tasarım, sonra kod.

1. OKU VE ÖZETLE
CLAUDE.md, docs/DECISIONS.md (özellikle #10, #11, #16), docs/PLAN.md (Android iş kolu), docs/ARCHITECTURE.md (Android tarafı ve protokol), protocol/, docs/TECH-STACK.md (Android) ve docs/RESEARCH.md'yi (biçim örneği olarak) oku. design/mobile/mobile.html ve design/desktop/desktop.html dosyalarını incele. Kısa özet ve plan, onayımı bekle.

2. AŞAMA A: ARAŞTIRMA VE TASARIM (KOD YOK)
a) docs/ANDROID.md: docs/RESEARCH.md biçiminde, her madde için karar + güncel resmi kaynaklar (bugün Ekim 2026). En az şunlar:
   - CompanionDeviceManager, cihaz varlığıyla arka planda uyanma
   - Ön plan servis türleri (connectedDevice) ve güncel Android sürümlerinin arka plan kuralları
   - BLE merkez rolüyle Windows GattServiceProvider'a bağlanma, bağ kurma
   - NotificationListenerService ve RemoteInput ile yazılı yanıt
   - SMS ve arama geçmişi izinleri, Play Store politikası
   - HFP, PBAP ve MAP desteği: Windows'taki klasik Bluetooth kodu Android telefonlarla yeniden kullanılabilir mi
   - Pano kısıtları ve çözümleri (Hızlı Ayarlar kutucuğu, paylaşım menüsü)
   - Paylaşım hedefi ve arka planda dosya alma
   - CameraX ile telefonu web kamerası yapma
   - MediaProjection ile ekran yansıtma, erişilebilirlik servisiyle PC'den kontrol ve Play politikası
   - En düşük SDK önerisi, güncel dağılım verisiyle
   - Kütüphane seçimleri (DI, protobuf, TLS, QR tarama), modül yapısı, Kotlin kod stili
   - Dağıtım: GitHub Releases, Play Store, F-Droid
b) design/android/android.html: design/mobile/mobile.html'deki ekranların Android karşılıkları, Material 3 diliyle ve CLAUDE.md'deki tasarım sözlüğündeki Fenpo renkleriyle. Ekranlar: ana ekran, eşleşme, izinler, paylaşım, bildirim ve Hızlı Ayarlar, kamera modu, ayarlar (Görünüm Sistem/Açık/Koyu; Dil Sistem/Türkçe/English, Android 13+ uygulama başına dil). Sayfada açık/koyu tema düğmesi olsun. Türkçe metinler, iPhone tasarımıyla aynı ses tonu. Telefon çerçeveli, tarayıcıda açılan tek bir HTML dosyası.
Bitince bana sun. Onayım olmadan koda geçme.

3. AŞAMA B VE SONRASI (ONAYDAN SONRA)
Önce git merge main. android/ iskeleti: onaylanan modül yapısı, version catalog, protocol/'den kod üretimi, tema ve diller (values ve values-tr), .github/workflows/android.yml (derleme ve birim testleri), tasarıma birebir ekranlar. Ardından QR eşleşme, TLS kanalı (ECDSA P-256, sertifika sabitleme), Hello ve yetenek pazarlığı, Fenpo BLE kanalı. Gerçek telefon gerektiren adımlardan önce bana haber ver; emülatörde Bluetooth doğrulanamaz.

Android'e özel özellikler (bildirim aynalama, SMS) için Envelope'da 90-109 aralığı ayrılmış. Protokolde değişiklik gerekiyorsa kendi dalında ayrı bir "protocol: ..." commit'i yap, docs/ARCHITECTURE.md'yi aynı commit'te güncelle ve bana söyle.
```
