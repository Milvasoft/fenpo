# Fizibilite Araştırması (15 Ekim 2026)

Her başlıkta kısa bir karar, en önemli bilgiler ve kaynaklar var. "Spike" yazan yerler gerçek cihazla denenmeden kesinleşmiyor (bkz. [PLAN.md](PLAN.md), Aşama 0).

## 1. Bildirim ve medya: ANCS + AMS
**Karar: Çalışıyor, dikkat edilecek yerler var.**
- `312arjun/iphone-companion` (MIT, Python + bleak, altında WinRT GATT) Windows'ta ANCS ve AMS'yi uygulamasız yapıyor. Eşleşme: Windows Ayarlar > Cihaz ekle, iki tarafta kod onayı, iPhone'da "Bildirimleri paylaş" onayı.
- Linux'taki numara (ANCS servis solicitation yayını) Windows'ta yasak: `BluetoothLEAdvertisementPublisher` bu alanları sisteme ayırıyor. İki yol var: Windows Ayarlar'dan eşleşme ya da iOS uygulamasının, şifreleme isteyen bir Fenpo karakteristiğine erişerek eşleşmeyi tetiklemesi.
- ANCS bağ şifrelenmeden görünmüyor. Windows, bağsız denemedeki boş servis listesini önbelleğe alıyor; `BluetoothCacheMode.Uncached` kullan. Service Changed (0x2A05) aboneliği şart. Control Point yazmaları sıralı olmalı. Phone Link açıksa bağlantıyı tutup çakışıyor.
- ANCS ile yazılı yanıt yok, sadece bildirimin sunduğu olumlu/olumsuz aksiyon. "Sessiz teslim" ayarlı uygulamalar iletilmiyor.
- iOS 26.3+ "Notification Forwarding" (AccessoryNotifications) sadece AB'de; Türkiye için işe yaramıyor.

**S1 sonucu (16 Ekim, iPhone 13, cihazda):** ANCS ve AMS, paketlenmemiş bir .NET 10 sürecinden çalışıyor ([spikes/S1-ancs-ams](../spikes/S1-ancs-ams/README.md)).
- Windows Ayarlar > Cihaz ekle ile yapılan tek eşleşme klasik ve LE kaydını birlikte oluşturuyor; ANCS ilk `Uncached` keşifte görünüyor. Phone Link'ten kalan bozuk bir bağ "bağlan, 10 saniyede kop, keşif `Unreachable`" döngüsüne sokuyor; çözümü iki taraftan eşleşmeyi kaldırıp yeniden eşleştirmek.
- Service Changed karakteristiğine abone olunamıyor (`AccessDenied`) ama `GattServicesChanged` olayı geliyor. iPhone her yeniden bağlanmada iki kez Service Changed gönderiyor ve **eski GATT nesneleri geçersiz kalıyor**; keşif ve abonelikler baştan kurulmalı.
- Abone olunca var olan bildirimler `PreExisting` bayrağıyla geliyor. Öznitelikler 120–400 ms'de okunuyor. Görülen uygulamaların hepsinde sadece olumsuz aksiyon ("Clear") var. **UID'ler her bağlantıda sıfırlanıyor.**
- MTU 527'de en uzun Data Source yanıtı 488 bayt, parçalanmadı.
- AMS komutları 40–90 ms içinde yansıyor. Desteklenen komut listesi oyuncunun durumuna göre değişiyor; ses güncellemeleri birleştiriliyor.
- Cihazda doğrulanmayanlar: yeniden bağlandıktan sonra bildirimlerin akmaya devam etmesi, bağlıyken gelen canlı bildirimin gecikmesi.

Kaynaklar: [iphone-companion](https://github.com/312arjun/iphone-companion), [ancs4linux](https://github.com/pzmarzly/ancs4linux), [BluetoothLEAdvertisementPublisher](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementpublisher?view=winrt-26100), [ANCS spec](https://developer.apple.com/library/content/documentation/CoreBluetooth/Reference/AppleNotificationCenterServiceSpecification/Specification/Specification.html), [AMS spec](https://developer.apple.com/library/ios/documentation/CoreBluetooth/Reference/AppleMediaService_Reference/Specification/Specification.html), [Accessory Design Guidelines](https://developer.apple.com/accessories/Accessory-Design-Guidelines.pdf) §58.12.4

## 2. Pil, saat, cihaz bilgisi
**Karar: Battery ve Current Time çalışıyor. Device Information için spike (S8).**
- Apple ADG §58.12.4: iOS 7'den beri her eşleşmiş aksesuara Battery Service, Current Time Service ve ANCS açık. iPhone, eşleşme sürdükçe ve bu servislerden biri kullanıldıkça bağlantıyı açık tutuyor; bu, bağlantıyı canlı tutmak için kullanışlı.
- Device Information (0x180A) belgelenmemiş, gerçek cihazda denenmeli.
- iOS uygulamaları bu servisleri kendileri yayınlayamaz (§58.14).

**S1/S8 sonucu (16 Ekim, cihazda):** Battery okunuyor ve değişince bildirim geliyor. Current Time okunuyor, PC ile 1–2 saniye fark var. Device Information sadece `Manufacturer Name` ("Apple Inc.") ve `Model Number` ("iPhone14,5") veriyor; **iOS sürümü yok**. Model adı için bir eşleme tablosu gerekiyor; iOS sürümü ancak Fenpo kanalından gelebilir.

## 3. Mesajlar ve rehber: MAP + PBAP
**Karar: Çalışıyor, iOS kısıtları var.**
- iOS, MAP 1.4 (bildirim, yanıt, yeni mesaj, gelen kutusu, okundu) ve PBAP 1.2 (rehber, arama geçmişi) destekliyor.
- Windows'ta RFCOMM ile erişilebilir: `RfcommServiceId.PhoneBookAccessPse` (0x112F), MAP için `FromShortId(0x1132)`. Sadece `bluetooth` yeteneği gerekiyor. Kanallar dinamik, SDP ile bulunmalı.
- BestOwl/MyPhone (MIT): OBEX istemci ve sunucu, `MasClient`, `MnsServer`, `PbapClient`. 2022'den beri durgun, `PushMessage()` yarım. iPhone klasör gezinmeye izin vermiyor.
- iPhone kısıtları (Phone Link'te de aynı): sadece bağlıyken gelen ve giden mesajlar, grup mesajı yok, resim gönderme yok, SMS ile iMessage ayırt edilemiyor.

**S2 sonucu (16 Ekim, iPhone 13, cihazda):** MAP kendi OBEX kodumuzla çalışıyor ([spikes/S2-map](../spikes/S2-map/README.md)).
- İlk bağlantı `Forbidden` dönüyor; iPhone'da "Show Message Notifications" açılınca bağlanıyor.
- SDP: MAP 1.4, RFCOMM kanal 8, L2CAP PSM `0x100B`, mesaj türü sadece `SMS_GSM`, en büyük OBEX paketi 4000 bayt.
- Yukarıdaki iki bilgi bu iOS'ta geçerli değil: **`SetPath` ile klasör gezinme çalışıyor**, **gelen kutusunda eşleşmeden önceki SMS'ler de var**. `sent` klasörü ise boş geliyor; telefondan gönderilmiş eski mesajlar yok.
- bMessage, gönderenin rehberdeki adını (`FN`) da taşıyor.
- Okundu işaretleme, MNS bildirim kaydı (iPhone 260 ms'de bizim RFCOMM sunucumuza bağlanıyor) ve **gönderme çalışıyor**: PushMessage 1,8 saniyede `Success` döndü, sonra `MessageShift` ve `SendingSuccess` olayları geldi, mesaj karşıya ulaştı.
- Olay raporları MAP 1.0 biçiminde: gönderen ve metin yok. `NewMessage` sonrası `GetMessage` bir kez 33 saniye gecikti; yeni mesajın ilk gösterimi ANCS'den yapılmalı.

**Ürün cihaz testi (16 Ekim öğlen, aynı iPhone, S3'teki yeniden eşleşmeden sonra):**
- "Mesaj Bildirimlerini Göster" açık olduğu hâlde iPhone MAS kaydını SDP'de göstermedi (paketli Fenpo'da da paketsiz S2 programında da 0 kayıt). iPhone'da Bluetooth kapatılıp açılınca klasik bağlantı yeniden kuruldu ve MAS geri geldi. Fenpo bu durumu "izin gerekiyor" olarak gösteriyor; sihirbaz ve Mesajlar sayfasının metnine Bluetooth'u kapatıp açma adımı eklenmeli.
- Rehberdeki bir kişiden gelen SMS listede `sender_name` olarak numarayla geldi, bMessage'daki `FN` de boştu (S2'de doluydu). Ad, aynı mesajın ANCS bildiriminin başlığından alınıyor (`SenderNames`: metin ve saat eşleşmesi). Bildirimi görülmemiş eski mesajlar numarayla kalıyor.
- Fenpo'dan gönderilen mesaj karşıya ulaştı, ama `sent` klasörü boş kaldı (S2'de oradaydı). Fenpo gönderdiklerini bu bilgisayarda saklıyor (`SentMessages`).
- **PBAP:** SDP'de PSE (0x112F) var, ama RFCOMM bağlantısını iPhone reddediyor ("destination host was down"). Bilgisayar PBAP istemci (PCE, 0x112E) kaydı yayınlasa ve iPhone'dan unutulup yeniden eşleşse de "Kişileri Eşzamanla" anahtarı çıkmadı. Tahmin (doğrulanmadı): iOS rehberi sadece eller serbest (HFP) bağlı cihazlarla paylaşıyor; HFP bu makinede kullanılamıyor (S3).

Kaynaklar: [Apple HT204387](https://support.apple.com/HT204387), [MyPhone](https://github.com/BestOwl/MyPhone), [RfcommServiceId](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.rfcomm.rfcommserviceid.phonebookaccesspse?view=winrt-20348), [imsg-map](https://docs.rs/crate/imsg-map/0.3.1)

## 4. Aramalar: HFP
**Karar: Çalışıyor ama kısıtlı yetenek ve en büyük risk (S3).**
- MSIX paketi + `rescap:phoneLineTransportManagement` gerekiyor. Akış: `PhoneLineTransportDevice.GetDeviceSelector` → `RequestAccessAsync` → `RegisterApp` → `Connect` → `PhoneLine` ile arama.
- Kısıtlı yetenekler yandan yüklemede onaysız çalışıyor; Store'a girmek onay ister ve büyük ihtimalle verilmez.
- `MYSTRAVIL/klangbruecke` (2026) bu yolla iki yönlü sesli gerçek HFP araması raporluyor; ses yolunu Windows yönetiyor. Microsoft Q&A'da `RequestAccessAsync`'in yetenek varken bile DeniedBySystem döndüğü çözülmemiş bir rapor var.
- HFP aktarımını aynı anda tek uygulama tutabilir; Phone Link çakışır.

**S3 sonucu (16 Ekim, Windows 11 26H2 build 26300 Insider, iPhone 13): bu makinede çalışmadı** ([spikes/S3-hfp](../spikes/S3-hfp/README.md)).
- Paketli (gevşek kayıt, Geliştirici Modu) bir uygulamada `RequestAccessAsync` `Allowed` dönüyor. Ama `RegisterApp` hata vermeden **kaydı almıyor**: `IsRegistered` false kalıyor, iPhone için `PhoneLine` oluşmuyor, `ConnectAsync` false dönüyor.
- LAF `phonelinetransportdevice_v1` durumu `Unavailable`.
- Phone Link kapalıyken ve iPhone sıfırdan eşleştirildikten sonra da sonuç aynı; rolü başka bir uygulamanın tutması elendi.
- Kararlı Windows 11'de davranış bilinmiyor. Sefirah (Windows 11) ve klangbruecke (Windows 10) kaydı alabildiğini bildiriyor.

Kaynaklar: [Yetenek bildirimleri](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations), [PhoneLineTransportDevice](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice?view=winrt-26100), [klangbruecke](https://github.com/MYSTRAVIL/klangbruecke), [MyPhone #7](https://github.com/bestowl/myphone/issues/7), [Q&A](https://learn.microsoft.com/en-us/answers/questions/1258935/in-phonelinetransportdevice-windows-applicationmod)

## 5. Pano
**Karar: Arka planda okuma imkânsız. Arka planda yazma için spike (S6). Kısayol yolları çalışıyor.**
- Apple DTS: arka planda `UIPasteboard` okumanın yolu yok, bilinçli politika. "Diğer uygulamalardan yapıştır: İzin Ver" ayarı sadece ön plan onay penceresini kaldırıyor. `UIPasteControl` sormadan yapıştırıyor.
- iPhone → PC: "Metni PC'ye gönder" App Intent'i + hazır Kısayol (Panoyu Al → Fenpo). Arkaya dokun, Eylem Düğmesi veya Denetim Merkezi'ne bağlanır. WinSend aynı yöntemi kullanıyor.
- PC → iPhone: intent PC panosunu döndürür, Kısayol "Panoya Kopyala" yapar.
- Klavye uzantısı (Tam Erişim) ve resim-içinde-resim hilesi mümkün ama App Store riski taşıyor; kullanmıyoruz.

Kaynaklar: [Apple forum 706761](https://developer.apple.com/forums/thread/706761), [Kontroller](https://developer.apple.com/documentation/widgetkit/creating-controls-to-perform-actions-across-the-system), [Octoclip kısıtları](https://docs.octoclip.app/advanced/ios/limitations)

## 6. Arka planda BLE kanalı
**Karar: Çalışıyor. AccessorySetupKit şart.**
- `bluetooth-central` + sabit `restoreIdentifier` ile iOS bağlantı ve bildirimlerde uygulamayı yaklaşık 10 saniyeliğine uyandırıyor. Bekleyen `connect()` hiç zaman aşımına uğramıyor.
- TN3115 (Eylül 2025): çökme ve bellekten atılmada yeniden başlatılıyor; yeniden başlatmada ilk kilit açmadan sonra; Ayarlar'dan Bluetooth kapatılınca ve zorla kapatmada **hayır**. iOS 26'dan beri Denetim Merkezi'nden Bluetooth açıp kapatmada sadece AccessorySetupKit ile kurulan uygulamalar geri geliyor. Apple DTS, zorla kapatma sonrası için doğru yolun ASK olduğunu söylüyor.
- Windows'ta `GattServiceProvider` için adaptörün çevre birim rolünü desteklemesi gerekiyor (`IsPeripheralRoleSupported`); bazı ucuz CSR donanımlarda yok. 128 bit UUID reklamın 31 baytının 16'sını kaplıyor.
- **S4 Windows ölçümü (16 Ekim):** `GattServiceProvider` paketlenmemiş süreçte çalışıyor. Reklama servis verisi eklenince (4 bayt bile) durum `StartedWithoutAllAdvertisementData` oluyor: genişletilmiş reklam destekli adaptörde bile eski 31 baytlık reklamda kalıyor ve veriyi atıyor. PC'yi ayırt etmek servis verisiyle yapılamaz ([spikes/S4-ble-peripheral](../spikes/S4-ble-peripheral/README.md)).
- **S4 iPhone'la (16 Ekim, nRF Connect):** PC bilgisayar adıyla (BUGRAKOSEN) ve Fenpo servisiyle görünüyor. Bağlı iPhone'la `rx` yazma ve `tx` bildirim çalışıyor, ATT MTU 527. Bağ zaten olduğu için şifreli karakteristikte soru çıkmadı.

Kaynaklar: [TN3115](https://developer.apple.com/documentation/technotes/tn3115-bluetooth-state-restoration-app-relaunch-rules), [Arka plan BLE](https://developer.apple.com/library/archive/documentation/NetworkingInternetWeb/Conceptual/CoreBluetooth_concepts/CoreBluetoothBackgroundProcessingForIOSApps/PerformingTasksWhileYourAppIsInTheBackground.html), [Apple forum 840468](https://origin-devforums.apple.com/forums/thread/840468), [GATT sunucusu](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-server)

## 7. PC'nin iPhone için klavye ve fare olması
**Karar: Çalışıyor, deneysel.**
- Windows'ta `GattServiceProvider` için yasak olanlar sadece DIS, GATT, GAP ve Scan Parameters. HID (0x1812) serbest. iOS, HOGP ana makinesi.
- `abhishek-raj/windows-ble-hid` (MIT): Windows yerleşik yığınıyla birleşik klavye + fare; iPhone'da klavye ve AssistiveTouch ile fare çalışıyor. Yeniden bağlanma denenmemiş.

Kaynak: [windows-ble-hid](https://github.com/abhishek-raj/windows-ble-hid)

## 8. iPhone'u web kamerası yapmak
**Karar: Çalışıyor. COM medya kaynağı .NET Native AOT olabilir (S7).**
- `MFCreateVirtualCamera` (Windows 11 22000+) özel medya kaynağının CLSID'sini alıyor. Kaynak Frame Server sürecinde yüklendiği için HKLM'de kayıtlı bir COM sunucusu olmalı ve Local Service tarafından okunabilir bir yerde durmalı (kullanıcı profili değil).
- `smourier/VCamNetSample` (MIT): .NET 10 Native AOT sürümü var. Fenpo.exe ile medya kaynağı arasında paylaşımlı bellek veya paylaşımlı doku gerekiyor.
- MSIX ile HKLM kaydı spike'ta netleşmeli; gerekirse kamera bileşeni ayrı, yönetici yetkili bir kurulumla gelir.
- **S7 sonucu (16 Ekim, gece):** VCamNetSample'ın AOT medya kaynağı .NET 10 Native AOT ile 5,5 MB'lık tek bir DLL'e derleniyor. İmzalı bir MSIX içinde `com:SurrogateServer/com:Class` ile bildirildiğinde `MFCreateVirtualCamera` `S_OK` dönüyor, ama `Start` **`Class not registered`** veriyor: Frame Server paketli COM sınıfını görmüyor. Kamera bileşeni ayrı, yönetici onaylı bir kurulum gerektiriyor. DLL `C:\ProgramData` altına kopyalanıp HKLM'ye kaydedilince `Start` `S_OK` dönüyor ve `MediaCapture` 1280×960 test desenini alıyor: Native AOT medya kaynağı Frame Server içinde çalışıyor ([spikes/S7-virtual-camera](../spikes/S7-virtual-camera/README.md)).

Kaynaklar: [MFCreateVirtualCamera](https://learn.microsoft.com/en-us/windows/win32/api/mfvirtualcamera/nf-mfvirtualcamera-mfcreatevirtualcamera), [VCamNetSample](https://github.com/smourier/VCamNetSample)

## 9. Kilitleme ve kilit açma
**Karar: Kilitleme çalışıyor. Companion Device Framework ölü. Credential Provider deneysel.**
- `SecondaryAuthenticationFactor` Windows 10 2004'ten beri kullanımdan kaldırılmış; oturum açma yeteneği artık verilmiyor.
- Gerçek yol: C++ `ICredentialProvider` DLL'i (Microsoft Windows-classic-samples), Fenpo.exe ile güvenli yerel kanal, DPAPI/TPM ile kimlik bilgisi saklama.
- Dynamic Lock yerleşik ama API'si yok. Kendi mantığımız: BLE RSSI + `LockWorkStation()`.
- **S1 sonucu (16 Ekim, cihazda):** Bağlı GATT bağlantısının RSSI'si WinRT'de yok. Ama Windows, eşleşmiş iPhone'un değişen özel adresini bağ adresine çözüyor: pasif `BluetoothLEAdvertisementWatcher` ve `System.Devices.Aep.SignalStrength` aynı değeri veriyor. Masada −50…−55 dBm (arada tek tük −75), uzaklaşırken −78…−92 dBm, başka odada reklam kesiliyor ve bağlantı ~10 saniyede kopuyor. Geri gelince `MaintainConnection` ile 1 saniyede yeniden bağlanıyor. Klasik adresin `SignalStrength` değeri kullanışsız (çoğunlukla 0).

Kaynaklar: [Companion device unlock](https://learn.microsoft.com/en-us/windows/uwp/security/companion-device-unlock), [line-win-credprov](https://github.com/der3318/line-win-credprov)

## 10. Ekran yansıtma
**Karar: Mümkün. Ana yol ReplayKit, isteğe bağlı UxPlay.**
- UxPlay aktif (son commit Ekim 2026, v1.73.7), Windows 10/11'de çalışıyor. 1.74 kendi mDNS yanıtlayıcısını getiriyor.
- Lisansı **GPLv3**, içindeki PlayFair'in hukuki durumu README'ye göre belirsiz. MIT Fenpo'ya gömülemez; ayrı çalıştırılabilir olarak, kaynağıyla birlikte dağıtılabilir. "AirPlay" ticari markası kullanılmamalı.

Kaynak: [UxPlay](https://github.com/FDH2/UxPlay)

## 11. Keşif: DNS-SD
**Karar: Çalışıyor, ağ koşullarına dikkat.**
- Windows 1703'ten beri yerleşik mDNS: `DnssdServiceInstance.RegisterStreamSocketListenerAsync`.
- Sessiz başarısızlıklar: güvenlik duvarı UDP 5353'ü engelliyor, ağ profili "Genel", birden fazla adaptör veya VPN.
- iOS: `NSLocalNetworkUsageDescription` ve `NSBonjourServices` içinde `_fenpo._tcp` olmalı. Yerel ağ izni soruluyor. Arka planda tarama yok.
- **S5 notu (16 Ekim, gece):** Kayıt için WinRT `DnssdServiceInstance` yerine Win32 `DnsServiceRegister` (dnsapi.dll) kullanıldı; TLS sunucusu sade bir `TcpListener` üzerinde kalabiliyor. Çalıştırılamadı: Windows Defender, TLS sunucusu, DPAPI ve mDNS kaydını birlikte taşıyan `PcServer.dll`'i `Backdoor:Win32/Farfli!pz` olarak karantinaya aldı (buluşsal yanlış alarm). Ürünün de aynı riski taşıdığı için imzalı ikili dosyalar ve sürümlerin Microsoft'a önceden gönderilmesi gerekiyor ([spikes/S5-tls-pairing](../spikes/S5-tls-pairing/README.md)).

Kaynaklar: [DnssdServiceInstance](https://learn.microsoft.com/en-us/uwp/api/windows.networking.servicediscovery.dnssd.dnssdserviceinstance), [Apple forum 653316](https://developer.apple.com/forums/thread/653316)

## 12. LocalSend
**Karar: Uygulanmalı.**
- Protokol v2.2: UDP multicast 224.0.0.167:53317, HTTPS REST (`prepare-upload` → `upload`), kendinden imzalı sertifikanın SHA-256 parmak izi. Uygulama reposu Apache-2.0 (MIT ile uyumlu). v3 taslağı sürüyor.
- Fayda: Fenpo for Windows, iOS/Android/macOS/Linux'taki LocalSend uygulamalarıyla anında konuşur.

Kaynak: [LocalSend protokolü](https://github.com/localsend/protocol)

## 13. Odak eşitleme
**Karar: iPhone → PC kısmen. PC'de Rahatsız Etme'yi açmak genel API ile mümkün değil.**
- iOS: `SetFocusFilterIntent` arka planda uzantı ister. `INFocusStatusCenter` sadece "odakta mı" bilgisini veriyor ve iletişim uygulamalarına yönelik bir yetenek istiyor (App Review riski).
- Windows: `FocusSessionManager.IsFocusActive` okunabiliyor; başlatma ve durdurma Microsoft'tan token isteyen Sınırlı Erişim Özelliği. Fenpo kendi bildirimlerini susturur ve göstergede gösterir.

Kaynaklar: [SetFocusFilterIntent](https://developer.apple.com/documentation/appintents/setfocusfilterintent), [Apple forum 761593](https://developer.apple.com/forums/thread/761593), [TryStartFocusSession](https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.focussessionmanager.trystartfocussession?view=winrt-26100)

## 14. Uygulama adı ve ikonu
**Karar: Çalışıyor.**
- `https://itunes.apple.com/lookup?bundleId=<id>&country=tr` ad ve `artworkUrl512` döndürüyor; Apple'ın kendi uygulamaları da (Mesajlar, Telefon, Takvim). Ayarlar uygulaması sonuç vermiyor, yerleşik yedek ikon seti gerekli.
- İnternetsiz ad için ANCS `GetAppAttributes` (DisplayName). Arama API'si dakikada yaklaşık 20 istek; sıkı önbellek şart.

Kaynak: [Search API](https://performance-partners.apple.com/search-api)

## 15. Güncel sürümler
- .NET 10.0.13 LTS (13 Ekim 2026). .NET 11 RC2, Kasım'da GA. .NET 8 ve 9'un desteği 10 Kasım 2026'da bitiyor.
- Windows App SDK 2.5.1 kararlı (16 Eylül 2026).
- iOS 27 (14 Eylül 2026), A13 ve sonrası. Xcode 27 + Swift 6.4, macOS Tahoe 26.6+ ve Apple silicon istiyor. Nisan 2027'den itibaren App Store yüklemeleri iOS 27 SDK'sı istiyor.
- iOS 26, Haziran 2026'da tüm iPhone'ların %79'unda.

Kaynaklar: [.NET sürümleri](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json), [Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads), [iOS 26 benimseme](https://macrumors.com/2026/06/09/ios-26-adoption-stats-wwdc/), [App Store gönderimleri](https://www.mactech.com/2026/09/10/app-store-submissions-now-open-for-the-latest-os-releases/)

## 16. Rakipler
- **Phone Link (iPhone):** ANCS ile bildirim, MAP ile mesaj, HFP ile arama, PBAP ile rehber. 2025'te Başlat menüsüne pil ve hızlı erişim paneli eklendi. Dosya gönderme "Link to Windows" iOS uygulamasıyla.
- **Intel Unison:** 30 Haziran 2025'te kapandı. iOS uygulamasıyla QR + Bluetooth eşleşme, mesaj, arama, galeri, iki yönlü dosya aktarımı. Her iki uygulama da açık kalmalıydı.
- **Çıkarım:** Phone Link'e yetişmek ANCS + MAP + HFP + PBAP demek. Geçmek ise iOS uygulaması (fotoğraf, dosya, intent'lerle pano, ASK ile kalıcı BLE) + LocalSend + sanal kamera + HID kontrol demek.

Kaynaklar: [9to5mac](https://9to5mac.com/2023/02/28/microsoft-imessage-windows-support/), [Neowin](https://www.neowin.net/news/iphone-users-can-now-interact-with-their-smartphones-from-windows-11s-start-menu/), [TechRadar](https://www.techradar.com/computing/windows/intel-is-discontinuing-an-app-youve-never-heard-of-but-sadly-that-alternative-to-windows-11s-phone-link-was-a-useful-tool-you-might-regret-never-using)
