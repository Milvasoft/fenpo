# Fenpo for iPhone: App Store başvurusu

Buğra'nın gözden geçirip App Store Connect'e gireceği taslaklar. Uygulama tarafında gereken her şey `ios` dalında
hazır; buradaki metinler önerdir, değiştirmekte serbestsin. Apple markaları ("AirDrop", "Handoff", "Continuity",
"AirPlay") hiçbir metinde özellik adı olarak geçmiyor (CLAUDE.md, App Review 5.2.5).

## Gönderimden önce karar verilecekler

| Konu | Öneri |
|---|---|
| Sürüm | `MARKETING_VERSION` = `1.0.0` (`ios/Config/Base.xcconfig`), App Store Connect'teki sürümle aynı. |
| Yapı | En son TestFlight yapısı (şu an 72'den sonrakiler). App Store Connect'te sürüme "Build" olarak seçilir. |
| Platformlar | Sadece iPhone (`TARGETED_DEVICE_FAMILY = 1`), iPad ekran görüntüsü gerekmez. App Store Connect › Fiyat ve Kullanılabilirlik'te **Apple silikonlu Mac'lerde kullanılabilir** ve **Apple Vision Pro** seçeneklerini **kapat**: AccessorySetupKit ve arka plan Bluetooth'u oralarda çalışmaz. |
| Demo videosu | İnceleyicinin Windows PC'si yok. 2–3 dakikalık bir video gerek (aşağıda "İnceleme notları"); YouTube "liste dışı" ya da Drive bağlantısı yeterli. |
| Gizlilik politikası | `docs/PRIVACY.md` (Windows oturumu yazdı), iOS düzeltmeleri dahil (e3acec0). |

## Kimlik bilgileri

| Alan | Değer |
|---|---|
| Ad | Fenpo Link (App Store Connect kaydındaki ad; cihazda görünen ad Fenpo) |
| Paket kimliği | com.fenpo.app |
| SKU | fenpo-ios |
| Birincil dil | Türkçe (İngilizce de eklenecek) |
| Birincil kategori | Utilities (Araçlar) |
| İkincil kategori | Productivity (Verimlilik) |
| Telif | 2026 Milvasoft |
| Destek URL'si | https://github.com/Milvasoft/fenpo/issues |
| Pazarlama URL'si | https://github.com/Milvasoft/fenpo |
| Gizlilik politikası URL'si | https://fenpo-website.vercel.app/privacy (sitedeki sayfa, kaynağı `docs/PRIVACY.md`; GitHub bağlantısı 1.0 incelemesi için çalışmaya devam ediyor) |
| Fiyat | Ücretsiz |

"Fenpo" adı mağazada alınmışsa yedek: "Fenpo: Telefon ve PC" / "Fenpo: Phone and PC".

## Metinler

Yapıştırmaya hazır hali ve sınır denetimi `design/store/metadata/` içinde (`design/store/CHECK.md`). Anahtar
kelimeler ad ve alt başlıktaki kelimeleri (Fenpo, Link, iPhone, Windows, PC) tekrar etmez; her biri en az üç harf. Mağaza
metinlerinde başka bir uygulama mağazasının adı geçmez.

### Türkçe

**Alt başlık** (30 karakter sınırı, 30): `iPhone ve Windows PC bir arada`

**Tanıtım metni** (170 sınırı):
> Panon, dosyaların, kameran ve bildirimlerin iPhone ile Windows bilgisayarın arasında kendiliğinden dolaşsın. Sunucu yok, hesap yok; her şey kendi cihazlarında kalır.

**Açıklama:**
> Fenpo, iPhone'unla Windows bilgisayarını tek bir ekip gibi çalıştırır. Bilgisayarındaki Fenpo ile bir kez QR kodla eşleştir; sonrası kendiliğinden.
>
> PANO
> • iPhone'da kopyaladığını PC'de yapıştır, PC'de kopyaladığını iPhone'da kullan.
> • Denetim Merkezi düğmeleri, kilit ekranı widget'ı ve Kestirmeler ile tek dokunuş.
>
> DOSYA VE FOTOĞRAF
> • Fotoğraf, video ve belgeleri Wi‑Fi üzerinden hızla gönder; PC'den gelenler Dosyalar uygulamasında.
> • Büyük aktarımları kilit ekranında ve Dinamik Ada'da izle.
> • Bilgisayarın paylaştığın klasörleri Dosyalar uygulamasında aç, kaydet, düzenle.
> • Bilgisayardan iPhone'unun fotoğraf arşivine göz at.
>
> KAMERA VE EKRAN
> • iPhone'unu bilgisayarın web kamerası yap: lens seçimi ve yakınlaştırma.
> • iPhone'unun ekranını bilgisayarında göster.
>
> BİLGİSAYARI KUMANDA ET
> • Dokunmatik yüzey ve klavye olarak kullan.
> • Sunum kumandası: ileri, geri, siyah ekran ve lazer işaretçi.
> • Bilgisayarda çalan müziği iPhone'dan yönet.
> • Bilgisayarı kilitle, iPhone'unu bilgisayardan çaldırıp bul.
>
> BİLDİRİM VE ODAK
> • Bilgisayarın seçtiğin uygulamalarının bildirimleri iPhone'una gelir.
> • iPhone'da bir odak açıkken bilgisayardaki Fenpo bildirimleri susar.
> • Bilgisayar, kaydettiğin Kişisel Erişim Noktası'na tek tıkla katılır.
>
> GİZLİLİK
> Fenpo'nun sunucusu ve hesabı yok. Veriler yalnızca iPhone'unla eşleştirdiğin bilgisayar arasında, Bluetooth ve yerel ağ üzerinden şifreli olarak gider. Hiçbir veri toplanmaz.
>
> Gereken: Windows 11 (22H2 ya da sonrası) çalıştıran, Bluetooth'lu bir bilgisayarda Fenpo for Windows. Fenpo açık kaynaklıdır.

**Anahtar kelimeler** (100 bayt sınırı, Türkçe harfler iki bayt; 100):
`bilgisayar,pano,dosya aktarımı,web kamerası,kumanda,sunum,bildirim,ekran,yansıtma,eşitle,klavye`

**Bu sürümdeki yenilikler:**
> Fenpo'nun ilk sürümü.

### English

**Subtitle** (30, 26): `Your iPhone and Windows PC`

**Promotional text** (170):
> Clipboard, files, camera and notifications move between your iPhone and your Windows PC by themselves. No server, no account: everything stays on your own devices.

**Description:**
> Fenpo makes your iPhone and your Windows PC work as one. Pair once with the QR code shown by Fenpo on your PC; everything after that just works.
>
> CLIPBOARD
> • Copy on your iPhone, paste on your PC, and the other way round.
> • One tap from Control Center, the Lock Screen widget or Shortcuts.
>
> FILES AND PHOTOS
> • Send photos, videos and documents quickly over Wi‑Fi; files from your PC land in the Files app.
> • Follow large transfers on the Lock Screen and in the Dynamic Island.
> • Open, save and edit your PC's shared folders right in the Files app.
> • Browse your iPhone's photo library from your PC.
>
> CAMERA AND SCREEN
> • Use your iPhone as your PC's webcam, with lens choice and zoom.
> • Show your iPhone's screen on your PC.
>
> CONTROL YOUR PC
> • Use your iPhone as a touchpad and keyboard.
> • Presentation remote: next, previous, black screen and a laser pointer.
> • Control the music playing on your PC.
> • Lock your PC, or ring your iPhone from the PC to find it.
>
> NOTIFICATIONS AND FOCUS
> • Get notifications from the PC apps you pick on your iPhone.
> • While a Focus is on, Fenpo's notifications on your PC stay quiet.
> • Your PC joins your saved Personal Hotspot with one click.
>
> PRIVACY
> Fenpo has no servers and no accounts. Data moves only between your iPhone and the PC you paired, encrypted, over Bluetooth and your local network. Nothing is collected.
>
> Requires Fenpo for Windows on a Windows 11 (22H2 or later) PC with Bluetooth. Fenpo is open source.

**Keywords** (100 bytes, 98):
`clipboard,file transfer,webcam,remote,presenter,notifications,screen,mirror,sync,touchpad,keyboard`

**What's new:**
> The first release of Fenpo.

## Ekran görüntüleri, önizleme ve yaratıcı varlıklar

Hepsi `design/store/` altında koddan üretilir; neyin nereye yüklendiği `design/store/README.md`'de, Apple'ın ölçüleri
`design/store/SPECS.md`'de. Zorunlu set **6.3 inç** (1206 × 2622, Dinamik Ada orta ekran); 6.9 inç (1320 × 2868)
isteğe bağlı, ikisi de üretiliyor. CI: Actions › iOS › Run workflow › **Only build the App Store assets** işaretli;
çıktı `app-store-assets` artifact'i. İçerik tamamen uydurma: PC adı "Ofis PC" / "Office PC", dosya, şarkı ve sanatçı
adları hayali, başka uygulama markası yok.

**Kendi telefonunla çekersen:** 6.3 inç (iPhone 15/16/17, 14 Pro–17 Pro) ya da 6.9 inç bir iPhone gerekir. Görüntülerde olmaması gerekenler: gerçek PC adı (ör. "BUGRAKOSEN", Windows'ta Fenpo › Ayarlar'dan geçici olarak
"Ofis PC" yap), gerçek kişi adları, telefon numaraları, mesaj ve bildirim içerikleri, kendi fotoğrafların, pano
içerikleri, gerçek şarkı/sanatçı adları ve diğer uygulamaların logoları. Durum çubuğunda saat 9:41, tam pil iyi durur.

## Uygulama gizliliği (App Privacy)

- **Veri toplama:** "Hayır, bu uygulamadan veri toplamıyoruz" (Data Not Collected).
- Gerekçe: Fenpo'nun sunucusu, hesabı, analitiği, reklamı ve üçüncü taraf SDK'sı yok. Apple'ın tanımında "toplama",
  verinin uygulamadan geliştiriciye ya da üçüncü tarafa gitmesi; Fenpo'da veri yalnızca kullanıcının kendi iPhone'u ile
  kendi bilgisayarı arasında, uçtan uca şifreli gider.
- Gizlilik manifesti (`ios/Shared/PrivacyInfo.xcprivacy`) bunu söylüyor: izleme yok, toplanan veri türü yok; kullanılan
  "gerekçe isteyen API"ler UserDefaults (CA92.1, 1C8F.1) ve dosya zaman damgası (C617.1).
- Çökme raporları Apple'ın kendi tanılama verisidir (kullanıcı izin verdiyse); "toplanan veri" sayılmaz.

### `docs/PRIVACY.md` için iOS düzeltmeleri

Yapıldı: hepsi `main`'de (e3acec0). Aşağıdaki liste kayıt için duruyor.

1. **Mikrofon:** iPhone uygulaması mikrofon izni istemiyor (Fenpo Kamera sessiz). "İzinler" listesinde mikrofonu
   "Android'de" kısmına almak ya da "(Android)" eklemek doğru olur.
2. **Kişisel Erişim Noktası:** Kullanıcının girdiği erişim noktası adı ve parolası iPhone'un anahtar zincirinde durur
   ve şifreli kanalla yalnızca eşleşmiş PC'ye gider. "Cihazlarının arasında kalanlar" listesine "Kişisel Erişim
   Noktası'nın adı ve parolası (girersen)" eklenmeli; parola olduğu için açıkça yazmak iyi olur.
3. **Odak durumu:** Odak eşitleme açıksa iPhone'da odağın açık olduğu bilgisi PC'ye gider; listeye "odak durumu"
   eklenebilir.
4. **Silme:** "Fenpo'yu kaldırdığında ya da eşleşmeyi sildiğinde silinir" iOS için tam doğru değil. iPhone'a gelen
   dosyalar Dosyalar › Fenpo'da, eşleşmeyi silince değil uygulamayı silince gider. iOS anahtar zinciri öğelerini
   uygulama silinince tutar; Fenpo yeniden kurulduğu ilk açılışta eskilerini siler (`InstallRecord`). Öneri: "Eşleşme
   anahtarları eşleşmeyi silince silinir; gelen dosyalar ve ayarlar Fenpo'yu kaldırınca silinir."
5. **İnternet:** iPhone uygulaması interneti hiç kullanmıyor (yalnızca yerel ağ ve Bluetooth). "İnternet kullanımı"
   bölümüne "Fenpo for iPhone interneti kullanmaz" eklenebilir.

## İnceleme notları (App Review Information)

**Oturum açma:** Gerekmez (hesap yok). "Sign-in required" kapalı.

**İletişim:** Buğra'nın adı, e-postası, telefonu.

**Notlar (İngilizce, kutuya yapıştırılacak):**

> Fenpo connects an iPhone to the user's own Windows PC. It needs Fenpo for Windows running on a PC with Bluetooth (free: Microsoft Store "Fenpo" or https://github.com/Milvasoft/fenpo/releases). There is no server and no account; all data moves directly between the two devices, encrypted (TLS 1.3 with pinned certificates over the local network, AES-GCM over Bluetooth LE).
>
> Demo video of every feature with a Windows PC: <VIDEO LINK>
>
> Without a PC, the app opens on "Add your computer"; pairing starts by scanning the QR code shown by Fenpo for Windows.
>
> How the app uses permissions and background modes:
> • Bluetooth (AccessorySetupKit + bluetooth-central background mode): the PC is added as an accessory with AccessorySetupKit; Fenpo keeps a BLE connection to it so the clipboard, the PC's notifications, media controls and "ring my iPhone" work while Fenpo is in the background. The PC's BLE notification also wakes Fenpo briefly to open a local network connection when the PC sends a file.
> • Local network: files, photos, the PC's shared folders (File Provider extension), the webcam and screen streams go over TLS directly to the paired PC on the same Wi‑Fi.
> • Camera: scanning the pairing QR code, and the optional webcam mode, which only runs while Fenpo is open and the PC asks for it.
> • Photos: the PC's photo browser shows the user's library and the user can turn on sending new screenshots. Only after the user grants access.
> • Screen broadcast (ReplayKit broadcast upload extension): started only by the user, from Fenpo or Control Center, to show the iPhone screen on the PC.
> • Notifications: local notifications for files received, the PC's notifications the user chose, and requests from the PC (camera, hotspot, ring).
> • Focus filter: an optional Fenpo Focus filter tells the PC that a Focus is on so it silences Fenpo notifications.
> • Personal Hotspot: the user may type the hotspot name and password into Fenpo so the PC can join; they stay in the iPhone's keychain and go only to the paired PC.

Video için çekilecekler (yaklaşık 3 dk): QR ile eşleşme ve aksesuar seçici; pano iki yöne; fotoğraf gönderme ve PC'den
dosya alma (Dosyalar uygulaması); Dosyalar'da PC klasörleri; web kamerası; ekran yayını; dokunmatik yüzey ve sunum
kumandası; telefonu çaldırma; PC bildirimi gelmesi.

## Şifreleme uyumluluğu (Export Compliance)

- `Fenpo/Info.plist` → `ITSAppUsesNonExemptEncryption = NO` zaten ayarlı; App Store Connect her yapıda soru sormaz.
- Gerekçe: Fenpo yalnızca iOS'un kendi şifrelemesini kullanır (Network.framework TLS 1.3, CryptoKit AES-GCM ve
  P-256), standart algoritmalarla, kimlik doğrulama ve kullanıcının kendi cihazları arasındaki verinin korunması için;
  kendine özgü bir algoritma yok. Apple'ın rehberine göre işletim sisteminin şifrelemesini kullanan uygulamalar
  dışa aktarma belgesinden muaf.
- App Store Connect yine de sorarsa: "Uygulama şifreleme kullanıyor mu?" → **Evet**; "Hangi tür?" → **Apple'ın
  işletim sistemindeki şifrelemeyi kullanan / standart algoritmalar** seçeneklerinden "Yukarıdakilerin hiçbiri"
  (proprietary değil, OS dışı standart algoritma eklemiyor); Fransa dağıtımı sorusu → Hayır. Bu bir hukuki beyandır;
  son karar senin.

## Yaş derecelendirmesi

Bütün sorulara **Hiç / Yok / Hayır**: şiddet, cinsellik, küfür, kumar, korku, ilaç/alkol, tıbbi bilgi yok;
sınırsız web erişimi yok (uygulama içinde tarayıcı yok); kullanıcıların birbirine içerik gönderdiği sosyal özellik
yok (veriler yalnızca kendi cihazların arasında); konum paylaşımı yok; reklam yok; uygulama içi satın alma yok
(şimdilik). Sonuç: **4+**.

## İleride ücretli özellikler

Fenpo şimdi ücretsiz. İleride bazı özellikler ücretli olursa:

- **Ücretsizken indirenleri tanımak:** StoreKit 2'nin `AppTransaction.shared` sonucu `originalAppVersion` (ilk
  indirilen sürümün **yapı numarası**, CFBundleVersion) ve `originalPurchaseDate` verir. Bunu kullanmanın önünde
  engel yok: uygulama App Store'dan, aynı paket kimliğiyle dağıtılıyor. Yerelde de `InstallRecord` ilk açılışın
  sürümünü ve tarihini App Group'a yazar (bu sürümden itibaren). Not: `originalAppVersion` yapı numarası olduğu için
  "ücretsiz dönem" sınırını yapı numarasıyla belirlemek gerekir (CI'da `github.run_number`).
- **Tek kapı:** Her özelliğin girişi `FeatureAccess.isAvailable(_:)` (FenpoCore) üzerinden geçiyor; şu an hepsi
  `true`. Ücretlendirmede yalnızca bu fonksiyon StoreKit hak edişlerine ve yukarıdaki kontrole bağlanır.
- **App Store Connect:** Ücretli bir şey eklemeden önce Anlaşmalar, Vergi ve Bankacılık'ta **Paid Applications
  Agreement** imzalanmalı; uygulama içi satın almalar oluşturulup incelemeye ilk sürümle birlikte gönderilmeli.
