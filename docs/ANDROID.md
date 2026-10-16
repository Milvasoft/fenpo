# Android Araştırması ve Teknoloji Kararları (16 Ekim 2026)

Fenpo for Android'in fizibilitesi, teknoloji seçimleri ve kod düzeni. Biçim [RESEARCH.md](RESEARCH.md) ile aynı: her başlıkta bir karar, en önemli bilgiler ve kaynaklar. "Cihazda doğrulanacak" yazan yerler gerçek telefonla denenmeden kesinleşmiyor (bkz. [§21](#21-cihazda-doğrulanacaklar)). Kesin olanlar zaten belli: Kotlin, Jetpack Compose, Material 3, Gradle Kotlin DSL, ortak protokol (DECISIONS #10, #11).

Kısaca: Android, iPhone'dan daha çok şeye izin veriyor (bildirimlere yazılı yanıt, arka planda dosya alma, PC'den panoya kendiliğinden yazma, ekranı PC'den kontrol), ama karşılığında her özellik ayrı bir izin, bir kısmı da Ayarlar'da gizli bir anahtar istiyor. Uygulamanın bel kemiği **CompanionDeviceManager ilişkisi + `connectedDevice` ön plan servisi**.

## 1. Cihaz ilişkilendirme: CompanionDeviceManager
**Karar: PC, profilsiz ve tek cihazlı bir CDM BLE ilişkisiyle kaydedilir. PC'nin yaklaşması CDM varlık olaylarıyla izlenir. Windows'un reklam adresinin dönüp dönmediği cihazda doğrulanacak (A1).**
- Akış: `AssociationRequest` + `BluetoothLeDeviceFilter` (PC'nin `PairingResult.ble_service_uuid`'si) + `setSingleDevice(true)` → `associate(request, executor, callback)` (API 33) → `onAssociationPending` sistem penceresini açar → `onAssociationCreated(AssociationInfo)`. Her ilişkide kullanıcı onayı çıkar; bunu atlayan `setSelfManaged` ve `REQUEST_COMPANION_SELF_MANAGED` sistem uygulamalarına ayrılmış.
- CDM taraması `ACCESS_FINE_LOCATION` istemez ama Konum Servisleri açık olmalı. Manifestte `uses-feature android.software.companion_device_setup`.
- **Profil yok.** `DEVICE_PROFILE_COMPUTER` (API 33) tam bizim senaryo ama `REQUEST_COMPANION_PROFILE_COMPUTER` `signature|privileged`, üçüncü taraf alamaz. `WATCH` profilini PC için kullanmak yanlış beyan olur.
- İlişki varken geçerli olan `normal` izinler: `REQUEST_COMPANION_START_FOREGROUND_SERVICES_FROM_BACKGROUND` (Google'ın önerdiği dar izin), `REQUEST_COMPANION_RUN_IN_BACKGROUND` (pil optimizasyonundan muafiyet), `REQUEST_COMPANION_USE_DATA_IN_BACKGROUND`, `REQUEST_OBSERVE_COMPANION_DEVICE_PRESENCE`. `BLUETOOTH_CONNECT`/`SCAN` ayrıca çalışma zamanında istenir.
- **Varlık:** API 33–35'te `startObservingDevicePresence(String)` + `CompanionDeviceService.onDeviceAppeared(AssociationInfo)`; API 36+'da `ObservingDevicePresenceRequest.setAssociationId` + `onDevicePresenceEvent(DevicePresenceEvent)` (`EVENT_BLE_APPEARED/DISAPPEARED`, `ASSOCIATION_REMOVED`). Uygulama çalışmıyorken de sistem servisi bağlar ve süreci korur. `setUuid()` sadece klasik Bluetooth ve sistem izniyle çalışıyor, bize uymuyor. Wi‑Fi cihazlarında varlık yok.
- **Risk:** Varlık, cihazın adresi taranarak bulunuyor; adres dönüyorsa Google bağ (bond) öneriyor. Windows `GattServiceProvider` reklamını hangi adresle yapıyor, bağdan sonra Android onu çözüyor mu: cihazda doğrulanacak (A1). Olmazsa yedek yol: servis UUID filtreli, `PendingIntent`'li arka plan taraması (§4).
- API 37 yenilikleri (`enableSystemDataSyncForTypes`, `FLAG_TASK_CONTINUITY`, `requestDeviceTrustPairing`) `DELIVER_COMPANION_MESSAGES` gibi izinlere bağlı; üçüncü tarafa açık olup olmadıkları belirsiz, Aşama A'da kullanılmıyor.

Kaynaklar: [Companion device pairing](https://developer.android.com/develop/connectivity/bluetooth/companion-device-pairing), [CompanionDeviceManager](https://developer.android.com/reference/android/companion/CompanionDeviceManager), [ObservingDevicePresenceRequest.Builder](https://developer.android.com/reference/android/companion/ObservingDevicePresenceRequest.Builder), [CompanionDeviceService](https://developer.android.com/reference/android/companion/CompanionDeviceService), [DevicePresenceEvent](https://developer.android.com/reference/android/companion/DevicePresenceEvent), [AssociationRequest](https://developer.android.com/reference/android/companion/AssociationRequest), [Manifest.permission](https://developer.android.com/reference/android/Manifest.permission), [AOSP AndroidManifest.xml](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/core/res/AndroidManifest.xml)

## 2. Arka planda ayakta kalma: ön plan servisi ve sürüm kuralları
**Karar: PC yakındayken tek bir `connectedDevice` ön plan servisi çalışır; BLE bağlantısı ve TLS kontrol bağlantısı bu serviste yaşar. Servis CDM varlık olayıyla başlar, PC kaybolunca durur. Samsung ve Xiaomi için kurulumda ek bir pil adımı var.**
- `connectedDevice` (API 34+ zorunlu tür): `FOREGROUND_SERVICE_CONNECTED_DEVICE` + ön koşul izinlerinden biri (`BLUETOOTH_CONNECT`, `CHANGE_NETWORK_STATE` vb.). Bu türde süre sınırı yok. Google'ın kendi belgesi bu tür için CDM varlık API'sini öneriyor.
- Arka plandan FGS başlatma (API 31+) yasak; istisnalar arasında CDM ilişkisi + `REQUEST_COMPANION_START_FOREGROUND_SERVICES_FROM_BACKGROUND` ve pil optimizasyonu muafiyeti var. `BOOT_COMPLETED`'tan `connectedDevice` başlatılabiliyor (Android 15 yasağı dataSync, camera, mediaProjection vb. için).
- `dataSync` kullanılmaz: Android 15'ten beri 24 saatte 6 saat sınırı var.
- Android 16: iş (job) kotaları ön plan servisiyle eş zamanlı çalışan işlere de uygulanıyor; uzun iş WorkManager'a verilmez. Android 17: RAM tabanlı bellek sınırları, arka planda ses için görünürken başlatılmış FGS, `IntentSender` için BAL kısıtı. Yeni bir FGS süre sınırı yok.
- Sürekli bildirim zorunlu. Tasarımda onu işe yarar kılıyoruz: bağlantı durumu + "Panoyu gönder" ve "PC'yi kilitle" eylemleri (`design/android/android.html`).
- **OEM'ler:** CDM'nin OEM'e özel uygulama öldürücülere karşı koruduğuna dair kaynak yok. Samsung kullanılmayan uygulamaları uyutuyor ve güncellemeden sonra listelere geri ekleyebiliyor; Xiaomi'de Autostart ve "Kısıtlama yok" gerekiyor. İzinler ekranında üreticiye göre bir adım ("Uyumayan uygulamalar", "Otomatik başlatma") gösterilir. Samsung ve Xiaomi'de gün boyu bağlantı cihazda denenecek (A2).

Kaynaklar: [FGS türleri](https://developer.android.com/develop/background-work/services/fgs/service-types), [Arka plandan FGS başlatma](https://developer.android.com/develop/background-work/services/fgs/restrictions-bg-start), [Android 15 davranış değişiklikleri](https://developer.android.com/about/versions/15/behavior-changes-15), [Android 16](https://developer.android.com/about/versions/16/behavior-changes-all), [Android 17](https://developer.android.com/about/versions/17/behavior-changes-all), [dontkillmyapp: Samsung](https://dontkillmyapp.com/samsung), [dontkillmyapp: Xiaomi](https://dontkillmyapp.com/xiaomi)

## 3. Yerel ağ izni (Android 17)
**Karar: `ACCESS_LOCAL_NETWORK` bildirilir. Bluetooth izinleriyle aynı "Yakındaki cihazlar" grubunda olduğu için kullanıcıya ayrıca sorulmuyor. İzin yoksa TLS bağlantısı zaman aşımına düşer; bu hata "Yerel ağ izni kapalı" diye gösterilir.**
- Android 16'da isteğe bağlıydı. Android 17'de targetSdk 37 olan uygulamalar için yerel ağ varsayılan olarak kapalı: giden ve gelen TCP, UDP, mDNS ve `NsdManager` kapsamda. `ACCESS_LOCAL_NETWORK` API 37, `dangerous`.
- targetSdk 37 öncesi uygulamalar `INTERNET` ile geçici örtük izin alıyor. Android 16'da önceden denemek için: `adb shell am compat enable RESTRICT_LOCAL_NETWORK com.fenpo.android`.
- Android 17'de targetSdk 37 ile Certificate Transparency varsayılan açık; kendi `TrustManager`'ımızı kullanan bağlantılarda uygulanmıyor (§13).

Kaynaklar: [Local network permission](https://developer.android.com/privacy-and-security/local-network-permission), [Android 17 davranış değişiklikleri](https://developer.android.com/about/versions/17/behavior-changes-17)

## 4. BLE kanalı: Windows `GattServiceProvider`'a merkez olarak bağlanma
**Karar: Çalışıyor; cihazda doğrulanacak (A1, A2). İlk bağlantı ön planda doğrudan (`autoConnect=false`, `TRANSPORT_LE`), bağ kurulduktan sonra bağlantı servisi `autoConnect=true` ile tutar. Bağ, şifreli `rx`/`tx` karakteristiğine erişimle tetiklenir. Yakınlık için RSSI'yi telefon ölçüp kanaldan gönderir.**
- `autoConnect=false` doğrudan bağlanır, kopunca yeniden denemez; `true` cihaz görününce bağlanır ve kopmadan sonra kendisi dener, ama süreç ölünce bağlantı da gider. Bu yüzden bağlantı `connectedDevice` servisinde yaşar, servisi CDM varlık olayı ayakta tutar (§1, §2).
- API 37'de yeni `connectGatt(BluetoothGattConnectionSettings, Executor, Callback)`: `setAutoConnectEnabled`, `setTransport`, `setAutomaticMtuEnabled` (varsayılan açık), `setRssiThresholdDbm`. Eski dört parametreli `connectGatt` 37'de kullanımdan kalktı; 33–36 için o kullanılır.
- **MTU:** Android 14'ten beri ilk `requestMtu` her zaman 517 ister, sonrakileri yok sayar; sonuç min(517, Windows'un MTU'su). Parça boyutu `onMtuChanged`'den (MTU − 3). Windows tarafı `tx` bildiriminde istemci başına `MaxNotificationSize`'a bakmalı.
- API 33 GATT API'leri: `writeCharacteristic(ch, value, writeType)` ve `writeDescriptor` `int` durum kodu döndürüyor (`ERROR_GATT_WRITE_REQUEST_BUSY`), `onCharacteristicChanged` değeri parametre olarak veriyor. Bildirim için CCCD'ye (0x2902) yazmak şart. Bütün GATT işlemleri tek kuyrukta sıralı (S1'deki Control Point dersiyle aynı).
- **Bağ:** Şifreli karakteristiğe ilk erişim `INSUFFICIENT_AUTHENTICATION` ile düşer, Android eşleşmeyi başlatır ve işlemi tekrarlar. `BOND_BONDING` sürerken keşif yapılmaz. `createBond()` sadece yedek; API 37'de `createBond(transport)` ve `getBondStatus(transport)` ile LE ve klasik bağ ayrı sorgulanabiliyor.
- İzinler: `BLUETOOTH_SCAN` (`neverForLocation`), `BLUETOOTH_CONNECT`. Konum izni sadece API 30 ve altında gerekirdi; minSdk 33'te hiç istenmez.
- **Bilinen sorunlar:** 133 hatası (her denemede `close()`, tarama ile bağlanmayı aynı anda yapmamak), `onServiceChanged` (API 31) gelince yeniden keşif. Windows'ta Ayarlar'dan önceden eşleşmiş telefonla GATT bağlantısında eşleşme çakışması bir Intel adaptörde raporlanmış; S4'te birden fazla adaptörle denenmeli.
- **Yedek uyanma yolu:** `BluetoothLeScanner.startScan(filters, settings, PendingIntent)` süreç ölüyken de uyandırıyor; filtre servis UUID'siyle kurulur (dönen adres filtresi `BLUETOOTH_PRIVILEGED` istiyor). PendingIntent'ten FGS başlatmak CDM ilişkisinin FGS istisnasıyla mümkün.
- **Yakınlık kilidi:** DECISIONS #29'daki yol (PC'nin telefonun reklamını dinlemesi) Android'de zayıf: telefon kendiliğinden reklam yapmıyor; Fenpo servisten reklam yapabilir ama adres türünü seçemiyor ve Windows'un bu adresi bağ adresine çözüp çözmediği belirsiz. Öneri: telefon bağlı GATT bağlantısında `readRemoteRssi()` ile ölçer, 5 saniyelik ortalamayı kanaldan gönderir; kural aynı (−80 dBm veya kopma, 30 saniye). Protokolde alan gerekiyor (§20).

Kaynaklar: [BluetoothGatt](https://developer.android.com/reference/android/bluetooth/BluetoothGatt), [BluetoothGattCallback](https://developer.android.com/reference/android/bluetooth/BluetoothGattCallback), [BluetoothGattConnectionSettings.Builder](https://developer.android.com/reference/android/bluetooth/BluetoothGattConnectionSettings.Builder), [BluetoothDevice](https://developer.android.com/reference/android/bluetooth/BluetoothDevice), [BluetoothLeScanner](https://developer.android.com/reference/android/bluetooth/le/BluetoothLeScanner), [Arka planda BLE](https://developer.android.com/develop/connectivity/bluetooth/ble/background), [Bluetooth izinleri](https://developer.android.com/develop/connectivity/bluetooth/bt-permissions), [Android 14 davranış değişiklikleri](https://developer.android.com/about/versions/14/behavior-changes-all), [Windows GATT sunucusu](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-server), [Microsoft Q&A 848196](https://learn.microsoft.com/en-us/answers/questions/848196/(ble)-gatt-connection-failed-while-doing-ble-pair), [AOSP le_advertising_manager.cc](https://android.googlesource.com/platform/packages/modules/Bluetooth/+/refs/heads/main/system/gd/hci/le_advertising_manager.cc), [van Welie: Making Android BLE work](https://habr.com/ru/post/539740)

## 5. Klasik Bluetooth: HFP, PBAP, MAP
**Karar: Windows'taki MAP, PBAP ve HFP istemci kodu Android'de de kullanılır. HFP ve PBAP standart; MAP üreticiye bağlı, telefon sunmuyorsa SMS Fenpo kanalından gelir (`CAPABILITY_MESSAGES`). Eşleşme Android uygulamasından başlar; klasik bağ LE bağından türetilmezse uygulama ayrıca kurar. Cihazda doğrulanacak (A4).**
- AOSP'de profiller derleme zamanında açılıyor (`bluetooth.profile.map.server.enabled`, `pbap.server.enabled`, `hfp.ag.enabled`); **MAP sunucusu varsayılan olarak kapalı**, açmak üreticiye kalmış. Pixel 6 ve 8 cihaz ağaçlarında üçü de açık. Samsung'un kendi yığını var, MAP desteği resmi olarak belgelenmemiş; Sony M4 Aqua'da MAP yok. Test telefonunda: `adb shell getprop bluetooth.profile.map.server.enabled` ve Windows'tan SDP sorgusu.
- AOSP MAP MSE: MAS 1.4, SMS ve MMS, bildirim (MNS), listeleme ve `pushMessage` (SmsManager ile gönderiyor). iPhone'dan farkı: gönderme gerçekten çalışıyor olmalı. RCS sohbetleri SMS sağlayıcısında olmadığı için MAP'te de görünmez (§7).
- PBAP rehberi ve **arama geçmişini** sunuyor. Android eşleşme penceresindeki "Kişilere ve arama geçmişine erişime izin ver" kutusu buna izin veriyor; sonradan sorulursa ayrı bir pencere çıkıyor. Bu yüzden Fenpo `READ_CALL_LOG` istemiyor. MAP için de ilk bağlantıda "Mesajlara erişim" penceresi çıkıyor.
- HFP: `PhoneLineTransportDevice` herhangi bir Bluetooth HFP AG ile çalışmalı; Phone Link'in Android aramaları da Bluetooth üzerinden. Phone Link Android'de mesajları MAP ile değil kendi uygulamasıyla alıyor: MAP'in güvenilmezliğinin bir işareti.
- **Eşleşme akışı:** QR ve TLS → CDM ilişkisi → şifreli karakteristik ile LE Secure Connections bağı. AOSP, iki taraf da isterse LE anahtarından klasik anahtarı türetiyor (CTKD). Windows'un CTKD davranışı belgelenmemiş. Klasik bağ oluşmadıysa Fenpo, PC'nin klasik adresine `createBond(TRANSPORT_BREDR)` (API 37; öncesinde `createBond()`) çağırır. Windows Ayarlar'dan eşleşme son çare. Pixel ve Samsung'da kaç pencere çıktığı A4'te sayılacak.

Kaynaklar: [AOSP Bluetooth: MAP, PBAP, Config](https://android.googlesource.com/platform/packages/modules/Bluetooth/+/refs/heads/main/android/app/src/com/android/bluetooth/), [AOSP smp_act.cc (CTKD)](https://android.googlesource.com/platform/packages/modules/Bluetooth/+/refs/heads/main/system/stack/smp/smp_act.cc), [Pixel 8 device.mk](https://android.googlesource.com/device/google/zuma/+/refs/heads/main/device.mk), [Pixel 6 device.mk](https://android.googlesource.com/device/google/gs101/+/refs/heads/main/device.mk), [PhoneLineTransportDevice](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice), [Phone Link aramaları](https://support.microsoft.com/en-us/topic/make-and-receive-phone-calls-from-your-pc-21564230-abf6-f2bb-c7b7-1b15570662f5), [Phone Link sorun giderme](https://support.microsoft.com/en-us/windows/apps/phonelink/troubleshooting-the-phone-link)

## 6. Bildirim aynalama: NotificationListenerService ve yazılı yanıt
**Karar: Çalışıyor, iPhone'un yapamadığı yazılı yanıt dahil. Tek kullanımlık kod içeren bildirimlerin içeriği büyük ihtimalle Fenpo'ya gelmeyecek; arayüz bunu açıkça söylüyor.**
- İzin: CDM ilişkisinden sonra `CompanionDeviceManager.requestNotificationAccess(ComponentName)`; olmazsa `ACTION_NOTIFICATION_LISTENER_DETAIL_SETTINGS` (API 30) ile doğrudan Fenpo'nun anahtarı açılır. Kontrol `NotificationManager.isNotificationListenerAccessGranted()`.
- **Kısıtlanmış ayarlar:** Android 13'ten beri mağaza dışından (APK) kurulan uygulamalarda bildirim erişimi ve erişilebilirlik kilitli; Android 15'te liste genişledi. Kullanıcı önce "Kısıtlanmış ayar" uyarısını görür, sonra Ayarlar > Uygulamalar > Fenpo > ⋮ > "Kısıtlanmış ayarlara izin ver" ve kilit doğrulaması. CDM bunu aşmıyor (belge: "Side-loaded apps must allow restricted settings before requesting notification access"). Play ve F-Droid istemcisi gibi oturum tabanlı kurulum yapan mağazalardan kurulanlar muaf. Tasarımdaki İzinler ekranı bu adımı anlatıyor.
- İçerik: `extras` içinden `EXTRA_TITLE`, `EXTRA_TEXT`, `EXTRA_BIG_TEXT`, `MessagingStyle` (`NotificationCompat.MessagingStyle.extractMessagingStyleFromNotification`), simgeler `getSmallIcon`/`getLargeIcon`, uygulama adı ve simgesi `PackageManager`'dan. Kapatma `cancelNotification(key)`; süregelen bildirimler kapatılamaz.
- **Yanıt:** Bildirimdeki `RemoteInput`'lu eylem bulunur, `RemoteInput.addResultsToIntent` ile doldurulur, `actionIntent.send(context, 0, intent)`. Bu intent'ler çoğunlukla broadcast veya servis; BAL kısıtları sadece aktivite başlatanlara uygulanıyor. Ama PC'den "telefonda aç" (`contentIntent`) telefon arka plandayken büyük ihtimalle engellenir (API 34+ BAL, Android 17'de `IntentSender` de). Bu düğme Android'de sunulmaz.
- **Tek kullanımlık kodlar (Android 15+):** Güvenilmeyen dinleyiciler OTP algılanan bildirimin sansürlü halini alıyor. Belge "companion device manager associations are exempt" diyor; ama basın ve izin tanımı muafiyetin `RECEIVE_SENSITIVE_NOTIFICATIONS` iznine bağlı olduğunu, bu iznin de üretici imzalı, varsayılan başlatıcı veya saat ve bilgisayar profilli eşlik uygulamalarına verildiğini gösteriyor. Fenpo bilgisayar profilini alamadığı için **muaf olmadığını varsayıyoruz**; profilsiz ilişkiyle cihazda denenecek (A5).
- Play: bildirim dinleyiciye özel beyan formu yok; genel hassas veri kuralları (belirgin açıklama, gizlilik politikası).

Kaynaklar: [NotificationListenerService](https://developer.android.com/reference/android/service/notification/NotificationListenerService), [RemoteInput](https://developer.android.com/reference/android/app/RemoteInput), [Settings](https://developer.android.com/reference/android/provider/Settings), [Arka planda aktivite başlatma](https://developer.android.com/guide/components/activities/background-starts), [Android 15 (OTP sansürü)](https://developer.android.com/about/versions/15/behavior-changes-all), [Kısıtlanmış ayarlar](https://support.google.com/android/answer/12623953), [Android Authority: hassas bildirimler](https://www.androidauthority.com/android-15-sensitive-notifications-3416414), [Android Authority: Phone Link](https://www.androidauthority.com/phone-link-sensitive-notifications-fix-3540158)

## 7. SMS ve arama geçmişi, Play politikası
**Karar: SMS önce klasik Bluetooth MAP ile denenir (§5); MAP'i olmayan telefonda Fenpo kanalıyla gelir (`CAPABILITY_MESSAGES`). Play'de "Cross-device synchronization or transfer of SMS or calls" istisnasıyla başvurulur. Arama geçmişi için izin istenmez, PBAP ile gelir. RCS yok.**
- Play, SMS ve arama kaydı izinlerini varsayılan işleyici olmayan uygulamalara sadece istisnalarla veriyor; istisnalar incelemeye bağlı ve "başka yol yoksa". **Cross-device sync:** `READ_SMS`, `RECEIVE_SMS`, `RECEIVE_MMS`, `RECEIVE_WAP_PUSH`, `SEND_SMS`, `READ_CALL_LOG` (`WRITE_*` yok). Permissions Declaration Form zorunlu.
- **Android 17, targetSdk 37:** OTP içeren SMS'ler diğer uygulamalara 3 saat gecikmeli görünüyor ("connected device companion apps" muaf, nasıl belirlendiği belgede yok). Yani PC'de doğrulama kodu SMS'i büyük ihtimalle görünmez; bildirimdeki OTP sansürüyle aynı sonuç.
- **RCS:** SMS/MMS sağlayıcısında yok; Samsung ve Google RCS'yi üçüncü taraflara kapatıyor. Phone Link da RCS göstermiyor. Mesajlar ekranında "RCS sohbetleri burada görünmez, telefondan yanıtla" denir.
- F-Droid ve GitHub sürümünde bu kısıt yok; Play sürümünde SMS özelliği beyan onaylanana kadar kapalı tutulabilir (§19).

Kaynaklar: [Play: SMS ve Call Log izinleri](https://support.google.com/googleplay/android-developer/answer/10208820), [Play: istisnalar](https://support.google.com/googleplay/android-developer/answer/16585319), [Android 17](https://developer.android.com/about/versions/17/behavior-changes-17), [Samsung: Phone Link mesajları](https://www.samsung.com/us/support/answer/ANS10001476/)

## 8. Pano
**Karar: PC → telefon kendiliğinden (arka planda `setPrimaryClip` çalışıyor). Telefon → PC tek dokunuş: Hızlı Ayarlar kutucuğu, sürekli bildirimdeki "Panoyu gönder" ve paylaşım menüsü. Üçü de odak alan küçük, şeffaf bir aktivite açıp panoyu orada okur.**
- Android 10+: panoyu sadece varsayılan klavye veya odaktaki uygulama okuyabilir. Android 12: okumada "Fenpo panodan yapıştırdı" bildirimi çıkar. Android 13: kopyalamada sistem önizlemesi; hassas içerik `ClipDescription.EXTRA_IS_SENSITIVE` ile işaretlenir. PC'den gelen şifre için de uygulanır (Windows panosundaki "geçmişe ekleme" işaretinden gelir; protokolde alan gerekiyor, §20).
- Yazmada belgelenmiş bir arka plan kısıtı yok; KDE Connect aynı yolu kullanıyor.
- `TileService.startActivityAndCollapse(Intent)` API 34'te kaldırıldı, `PendingIntent` sürümü kullanılır. Kutucuk kendi başına odak almadığı için okuyamaz, aktivite şart.
- iPhone'dan farkı: "PC'den yapıştır" ayrı bir iş değil, panoya zaten gelmiş oluyor. Ana ekrandaki kutu son gelen içeriği gösterir.

Kaynaklar: [Android 10 gizlilik](https://developer.android.com/about/versions/10/privacy/changes), [Kopyala ve yapıştır](https://developer.android.com/develop/ui/views/touch-and-input/copy-paste), [ClipboardManager](https://developer.android.com/reference/android/content/ClipboardManager), [TileService](https://developer.android.com/reference/android/service/quicksettings/TileService), [KDE Connect MR 145](https://invent.kde.org/network/kdeconnect-android/-/merge_requests/145)

## 9. Paylaşım hedefi ve arka planda dosya alma
**Karar: Her eşleşmiş PC bir doğrudan paylaşım kısayolu olur ("BUGRA-PC"). Ayrıca iki paylaşım hedefi: "PC'ye gönder" ve "PC'de aç". Dosya alma bağlantı servisinin içinde yapılır, dosya izinsiz olarak `MediaStore.Downloads/Fenpo`'ya yazılır.**
- Doğrudan paylaşım API 30'dan beri sadece Sharing Shortcuts ile: `shortcuts.xml` içinde `<share-target>`, `ShortcutManagerCompat.pushDynamicShortcut`, `setLongLived(true)`, `setCategories`. Seçim `EXTRA_SHORTCUT_ID` ile gelir; hedef doğrudan gönderir, arada seçim ekranı olmaz.
- Gönderim ve alma `connectedDevice` servisinin içinde; `dataSync`'in 6 saat sınırına ve `BOOT_COMPLETED` yasağına takılmaz.
- `MediaStore.Downloads` (API 29) uygulamanın kendi eklediği dosyalar için izin istemiyor (`IS_PENDING` deseni).
- İlerleme: API 36+ `Notification.ProgressStyle`, altında standart ilerleme çubuğu. Kullanıcının başlattığı aktarımlarda Live Update (durum çubuğu çipi) istenir: `setRequestPromotedOngoing`, `POST_PROMOTED_NOTIFICATIONS` (36.1), kısa metin `setShortCriticalText`. Tasarımdaki "%64" çipi bu.

Kaynaklar: [Doğrudan paylaşım](https://developer.android.com/training/sharing/direct-share-targets), [Live Updates](https://developer.android.com/develop/ui/views/notifications/live-update), [Notification.ProgressStyle](https://developer.android.com/reference/android/app/Notification.ProgressStyle), [MediaStore.Downloads](https://developer.android.com/reference/android/provider/MediaStore.Downloads)

## 10. Telefonu web kamerası yapma
**Karar: Fenpo'nun kendi hattı Wi‑Fi üzerinden: CameraX `Preview` → `MediaCodec` giriş yüzeyi → H.264/HEVC → TLS. Kamerayı başlatmak için telefonda bir dokunuş gerekiyor. USB için kendi taşımamızı yazmıyoruz; telefon destekliyorsa Android'in yerleşik USB web kamerası modu önerilir.**
- **Uygulamada Camera2 kullanıldı (16 Ekim):** aynı kare hem kodlayıcıya hem kamera ekranındaki önizlemeye gitmeli; CameraX `Preview` kareyi tek yüzeye veriyor, Camera2 bir oturumda iki çıktıyı doğrudan destekliyor. Yakınlaştırma `CONTROL_ZOOM_RATIO`, "Ortala" yüz algılamayla `SCALER_CROP_REGION`. Aşağıdaki CameraX notu karar anındaki araştırma.
- CameraX 1.6.2 kararlı (Ağustos 2026). `VideoCapture` dosyaya mux'lanmış çıktı veriyor, `ImageAnalysis` kareyi CPU'ya kopyalıyor; ikisi de akış için yanlış. Doğrusu `Preview` + `SurfaceProvider`'ın `MediaCodec.createInputSurface()` döndürmesi; vizör ve kodlayıcıya aynı kare için `CameraEffect` denenecek.
- Kodlayıcı: `KEY_LOW_LATENCY` sadece çözücü içindir. Kodlayıcıda `KEY_LATENCY`, `KEY_MAX_B_FRAMES=0`, `KEY_PREPEND_HEADER_TO_SYNC_FRAMES`, `KEY_INTRA_REFRESH_PERIOD` (destek varsa), `KEY_REPEAT_PREVIOUS_FRAME_AFTER`; bitrate modu CBR (`isBitrateModeSupported` ile).
- **Kamera FGS:** `camera` türü "kullanımdayken" izni; Android 14+ arka plandan başlatılırsa `SecurityException`. İstisnalar bildirim etkileşimi ve görünür aktivite; CDM yok. Yani PC kamerayı isteyince telefonda "Kamerayı başlat" bildirimi çıkar (tasarımda yazıyor). Başladıktan sonra arka planda sürebiliyor; ekran kapalıyken davranış üreticiye göre değişebilir (A6).
- **Yerleşik USB web kamerası (Android 14 QPR1+):** Üretici açmışsa (Pixel 6+, Galaxy S26) USB tercihlerinden "Web kamerası" seçilir, Windows standart UVC görür. Uygulama bu modu açamaz. Fenpo'nun sanal kamerası devreye girmez; kablolu en düşük gecikme için yönlendirme olarak sunulur.
- **USB taşıma:** `adb forward` geliştirici modu istiyor; AOA Windows'ta WinUSB sürücüsü istiyor (MSIX ile zor); USB tethering her seferinde elle açılıyor ve PC'nin internetini telefona yönlendiriyor. Üçü de ürüne uygun değil. Bu yüzden ana ekrandaki bağlantı çiplerinde Android için USB yok.

Kaynaklar: [CameraX sürümleri](https://developer.android.com/jetpack/androidx/releases/camera), [MediaFormat](https://developer.android.com/reference/android/media/MediaFormat), [EncoderCapabilities](https://developer.android.com/reference/android/media/MediaCodecInfo.EncoderCapabilities), [FGS türleri](https://developer.android.com/develop/background-work/services/fgs/service-types), [Webcam (AOSP)](https://source.android.com/docs/core/camera/webcam), [Pixel web kamerası](https://support.google.com/pixelcamera/answer/14274129), [AOA](https://source.android.com/docs/core/interaction/accessories/aoa), [scrcpy SSS](https://github.com/Genymobile/scrcpy/blob/master/FAQ.md)

## 11. Ekran yansıtma ve PC'den kontrol
**Karar: Mümkün (Aşama 4). MediaProjection → VirtualDisplay → MediaCodec → TLS; kontrol `AccessibilityService.dispatchGesture` ve `performGlobalAction`, metin `getInputMethod().commitText` (API 33). Her oturum telefonda bir onay istiyor, telefon kilitlenince yansıtma duruyor.**
- MediaProjection: her oturumda kullanıcı onayı; API 34+'te onay intent'i veya `MediaProjection` ikinci kez kullanılırsa `SecurityException`. `mediaProjection` FGS türü, onaydan sonra başlatılır. Kullanıcı tek uygulama paylaşımını da seçebilir.
- Android 15: paylaşım sırasında bildirim içeriği, şifre alanları ve OTP gösteren uygulamalar gizlenir. 15 QPR1+: durum çubuğunda büyük çip, **ekran kilitlenince yansıtma durur**. Kilitli telefona PC'den erişim yok. `FLAG_SECURE` pencereler siyah görünür.
- Erişilebilirlik: `isAccessibilityTool=false` (Fenpo bir erişilebilirlik aracı değil). Play için uygulama içinde ayrı ve onay isteyen bir açıklama, Play Console beyanı ve akışı gösteren video gerekiyor; kullanıcının yönettiği uzaktan kontrol açıkça yasak değil, açıkça izinli de değil (TeamViewer ve AnyDesk emsal). Mağaza dışı kurulumda kısıtlanmış ayar adımı (§6).
- Android 16'da `accessibilityDataSensitive` işaretli görünümler araç olmayan servislere kapalı (bankacılık ekranları PC'den kontrol edilemez). Android 17 Gelişmiş Koruma açıkken araç olmayan erişilebilirlik servisleri kapatılıyor (basın, resmi sayfada doğrulanamadı).
- Gecikme: scrcpy 35–70 ms diyor ama `adb shell` ayrıcalığıyla çalışıyor; Fenpo ancak yaklaşabilir.

Kaynaklar: [MediaProjection](https://developer.android.com/media/grow/media-projection), [Android 14](https://developer.android.com/about/versions/14/behavior-changes-14), [Android 15](https://developer.android.com/about/versions/15/behavior-changes-all), [AccessibilityService](https://developer.android.com/reference/android/accessibilityservice/AccessibilityService), [Play: erişilebilirlik API'si](https://support.google.com/googleplay/android-developer/answer/10964491), [Android blog: veri gizliliği](https://developer.android.com/blog/posts/enhancing-android-security-stop-malware-from-snooping-on-your-app-data), [scrcpy](https://github.com/Genymobile/scrcpy)

## 12. En düşük sürüm (minSdk)
**Karar: `minSdk 31` (Android 12), `targetSdk 37`, `compileSdk 37`. İlk karar 33'tü; test telefonu Galaxy S10+ Android 12'de kaldığı ve güncellenmediği için Buğra 31'e indirdi (16 Ekim).**
- Android 12 için ayrı yollar: GATT'ta eski yazma ve bildirim geri çağrıları, CDM'de sonucun pencerenin activity sonucundan okunması (Android 12'de `AssociationInfo` ve ilişki kimliği yok, PC adresle tanınıyor), varlık servisinde adresli geri çağrılar, `POST_NOTIFICATIONS` sadece 13+'ta istenir. Android 12 CDM'i Activity bağlamı istiyor; uygulama bağlamıyla `associate` çöküyor (cihaz testi). Bu telefon Android 13+ yolunu denemiyor; onun için ayrıca bir telefon gerekiyor.
- Güncel sürüm Android 17 (API 37), Pixel'de 16 Haziran 2026'da kararlı. Play, 31 Ağustos 2026'dan beri yeni uygulama ve güncellemelerde targetSdk 36 istiyor.
- Dağılım (kümülatif): Google'ın Android Studio verisi (Aralık 2025) ≥31 %69, ≥33 %58; apilevels.com (Statcounter, Nisan 2026) ≥31 %79, ≥33 %69, ≥34 %55. Fenpo for Android en erken 2027'de yayımlanacağı için ≥33 o zaman %75 civarında olur.
- **33 neden:** CDM'nin kullanışlı yüzeyi (`AssociationInfo`, executor'lı `associate`, `disassociate(int)`, `onDeviceAppeared(AssociationInfo)`) 33'te geliyor; `POST_NOTIFICATIONS` ve uygulama başına dil sistem ayarı 33'te. 31–32 için ikinci bir CDM ve bildirim yolu yazmak gerekirdi. Kısıtlanmış ayarlar zaten 13'ten itibaren var, yani İzinler ekranı tek tip.
- **Alternatif 31 (Android 12):** +%10 erişim; yeni Bluetooth izin modeli ve FGS kuralları 31'de başlıyor. Bedeli: eski CDM varlık API'si (`String` adresli) ve Android 12 için ayrı izin akışı. TLS 1.3 29'dan beri var, yani 29–30 de teknik olarak mümkün ama Bluetooth izinleri konum iznine bağlı; önermiyorum.

Kaynaklar: [Android 17 sürüm notları](https://developer.android.com/about/versions/17/release-notes), [Play target SDK](https://developer.android.com/google/play/requirements/target-sdk), [Android Studio dağılım verisi](https://dl.google.com/android/studio/metadata/distributions.json), [apilevels.com](https://apilevels.com/)

## 13. TLS ve kimlik
**Karar: Telefonun kimliği Android Keystore'da (TEE) üretilen, dışarı çıkarılamayan bir ECDSA P-256 anahtarı ve kendinden imzalı sertifika. TLS 1.3, karşılıklı sertifika sabitleme; BouncyCastle gerekmiyor.**
- `KeyPairGenerator.getInstance("EC", "AndroidKeyStore")`, `KeyGenParameterSpec.Builder(alias, PURPOSE_SIGN)`: `secp256r1`, `setDigests(DIGEST_NONE, DIGEST_SHA256)`, `setCertificateSubject`, `setCertificateSerialNumber`, `setCertificateNotBefore/After` (varsayılanları `CN=fake`, 1970–2048). Keystore sertifikayı kendisi imzalıyor; bunun için kullanıcı doğrulaması ve attestation istenmez.
- **Tuzak:** Conscrypt, çıkarılamayan anahtarla el sıkışmayı `NONEwithECDSA` ile imzalıyor; anahtarda `DIGEST_NONE` yoksa el sıkışma düşer.
- `SSLContext.getInstance("TLSv1.3")`, kendi `X509ExtendedKeyManager`'ımız (Keystore'dan anahtar ve zincir) ve `X509ExtendedTrustManager` (sertifikanın SHA-256'sı QR'daki `fp` ile `MessageDigest.isEqual`). Ağ güvenlik yapılandırması ve CT buna karışmıyor.
- Keystore sertifikasında SAN ve EKU ayarlanamıyor. Windows Schannel istemci sertifikasında bunları isterse sertifika bcpkix (MIT) ile Keystore anahtarı üzerinden yeniden imzalanır; S5 sırasında Windows tarafıyla birlikte denenecek (A3, emülatörde de yapılabilir).
- StrongBox kullanılmaz: yavaş, her cihazda yok, `DIGEST_NONE` desteği belirsiz.
- Keystore anahtarları yedeklenmiyor; kimlik ve eşleşme verisi Auto Backup'tan çıkarılır (`dataExtractionRules`). Telefon değişince yeniden eşleşilir.
- BLE kanalının AES-GCM'i: `javax.crypto` `AES/GCM/NoPadding`, anahtar `PairingResult.ble_channel_key`.

Kaynaklar: [KeyGenParameterSpec](https://raw.githubusercontent.com/aosp-mirror/platform_frameworks_base/main/keystore/java/android/security/keystore/KeyGenParameterSpec.java), [Conscrypt CryptoUpcalls](https://raw.githubusercontent.com/google/conscrypt/master/common/src/main/java/org/conscrypt/CryptoUpcalls.java), [Network security config](https://developer.android.com/privacy-and-security/security-config)

### Eşleşme akışı (Aşama C, uygulandı)
- Protokol `e185f5d` ile uyumlu: BLE mühürlü mesajı `nonce (12) || ciphertext || tag`, en fazla 4096 bayt; `Hello` `os_version` ve `model` taşıyor; `PcStatus` bağlantı durumuna yazılıyor; `PairingRequest.certificate_sha256` ve `PairingResult.ble_service_uuid` artık `reserved`.
- QR kamerayla (CameraX + zxing-cpp) okunur. **Elle kod girişi yok** (Buğra'nın kararı, 16 Ekim): S5'e göre parmak izi taşımayan 6 haneli kod, araya giren saldırgana karşı koruma sağlamıyor. Windows ve iPhone tarafının da aynı kararı alması gerekiyor; DECISIONS.md'ye eklenmeli.
- Telefon QR'daki adresleri sırayla dener; sertifika parmak izi tutmazsa diğer adresleri denemeden durur. `PairingRequest` gönderir, `certificate_sha256` alanını boş bırakır (S5 önerisi 1: PC parmak izini TLS'ten alır). `PairingResult` kabul edilirse aynı bağlantıda `Hello` alışverişi yapılır (S5 önerisi 2) ve yetenekler kesişimle belirlenir.
- PC'nin kimliği, sertifika parmak izi, adresleri ve BLE servis UUID'si DataStore'a; BLE kanal anahtarı Keystore'daki AES-GCM anahtarıyla şifrelenip yazılır.
- Kontrol bağlantısında zarf sınırı PC ile aynı: 1 MB.

### Wi‑Fi kanalı (Aşama C, uygulandı)
- `ConnectionService` iki yolu yan yana çalıştırır: BLE (③) ve Wi‑Fi TLS (④). Biri açıkken telefon bağlı sayılır. Her bağlantı `Hello` ile başlar; PC'nin `Hello`'su gelince 30 saniyede bir `Ping` atılır, 10 saniyede `Pong` gelmezse yol yeniden kurulur.
- Wi‑Fi yolu Wi‑Fi veya Ethernet ağı varken çalışır (internet şartı yok), soket o ağa bağlanır. Sırayla saklı adresler denenir, hiçbiri cevap vermezse 8 saniye DNS-SD (`_fenpo._tcp`, TXT `id` = PC kimliği). Cevap veren adres listenin başına yazılır. Ulaşılamazsa 5 saniyeden 5 dakikaya kadar artan aralıkla yeniden denenir; ağ değişince ve PC BLE üzerinden `DeviceState.addresses` gönderince hemen. PC `ERROR_CODE_NOT_PAIRED` derse Wi‑Fi yolu durur.
- Telefon `CAPABILITY_DEVICE_STATE` sunar ve PC de sunuyorsa `DeviceState`'i (pil, şarj, Rahatsız Etmeyin) `Hello`'dan sonra ve her değişiklikte en iyi yoldan gönderir: Wi‑Fi, yoksa BLE.
- Kalıcı bildirim yolu söyler: "Bağlı, Bluetooth", "Bağlı, Wi‑Fi", "Bağlı, Bluetooth ve Wi‑Fi".

## 14. Kütüphaneler ve sürümler
**Karar: aşağıdaki tablo. Dış bağımlılık sadece özgür lisanslı (F-Droid şartı); Google Play Services, Firebase, ML Kit yok.**

| Konu | Seçim | Neden |
|---|---|---|
| Kotlin | 2.4.21 | Güncel kararlı (8 Ekim 2026) |
| Derleme | AGP 9.4.1, Gradle 9.7.1, JDK 21 (hedef 17) | AGP 9 Kotlin'i kendi içinde getiriyor: `org.jetbrains.kotlin.android` ve kapt yok, KSP var. KGP 2.4.21 Gradle 9.7'ye kadar tam destekli |
| UI | Compose BOM 2026.09.00, Material 3 1.4.0 | Material 3 Expressive (1.5.0) henüz beta; kararlı olunca geçilir |
| Gezinme | Navigation 3 1.2.0 | Kararlı; ekran yığını uygulamanın kendi durumu, Compose ile doğal |
| Eşzamanlılık | kotlinx.coroutines 1.11.0, Flow | Standart |
| DI | Hilt (Dagger 2.60.1, KSP) | En olgun; `@AndroidEntryPoint` servislerde de çalışıyor. Metro hızlı ama her Kotlin sürümünü bekletebilir; Koin'in derleme zamanı denetimi henüz RC |
| Protokol | Square Wire 7.1.0 | Saf JVM, `protoc` ikilisi indirmiyor (F-Droid). `../protocol` klasörünü doğrudan okuyor. `oneof` için `sealed_class` modu: `Envelope.body` üzerinde tam `when` |
| Ayarlar ve eşleşmeler | DataStore 1.2.1 | Küçük veri; Room ancak aktarım geçmişi gerekince |
| Kamera | CameraX 1.6.2 | QR ve web kamerası |
| QR | zxing-cpp Android 3.1.1 | Aktif, ekrandaki QR'da dayanıklı. Yedek: `com.google.zxing:core` 3.5.4 (saf Java). zxing-android-embedded 2021'den beri durgun; ML Kit kapalı kaynak |
| Dil | AppCompat 1.8.0 | `setApplicationLocales` + `autoStoreLocales`; API 33+'te sistemin `LocaleManager`'ı |
| TLS, kripto | Platform (Conscrypt, Keystore, `javax.crypto`) | Ek bağımlılık yok |
| Test | JUnit (saf JVM modüllerde JUnit Platform + `kotlin.test`, Android modüllerinde JUnit 4), kotlinx-coroutines-test, Turbine 1.2.1, Robolectric 4.17, Roborazzi 1.76.0 | Roborazzi, Robolectric niteleyicileriyle (`+night`, `tr`) her ekranı iki tema ve iki dilde fotoğraflar: CLAUDE.md'deki "tema ve dil her ekranda" kuralının testi |
| Biçim | Spotless 8.10.4 + ktlint 1.8.0 (`ktlint_official`) + compose-rules 0.6.8, Android Lint | detekt 2.0 hâlâ alpha |

Kaynaklar: [Kotlin sürümleri](https://kotlinlang.org/docs/releases.html), [AGP sürüm notları](https://developer.android.com/build/releases/gradle-plugin), [Yerleşik Kotlin'e geçiş](https://developer.android.com/build/migrate-to-built-in-kotlin), [Gradle](https://gradle.org/releases/), [BOM eşlemesi](https://developer.android.com/develop/ui/compose/bom/bom-mapping), [Material 3 sürümleri](https://developer.android.com/jetpack/androidx/releases/compose-material3), [Navigation 3](https://developer.android.com/jetpack/androidx/releases/navigation3), [Wire](https://square.github.io/wire/), [Wire CHANGELOG](https://github.com/square/wire/blob/master/CHANGELOG.md), [Dagger](https://github.com/google/dagger/releases), [Metro uyumluluk](https://zacsweers.github.io/metro/latest/compatibility/), [Koin](https://github.com/InsertKoinIO/koin/releases), [zxing-cpp Android](https://github.com/zxing-cpp/zxing-cpp/tree/master/wrappers/android), [ML Kit barkod](https://developers.google.com/ml-kit/vision/barcode-scanning/android)

## 15. Modül yapısı
**Karar: Now in Android'in sadeleştirilmiş hali, `build-logic` konvansiyon eklentileriyle. Protokol, çerçeveleme ve eşleşme mantığı saf JVM modüllerinde; Android'siz, hızlı test edilir (iOS'taki `FenpoKit` çekirdeğinin karşılığı).**

```
android/
├── settings.gradle.kts, build.gradle.kts, gradle/libs.versions.toml
├── build-logic/convention/     fenpo.android.application, .library, .compose, fenpo.jvm.library, fenpo.hilt
├── app/                        MainActivity (AppCompatActivity), Nav3 NavDisplay, Hilt kökü, manifest, kaynaklar
├── core/
│   ├── protocol/      (JVM)    Wire ile ../protocol'den üretilen tipler, Envelope yardımcıları
│   ├── transport/     (JVM)    [uint32 BE][Envelope] çerçeveleme, oturum ve Hello durum makinesi, BLE parçalama
│   ├── pairing/       (JVM)    fenpo://pair ayrıştırma, eşleşme durum makinesi
│   ├── security/               Keystore kimliği, KeyManager, sabitleyen TrustManager, AES-GCM
│   ├── ble/                    GATT istemcisi, bağ, MTU, yeniden bağlanma
│   ├── connection/             CDM, connectedDevice servisi; BLE + TLS bağlantılarını yönetir; özellikler `PcFeature` ile takılır
│   ├── media/                  (Aşama D'de eklendi) H.264 kodlayıcı, ses yakalama, görüntü bağlantısı göndericisi
│   ├── data/                   DataStore: ayarlar (tema, dil, özellikler), eşleşmiş PC'ler
│   ├── designsystem/           FenpoTheme, renk rolleri, ortak bileşenler
│   └── testing/                sahteler, test dispatcher'ları, fixture yükleyici
└── feature/
    ├── home/                   ana ekran
    ├── pairing/                QR tarayıcı, elle kod, eşleşme adımları, izinler
    ├── transfers/              aktarımlar, paylaşım hedefleri
    ├── settings/               ayarlar, dil, bildirim gönderen uygulamalar
    ├── camera/                 (Aşama D'de eklendi) kamera ekranı, kamera servisi
    └── mirror/                 (Aşama D'de eklendi) ekran yansıtma, PC'den kontrol (erişilebilirlik servisi)
```

- Bağımlılık yönü: `feature → core`, `app → feature + core`. Özellik modülleri birbirine bağlanmaz; Nav3 anahtarları her özelliğin kendi modülünde.
- Sonraki aşamalarda eklenecekler: `feature:camera`, `feature:mirror`, `core:notifications` (bildirim aynalama), `core:messages`. `core:ui` ihtiyaç doğarsa açılır.
- Paket adı `com.fenpo.android` (protokol `com.fenpo.protocol.v1` ile aynı kök). Alan adı çakışırsa DECISIONS #1'deki yedek ad geçerli.

Kaynaklar: [Now in Android](https://github.com/android/nowinandroid)

## 16. Kotlin kod stili ve test (CLAUDE.md'ye işlenecek öneri)
**Karar: Resmi Kotlin kodlama kuralları + Android Kotlin stil rehberi, `ktlint_official` ile zorlanır.**
- Satır sonu LF, girinti 4 boşluk, satır uzunluğu 120. `android/.editorconfig` ktlint ayarlarını taşır (kökteki `.editorconfig`'e dokunmadan; oraya `[*.{kt,kts}]` eklemek istersek önce Buğra'ya sorulur).
- Core modüllerde `explicitApi()`. Public API'lerde İngilizce KDoc. Kod, log ve yorumlar İngilizce.
- Bağımlılıklar constructor injection ile, `private val` olarak alınır (C#'taki "primary constructor + alan" deseninin Kotlin karşılığı):

  ```kotlin
  /**
   * Keeps the Fenpo BLE channel to the paired PC open and reconnects when it drops.
   */
  class BleChannel @Inject constructor(
      private val gattClient: GattClient,
      private val crypto: ChannelCrypto,
      @IoDispatcher private val dispatcher: CoroutineDispatcher,
  ) {
      /**
       * Connects to the PC and suspends until the channel is ready.
       */
      suspend fun connect(address: String) { ... }
  }
  ```

- Asenkron iş `suspend` fonksiyon ve `Flow`; `GlobalScope` yok, kapsamlar sahibine bağlı (servis, ViewModel). Bloklayan G/Ç `Dispatchers.IO`'da, dispatcher enjekte edilir.
- UI durumu ViewModel'de `StateFlow`, ekranda `collectAsStateWithLifecycle`. Composable'lar durumsuz, durum ve olaylar parametreyle.
- Metin sadece `strings.xml`'de (`values` English, `values-tr` Türkçe, ikisi birlikte güncellenir); renk sadece `MaterialTheme.colorScheme` ve Fenpo'nun genişletilmiş rolleri. Kodda sabit metin ve renk yok; Android Lint `HardcodedText` hata seviyesinde.
- Protokol ve ayrıştırıcı kodunda Java'nın `toLowerCase()`/`toUpperCase()`'i yok (Türkçe "I" sorunu); Kotlin'in `lowercase()`'i yerel ayardan bağımsız.
- Testler: saf JVM modüllerde birim testleri; Android modüllerinde Robolectric; her ekran için Roborazzi görüntüsü (açık, koyu × Türkçe, English). Ayrıştırıcı fixture'ları `src/test/resources/fixtures/`.

Kaynaklar: [Kotlin coding conventions](https://kotlinlang.org/docs/coding-conventions.html), [Android Kotlin style guide](https://developer.android.com/kotlin/style-guide), [ktlint](https://pinterest.github.io/ktlint/), [compose-rules](https://mrmans0n.github.io/compose-rules/)

## 17. Tema ve dil
**Karar: Görünüm (Sistem, Açık, Koyu) DataStore'da; Dil (Sistem dili, Türkçe, English) AppCompat'ın uygulama dili API'siyle, yeniden başlatmasız. Dinamik renk (duvar kâğıdı) kapalı.**
- Renk şemaları `design/android/android.html`'deki "Renk rolleri" tablosundan; `lightColorScheme`/`darkColorScheme` + başarı (Yaprak) için genişletilmiş renk. Sistem çubukları `enableEdgeToEdge`.
- Tema: `theme_mode` DataStore'da, ilk kare doğru temayla çizilsin diye açılış ekranı değer gelene kadar tutulur. Seçim Compose'da uygulanır (`FenpoTheme(darkTheme = …)`), sistem çubuklarının simge rengi `enableEdgeToEdge` ile aynı değerden gelir. `AppCompatDelegate.setDefaultNightMode` kullanılmıyor: her değişiklikte aktiviteyi yeniden oluşturuyor, Compose ise anında geçiyor.
- Dil: `androidResources { generateLocaleConfig = true }`, `res/resources.properties` içinde `unqualifiedResLocale=en-US`; `AppLocalesMetadataHolderService` + `autoStoreLocales=true`. API 33+'te seçim sistemin Ayarlar > Uygulamalar > Fenpo > Dil ayarıyla aynı yerde; paylaşım hedefleri, kutucuklar ve bildirimler de aynı dili kullanır. iOS'taki gibi sistem ayarına yönlendirmek yerine uygulama içinde seçim, çünkü Android bunu tutarlı biçimde destekliyor.

Kaynaklar: [Uygulama başına dil](https://developer.android.com/guide/topics/resources/app-languages)

## 18. CI
**Karar: `.github/workflows/android.yml`, `ubuntu-latest`, sadece `android/**` ve `protocol/**` değişince.**
- `actions/checkout@v7`, `actions/setup-java@v6` (Temurin 21), `gradle/actions/setup-gradle@v6` ve `cache-provider: basic` (varsayılan önbellek bileşeni kapalı kaynak).
- `./gradlew spotlessCheck lintDebug test verifyRoborazziDebug assembleDebug` (AGP 9 Android birim testlerini sadece debug için oluşturuyor). Ekran görüntüleri `src/test/screenshots/` altında repoda; Windows'ta kaydedilenlerin Linux runner'da %0,5 eşik içinde kaldığı ilk CI koşusunda görülecek.
- Yerel derleme: [android/README.md](../android/README.md).
- Sürüm imzalı APK'yı etiketle GitHub Releases'a koyan ikinci iş Aşama B'den sonra; imza anahtarı repo sırrında.

Kaynaklar: [setup-gradle](https://github.com/gradle/actions/blob/main/docs/setup-gradle.md), [setup-java](https://github.com/actions/setup-java/releases), [checkout](https://github.com/actions/checkout/releases)

## 19. Dağıtım
**Karar: Sıra: (1) GitHub Releases'ta kendi anahtarımızla imzalı, tekrarlanabilir APK, (2) IzzyOnDroid, (3) F-Droid ana depo (geliştirici imzalı tekrarlanabilir derleme), (4) Google Play en son, SMS ve erişilebilirlik beyanlarıyla.**
- **Geliştirici doğrulaması:** 30 Eylül 2026'dan beri Brezilya, Endonezya, Singapur ve Tayland'da katılımcı mağazalardan kurulumda zorunlu; 2027'de sertifikalı cihazlarda her kurulum için global olacak, Türkiye ilk dalgada yok. Paket adı şimdiden Android Developer Console'a kaydedilmeli: tam hesap (kimlik + 25 $) veya 20 cihaza kadar ücretsiz "limited distribution". Açık kaynak kayıt rehberi F-Droid'in yeniden imzaladığı APK parmak izini eklemeye izin veriyor. 2027 sonrası kayıtsız APK sadece "advanced flow" (24 saat bekleme) veya `adb` ile kurulabilir. Milvasoft hesabıyla kayıt önerilir.
- **F-Droid:** Play Services, Firebase, ML Kit yasak; araç zinciri tamamen özgür. Wire seçimi `protoc` ikilisi sorununu baştan kaldırıyor. Foojay toolchain otomatik indirmesi kullanılmaz. Tekrarlanabilir derlemede F-Droid kaynaktan derler, eşleşirse bizim imzalı APK'mızı yayınlar; böylece GitHub, IzzyOnDroid ve F-Droid aynı imzayı taşır ve kullanıcı kaynak değiştirse de güncelleme alır. Sunucusuz uygulamaya NonFreeNet anti-özelliği uygulanmaz.
- **IzzyOnDroid:** GitHub sürümlerindeki APK'yı alıyor, FOSS şartı, VirusTotal ve imza tutarlılığı denetimi. Codeberg'de issue ile başvurulur.
- **Play:** Kişisel hesapta 12 test kullanıcısı × 14 gün kapalı test şartı; targetSdk 36+ (1 Kasım 2026 uzatması). Riskler: SMS/Call Log beyanı (§7), erişilebilirlik beyanı ve videosu (§11), FGS türü beyanı. Play sürümü ayrı bir `play` flavor'ı; beyan onaylanmayan özellik o flavor'da kapalı.

Kaynaklar: [Geliştirici doğrulaması](https://developer.android.com/developer-verification), [SSS](https://developer.android.com/developer-verification/guides/faq), [Açık kaynak kaydı](https://developer.android.com/developer-verification/guides/open-source-app-registration), [Play test şartı](https://support.google.com/googleplay/android-developer/answer/14151465), [F-Droid Inclusion Policy](https://f-droid.org/docs/Inclusion_Policy/), [Anti-Features](https://f-droid.org/docs/Anti-Features/), [Reproducible Builds](https://f-droid.org/docs/Reproducible_Builds/), [IzzyOnDroid](https://apt.izzysoft.de/fdroid/index/info)

## 20. Protokol ve Windows tarafına etkisi
Her protokol değişikliği ayrı bir `protocol: ...` commit'i olur ve Buğra'ya sorulur. Durum (16 Ekim):

| Konu | Gereken | Ne zaman |
|---|---|---|
| Medya kontrolü | **Önerildi:** `media.proto`, `faf81f1`. Android'de AMS yok. Şu an çalan ve medya komutları için bir `Capability` ve Envelope aralığı yok; `CAPABILITY_NOTIFICATION_MIRROR` bunu kapsamıyor | Aşama D, ayrı yetenek önerilecek |
| Bildirim aynalama | 90–96: bildirim, kaldırma, anlık görüntü sonu, kapat, eylem ve yazılı yanıt, uygulama simgesi | **Yapıldı:** `aedd2c0`, `bfd7921` (§22), `main`'e onayla |
| Mesajlar | 100–109: sadece MAP'i olmayan telefonlar için (§5) | Aşama D, MAP sonucuna göre |
| `Hello` | İşletim sistemi sürümü ve model adı (DECISIONS #30, S5 commit'i); Android'de "Android 17, Pixel 9" | **Yapıldı:** `e185f5d` |
| Hassas pano | `ClipboardItem`'da "hassas" işareti (şifre yöneticisinden kopyalanan) | **Önerildi:** `faf81f1` (§22) |
| Yakınlık | Telefonun ölçtüğü RSSI (`DeviceState` içinde veya ayrı mesaj, §4) | **Önerildi:** `DeviceState.ble_rssi_dbm`, `faf81f1` |
| Kamera isteği | PC kamerayı isteyince telefon "dokunuş bekleniyor" durumunu bildirebilmeli (§10) | Aşama 4 |

Windows tarafı için notlar:
- **Uyandırma:** Android'de Fenpo'yu PC yaklaşınca CDM uyandırıyor; bunun için PC, Fenpo çalıştığı sürece Fenpo servis UUID'siyle reklam yapmalı (S4'ün Windows tarafı).
- **Yakınlık kilidi:** DECISIONS #29 iPhone'un reklam RSSI'sine dayanıyor. Android telefon kendiliğinden reklam yapmıyor; öneri RSSI'yi telefonun ölçüp göndermesi (§4).
- **Arayüz metinleri:** `design/desktop/desktop.html` iPhone'a göre yazılmış ("iPhone'um", "WhatsApp gibi uygulamalarda yanıt yazılamıyor"). Android eşleşince bildirimlerde yanıt kutusu her uygulamada olabilir, "telefonda aç" düğmesi olmaz (§6), mesajlar ekranında RCS notu gerekir (§7).

## 21. Cihazda doğrulanacaklar
Gerçek telefon (DECISIONS #27) ve Windows'ta Fenpo BLE çevre birimi (S4'ün Windows tarafı) gerekiyor. Aşama C'den önce, iPhone'daki spike'ların karşılığı olarak küçük denemeler.

| # | Deneme | Neyi cevaplıyor | Olmazsa |
|---|---|---|---|
| A1 | CDM ilişkisi + varlık olayları, Windows `GattServiceProvider` reklamıyla | PC yaklaşınca Fenpo uyanıyor mu, Windows'un reklam adresi dönüyor mu, bağdan sonra çözülüyor mu | Servis UUID filtreli `PendingIntent` taraması |
| A2 | BLE bağlantısı, bağ, MTU, `tx` bildirimleri; bir gün boyu bağlı kalma (Pixel ve Samsung) | Gerçek kullanımda kopma ve yeniden bağlanma, OEM öldürmesi | Kurulumda üreticiye göre pil adımı |
| A3 | Keystore anahtarlı karşılıklı TLS 1.3 ↔ Windows `SslStream` | Schannel, Keystore'un ürettiği sertifikayı kabul ediyor mu | bcpkix ile yeniden imzalanan sertifika |
| A4 | Windows'taki MAP, PBAP ve HFP kodu test telefonuyla | Klasik Bluetooth kodu Android'de yeniden kullanılabiliyor mu | SMS Fenpo kanalından (`CAPABILITY_MESSAGES`) |
| A5 | Profilsiz CDM ilişkisiyle OTP içeren bildirim | Fenpo OTP sansüründen muaf mı | PC'de "içerik gizlendi" metni |
| A6 | Kamera FGS'si ekran kapalıyken | Web kamerası telefon kilitlenince sürüyor mu | "Ekranı karart" modu ekran açık kalarak |

A3 emülatörde de yapılabilir; diğerleri gerçek Bluetooth istiyor.

### Sonuçlar (16 Ekim, Galaxy S10+ SM-G975F, Android 12 / API 31, One UI; PC'de S4 PeripheralSpike, MediaTek adaptör)

Debug derlemesindeki "BLE probe" ile, ürün sınıfları doğrudan sürülerek (S5 sunucusu Defender yüzünden çalışmadığı için gerçek eşleşme yerine test anahtarıyla).

| # | Sonuç |
|---|---|
| A1 | **İlişki çalışıyor.** CDM penceresi PC'yi Fenpo servis UUID'siyle buldu ve **"BUGRAKOSEN"** adıyla gösterdi: Windows reklama bilgisayar adını koyuyor (S4'ün açık sorusu). Samsung metni: "Fenpo uygulaması BUGRAKOSEN cihazınızı yönetebilsin mi?". Kaydedilen adres `73:05:…` **çözülebilir özel adres** (dönen). **Varlık çalışıyor:** gözlem başlayınca "PC appeared" 1 saniyede geldi, `connectedDevice` servisi ön planda başladı ve `autoConnect` 9 saniyede bağlandı. **Denenemeyen:** "kayboldu" olayı ve uygulama süreci ölüyken uyanma; PC spike'ı ikinci çalıştırmadan itibaren reklamı `Aborted` durumunda bıraktı (Windows tarafı, S4'e not), PC görünmez olunca test anlamsızlaştı. Adres dönünce (Windows'ta yaklaşık 15 dakika) varlığın sürmesi de uzun bir denemeyle bakılacak. |
| A2 | **Çalışıyor.** Bağ, şifreli CCCD yazmasıyla **pencere çıkmadan** (Just Works) 12 saniyede kuruldu; Android ayrıca PC'nin kimlik adresini (`CC:47:…`) bağladı. MTU **517**, PC tarafında `maxNotification` 514. `rx`'e yanıtsız yazma PC'ye ulaştı, yazma geri çağrısı geldi; PC'nin `tx` bildirimi geldi (Android 12'nin eski geri çağrısıyla). RSSI masada **−43…−47 dBm**. Şifreli `Hello` 119 bayt tek parça gitti; sayaç 1 → 2 → 3 ilerledi, yeniden başlatmada geri gitmedi. |
| Ek | **Klasik bağ da oluştu (CTKD).** LE bağından sonra telefon PC'yi klasik Bluetooth'ta da bağlı gösterdi ve **HEADSET ile A2DP bağlantı politikasını kendiliğinden açtı**: telefonun sesi ve aramaları istenmeden PC'ye gidebilir. HFP için iyi haber (aramalar için ayrıca Windows eşleşmesi gerekmeyebilir), A2DP için istenmeyen bir yan etki. Fenpo bu politikaları değiştiremiyor (`BLUETOOTH_PRIVILEGED`); Windows tarafında A2DP sink'in kapatılması veya kullanıcıya söylenmesi gerekiyor. |
| Ek | **Bağlantı canlı ama servis yok.** PC servisi kaldırınca Windows "Service Changed" gönderdi, telefon yeniden bağlandı ve Android önbellekteki GATT tablosuyla kanalı "hazır" saydı; arkada dinleyen yoktu. Bu yüzden bağlantı servisi, PC `Hello` ile cevap verdikten sonra her 30 saniyede `Ping` atıyor, 10 saniyede `Pong` gelmezse yeniden bağlanıyor (`Heartbeat`). Servis geri gelince "Service Changed" ile yeniden keşif ve abonelik doğru çalıştı. |
| Ek | Samsung'un yazı tipi Roboto'dan uzun: hızlı işlem kutucuklarında ikinci satırın alt kısmı kesildi (ekran görüntüsü testleri bunu yakalamadı). Kutucuklar artık metne göre uzuyor. |
| A3 | **Çalışıyor (telefon tarafı).** Keystore anahtarı (P-256, `DIGEST_NONE`) Galaxy S10+ Android 12'de Conscrypt ile TLS 1.3 istemci sertifikası olarak el sıkıştı (instrumented test 3/3). Windows oturumu Keystore biçimindeki sertifikanın Schannel'la eşleştiğini kendi testiyle gösterdi; bcpkix gerekmiyor. Test sertifikaları Android 12'nin okuyabildiği eski PKCS#12 biçiminde (`keytool -J-Dkeystore.pkcs12.legacy`). |
| A1 (devam) | Varlık olayları iki yönde geldi: "kayboldu" ve 17 dakika sonra "geldi", ardından otomatik bağlantı. Adres ilişkiden 30+ dakika sonra da tuttu (bağ sayesinde dönen adres çözülüyor). Süreç ölüyken uyanma hâlâ ayrıca denenecek. |
| Ek | `bluetooth.profile.*` sistem özellikleri Samsung'da boş: MAP/PBAP desteği bu yolla okunamıyor, Windows'tan SDP sorgusuyla bakılacak (A4). |
| A4 (ilk veri) | Gerçek eşleşme sırasında PC'nin MAP istemcisi Galaxy S10+'a bağlandı ("Message access connected", en büyük paket 65534; Windows oturumu, 16 Ekim 14:35). Bu Samsung'da MAP sunucusu var. Mesaj okuma ve gönderme henüz denenmedi. |

### Gerçek eşleşme (16 Ekim, Windows Fenpo `9f897f9` ↔ Galaxy S10+)

Windows'taki Fenpo'nun QR ekranından baştan sona: QR → TLS 1.3 (sabitlenmiş PC sertifikası, Keystore istemci sertifikası) → `PairingRequest`/`PairingResult` → iki yönlü `Hello` → CDM ilişkisi → bağ → BLE üzerinden şifreli `Hello` ve PC'nin `PcStatus`'u. Kalp atışı `Pong`'ları geliyor. Windows oturumu kendi tarafında doğruladı.

| Bulgu | Ne yapıldı |
|---|---|
| QR Android 12'de "Bu bir Fenpo kodu değil" dedi. `URLDecoder.decode(String, Charset)` API 33'te geldi; API 31'de `NoSuchMethodError` `runCatching` içinde yutulup "bozuk kod" sayıldı. | `decode(value, "UTF-8")` kullanılıyor, sadece `IllegalArgumentException` yakalanıyor. Windows'un gösterdiği kodun biçimiyle birim testi eklendi. JVM modülleri (`core:pairing`, `core:transport`) Android API düzeyine göre lint'ten geçmiyor; ayrıca bir API imza denetimi düşünülecek. |
| Okunan ama alanı bozuk kod da "Fenpo kodu değil" diyordu. | Ayrı ipucu: "Kod tam okunamadı…". Reddedilen kod log'a alan adlarıyla düşüyor; `id`, `n`, `fp`, `t` değerleri maskeli. |
| PC bağı kaybetti (anahtar yok, 0x06); Android de bağı sildi, kayıtlı dönen adres artık çözülmedi ve bağlantı 133 ile düştü. | "Yeniden dene" Bluetooth adımında ilişkiyi baştan kuruyor; adresi farklı eski ilişki bırakılıyor. |
| İzinler ekranındaki "Bildirim erişimi" satırı açılamıyordu: henüz `NotificationListenerService` yok, sistem listesinde Fenpo çıkmıyor. | Satır Aşama D'ye (bildirim aynalama) kadar gizli (`AppPermission.shown`). |
| Samsung yazı tipinde izin düğmesinin metni kesildi. | Düğme yüksekliği metne göre uzuyor (`heightIn(min = 32.dp)`). |

**Wi‑Fi kanalı (16 Ekim 15:00, Windows Fenpo `7396bc3`):** saklı adres `192.168.1.14:53113` ilk denemede TLS 1.3 ile açıldı, PC `Hello` verdi, `DeviceState` (%44, şarjda) Wi‑Fi'den gitti; 2 saniye sonra BLE yolu da açıldı, bildirim "Bağlı, Bluetooth ve Wi‑Fi". Telefonun Wi‑Fi'si kapatılınca bağlantı hemen kapandı, `DeviceState` BLE'ye geçti, bildirim "Bağlı, Bluetooth" oldu; Wi‑Fi açılınca 5 saniyede yeniden bağlandı. **Denenmedi:** DNS-SD yedeği (saklı adres çalıştığı için gerekmedi), `ERROR_CODE_NOT_PAIRED`, Android 17'de yerel ağ izni, PC'nin `DeviceState.addresses` göndermesi (Windows'ta henüz yok).

Süreç ölüyken uyanma §22'de denendi.

## 22. Aşama D (uygulandı, 16 Ekim)

Hepsi protokole göre yazıldı ve birim testleriyle doğrulandı. **Windows tarafı bunları henüz sunmuyor**: PC'nin `Hello`'su sadece `CAPABILITY_DEVICE_STATE` veriyor, bu yüzden telefon diğer mesajları göndermiyor; uçtan uca cihaz denemesi Windows'taki karşılıklar gelince yapılacak. Telefon `Hello`'da `DEVICE_STATE`, `CLIPBOARD`, `OPEN_URL`, `RING`, `LOCK_PC`, `FILE_TRANSFER` ve `NOTIFICATION_MIRROR` sunuyor.

| Özellik | Telefonda | Not |
|---|---|---|
| Mesajlaşma katmanı | `PcMessenger`: özellik mesajları Wi‑Fi açıksa Wi‑Fi'den, yoksa BLE'den; PC yeteneği sunmuyorsa gönderilmez. BLE'ye sığmayan mesaj Wi‑Fi bekler. | |
| Pano (§8) | PC'den gelen metin, bağlantı ve PNG/JPEG panoya kendiliğinden yazılır ("PC'den panoya alındı"). Telefondan PC'ye: Hızlı Ayarlar kutucuğu, kalıcı bildirimdeki "Panoyu gönder", ana ekran kutucuğu; üçü de odak alan görünmez aktiviteyi açar. `ClipboardRequest`'e `ERROR_CODE_UNSUPPORTED` döner (Android arka planda okutmuyor). "Pano paylaşımı" kapalıysa iki yön de durur. | |
| Telefonu çaldır | `RingDevice`: alarm sesi, alarm ses düzeyi tam, titreşim, "Durdur"lu bildirim, 1 dakikada kendiliğinden durur. | Rahatsız Etmeyin'de alarmlar engellenmemişse çalar. |
| PC'yi kilitle | Hızlı Ayarlar kutucuğu ve kalıcı bildirim. | |
| Bağlantılar | PC'den gelen `OpenUrl` dokununca açılan bildirim olur (arka planda tarayıcı açılamıyor); sadece http/https. Paylaşım menüsünde "PC'de aç" paylaşılan metindeki ilk bağlantıyı PC'de açar. | Belgeyi PC'de açmak için protokolde "aktarımdan sonra aç" alanı yok; şimdilik sadece bağlantılar. |
| Dosya aktarımı (§9) | Veri bağlantısını her iki yönde telefon açar (protokol `c5e2cde`). Gönderme: paylaşım menüsü "PC'ye gönder" (tek ve çoklu), doğrudan paylaşım kısayolu (PC'nin adı kişilerin yanında), ana ekran "Fotoğraf gönder" (fotoğraf seçici). Alma: Wi‑Fi açık ve yer varsa kabul, `Download/Fenpo`'ya MediaStore ile (izinsiz), varsa SHA-256 denetimi, yarım dosyalar silinir. İlerleme bildirimi ve İptal; Aktarımlar sekmesinde süren aktarımlar ve geçmiş. | Tasarımda Aktarımlar ekranı yok; ana ekrandaki "Son aktarılanlar" satırları kullanıldı. Live Update ve `ProgressStyle` (API 36) henüz yok, standart ilerleme çubuğu. |
| Bildirim aynalama (§6) | `NotificationListenerService` → her `Hello`'dan sonra anlık görüntü (`pre_existing`, `NotificationSnapshotDone`; Wi‑Fi açıkken BLE'de atlanır), sonra ekleme, güncelleme, kaldırma; `seq` zamana dayalı (µs), süreç yeniden başlasa da geri gitmez. Kapatma, eylem ve yazılı yanıt (`RemoteInput`), uygulama simgesi isteği. BLE'de büyük simge düşer, metin kısalır. Ayarlar'da uygulama başına seçim ("PC'den yanıtlanır", "Sadece gösterilir", "Kapalı") ve "Yeni uygulamalar da gönderilsin". | Protokol önerisi `aedd2c0` + `bfd7921` (Windows incelemesiyle kategori, anlık görüntü sınırı, `seq`); `main`'e Buğra'nın onayıyla girer. Android 15'in OTP sansürünü tespit edemiyoruz: `content_hidden` şimdilik hep `false`, A5'te bakılacak. |
| Rahatsız Etmeyin | "Rahatsız etmeyin'i eşitle" kapalıysa `DeviceState.focus_active` gönderilmez. | |
| Şu an çalıyor | `MediaBridge`: bildirim erişimi sayesinde `MediaSessionManager` ile etkin medya oturumu okunur; parça, çalma durumu ya da ses düzeyi değişince `MediaState`, kapak sadece Wi‑Fi'de. PC'nin `MediaCommand`'ı (çal, duraklat, sonraki, önceki, sar, ses) uygulanır. | Protokol `faf81f1`, Windows incelemesi ve Buğra'nın onayı bekleniyor. |
| Yakınlık kilidi | Telefon PC'yi BLE bağlantısında ölçer, `DeviceState.ble_rssi_dbm` 4 dB değişince ya da 30 saniyede bir gider. | Aynı protokol commit'i. Eşik ve kilit kararı PC'de. |
| Hassas pano | Şifre yöneticisinin `IS_SENSITIVE` işaretli kopyası `sensitive` gider; PC'den gelen hassas içerik panoya işaretli yazılır, bildirimde ve ana ekranda "Gizli içerik" görünür. | Aynı protokol commit'i. |

**Uçtan uca (16 Ekim 16:15–17:03, Galaxy S10+ ↔ Windows Fenpo, Wi‑Fi ve BLE açık):**

| Deneme | Sonuç |
|---|---|
| PC'nin `Hello`'su | `DEVICE_STATE`, `CLIPBOARD`, `OPEN_URL`, `RING`, `LOCK_PC`, `FILE_TRANSFER` sunuyor; iki yolda da cevap geliyor. |
| PC → telefon | Pano (metin, görsel), telefonu çaldırma, dosya (`Download/Fenpo`), `DeviceState.addresses`: **geçti**. |
| Telefon → PC | Pano (bildirim düğmesi, metin ve görsel), "PC'de aç" (Chrome'dan, PC'de varsayılan tarayıcı açıldı), "PC'yi kilitle" (kalıcı bildirim), dosya (fotoğraf seçici ve Galeri): **geçti**. |
| Galeri paylaşımı ilk denemede düştü | Telefon veri bağlantısını son bayttan hemen sonra kapatıyordu; PC'nin TLS 1.3 oturum biletleri okunmadığı için Android TCP RST gönderdi, PC son baytları kaybetti. Telefon artık bağlantıyı boşaltıp PC'nin `COMPLETED`'ından sonra kapatıyor; protokol notu Windows'ta `2194f63`. Ayrıca boru veren sağlayıcılar önce önbelleğe kopyalanıyor. |
| PC adres listesi | 169.254 (Tailscale) ve Hyper‑V adresleri önce geliyordu, her biri 5 saniye kaybettiriyordu. PC listeyi düzeltti; telefon da kendi alt ağını öne alıyor. |
| Medya | Bildirim erişimi açıkken YouTube Music oturumu okundu. PC henüz `MEDIA` sunmuyor. |

**Süreç ölüyken uyanma (16 Ekim 17:07–17:08):** PC 40 saniye kapatıldı, telefon "PC kayboldu" deyip servisi kapattı, uygulama süreci `am crash` ile öldürüldü. Android süreci bildirim dinleyicisi için 1 saniyede yeniden başlattı (dinleyici bağlıyken süreç `am kill` ile de ölmüyor; pratikte hep ayakta). PC dönünce CDM "göründü" olayını yeni sürece verdi, bağlantı servisi başladı ve PC'nin `Hello`'su 0,6 saniyede Wi‑Fi'den geldi, BLE 2 saniye sonra açıldı: **geçti**. Bildirim erişimi verilmemiş bir telefonda sürecin gerçekten ölü olduğu durum ayrıca denenecek.

Kurulumdan sonraki "Paired with" kayıtları QR'ın elle yeniden okutulmasından; uygulama kendiliğinden eşleşmiyor.

**Kamera ve ekran yansıtma (16 Ekim, protokol `636f7d4` + `ef35a40`, Buğra'nın onayını bekliyor):**

| Özellik | Telefonda | Not |
|---|---|---|
| Ortak | `core:media`: yüzey girişli H.264 kodlayıcı (Annex-B, Main, B-kare yok, CBR, 2 sn GOP, istekle anahtar kare, her anahtar kareden önce codec config), PCM 48 kHz ya da AAC-LC ses, görüntü bağlantısı (telefon açar, ilk çerçeve `MediaStreamOpen`), geride kalınca bir sonraki anahtar kareye kadar görüntü atılır ve bit hızı düşer (6 Mbps hedef, 1,5 taban). | PC'nin çözebildikleri `accepted` listesinden seçilir. |
| Web kamerası | PC `CameraStart` gönderince "Kamerayı başlat" bildirimi (`WAITING_FOR_TAP`), dokununca kamera ekranı ve `camera` FGS'si; tasarımdaki ekran: "Canlı: Teams", Wi‑Fi, 0,5/1×/2, Ortala, Mikrofon, Çevir, Ekranı karart. Ayarlar iki yönde eşitlenir. Mikrofon PC kabul ederse gider. | **Cihazda denenmedi:** Windows'ta Fenpo Kamera yok. Görüntü sensör yönünde gider; telefon yatay standdayken düz. Dikey tutuşta döndürme için ayrı bir GL adımı gerekir. "Teams'te canlı" yerine "Canlı: Teams" (Türkçe ek uygulama adına göre değişiyor). |
| Ekran yansıtma | PC `ScreenStart` gönderince "Ekranı paylaş" bildirimi (`WAITING_FOR_CONSENT`), Android'in onayı, `mediaProjection` FGS'si, VirtualDisplay → kodlayıcı; uzun kenar en fazla 1920; döndürmede aynı bağlantıda yeni boyut ve anahtar kare; kilitlenince `PAUSED_LOCKED`; istenirse telefonun sesi (playback capture). | **Cihazda denenmedi:** Windows tarafı yok. |
| PC'den kontrol | `FenpoAccessibilityService` (`isAccessibilityTool=false`): dokunma, uzun basma, kaydırma, Geri/Ana ekran/Son uygulamalar/Bildirimler, odaktaki alana yazma. Kapalıyken `ScreenInput` `UNSUPPORTED` ile cevaplanır; PC kontrol isterse paylaşım bildiriminde "Kontrole izin ver" düğmesi erişilebilirlik ayarını açar. | İzinler ekranına eklenmedi: kontrol istemeyen için sürekli "eksik izin" olurdu. Mağaza dışı kurulumda Android 13+ kısıtlanmış ayar adımı gerekir. |

**Akşam eklenenler (16 Ekim):**

| Özellik | Telefonda | Durum |
|---|---|---|
| Ekran görüntülerini gönder | Ayar açıkken yeni ekran görüntüsü dosya aktarımıyla PC'ye gider; anahtar açılınca fotoğraf izni istenir, izin yoksa anahtar kapalı görünür. | **Geçti** (22:14, alındıktan 0,1 sn sonra aktarım tamamlandı). İlk denemede ayar izin sorulmadan önceki bir sürümden açık kalmıştı: anahtar açık görünüyor, hiçbir şey gitmiyordu; düzeltildi. |
| Kamera döndürme | Kamera servisi `SurfaceTexture` dönüşümüne sensör dönüşünü (ön lenste aynayı da) zaten koyuyor; GL adımı dönüşü bu matristen okur, resmi kendi oranında çizer, yalnız tutuş yönü kadar döndürür, ön lensin aynasını kaldırır. Lens değişince dönüş yeniden hesaplanır. | **Geçti** (21:54, ön ve arka lens, dikey). İlk sürüm sensör dönüşünü ikinci kez uyguluyordu: PC'de görüntü yan ve sünük geliyordu. Yatay tutuş ayrıca denenmedi. |
| Dikey tutuşta görüş alanı | Dikeyde yatay kareye kırpma yaklaşık 1,8 kat yakınlaştırıyor. S10+'da ana lens 1×'in altına inmiyor (`zoomRatioRange` 1–8); uygulamalara açık 1,8 mm ultra geniş lense (kamera 2, ana lensten 1,96 kat geniş) geçilir, yakınlaştırma ana lens ölçeğinde kalır ve 0,5 seçeneği çıkar. Aynı anda tek kamera açılır. | **Geçti** (21:54). Hızlı lens değişiminde iki kamera birden açılıp "Camera error 2" veriyordu; düzeltildi. |
| Kamera gecikmesi | Telefonda çekimden gönderime 130–160 ms, 30 fps, kayıp yok; `WIFI_MODE_FULL_LOW_LATENCY` ve 5 saniyede bir akış özeti (`FenpoMedia`). | **Geçti**: 3 saniyelik gecikme PC oynatıcısındaydı, Windows düzeltti; Buğra: "neredeyse realtime". |
| Ekran yansıtma ve PC'den kontrol | Erişilebilirlik hizmetini Buğra açtı. | **Geçti** (21:58–22:00): yaklaşık 35 fps, kayıp yok, yakalamadan gönderime 41 ms; dokunma, kaydırma ve yazma çalıştı. |
| Web kamerası uçtan uca | 21:05:42–21:06:12 akış PC'ye gitti, çözücü hatası yok. | **Geçti** (PC tarafındaki kuyruk hatası düzeltildikten sonra). |
| PC sekmesi | PC'nin `MediaState`'i ("PC'de çalıyor": önceki, oynat/duraklat, sonraki, ses), PC'yi çaldır, kilitle, pano. | Medya kontrolü **geçti** (16 Ekim akşamı): kart, önceki, oynat/duraklat, sonraki, ses. PC'yi çaldırma da **geçti**. |
| SMS (MAP'siz telefonlar) | `messages.proto` (`5e454cb`), `SmsBridge`; `MESSAGES` ayar ve izinle sunulur. | S10+'da MAP olduğu için PC MAP'i tercih ediyor; onay bekliyor. |
| Fare, klavye, sunum kumandası | `pc_input.proto` (`b49f148`, `de07378`), `feature:remote`. | **Geçti** (16 Ekim akşamı): imleç, tık, sağ tık, iki parmakla kaydırma, tuşlar, Türkçe yazma ve silme; sunumda başlat, sonraki, önceki, siyah ekran, bitir, lazer işaretçi ve ekranı açık tutma. İlk sürümdeki donma (ANR) düzeltilmişti. Basılı tutup sürükleme çalışmadı: duran parmağın küçük hareketleri 450 ms'lik sayacı her seferinde baştan başlatıyordu; sayaç artık dokunuştan sayılıyor ve sürükleme başlayınca telefon titriyor; düzeltmeden sonra **geçti**. |

**Cihazda denenebilenler (PC'siz):** paylaşım hedefleri ve Hızlı Ayarlar kutucukları sisteme kayıtlı (`query-activities`, `query-services`). Wi‑Fi yolu 17 dakika kesintisiz kaldı, pil değiştikçe `DeviceState` gitti.
