# Fenpo for Android: Google Play gönderimi

Taslak, 17 Ekim 2026. Kararlar verildi (§0); metinler Buğra'nın son okumasını bekliyor. Mağaza metinleri, grafikler, sürüm ve imzalama, politika denetimi
ve Data safety yanıtları burada; Play Console'a girilecek her şey bu belgeden kopyalanır.

## 0. Önce Buğra'nın vermesi gereken kararlar

| Konu | Neden | Öneri |
|---|---|---|
| **Karar: kuruluş hesabı (Milvasoft).** Geliştirici hesabı türü | Kasım 2023'ten sonra açılan **kişisel** hesaplar üretime çıkmadan önce **12 test kullanıcısıyla 14 gün kapalı test** yapmak zorunda. **Kuruluş** hesabı (Milvasoft, D-U-N-S numarası gerekir) bunu atlar. | Kapalı test zorunluluğu yok; doğrudan üretime (ya da isteğe bağlı kısa bir iç teste) gönderilebilir. |
| **Karar: ilk sürüm SMS'le çıkıyor (Buğra).** SMS izinleri | `READ_SMS`, `RECEIVE_SMS`, `SEND_SMS` kısıtlı izinler. "Cihazlar arası SMS eşitleme" istisnası Fenpo'ya uyuyor ama inceleme "başka yol yoksa" diyor ve MAP (Bluetooth) çoğu telefonda başka bir yol. Reddedilirse tüm sürüm bekler. | İzin Beyan Formu "Cihazlar arası SMS eşitleme" istisnasıyla doldurulur (§4.7), video hazırlanır. Reddedilirse SMS izinleri `tools:node="remove"` ile çıkarılıp yeniden gönderilir; özellik kendiliğinden kapanır. |
| Fotoğraf ve video izinleri | `READ_MEDIA_IMAGES/VIDEO` için beyan gerekiyor; onay "fotoğraf seçici yetmiyorsa" veriliyor. | Beyanla gönder (§4.3). Reddedilirse bu iki özellik kapatılıp yeniden gönderilir. |
| Sürüm numarası | Play'deki ilk sürüm. | `versionName` 1.0.0, `versionCode` 1. Her yüklemede `versionCode` artar. |

## 1. Mağaza metinleri

Kurallar: düz dil, Apple ve Microsoft'un özellik adları (AirDrop, Handoff, Phone Link, …) yok. "Windows" platform adı
olarak geçer (uyumluluğu anlatmak için gerekli).

### Türkçe (tr-TR)

**Başlık** (≤ 30): `Fenpo: Telefon ve PC` (20)

**Kısa açıklama** (≤ 80): `Telefonunu Windows PC'ne bağla: dosya, pano, bildirim, kamera ve ekran.` (70)

**Tam açıklama** (≤ 4000):

```
Fenpo telefonunu Windows bilgisayarına bağlar; ikisi tek bir cihaz gibi çalışır. Sunucu yok, hesap yok: her şey senin cihazların arasında, şifreli olarak olur.

Bir kez QR koduyla eşleştir, sonra Fenpo arka planda bağlı kalır.

DOSYALAR VE FOTOĞRAFLAR
• Dosya ve fotoğrafları iki yönde gönder; galeriden ya da herhangi bir uygulamadan "PC'ye gönder" ile.
• Yeni ekran görüntülerin istersen kendiliğinden PC'ye gider.
• PC'den telefonunun fotoğraflarına göz at, istediklerini indir.
• PC'nin paylaştığın klasörlerini telefonun dosya seçicisinde aç, dosya kaydet.

PANO
• Telefonda kopyaladığını PC'de yapıştır, PC'de kopyaladığını telefonda. Metin, bağlantı ve resim.

BİLDİRİMLER
• Telefonunun bildirimleri PC'de görünür; mesajları PC'den yanıtla, bildirimleri kapat.
• Hangi uygulamaların bildirim göndereceğini sen seçersin.
• İstersen PC'nin bildirimleri de telefonuna gelir.

KAMERA VE EKRAN
• Telefonunun kamerasını PC'de web kamerası olarak kullan; ön ya da arka kamera, geniş açı ve yakınlaştırmayla.
• Telefonunun ekranını PC'de gör ve PC'nin faresi ve klavyesiyle kullan.

KUMANDA
• Telefonun PC'nin dokunmatik yüzeyi ve klavyesi olur.
• Sunum kumandası: sonraki, önceki, siyah ekran ve lazer işaretçi.
• PC'de çalan müziği telefondan yönet, sesi aç kıs.

DİĞERLERİ
• PC'yi kilitle, telefonunu PC'den çaldırarak bul, PC'de bağlantı aç.
• Telefonunun hotspot'una PC'den tek tıkla bağlan.
• Pil durumu ve Rahatsız Etmeyin eşitlemesi.

GİZLİLİK
Fenpo hiçbir veri toplamaz. Veriler yalnızca eşleştirdiğin bilgisayarla, Bluetooth ve yerel ağ üzerinden, şifreli olarak (TLS 1.3, AES-GCM) taşınır. Reklam, analiz ve takip yok.

Fenpo'yu kullanmak için Windows bilgisayarında Fenpo for Windows gerekir.
```

### English (en-US)

**Title** (≤ 30): `Fenpo: Phone and PC` (19)

**Short description** (≤ 80): `Link your phone to your Windows PC: files, clipboard, notifications, camera.` (77)

**Full description** (≤ 4000):

```
Fenpo links your phone to your Windows PC so the two work as one. No servers, no accounts: everything happens between your own devices, encrypted.

Pair once with a QR code; after that Fenpo stays connected in the background.

FILES AND PHOTOS
• Send files and photos both ways, from the gallery or any app with "Send to PC".
• New screenshots can go to your PC on their own.
• Browse your phone's photos from the PC and download the ones you want.
• Open the folders your PC shares in the phone's file picker and save files there.

CLIPBOARD
• Copy on the phone, paste on the PC, and the other way round. Text, links and images.

NOTIFICATIONS
• Your phone's notifications appear on the PC; reply to messages and dismiss notifications there.
• You choose which apps send notifications.
• Your PC's notifications can come to the phone too.

CAMERA AND SCREEN
• Use your phone's camera as the PC's webcam: front or back camera, ultra-wide and zoom.
• See your phone's screen on the PC and use it with the PC's mouse and keyboard.

REMOTE
• The phone becomes the PC's touchpad and keyboard.
• Presentation remote: next, previous, black screen and a laser pointer.
• Control the music playing on your PC and its volume.

AND MORE
• Lock the PC, find your phone by ringing it from the PC, open links on the PC.
• Join your phone's hotspot from the PC in one click.
• Battery level and Do Not Disturb sync.

PRIVACY
Fenpo collects no data. Your data moves only to the PC you paired, over Bluetooth and your local network, encrypted (TLS 1.3, AES-GCM). No ads, analytics or tracking.

Fenpo needs Fenpo for Windows on your PC.
```

## 2. Grafikler ve video

Hepsi `design/store/android/` altında, tek komutla üretiliyor (`python design/store/android/scripts/build.py`):
simge, iki dilde öne çıkan grafik ve alt metni, her dilde açık ve koyu 7'şer tasarlanmış ekran görüntüsü, ham ekranlar,
tanıtım videoları. Nereye yükleneceği `design/store/android/README.md`'de, nasıl tasarlandığı `PLAN.md`'de. Mağaza
metinlerinin güncel hali de orada (`metadata/<dil>.md`); §1'deki metinlerden farkı bölüm başlıklarının büyük harfle
yazılmaması ve SMS satırı.

## 3. Sürüm ve imzalama

- `./gradlew :app:bundleRelease` → `app/build/outputs/bundle/release/app-release.aab` (yaklaşık 16 MB, R8 kapalı).
- **Play App Signing** açık olacak: Google kullanıcılara giden paketi kendi anahtarıyla imzalar. Bizim yalnızca bir
  **yükleme anahtarımız** olur.
- Yükleme anahtarını **Buğra kendisi** oluşturur, şifresini kimseyle paylaşmaz, depoya koymaz:

  ```
  keytool -genkeypair -v -keystore %USERPROFILE%\.fenpo\fenpo-upload.jks -alias fenpo-upload -keyalg RSA -keysize 4096 -validity 9125
  ```

  Sonra `%USERPROFILE%\.gradle\gradle.properties` dosyasına (depo dışında) şunları yazar:

  ```
  FENPO_UPLOAD_STORE_FILE=C:/Users/bugra/.fenpo/fenpo-upload.jks
  FENPO_UPLOAD_STORE_PASSWORD=...
  FENPO_UPLOAD_KEY_ALIAS=fenpo-upload
  FENPO_UPLOAD_KEY_PASSWORD=...
  ```

  Aynı adlar ortam değişkeni olarak da okunur (CI için). Bunlar yokken release paketi imzasız çıkar; Play imzasız
  paketi kabul etmez.
- Anahtar dosyasının bir yedeğini depo dışında güvenli bir yerde tut. Kaybolursa Play Console'dan yükleme anahtarı
  sıfırlama istenebilir (Play App Signing açık olduğu için uygulama kaybolmaz).

## 4. Politika denetimi

| # | Konu | Fenpo'da | Gerekli olan | Öneri |
|---|---|---|---|---|
| 4.1 | SMS izinleri (`READ_SMS`, `RECEIVE_SMS`, `SEND_SMS`) | MAP'i olmayan telefonlarda SMS'leri PC'de okumak ve yanıtlamak (`messages.proto`). | Varsayılan SMS uygulaması olmayanlar için yalnızca istisnayla; "Cihazlar arası SMS ve arama eşitleme" istisnası bu izinlerin hepsini kapsıyor. İzin Beyan Formu ve video gerekiyor; "başka yol yoksa" şartı var. | İlk sürüm SMS'le gidiyor: beyan §4.7'de. Gerekçe: bazı Android telefonları Bluetooth MAP'i hiç sunmuyor ya da mesaj göndermeden sunuyor (Fenpo bunu kendi telefonlarımızda görmedi; S10+ MAP'i destekliyor); o telefonlarda SMS'i PC'ye taşımanın başka yolu yok. Fenpo varsayılan SMS uygulaması olmaz; mesajlar yalnızca kullanıcının eşleştirdiği PC'ye, şifreli gider. Reddedilirse izinleri `tools:node="remove"` ile çıkar; özellik kendiliğinden kapanır. |
| 4.2 | Erişilebilirlik hizmeti ("Fenpo: PC'den kontrol") | `isAccessibilityTool="false"`; yalnızca ekran paylaşılırken PC'nin dokunma, kaydırma ve yazma girdisini telefonda uygular. | Erişilebilirlik API beyanı (Play Console, App content), **uygulama içinde göze çarpan açıklama ve onay** (kullanıcı ayarlara gitmeden önce), gizlilik politikasında anlatım, beyan videosu. | Açıklama ve onay ekranı bu dalda eklendi (`MirrorControlDisclosureActivity`). Beyan metni §4.7'de. Gizlilik politikasına eklenmesi gerekiyor (§6). |
| 4.3 | Fotoğraf ve video (`READ_MEDIA_IMAGES`, `READ_MEDIA_VIDEO`, `READ_MEDIA_VISUAL_USER_SELECTED`) | PC'den fotoğraf arşivine göz atmak ve yeni ekran görüntülerini kendiliğinden göndermek. İkisi de seçiciyle yapılamaz. | Beyan formu: sistem fotoğraf seçicisinin neden yetmediği. İzni vermeyen için çalışan bir yol. | Beyan: "PC'den telefonun tüm fotoğraf arşivine göz atılıyor ve yeni ekran görüntüleri arka planda PC'ye gönderiliyor; seçici, kullanıcı telefonun başında değilken çalışamaz." İzin verilmezse PC "erişim yok" der, ekran görüntüsü anahtarı kapalı kalır; Android 14+ kısmi izin `LIMITED` olarak destekleniyor. |
| 4.4 | Ön plan servisleri | `connectedDevice` (bağlantı), `mediaProjection` (ekran), `camera` (web kamerası; telefonun mikrofonu Buğra'nın kararıyla kaldırıldı). | Play Console > App content > Foreground service permissions: her tür için açıklama ve kısa video. | Açıklamalar §4.7'de. Videolar: eşleşmiş telefonla PC'ye dosya gönderimi (connectedDevice), PC'den ekran yansıtma (mediaProjection), PC'de web kamerası (camera). |
| 4.5 | Bildirim erişimi (`NotificationListenerService`) | Bildirimleri seçilen uygulamalar için PC'ye aynalamak. | Özel bir beyan yok; Kullanıcı Verisi politikası gereği açık bilgi ve gizlilik politikasında anlatım. | İzinler ekranında açıklaması var; gizlilik politikası anlatıyor. |
| 4.6 | Diğer | targetSdk 37 (Play şartı 36+), `QUERY_ALL_PACKAGES` yok (yalnızca başlatıcı uygulamaları için `<queries>`), `BLUETOOTH_SCAN` `neverForLocation` ile (konum için kullanılmıyor), konum izni yok, `USE_FULL_SCREEN_INTENT` yok. | — | Uygun. |

### 4.7 Beyan metinleri (Play Console'a kopyalanacak)

**SMS (Permissions Declaration Form, EN)** — Core functionality: *Cross-device synchronization or transfer of SMS or
calls.* Fenpo links the user's phone to their own Windows PC. With "Sync messages" turned on, the user reads their
text messages on the PC and replies from the PC; the phone sends the reply. READ_SMS lists the conversations,
RECEIVE_SMS shows new messages on the PC as they arrive, SEND_SMS sends the user's replies. Messages go only to the PC
the user paired with a QR code, end-to-end encrypted over Bluetooth or the local network; there is no server and
nothing is collected. Fenpo is not the default SMS app. Some Android phones do not offer Bluetooth MAP (or offer it
without sending), so there is no alternative way to bring texts to the PC. Video: turn on "Sync messages", grant the
SMS permission, receive a text and see it on the PC, reply on the PC and see it sent from the phone.

**Erişilebilirlik (EN):** Fenpo lets the user control their own phone from their paired Windows PC while they share the
phone's screen with that PC. The accessibility service performs the taps, swipes and typing the user makes on the PC.
It acts only while a screen-sharing session the user started is running, reads no screen content for any other
purpose and sends nothing anywhere but the user's own PC. The user turns it on explicitly after an in-app disclosure.

**connectedDevice (EN):** Keeps the encrypted Bluetooth and local-network connection to the user's paired PC (a companion
device associated through CompanionDeviceManager), so files, clipboard and notifications reach the PC while Fenpo is in
the background.

**mediaProjection (EN):** Shows the phone's screen on the user's paired PC after the system's screen-capture consent,
started by the user from the PC.

**camera (EN):** Uses the phone as the paired PC's webcam; started
by the user with a tap on the phone, with a visible notification while it runs.

## 5. Data safety

Fenpo'nun sunucusu yok. Play'in tanımına göre "toplama", verinin cihazdan ayrılması; ama **uçtan uca şifreli**, yani
geliştirici dahil hiçbir aracının okuyamadığı veri beyan edilmiyor. Fenpo'nun taşıdığı her şey yalnızca kullanıcının
eşleştirdiği PC'ye gidiyor; ağda sertifikası sabitlenmiş TLS 1.3, Bluetooth'ta AES-GCM ile şifreli.

| Soru | Yanıt |
|---|---|
| Uygulama kullanıcı verisi topluyor ya da paylaşıyor mu? | **Hayır** |
| Veriler aktarım sırasında şifreleniyor mu? | Evet (soru "toplanmıyor" yanıtında gelmese de gizlilik politikasında yazıyor) |
| Kullanıcılar veri silmeyi isteyebilir mi? | Uygulanmaz: veri toplanmıyor. Uygulamayı kaldırmak ya da eşleşmeyi silmek telefondaki her şeyi siler. |
| Gizlilik politikası | https://github.com/Milvasoft/fenpo/blob/main/docs/PRIVACY.md |
| Reklam içeriyor mu? | Hayır |
| Hedef kitle | 13 yaş ve üzeri (çocuklara yönelik değil) |
| İçerik derecelendirmesi | Araçlar / verimlilik; kullanıcılar arası iletişim yok (mesajlar yalnızca kullanıcının kendi cihazlarında) |

## 6. Gizlilik politikasında Android için düzeltilecekler

Windows'a bildirildi; politika `main`'de duruyor:

- Android **kişiler izni istemiyor** ("Android'de SMS, kişiler ve bildirim erişimi" yerine "SMS (yalnızca MAP'i olmayan
  telefonlarda), bildirim erişimi ve erişilebilirlik hizmeti"). Arama ve kişi bilgisi PC'ye Fenpo uygulamasından değil,
  telefonun Bluetooth'undan (HFP, PBAP) gidiyor.
- **Erişilebilirlik hizmeti** anlatılmalı: yalnızca ekran paylaşılırken PC'nin dokunma, kaydırma ve yazma girdisini
  uygular; ekran içeriğini başka amaçla okumaz ve hiçbir yere göndermez.
- **Hotspot adı ve şifresi**: kullanıcının yazdığı ad ve şifre telefonda şifreli saklanır ve yalnızca eşleştirdiği PC'ye
  gider.
- **PC'nin bildirimleri ve paylaşılan klasörleri** telefona gelir; Android'de USB bağlantısı yok.

## 7. Gelecekte ücretli özellikler için hazırlık

Fenpo şimdi ücretsiz. Play kuralı: ücretsiz bir uygulama sonradan ücretli (peşin) yapılamaz; ileride ancak uygulama içi
satın alma (Play Billing) olur. Kod tarafında hazırlık yapıldı, ödeme kodu yok:

- `InstallRecord`: ilk açılışta uygulama sürümü ve tarihi bir kez kaydedilir (Windows'taki FirstVersion, FirstRunAt
  gibi). Erken kullanıcılara sahip oldukları özellikleri bırakmak için.
- `Entitlements`: "bu özellik açık mı" tek yerde. Kamera, ekran yansıtma, fare ve klavye, PC klasörleri, fotoğraflar,
  hotspot ve PC bildirimleri `PremiumFeature` olarak listeli; şimdilik hepsi açık. Telefonun Hello'su yalnızca açık
  olanları sunar, PC kapalı olanı gizler.

## 8. Gönderim adımları

1. Kuruluş hesabıyla (Milvasoft) uygulamayı oluştur.
2. Yükleme anahtarını oluştur (§3), `./gradlew :app:bundleRelease`.
3. Play Console: uygulama oluştur (ücretsiz, uygulama), Play App Signing'i kabul et, AAB'yi yükle.
4. Mağaza sayfası: §1 metinleri, §2 grafikler.
5. App content: gizlilik politikası, reklam yok, Data safety (§5), hedef kitle, içerik derecelendirmesi, erişilebilirlik ve
   ön plan servisi beyanları (§4.7), fotoğraf beyanı (§4.3), SMS İzin Beyan Formu (§4.7).
6. İncelemeye gönder.
